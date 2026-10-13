#!/usr/bin/env python3
"""Publish the built VPM snapshot to the two approved mirrors, then read it back.

The canonical Pages deployment must already have succeeded. Its publication.json
receipt binds the original rendered page, catalog and every asset; changing that
receipt invalidates a synchronization even if the catalog is unchanged. The
official Git tree's publisher sequence also prevents a complete old workflow
rerun from rolling back that site after re-deploying its old canonical receipt.
Preflight saves non-secret publisher/digest evidence for the producer to check
before canonical deployment. The CLI requires the approved GitHub Actions run.
This tool creates
ordinary commits using GitHub's expectedHeadOid compare-and-swap mutation; it
does not force push, configure hosting, create releases, or bypass branch rules.
The token is read from VPM_MIRROR_TOKEN and is used only at api.github.com.
"""
import argparse
import base64
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import tempfile
import time
from typing import NamedTuple, Optional
import urllib.error
import urllib.parse
import urllib.request
import uuid

CANONICAL_BASE = "https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/"
CANONICAL_URL = CANONICAL_BASE + "index.json"
OFFICIAL_BASE = "https://vrvlog.fun/exporter/"
OFFICIAL_URL = OFFICIAL_BASE + "index.json"
LEGACY_URL = "https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json"
REPOSITORY_ID = "com.vrvlog.packages"
EXPORTER = "com.vrvlog.liltoon-vrm-exporter"
RELEASE_PATH = "/VR-Vlog/VR-Vlog-lilToon-Exporter/releases/download/"
HISTORICAL_RELEASE_PATH = "/mitsuya0077/VR-Vlog-lilToon-Exporter/releases/download/"
MAX_LISTING_BYTES = 4 * 1024 * 1024
MAX_FILE_BYTES = 4 * 1024 * 1024
MAX_PUBLICATION_BYTES = 8 * 1024 * 1024
MAX_API_BYTES = 8 * 1024 * 1024
MAX_FILES = 1000
MAX_DIRECTORIES = 1000
MAX_CAS_ATTEMPTS = 4
MAX_POLLS = 30
POLL_DELAY = 10
TOKEN_ENV = "VPM_MIRROR_TOKEN"
PUBLICATION_RECEIPT = "publication.json"
CANONICAL_RECEIPT_URL = CANONICAL_BASE + PUBLICATION_RECEIPT
OFFICIAL_RECEIPT_URL = OFFICIAL_BASE + PUBLICATION_RECEIPT
PUBLISHER_REPOSITORY = "VR-Vlog/VR-Vlog-lilToon-Exporter"
PUBLISHER_WORKFLOW = ".github/workflows/build-listing.yml"
PREFLIGHT_FILENAME = "vpm-mirror-preflight.json"
ROOT = Path(__file__).resolve().parents[1]


class SyncError(ValueError):
    """A safe message suitable for CI output, without response bodies or secrets."""


class ApiError(SyncError):
    pass


class CommitUncertain(ApiError):
    """The mutation may have committed; read the current tree before retrying."""


class Target(NamedTuple):
    repository: str
    prefix: str
    listing_url: str
    branch: str = "main"

    @property
    def listing_path(self):
        return self.prefix + "index.json"


OFFICIAL = Target("mitsuya0077/vr-vlog-website", "public/exporter/", OFFICIAL_URL)
LEGACY = Target("mitsuya0077/mitsuya0077.github.io", "VR-Vlog-lilToon-Exporter/", LEGACY_URL)
TARGETS = (OFFICIAL, LEGACY)


class Entry(NamedTuple):
    oid: str
    mode: str = "100644"


class Snapshot(NamedTuple):
    head: str
    entries: dict
    listing: Optional[bytes]
    receipt: Optional[bytes] = None


class Changes(NamedTuple):
    additions: dict
    deletions: tuple


class Budget:
    def __init__(self, seconds=600, *, clock=time.monotonic):
        self.clock = clock
        self.deadline = clock() + seconds

    def remaining(self):
        remaining = self.deadline - self.clock()
        if remaining <= 0:
            raise SyncError("Mirror synchronization exceeded its time budget; retry the same publication")
        return remaining


def require(condition, message):
    if not condition:
        raise SyncError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON key")
        result[key] = value
    return result


def parse_json(raw, limit):
    require(isinstance(raw, bytes) and len(raw) <= limit, "JSON exceeds its size bound")
    try:
        return json.loads(raw, object_pairs_hook=unique_object,
                          parse_constant=lambda _: (_ for _ in ()).throw(SyncError("Non-finite JSON number")))
    except (UnicodeError, ValueError, RecursionError) as error:
        raise SyncError("Invalid JSON") from error


