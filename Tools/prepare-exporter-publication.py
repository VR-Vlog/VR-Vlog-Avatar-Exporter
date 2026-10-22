#!/usr/bin/env python3
"""Preserve the built exporter site before replacing its canonical HTML with a redirect.

The saved snapshot retains the canonical listing URL and is the input to
sync-vpm-mirrors.py. The mirror tool rewrites the official site's URLs.
"""
import argparse
import hashlib
from html.parser import HTMLParser
import importlib.util
import json
import math
import os
from pathlib import Path
import re
import shutil
import stat
import tempfile
from urllib.parse import parse_qs, urlsplit

CANONICAL_URL = "https://vr-vlog.github.io/VR-Vlog-Avatar-Exporter/index.json"
OFFICIAL_BASE = "https://vrvlog.fun/exporter/"
REPOSITORY_ID = "com.vrvlog.packages"
PUBLICATION_RECEIPT = "publication.json"
PUBLISHER_REPOSITORY = "VR-Vlog/VR-Vlog-Avatar-Exporter"
LEGACY_PUBLISHER_REPOSITORY = "VR-Vlog/VR-Vlog-lilToon-Exporter"
PUBLISHER_REPOSITORY_ID = "1355769104"
PUBLISHER_WORKFLOW = ".github/workflows/build-listing.yml"
ZIP_REPOSITORIES = (PUBLISHER_REPOSITORY, LEGACY_PUBLISHER_REPOSITORY,
                    "mitsuya0077/VR-Vlog-lilToon-Exporter")
PREFLIGHT_FILE = "vpm-mirror-preflight.json"
# UniVRM is bundled inside the exporter; earlier dependency packages stay only as published history.
REQUIRED_PACKAGES = ("com.vrvlog.liltoon-vrm-exporter",)
MAX_FILE_BYTES = 4 * 1024 * 1024
MAX_PUBLICATION_BYTES = 8 * 1024 * 1024
MAX_FILES = 1000
MAX_DIRECTORIES = 1000
TEMPLATE_TOKEN = re.compile(r"\{\{[^\r\n]*?\}\}|\{\{\s*[-~]?\s*(?:listingInfo|packages?|for|end)\b", re.I)
LISTING_TOKEN_BYTES = re.compile(rb"\{\{\s*[-~]?\s*(?:listingInfo|packages?|for|end)\b", re.I)


class PreparationError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise PreparationError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Listing contains duplicate JSON keys")
        result[key] = value
    return result


def safe_path(relative):
    require(isinstance(relative, str) and relative and not relative.startswith("/")
            and all(part not in ("", ".", "..", ".git") and "\\" not in part
                    and not any(ord(char) < 32 for char in part) for part in relative.split("/")),
            "Invalid publication path")
    return relative


def receipt_digest(payload):
    raw = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":"),
                     allow_nan=False).encode("utf-8")
    return hashlib.sha256(raw).hexdigest()


def validate_publisher(publisher, *, allow_legacy=False):
    repositories = (PUBLISHER_REPOSITORY, LEGACY_PUBLISHER_REPOSITORY) if allow_legacy else (PUBLISHER_REPOSITORY,)
    require(isinstance(publisher, dict) and set(publisher) ==
            {"repository", "workflow", "run_number", "run_id", "source_commit"}
            and publisher.get("repository") in repositories
            and publisher.get("workflow") == PUBLISHER_WORKFLOW,
            "Unexpected publication publisher identity")
    for key in ("run_number", "run_id"):
        require(type(publisher[key]) is int and 0 < publisher[key] < 2 ** 63,
                "Invalid publication publisher run identity")
    require(isinstance(publisher["source_commit"], str)
            and re.fullmatch(r"[0-9a-f]{40}", publisher["source_commit"]),
            "Invalid publication publisher source commit")
    return publisher


