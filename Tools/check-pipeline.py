"""Enforce this repository's workflow trust and resource policy.

This is a regression guard, not a proof that arbitrary workflow code is safe.
actionlint separately checks GitHub Actions syntax and expressions.
"""
import copy
import json
import re
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[1]
WORKFLOWS = {"validate.yml", "build-listing.yml", "release-vpm.yml",
             "release-candidate.yml", "check-upstream.yml"}
JOBS = {
    "validate.yml": {"schema"}, "release-candidate.yml": {"candidate"},
    "release-vpm.yml": {"release", "dispatch-listing"},
    "check-upstream.yml": {"inspect"},
    "build-listing.yml": {"changes", "check-listing-builder", "listing-validation", "build-listing"},
}
PAGES_GATE = "(github.event_name == 'workflow_dispatch' && github.ref == 'refs/heads/main') || (github.event_name == 'release' && github.event.release.draft == false)"
WRITE_JOBS = {
    ("release-vpm.yml", "release"): {"contents": "write", "actions": "read"},
    ("release-vpm.yml", "dispatch-listing"): {"contents": "read", "actions": "write"},
    ("build-listing.yml", "build-listing"): {"contents": "read", "pages": "write", "id-token": "write"},
}
MIRROR_ENV = {"VPM_MIRROR_TOKEN": "${{ secrets.VPM_MIRROR_TOKEN }}"}
PUBLICATION_RUNS = {
    "mirror-preflight": "python3 Tools/sync-vpm-mirrors.py --preflight",
    "prepare-publication": 'python3 Tools/prepare-exporter-publication.py --publication-root Website --official-root "$RUNNER_TEMP/official-exporter-publication"',
    "mirror-deploy-guard": 'python3 Tools/sync-vpm-mirrors.py --verify-before-deployment "$RUNNER_TEMP/official-exporter-publication"',
    "mirrors": 'python3 Tools/sync-vpm-mirrors.py --publication-root "$RUNNER_TEMP/official-exporter-publication"',
}
PUBLICATION_MARKERS = ("VPM_MIRROR_TOKEN", "sync-vpm-mirrors.py", "prepare-exporter-publication.py")
DEPLOYMENT = {"id": "deployment", "uses": "actions/deploy-pages@d6db90164ac5ed86f2b6aed7e0febac5b3c0c03e"}


class WorkflowLoader(yaml.SafeLoader):
    pass


# GitHub uses YAML 1.2 booleans: a workflow's "on" key must stay a string.
WorkflowLoader.yaml_implicit_resolvers = copy.deepcopy(yaml.SafeLoader.yaml_implicit_resolvers)
for key, resolvers in WorkflowLoader.yaml_implicit_resolvers.items():
    WorkflowLoader.yaml_implicit_resolvers[key] = [r for r in resolvers if r[0] != "tag:yaml.org,2002:bool"]
WorkflowLoader.add_implicit_resolver("tag:yaml.org,2002:bool", re.compile(r"^(?:true|false)$", re.I), list("tTfF"))


def mapping(loader, node):
    result = {}
    for key_node, value_node in node.value:
        key = loader.construct_object(key_node)
        if key in result:
            raise ValueError("Duplicate YAML key: " + str(key))
        result[key] = loader.construct_object(value_node)
    return result


WorkflowLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, mapping)


def load_workflow(text):
    value = yaml.load(text, Loader=WorkflowLoader)
    if not isinstance(value, dict):
        raise ValueError("Workflow must be a mapping")
    return value


def require(condition, message):
    if not condition:
        raise ValueError(message)


def contains_secret_reference(value):
    return re.search(r"\bsecrets\s*[.\[]", json.dumps(value)) is not None


def check_publication_step(name, job_name, step):
    """Only the reviewed publication commands may handle the mirror credential."""
    identity = step.get("id")
    text = json.dumps(step)
    if identity not in PUBLICATION_RUNS and not any(marker in text for marker in PUBLICATION_MARKERS):
        return
    require((name, job_name) == ("build-listing.yml", "build-listing"),
            "Mirror preparation and credentials belong only to the Pages publication job")
    require(identity in PUBLICATION_RUNS and step.get("run") == PUBLICATION_RUNS[identity],
            "Unreviewed mirror command or destination override")
    allowed = {"id", "name", "run", "env"}
    require(step.get("env") == MIRROR_ENV, "Mirror credential must use only the dedicated step environment")
    require(set(step) <= allowed, "Publication steps must use default success, shell and working directory")


def check_step_secrets(name, job_name, step):
    if not contains_secret_reference(step):
        return
    require((name, job_name) == ("build-listing.yml", "build-listing"),
            "Only the existing Pages publication job may reference secrets")
    if step.get("id") in PUBLICATION_RUNS:
        require(step.get("env") == MIRROR_ENV, "Mirror credential environment is not approved")
    else:
        require(step.get("name") == "Generate VPM listing"
                and step.get("env") == {"GITHUB_TOKEN": "${{ secrets.GITHUB_TOKEN }}"},
                "Unreviewed secret reference outside the mirror steps or existing listing builder")
    require(not contains_secret_reference({key: value for key, value in step.items() if key != "env"}),
            "Secrets must enter only the approved step environment")


