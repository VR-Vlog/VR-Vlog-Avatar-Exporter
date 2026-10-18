"""Validate the reviewed commit, package version and explicit release channel."""
import json
import importlib.util
import os
import re
from pathlib import Path


def validate(package, compatibility, *, version, univrm_version, channel, ref, sha, expected_sha):
    if ref != "refs/heads/main":
        raise ValueError("Publish only the reviewed main branch")
    if not re.fullmatch(r"[0-9a-f]{40}", expected_sha) or sha != expected_sha:
        raise ValueError("The main commit must equal the reviewed expected commit")
    if version != package["version"]:
        raise ValueError("Requested version must equal package.json")
    number = r"(?:0|[1-9][0-9]*)"
    stable = rf"{number}\.{number}\.{number}"
    patterns = {"stable": stable, "beta": stable + rf"-beta\.{number}"}
    if channel not in patterns or not re.fullmatch(patterns[channel], version):
        raise ValueError("Release channel and version must agree (stable or X.Y.Z-beta.N)")
    if univrm_version != compatibility["uniVrm"]["releaseVersion"]:
        raise ValueError("UniVRM version must match the pinned release dependency")
    return ["--prerelease", "--latest=false"] if channel == "beta" else ["--latest"]


def staging_tool(root):
    spec = importlib.util.spec_from_file_location("staging_beta", root / "Tools/resolve-staging-beta.py")
    staging = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(staging)
    return staging


def publication_context(environment, root):
    if environment.get("GITHUB_EVENT_NAME") != "workflow_run":
        return environment["GITHUB_REF"], environment["GITHUB_SHA"]
    event = json.loads(Path(environment["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    return staging_tool(root).release_context(event, "workflow_run", environment["EXPECTED_COMMIT"],
                                              environment["RELEASE_CHANNEL"], root)


if __name__ == "__main__":
    root = Path(__file__).resolve().parents[1]
    ref, sha = publication_context(os.environ, root)
    flags = validate(
        json.loads((root / "package.json").read_text(encoding="utf-8")),
        json.loads((root / "Compatibility/dependencies.json").read_text(encoding="utf-8")),
        version=os.environ["EXPORTER_VERSION"], univrm_version=os.environ["UNIVRM_VERSION"],
        channel=os.environ["RELEASE_CHANNEL"], ref=ref,
        sha=sha, expected_sha=os.environ["EXPECTED_COMMIT"],
    )
    # Only hard-coded flags reach the shell; no workflow input becomes an option.
    print("\n".join(flags))