def publisher_from_environment(environment):
    repository = environment.get("GITHUB_REPOSITORY")
    workflow_ref = environment.get("GITHUB_WORKFLOW_REF")
    event = environment.get("GITHUB_EVENT_NAME")
    ref = environment.get("GITHUB_REF")
    require(environment.get("GITHUB_ACTIONS") == "true" and repository == PUBLISHER_REPOSITORY
            and environment.get("GITHUB_REPOSITORY_ID") == PUBLISHER_REPOSITORY_ID and isinstance(workflow_ref, str)
            and isinstance(ref, str) and workflow_ref == f"{repository}/{PUBLISHER_WORKFLOW}@{ref}",
            "Publication requires the fixed publisher workflow context")
    beta_tag = re.fullmatch(r"refs/tags/v(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)-beta\.(?:0|[1-9][0-9]*)", ref)
    require((event == "workflow_dispatch" and (ref == "refs/heads/main" or beta_tag))
            or (event == "release" and re.fullmatch(r"refs/tags/v[0-9A-Za-z][0-9A-Za-z.+-]*", ref)),
            "Publication requires main dispatch, an authenticated beta tag dispatch or a release tag context")
    publisher = {"repository": repository, "workflow": PUBLISHER_WORKFLOW,
                 "source_commit": environment.get("GITHUB_SHA")}
    for key, variable in (("run_number", "GITHUB_RUN_NUMBER"), ("run_id", "GITHUB_RUN_ID")):
        value = environment.get(variable)
        require(isinstance(value, str) and re.fullmatch(r"[1-9][0-9]{0,18}", value),
                "Publication requires a valid CI run identity")
        publisher[key] = int(value)
    return validate_publisher(publisher)


def validate_preflight(preflight, publisher):
    validate_publisher(publisher)
    require(isinstance(preflight, dict) and set(preflight) == {"schema", "publisher", "official"}
            and type(preflight.get("schema")) is int and preflight["schema"] == 1,
            "Publication requires a valid mirror preflight proof")
    require(validate_publisher(preflight["publisher"]) == publisher,
            "Mirror preflight proof belongs to a different publisher run")
    official = preflight["official"]
    if official is None:
        return None
    require(isinstance(official, dict) and set(official) == {"publisher", "digest"},
            "Invalid observed official publication context")
    previous = validate_publisher(official["publisher"], allow_legacy=True)
    require(isinstance(official["digest"], str) and re.fullmatch(r"[0-9a-f]{64}", official["digest"]),
            "Invalid observed official publication digest")
    require(publisher["run_number"] >= previous["run_number"],
            "An older publisher run cannot replace a newer official publication")
    if publisher["run_number"] == previous["run_number"]:
        require(publisher == previous, "Equal publisher sequence belongs to a different run or source commit")
    else:
        require(publisher["run_id"] != previous["run_id"],
                "A newer publisher sequence cannot reuse an earlier run identity")
    return official


def read_preflight(environment, publisher):
    runner_temp = environment.get("RUNNER_TEMP")
    require(isinstance(runner_temp, str) and runner_temp and Path(runner_temp).is_absolute(),
            "Publication requires the runner temporary directory")
    root = Path(runner_temp)
    require(root.is_dir() and not root.is_symlink(), "Runner temporary directory must be a real directory")
    path = root / PREFLIGHT_FILE
    require(path.exists() and stat.S_ISREG(path.lstat().st_mode), "Mirror preflight proof is missing or unsafe")
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, "rb") as stream:
        require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), "Mirror preflight proof is not a regular file")
        raw = stream.read(MAX_FILE_BYTES + 1)
    require(len(raw) <= MAX_FILE_BYTES, "Mirror preflight proof exceeds its size bound")
    def invalid_constant(_):
        raise PreparationError("Mirror preflight proof contains invalid JSON")
    try:
        proof = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object, parse_constant=invalid_constant)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise PreparationError("Mirror preflight proof is not valid UTF-8 JSON") from error
    validate_preflight(proof, publisher)
    return proof


