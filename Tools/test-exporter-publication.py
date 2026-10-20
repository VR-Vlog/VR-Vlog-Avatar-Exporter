"""Exercise publication splitting, retry safety, and rejection before mutation."""
import copy
import contextlib
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

spec = importlib.util.spec_from_file_location("exporter_publication", Path(__file__).with_name("prepare-exporter-publication.py"))
publication = importlib.util.module_from_spec(spec)
spec.loader.exec_module(publication)


def contents(root):
    return {path.relative_to(root).as_posix(): path.read_bytes()
            for path in root.rglob("*") if path.is_file() and not path.is_symlink()}


def original_files(files):
    return {path: raw for path, raw in files.items() if path != publication.PUBLICATION_RECEIPT}


class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / "Website"
        self.official = self.root / "official-exporter-publication"
        self.source.mkdir()
        self.publisher = {"repository": publication.PUBLISHER_REPOSITORY,
                          "workflow": publication.PUBLISHER_WORKFLOW,
                          "run_number": 42, "run_id": 38434276596, "source_commit": "c" * 40}
        self.preflight = {"schema": 1, "publisher": self.publisher, "official": None}
        self.fresh_official_reader = lambda: None
        self.listing = {"id": publication.REPOSITORY_ID, "url": publication.CANONICAL_URL, "packages": {}}
        for name in publication.REQUIRED_PACKAGES:
            version = "0.11.14" if name == publication.REQUIRED_PACKAGES[0] else "0.131.0"
            manifest = {"name": name, "version": version, "zipSHA256": "a" * 64,
                        "url": f"https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/releases/download/v{version}/{name}-{version}.zip"}
            self.listing["packages"][name] = {"versions": {version: manifest}}
        self.write_listing()
        self.html = f'''<!doctype html><html><head><link rel="canonical" href="{publication.OFFICIAL_BASE}"></head>
<body><main id="main"><a href="vcc://vpm/addRepo?url={publication.CANONICAL_URL}">追加</a>
<input id="listing-url" value="{publication.CANONICAL_URL}"><p>©もち山金魚</p></main></body></html>'''.encode("utf-8")
        (self.source / "index.html").write_bytes(self.html)
        (self.source / "assets").mkdir()
        (self.source / "assets/image.bin").write_bytes(b"\x00\xff\x01")
        (self.source / "assets/README.md").write_text("Original image credits\n", encoding="utf-8")
        (self.source / "localize.js").write_text('const languages = {en: {copy: "Copy"}};\n', encoding="utf-8")
        (self.source / "styles.css").write_text("body { color: #fff; }\n", encoding="utf-8")

    def prepare(self, source, destination, *, publisher=None, preflight=None, fresh_official_reader=None):
        return publication.prepare(source, destination, publisher=publisher or self.publisher,
                                   preflight=self.preflight if preflight is None else preflight,
                                   fresh_official_reader=self.fresh_official_reader if fresh_official_reader is None else fresh_official_reader)

    def ci_environment(self):
        return {"GITHUB_ACTIONS": "true", "GITHUB_REPOSITORY": publication.PUBLISHER_REPOSITORY,
                "GITHUB_REPOSITORY_ID": publication.PUBLISHER_REPOSITORY_ID,
                "GITHUB_WORKFLOW_REF": publication.PUBLISHER_REPOSITORY + "/" + publication.PUBLISHER_WORKFLOW + "@refs/heads/main",
                "GITHUB_EVENT_NAME": "workflow_dispatch", "GITHUB_REF": "refs/heads/main",
                "GITHUB_RUN_NUMBER": str(self.publisher["run_number"]), "GITHUB_RUN_ID": str(self.publisher["run_id"]),
                "GITHUB_SHA": self.publisher["source_commit"], "RUNNER_TEMP": str(self.root)}

    def observed(self, publisher, digest):
        return {"schema": 1, "publisher": self.publisher,
                "official": {"publisher": publisher, "digest": digest}}

    def historical_receipt(self, **publisher_fields):
        # A stored fixture, not a call to the new receipt writer with an alias.
        receipt = json.loads(publication.publication_receipt(contents(self.source), self.publisher))
        receipt["publisher"].update(repository=publication.LEGACY_PUBLISHER_REPOSITORY, **publisher_fields)
        receipt["digest"] = publication.receipt_digest({key: receipt[key] for key in ("schema", "id", "files", "publisher")})
        return (json.dumps(receipt, ensure_ascii=False, sort_keys=True, indent=2) + "\n").encode("utf-8")

    def write_listing(self):
        (self.source / "index.json").write_text(json.dumps(self.listing, indent=2) + "\n", encoding="utf-8")

    def previous_snapshot(self):
        self.official.mkdir()
        for relative, raw in contents(self.source).items():
            path = self.official / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        (self.official / "old.txt").write_bytes(b"previous publication")
        return contents(self.official)

    def assert_rejected_unchanged(self, pattern, *, source=None, official=None):
        before = contents(self.source)
        destination_before = contents(self.official) if self.official.exists() else None
        with self.assertRaisesRegex(publication.PreparationError, pattern):
            self.prepare(source or self.source, official or self.official)
        self.assertEqual(before, contents(self.source))
        if destination_before is None:
            self.assertFalse(self.official.exists())
        else:
            self.assertEqual(destination_before, contents(self.official))

    def test_full_snapshot_is_byte_identical_with_receipt_and_canonical_html_redirect(self):
        before = contents(self.source)
        report = self.prepare(self.source, self.official)
        self.assertEqual("prepared", report["status"])
        saved = contents(self.official)
        self.assertEqual(before, original_files(saved))
        receipt = publication.validate_publication_receipt(saved)
        self.assertEqual(hashlib.sha256(before["index.html"]).hexdigest(), receipt["files"]["index.html"])
        self.assertNotIn(publication.PUBLICATION_RECEIPT, receipt["files"])
        after = contents(self.source)
        self.assertEqual(saved[publication.PUBLICATION_RECEIPT], after[publication.PUBLICATION_RECEIPT])
        self.assertEqual(set(before) | {publication.PUBLICATION_RECEIPT}, set(after))
        self.assertEqual({key: raw for key, raw in before.items() if key != "index.html"},
                         {key: raw for key, raw in original_files(after).items() if key != "index.html"})
        self.assertEqual(before["index.json"], after["index.json"])
        redirect = after["index.html"].decode("utf-8")
        self.assertIn(f'content="0; url={publication.OFFICIAL_BASE}"', redirect)
        self.assertIn(f'location.replace("{publication.OFFICIAL_BASE}" + location.search + location.hash)', redirect)
        self.assertIn(f'<a href="{publication.OFFICIAL_BASE}">', redirect)
        self.assertIn('<a href="index.json">', redirect)
        self.assertNotIn('vcc://', redirect)

    def test_completed_retry_rejects_redirect_and_retains_both_publications(self):
        self.prepare(self.source, self.official)
        self.assert_rejected_unchanged("already a redirect")

    def test_rebuilt_retry_replaces_snapshot_without_retaining_stale_files(self):
        self.previous_snapshot()
        before = contents(self.source)
        self.prepare(self.source, self.official)
        self.assertEqual(before, original_files(contents(self.official)))
        (self.source / "index.html").write_bytes(self.html)
        (self.source / "assets/new.bin").write_bytes(b"new version")
        rebuilt = contents(self.source)
        self.prepare(self.source, self.official)
        saved = contents(self.official)
        self.assertEqual(original_files(rebuilt), original_files(saved))
        publication.validate_publication_receipt(saved)
        self.assertNotEqual(rebuilt[publication.PUBLICATION_RECEIPT], saved[publication.PUBLICATION_RECEIPT])

    def test_identical_parent_and_child_roots_are_rejected(self):
        for destination in (self.source, self.source / "mirror", self.root):
            with self.subTest(destination=destination):
                self.assert_rejected_unchanged("distinct and non-overlapping", official=destination)

    def test_symlink_alias_to_nested_destination_cannot_bypass_overlap_check(self):
        alias = self.root / "alias"
        alias.symlink_to(self.source, target_is_directory=True)
        self.assert_rejected_unchanged("distinct and non-overlapping", official=alias / "mirror")

    def test_similar_sibling_name_is_not_mistaken_for_an_overlapping_root(self):
        sibling = self.root / "Website-official"
        before = contents(self.source)
        self.prepare(self.source, sibling)
        self.assertEqual(before, original_files(contents(sibling)))

    def test_unrendered_html_package_loop_and_script_tokens_are_rejected(self):
        for relative, text in (("index.html", "{{ listingInfo.Url }}"),
                               ("index.html", "{{~ for package in packages ~}}"),
                               ("localize.js", "{{ package.ZipUrl }}"),
                               ("assets/README.md", "{{ listingInfo.Url }}"),
                               ("assets/image.bin", "{{ listingInfo.Url }}")):
            with self.subTest(relative=relative, text=text):
                path = self.source / relative
                saved = path.read_bytes()
                path.write_text(text, encoding="utf-8")
                self.assert_rejected_unchanged("unresolved template")
                path.write_bytes(saved)

    def test_missing_entry_files_and_non_utf8_text_are_rejected(self):
        for relative in ("index.html", "index.json"):
            path = self.source / relative
            saved = path.read_bytes()
            path.unlink()
            self.assert_rejected_unchanged("requires index.html and index.json")
            path.write_bytes(saved)
        (self.source / "localize.js").write_bytes(b"\xff")
        self.assert_rejected_unchanged("not UTF-8")

    def test_listing_identity_missing_payload_and_bad_manifest_are_rejected(self):
        good = copy.deepcopy(self.listing)
        cases = []
        for key, value in (("id", "other.repository"), ("url", publication.OFFICIAL_BASE + "index.json"),
                           ("packages", {})):
            bad = copy.deepcopy(good)
            bad[key] = value
            cases.append(bad)
        bad = copy.deepcopy(good)
        next(iter(bad["packages"][publication.REQUIRED_PACKAGES[0]]["versions"].values()))["zipSHA256"] = "0"
        cases.append(bad)
        for listing in cases:
            self.listing = listing
            self.write_listing()
            self.assert_rejected_unchanged("identity|canonical URL|missing|checksum")

    def test_duplicate_and_nonfinite_json_are_rejected(self):
        for raw in ('{"id":"com.vrvlog.packages","id":"other"}', '{"id": NaN}', '{"id": 1e9999}'):
            (self.source / "index.json").write_text(raw, encoding="utf-8")
            self.assert_rejected_unchanged("duplicate|non-finite")

    def test_zip_urls_accept_only_new_org_old_org_and_old_personal_repositories(self):
        name = publication.REQUIRED_PACKAGES[0]
        versions = self.listing["packages"][name]["versions"]
        version, manifest = next(iter(versions.items()))
        for repository in ("VR-Vlog/VR-Vlog-Avatar-Exporter", "VR-Vlog/VR-Vlog-lilToon-Exporter",
                           "mitsuya0077/VR-Vlog-lilToon-Exporter"):
            with self.subTest(repository=repository):
                manifest["url"] = f"https://github.com/{repository}/releases/download/v{version}/{name}-{version}.zip"
                raw = json.dumps(self.listing).encode("utf-8")
                self.assertEqual(self.listing, publication.validate_listing(raw))
        for base in ("https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter-evil",
                     "https://github.com/other/VR-Vlog-Avatar-Exporter",
                     "https://github.com/mitsuya0077/VR-Vlog-Avatar-Exporter",
                     "https://github.com.evil.example/VR-Vlog/VR-Vlog-Avatar-Exporter",
                     "https://github.com:443/VR-Vlog/VR-Vlog-Avatar-Exporter",
                     "https://github.com@evil.example/VR-Vlog/VR-Vlog-Avatar-Exporter"):
            with self.subTest(base=base):
                manifest["url"] = f"{base}/releases/download/v{version}/{name}-{version}.zip"
                with self.assertRaisesRegex(publication.PreparationError, "ZIP origin"):
                    publication.validate_listing(json.dumps(self.listing).encode("utf-8"))

    def test_symlink_file_directory_and_root_are_rejected(self):
        target = self.root / "outside.bin"
        target.write_bytes(b"outside")
        link = self.source / "linked.bin"
        link.symlink_to(target)
        self.assert_rejected_unchanged("non-regular")
        link.unlink()
        link.symlink_to(self.root, target_is_directory=True)
        self.assert_rejected_unchanged("directory symlink")
        link.unlink()
        alias = self.root / "root-link"
        alias.symlink_to(self.source, target_is_directory=True)
        self.assert_rejected_unchanged("roots cannot be symlinks", source=alias)

    def test_special_file_and_unsafe_paths_are_rejected(self):
        fifo = self.source / "pipe"
        os.mkfifo(fifo)
        self.assert_rejected_unchanged("non-regular")
        fifo.unlink()
        for name in ("bad\\path", "bad\npath"):
            path = self.source / name
            path.write_bytes(b"invalid name")
            self.assert_rejected_unchanged("Invalid publication path")
            path.unlink()
        (self.source / ".git").mkdir()
        self.assert_rejected_unchanged("Invalid publication directory")

    def test_file_total_and_count_bounds_fail_before_changes(self):
        for name, value in (("MAX_FILE_BYTES", 1), ("MAX_PUBLICATION_BYTES", 1),
                            ("MAX_FILES", 1), ("MAX_DIRECTORIES", 1)):
            with self.subTest(bound=name), mock.patch.object(publication, name, value):
                self.assert_rejected_unchanged("bound")

    def test_unreadable_directory_is_rejected_instead_of_silently_omitted(self):
        real_scandir = os.scandir
        def fail_assets(path):
            if not isinstance(path, int) and Path(path).resolve() == (self.source / "assets").resolve():
                raise PermissionError("simulated unreadable directory")
            return real_scandir(path)
        before = contents(self.source)
        with mock.patch.object(publication.os, "scandir", side_effect=fail_assets), \
                self.assertRaisesRegex(publication.PreparationError, "directory could not be read"):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertFalse(self.official.exists())

    def test_snapshot_install_failure_preserves_source_and_previous_snapshot(self):
        old = self.previous_snapshot()
        before = contents(self.source)
        real_replace = os.replace
        def fail_snapshot(source, destination):
            if Path(source).name == "snapshot":
                raise OSError("simulated snapshot installation failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_snapshot), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(old, contents(self.official))

    def test_redirect_replace_failure_restores_previous_snapshot(self):
        old = self.previous_snapshot()
        before = contents(self.source)
        real_replace = os.replace
        def fail_redirect(source, destination):
            if Path(source).name.startswith(".exporter-redirect-"):
                raise OSError("simulated redirect installation failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_redirect), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(old, contents(self.official))

    def test_failed_rollback_retains_original_snapshot_backup(self):
        old = self.previous_snapshot()
        before = contents(self.source)
        real_replace = os.replace
        def fail_redirect_and_restore(source, destination):
            if Path(source).name.startswith(".exporter-redirect-") or Path(source).name == "previous":
                raise OSError("simulated failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_redirect_and_restore), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        backups = list(self.root.glob(".exporter-publication-*/previous"))
        self.assertEqual(1, len(backups))
        self.assertEqual(old, contents(backups[0]))
        self.assertEqual(before, contents(self.source))

    def test_receipt_is_deterministic_and_matches_the_agreed_digest_encoding(self):
        files = contents(self.source)
        raw = publication.publication_receipt(files, self.publisher)
        self.assertEqual(raw, publication.publication_receipt(dict(reversed(list(files.items()))), self.publisher))
        receipt = publication.parse_publication_receipt(raw)
        payload = {"schema": 2, "id": publication.REPOSITORY_ID, "publisher": self.publisher,
                   "files": {path: hashlib.sha256(value).hexdigest() for path, value in files.items()}}
        expected = hashlib.sha256(json.dumps(payload, ensure_ascii=False, sort_keys=True,
                                            separators=(",", ":"), allow_nan=False).encode("utf-8")).hexdigest()
        self.assertEqual(expected, receipt["digest"])
        with self.assertRaisesRegex(publication.PreparationError, "cannot include itself"):
            publication.publication_receipt(dict(files, **{publication.PUBLICATION_RECEIPT: raw}), self.publisher)

    def test_same_listing_with_different_html_has_a_different_publication_identity(self):
        files = contents(self.source)
        first = publication.publication_receipt(files, self.publisher)
        changed = dict(files)
        changed["index.html"] += b"\n<!-- newer introduction -->\n"
        second = publication.publication_receipt(changed, self.publisher)
        self.assertEqual(files["index.json"], changed["index.json"])
        self.assertNotEqual(first, second)
        self.assertNotEqual(publication.parse_publication_receipt(first)["digest"],
                            publication.parse_publication_receipt(second)["digest"])

    def test_tampered_missing_or_added_snapshot_assets_fail_receipt_validation(self):
        self.prepare(self.source, self.official)
        files = contents(self.official)
        for action in ("changed", "missing", "added"):
            with self.subTest(action=action):
                bad = dict(files)
                if action == "changed":
                    bad["styles.css"] += b"body { background: red; }"
                elif action == "missing":
                    del bad["assets/README.md"]
                else:
                    bad["unlisted.txt"] = b"unlisted bytes"
                with self.assertRaisesRegex(publication.PreparationError, "differs from the complete snapshot"):
                    publication.validate_publication_receipt(bad)
        (self.official / "styles.css").write_bytes(b"tampered")
        with self.assertRaisesRegex(publication.PreparationError, "differs from the complete snapshot"):
            publication.read_snapshot(self.official)

    def test_malformed_reserved_receipt_is_rejected_before_changes(self):
        valid = json.loads(publication.publication_receipt(contents(self.source), self.publisher))
        bad_receipts = [b"unrelated source file", b"{}", b'{"schema":1,"schema":1}']
        for change in ("schema", "digest", "hash", "self", "path", "extra"):
            bad = copy.deepcopy(valid)
            if change == "schema":
                bad["schema"] = True
            elif change == "digest":
                bad["digest"] = "0" * 64
            elif change == "hash":
                bad["files"]["index.html"] = "A" * 64
            elif change == "self":
                bad["files"][publication.PUBLICATION_RECEIPT] = "a" * 64
            elif change == "path":
                bad["files"]["../outside"] = "a" * 64
            else:
                bad["unapproved"] = "field"
            if change != "digest":
                bad["digest"] = publication.receipt_digest({key: bad[key] for key in ("schema", "id", "files", "publisher")})
            bad_receipts.append(json.dumps(bad).encode("utf-8"))
        for raw in bad_receipts:
            with self.subTest(raw=raw[:80]):
                (self.source / publication.PUBLICATION_RECEIPT).write_bytes(raw)
                self.assert_rejected_unchanged("receipt|publication path")

    def test_rebuilt_source_regenerates_a_valid_prior_receipt_from_new_html(self):
        self.prepare(self.source, self.official)
        old = (self.source / publication.PUBLICATION_RECEIPT).read_bytes()
        listing = (self.source / "index.json").read_bytes()
        (self.source / "index.html").write_bytes(self.html + b"\n<!-- updated page -->\n")
        self.prepare(self.source, self.official)
        saved = contents(self.official)
        self.assertNotEqual(old, saved[publication.PUBLICATION_RECEIPT])
        self.assertEqual(saved[publication.PUBLICATION_RECEIPT], (self.source / publication.PUBLICATION_RECEIPT).read_bytes())
        self.assertEqual(listing, saved["index.json"])
        publication.validate_publication_receipt(saved)

    def test_receipt_staging_write_failure_preserves_both_publications(self):
        previous = self.previous_snapshot()
        before = contents(self.source)
        real_write = Path.write_bytes
        def fail_receipt(path, raw):
            if path.name == publication.PUBLICATION_RECEIPT:
                raise OSError("simulated receipt write failure")
            return real_write(path, raw)
        with mock.patch.object(Path, "write_bytes", autospec=True, side_effect=fail_receipt), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(previous, contents(self.official))

    def test_canonical_receipt_replace_failure_preserves_both_publications(self):
        previous = self.previous_snapshot()
        before = contents(self.source)
        real_replace = os.replace
        def fail_receipt(source, destination):
            if Path(source).name.startswith(".exporter-receipt-"):
                raise OSError("simulated receipt replacement failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_receipt), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(previous, contents(self.official))

    def test_redirect_failure_restores_existing_canonical_receipt_and_previous_snapshot(self):
        self.prepare(self.source, self.official)
        (self.source / "index.html").write_bytes(self.html + b"\n<!-- rebuilt page -->\n")
        before = contents(self.source)
        previous = contents(self.official)
        real_replace = os.replace
        def fail_redirect(source, destination):
            if Path(source).name.startswith(".exporter-redirect-"):
                raise OSError("simulated redirect replacement failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_redirect), self.assertRaises(OSError):
            self.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(previous, contents(self.official))

    def test_older_full_workflow_rerun_is_rejected_before_any_replacement(self):
        self.previous_snapshot()
        newer = dict(self.publisher, run_number=43, run_id=38434276597, source_commit="d" * 40)
        self.preflight = self.observed(newer, "a" * 64)
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("older publisher run")
            replacement.assert_not_called()

    def test_equal_sequence_with_changed_page_digest_is_rejected_before_deployment(self):
        old_receipt = publication.parse_publication_receipt(publication.publication_receipt(contents(self.source), self.publisher))
        self.preflight = self.observed(self.publisher, old_receipt["digest"])
        (self.source / "index.html").write_bytes(self.html + b"\n<!-- changed after publication -->\n")
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("equal publisher sequence.*different.*digest")
            replacement.assert_not_called()

    def test_equal_sequence_with_different_run_or_source_commit_is_rejected(self):
        for previous in (dict(self.publisher, run_id=self.publisher["run_id"] + 1),
                         dict(self.publisher, source_commit="d" * 40)):
            with self.subTest(previous=previous):
                self.preflight = self.observed(previous, "a" * 64)
                self.assert_rejected_unchanged("Equal publisher sequence.*different run or source")

    def test_exact_same_run_rerun_regenerates_identical_receipt_and_recovers(self):
        expected = publication.publication_receipt(contents(self.source), self.publisher)
        digest = publication.parse_publication_receipt(expected)["digest"]
        self.preflight = self.observed(self.publisher, digest)
        self.fresh_official_reader = lambda: expected
        self.prepare(self.source, self.official)
        saved = contents(self.official)
        self.assertEqual(expected, saved[publication.PUBLICATION_RECEIPT])
        (self.source / "index.html").write_bytes(self.html)
        report = self.prepare(self.source, self.official)
        self.assertEqual(saved, contents(self.official))
        self.assertEqual(digest, report["publicationDigest"])

    def test_newer_dispatch_has_new_publisher_bound_receipt_even_for_identical_files(self):
        previous = dict(self.publisher)
        old = publication.parse_publication_receipt(publication.publication_receipt(contents(self.source), previous))
        self.publisher = dict(previous, run_number=43, run_id=38434276597, source_commit="d" * 40)
        self.preflight = self.observed(previous, old["digest"])
        self.fresh_official_reader = lambda: json.dumps(old).encode("utf-8")
        self.prepare(self.source, self.official)
        fresh = publication.validate_publication_receipt(contents(self.official))
        self.assertEqual(self.publisher, fresh["publisher"])
        self.assertEqual(old["files"], fresh["files"])
        self.assertNotEqual(old["digest"], fresh["digest"])

    def test_proof_from_another_ci_run_and_invalid_proof_shapes_are_rejected(self):
        cases = [None, {}, {"schema": True, "publisher": self.publisher, "official": None},
                 {"schema": 1, "publisher": dict(self.publisher, run_id=38434276598), "official": None},
                 self.observed(self.publisher, "A" * 64), self.observed(self.publisher, None)]
        for proof in cases:
            with self.subTest(proof=proof):
                before = contents(self.source)
                with self.assertRaises(publication.PreparationError):
                    publication.prepare(self.source, self.official, publisher=self.publisher, preflight=proof,
                                        fresh_official_reader=lambda: None)
                self.assertEqual(before, contents(self.source))
                self.assertFalse(self.official.exists())

    def test_receipt_rejects_schema_one_and_foreign_or_invalid_publisher_contexts(self):
        receipt = json.loads(publication.publication_receipt(contents(self.source), self.publisher))
        old = {key: value for key, value in receipt.items() if key != "publisher"}
        old["schema"] = 1
        old["digest"] = publication.receipt_digest({key: old[key] for key in ("schema", "id", "files")})
        with self.assertRaisesRegex(publication.PreparationError, "schema or identity"):
            publication.parse_publication_receipt(json.dumps(old).encode("utf-8"))
        for key, value in (("repository", "other/repository"), ("workflow", ".github/workflows/other.yml"),
                           ("run_number", True), ("run_number", 0), ("run_id", "38434276596"),
                           ("source_commit", "C" * 40)):
            bad = dict(self.publisher, **{key: value})
            with self.subTest(key=key, value=value), self.assertRaises(publication.PreparationError):
                publication.publication_receipt(contents(self.source), bad)

    def test_historical_publisher_receipt_is_read_without_normalizing_its_digest(self):
        raw = self.historical_receipt(run_number=41, run_id=38434276595)
        stored = json.loads(raw)
        parsed = publication.parse_publication_receipt(raw)
        self.assertEqual(stored, parsed)
        self.assertEqual(publication.LEGACY_PUBLISHER_REPOSITORY, parsed["publisher"]["repository"])
        files = dict(contents(self.source), **{publication.PUBLICATION_RECEIPT: raw})
        self.assertEqual(parsed, publication.validate_publication_receipt(files))
        with self.assertRaisesRegex(publication.PreparationError, "publisher identity"):
            publication.publication_receipt(contents(self.source), parsed["publisher"])
        stored["publisher"]["repository"] = publication.PUBLISHER_REPOSITORY
        with self.assertRaisesRegex(publication.PreparationError, "digest differs"):
            publication.parse_publication_receipt(json.dumps(stored).encode("utf-8"))

    def test_new_publisher_preparation_upgrades_an_older_historical_receipt(self):
        raw = self.historical_receipt(run_number=41, run_id=38434276595)
        old = publication.parse_publication_receipt(raw)
        self.preflight = self.observed(old["publisher"], old["digest"])
        self.fresh_official_reader = lambda: raw
        (self.source / publication.PUBLICATION_RECEIPT).write_bytes(raw)
        before = original_files(contents(self.source))
        self.prepare(self.source, self.official)
        saved = contents(self.official)
        self.assertEqual(before, original_files(saved))
        self.assertEqual(self.publisher, publication.validate_publication_receipt(saved)["publisher"])
        self.assertEqual(old, publication.parse_publication_receipt(raw))

    def test_historical_alias_does_not_weaken_sequence_or_incoming_proof_checks(self):
        for changes, pattern in (({}, "Equal publisher sequence"),
                                 ({"run_number": 43, "run_id": 38434276597}, "older publisher run"),
                                 ({"run_number": 41}, "newer publisher sequence.*reuse")):
            raw = self.historical_receipt(**changes)
            previous = publication.parse_publication_receipt(raw)
            self.preflight = self.observed(previous["publisher"], previous["digest"])
            with self.subTest(changes=changes), mock.patch.object(publication.os, "replace") as replacement:
                self.assert_rejected_unchanged(pattern)
                replacement.assert_not_called()
        old = publication.parse_publication_receipt(self.historical_receipt())["publisher"]
        proof = {"schema": 1, "publisher": old, "official": None}
        with self.assertRaisesRegex(publication.PreparationError, "publisher identity"):
            publication.validate_preflight(proof, self.publisher)

    def test_ci_context_requires_fixed_workflow_main_dispatch_or_matching_release_tag(self):
        environment = self.ci_environment()
        self.assertEqual(self.publisher, publication.publisher_from_environment(environment))
        release = dict(environment, GITHUB_EVENT_NAME="release", GITHUB_REF="refs/tags/v0.11.14-beta.1",
                       GITHUB_WORKFLOW_REF=publication.PUBLISHER_REPOSITORY + "/" + publication.PUBLISHER_WORKFLOW + "@refs/tags/v0.11.14-beta.1")
        self.assertEqual(self.publisher, publication.publisher_from_environment(release))
        beta_dispatch = dict(release, GITHUB_EVENT_NAME="workflow_dispatch")
        self.assertEqual(self.publisher, publication.publisher_from_environment(beta_dispatch))
        for ref in ("refs/tags/v0.11.16", "refs/tags/v0.11.16-beta.01", "refs/tags/v0.11.16-rc.1"):
            invalid = dict(beta_dispatch, GITHUB_REF=ref,
                           GITHUB_WORKFLOW_REF=publication.PUBLISHER_REPOSITORY + "/" + publication.PUBLISHER_WORKFLOW + "@" + ref)
            with self.subTest(ref=ref), self.assertRaises(publication.PreparationError):
                publication.publisher_from_environment(invalid)
        for key, value in (("GITHUB_ACTIONS", "false"), ("GITHUB_REPOSITORY", "fork/repository"),
                           ("GITHUB_REPOSITORY", publication.LEGACY_PUBLISHER_REPOSITORY),
                           ("GITHUB_REPOSITORY_ID", "1355769105"), ("GITHUB_REPOSITORY_ID", "01355769104"),
                           ("GITHUB_REPOSITORY_ID", 1355769104), ("GITHUB_EVENT_NAME", "pull_request"),
                           ("GITHUB_REF", "refs/heads/feature"), ("GITHUB_WORKFLOW_REF", "foreign/workflow@refs/heads/main"),
                           ("GITHUB_RUN_NUMBER", "042"), ("GITHUB_RUN_NUMBER", "0"),
                           ("GITHUB_RUN_ID", "1e10"), ("GITHUB_SHA", "c" * 39)):
            with self.subTest(key=key, value=value), self.assertRaises(publication.PreparationError):
                publication.publisher_from_environment(dict(environment, **{key: value}))
        for key in environment:
            if key == "RUNNER_TEMP":
                continue
            missing = dict(environment)
            del missing[key]
            with self.subTest(missing=key), self.assertRaises(publication.PreparationError):
                publication.publisher_from_environment(missing)

    def test_preflight_reader_requires_bounded_regular_file_in_runner_temp(self):
        environment = self.ci_environment()
        path = self.root / publication.PREFLIGHT_FILE
        with self.assertRaisesRegex(publication.PreparationError, "missing or unsafe"):
            publication.read_preflight(environment, self.publisher)
        path.write_text(json.dumps(self.preflight), encoding="utf-8")
        self.assertEqual(self.preflight, publication.read_preflight(environment, self.publisher))
        with mock.patch.object(publication, "MAX_FILE_BYTES", 1), self.assertRaisesRegex(publication.PreparationError, "size bound"):
            publication.read_preflight(environment, self.publisher)
        path.write_text('{"schema":1,"schema":1}', encoding="utf-8")
        with self.assertRaisesRegex(publication.PreparationError, "not valid UTF-8 JSON"):
            publication.read_preflight(environment, self.publisher)
        path.unlink()
        path.symlink_to(self.source / "index.json")
        with self.assertRaisesRegex(publication.PreparationError, "missing or unsafe"):
            publication.read_preflight(environment, self.publisher)
        path.unlink()
        os.mkfifo(path)
        with self.assertRaisesRegex(publication.PreparationError, "missing or unsafe"):
            publication.read_preflight(environment, self.publisher)

    def test_cli_refuses_missing_preflight_and_succeeds_with_matching_ci_proof(self):
        command = [sys.executable, str(Path(__file__).with_name("prepare-exporter-publication.py")),
                   "--publication-root", str(self.source), "--official-root", str(self.official)]
        environment = dict(os.environ, **self.ci_environment())
        before = contents(self.source)
        missing = subprocess.run(command, env=environment, capture_output=True, text=True, timeout=10)
        self.assertEqual(1, missing.returncode)
        self.assertIn("Mirror preflight proof is missing", missing.stderr)
        self.assertEqual(before, contents(self.source))
        self.assertFalse(self.official.exists())
        (self.root / publication.PREFLIGHT_FILE).write_text(json.dumps(self.preflight), encoding="utf-8")
        output = io.StringIO()
        with mock.patch.object(publication, "official_reader_from_environment", return_value=lambda: None) as reader_factory, \
                mock.patch.dict(os.environ, environment, clear=True), mock.patch.object(sys, "argv", command[1:]), \
                contextlib.redirect_stdout(output):
            publication.main()
        reader_factory.assert_called_once()
        self.assertEqual(self.publisher, reader_factory.call_args.args[1])
        self.assertEqual("prepared", json.loads(output.getvalue())["status"])
        self.assertEqual(self.publisher, publication.validate_publication_receipt(contents(self.official))["publisher"])

    def test_newer_sequence_cannot_reuse_an_earlier_run_identity(self):
        previous = dict(self.publisher, run_number=self.publisher["run_number"] - 1)
        self.preflight = self.observed(previous, "a" * 64)
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("newer publisher sequence.*reuse")
            replacement.assert_not_called()

    def test_fresh_read_happens_after_staging_and_before_either_published_tree_changes(self):
        before = contents(self.source)
        previous = self.previous_snapshot()
        calls = []
        def reader():
            calls.append("fresh")
            stages = list(self.root.glob(".exporter-publication-*/snapshot"))
            self.assertEqual(1, len(stages))
            staged = publication.read_snapshot(stages[0])
            self.assertEqual(before, original_files(staged))
            self.assertEqual(self.publisher, publication.validate_publication_receipt(staged)["publisher"])
            self.assertEqual(before, contents(self.source))
            self.assertEqual(previous, contents(self.official))
            return None
        self.prepare(self.source, self.official, fresh_official_reader=reader)
        self.assertEqual(["fresh"], calls)

    def test_cached_bootstrap_cannot_deploy_over_a_fresh_newer_official_receipt(self):
        newer = dict(self.publisher, run_number=43, run_id=38434276597)
        latest = publication.publication_receipt(contents(self.source), newer)
        self.fresh_official_reader = lambda: latest
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("older publisher run")
            replacement.assert_not_called()

    def test_cached_bootstrap_cannot_deploy_over_equal_sequence_different_html(self):
        changed = contents(self.source)
        changed["index.html"] += b"\n<!-- direct update after preflight -->\n"
        latest = publication.publication_receipt(changed, self.publisher)
        self.fresh_official_reader = lambda: latest
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("equal publisher sequence.*different latest official publication")
            replacement.assert_not_called()

    def test_cached_older_receipt_cannot_deploy_when_latest_official_advanced(self):
        old = dict(self.publisher, run_number=41, run_id=38434276595)
        old_raw = publication.publication_receipt(contents(self.source), old)
        self.preflight = self.observed(old, publication.parse_publication_receipt(old_raw)["digest"])
        latest = publication.publication_receipt(contents(self.source), dict(self.publisher, run_number=43, run_id=38434276597))
        self.fresh_official_reader = lambda: latest
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("older publisher run")
            replacement.assert_not_called()

    def test_previously_observed_receipt_cannot_disappear_and_be_treated_as_bootstrap(self):
        previous = dict(self.publisher, run_number=41, run_id=38434276595)
        raw = publication.publication_receipt(contents(self.source), previous)
        self.preflight = self.observed(previous, publication.parse_publication_receipt(raw)["digest"])
        self.assert_rejected_unchanged("Previously observed.*now missing")

    def test_malformed_fresh_receipt_fails_without_any_output_replacement(self):
        for raw in (b"malformed receipt", b'{"schema":1}', b'{}'):
            with self.subTest(raw=raw), mock.patch.object(publication.os, "replace") as replacement:
                self.fresh_official_reader = lambda: raw
                self.assert_rejected_unchanged("receipt")
                replacement.assert_not_called()

    def test_fresh_api_failure_is_clipped_and_preserves_source_and_previous_snapshot(self):
        self.previous_snapshot()
        def failure():
            raise RuntimeError("private response body must not appear")
        self.fresh_official_reader = failure
        with mock.patch.object(publication.os, "replace") as replacement:
            self.assert_rejected_unchanged("^Latest official publication could not be checked$")
            replacement.assert_not_called()
        self.assertFalse(list(self.root.glob(".exporter-publication-*")))

    def test_fresh_reader_is_mandatory_and_never_defaults_to_live_network(self):
        before = contents(self.source)
        with self.assertRaisesRegex(publication.PreparationError, "fresh official receipt reader"):
            publication.prepare(self.source, self.official, publisher=self.publisher,
                                preflight=self.preflight, fresh_official_reader=None)
        self.assertEqual(before, contents(self.source))
        self.assertFalse(self.official.exists())

    def test_cli_reader_reuses_fixed_mirror_api_context_checkout_and_official_target(self):
        target = object()
        api = types.SimpleNamespace(snapshot=mock.Mock(return_value=types.SimpleNamespace(receipt=None)))
        api_factory = types.SimpleNamespace(from_environment=mock.Mock(return_value=api))
        mirror = types.SimpleNamespace(publisher_from_environment=mock.Mock(return_value=self.publisher),
                                       validate_checkout=mock.Mock(), GitHubApi=api_factory,
                                       Budget=mock.Mock(return_value="bounded-budget"), TOKEN_ENV="VPM_MIRROR_TOKEN", OFFICIAL=target)
        environment = dict(self.ci_environment(), VPM_MIRROR_TOKEN="fixture")
        with mock.patch.object(publication, "load_mirror_tools", return_value=mirror):
            reader = publication.official_reader_from_environment(environment, self.publisher)
            mirror.GitHubApi.from_environment.assert_not_called()
            self.assertIsNone(reader())
        mirror.publisher_from_environment.assert_called_once_with(environment)
        mirror.validate_checkout.assert_called_once_with(self.publisher)
        mirror.GitHubApi.from_environment.assert_called_once_with(environment, "bounded-budget", require_org=False)
        api.snapshot.assert_called_once_with(target)


if __name__ == "__main__":
    unittest.main()
