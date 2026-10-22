"""Authenticate a completed main candidate and resolve its automatic beta inputs."""
import argparse
import importlib.util
import io
import json
import os
import re
import subprocess
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
NUMBER = r"(?:0|[1-9][0-9]*)"
STABLE = re.compile(rf"{NUMBER}\.{NUMBER}\.{NUMBER}")
BETA = re.compile(rf"{NUMBER}\.{NUMBER}\.{NUMBER}-beta\.{NUMBER}")
SHA = re.compile(r"[0-9a-f]{40}")
SHA256 = re.compile(r"[0-9a-f]{64}")


def load_tool(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / "Tools" / (name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


verifier = load_tool("verify-release-candidate")
PREFIX = "repos/" + verifier.REPOSITORY


def require(condition, message):
    if not condition:
        raise ValueError(message)


def positive_integer(value):
    return type(value) is int and value > 0


def official_repository(repository):
    return (isinstance(repository, dict)
            and type(repository.get("id")) is int
            and repository["id"] == verifier.REPOSITORY_GITHUB_ID
            and repository.get("full_name") == verifier.REPOSITORY)


def verify_candidate_run(run, workflow_id, sha):
    require(isinstance(run, dict) and positive_integer(run.get("id"))
            and positive_integer(run.get("run_attempt")) and positive_integer(workflow_id)
            and isinstance(sha, str) and SHA.fullmatch(sha), "Incomplete candidate run identity")
    verifier.verify_run(run, workflow_id, sha)
    require(run.get("event") == "push" and official_repository(run.get("head_repository")),
            "Automatic beta publication requires a main push from the official repository")


def event_candidate(event, event_name):
    require(event_name == "workflow_run" and isinstance(event, dict)
            and event.get("action") == "completed" and official_repository(event.get("repository")),
            "Automatic beta publication requires the actual completed workflow_run event")
    run = event.get("workflow_run")
    require(isinstance(run, dict), "Missing candidate event run")
    verify_candidate_run(run, run.get("workflow_id"), run.get("head_sha"))
    return run


def authenticate_candidate(event, event_name, api=None):
    api = api or verifier.gh_api
    payload = event_candidate(event, event_name)
    workflow = api(PREFIX + "/actions/workflows/release-candidate.yml")
    require(isinstance(workflow, dict) and positive_integer(workflow.get("id"))
            and workflow.get("path") == verifier.WORKFLOW, "Unapproved candidate workflow")
    run = api(PREFIX + "/actions/runs/" + str(payload["id"]))
    verify_candidate_run(run, workflow["id"], payload["head_sha"])
    require(run["id"] == payload["id"] and run["workflow_id"] == payload["workflow_id"]
            and run["run_attempt"] == payload["run_attempt"], "Candidate event and API run differ")
    return run


def verify_checkout(sha, root=ROOT):
    require(isinstance(sha, str) and SHA.fullmatch(sha), "A complete candidate commit is required")
    checked_out = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip()
    require(checked_out == sha, "Checkout differs from the candidate commit")
    ancestor = subprocess.run(["git", "merge-base", "--is-ancestor", sha, "origin/main"],
                              cwd=root, capture_output=True, timeout=30, check=False)
    require(ancestor.returncode == 0, "Candidate commit must belong to origin/main")
    return checked_out


def release_context(event, event_name, expected_sha, channel, root=ROOT, api=None):
    """Authenticate automatic release context without downloading another artifact."""
    require(channel == "beta", "Automatic staging publication can only publish beta")
    run = authenticate_candidate(event, event_name, api)
    require(expected_sha == run["head_sha"], "Release input differs from the candidate event commit")
    return "refs/heads/main", verify_checkout(expected_sha, root)


def package_version(root):
    package = json.loads((root / "package.json").read_text(encoding="utf-8"))
    version = package.get("version") if isinstance(package, dict) else None
    require(isinstance(version, str) and (BETA.fullmatch(version) or STABLE.fullmatch(version)),
            "Staging source must use X.Y.Z-beta.N or an explicitly manual stable version")
    return version


def candidate_hash(data):
    # Read only bounded JSON first. The existing verifier subsequently checks the
    # entire exact inventory, package bytes, validation receipt and run binding.
    require(len(data) <= verifier.MAX_ARCHIVE_BYTES, "Candidate archive exceeds its size budget")
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        entry = archive.getinfo("candidate.json")
        require(entry.file_size <= 128 * 1024 and not entry.flag_bits & 1,
                "Invalid candidate metadata size or encryption")
        metadata = json.loads(archive.read(entry))
    digest = metadata.get("package_sha256") if isinstance(metadata, dict) else None
    require(isinstance(digest, str) and SHA256.fullmatch(digest), "Invalid candidate package SHA-256")
    return digest


def resolve(event, event_name, root=ROOT, api=None):
    api = api or verifier.gh_api
    run = authenticate_candidate(event, event_name, api)
    sha = verify_checkout(run["head_sha"], root)
    version = package_version(root)
    listing = api(PREFIX + "/actions/runs/" + str(run["id"]) + "/artifacts?per_page=100")
    require(isinstance(listing, dict) and isinstance(listing.get("artifacts"), list)
            and type(listing.get("total_count")) is int
            and listing["total_count"] == len(listing["artifacts"]), "Candidate artifact list is incomplete")
    artifact = verifier.select_artifact(listing["artifacts"], sha)
    require(positive_integer(artifact.get("id")), "Invalid candidate artifact identity")
    data = api(PREFIX + "/actions/artifacts/" + str(artifact["id"]) + "/zip", binary=True)
    digest = candidate_hash(data)
    verifier.verify_archive(data, sha, version, digest, run["id"])
    after = authenticate_candidate(event, event_name, api)
    require(after["run_attempt"] == run["run_attempt"], "Candidate run changed during resolution")
    if STABLE.fullmatch(version):
        return {"publish": "false"}
    return {"publish": "true", "version": version, "channel": "beta", "expected_commit": sha,
            "package_sha256": digest, "candidate_run_id": str(run["id"])}


def verify_published_tag(environment, root=ROOT, api=None):
    api = api or verifier.gh_api
    ref = environment.get("GITHUB_REF", "")
    version = ref.removeprefix("refs/tags/v")
    require(environment.get("GITHUB_EVENT_NAME") == "workflow_dispatch"
            and ref == "refs/tags/v" + version and BETA.fullmatch(version),
            "Listing tag dispatch requires an exact beta version tag")
    require(environment.get("GITHUB_WORKFLOW_REF") == verifier.REPOSITORY
            + "/.github/workflows/build-listing.yml@" + ref, "Listing workflow must originate at the same beta tag")
    sha = verify_checkout(environment.get("GITHUB_SHA"), root)
    tag_sha = subprocess.check_output(["git", "rev-parse", ref + "^{commit}"], cwd=root, text=True).strip()
    require(tag_sha == sha, "Published tag differs from the listing checkout")
    source_version = package_version(root)
    require(source_version == version, "Published tag version differs from its package manifest")
    release = api(PREFIX + "/releases/tags/v" + version)
    require(isinstance(release, dict) and release.get("tag_name") == "v" + version
            and release.get("draft") is False and release.get("prerelease") is True,
            "Listing requires an already published beta release")
    filename = "com.vrvlog.liltoon-vrm-exporter-" + version + ".zip"
    assets = release.get("assets")
    require(isinstance(assets, list), "Published release assets are missing")
    matches = [asset for asset in assets if isinstance(asset, dict) and asset.get("name") == filename]
    require(len(matches) == 1 and matches[0].get("state") == "uploaded"
            and positive_integer(matches[0].get("size")) and matches[0]["size"] <= 20 * 1024 * 1024,
            "Published beta exporter ZIP is missing or invalid")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--verify-published-tag", action="store_true")
    args = parser.parse_args()
    if args.verify_published_tag:
        verify_published_tag(os.environ)
        print("Published beta tag and exact listing source verified")
        return
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    outputs = resolve(event, os.environ.get("GITHUB_EVENT_NAME"))
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as destination:
        for name, value in outputs.items():
            destination.write(name + "=" + value + "\n")
    print("Automatic beta candidate verified" if outputs["publish"] == "true" else "Stable candidate awaits manual publication")


if __name__ == "__main__":
    main()