def load_mirror_tools():
    try:
        spec = importlib.util.spec_from_file_location("exporter_mirror_api", Path(__file__).with_name("sync-vpm-mirrors.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    except Exception:
        raise PreparationError("Mirror API helper could not be loaded") from None


def official_reader_from_environment(environment, publisher):
    mirror = load_mirror_tools()
    def read_official():
        require(mirror.publisher_from_environment(environment) == publisher,
                "Publication publisher context changed after preflight")
        mirror.validate_checkout(publisher)
        api = mirror.GitHubApi.from_environment(environment, mirror.Budget(), require_org=False)
        return api.snapshot(mirror.OFFICIAL).receipt
    return read_official


def require_fresh_official(candidate, cached_official, reader):
    try:
        raw = reader()
    except Exception:
        # Readers may raise OS, network or API errors containing response bodies
        # or credentials. Only this fixed message is suitable for CLI output.
        raise PreparationError("Latest official publication could not be checked") from None
    if raw is None:
        require(cached_official is None, "Previously observed official publication receipt is now missing")
        return
    latest = parse_publication_receipt(raw)
    context = candidate["publisher"]
    observed = {"publisher": latest["publisher"], "digest": latest["digest"]}
    validate_preflight({"schema": 1, "publisher": context, "official": observed}, context)
    if context["run_number"] == latest["publisher"]["run_number"]:
        require(candidate == latest, "An equal publisher sequence cannot replace a different latest official publication")


def publication_receipt(files, publisher):
    validate_publisher(publisher)
    require(PUBLICATION_RECEIPT not in files, "Publication receipt cannot include itself")
    require("index.html" in files and "index.json" in files and len(files) < MAX_FILES,
            "Publication receipt requires a complete bounded snapshot")
    hashes = {}
    for relative, raw in sorted(files.items()):
        safe_path(relative)
        require(isinstance(raw, bytes), "Publication receipt requires file bytes")
        hashes[relative] = hashlib.sha256(raw).hexdigest()
    payload = {"schema": 2, "id": REPOSITORY_ID, "files": hashes, "publisher": dict(publisher)}
    receipt = dict(payload, digest=receipt_digest(payload))
    raw = (json.dumps(receipt, ensure_ascii=False, sort_keys=True, indent=2, allow_nan=False) + "\n").encode("utf-8")
    require(len(raw) <= MAX_FILE_BYTES, "Publication receipt exceeds its size bound")
    return raw


def parse_publication_receipt(raw):
    require(isinstance(raw, bytes) and len(raw) <= MAX_FILE_BYTES, "Publication receipt exceeds its size bound")
    def invalid_constant(_):
        raise PreparationError("Publication receipt contains a non-finite JSON value")
    try:
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object, parse_constant=invalid_constant)
    except (UnicodeError, ValueError, RecursionError) as error:
        raise PreparationError("Publication receipt is not valid UTF-8 JSON") from error
    require(isinstance(data, dict) and set(data) == {"schema", "id", "files", "publisher", "digest"}
            and type(data.get("schema")) is int and data["schema"] == 2 and data.get("id") == REPOSITORY_ID,
            "Unexpected publication receipt schema or identity")
    # Read receipts issued before this same repository was renamed without
    # normalizing their publisher name: the digest binds the original identity.
    validate_publisher(data["publisher"], allow_legacy=True)
    hashes = data["files"]
    require(isinstance(hashes, dict) and "index.html" in hashes and "index.json" in hashes
            and PUBLICATION_RECEIPT not in hashes and len(hashes) < MAX_FILES,
            "Publication receipt does not describe a complete bounded snapshot")
    for relative, digest in hashes.items():
        safe_path(relative)
        require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest),
                "Invalid publication receipt file hash")
    digest = data["digest"]
    require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest), "Invalid publication receipt digest")
    require(digest == receipt_digest({key: data[key] for key in ("schema", "id", "files", "publisher")}),
            "Publication receipt digest differs")
    return data


def validate_publication_receipt(files):
    require(PUBLICATION_RECEIPT in files, "Publication receipt is missing")
    receipt = parse_publication_receipt(files[PUBLICATION_RECEIPT])
    require(len(files) <= MAX_FILES, "Publication receipt exceeds its file-count bound")
    expected = {}
    for relative, raw in files.items():
        if relative == PUBLICATION_RECEIPT:
            continue
        safe_path(relative)
        require(isinstance(raw, bytes), "Publication receipt requires file bytes")
        expected[relative] = hashlib.sha256(raw).hexdigest()
    require(receipt["files"] == expected, "Publication receipt differs from the complete snapshot")
    return receipt