def check_mirror_publication(workflow, job):
    require(workflow.get("env") == {"listPublishDirectory": "Website", "pathToCi": "ci"},
            "Preserve the fixed publication paths and command environment")
    require(not workflow.get("defaults") and not job.get("defaults") and not job.get("env"),
            "Publication commands must not inherit shell, directory or credential overrides")
    require(job.get("environment") == {"name": "github-pages", "url": "${{ steps.deployment.outputs.page_url }}"},
            "Preserve the existing Pages environment and approval boundary")
    steps = job.get("steps", [])
    positions = {}
    for identity in PUBLICATION_RUNS:
        matches = [i for i, step in enumerate(steps) if step.get("id") == identity]
        require(len(matches) == 1, "Exactly one reviewed " + identity + " step is required")
        positions[identity] = matches[0]
    deployments = [i for i, step in enumerate(steps) if str(step.get("uses", "")).startswith("actions/deploy-pages@")]
    require(len(deployments) == 1 and steps[deployments[0]] == DEPLOYMENT,
            "Preserve the sole successful canonical Pages deployment")
    uploads = [i for i, step in enumerate(steps) if str(step.get("uses", "")).startswith("actions/upload-pages-artifact@")]
    require(len(uploads) == 1, "Exactly one canonical Pages artifact upload is required")
    checks = [i for i, step in enumerate(steps)
              if step.get("run") == 'python3 Tools/check-listing.py --listing "$listPublishDirectory/index.json"'
              and "if" not in step]
    require(len(checks) == 1 and checks[0] < positions["prepare-publication"],
            "Validate the complete listing before preparing publication")
    require(positions["mirror-preflight"] < positions["prepare-publication"] < uploads[0]
            < positions["mirror-deploy-guard"] < deployments[0] < positions["mirrors"],
            "Preserve preflight, preparation, upload, fresh deployment guard, deployment and mirror order")
    require(positions["mirror-deploy-guard"] == deployments[0] - 1,
            "The fresh mirror guard must immediately precede canonical deployment")
    require(positions["mirrors"] == len(steps) - 1, "Mirror publication must be the final Pages publication step")


