#!/usr/bin/env python3
"""Synchronize the personal Pages VPM endpoint without adding a CI route."""
import argparse
import copy
from contextlib import contextmanager
import json
import os
from pathlib import Path
import re
import tempfile
import urllib.parse
import urllib.request

SOURCE_URL = "https://vr-vlog.github.io/VR-Vlog-Avatar-Exporter/index.json"
LEGACY_URL = "https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json"
REPOSITORY_ID = "com.vrvlog.packages"
EXPORTER = "com.vrvlog.liltoon-vrm-exporter"
RELEASE_PREFIX = "https://github.com/VR-Vlog/VR-Vlog-Avatar-Exporter/releases/download/"
PREVIOUS_RELEASE_PREFIX = "https://github.com/VR-Vlog/VR-Vlog-lilToon-Exporter/releases/download/"
HISTORICAL_RELEASE_PREFIX = "https://github.com/mitsuya0077/VR-Vlog-lilToon-Exporter/releases/download/"
RELEASE_PREFIXES = (RELEASE_PREFIX, PREVIOUS_RELEASE_PREFIX, HISTORICAL_RELEASE_PREFIX)
MAX_BYTES = 4 * 1024 * 1024


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def parse_listing(raw, expected_url, *, allow_historical_urls=False):
    # Retain keyword compatibility; fixed transfer/rename aliases are valid on
    # every endpoint because preserved versions can carry their original URLs.
    if len(raw) > MAX_BYTES:
        raise ValueError("Listing exceeds the size limit")
    data = json.loads(raw, object_pairs_hook=unique_object,
                      parse_constant=lambda _: (_ for _ in ()).throw(ValueError("Non-finite JSON number")))
    if not isinstance(data, dict) or data.get("id") != REPOSITORY_ID:
        raise ValueError("Unexpected repository identity")
    if data.get("url") != expected_url:
        raise ValueError("Unexpected listing URL")
    packages = data.get("packages")
    if not isinstance(packages, dict) or not all(
        name in packages for name in (EXPORTER, "com.vrmc.gltf", "com.vrmc.vrm")
    ):
        raise ValueError("Exporter or required dependencies are missing")
    for name, package in packages.items():
        if not isinstance(name, str) or not re.fullmatch(r"[a-z0-9][a-z0-9._-]*", name):
            raise ValueError("Invalid package name")
        versions = package.get("versions") if isinstance(package, dict) else None
        if not isinstance(versions, dict) or not versions:
            raise ValueError(f"Missing versions: {name}")
        for version, manifest in versions.items():
            if not isinstance(version, str) or not re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]*", version):
                raise ValueError("Invalid package version")
            if not isinstance(manifest, dict) or manifest.get("name") != name or manifest.get("version") != version:
                raise ValueError(f"Package identity mismatch: {name} {version}")
            if not isinstance(manifest.get("zipSHA256"), str) or not re.fullmatch(r"[0-9a-f]{64}", manifest["zipSHA256"]):
                raise ValueError(f"Missing ZIP checksum: {name} {version}")
            url = manifest.get("url")
            if not isinstance(url, str):
                raise ValueError(f"Unexpected ZIP origin: {name} {version}")
            parsed = urllib.parse.urlsplit(url)
            prefix = next((prefix for prefix in RELEASE_PREFIXES if url.startswith(prefix)), None)
            if parsed.scheme != "https" or parsed.netloc != "github.com" or parsed.query or parsed.fragment or prefix is None:
                raise ValueError(f"Unexpected ZIP origin: {name} {version}")
            suffix = url[len(prefix):].split("/")
            if len(suffix) != 2 or not suffix[0] or suffix[1] != f"{name}-{version}.zip":
                raise ValueError(f"Unexpected ZIP asset path: {name} {version}")
            if name == EXPORTER and suffix[0] != f"v{version}":
                raise ValueError("Exporter release tag differs from its version")
    return data


@contextmanager
def publication_lock(path):
    # Keep the inode stable. Unlinking an advisory lock permits another process
    # to lock a replacement inode while the first process still owns this one.
    if path.is_symlink():
        raise ValueError("Refusing to follow a lock-path symlink")
    with path.open("a+b") as handle:
        if os.name == "nt":
            import msvcrt
            handle.seek(0)
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
            try:
                yield
            finally:
                handle.seek(0)
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
            try:
                yield
            finally:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def make_legacy(source, previous=None):
    parse_listing(json.dumps(source, allow_nan=False).encode(), SOURCE_URL)
    if previous is not None:
        parse_listing(json.dumps(previous, allow_nan=False).encode(), LEGACY_URL)
        for name, package in previous["packages"].items():
            for version, old in package["versions"].items():
                current = source["packages"].get(name, {}).get("versions", {}).get(version)
                def comparable(manifest):
                    result = copy.deepcopy(manifest)
                    prefix = next(prefix for prefix in RELEASE_PREFIXES if result["url"].startswith(prefix))
                    result["url"] = result["url"][len(prefix):]
                    return result
                if current is None or comparable(current) != comparable(old):
                    raise ValueError(f"Published version removed or manifest changed: {name} {version}")
    legacy = copy.deepcopy(source)
    legacy["url"] = LEGACY_URL
    return (json.dumps(legacy, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def download_listing():
    request = urllib.request.Request(SOURCE_URL, headers={"Accept": "application/json"})
    with urllib.request.build_opener(NoRedirect()).open(request, timeout=30) as response:
        if response.status != 200 or response.url != SOURCE_URL:
            raise ValueError("Unexpected source response or redirect")
        if response.headers.get_content_type() != "application/json":
            raise ValueError("Source did not return JSON")
        return parse_listing(response.read(MAX_BYTES + 1), SOURCE_URL)


def synchronize(site_root, source):
    site_root = Path(site_root)
    if not site_root.is_dir() or not (site_root / ".nojekyll").is_file():
        raise ValueError("Use the existing static personal Pages checkout")
    directory = site_root / "VR-Vlog-lilToon-Exporter"
    target = directory / "index.json"
    if directory.is_symlink() or target.is_symlink():
        raise ValueError("Refusing to follow a compatibility-path symlink")
    directory.mkdir(exist_ok=True)
    # OS-managed locks release even when a synchronizer crashes or is killed.
    with publication_lock(site_root / ".sync-legacy-listing.lock"):
        before = target.read_bytes() if target.exists() else None
        previous = parse_listing(before, LEGACY_URL, allow_historical_urls=True) if before is not None else None
        contents = make_legacy(source, previous)
        if before == contents:
            return False
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(dir=directory, prefix=".index-", delete=False) as output:
                temporary = Path(output.name)
                output.write(contents)
                output.flush()
                os.fsync(output.fileno())
            os.chmod(temporary, 0o644)
            if target.is_symlink() or (target.read_bytes() if target.exists() else None) != before:
                raise ValueError("Compatibility listing changed during synchronization")
            os.replace(temporary, target)
            temporary = None
            return True
        finally:
            if temporary is not None:
                temporary.unlink()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--site-root", required=True, type=Path,
                        help="Existing mitsuya0077.github.io checkout; edits only the legacy index.json")
    args = parser.parse_args()
    try:
        changed = synchronize(args.site_root, download_listing())
    except (OSError, ValueError) as error:
        parser.exit(1, f"Synchronization failed; existing listing preserved: {error}\n")
    print("Legacy listing updated; review and publish its Pages PR." if changed else "Legacy listing already matches.")


if __name__ == "__main__":
    main()