def parse_listing(raw, expected_url, *, allow_historical_urls=False):
    data = parse_json(raw, MAX_LISTING_BYTES)
    require(isinstance(data, dict) and data.get("id") == REPOSITORY_ID, "Unexpected repository identity")
    require(data.get("url") == expected_url, "Unexpected listing root URL")
    packages = data.get("packages")
    require(isinstance(packages, dict) and all(name in packages for name in
            (EXPORTER, "com.vrmc.gltf", "com.vrmc.vrm")), "Exporter or required dependencies are missing")
    for name, package in packages.items():
        require(isinstance(name, str) and re.fullmatch(r"[a-z0-9][a-z0-9._-]*", name), "Invalid package name")
        versions = package.get("versions") if isinstance(package, dict) else None
        require(isinstance(versions, dict) and versions, "Package versions are missing")
        for version, manifest in versions.items():
            require(isinstance(version, str) and re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]*", version), "Invalid package version")
            require(isinstance(manifest, dict) and manifest.get("name") == name
                    and manifest.get("version") == version, "Package identity mismatch")
            digest = manifest.get("zipSHA256")
            require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Missing ZIP checksum")
            url = manifest.get("url")
            require(isinstance(url, str), "Missing package ZIP URL")
            try:
                parsed = urllib.parse.urlsplit(url)
            except ValueError as error:
                raise SyncError("Invalid package ZIP URL") from error
            paths = (RELEASE_PATH, HISTORICAL_RELEASE_PATH) if allow_historical_urls else (RELEASE_PATH,)
            prefix = next((path for path in paths if parsed.path.startswith(path)), None)
            require(parsed.scheme == "https" and parsed.netloc == "github.com" and not parsed.query
                    and not parsed.fragment and prefix is not None, "Unexpected ZIP origin")
            suffix = parsed.path[len(prefix):].split("/")
            require(len(suffix) == 2 and suffix[0] and suffix[1] == f"{name}-{version}.zip", "Unexpected ZIP asset path")
            if name == EXPORTER:
                require(suffix[0] == f"v{version}", "Exporter release tag differs from its version")
    return data


def preserve_history(source, previous):
    if previous is None:
        return
    for name, package in previous["packages"].items():
        for version, old in package["versions"].items():
            current = source["packages"].get(name, {}).get("versions", {}).get(version)
            require(current is not None and current["zipSHA256"] == old["zipSHA256"],
                    "A published version would be removed or its ZIP checksum changed")


