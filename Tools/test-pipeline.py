"""Adversarial regressions for workflow trust and publication receipts."""
import copy
import hashlib
import importlib.util
import io
import json
import subprocess
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]


def tool(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "Tools" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


policy = tool("check-pipeline")
runner = tool("run-validation")
candidate = tool("build-release-candidate")
release = tool("verify-release-candidate")
lint = tool("run-actionlint")
gitflow = tool("check-gitflow")
SHA = "a" * 40


def receipt():
    return {"schema": 1, "commit": SHA, "scope": "host-only", "passed": True,
            "python": "3.12.14",
            "source_clean": True,
            "expected_checks": len(runner.plan()),
            "checks": [{"command": cmd[1:], "exit_code": 0, "seconds": 1} for cmd in runner.plan()]}


class WorkflowTrustTests(unittest.TestCase):
    def workflow(self, name="validate.yml"):
        return policy.load_workflow((ROOT / ".github/workflows" / name).read_text())

    def test_current_inventory_passes(self):
        policy.check_all()

    def test_duplicate_keys_and_yaml_boolean_ambiguity(self):
        self.assertIn("on", policy.load_workflow("on:\n  pull_request:\n"))
        for text in ("permissions: {}\npermissions: write-all\n", "jobs:\n  test:\n    steps: []\n    steps: []\n"):
            with self.subTest(text=text), self.assertRaises(ValueError):
                policy.load_workflow(text)

    def test_untrusted_triggers_and_privilege_escalation_fail(self):
        for trigger in ("pull_request_target", "workflow_run", "issue_comment", "schedule"):
            data = self.workflow()
            data["on"][trigger] = None
            with self.subTest(trigger=trigger), self.assertRaises(ValueError):
                policy.check("validate.yml", data)
        data = self.workflow()
        data["jobs"]["schema"]["permissions"] = {"contents": "write"}
        with self.assertRaises(ValueError):
            policy.check("validate.yml", data)

    def test_pr_cannot_receive_secrets_or_persist_checkout_credentials(self):
        mutations = (lambda j: j["steps"][0]["with"].update({"persist-credentials": True}),
                     lambda j: j.update({"env": {"KEY": "${{ secrets.PRIVATE_KEY }}"}}),
                     lambda j: j.update({"env": {"KEY": "${{ secrets['PRIVATE_KEY'] }}"}}),
                     lambda j: j["steps"][0].update({"uses": "actions/checkout@v4"}),
                     lambda j: j["steps"].append({"run": "echo '${{ github.event.pull_request.title }}'"}))
        for mutation in mutations:
            data = self.workflow()
            mutation(data["jobs"]["schema"])
            with self.assertRaises(ValueError):
                policy.check("validate.yml", data)

    def test_resource_changes_and_ignored_failures_fail(self):
        for change in ({"runs-on": "self-hosted"}, {"runs-on": "ubuntu-8-core"},
                       {"timeout-minutes": 0}, {"timeout-minutes": 360},
                       {"continue-on-error": True}):
            data = self.workflow()
            data["jobs"]["schema"].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("validate.yml", data)
        data = self.workflow()
        data["jobs"]["extra"] = copy.deepcopy(data["jobs"]["schema"])
        with self.assertRaises(ValueError):
            policy.check("validate.yml", data)

    def test_missing_or_conditional_full_validation_fails(self):
        for name, job in (("validate.yml", "schema"), ("release-candidate.yml", "candidate"), ("release-vpm.yml", "release")):
            data = self.workflow(name)
            step = next(s for s in data["jobs"][job]["steps"] if s.get("run", "").startswith("python3 Tools/run-validation.py"))
            step["if"] = "false"
            with self.subTest(name=name), self.assertRaises(ValueError):
                policy.check(name, data)

    def test_branch_validation_and_staging_events_cannot_be_broadened_or_skipped(self):
        for branches in (["main"], ["develop", "main"], ["*"]):
            data = self.workflow()
            data["on"]["push"]["branches"] = branches
            with self.subTest(branches=branches), self.assertRaises(ValueError):
                policy.check("validate.yml", data)
        for change in ({"branches": ["develop"]}, {"types": ["opened", "synchronize", "reopened"]}):
            data = self.workflow()
            data["on"]["pull_request"] = change
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("validate.yml", data)
        for change in ({"if": "false"}, {"if": "github.base_ref == 'main'"}, {"continue-on-error": True},
                       {"env": {"GITHUB_EVENT_PATH": "fake.json"}}):
            data = self.workflow()
            guard = next(step for step in data["jobs"]["schema"]["steps"] if step.get("name") == "Check Gitflow pull request target")
            guard.update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("validate.yml", data)
        data = self.workflow("release-candidate.yml")
        data["on"]["push"]["branches"] = ["develop"]
        with self.assertRaises(ValueError):
            policy.check("release-candidate.yml", data)
        for name in ("release-vpm.yml", "build-listing.yml"):
            data = self.workflow(name)
            data["on"]["push"] = {"branches": ["main"]}
            with self.subTest(name=name), self.assertRaises(ValueError):
                policy.check(name, data)

    def test_staging_candidate_cannot_substitute_an_untrusted_commit_source(self):
        for name in ("Require exact main source", "Build and inspect the candidate"):
            for source in ("${{ inputs.expected_commit }}", "${{ github.sha }}",
                           "${{ github.event.pull_request.head.sha }}", "${{ github.event.after }}"):
                data = self.workflow("release-candidate.yml")
                step = next(step for step in data["jobs"]["candidate"]["steps"] if step.get("name") == name)
                step["env"]["EXPECTED_COMMIT"] = source
                with self.subTest(name=name, source=source), self.assertRaises(ValueError):
                    policy.check("release-candidate.yml", data)
        data = self.workflow("release-candidate.yml")
        source = next(step for step in data["jobs"]["candidate"]["steps"] if step.get("name") == "Require exact main source")
        source["if"] = "github.event_name == 'workflow_dispatch'"
        with self.assertRaises(ValueError):
            policy.check("release-candidate.yml", data)

    def test_main_publication_gates_cannot_be_bypassed(self):
        for name, job in (("release-vpm.yml", "release"), ("release-candidate.yml", "candidate"), ("build-listing.yml", "build-listing")):
            data = self.workflow(name)
            data["jobs"][job]["if"] = "true || " + data["jobs"][job]["if"]
            with self.subTest(name=name), self.assertRaises(ValueError):
                policy.check(name, data)

    def test_artifact_retention_and_upload_scope_fail_closed(self):
        for change in ({"retention-days": 90}, {"path": "."}, {"path": "work"}):
            data = self.workflow("release-candidate.yml")
            data["jobs"]["candidate"]["steps"][-1]["with"].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("release-candidate.yml", data)

    def test_publication_cannot_skip_the_candidate_receipt_gate(self):
        data = self.workflow("release-vpm.yml")
        step = next(s for s in data["jobs"]["release"]["steps"] if "Tools/verify-release-candidate.py" in s.get("run", ""))
        step["if"] = "false"
        with self.assertRaises(ValueError):
            policy.check("release-vpm.yml", data)

    def test_approved_manual_runs_cannot_replace_pending_runs(self):
        for name in ("release-vpm.yml", "release-candidate.yml"):
            data = self.workflow(name)
            data["concurrency"].pop("queue")
            with self.subTest(name=name), self.assertRaises(ValueError):
                policy.check(name, data)
        data = self.workflow("build-listing.yml")
        data["jobs"]["build-listing"]["concurrency"].pop("queue")
        with self.assertRaises(ValueError):
            policy.check("build-listing.yml", data)

    def test_mirror_credentials_cannot_escape_the_four_publication_steps(self):
        approved = {"id": "mirrors", "run": policy.PUBLICATION_RUNS["mirrors"],
                    "env": dict(policy.MIRROR_ENV)}
        data = self.workflow()
        data["jobs"]["schema"]["steps"].append(approved)
        with self.assertRaises(ValueError):
            policy.check("validate.yml", data)
        for mutation in (
                lambda w: w["env"].update(policy.MIRROR_ENV),
                lambda w: w["jobs"]["build-listing"].update({"env": dict(policy.MIRROR_ENV)}),
                lambda w: w["jobs"]["build-listing"]["steps"][0].update({"env": dict(policy.MIRROR_ENV)}),
                lambda w: w["jobs"]["check-listing-builder"]["steps"].append(copy.deepcopy(approved)),
                lambda w: w["jobs"]["build-listing"]["steps"][0].update({"env": {"KEY": "${{ secrets[format('VPM_{0}_TOKEN', 'MIRROR')] }}"}}),
                lambda w: w["jobs"]["build-listing"]["steps"].append({"run": "echo token", "env": {"KEY": "${{ secrets['VPM_MIRROR_TOKEN'] }}"}})):
            data = self.workflow("build-listing.yml")
            mutation(data)
            with self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)
        data = self.workflow("release-vpm.yml")
        data["jobs"]["release"]["steps"].append(approved)
        with self.assertRaises(ValueError):
            policy.check("release-vpm.yml", data)

    def test_mirror_extension_preserves_listing_events_required_gate_and_concurrency(self):
        mutations = (
            lambda w: w["on"].update({"schedule": []}),
            lambda w: w["on"]["release"].update({"types": ["created"]}),
            lambda w: w["on"]["pull_request"].update({"types": ["opened", "synchronize", "reopened"]}),
            lambda w: w["on"]["pull_request"].update({"branches": ["develop"]}),
            lambda w: w["concurrency"].update({"cancel-in-progress": True}),
            lambda w: w["jobs"]["changes"].update({"if": "false"}),
            lambda w: w["jobs"]["check-listing-builder"].update({"if": "false"}),
            lambda w: w["jobs"]["listing-validation"].update({"if": "false"}),
            lambda w: w["jobs"]["listing-validation"].update({"needs": ["changes"]}),
        )
        for mutation in mutations:
            data = self.workflow("build-listing.yml")
            mutation(data)
            with self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)

    def test_publication_commands_cannot_be_skipped_overridden_or_redirected(self):
        changes = ({"if": "always()"}, {"if": False}, {"continue-on-error": True},
                   {"shell": "bash --noprofile --norc {0}"}, {"working-directory": "untrusted"})
        for identity in policy.PUBLICATION_RUNS:
            for change in changes:
                data = self.workflow("build-listing.yml")
                step = next(s for s in data["jobs"]["build-listing"]["steps"] if s.get("id") == identity)
                step.update(change)
                with self.subTest(identity=identity, change=change), self.assertRaises(ValueError):
                    policy.check("build-listing.yml", data)
            for suffix in (' --repository attacker/fork', ' --publication-root unreviewed', ' &', ' || true'):
                data = self.workflow("build-listing.yml")
                step = next(s for s in data["jobs"]["build-listing"]["steps"] if s.get("id") == identity)
                step["run"] += suffix
                with self.subTest(identity=identity, suffix=suffix), self.assertRaises(ValueError):
                    policy.check("build-listing.yml", data)

    def test_mirror_requires_one_preflight_preparation_and_unchanged_deployment(self):
        for identity in policy.PUBLICATION_RUNS:
            for duplicate in (False, True):
                data = self.workflow("build-listing.yml")
                steps = data["jobs"]["build-listing"]["steps"]
                step = next(s for s in steps if s.get("id") == identity)
                if duplicate:
                    steps.append(copy.deepcopy(step))
                else:
                    steps.remove(step)
                with self.subTest(identity=identity, duplicate=duplicate), self.assertRaises(ValueError):
                    policy.check("build-listing.yml", data)
        for change in ({"if": "always()"}, {"if": False}, {"continue-on-error": True}, {"id": "other-deployment"}):
            data = self.workflow("build-listing.yml")
            step = next(s for s in data["jobs"]["build-listing"]["steps"] if s.get("id") == "deployment")
            step.update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)

    def test_mirrors_run_only_after_successful_deployment_of_the_prepared_listing(self):
        for identity, target in (("mirrors", "deployment"), ("mirror-deploy-guard", "prepare-publication"), ("prepare-publication", "mirror-preflight"),
                                 ("mirror-preflight", "mirrors")):
            data = self.workflow("build-listing.yml")
            steps = data["jobs"]["build-listing"]["steps"]
            step = next(s for s in steps if s.get("id") == identity)
            steps.remove(step)
            steps.insert(next(i for i, s in enumerate(steps) if s.get("id") == target), step)
            with self.subTest(identity=identity, target=target), self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)
        data = self.workflow("build-listing.yml")
        data["jobs"]["build-listing"]["steps"].append({"run": "echo another publication"})
        with self.assertRaises(ValueError):
            policy.check("build-listing.yml", data)
        data = self.workflow("build-listing.yml")
        step = next(s for s in data["jobs"]["build-listing"]["steps"] if "Tools/check-listing.py" in s.get("run", ""))
        step["if"] = False
        with self.assertRaises(ValueError):
            policy.check("build-listing.yml", data)

    def test_fresh_guard_cannot_run_after_deployment_or_leave_an_intervening_step(self):
        for mutation in ("after-deployment", "intervening-step"):
            data = self.workflow("build-listing.yml")
            steps = data["jobs"]["build-listing"]["steps"]
            guard = next(s for s in steps if s.get("id") == "mirror-deploy-guard")
            if mutation == "after-deployment":
                steps.remove(guard)
                deployment = next(i for i, s in enumerate(steps) if s.get("id") == "deployment")
                steps.insert(deployment + 1, guard)
            else:
                deployment = next(i for i, s in enumerate(steps) if s.get("id") == "deployment")
                steps.insert(deployment, {"run": "echo intervening-operation"})
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)

    def test_mirror_credential_and_approval_environment_are_fixed(self):
        for identity in policy.PUBLICATION_RUNS:
            for env in ({}, {"VPM_MIRROR_TOKEN": "${{ secrets.GITHUB_TOKEN }}"},
                        {"VPM_MIRROR_TOKEN": "${{ secrets['VPM_MIRROR_TOKEN'] }}"},
                        dict(policy.MIRROR_ENV, OTHER_TOKEN="${{ secrets.OTHER_TOKEN }}")):
                data = self.workflow("build-listing.yml")
                step = next(s for s in data["jobs"]["build-listing"]["steps"] if s.get("id") == identity)
                step["env"] = env
                with self.subTest(identity=identity, env=env), self.assertRaises(ValueError):
                    policy.check("build-listing.yml", data)
        for change in ({"environment": "other"}, {"defaults": {"run": {"working-directory": "other"}}},
                       {"env": {"PATH": "untrusted"}}):
            data = self.workflow("build-listing.yml")
            data["jobs"]["build-listing"].update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)

    def test_organization_credential_stays_out_of_preparation_and_untrusted_steps(self):
        for target in ("prepare-publication", "checkout", "pr-validation"):
            data = self.workflow("validate.yml" if target == "pr-validation" else "build-listing.yml")
            job = data["jobs"]["schema" if target == "pr-validation" else "build-listing"]
            step = next(s for s in job["steps"] if s.get("id") == target) if target == "prepare-publication" else job["steps"][0]
            step.setdefault("env", {})["VPM_ORG_MIRROR_TOKEN"] = "${{ secrets.VPM_ORG_MIRROR_TOKEN }}"
            with self.subTest(target=target), self.assertRaises(ValueError):
                policy.check("validate.yml" if target == "pr-validation" else "build-listing.yml", data)
        for identity in ("mirror-preflight", "mirror-deploy-guard", "mirrors"):
            data = self.workflow("build-listing.yml")
            step = next(s for s in data["jobs"]["build-listing"]["steps"] if s.get("id") == identity)
            step["env"].pop("VPM_ORG_MIRROR_TOKEN")
            with self.subTest(identity=identity), self.assertRaises(ValueError):
                policy.check("build-listing.yml", data)


