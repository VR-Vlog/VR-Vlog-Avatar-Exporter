#!/usr/bin/env python3
"""Mirror publication regressions using local fakes; no live mutation or token."""
import base64
import copy
from email.message import Message
import importlib.util
import io
import json
import os
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
import urllib.error

spec = importlib.util.spec_from_file_location("sync_mirrors", Path(__file__).with_name("sync-vpm-mirrors.py"))
sync = importlib.util.module_from_spec(spec)
spec.loader.exec_module(sync)


def listing():
    return {"name": "VR Vlog Packages", "id": sync.REPOSITORY_ID,
            "url": sync.CANONICAL_URL, "author": {"name": "VR Vlog"}, "packages": {
                name: {"versions": {"1.0.0": {"name": name, "version": "1.0.0",
                    "zipSHA256": "a" * 64, "url": "https://github.com" + sync.RELEASE_PATH
                    + "v1.0.0/" + name + "-1.0.0.zip"}}}
                for name in (sync.EXPORTER, "com.vrmc.gltf", "com.vrmc.vrm")}}


def new_version(data):
    data = copy.deepcopy(data)
    version = copy.deepcopy(data["packages"][sync.EXPORTER]["versions"]["1.0.0"])
    version.update(version="1.1.0", zipSHA256="b" * 64,
                   url="https://github.com" + sync.RELEASE_PATH + "v1.1.0/" + sync.EXPORTER + "-1.1.0.zip")
    data["packages"][sync.EXPORTER]["versions"]["1.1.0"] = version
    return data


def desired(source):
    files = {"index.json": json.dumps(source).encode(), "index.html": b"<html>Approved publication</html>",
             "assets/image.png": b"\x89PNG\r\n"}
    return {sync.OFFICIAL.repository: {
                sync.OFFICIAL.listing_path: sync.render_listing(source, sync.OFFICIAL_URL),
                sync.OFFICIAL.prefix + "index.html": files["index.html"],
                sync.OFFICIAL.prefix + "assets/image.png": files["assets/image.png"],
                sync.OFFICIAL.prefix + sync.PUBLICATION_RECEIPT: receipt_bytes(files)},
            sync.LEGACY.repository: {sync.LEGACY.listing_path: sync.render_listing(source, sync.LEGACY_URL)}}


def receipt_bytes(files):
    payload = {"schema": 1, "id": sync.REPOSITORY_ID,
               "files": {path: sync.hashlib.sha256(raw).hexdigest() for path, raw in files.items()
                         if path != sync.PUBLICATION_RECEIPT}}
    digest = sync.hashlib.sha256(json.dumps(payload, ensure_ascii=False, sort_keys=True,
                                           separators=(",", ":"), allow_nan=False).encode()).hexdigest()
    return (json.dumps({**payload, "digest": digest}, ensure_ascii=False, sort_keys=True,
                       indent=2, allow_nan=False) + "\n").encode()


def write_receipt(root):
    files = {path.relative_to(root).as_posix(): path.read_bytes() for path in root.rglob("*")
             if path.is_file() and path.name != sync.PUBLICATION_RECEIPT}
    (root / sync.PUBLICATION_RECEIPT).write_bytes(receipt_bytes(files))


class FakeReceipt:
    def __init__(self, source):
        self.current = sync.parse_publication_receipt(desired(source)[sync.OFFICIAL.repository][sync.OFFICIAL.prefix + sync.PUBLICATION_RECEIPT])
        self.calls = []
        self.responses = {}

    def __call__(self, url, budget):
        self.calls.append(url)
        queue = self.responses.get(url)
        if queue:
            result = queue.pop(0)
            if isinstance(result, Exception):
                raise result
            return result
        return self.current


class FakeApi:
    def __init__(self):
        self.files = {target: {} for target in sync.TARGETS}
        self.heads = {target: "1" * 40 for target in sync.TARGETS}
        self.commits = []
        self.hooks = {}

    def snapshot(self, target):
        scoped = {path: sync.Entry(sync.git_blob_oid(raw)) for path, raw in self.files[target].items()
                  if sync.in_scope(target, path)}
        return sync.Snapshot(self.heads[target], scoped, self.files[target].get(target.listing_path))

    def commit(self, target, expected, changes):
        self.commits.append((target, expected, changes))
        hook = self.hooks.get(target)
        if hook is not None:
            hook(target, expected, changes)
        if expected != self.heads[target]:
            raise sync.CommitUncertain("Concurrent head changed")
        self.files[target].update(changes.additions)
        for path in changes.deletions:
            del self.files[target][path]
        self.heads[target] = sync.git_blob_oid(str(len(self.commits)).encode())
        return self.heads[target]


class FakePublic:
    def __init__(self, source, api):
        self.source = source
        self.api = api
        self.calls = []
        self.responses = {}

    def __call__(self, url, budget):
        self.calls.append(url)
        queue = self.responses.get(url)
        if queue:
            response = queue.pop(0)
            if isinstance(response, Exception):
                raise response
            return response
        if url == sync.CANONICAL_URL:
            return self.source
        target = next(target for target in sync.TARGETS if target.listing_url == url)
        return sync.parse_listing(self.api.files[target][target.listing_path], url)