def render_listing(source, listing_url):
    require(listing_url in (OFFICIAL_URL, LEGACY_URL), "Unapproved mirror listing URL")
    data = copy.deepcopy(source)
    data["url"] = listing_url
    try:
        raw = (json.dumps(data, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")
    except (UnicodeError, TypeError, ValueError, RecursionError) as error:
        raise SyncError("Listing cannot be rendered as bounded UTF-8 JSON") from error
    require(len(raw) <= MAX_LISTING_BYTES, "Rendered listing exceeds its size bound")
    return raw


def safe_path(path):
    require(isinstance(path, str) and path and "\\" not in path and "\x00" not in path,
            "Invalid publication path")
    parts = path.split("/")
    require(all(part not in ("", ".", "..", ".git") and not any(ord(char) < 32 for char in part)
                for part in parts), "Invalid publication path")
    require(not PurePosixPath(path).is_absolute(), "Invalid absolute publication path")
    return path


def official_asset(path, raw):
    if Path(path).suffix.lower() not in (".html", ".js", ".css"):
        return raw
    try:
        text = raw.decode("utf-8")
    except UnicodeError as error:
        raise SyncError("Publication text is not UTF-8") from error
    require(not re.search(r"\{\{\s*listingInfo\b", text, re.I), "Publication contains unresolved listing tokens")
    text = text.replace(CANONICAL_BASE, OFFICIAL_BASE)
    encoded = re.escape(urllib.parse.quote(CANONICAL_BASE, safe=""))
    text = re.sub(encoded, lambda _: urllib.parse.quote(OFFICIAL_BASE, safe=""), text, flags=re.I)
    return text.encode("utf-8")


def validate_publisher(publisher):
    require(isinstance(publisher, dict) and set(publisher) ==
            {"repository", "workflow", "run_number", "run_id", "source_commit"}, "Invalid publication publisher context")
    require(publisher["repository"] == PUBLISHER_REPOSITORY and publisher["workflow"] == PUBLISHER_WORKFLOW,
            "Unexpected publication publisher identity")
    for key in ("run_number", "run_id"):
        require(type(publisher[key]) is int and 0 < publisher[key] < 2 ** 63, "Invalid publication run identity")
    require(isinstance(publisher["source_commit"], str) and re.fullmatch(r"[0-9a-f]{40}", publisher["source_commit"]),
            "Invalid publication source commit")
    return publisher


def publisher_from_environment(environment=None):
    environment = os.environ if environment is None else environment
    require(environment.get("GITHUB_ACTIONS") == "true" and environment.get("GITHUB_REPOSITORY") == PUBLISHER_REPOSITORY,
            "Mirror publication requires the approved GitHub Actions repository context")
    event = environment.get("GITHUB_EVENT_NAME")
    ref = environment.get("GITHUB_REF")
    allowed_ref = event == "workflow_dispatch" and ref == "refs/heads/main"
    allowed_ref |= event == "release" and isinstance(ref, str) and bool(re.fullmatch(r"refs/tags/v[0-9A-Za-z][0-9A-Za-z.+-]*", ref))
    require(allowed_ref and environment.get("GITHUB_WORKFLOW_REF") ==
            f"{PUBLISHER_REPOSITORY}/{PUBLISHER_WORKFLOW}@{ref}", "Unexpected publication workflow or ref")
    numbers = {}
    for field, variable in (("run_number", "GITHUB_RUN_NUMBER"), ("run_id", "GITHUB_RUN_ID")):
        value = environment.get(variable)
        require(isinstance(value, str) and re.fullmatch(r"[1-9][0-9]{0,18}", value), "Missing or invalid publication run context")
        numbers[field] = int(value)
    return validate_publisher({"repository": PUBLISHER_REPOSITORY, "workflow": PUBLISHER_WORKFLOW,
                               **numbers, "source_commit": environment.get("GITHUB_SHA")})


def validate_checkout(publisher):
    try:
        result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, check=True,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, timeout=15)
    except (OSError, subprocess.SubprocessError) as error:
        raise SyncError("Publication source checkout could not be verified") from None
    require(result.stdout.strip() == publisher["source_commit"], "Publication source checkout differs from its GitHub run")


def preserve_publisher_order(candidate, previous):
    validate_publisher(candidate)
    if previous is None:
        return
    validate_publisher(previous)
    require(candidate["run_number"] >= previous["run_number"], "Older publication run cannot replace the official site")
    if candidate["run_number"] == previous["run_number"]:
        require(candidate == previous, "Equal publication sequence has a different run identity or source commit")
    else:
        require(candidate["run_id"] != previous["run_id"], "Different publication sequences have the same run identity")


def preserve_publication_order(candidate, previous):
    preserve_publisher_order(candidate["publisher"], None if previous is None else previous["publisher"])
    if previous is not None and candidate["publisher"]["run_number"] == previous["publisher"]["run_number"]:
        require(candidate == previous, "Equal publication sequence has a different full snapshot")


def save_preflight_evidence(evidence):
    runner_temp = os.environ.get("RUNNER_TEMP")
    require(isinstance(runner_temp, str) and runner_temp, "Missing publication runner temporary directory")
    directory = Path(runner_temp)
    require(directory.is_absolute() and directory.is_dir() and not directory.is_symlink(),
            "Publication runner temporary directory is unavailable")
    target = directory / PREFLIGHT_FILENAME
    require(not target.is_symlink() and (not target.exists() or stat.S_ISREG(target.lstat().st_mode)),
            "Preflight evidence path is not a regular task file")
    if target.exists() and hasattr(os, "getuid"):
        require(target.lstat().st_uid == os.getuid(), "Preflight evidence file is not owned by this task user")
    raw = (json.dumps(evidence, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False) + "\n").encode("utf-8")
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=directory, prefix=".vpm-mirror-preflight-", delete=False) as output:
            temporary = Path(output.name)
            output.write(raw)
            output.flush()
            os.fsync(output.fileno())
        os.chmod(temporary, 0o600)
        require(not target.is_symlink() and (not target.exists() or stat.S_ISREG(target.lstat().st_mode)),
                "Preflight evidence path changed during preparation")
        os.replace(temporary, target)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink()


