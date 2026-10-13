"""Exercise publication splitting, retry safety, and rejection before mutation."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile
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
        self.listing = {"id": publication.REPOSITORY_ID, "url": publication.CANONICAL_URL, "packages": {}}
        for name in publication.REQUIRED_PACKAGES:
            version = "0.11.14" if name == publication.REQUIRED_PACKAGES[0] else "0.131.0"
            manifest = {"name": name, "version": version, "zipSHA256": "a" * 64,
                        "url": f"https://github.com/VR-Vlog/VR-Vlog-lilToon-Exporter/releases/download/v{version}/{name}-{version}.zip"}
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
            publication.prepare(source or self.source, official or self.official)
        self.assertEqual(before, contents(self.source))
        if destination_before is None:
            self.assertFalse(self.official.exists())
        else:
            self.assertEqual(destination_before, contents(self.official))

    def test_full_snapshot_is_byte_identical_with_receipt_and_canonical_html_redirect(self):
        before = contents(self.source)
        report = publication.prepare(self.source, self.official)
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
        publication.prepare(self.source, self.official)
        self.assert_rejected_unchanged("already a redirect")

    def test_rebuilt_retry_replaces_snapshot_without_retaining_stale_files(self):
        self.previous_snapshot()
        before = contents(self.source)
        publication.prepare(self.source, self.official)
        self.assertEqual(before, original_files(contents(self.official)))
        (self.source / "index.html").write_bytes(self.html)
        (self.source / "assets/new.bin").write_bytes(b"new version")
        rebuilt = contents(self.source)
        publication.prepare(self.source, self.official)
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
        publication.prepare(self.source, sibling)
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
            publication.prepare(self.source, self.official)
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
            publication.prepare(self.source, self.official)
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
            publication.prepare(self.source, self.official)
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
            publication.prepare(self.source, self.official)
        backups = list(self.root.glob(".exporter-publication-*/previous"))
        self.assertEqual(1, len(backups))
        self.assertEqual(old, contents(backups[0]))
        self.assertEqual(before, contents(self.source))

    def test_receipt_is_deterministic_and_matches_the_agreed_digest_encoding(self):
        files = contents(self.source)
        raw = publication.publication_receipt(files)
        self.assertEqual(raw, publication.publication_receipt(dict(reversed(list(files.items())))))
        receipt = publication.parse_publication_receipt(raw)
        payload = {"schema": 1, "id": publication.REPOSITORY_ID,
                   "files": {path: hashlib.sha256(value).hexdigest() for path, value in files.items()}}
        expected = hashlib.sha256(json.dumps(payload, ensure_ascii=False, sort_keys=True,
                                            separators=(",", ":"), allow_nan=False).encode("utf-8")).hexdigest()
        self.assertEqual(expected, receipt["digest"])
        with self.assertRaisesRegex(publication.PreparationError, "cannot include itself"):
            publication.publication_receipt(dict(files, **{publication.PUBLICATION_RECEIPT: raw}))

    def test_same_listing_with_different_html_has_a_different_publication_identity(self):
        files = contents(self.source)
        first = publication.publication_receipt(files)
        changed = dict(files)
        changed["index.html"] += b"\n<!-- newer introduction -->\n"
        second = publication.publication_receipt(changed)
        self.assertEqual(files["index.json"], changed["index.json"])
        self.assertNotEqual(first, second)
        self.assertNotEqual(publication.parse_publication_receipt(first)["digest"],
                            publication.parse_publication_receipt(second)["digest"])

    def test_tampered_missing_or_added_snapshot_assets_fail_receipt_validation(self):
        publication.prepare(self.source, self.official)
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
        valid = json.loads(publication.publication_receipt(contents(self.source)))
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
                bad["digest"] = publication.receipt_digest({key: bad[key] for key in ("schema", "id", "files")})
            bad_receipts.append(json.dumps(bad).encode("utf-8"))
        for raw in bad_receipts:
            with self.subTest(raw=raw[:80]):
                (self.source / publication.PUBLICATION_RECEIPT).write_bytes(raw)
                self.assert_rejected_unchanged("receipt|publication path")

    def test_rebuilt_source_regenerates_a_valid_prior_receipt_from_new_html(self):
        publication.prepare(self.source, self.official)
        old = (self.source / publication.PUBLICATION_RECEIPT).read_bytes()
        listing = (self.source / "index.json").read_bytes()
        (self.source / "index.html").write_bytes(self.html + b"\n<!-- updated page -->\n")
        publication.prepare(self.source, self.official)
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
            publication.prepare(self.source, self.official)
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
            publication.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(previous, contents(self.official))

    def test_redirect_failure_restores_existing_canonical_receipt_and_previous_snapshot(self):
        publication.prepare(self.source, self.official)
        (self.source / "index.html").write_bytes(self.html + b"\n<!-- rebuilt page -->\n")
        before = contents(self.source)
        previous = contents(self.official)
        real_replace = os.replace
        def fail_redirect(source, destination):
            if Path(source).name.startswith(".exporter-redirect-"):
                raise OSError("simulated redirect replacement failure")
            return real_replace(source, destination)
        with mock.patch.object(publication.os, "replace", side_effect=fail_redirect), self.assertRaises(OSError):
            publication.prepare(self.source, self.official)
        self.assertEqual(before, contents(self.source))
        self.assertEqual(previous, contents(self.official))


if __name__ == "__main__":
    unittest.main()