class FakeClock:
    def __init__(self):
        self.now = 0

    def __call__(self):
        return self.now

    def sleep(self, seconds):
        self.now += seconds


class Response:
    def __init__(self, request, data, *, content_type="application/json", status=200, url=None):
        self.status = status
        self.url = url or request.full_url
        self.data = data
        self.offset = 0
        self.headers = Message()
        self.headers["Content-Type"] = content_type

    def read(self, limit):
        chunk = self.data[self.offset:self.offset + limit]
        self.offset += len(chunk)
        return chunk

    def __enter__(self):
        return self

    def __exit__(self, *args):
        return False


class Opener:
    def __init__(self, responses):
        self.responses = list(responses)
        self.requests = []

    def open(self, request, timeout):
        self.requests.append((request, timeout))
        data = self.responses.pop(0)
        if isinstance(data, Exception):
            raise data
        if callable(data):
            return data(request)
        return Response(request, json.dumps(data).encode())


class MirrorTests(unittest.TestCase):
    def run_sync(self, source=None, api=None, public=None, *, seconds=600, receipt=None):
        source = source or listing()
        api = api or FakeApi()
        public = public or FakePublic(source, api)
        receipt = receipt or FakeReceipt(source)
        clock = FakeClock()
        report = sync.synchronize(source, desired(source), api, public,
                                  sync.Budget(seconds, clock=clock), sleep=clock.sleep, receipt_fetch=receipt)
        return report, api, public

    def test_render_changes_only_root_url_and_keeps_all_manifests(self):
        source = new_version(listing())
        before = copy.deepcopy(source)
        for url in (sync.OFFICIAL_URL, sync.LEGACY_URL):
            data = json.loads(sync.render_listing(source, url))
            self.assertEqual(data.pop("url"), url)
            self.assertEqual(data, {key: value for key, value in source.items() if key != "url"})
        self.assertEqual(source, before)

    def test_build_snapshot_copies_binary_and_credits_rewrites_rendered_links(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            encoded = sync.urllib.parse.quote(sync.CANONICAL_URL, safe="").lower()
            page = '<input value="' + sync.CANONICAL_URL + '"><a href="vcc://vpm/addRepo?url=' + encoded + '">Add</a>'
            (root / "index.html").write_text(page)
            (root / "app.js").write_text('const url = "' + sync.CANONICAL_URL + '";')
            (root / "credit.txt").write_text("Image credit: artist")
            (root / "image.png").write_bytes(b"\xff\x00binary")
            write_receipt(root)
            source, files = sync.read_publication(root)
            self.assertEqual(source["url"], sync.CANONICAL_URL)
            official = files[sync.OFFICIAL.repository]
            self.assertIn(sync.OFFICIAL_URL.encode(), official[sync.OFFICIAL.prefix + "index.html"])
            self.assertIn(sync.urllib.parse.quote(sync.OFFICIAL_URL, safe="").encode(), official[sync.OFFICIAL.prefix + "index.html"])
            self.assertEqual(official[sync.OFFICIAL.prefix + "credit.txt"], b"Image credit: artist")
            self.assertEqual(official[sync.OFFICIAL.prefix + "image.png"], b"\xff\x00binary")
            self.assertEqual(set(files[sync.LEGACY.repository]), {sync.LEGACY.listing_path})

    def test_unbuilt_tokens_symlinks_and_size_bounds_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            page = root / "index.html"
            page.write_text('<input value="{{ listingInfo.Url }}">')
            write_receipt(root)
            with self.assertRaisesRegex(sync.SyncError, "unresolved"):
                sync.read_publication(root)
            page.write_text("Rendered")
            (root / "leak").symlink_to(root / "index.json")
            with self.assertRaisesRegex(sync.SyncError, "non-regular"):
                sync.read_publication(root)
            (root / "leak").unlink()
            (root / "link").symlink_to(root, target_is_directory=True)
            with self.assertRaisesRegex(sync.SyncError, "directory symlink"):
                sync.read_publication(root)
            (root / "link").unlink()
            with patch.object(sync, "MAX_FILE_BYTES", 10), self.assertRaises(sync.SyncError):
                sync.read_publication(root)

    def test_unreadable_or_disappearing_directory_is_not_silently_omitted(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            (root / "index.html").write_text("Rendered")
            blocked = root / "assets"; blocked.mkdir()
            (blocked / "published-image.png").write_bytes(b"Published asset")
            original_scandir = os.scandir
            for error in (PermissionError("private-error-marker"), FileNotFoundError("private-error-marker")):
                def fail_directory(path, error=error):
                    if Path(path) == blocked:
                        raise error
                    return original_scandir(path)
                with self.subTest(error=type(error).__name__), patch.object(sync.os, "scandir", side_effect=fail_directory):
                    with self.assertRaisesRegex(sync.SyncError, "directory could not be read") as caught:
                        sync.read_publication(root)
                    self.assertNotIn("private-error-marker", str(caught.exception))

    def test_empty_directories_are_counted_and_invalid_directories_are_rejected(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            (root / "index.html").write_text("Rendered")
            (root / "empty-one").mkdir(); (root / "empty-two").mkdir()
            with patch.object(sync, "MAX_DIRECTORIES", 2):
                with self.assertRaisesRegex(sync.SyncError, "directory-count bound"):
                    sync.read_publication(root)
            (root / ".git").mkdir()
            with self.assertRaisesRegex(sync.SyncError, "Invalid publication path"):
                sync.read_publication(root)

    def test_opened_file_is_checked_and_read_is_bounded(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            (root / "index.html").write_text("Rendered")
            opened = unittest.mock.Mock()
            opened.__enter__ = unittest.mock.Mock(return_value=opened)
            opened.__exit__ = unittest.mock.Mock(return_value=False)
            opened.fileno.return_value = 9
            opened.read.return_value = b"x" * 11
            with patch.object(sync, "MAX_FILE_BYTES", 10), \
                 patch.object(sync.os, "open", return_value=9), \
                 patch.object(sync.os, "fdopen", return_value=opened), \
                 patch.object(sync.os, "fstat", return_value=unittest.mock.Mock(st_mode=sync.stat.S_IFREG)):
                with self.assertRaisesRegex(sync.SyncError, "file exceeds its size bound"):
                    sync.read_publication(root)
            opened.read.assert_called_once_with(11)

    def test_full_snapshot_receipt_rejects_missing_mixed_page_and_asset_snapshots(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "index.json").write_text(json.dumps(listing()))
            (root / "index.html").write_text("Original rendered page")
            (root / "image.png").write_bytes(b"Original asset")
            with self.assertRaisesRegex(sync.SyncError, "requires its full-snapshot receipt"):
                sync.read_publication(root)
            write_receipt(root)
            original = {path.name: path.read_bytes() for path in root.iterdir()}
            for name, content in (("index.html", b"New page, identical catalog"), ("image.png", b"New asset")):
                (root / name).write_bytes(content)
                with self.subTest(name=name), self.assertRaisesRegex(sync.SyncError, "differs from the full built snapshot"):
                    sync.read_publication(root)
                (root / name).write_bytes(original[name])
            source, files = sync.read_publication(root)
            self.assertEqual(source, listing())
            self.assertEqual(files[sync.OFFICIAL.repository][sync.OFFICIAL.prefix + sync.PUBLICATION_RECEIPT],
                             original[sync.PUBLICATION_RECEIPT])

    def test_receipt_schema_hashes_digest_and_file_paths_are_strict(self):
        valid = json.loads(receipt_bytes({"index.html": b"Page", "index.json": b"{}"}))
        malformed = [b'{"schema":1,"schema":2}', b"not JSON"]
        for key, value in (("schema", True), ("schema", 2), ("id", "other"), ("digest", "b" * 64),
                           ("files", {"../private": "a" * 64}),
                           ("files", {sync.PUBLICATION_RECEIPT: "a" * 64}),
                           ("files", {"index.html": "bad"})):
            data = copy.deepcopy(valid); data[key] = value; malformed.append(json.dumps(data).encode())
        data = copy.deepcopy(valid); data["unexpected"] = "field"; malformed.append(json.dumps(data).encode())
        for raw in malformed:
            with self.subTest(raw=raw), self.assertRaises(sync.SyncError):
                sync.parse_publication_receipt(raw)

    def test_initial_receipt_propagation_is_polled_but_later_different_receipt_aborts(self):
        source = listing(); api = FakeApi(); receipt = FakeReceipt(source)
        older = json.loads(receipt_bytes({"index.json": json.dumps(source).encode(), "index.html": b"Older page"}))
        receipt.responses[sync.CANONICAL_RECEIPT_URL] = [older, sync.ApiError("Propagation pending"), receipt.current]
        report, _, _ = self.run_sync(source, api, receipt=receipt)
        self.assertEqual(report["publicationDigest"], receipt.current["digest"])
        self.assertGreaterEqual(receipt.calls.count(sync.CANONICAL_RECEIPT_URL), 7)
        self.assertIn(sync.OFFICIAL_RECEIPT_URL, receipt.calls)

    def test_same_catalog_new_page_or_asset_cannot_overwrite_current_publication(self):
        for changed_files in ({"index.html": b"New page"}, {"assets/image.png": b"New asset"}):
            api = FakeApi(); receipt = FakeReceipt(listing())
            newer_files = {"index.json": json.dumps(listing()).encode(), "index.html": b"<html>Approved publication</html>",
                           "assets/image.png": b"\x89PNG\r\n", **changed_files}
            newer = sync.parse_publication_receipt(receipt_bytes(newer_files))
            receipt.responses[sync.CANONICAL_RECEIPT_URL] = [receipt.current, newer]
            with self.subTest(changed_files=changed_files), self.assertRaisesRegex(sync.SyncError, "Canonical full publication changed"):
                self.run_sync(api=api, receipt=receipt)
            self.assertEqual(api.commits, [])
            self.assertEqual(receipt.calls.count(sync.CANONICAL_RECEIPT_URL), 2)

    def test_initial_full_snapshot_mismatch_is_bounded_and_never_writes(self):
        api = FakeApi(); receipt = FakeReceipt(listing())
        receipt.current = sync.parse_publication_receipt(receipt_bytes({"index.html": b"Different page", "index.json": json.dumps(listing()).encode()}))
        with self.assertRaisesRegex(sync.SyncError, "Canonical full publication.*polling bound"):
            self.run_sync(api=api, receipt=receipt)
        self.assertEqual(api.commits, [])
        self.assertEqual(receipt.calls.count(sync.CANONICAL_RECEIPT_URL), sync.MAX_POLLS)

    def test_receipt_freshness_applies_to_noops_and_cas_or_ambiguous_retries(self):
        newer = sync.parse_publication_receipt(receipt_bytes({"index.json": json.dumps(listing()).encode(), "index.html": b"Newer page"}))
        for mode in ("noop", "cas", "ambiguous"):
            api = FakeApi(); receipt = FakeReceipt(listing())
            if mode == "noop":
                api.files[sync.OFFICIAL].update(desired(listing())[sync.OFFICIAL.repository])
                receipt.responses[sync.CANONICAL_RECEIPT_URL] = [receipt.current, newer]
            else:
                def advance(target, expected, changes):
                    del api.hooks[target]
                    api.heads[target] = "2" * 40
                    if mode == "ambiguous":
                        api.files[target].update(changes.additions)
                    receipt.current = newer
                    raise sync.CommitUncertain("Concurrent full-site publication")
                api.hooks[sync.OFFICIAL] = advance
            with self.subTest(mode=mode), self.assertRaisesRegex(sync.SyncError, "Canonical full publication changed"):
                self.run_sync(api=api, receipt=receipt)
            self.assertEqual(len(api.commits), 0 if mode == "noop" else 1)

    def test_midrun_same_catalog_advance_aborts_before_second_commit(self):
        api = FakeApi(); receipt = FakeReceipt(listing())
        def advance(target, expected, changes):
            del api.hooks[target]
            receipt.current = sync.parse_publication_receipt(receipt_bytes({"index.html": b"New page", "index.json": json.dumps(listing()).encode()}))
        api.hooks[sync.OFFICIAL] = advance
        with self.assertRaisesRegex(sync.SyncError, "Canonical full publication changed"):
            self.run_sync(api=api, receipt=receipt)
        self.assertEqual(len(api.commits), 1)

    def test_official_receipt_waits_for_full_site_deployment_and_fails_if_it_never_arrives(self):
        for finish in (False, True):
            api = FakeApi(); receipt = FakeReceipt(listing())
            old = sync.parse_publication_receipt(receipt_bytes({"index.html": b"Older published page", "index.json": json.dumps(listing()).encode()}))
            receipt.responses[sync.OFFICIAL_RECEIPT_URL] = [old] * (2 if finish else sync.MAX_POLLS)
            if finish:
                report, _, _ = self.run_sync(api=api, receipt=receipt)
                self.assertEqual(report["status"], "passed")
                self.assertEqual(receipt.calls.count(sync.OFFICIAL_RECEIPT_URL), 3)
                self.assertEqual(receipt.calls[-1], sync.CANONICAL_RECEIPT_URL)
            else:
                with self.assertRaisesRegex(sync.SyncError, "Official full publication"):
                    self.run_sync(api=api, receipt=receipt)
            self.assertEqual(len(api.commits), 2)

    def test_final_same_catalog_receipt_advance_prevents_success(self):
        api = FakeApi(); receipt = FakeReceipt(listing())
        original = receipt.__call__
        newer = sync.parse_publication_receipt(receipt_bytes({"index.html": b"New page", "index.json": json.dumps(listing()).encode()}))
        def fetch_receipt(url, budget):
            value = original(url, budget)
            if url == sync.OFFICIAL_RECEIPT_URL:
                receipt.current = newer
            return value
        with self.assertRaisesRegex(sync.SyncError, "Canonical full publication changed"):
            self.run_sync(api=api, receipt=fetch_receipt)
        self.assertEqual(len(api.commits), 2)

    def test_changed_receipt_during_endpoint_polling_aborts_without_waiting_for_a_rewind(self):
        api = FakeApi(); public = FakePublic(listing(), api); receipt = FakeReceipt(listing())
        public.responses[sync.OFFICIAL_URL] = [sync.ApiError("Official JSON not ready")]
        newer = sync.parse_publication_receipt(receipt_bytes({"index.html": b"New page", "index.json": json.dumps(listing()).encode()}))
        receipt.responses[sync.CANONICAL_RECEIPT_URL] = [receipt.current] * 4 + [newer]
        with self.assertRaisesRegex(sync.SyncError, "Canonical full publication changed"):
            self.run_sync(api=api, public=public, receipt=receipt)
        self.assertEqual(public.calls.count(sync.OFFICIAL_URL), 1)
        self.assertEqual(receipt.calls.count(sync.CANONICAL_RECEIPT_URL), 5)
        self.assertEqual(len(api.commits), 2)

    def test_validation_rejects_duplicate_keys_nonfinite_bad_identity_and_origin(self):
        malformed = [b'{"id":"one","id":"two"}', b'{"number":NaN}', b"null", b"not JSON"]
        for key, value in (("id", "other"), ("url", sync.OFFICIAL_URL)):
            data = listing(); data[key] = value; malformed.append(json.dumps(data).encode())
        for key, value in (("zipSHA256", "bad"), ("name", "other"),
                           ("url", "https://github.com.evil.test/secret.zip"),
                           ("url", "https://github.com" + sync.RELEASE_PATH + "v1.0.0/other.zip?key=secret")):
            data = listing(); data["packages"][sync.EXPORTER]["versions"]["1.0.0"][key] = value
            malformed.append(json.dumps(data).encode())
        for raw in malformed:
            with self.subTest(raw=raw), self.assertRaises(sync.SyncError):
                sync.parse_listing(raw, sync.CANONICAL_URL)
        with patch.object(sync, "MAX_LISTING_BYTES", 1), self.assertRaises(sync.SyncError):
            sync.parse_listing(json.dumps(listing()).encode(), sync.CANONICAL_URL)

    def test_success_checks_all_three_endpoints_and_retry_is_noop(self):
        report, api, public = self.run_sync()
        self.assertEqual(report["status"], "passed")
        self.assertEqual(len(api.commits), 2)
        self.assertEqual(public.calls[-5:], [sync.OFFICIAL_URL, sync.LEGACY_URL, sync.CANONICAL_URL,
                                            sync.CANONICAL_URL, sync.CANONICAL_URL])
        self.assertEqual(set(report["verifiedEndpoints"]), {sync.CANONICAL_URL, sync.OFFICIAL_URL, sync.LEGACY_URL})
        again, _, _ = self.run_sync(api=api)
        self.assertEqual(len(api.commits), 2)
        self.assertFalse(any(entry["changed"] for entry in again["repositories"]))

    def test_canonical_cdn_staleness_and_public_mirror_delay_are_polled(self):
        source = new_version(listing())
        api = FakeApi(); public = FakePublic(source, api)
        public.responses[sync.CANONICAL_URL] = [listing(), sync.ApiError("Temporary HTTP failure"), source]
        public.responses[sync.OFFICIAL_URL] = [sync.ApiError("Pages not deployed yet")]
        report, _, _ = self.run_sync(source, api, public)
        self.assertEqual(report["status"], "passed")
        self.assertGreaterEqual(public.calls.count(sync.CANONICAL_URL), 6)

    def test_canonical_mismatch_never_writes_and_polling_is_bounded(self):
        api = FakeApi(); public = FakePublic(new_version(listing()), api)
        with self.assertRaisesRegex(sync.SyncError, "polling bound"):
            self.run_sync(listing(), api, public)
        self.assertEqual(api.commits, [])
        self.assertEqual(len(public.calls), sync.MAX_POLLS)

    def test_canonical_advancing_before_either_commit_aborts(self):
        for before_second in (False, True):
            with self.subTest(before_second=before_second):
                api = FakeApi(); public = FakePublic(listing(), api)
                public.responses[sync.CANONICAL_URL] = [listing(), listing(), new_version(listing())] if before_second else [listing(), new_version(listing())]
                with self.assertRaisesRegex(sync.SyncError, "Canonical publication changed"):
                    self.run_sync(listing(), api, public)
                self.assertEqual(len(api.commits), 1 if before_second else 0)

    def test_both_histories_are_preflighted_and_cannot_downgrade_or_change_hash(self):
        for previous in (new_version(listing()), listing()):
            with self.subTest(previous=previous):
                api = FakeApi()
                if previous == listing():
                    previous["packages"][sync.EXPORTER]["versions"]["1.0.0"]["zipSHA256"] = "b" * 64
                api.files[sync.LEGACY][sync.LEGACY.listing_path] = sync.render_listing(previous, sync.LEGACY_URL)
                with self.assertRaisesRegex(sync.SyncError, "published version"):
                    self.run_sync(api=api)
                self.assertEqual(api.commits, [])

    def test_historical_personal_release_origins_are_replaced_by_canonical_data(self):
        api = FakeApi(); previous = json.loads(sync.render_listing(listing(), sync.LEGACY_URL))
        for package in previous["packages"].values():
            for manifest in package["versions"].values():
                manifest["url"] = manifest["url"].replace(sync.RELEASE_PATH, sync.HISTORICAL_RELEASE_PATH)
        api.files[sync.LEGACY][sync.LEGACY.listing_path] = json.dumps(previous).encode()
        self.run_sync(api=api)
        self.assertEqual(api.files[sync.LEGACY][sync.LEGACY.listing_path], desired(listing())[sync.LEGACY.repository][sync.LEGACY.listing_path])

    def test_target_changes_are_scoped_and_preserve_unrelated_site_content(self):
        api = FakeApi()
        outside = {sync.OFFICIAL: {"public/privacy.html": b"Privacy", "README.md": b"README"},
                   sync.LEGACY: {sync.LEGACY.prefix + "index.html": b"Legacy landing", "vr-vlog/index.html": b"App"}}
        for target in sync.TARGETS:
            api.files[target].update(outside[target])
        api.files[sync.OFFICIAL][sync.OFFICIAL.prefix + "obsolete.css"] = b"Obsolete"
        self.run_sync(api=api)
        for target in sync.TARGETS:
            for path, raw in outside[target].items():
                self.assertEqual(api.files[target][path], raw)
        self.assertNotIn(sync.OFFICIAL.prefix + "obsolete.css", api.files[sync.OFFICIAL])
        for target, _, changes in api.commits:
            self.assertTrue(all(sync.in_scope(target, path) for path in (*changes.additions, *changes.deletions)))

    def test_concurrent_head_is_replanned_and_never_loses_unrelated_changes(self):
        api = FakeApi()
        def advance(target, expected, changes):
            del api.hooks[target]
            api.heads[target] = "2" * 40
            api.files[target]["public/concurrent.html"] = b"Other publication"
        api.hooks[sync.OFFICIAL] = advance
        self.run_sync(api=api)
        official = [item for item in api.commits if item[0] == sync.OFFICIAL]
        self.assertEqual([item[1] for item in official], ["1" * 40, "2" * 40])
        self.assertEqual(api.files[sync.OFFICIAL]["public/concurrent.html"], b"Other publication")

    def test_concurrent_newer_listing_is_not_overwritten_after_cas_conflict(self):
        api = FakeApi()
        def advance(target, expected, changes):
            del api.hooks[target]
            api.heads[target] = "2" * 40
            api.files[target][target.listing_path] = sync.render_listing(new_version(listing()), target.listing_url)
        api.hooks[sync.OFFICIAL] = advance
        with self.assertRaisesRegex(sync.SyncError, "published version"):
            self.run_sync(api=api)
        self.assertEqual(len(api.commits), 1)

    def test_response_lost_after_commit_is_recovered_without_duplicate_commit(self):
        api = FakeApi()
        def ambiguous(target, expected, changes):
            del api.hooks[target]
            api.files[target].update(changes.additions)
            api.heads[target] = "2" * 40
            raise sync.CommitUncertain("Response lost")
        api.hooks[sync.OFFICIAL] = ambiguous
        report, _, _ = self.run_sync(api=api)
        self.assertEqual(len(api.commits), 2)
        self.assertEqual(report["repositories"][0]["commit"], "2" * 40)

    def test_partial_failure_retains_first_commit_and_retry_only_updates_second(self):
        api = FakeApi()
        api.hooks[sync.LEGACY] = lambda *args: (_ for _ in ()).throw(sync.CommitUncertain("Branch rule denied"))
        progress = []
        clock = FakeClock()
        with self.assertRaisesRegex(sync.SyncError, "retry bound"):
            sync.synchronize(listing(), desired(listing()), api, FakePublic(listing(), api),
                             sync.Budget(clock=clock), sleep=clock.sleep, progress=progress.append,
                             receipt_fetch=FakeReceipt(listing()))
        first_head = api.heads[sync.OFFICIAL]
        self.assertEqual(len(progress), 1)
        self.assertEqual(progress[0]["commit"], first_head)
        self.assertEqual(progress[0]["commitUrl"], f"https://github.com/{sync.OFFICIAL.repository}/commit/{first_head}")
        self.assertEqual(sum(target == sync.OFFICIAL for target, _, _ in api.commits), 1)
        del api.hooks[sync.LEGACY]
        self.run_sync(api=api)
        self.assertEqual(api.heads[sync.OFFICIAL], first_head)
        self.assertEqual(sum(target == sync.OFFICIAL for target, _, _ in api.commits), 1)

    def test_final_canonical_advance_prevents_reporting_success(self):
        api = FakeApi(); public = FakePublic(listing(), api)
        public.responses[sync.CANONICAL_URL] = [listing(), listing(), listing()]
        original = api.commit
        def commit(target, expected, changes):
            oid = original(target, expected, changes)
            if target == sync.LEGACY:
                public.source = new_version(listing())
            return oid
        api.commit = commit
        with self.assertRaisesRegex(sync.SyncError, "polling bound"):
            self.run_sync(listing(), api, public)
        self.assertEqual(len(api.commits), 2)

    def test_shared_time_budget_caps_wait_and_http_timeout(self):
        api = FakeApi(); public = FakePublic(new_version(listing()), api)
        with self.assertRaisesRegex(sync.SyncError, "time budget"):
            self.run_sync(listing(), api, public, seconds=15)
        clock = FakeClock(); opener = Opener([{}])
        client = sync.GitHubApi("local-test-token", sync.Budget(3, clock=clock), opener=opener)
        client.request("/graphql")
        self.assertEqual(opener.requests[0][1], 3)


class ApiTests(unittest.TestCase):
    def api(self, responses):
        opener = Opener(responses)
        return sync.GitHubApi("local-test-token", sync.Budget(), opener=opener), opener

    def test_snapshot_uses_commit_pinned_tree_and_checks_blob_hash(self):
        raw = sync.render_listing(listing(), sync.OFFICIAL_URL); oid = sync.git_blob_oid(raw)
        responses = [{"object": {"sha": "1" * 40, "type": "commit"}}, {"tree": {"sha": "2" * 40}},
                     {"truncated": False, "tree": [{"path": "public", "type": "tree", "mode": "040000"},
                         {"path": "public/exporter", "type": "tree", "mode": "040000"},
                         {"path": sync.OFFICIAL.listing_path, "type": "blob", "mode": "100644", "sha": oid},
                         {"path": "public/privacy.html", "type": "blob", "mode": "100644", "sha": "3" * 40}]},
                     {"encoding": "base64", "content": base64.b64encode(raw).decode()}]
        client, opener = self.api(responses)
        snapshot = client.snapshot(sync.OFFICIAL)
        self.assertEqual(snapshot.listing, raw)
        self.assertEqual(set(snapshot.entries), {sync.OFFICIAL.listing_path})
        self.assertIn("/git/commits/" + "1" * 40, opener.requests[1][0].full_url)
        self.assertIn("/git/trees/" + "2" * 40, opener.requests[2][0].full_url)
        responses[-1] = {"encoding": "base64", "content": base64.b64encode(b"wrong").decode()}
        client, _ = self.api(responses)
        with self.assertRaisesRegex(sync.SyncError, "identity differs"):
            client.snapshot(sync.OFFICIAL)

    def test_truncated_tree_scoped_symlink_gitlink_and_ancestor_file_are_rejected(self):
        bad_trees = [{"truncated": True, "tree": []}]
        for path, mode, kind in (("public", "120000", "blob"), ("public/exporter", "100644", "blob"),
                                 (sync.OFFICIAL.prefix + "link", "120000", "blob"),
                                 (sync.OFFICIAL.prefix + "module", "160000", "commit")):
            bad_trees.append({"truncated": False, "tree": [{"path": path, "mode": mode, "type": kind, "sha": "3" * 40}]})
        for tree in bad_trees:
            client, _ = self.api([{"object": {"sha": "1" * 40, "type": "commit"}}, {"tree": {"sha": "2" * 40}}, tree])
            with self.subTest(tree=tree), self.assertRaises(sync.SyncError):
                client.snapshot(sync.OFFICIAL)

    def test_preflight_reads_only_the_two_approved_repositories_without_commits(self):
        responses = []
        for target in sync.TARGETS:
            responses.extend([
                {"full_name": target.repository, "default_branch": "main", "archived": False, "permissions": {"push": True}},
                {"object": {"sha": "1" * 40, "type": "commit"}},
                {"tree": {"sha": "2" * 40}}, {"truncated": False, "tree": []}])
        client, opener = self.api(responses)
        report = client.preflight()
        self.assertEqual(report, {"status": "passed", "repositories": [target.repository for target in sync.TARGETS]})
        self.assertEqual(len(opener.requests), 8)
        for request, _ in opener.requests:
            self.assertEqual(request.get_method(), "GET")
            self.assertIsNone(request.data)
            self.assertTrue(any(request.full_url.startswith("https://api.github.com/repos/" + target.repository) for target in sync.TARGETS))

    def test_preflight_rejects_changed_default_branch_and_no_write_access(self):
        valid = {"full_name": sync.OFFICIAL.repository, "default_branch": "main", "archived": False, "permissions": {"push": True}}
        for key, value in (("default_branch", "other"), ("archived", True), ("permissions", {"push": False}), ("full_name", "other/repo")):
            metadata = copy.deepcopy(valid); metadata[key] = value
            client, opener = self.api([metadata])
            with self.subTest(key=key), self.assertRaises(sync.SyncError):
                client.preflight()
            self.assertEqual(len(opener.requests), 1)

    def test_cli_preflight_needs_no_publication_directory_and_never_commits(self):
        fake = unittest.mock.Mock()
        fake.preflight.return_value = {"status": "passed", "repositories": [target.repository for target in sync.TARGETS]}
        output = io.StringIO()
        with patch.object(sys, "argv", ["sync-vpm-mirrors.py", "--preflight"]), \
             patch.dict(os.environ, {sync.TOKEN_ENV: "local-test-token"}), \
             patch.object(sync, "GitHubApi", return_value=fake), \
             patch.object(sync, "read_publication", side_effect=AssertionError("Preflight must not need files")), \
             patch.object(sys, "stdout", output):
            sync.main()
        fake.preflight.assert_called_once_with()
        fake.commit.assert_not_called()
        self.assertNotIn("local-test-token", output.getvalue())

    def test_malformed_api_objects_fail_without_raw_traceback_data(self):
        for ref in (None, {}, {"object": None}, {"object": []}):
            client, _ = self.api([ref])
            with self.subTest(ref=ref), self.assertRaises(sync.SyncError):
                client.snapshot(sync.OFFICIAL)
        for response in ({"data": None}, {"data": {"createCommitOnBranch": None}}, {"data": {"createCommitOnBranch": {"commit": None}}}):
            client, _ = self.api([response])
            with self.subTest(response=response), self.assertRaises(sync.CommitUncertain):
                client.commit(sync.LEGACY, "1" * 40, sync.Changes({sync.LEGACY.listing_path: b"{}"}, ()))

    def test_slow_streaming_response_also_consumes_total_budget(self):
        clock = FakeClock()
        class SlowResponse(Response):
            def read1(self, limit):
                clock.now += 2
                return self.read(1)
        opener = Opener([lambda req: SlowResponse(req, b"{}")])
        with self.assertRaisesRegex(sync.SyncError, "time budget"):
            sync.read_response(sync.urllib.request.Request(sync.CANONICAL_URL), 10,
                               sync.Budget(3, clock=clock), opener=opener)

    def test_mutation_has_expected_head_and_exact_paths_without_force(self):
        client, opener = self.api([{"data": {"createCommitOnBranch": {"commit": {"oid": "2" * 40}}}}])
        changes = sync.Changes({sync.LEGACY.listing_path: b"binary\x00"}, ())
        self.assertEqual(client.commit(sync.LEGACY, "1" * 40, changes), "2" * 40)
        request = opener.requests[0][0]; body = json.loads(request.data)
        data = body["variables"]["input"]
        self.assertEqual(data["expectedHeadOid"], "1" * 40)
        self.assertEqual(data["branch"], {"repositoryNameWithOwner": sync.LEGACY.repository, "branchName": "main"})
        self.assertEqual(data["fileChanges"]["additions"], [{"path": sync.LEGACY.listing_path, "contents": base64.b64encode(b"binary\x00").decode()}])
        self.assertNotIn("force", json.dumps(body))
        self.assertEqual(request.full_url, "https://api.github.com/graphql")

    def test_commit_errors_are_safe_and_do_not_echo_bodies_or_secret(self):
        secret = "sensitive-fake-marker"
        for response in ({"errors": [{"message": secret}]}, urllib.error.URLError(secret),
                         urllib.error.HTTPError("https://private.test/?token=" + secret, 403, secret, {}, None)):
            client, _ = self.api([response])
            with self.assertRaises(sync.CommitUncertain) as caught:
                client.commit(sync.LEGACY, "1" * 40, sync.Changes({sync.LEGACY.listing_path: b"{}"}, ()))
            self.assertNotIn(secret, str(caught.exception))

    def test_unapproved_repos_paths_and_token_headers_are_rejected(self):
        client, opener = self.api([])
        for target in (sync.Target("other/repo", sync.OFFICIAL.prefix, sync.OFFICIAL_URL),):
            with self.assertRaises(sync.SyncError):
                client.snapshot(target)
        for path in ("README.md", "VR-Vlog-lilToon-Exporter/../README.md", "/absolute", "a\\b", ".git/config"):
            with self.subTest(path=path), self.assertRaises(sync.SyncError):
                client.commit(sync.LEGACY, "1" * 40, sync.Changes({path: b"x"}, ()))
        self.assertEqual(opener.requests, [])
        for route in ("/repos/other/repo", "/repos/" + sync.OFFICIAL.repository + "/../../other/repo",
                      "/repos/" + sync.OFFICIAL.repository + "/actions/workflows", None):
            with self.subTest(route=route), self.assertRaises(sync.SyncError):
                client.request(route)
        for token in (None, "", "unsafe\nheader", "non-ascii-秘密"):
            with self.assertRaises(sync.SyncError):
                sync.GitHubApi(token, sync.Budget())

    def test_http_rejects_redirect_html_and_oversized_response(self):
        request = sync.urllib.request.Request(sync.CANONICAL_URL)
        for response in (lambda req: Response(req, b"{}", status=302),
                         lambda req: Response(req, b"{}", url="https://elsewhere.test/"),
                         lambda req: Response(req, b"12345")):
            opener = Opener([response])
            with self.assertRaises(sync.SyncError):
                sync.read_response(request, 4, sync.Budget(), opener=opener)
        self.assertIsNone(sync.NoRedirect().redirect_request(request, None, 302, "Found", {}, "https://elsewhere.test/"))
        with patch.object(sync, "read_response", return_value=(b"{}", Response(request, b"", content_type="text/html").headers)):
            with self.assertRaisesRegex(sync.SyncError, "did not return JSON"):
                sync.fetch_listing(sync.CANONICAL_URL, sync.Budget())

    def test_receipt_reads_use_unique_queries_fixed_origins_and_no_auth(self):
        raw = receipt_bytes({"index.html": b"Page", "index.json": b"{}"})
        observed = []
        def respond(request, limit, budget):
            observed.append(request)
            return raw, Response(request, raw).headers
        with patch.object(sync, "read_response", side_effect=respond):
            for url in (sync.CANONICAL_RECEIPT_URL, sync.OFFICIAL_RECEIPT_URL, sync.CANONICAL_RECEIPT_URL):
                self.assertEqual(sync.fetch_publication_receipt(url, sync.Budget()), sync.parse_publication_receipt(raw))
        self.assertEqual(len({request.full_url for request in observed}), 3)
        for request, base in zip(observed, (sync.CANONICAL_RECEIPT_URL, sync.OFFICIAL_RECEIPT_URL, sync.CANONICAL_RECEIPT_URL)):
            self.assertTrue(request.full_url.startswith(base + "?mirror-check="))
            self.assertIsNone(request.get_header("Authorization"))
            self.assertEqual(request.get_header("Cache-control"), "no-cache, no-store")
        with self.assertRaisesRegex(sync.SyncError, "Unapproved publication receipt endpoint"):
            sync.fetch_publication_receipt("https://private.test/publication.json", sync.Budget())
        request = sync.urllib.request.Request(sync.CANONICAL_RECEIPT_URL)
        with patch.object(sync, "read_response", return_value=(raw, Response(request, raw, content_type="text/html").headers)):
            with self.assertRaisesRegex(sync.SyncError, "did not return JSON"):
                sync.fetch_publication_receipt(sync.CANONICAL_RECEIPT_URL, sync.Budget())


if __name__ == "__main__":
    unittest.main()