def validate_listing(raw):
    def invalid_constant(_):
        raise PreparationError("Listing contains a non-finite JSON value")
    def finite_float(value):
        number = float(value)
        require(math.isfinite(number), "Listing contains a non-finite JSON value")
        return number
    try:
        data = json.loads(raw.decode("utf-8"), object_pairs_hook=unique_object,
                          parse_constant=invalid_constant, parse_float=finite_float)
    except PreparationError:
        raise
    except (UnicodeError, ValueError, RecursionError) as error:
        raise PreparationError("Built listing is not valid UTF-8 JSON") from error
    require(isinstance(data, dict) and data.get("id") == REPOSITORY_ID,
            "Unexpected listing repository identity")
    require(data.get("url") == CANONICAL_URL, "Built listing must retain the canonical URL")
    packages = data.get("packages")
    require(isinstance(packages, dict) and all(name in packages for name in REQUIRED_PACKAGES),
            "Built listing is missing the exporter")
    for name, package in packages.items():
        require(isinstance(name, str) and re.fullmatch(r"[a-z0-9][a-z0-9._-]*", name),
                "Invalid listed package name")
        versions = package.get("versions") if isinstance(package, dict) else None
        require(isinstance(versions, dict) and versions, "Listed package versions are missing")
        for version, manifest in versions.items():
            require(isinstance(version, str) and re.fullmatch(r"[0-9A-Za-z][0-9A-Za-z.+-]*", version)
                    and isinstance(manifest, dict) and manifest.get("name") == name
                    and manifest.get("version") == version, "Listed package identity differs")
            digest = manifest.get("zipSHA256")
            require(isinstance(digest, str) and re.fullmatch(r"[0-9a-f]{64}", digest),
                    "Listed package is missing its ZIP checksum")
            url = manifest.get("url")
            require(isinstance(url, str), "Listed package is missing its ZIP URL")
            try:
                parsed = urlsplit(url)
            except ValueError as error:
                raise PreparationError("Invalid listed ZIP URL") from error
            prefix = next((f"/{repository}/releases/download/" for repository in ZIP_REPOSITORIES
                           if parsed.path.startswith(f"/{repository}/releases/download/")), None)
            require(parsed.scheme == "https" and parsed.netloc == "github.com"
                    and not parsed.query and not parsed.fragment and prefix is not None,
                    "Unexpected listed ZIP origin")
            suffix = parsed.path[len(prefix):].split("/")
            require(len(suffix) == 2 and suffix[0] and suffix[1] == f"{name}-{version}.zip",
                    "Unexpected listed ZIP asset path")
            if name == REQUIRED_PACKAGES[0]:
                require(suffix[0] == f"v{version}", "Exporter release tag differs from its version")
    return data


class RenderedPage(HTMLParser):
    def __init__(self):
        super().__init__()
        self.main = self.listing_field = self.vcc = self.refresh = False

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        self.main |= tag == "main" and attrs.get("id") == "main"
        self.listing_field |= tag == "input" and attrs.get("id") == "listing-url" and attrs.get("value") == CANONICAL_URL
        self.refresh |= tag == "meta" and attrs.get("http-equiv", "").lower() == "refresh"
        if tag == "a" and attrs.get("href", "").startswith("vcc:"):
            try:
                parsed = urlsplit(attrs["href"])
            except ValueError as error:
                raise PreparationError("Invalid VCC installation URL") from error
            self.vcc |= parsed.scheme == "vcc" and parsed.netloc == "vpm" and parsed.path == "/addRepo" \
                and parse_qs(parsed.query).get("url") == [CANONICAL_URL]


