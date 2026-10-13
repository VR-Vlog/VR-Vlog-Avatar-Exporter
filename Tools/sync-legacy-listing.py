#!/usr/bin/env python3
"""Synchronize the personal Pages VPM endpoint without adding a CI route."""
import argparse
import copy
import json
import os
from pathlib import Path
import re
import tempfile
import urllib.request

SOURCE_URL = "https://vr-vlog.github.io/VR-Vlog-lilToon-Exporter/index.json"
LEGACY_URL = "https://mitsuya0077.github.io/VR-Vlog-lilToon-Exporter/index.json"
REPOSITORY_ID = "com.vrvlog.packages"
EXPORTER = "com.vrvlog.liltoon-vrm-exporter"
RELEASE_PREFIX = "https://github.com/VR-Vlog/VR-Vlog-lilToon-Exporter/releases/download/"
MAX_BYTES = 4 * 1024 * 1024


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate JSON key: {key}")
        result[key] = value
    return result


def parse_listing(raw, expected_url):
    if len(raw) > MAX_BYTES:
        raise ValueError("Listing exceeds the size limit")
    data = json.loads(raw, object_pairs_hook=unique_object)
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
        versions = package.get("versions") if isinstance(package, dict) else None
        if not isinstance(versions, dict) or not versions:
            raise ValueError(f"Missing versions: {name}")
        for version, manifest in versions.items():
            if not isinstance(manifest, dict) or manifest.get("name") != name or manifest.get("version") != version:
                raise ValueError(f"Package identity mismatch: {name} {version}")
            if not re.fullmatch(r"[0-9a-f]{64}", str(manifest.get("zipSHA256", ""))):
                raise ValueError(f"Missing ZIP checksum: {name} {version}")
            url = manifest.get("url")
            if not isinstance(url, str) or not url.startswith(RELEASE_PREFIX):
                raise ValueError(f"Unexpected ZIP origin: {name} {version}")
    return data


def make_legacy(source, previous=None):
    if previous is not None:
        for name, package in previous["packages"].items():
            for version, old in package["versions"].items():
                current = source["packages"].get(name, {}).get("versions", {}).get(version)
                if current is None or current["zipSHA256"] != old["zipSHA256"]:
                    raise ValueError(f"Published version removed or ZIP changed: {name} {version}")
    legacy = copy.deepcopy(source)
    legacy["url"] = LEGACY_URL
    return (json.dumps(legacy, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def download_listing():
    request = urllib.request.Request(SOURCE_URL, headers={"Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
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
    # Fail on concurrent invocations instead of overwriting another sync.
    lock = site_root / ".sync-legacy-listing.lock"
    lock_fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
    temporary = None
    try:
        os.close(lock_fd)
        before = target.read_bytes() if target.exists() else None
        previous = parse_listing(before, LEGACY_URL) if before is not None else None
        contents = make_legacy(source, previous)
        if before == contents:
            return False
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
        lock.unlink()


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