def load_producer_tools():
    # Runtime-only import: the producer also loads this module for its fixed
    # Git reader, so neither module imports the other while being defined.
    try:
        spec = importlib.util.spec_from_file_location("exporter_producer_proof", Path(__file__).with_name("prepare-exporter-publication.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    except Exception:
        raise SyncError("Publication proof helper could not be loaded") from None


def read_preflight_evidence(publisher, environment=None):
    environment = os.environ if environment is None else environment
    try:
        return load_producer_tools().read_preflight(environment, publisher)
    except Exception:
        raise SyncError("Publication preflight evidence could not be verified") from None


def parse_publication_receipt(raw):
    receipt = parse_json(raw, MAX_LISTING_BYTES)
    require(isinstance(receipt, dict) and set(receipt) == {"schema", "id", "publisher", "files", "digest"}
            and type(receipt.get("schema")) is int and receipt["schema"] == 2
            and receipt.get("id") == REPOSITORY_ID, "Unexpected publication receipt identity or schema")
    validate_publisher(receipt["publisher"])
    files = receipt.get("files")
    require(isinstance(files, dict) and len(files) < MAX_FILES
            and all(path in files for path in ("index.html", "index.json"))
            and PUBLICATION_RECEIPT not in files, "Invalid publication receipt file map")
    for path, digest in files.items():
        safe_path(path)
        require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Invalid publication file checksum")
    digest = receipt.get("digest")
    require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Invalid publication receipt digest")
    payload = {key: receipt[key] for key in ("schema", "id", "publisher", "files")}
    try:
        canonical = json.dumps(payload, ensure_ascii=False, sort_keys=True,
                               separators=(",", ":"), allow_nan=False).encode("utf-8")
    except (UnicodeError, ValueError, TypeError, RecursionError) as error:
        raise SyncError("Invalid publication receipt encoding") from error
    require(hashlib.sha256(canonical).hexdigest() == digest, "Publication receipt digest differs")
    return receipt


def validate_publication_receipt(files):
    require(PUBLICATION_RECEIPT in files, "Built publication requires its full-snapshot receipt")
    receipt = parse_publication_receipt(files[PUBLICATION_RECEIPT])
    actual = {path: hashlib.sha256(raw).hexdigest() for path, raw in files.items() if path != PUBLICATION_RECEIPT}
    require(receipt["files"] == actual, "Publication receipt differs from the full built snapshot")
    return receipt


def read_publication(root):
    root = Path(root)
    require(root.is_dir() and not root.is_symlink(), "Use the built Website publication directory")
    files = {}
    total = 0
    directory_count = 0

    def walk_error(error):
        raise SyncError("Publication directory could not be read") from error

    for folder, directories, filenames in os.walk(root, followlinks=False, onerror=walk_error):
        directory_count += 1
        require(directory_count <= MAX_DIRECTORIES, "Publication exceeds its directory-count bound")
        require(not Path(folder).is_symlink(), "Publication contains a directory symlink")
        for name in directories:
            safe_path((Path(folder) / name).relative_to(root).as_posix())
            require(not (Path(folder) / name).is_symlink(), "Publication contains a directory symlink")
        for name in filenames:
            path = Path(folder) / name
            require(stat.S_ISREG(path.lstat().st_mode), "Publication contains a non-regular file")
            relative = safe_path(path.relative_to(root).as_posix())
            # Pin the final component, reject a raced-in special file, and bound
            # the actual read even if the file grows after its metadata check.
            descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            with os.fdopen(descriptor, "rb") as stream:
                require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), "Publication contains a non-regular file")
                raw = stream.read(MAX_FILE_BYTES + 1)
            require(len(raw) <= MAX_FILE_BYTES, "Publication file exceeds its size bound")
            total += len(raw)
            require(total <= MAX_PUBLICATION_BYTES and len(files) < MAX_FILES, "Publication exceeds its size or file-count bound")
            files[relative] = raw
    require("index.html" in files and "index.json" in files, "Built publication requires index.html and index.json")
    validate_publication_receipt(files)
    source = parse_listing(files["index.json"], CANONICAL_URL)
    # Validate the actual rendered page before contacting any remote repository.
    assets = {path: official_asset(path, raw) for path, raw in files.items() if path != "index.json"}
    official = {OFFICIAL.prefix + path: raw for path, raw in assets.items()}
    official[OFFICIAL.listing_path] = render_listing(source, OFFICIAL_URL)
    legacy = {LEGACY.listing_path: render_listing(source, LEGACY_URL)}
    return source, {OFFICIAL.repository: official, LEGACY.repository: legacy}


def git_blob_oid(raw):
    return hashlib.sha1(f"blob {len(raw)}\0".encode("ascii") + raw).hexdigest()


def in_scope(target, path):
    return path.startswith(OFFICIAL.prefix) if target == OFFICIAL else path == LEGACY.listing_path


def plan_changes(target, snapshot, desired, source):
    require(target in TARGETS, "Unapproved publication target")
    require(target.listing_path in desired, "Mirror listing is absent from desired files")
    require(matches_snapshot(parse_listing(desired[target.listing_path], target.listing_url), source,
                             target.listing_url), "Desired mirror listing differs from the canonical snapshot")
    for path in desired:
        safe_path(path)
        require(in_scope(target, path), "Change exceeds the approved mirror path")
        require(isinstance(desired[path], bytes) and len(desired[path]) <= MAX_FILE_BYTES,
                "Desired publication file exceeds its size bound")
    require(sum(map(len, desired.values())) <= MAX_PUBLICATION_BYTES and len(desired) <= MAX_FILES,
            "Desired publication exceeds its size or file-count bound")
    if target == OFFICIAL:
        receipt_path = target.prefix + PUBLICATION_RECEIPT
        require(receipt_path in desired, "Official publication receipt is absent")
        candidate = parse_publication_receipt(desired[receipt_path])
        require(receipt_path not in snapshot.entries or snapshot.receipt is not None,
                "Official publication receipt blob is absent")
        previous_receipt = None if snapshot.receipt is None else parse_publication_receipt(snapshot.receipt)
        preserve_publication_order(candidate, previous_receipt)
    for path, entry in snapshot.entries.items():
        safe_path(path)
        require(in_scope(target, path) and entry.mode in ("100644", "100755"), "Unexpected mirror tree entry")
    previous = None if snapshot.listing is None else parse_listing(
        snapshot.listing, target.listing_url, allow_historical_urls=target == LEGACY)
    preserve_history(source, previous)
    additions = {path: raw for path, raw in desired.items()
                 if path not in snapshot.entries or snapshot.entries[path].oid != git_blob_oid(raw)}
    deletions = tuple(sorted(set(snapshot.entries) - set(desired)))
    require(len(additions) + len(deletions) <= MAX_FILES, "Mirror changes exceed the file-count bound")
    return Changes(additions, deletions)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def read_response(request, limit, budget, *, opener=None):
    opener = opener or urllib.request.build_opener(NoRedirect())
    try:
        with opener.open(request, timeout=min(15, budget.remaining())) as response:
            require(response.status == 200 and response.url == request.full_url, "Unexpected HTTP response or redirect")
            chunks = []
            length = 0
            # read1 returns available data rather than waiting for the entire
            # bound, allowing a slow response to consume the shared deadline.
            read = getattr(response, "read1", response.read)
            while length <= limit:
                budget.remaining()
                chunk = read(min(64 * 1024, limit + 1 - length))
                budget.remaining()
                if not chunk:
                    break
                chunks.append(chunk)
                length += len(chunk)
            raw = b"".join(chunks)
            require(len(raw) <= limit, "HTTP response exceeds its size bound")
            return raw, response.headers
    except urllib.error.HTTPError as error:
        raise ApiError(f"HTTP request failed (status {error.code})") from None
    except (OSError, urllib.error.URLError) as error:
        raise ApiError("HTTP request failed") from None