class GitflowTests(unittest.TestCase):
    def event(self, base="main", head="release/1.2.3"):
        repository = {"id": 123, "full_name": "VR-Vlog/VR-Vlog-Avatar-Exporter"}
        return {"pull_request": {"base": {"ref": base, "repo": dict(repository)},
                                 "head": {"ref": head, "repo": dict(repository)}}}

    def test_release_hotfix_and_non_main_targets_pass(self):
        for base, head in (("main", "release/0.0.0"), ("main", "release/1.23.456"),
                           ("main", "hotfix/save-error"), ("main", "hotfix/1.2.3"),
                           ("develop", "feature/change"), ("develop", "main"),
                           ("release/1.2.3", "fix/release-error")):
            with self.subTest(base=base, head=head):
                gitflow.validate(self.event(base, head), "pull_request")
        event = self.event("develop", "feature/external")
        event["pull_request"]["head"]["repo"] = {"id": 456, "full_name": "contributor/fork"}
        gitflow.validate(event, "pull_request")

    def test_main_rejects_direct_development_and_invalid_release_or_hotfix_names(self):
        for head in ("develop", "feature/change", "release/1.2", "release/01.2.3", "release/1.02.3",
                     "release/1.2.03", "release/1.2.3-beta.1", "release/1.2.3/extra", "release/",
                     "hotfix/", "hotfix/nested/fix", "hotfix/.hidden", "hotfix/fix..name",
                     "hotfix/fix.lock", "hotfix/fix.", "hotfix/fix name"):
            with self.subTest(head=head), self.assertRaises(ValueError):
                gitflow.validate(self.event(head=head), "pull_request")

    def test_fork_and_incomplete_repository_identity_cannot_promote_to_main(self):
        for repository in ({"id": 456, "full_name": "contributor/fork"},
                           {"id": 456, "full_name": "VR-Vlog/VR-Vlog-Avatar-Exporter"},
                           {"id": 123, "full_name": "contributor/fork"},
                           {"id": True, "full_name": "VR-Vlog/VR-Vlog-Avatar-Exporter"},
                           {"id": "123", "full_name": "VR-Vlog/VR-Vlog-Avatar-Exporter"},
                           {"id": 0, "full_name": "VR-Vlog/VR-Vlog-Avatar-Exporter"},
                           {"id": 123, "full_name": ""}, {"id": 123, "full_name": " "}, {}, None):
            for side in ("head", "base"):
                event = self.event()
                event["pull_request"][side]["repo"] = repository
                with self.subTest(side=side, repository=repository), self.assertRaises(ValueError):
                    gitflow.validate(event, "pull_request")

    def test_missing_malformed_or_wrong_event_payload_fails_closed(self):
        for event_name in (None, "push", "pull_request_target", "workflow_dispatch"):
            with self.subTest(event_name=event_name), self.assertRaises(ValueError):
                gitflow.validate(self.event(), event_name)
        for event in (None, [], {}, {"pull_request": None}, {"pull_request": {}},
                      {"pull_request": {"base": "main", "head": "release/1.2.3"}}):
            with self.subTest(event=event), self.assertRaises(ValueError):
                gitflow.validate(event, "pull_request")
        for side in ("base", "head"):
            for ref in (None, "", 123, "bad..branch", "trailing/", "has space", ".hidden", "fix.lock"):
                event = self.event("develop", "feature/change")
                event["pull_request"][side]["ref"] = ref
                with self.subTest(side=side, ref=ref), self.assertRaises(ValueError):
                    gitflow.validate(event, "pull_request")

    def test_command_reads_the_event_file_instead_of_branch_environment_hints(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "event.json"
            environment = {"GITHUB_EVENT_PATH": str(path), "GITHUB_EVENT_NAME": "pull_request",
                           "GITHUB_BASE_REF": "develop", "GITHUB_HEAD_REF": "release/1.2.3"}
            path.write_text(json.dumps(self.event("main", "feature/unapproved")), encoding="utf-8")
            with mock.patch.dict(gitflow.os.environ, environment), self.assertRaises(ValueError):
                gitflow.main()
            path.write_text(json.dumps(self.event("develop", "main")), encoding="utf-8")
            environment["GITHUB_BASE_REF"] = "main"
            environment["GITHUB_HEAD_REF"] = "feature/unapproved"
            with mock.patch.dict(gitflow.os.environ, environment), mock.patch("sys.stdout", new=io.StringIO()):
                gitflow.main()


class ValidationReceiptTests(unittest.TestCase):
    def test_mirror_and_publication_regressions_join_every_complete_host_plan(self):
        existing = ("check-pipeline.py", "test-pipeline.py", "validate.py", "test-release-policy.py",
                    "test-listing.py", "check-public-content.py", "test-package.py",
                    "test-aao-vertex-buffer-patch.py", "generate-compatibility.py", "test-compatibility-tooling.py")
        added = {"test-vpm-mirrors.py", "test-exporter-publication.py",
                 "check-liltoon-structure.py", "test-liltoon-catalogue.py",
                 "test-liltoon-version-parity.py", "test-liltoon-version-parity-runner.py"}
        self.assertEqual(existing, tuple(name for name in runner.PYTHON_CHECKS if name not in added))
        for name in added:
            self.assertEqual(1, runner.PYTHON_CHECKS.count(name))
            self.assertIn(["test-python", "Tools/" + name], runner.plan("test-python", "test-pwsh"))

    def test_commit_changed_during_checks_cannot_receive_a_success_receipt(self):
        state = mock.Mock(side_effect=[(SHA, True), ("b" * 40, True)])
        execute = mock.Mock(return_value=subprocess.CompletedProcess([], 0))
        result = runner.validation_receipt([["python", "one"]], ROOT, execute, state)
        self.assertFalse(result["passed"])
        self.assertEqual(SHA, result["commit"])

    def test_first_failure_or_missing_executable_stops_the_plan(self):
        execute = mock.Mock(side_effect=[subprocess.CompletedProcess([], 1)])
        results = runner.run_checks([["python", "one"], ["python", "two"]], ROOT, execute)
        self.assertEqual(1, len(results))
        self.assertEqual(1, execute.call_count)
        for failure in (OSError(), subprocess.TimeoutExpired("test", 600)):
            execute = mock.Mock(side_effect=failure)
            self.assertEqual(1, runner.run_checks([["python", "one"]], ROOT, execute)[0]["exit_code"])

    def test_all_transfer_modes_remain_in_the_full_plan(self):
        modes = [cmd[-1] for cmd in runner.plan() if "-CloudAvailabilityMode" in cmd]
        self.assertEqual(["Default", "EditorOnly", "DevelopmentOnly", "DevelopmentEditor"], modes)

    def test_incomplete_failed_wrong_sha_and_fabricated_scope_fail(self):
        candidate.validate_report(receipt(), SHA)
        mutations = (lambda r: r.update({"commit": "b" * 40}),
                     lambda r: r.update({"source_clean": False}),
                     lambda r: r.update({"python": "3.13.1"}),
                     lambda r: r.update({"schema": True}),
                     lambda r: r.update({"scope": "unity"}),
                     lambda r: r.update({"passed": False}),
                     lambda r: r["checks"].pop(),
                     lambda r: r["checks"][0].update({"exit_code": 1}),
                     lambda r: r["checks"][0].update({"exit_code": False}),
                     lambda r: r["checks"].reverse())
        for mutate in mutations:
            report = receipt()
            mutate(report)
            with self.assertRaises(ValueError):
                candidate.validate_report(report, SHA)

    def test_zip_documentation_targets_are_checked_in_delivered_files(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "package.zip"
            for target in ("missing.md", "../outside.md", "%2e%2e/outside.md", "/outside.md"):
                with zipfile.ZipFile(path, "w") as archive:
                    archive.writestr("README.md", "[guide](" + target + ")")
                with self.subTest(target=target), self.assertRaises(ValueError):
                    candidate.check_zip_links(path)
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("docs/README.md", "[guide](../guide.md#usage) [external](https://example.org/guide)")
                archive.writestr("guide.md", "# Usage")
            candidate.check_zip_links(path)

    def test_linter_archive_must_match_pinned_hash(self):
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            lint.binary_from_archive(b"untrusted executable", "0" * 64)

    def test_only_policy_checked_queue_diagnostics_are_compatible(self):
        approved = lint.approved_queue_positions()
        self.assertEqual(3, len(approved))
        path, line, column = next(iter(approved))
        allowed = {"message": lint.QUEUE_MESSAGE, "kind": "syntax-check", "filepath": path,
                   "line": line, "column": column}
        self.assertEqual([], lint.blocking_diagnostics([allowed], approved))
        for change in ({"message": "other error"}, {"kind": "expression"}, {"line": line + 1},
                       {"column": column + 1}, {"filepath": "unapproved.yml"}):
            other = dict(allowed, **change)
            self.assertEqual([other], lint.blocking_diagnostics([allowed, other], approved))
        with self.assertRaises(ValueError):
            lint.blocking_diagnostics("not a diagnostic list", approved)

    def test_queue_compatibility_requires_valid_source_policy(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "Tools").mkdir()
            (root / "Tools/check-pipeline.py").write_text((ROOT / "Tools/check-pipeline.py").read_text())
            (root / ".github/workflows").mkdir(parents=True)
            for source in (ROOT / ".github/workflows").glob("*.yml"):
                (root / ".github/workflows" / source.name).write_text(source.read_text())
            path = root / ".github/workflows/release-vpm.yml"
            path.write_text(path.read_text().replace("queue: max", "queue: invalid"))
            with self.assertRaises(ValueError):
                lint.approved_queue_positions(root)

    def test_cost_model_is_bounded_and_distinguishes_billable_storage(self):
        cost = tool("estimate-pipeline-cost")
        result = cost.estimate(500, 8)
        self.assertEqual(0, result["incremental_runner_cost_usd"])
        self.assertEqual(37.8, result["average_added_artifact_mib"])
        self.assertAlmostEqual(0.009229, result["incremental_storage_cost_if_all_added_bytes_billable_usd"])
        for counts in ((-1, 2), (1, -2), (True, 2)):
            with self.assertRaises(ValueError):
                cost.estimate(*counts)


class PublicationReceiptTests(unittest.TestCase):
    def run_fixture(self):
        return {"repository": {"full_name": release.REPOSITORY, "id": release.REPOSITORY_GITHUB_ID}, "workflow_id": 123,
                "path": release.WORKFLOW, "head_sha": SHA, "head_branch": "main",
                "event": "workflow_dispatch", "status": "completed", "conclusion": "success"}

    def archive(self, metadata_changes=None, extra=None, report=None):
        package = "com.vrvlog.liltoon-vrm-exporter-1.2.3.zip"
        payload = b"immutable reviewed package"
        digest = hashlib.sha256(payload).hexdigest()
        meta = {"schema": 1, "repository": release.REPOSITORY, "commit": SHA, "run_id": 42,
                "version": "1.2.3", "package": package, "package_sha256": digest, "validation_scope": "host-only"}
        meta.update(metadata_changes or {})
        out = io.BytesIO()
        with zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            archive.writestr("candidate.json", json.dumps(meta))
            archive.writestr("validation.json", json.dumps(report if report is not None else receipt()))
            archive.writestr(package, payload)
            if extra:
                archive.writestr(extra, "untrusted extra")
        return out.getvalue(), digest

    def test_success_receipt_is_bound_to_run_commit_version_and_bytes(self):
        for event in ("workflow_dispatch", "push"):
            run = self.run_fixture()
            run["event"] = event
            release.verify_run(run, 123, SHA)
        data, digest = self.archive()
        release.verify_archive(data, SHA, "1.2.3", digest, 42)
        with self.assertRaises(ValueError):
            release.verify_archive(data, SHA, "1.2.3", "b" * 64, 42)

    def test_fork_pr_wrong_workflow_failed_and_in_progress_runs_fail(self):
        changes = ({"repository": {"full_name": "attacker/fork"}}, {"head_sha": "b" * 40},
                   {"workflow_id": 456}, {"path": ".github/workflows/fake.yml"},
                   {"event": "pull_request"}, {"event": "pull_request_target"}, {"event": "workflow_run"},
                   {"event": "release"}, {"event": None}, {"head_branch": "develop"}, {"head_branch": "feature"},
                   {"status": "in_progress"}, {"conclusion": "failure"}, {"conclusion": "cancelled"})
        for change in changes:
            run = self.run_fixture()
            run.update(change)
            with self.subTest(change=change), self.assertRaises(ValueError):
                release.verify_run(run, 123, SHA)

    def test_pre_transfer_receipt_preserves_exact_run_and_package_binding(self):
        data, digest = self.archive({"repository": release.LEGACY_REPOSITORY})
        release.verify_run(self.run_fixture(), 123, SHA)
        release.verify_archive(data, SHA, "1.2.3", digest, 42)
        with self.assertRaises(ValueError):
            release.verify_archive(data, SHA, "1.2.3", digest, 43)

    def test_pre_rename_receipt_retains_exact_run_and_package_binding(self):
        data, digest = self.archive({"repository": release.PRE_RENAME_REPOSITORY})
        release.verify_run(self.run_fixture(), 123, SHA)
        release.verify_archive(data, SHA, "1.2.3", digest, 42)
        with self.assertRaises(ValueError):
            release.verify_archive(data, "b" * 40, "1.2.3", digest, 42)

    def test_repository_name_alone_cannot_authenticate_a_candidate(self):
        for identity in (None, 1, True, str(release.REPOSITORY_GITHUB_ID)):
            run = self.run_fixture()
            run["repository"]["id"] = identity
            with self.subTest(identity=identity), self.assertRaises(ValueError):
                release.verify_run(run, 123, SHA)

    def test_duplicate_expired_and_oversize_artifacts_fail(self):
        row = {"name": "release-candidate-" + SHA, "expired": False, "size_in_bytes": 100}
        self.assertEqual(row, release.select_artifact([row], SHA))
        for rows in ([], [row, row], [dict(row, expired=True)], [dict(row, size_in_bytes=30 * 1024 * 1024)]):
            with self.assertRaises(ValueError):
                release.select_artifact(rows, SHA)

    def test_altered_metadata_inventory_and_incomplete_receipts_fail(self):
        for changes in ({"repository": "attacker/fork"}, {"run_id": 43}, {"commit": "b" * 40},
                        {"version": "1.2.4"}, {"validation_scope": "unity"}):
            data, digest = self.archive(changes)
            with self.subTest(changes=changes), self.assertRaises(ValueError):
                release.verify_archive(data, SHA, "1.2.3", digest, 42)
        for extra in ("../escape", "logs/private.txt", "candidate.json"):
            data, digest = self.archive(extra=extra)
            with self.assertRaises(ValueError):
                release.verify_archive(data, SHA, "1.2.3", digest, 42)
        data, digest = self.archive(report=dict(receipt(), passed=False))
        with self.assertRaises(ValueError):
            release.verify_archive(data, SHA, "1.2.3", digest, 42)

    def test_archive_decompression_budget_is_enforced(self):
        original, digest = self.archive()
        out = io.BytesIO()
        with zipfile.ZipFile(io.BytesIO(original)) as source, zipfile.ZipFile(out, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for name in source.namelist():
                archive.writestr(name, b"x" * (129 * 1024) if name == "candidate.json" else source.read(name))
        with self.assertRaises(ValueError):
            release.verify_archive(out.getvalue(), SHA, "1.2.3", digest, 42)


if __name__ == "__main__":
    unittest.main()