def read_snapshot(root, *, allow_stale_receipt=False):
    require(root.is_dir() and not root.is_symlink(), "Publication root must be a real directory")
    files = {}
    total = 0
    directory_count = 0
    def walk_error(error):
        raise PreparationError("Publication directory could not be read") from error
    for folder, directories, filenames in os.walk(root, followlinks=False, onerror=walk_error):
        directory_count += 1
        require(directory_count <= MAX_DIRECTORIES, "Publication exceeds its directory-count bound")
        require(not Path(folder).is_symlink(), "Publication contains a directory symlink")
        for name in directories:
            require(name != ".git" and "\\" not in name and not any(ord(char) < 32 for char in name),
                    "Invalid publication directory")
            require(not (Path(folder) / name).is_symlink(), "Publication contains a directory symlink")
        for name in filenames:
            path = Path(folder) / name
            relative = safe_path(path.relative_to(root).as_posix())
            require(stat.S_ISREG(path.lstat().st_mode), "Publication contains a non-regular file")
            # O_NOFOLLOW pins the final component; O_NONBLOCK avoids hanging on a
            # special file if an entry changes between validation and opening.
            descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
            with os.fdopen(descriptor, "rb") as stream:
                require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode), "Publication contains a non-regular file")
                raw = stream.read(MAX_FILE_BYTES + 1)
            require(len(raw) <= MAX_FILE_BYTES, "Publication file exceeds its size bound")
            require(not LISTING_TOKEN_BYTES.search(raw), "Publication contains unresolved template tokens")
            total += len(raw)
            require(total <= MAX_PUBLICATION_BYTES and len(files) < MAX_FILES,
                    "Publication exceeds its size or file-count bound")
            if path.suffix.lower() in (".html", ".js", ".css", ".json"):
                try:
                    text = raw.decode("utf-8")
                except UnicodeError as error:
                    raise PreparationError("Publication text is not UTF-8") from error
                require(not TEMPLATE_TOKEN.search(text), "Publication contains unresolved template tokens")
            files[relative] = raw
    require("index.html" in files and "index.json" in files, "Built publication requires index.html and index.json")
    validate_listing(files["index.json"])
    page = RenderedPage()
    page.feed(files["index.html"].decode("utf-8"))
    require(not page.refresh, "Publication is already a redirect; rebuild Website before retrying")
    require(page.main and page.listing_field and page.vcc, "Publication must contain the rendered exporter page")
    if PUBLICATION_RECEIPT in files:
        if allow_stale_receipt:
            # A rebuilt Website can retain a prior successful preparation's
            # well-formed receipt. Exclude it and bind the newly rendered files.
            parse_publication_receipt(files[PUBLICATION_RECEIPT])
        else:
            validate_publication_receipt(files)
    return files


def redirect_html():
    return f'''<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>VR Vlog lilToon Exporter</title>
  <link rel="canonical" href="{OFFICIAL_BASE}">
  <meta http-equiv="refresh" content="0; url={OFFICIAL_BASE}">
  <script>location.replace("{OFFICIAL_BASE}" + location.search + location.hash);</script>
</head>
<body>
  <h1>VR Vlog lilToon Exporter</h1>
  <p><a href="{OFFICIAL_BASE}">公式の導入ページを開く</a></p>
  <p>VCC／ALCOMの登録済みVPM URLは引き続き利用できます。登録変更や再インストールは不要です。</p>
  <p><a href="index.json">登録用のVPM一覧</a></p>
</body>
</html>
'''.encode("utf-8")