def fetch_listing(url, budget):
    require(url in (CANONICAL_URL, OFFICIAL_URL, LEGACY_URL), "Unapproved public endpoint")
    request = urllib.request.Request(url, headers={"Accept": "application/json", "Cache-Control": "no-cache",
                                                  "User-Agent": "VRVlog-VPM-mirror-sync"})
    raw, headers = read_response(request, MAX_LISTING_BYTES, budget)
    require(headers.get_content_type() == "application/json", "Public endpoint did not return JSON")
    return parse_listing(raw, url)


def fetch_publication_receipt(url, budget):
    # The query is unique for every observation, including CAS retries. This is
    # public evidence and carries no credential; it never changes the origin.
    require(url in (CANONICAL_RECEIPT_URL, OFFICIAL_RECEIPT_URL), "Unapproved publication receipt endpoint")
    url += "?mirror-check=" + uuid.uuid4().hex
    request = urllib.request.Request(url, headers={"Accept": "application/json", "Cache-Control": "no-cache, no-store",
                                                  "User-Agent": "VRVlog-VPM-mirror-sync"})
    raw, headers = read_response(request, MAX_LISTING_BYTES, budget)
    require(headers.get_content_type() == "application/json", "Public publication receipt did not return JSON")
    return parse_publication_receipt(raw)


def matches_snapshot(listing, source, url):
    expected = copy.deepcopy(source)
    expected["url"] = url
    return listing == expected


def wait_for_endpoints(source, urls, fetch, budget, *, sleep=time.sleep, freshness=None):
    for attempt in range(MAX_POLLS):
        if freshness is not None:
            freshness()
        matched = True
        for url in urls:
            budget.remaining()
            try:
                matched &= matches_snapshot(fetch(url, budget), source, url)
            except (SyncError, OSError):
                matched = False
        if matched:
            return
        if attempt + 1 < MAX_POLLS:
            sleep(min(POLL_DELAY, budget.remaining()))
    raise SyncError("Public VPM endpoints did not match the publication within the polling bound; retry the same publication")


def require_current_source(source, expected_receipt, fetch, receipt_fetch, budget):
    require(matches_snapshot(fetch(CANONICAL_URL, budget), source, CANONICAL_URL),
            "Canonical publication changed; rerun synchronization for the current canonical snapshot")
    require_current_receipt(expected_receipt, receipt_fetch, budget)


def require_current_receipt(expected_receipt, receipt_fetch, budget):
    require(receipt_fetch(CANONICAL_RECEIPT_URL, budget) == expected_receipt,
            "Canonical full publication changed; rerun synchronization for its current page and assets")


def wait_for_initial_source(source, expected_receipt, fetch, receipt_fetch, budget, *, sleep=time.sleep):
    for attempt in range(MAX_POLLS):
        budget.remaining()
        try:
            matched = matches_snapshot(fetch(CANONICAL_URL, budget), source, CANONICAL_URL)
            matched &= receipt_fetch(CANONICAL_RECEIPT_URL, budget) == expected_receipt
            if matched:
                return
        except (SyncError, OSError):
            pass
        if attempt + 1 < MAX_POLLS:
            sleep(min(POLL_DELAY, budget.remaining()))
    raise SyncError("Canonical full publication did not match the snapshot within the polling bound; retry its current publication")


def wait_for_official_receipt(source, expected_receipt, fetch, receipt_fetch, budget, *, sleep=time.sleep):
    for attempt in range(MAX_POLLS):
        # After observing the expected canonical receipt, never wait for that
        # source to rewind: a later full-site publication invalidates this run.
        require_current_source(source, expected_receipt, fetch, receipt_fetch, budget)
        try:
            if receipt_fetch(OFFICIAL_RECEIPT_URL, budget) == expected_receipt:
                return
        except (SyncError, OSError):
            pass
        if attempt + 1 < MAX_POLLS:
            sleep(min(POLL_DELAY, budget.remaining()))
    raise SyncError("Official full publication did not match its receipt within the polling bound; retry the same publication")


