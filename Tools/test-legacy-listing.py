#!/usr/bin/env python3
"""Local regression tests; this file does not introduce a CI job."""
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("sync_legacy", Path(__file__).with_name("sync-legacy-listing.py"))
sync = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sync)


def listing():
    return {"name": "VR Vlog Packages", "author": "VR Vlog", "id": sync.REPOSITORY_ID,
            "url": sync.SOURCE_URL, "packages": {
                name: {"versions": {"1.0.0": {"name": name, "version": "1.0.0",
                    "zipSHA256": "a" * 64, "url": sync.RELEASE_PREFIX + "v1.0.0/" + name + "-1.0.0.zip"}}}
                for name in (sync.EXPORTER, "com.vrmc.gltf", "com.vrmc.vrm")}}


class LegacyListingTests(unittest.TestCase):
    def test_only_root_url_changes_and_source_is_not_mutated(self):
        source = listing()
        before = copy.deepcopy(source)
        legacy = json.loads(sync.make_legacy(source))
        self.assertEqual(source, before)
        self.assertEqual(legacy["url"], sync.LEGACY_URL)
        legacy["url"] = sync.SOURCE_URL
        self.assertEqual(legacy, source)

    def test_new_version_is_added_but_old_version_remains(self):
        source = listing()
        previous = json.loads(sync.make_legacy(source))
        new = copy.deepcopy(source["packages"][sync.EXPORTER]["versions"]["1.0.0"])
        new.update(version="1.1.0", zipSHA256="b" * 64,
                   url=sync.RELEASE_PREFIX + "v1.1.0/" + sync.EXPORTER + "-1.1.0.zip")
        source["packages"][sync.EXPORTER]["versions"]["1.1.0"] = new
        self.assertIn("1.1.0", json.loads(sync.make_legacy(source, previous))["packages"][sync.EXPORTER]["versions"])

    def test_missing_or_modified_history_is_rejected(self):
        for delete in (True, False):
            with self.subTest(delete=delete):
                source = listing()
                previous = json.loads(sync.make_legacy(source))
                versions = source["packages"][sync.EXPORTER]["versions"]
                if delete:
                    versions.clear()
                else:
                    versions["1.0.0"]["zipSHA256"] = "b" * 64
                with self.assertRaises(ValueError):
                    sync.make_legacy(source, previous)

    def test_bad_identity_checksum_origin_and_dependencies_are_rejected(self):
        bad = []
        for key, value in (("id", "another.repo"), ("url", sync.LEGACY_URL)):
            data = listing(); data[key] = value; bad.append(data)
        data = listing(); del data["packages"]["com.vrmc.vrm"]; bad.append(data)
        for key, value in (("zipSHA256", "a"), ("url", "https://example.com/package.zip"), ("version", "2.0.0")):
            data = listing(); data["packages"][sync.EXPORTER]["versions"]["1.0.0"][key] = value; bad.append(data)
        for data in bad:
            with self.subTest(data=data), self.assertRaises(ValueError):
                sync.parse_listing(json.dumps(data).encode(), sync.SOURCE_URL)

    def test_duplicate_keys_and_oversized_json_are_rejected(self):
        for raw in (b'{"id":"a","id":"b"}', b" " * (sync.MAX_BYTES + 1)):
            with self.assertRaises(ValueError):
                sync.parse_listing(raw, sync.SOURCE_URL)

    def test_repository_transfer_and_rename_release_aliases_preserve_history(self):
        source = listing()
        historical = json.loads(sync.make_legacy(source))
        for package in historical["packages"].values():
            for manifest in package["versions"].values():
                manifest["url"] = manifest["url"].replace(sync.RELEASE_PREFIX, sync.HISTORICAL_RELEASE_PREFIX)
        raw = json.dumps(historical).encode()
        previous = sync.parse_listing(raw, sync.LEGACY_URL, allow_historical_urls=True)
        self.assertEqual(json.loads(sync.make_legacy(source, previous))["packages"], source["packages"])
        historical["url"] = sync.SOURCE_URL
        self.assertEqual(sync.parse_listing(json.dumps(historical).encode(), sync.SOURCE_URL), historical)
        for package in historical["packages"].values():
            for manifest in package["versions"].values():
                manifest["url"] = manifest["url"].replace(sync.HISTORICAL_RELEASE_PREFIX, sync.PREVIOUS_RELEASE_PREFIX)
        historical["url"] = sync.LEGACY_URL
        previous = sync.parse_listing(json.dumps(historical).encode(), sync.LEGACY_URL)
        self.assertEqual(json.loads(sync.make_legacy(source, previous))["packages"], source["packages"])

    def test_immutable_manifest_tag_filename_hash_and_exact_origins(self):
        source = listing()
        for key, value in (("description", "altered"), ("dependencies", {"other.package": "1.0.0"}),
                           ("zipSHA256", "b" * 64),
                           ("url", sync.PREVIOUS_RELEASE_PREFIX + "other-tag/com.vrmc.vrm-1.0.0.zip")):
            previous = json.loads(sync.make_legacy(source))
            previous["packages"]["com.vrmc.vrm"]["versions"]["1.0.0"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                sync.make_legacy(source, previous)
        valid_url = source["packages"][sync.EXPORTER]["versions"]["1.0.0"]["url"]
        for url in (valid_url.replace("github.com", "github.com.evil.test"), valid_url + "?token=marker",
                    valid_url + "#fragment", valid_url.replace("v1.0.0/", "v2.0.0/"),
                    valid_url.replace("-1.0.0.zip", ".zip"), valid_url.replace("https://", "http://")):
            bad = copy.deepcopy(source); bad["packages"][sync.EXPORTER]["versions"]["1.0.0"]["url"] = url
            with self.subTest(url=url), self.assertRaises(ValueError):
                sync.parse_listing(json.dumps(bad).encode(), sync.SOURCE_URL)

    def test_failed_replace_preserves_previous_file_and_cleans_own_temporary_files(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / ".nojekyll").touch()
            source = listing(); sync.synchronize(root, source)
            target = root / "VR-Vlog-lilToon-Exporter/index.json"; before = target.read_bytes()
            source["name"] = "Updated name"
            with patch.object(sync.os, "replace", side_effect=OSError("disk failure")):
                with self.assertRaises(OSError): sync.synchronize(root, source)
            self.assertEqual(target.read_bytes(), before)
            self.assertTrue((root / ".sync-legacy-listing.lock").exists())
            self.assertEqual(list(target.parent.iterdir()), [target])

    def test_repeated_sync_is_noop_and_active_lock_is_not_removed(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / ".nojekyll").touch()
            self.assertTrue(sync.synchronize(root, listing()))
            self.assertFalse(sync.synchronize(root, listing()))
            lock = root / ".sync-legacy-listing.lock"; lock.write_text("another owner")
            with sync.publication_lock(lock):
                with self.assertRaises(OSError): sync.synchronize(root, listing())
            self.assertEqual(lock.read_text(), "another owner")

    @unittest.skipIf(os.name == "nt", "Process termination case uses the POSIX fixture")
    def test_killed_owner_lock_is_released_without_deleting_lock_file(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / ".nojekyll").touch()
            lock = root / ".sync-legacy-listing.lock"
            program = "import fcntl,sys; f=open(sys.argv[1],'a+b'); fcntl.flock(f,fcntl.LOCK_EX); print('locked',flush=True); sys.stdin.read()"
            process = subprocess.Popen([sys.executable, "-c", program, str(lock)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
            try:
                self.assertEqual(process.stdout.readline().strip(), "locked")
                with self.assertRaises(OSError): sync.synchronize(root, listing())
                process.kill(); process.wait(timeout=5)
                self.assertTrue(lock.exists())
                self.assertTrue(sync.synchronize(root, listing()))
            finally:
                if process.poll() is None:
                    process.kill(); process.wait(timeout=5)
                process.stdin.close(); process.stdout.close()

    def test_symlink_is_rejected_without_overwriting_its_destination(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); (root / ".nojekyll").touch()
            elsewhere = root / "other.json"; elsewhere.write_text("keep")
            directory = root / "VR-Vlog-lilToon-Exporter"; directory.mkdir()
            (directory / "index.json").symlink_to(elsewhere)
            with self.assertRaises(ValueError): sync.synchronize(root, listing())
            self.assertEqual(elsewhere.read_text(), "keep")


if __name__ == "__main__":
    unittest.main()