def prepare(publication_root, official_root, *, publisher, preflight, fresh_official_reader):
    require(callable(fresh_official_reader), "Publication requires a fresh official receipt reader")
    official = validate_preflight(preflight, publisher)
    source_arg, destination_arg = Path(publication_root).absolute(), Path(official_root).absolute()
    require(not source_arg.is_symlink() and not destination_arg.is_symlink(), "Publication roots cannot be symlinks")
    source, destination = source_arg.resolve(), destination_arg.resolve()
    require(source != destination and source not in destination.parents and destination not in source.parents,
            "Publication roots must be distinct and non-overlapping")
    files = read_snapshot(source, allow_stale_receipt=True)
    original_receipt = files.pop(PUBLICATION_RECEIPT, None)
    receipt = publication_receipt(files, publisher)
    if official is not None and official["publisher"]["run_number"] == publisher["run_number"]:
        require(parse_publication_receipt(receipt)["digest"] == official["digest"],
                "An equal publisher sequence cannot replace a different official publication digest")
    snapshot_files = dict(files, **{PUBLICATION_RECEIPT: receipt})
    require(not destination.exists() or destination.is_dir(), "Official snapshot root must be a directory")
    if destination.exists() and any(destination.iterdir()):
        read_snapshot(destination)
    # All input checks happen before creating or replacing either publication.
    destination.parent.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".exporter-publication-", dir=destination.parent))
    redirect_temp = None
    receipt_temp = None
    previous = staging / "previous"
    previous_receipt = staging / "previous-publication.json"
    snapshot = staging / "snapshot"
    installed = False
    receipt_installed = False
    completed = False
    rollback_failed = False
    try:
        snapshot.mkdir()
        for relative, raw in snapshot_files.items():
            path = snapshot / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(raw)
        require(read_snapshot(snapshot) == snapshot_files, "Staged snapshot bytes differ from the built publication")
        if original_receipt is not None:
            previous_receipt.write_bytes(original_receipt)
        # Read the latest official Git tree after validating/staging the full
        # candidate, before creating canonical temp files or replacing outputs.
        require_fresh_official(parse_publication_receipt(receipt), official, fresh_official_reader)
        with tempfile.NamedTemporaryFile(prefix=".exporter-redirect-", dir=source, delete=False) as stream:
            redirect_temp = Path(stream.name)
            stream.write(redirect_html())
        redirect_temp.chmod(0o644)
        with tempfile.NamedTemporaryFile(prefix=".exporter-receipt-", dir=source, delete=False) as stream:
            receipt_temp = Path(stream.name)
            stream.write(receipt)
        receipt_temp.chmod(0o644)
        if destination.exists():
            os.replace(destination, previous)
        try:
            os.replace(snapshot, destination)
            installed = True
            os.replace(receipt_temp, source / PUBLICATION_RECEIPT)
            receipt_installed = True
            os.replace(redirect_temp, source / "index.html")
            completed = True
        except OSError:
            try:
                if receipt_installed:
                    if original_receipt is None:
                        (source / PUBLICATION_RECEIPT).unlink()
                    else:
                        os.replace(previous_receipt, source / PUBLICATION_RECEIPT)
            except OSError:
                rollback_failed = True
                raise
            finally:
                try:
                    if installed:
                        shutil.rmtree(destination)
                    if previous.exists():
                        os.replace(previous, destination)
                except OSError:
                    rollback_failed = True
                    raise
            raise
    finally:
        if redirect_temp is not None:
            redirect_temp.unlink(missing_ok=True)
        if receipt_temp is not None:
            receipt_temp.unlink(missing_ok=True)
        # If restoring an older destination fails, retain its backup for recovery.
        if completed or (not rollback_failed and not previous.exists()):
            shutil.rmtree(staging, ignore_errors=True)
    return {"status": "prepared", "files": len(snapshot_files), "bytes": sum(map(len, snapshot_files.values())),
            "publicationDigest": parse_publication_receipt(receipt)["digest"],
            "redirect": OFFICIAL_BASE, "listingUrl": CANONICAL_URL}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publication-root", required=True, type=Path,
                        help="Actual rendered Website directory produced by the listing builder")
    parser.add_argument("--official-root", required=True, type=Path,
                        help="Separate temporary directory for the full snapshot used by mirror sync")
    args = parser.parse_args()
    try:
        publisher = publisher_from_environment(os.environ)
        preflight = read_preflight(os.environ, publisher)
        reader = official_reader_from_environment(os.environ, publisher)
        report = prepare(args.publication_root, args.official_root, publisher=publisher, preflight=preflight,
                         fresh_official_reader=reader)
    except (PreparationError, OSError) as error:
        message = str(error) if isinstance(error, PreparationError) else "Local publication could not be prepared"
        parser.exit(1, f"Exporter publication preparation failed: {message}.\n")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