class GitHubApi:
    def __init__(self, token, budget, *, opener=None):
        require(isinstance(token, str) and re.fullmatch(r"[A-Za-z0-9_.-]+", token), "Missing or invalid mirror credential")
        self.token = token
        self.budget = budget
        self.opener = opener

    def request(self, path, body=None):
        allowed = path == "/graphql"
        if isinstance(path, str):
            for target in TARGETS:
                base = "/repos/" + target.repository
                allowed |= path in (base, base + "/git/ref/heads/main") or bool(re.fullmatch(
                    re.escape(base) + r"/git/(?:commits|blobs)/[0-9a-f]{40}", path)) or bool(re.fullmatch(
                    re.escape(base) + r"/git/trees/[0-9a-f]{40}\?recursive=1", path))
        require(allowed and (path == "/graphql" or body is None), "Unapproved GitHub API route")
        request = urllib.request.Request("https://api.github.com" + path,
            data=None if body is None else json.dumps(body).encode("utf-8"),
            headers={"Accept": "application/vnd.github+json", "Authorization": "Bearer " + self.token,
                     "Content-Type": "application/json", "X-GitHub-Api-Version": "2022-11-28",
                     "User-Agent": "VRVlog-VPM-mirror-sync"})
        raw, _ = read_response(request, MAX_API_BYTES, self.budget, opener=self.opener)
        return parse_json(raw, MAX_API_BYTES)

    def preflight(self, publisher):
        validate_publisher(publisher)
        previous = None
        for target in TARGETS:
            metadata = self.request("/repos/" + target.repository)
            name = metadata.get("full_name") if isinstance(metadata, dict) else None
            require(isinstance(name, str) and name.lower() == target.repository.lower()
                    and metadata.get("default_branch") == target.branch and metadata.get("archived") is False,
                    "Mirror repository is unavailable or its default branch changed")
            permissions = metadata.get("permissions")
            require(isinstance(permissions, dict) and permissions.get("push") is True,
                    "Mirror credential does not have repository write access")
            snapshot = self.snapshot(target)
            if target == OFFICIAL and snapshot.receipt is not None:
                receipt = parse_publication_receipt(snapshot.receipt)
                preserve_publisher_order(publisher, receipt["publisher"])
                previous = {"publisher": receipt["publisher"], "digest": receipt["digest"]}
        evidence = {"schema": 1, "publisher": publisher, "official": previous}
        report = {"status": "passed", "repositories": [target.repository for target in TARGETS]}
        return report, evidence

    def snapshot(self, target):
        require(target in TARGETS, "Unapproved GitHub repository")
        base = "/repos/" + target.repository
        ref = self.request(base + "/git/ref/heads/" + target.branch)
        obj = ref.get("object") if isinstance(ref, dict) else None
        head = obj.get("sha") if isinstance(obj, dict) else None
        require(isinstance(head, str) and re.fullmatch(r"[0-9a-f]{40}", head)
                and obj.get("type") == "commit", "Invalid target branch head")
        commit = self.request(base + "/git/commits/" + head)
        obj = commit.get("tree") if isinstance(commit, dict) else None
        tree_oid = obj.get("sha") if isinstance(obj, dict) else None
        require(isinstance(tree_oid, str) and re.fullmatch(r"[0-9a-f]{40}", tree_oid), "Invalid target tree")
        tree = self.request(base + "/git/trees/" + tree_oid + "?recursive=1")
        require(isinstance(tree, dict) and tree.get("truncated") is False
                and isinstance(tree.get("tree"), list), "Target tree is incomplete")
        entries = {}
        seen = set()
        ancestors = {str(parent) for parent in PurePosixPath(target.listing_path).parents if str(parent) != "."}
        for item in tree["tree"]:
            require(isinstance(item, dict), "Invalid target tree entry")
            path = safe_path(item.get("path"))
            require(path not in seen, "Duplicate target tree path")
            seen.add(path)
            if path in ancestors:
                require(item.get("type") == "tree" and item.get("mode") == "040000", "Mirror ancestor is not a directory")
            if not in_scope(target, path):
                continue
            if item.get("type") == "tree":
                require(item.get("mode") == "040000", "Invalid mirror directory mode")
                continue
            require(item.get("type") == "blob" and item.get("mode") in ("100644", "100755"), "Mirror contains a non-regular Git entry")
            oid = item.get("sha")
            require(isinstance(oid, str) and re.fullmatch(r"[0-9a-f]{40}", oid), "Invalid mirror blob identity")
            entries[path] = Entry(oid, item["mode"])
            require(len(entries) <= MAX_FILES, "Mirror tree exceeds the file-count bound")
        def blob_contents(path):
            if path not in entries:
                return None
            oid = entries[path].oid
            blob = self.request(base + "/git/blobs/" + oid)
            require(isinstance(blob, dict) and blob.get("encoding") == "base64"
                    and isinstance(blob.get("content"), str), "Invalid mirror publication blob")
            try:
                raw = base64.b64decode("".join(blob["content"].split()), validate=True)
            except (ValueError, UnicodeError) as error:
                raise SyncError("Invalid mirror publication encoding") from error
            require(len(raw) <= MAX_LISTING_BYTES and git_blob_oid(raw) == oid, "Mirror publication blob identity differs")
            return raw
        listing = blob_contents(target.listing_path)
        receipt = blob_contents(OFFICIAL.prefix + PUBLICATION_RECEIPT) if target == OFFICIAL else None
        return Snapshot(head, entries, listing, receipt)

    def commit(self, target, expected_head, changes):
        require(target in TARGETS and isinstance(expected_head, str)
                and re.fullmatch(r"[0-9a-f]{40}", expected_head), "Unapproved GitHub commit target")
        require(changes.additions or changes.deletions, "Cannot create an empty mirror commit")
        require(len(changes.additions) + len(changes.deletions) <= MAX_FILES,
                "Mirror commit exceeds its file-count bound")
        require(all(isinstance(raw, bytes) and len(raw) <= MAX_FILE_BYTES for raw in changes.additions.values())
                and sum(map(len, changes.additions.values())) <= MAX_PUBLICATION_BYTES,
                "Mirror commit exceeds its size bound")
        for path in (*changes.additions, *changes.deletions):
            safe_path(path)
            require(in_scope(target, path), "Commit exceeds the approved mirror path")
        body = {"query": "mutation($input: CreateCommitOnBranchInput!) { createCommitOnBranch(input: $input) { commit { oid url } } }",
                "variables": {"input": {
                    "branch": {"repositoryNameWithOwner": target.repository, "branchName": target.branch},
                    "expectedHeadOid": expected_head,
                    "message": {"headline": "Synchronize approved VPM publication"},
                    "fileChanges": {
                        "additions": [{"path": path, "contents": base64.b64encode(raw).decode("ascii")}
                                      for path, raw in sorted(changes.additions.items())],
                        "deletions": [{"path": path} for path in changes.deletions]}}}}
        try:
            response = self.request("/graphql", body)
            if not isinstance(response, dict) or response.get("errors"):
                raise CommitUncertain("GitHub rejected the mirror commit; re-reading its branch before retry")
            data = response.get("data")
            mutation = data.get("createCommitOnBranch") if isinstance(data, dict) else None
            commit = mutation.get("commit") if isinstance(mutation, dict) else None
            require(isinstance(commit, dict), "Missing committed mirror identity")
            oid = commit.get("oid")
            require(isinstance(oid, str) and re.fullmatch(r"[0-9a-f]{40}", oid), "Invalid committed mirror identity")
            return oid
        except (ApiError, SyncError) as error:
            raise CommitUncertain("Mirror commit result was not confirmed; re-reading its branch before retry") from None