def check(name, workflow):
    events = workflow.get("on")
    require(isinstance(events, dict), name + ": explicit event mapping required")
    allowed = {"pull_request", "push", "workflow_dispatch", "release"}
    require(set(events) <= allowed, name + ": unsafe or unreviewed trigger")
    require(workflow.get("permissions") in ({}, {"contents": "read"}), name + ": default permissions must be read-only")
    require(isinstance(workflow.get("concurrency"), dict), name + ": concurrency required")
    jobs = workflow.get("jobs", {})
    require(set(jobs) == JOBS[name], name + ": job inventory requires review")
    require(not contains_secret_reference(workflow.get("env", {})), name + ": global secrets are not approved")
    metadata = {key: value for key, value in workflow.items() if key != "jobs"}
    require(not contains_secret_reference(metadata), name + ": global secret references are not approved")
    require(not any(marker in json.dumps(metadata) for marker in PUBLICATION_MARKERS),
            name + ": mirror commands and credentials must be step scoped")
    for job_name, job in jobs.items():
        prefix = name + ": " + job_name
        require(job.get("runs-on") == "ubuntu-latest", prefix + ": only standard hosted Ubuntu is approved")
        timeout = job.get("timeout-minutes")
        require(type(timeout) is int and 1 <= timeout <= 30, prefix + ": bounded timeout required")
        require(job.get("continue-on-error", False) is False, prefix + ": job failures must block")
        permissions = job.get("permissions", workflow["permissions"])
        require(isinstance(permissions, dict), prefix + ": explicit permissions mapping required")
        if "write" in permissions.values():
            require(permissions == WRITE_JOBS.get((name, job_name)), prefix + ": unexpected write permission")
            require(job.get("if"), prefix + ": publication trigger gate required")
        else:
            require(set(permissions.values()) <= {"read", "none"}, prefix + ": unknown permission")
            require(not contains_secret_reference(job), prefix + ": read-only jobs must not receive secrets")
        require(job.get("container") is None and job.get("services") is None,
                prefix + ": unreviewed containers/services")
        metadata = {key: value for key, value in job.items() if key != "steps"}
        require(not contains_secret_reference(metadata), prefix + ": secrets must be step scoped")
        require(not any(marker in json.dumps(metadata) for marker in PUBLICATION_MARKERS),
                prefix + ": mirror commands and credentials must be step scoped")
        for step in job.get("steps", []):
            require(step.get("continue-on-error", False) is False, prefix + ": step failures must block")
            action = step.get("uses")
            if action:
                require(re.fullmatch(r"[\w.-]+/[\w./-]+@[0-9a-f]{40}", action), prefix + ": action must be SHA pinned")
                if action.startswith("actions/checkout@"):
                    require(step.get("with", {}).get("persist-credentials") is False, prefix + ": checkout credentials must not persist")
                if action.startswith("actions/upload-artifact@") or action.startswith("actions/upload-pages-artifact@"):
                    days = step.get("with", {}).get("retention-days")
                    require(type(days) is int and 1 <= days <= 7, prefix + ": artifact retention must be bounded")
                    path = step.get("with", {}).get("path", "")
                    require(path in {"work/candidate", "${{ env.listPublishDirectory }}"}, prefix + ": artifact upload scope is not approved")
            require("${{" not in step.get("run", ""), prefix + ": expressions must enter scripts through env")
            check_publication_step(name, job_name, step)
            check_step_secrets(name, job_name, step)

    if name == "validate.yml":
        require(set(events) == {"pull_request", "push"}, "Validate must run on every PR and main push")
        require(events["push"] == {"branches": ["main"]}, "Validate push must target main")
        require("schema" in jobs, "Required schema check must be preserved")
    if name in {"release-vpm.yml", "release-candidate.yml", "check-upstream.yml"}:
        require(set(events) == {"workflow_dispatch"}, name + ": manual trigger required")
    if name in {"release-vpm.yml", "release-candidate.yml"}:
        inputs = events["workflow_dispatch"].get("inputs", {})
        require(inputs.get("expected_commit", {}).get("required") is True, name + ": expected commit required")
        job = jobs["release" if name == "release-vpm.yml" else "candidate"]
        require(job.get("if") == "github.ref == 'refs/heads/main'", name + ": main-only job required")
        group = "release-vpm" if name == "release-vpm.yml" else "release-candidate"
        require(workflow["concurrency"] == {"group": group, "cancel-in-progress": False, "queue": "max"},
                name + ": preserve and serialize queued manual runs")
    if name in {"validate.yml", "release-vpm.yml", "release-candidate.yml"}:
        job_name = {"validate.yml": "schema", "release-vpm.yml": "release", "release-candidate.yml": "candidate"}[name]
        require(any(step.get("run") == "python3 Tools/run-validation.py --report work/validation.json"
                    and not step.get("if") for step in jobs[job_name]["steps"]), name + ": unconditional complete validation required")
    if name == "release-vpm.yml":
        require(inputs.get("candidate_run_id", {}).get("required") is True, "Publication requires a successful candidate run")
        steps = jobs["release"]["steps"]
        runs = [step.get("run", "") for step in steps]
        gates = [step for step in steps if "Tools/verify-release-candidate.py" in step.get("run", "")]
        require(len(gates) == 1 and not gates[0].get("if"), "Unconditional candidate receipt gate required")
        require(steps.index(gates[0]) < next(i for i, step in enumerate(steps) if step.get("name") == "Publish release"),
                "Candidate verification must precede publication")
        require(any("Tools/run-validation.py" in run for run in runs), "Full publication validation missing")
    if name == "build-listing.yml":
        require(set(events) == {"workflow_dispatch", "pull_request", "release"}
                and events["release"] == {"types": ["published", "released"]},
                "Preserve the existing listing validation and approved publication events")
        require("listing-validation" in jobs, "Required listing-validation check must be preserved")
        require(jobs["changes"].get("if") == "github.event_name == 'pull_request'"
                and jobs["check-listing-builder"].get("if") == "github.event_name == 'pull_request' && needs.changes.outputs.relevant == 'true'"
                and jobs["check-listing-builder"].get("needs") == "changes"
                and jobs["listing-validation"].get("if") == "${{ always() && github.event_name == 'pull_request' }}"
                and jobs["listing-validation"].get("needs") == ["changes", "check-listing-builder"],
                "Preserve PR listing detection, builder and unconditional required gate")
        gate = jobs["build-listing"].get("if", "")
        require(gate == PAGES_GATE,
                "Pages manual deployment must target main")
        require(workflow["concurrency"] == {"group": "listing-${{ github.event.pull_request.number || github.run_id }}",
                                            "cancel-in-progress": "${{ github.event_name == 'pull_request' }}"},
                "Do not replace pending publication workflows")
        require(jobs["build-listing"].get("concurrency") == {"group": "pages", "cancel-in-progress": False, "queue": "max"},
                "Preserve pending Pages deployments")
        check_mirror_publication(workflow, jobs["build-listing"])


def check_all(root=ROOT):
    files = {p.name: p for p in (root / ".github/workflows").iterdir()
             if p.suffix in {".yml", ".yaml"}}
    require(set(files) == WORKFLOWS, "Workflow inventory changed; review the trust and cost policy")
    for name, path in sorted(files.items()):
        check(name, load_workflow(path.read_text(encoding="utf-8")))


if __name__ == "__main__":
    check_all()
    print("Workflow trust/resource policy passed; standard Ubuntu, bounded jobs, pinned actions.")