def synchronize(source, desired, api, fetch, budget, *, sleep=time.sleep, progress=lambda _: None,
                receipt_fetch=fetch_publication_receipt):
    require(isinstance(source, dict), "Missing canonical snapshot")
    try:
        raw_source = json.dumps(source, allow_nan=False).encode("utf-8")
    except (UnicodeError, TypeError, ValueError, RecursionError) as error:
        raise SyncError("Invalid canonical snapshot") from error
    parse_listing(raw_source, CANONICAL_URL)
    require(set(desired) == {target.repository for target in TARGETS}, "Unapproved desired repository set")
    receipt_path = OFFICIAL.prefix + PUBLICATION_RECEIPT
    require(receipt_path in desired[OFFICIAL.repository], "Official publication receipt is absent")
    expected_receipt = parse_publication_receipt(desired[OFFICIAL.repository][receipt_path])
    wait_for_initial_source(source, expected_receipt, fetch, receipt_fetch, budget, sleep=sleep)
    # Preflight both repositories before the first mutation. Partial failures can
    # still occur after that point; a retry compares trees and skips applied work.
    initial = {target: api.snapshot(target) for target in TARGETS}
    for target in TARGETS:
        plan_changes(target, initial[target], desired[target.repository], source)
    reports = []

    def require_official_order():
        current = api.snapshot(OFFICIAL)
        path = OFFICIAL.prefix + PUBLICATION_RECEIPT
        require(path in current.entries and current.receipt is not None, "Confirmed official publication receipt is absent")
        receipt = parse_publication_receipt(current.receipt)
        preserve_publication_order(expected_receipt, receipt)
        require(receipt == expected_receipt, "Confirmed official publication no longer matches this snapshot")

    for target in TARGETS:
        snapshot = initial[target]
        attempted = False

        def record(oid, changed):
            report = {"repository": target.repository, "commit": oid, "changed": changed,
                      "commitUrl": f"https://github.com/{target.repository}/commit/{oid}"}
            reports.append(report)
            progress(report)

        for attempt in range(MAX_CAS_ATTEMPTS):
            budget.remaining()
            require_current_source(source, expected_receipt, fetch, receipt_fetch, budget)
            if target == LEGACY:
                require_official_order()
            changes = plan_changes(target, snapshot, desired[target.repository], source)
            if not changes.additions and not changes.deletions:
                record(snapshot.head, None if attempted else False)
                break
            attempted = True
            try:
                oid = api.commit(target, snapshot.head, changes)
            except CommitUncertain:
                # Includes a stale expected head, transient errors, and a commit
                # accepted by GitHub whose response never reached this process.
                snapshot = api.snapshot(target)
                if attempt + 1 == MAX_CAS_ATTEMPTS:
                    remaining = plan_changes(target, snapshot, desired[target.repository], source)
                    if remaining.additions or remaining.deletions:
                        raise SyncError("Mirror commit was not confirmed within the retry bound; retry the same publication") from None
                    record(snapshot.head, None)
                continue
            record(oid, True)
            break
    # Canonical is last so its final observation is newer than both mirror reads.
    wait_for_endpoints(source, (OFFICIAL_URL, LEGACY_URL, CANONICAL_URL), fetch, budget, sleep=sleep,
                       freshness=lambda: require_current_receipt(expected_receipt, receipt_fetch, budget))
    wait_for_official_receipt(source, expected_receipt, fetch, receipt_fetch, budget, sleep=sleep)
    require_current_source(source, expected_receipt, fetch, receipt_fetch, budget)
    require_official_order()
    return {"status": "passed", "repositoryId": REPOSITORY_ID, "repositories": reports,
            "verifiedEndpoints": [CANONICAL_URL, OFFICIAL_URL, LEGACY_URL],
            "publicationDigest": expected_receipt["digest"],
            "verifiedPublicationReceipts": [CANONICAL_RECEIPT_URL, OFFICIAL_RECEIPT_URL]}


def verify_before_deployment(root, publisher, api, preflight):
    """Re-read approved Git trees after upload; neither publish nor poll URLs."""
    validate_publisher(publisher)
    source, desired = read_publication(root)
    receipt = parse_publication_receipt(desired[OFFICIAL.repository][OFFICIAL.prefix + PUBLICATION_RECEIPT])
    require(receipt["publisher"] == publisher, "Built publication belongs to a different GitHub run or source commit")
    try:
        cached = load_producer_tools().validate_preflight(preflight, publisher)
    except Exception:
        raise SyncError("Publication preflight evidence does not match this run") from None
    if cached is not None and cached["publisher"]["run_number"] == publisher["run_number"]:
        require(receipt["digest"] == cached["digest"], "Equal publication sequence differs from its preflight snapshot")
    # The proof is an observation from before building. Read both repositories
    # again here, immediately before deploy-pages, instead of reusing that state.
    # Official is read last, keeping the deployment anchor newer than the
    # legacy-history observation without claiming an atomic cross-repo read.
    snapshots = {target: api.snapshot(target) for target in reversed(TARGETS)}
    require(cached is None or snapshots[OFFICIAL].receipt is not None,
            "Previously observed official publication receipt is now missing")
    repositories = []
    for target in TARGETS:
        changes = plan_changes(target, snapshots[target], desired[target.repository], source)
        repositories.append({"repository": target.repository, "commit": snapshots[target].head,
                             "pendingFileChanges": len(changes.additions) + len(changes.deletions)})
    return {"status": "verified-before-deployment", "repositoryId": REPOSITORY_ID,
            "publicationDigest": receipt["digest"], "repositories": repositories}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--publication-root", type=Path,
                        help="Actual built Website directory from the just-published canonical deployment")
    mode.add_argument("--preflight", action="store_true",
                      help="Read-only credential and approved target-repository availability check")
    mode.add_argument("--verify-before-deployment", type=Path,
                      help="Read-only check of the full saved snapshot and fresh Git trees immediately before canonical deployment")
    args = parser.parse_args()
    try:
        publisher = publisher_from_environment()
        validate_checkout(publisher)
        budget = Budget()
        api = GitHubApi(os.environ.get(TOKEN_ENV), budget)
        if args.preflight:
            report, evidence = api.preflight(publisher)
            save_preflight_evidence(evidence)
        elif args.verify_before_deployment is not None:
            proof = read_preflight_evidence(publisher)
            report = verify_before_deployment(args.verify_before_deployment, publisher, api, proof)
        else:
            source, desired = read_publication(args.publication_root)
            receipt = parse_publication_receipt(desired[OFFICIAL.repository][OFFICIAL.prefix + PUBLICATION_RECEIPT])
            require(receipt["publisher"] == publisher, "Built publication belongs to a different GitHub run or source commit")
            report = synchronize(source, desired, api, fetch_listing, budget,
                                 progress=lambda entry: print(json.dumps({"event": "mirror-tree-confirmed", **entry}), flush=True))
    except (SyncError, OSError) as error:
        # Never print raw OS/network errors: they can contain URLs or credentials.
        message = str(error) if isinstance(error, SyncError) else "Local publication could not be read"
        parser.exit(1, f"Mirror synchronization failed: {message}. Applied commits are retained; retry is idempotent.\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
