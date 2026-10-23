"""Allow main promotions only from this repository's release or hotfix branches."""
import json
import base64
import os
import re
import subprocess
from pathlib import Path


NUMBER = r"(?:0|[1-9][0-9]*)"
RELEASE = re.compile(rf"release/{NUMBER}\.{NUMBER}\.{NUMBER}")
HOTFIX = re.compile(r"hotfix/[A-Za-z0-9][A-Za-z0-9._-]*")
VERSION = re.compile(rf"({NUMBER})\.({NUMBER})\.({NUMBER})(?:-beta\.({NUMBER}))?")
WEBSITE_ONLY_CI_PATHS = frozenset({
    ".github/workflows/release-candidate.yml",
    "Tools/check-gitflow.py", "Tools/check-pipeline.py", "Tools/test-pipeline.py",
})


def website_only_paths(paths):
    """Keep site publication independent from package version and release tags."""
    return bool(paths) and all(
        isinstance(path, str) and "\\" not in path
        and all(part not in {"", ".", ".."} for part in path.split("/"))
        and (path.startswith("Website/") or path in WEBSITE_ONLY_CI_PATHS)
        for path in paths)


def promotion_changed_paths(pull, root):
    """Inspect the complete, immutable base/head diff, including renamed sources."""
    shas = [pull[side].get("sha", "") for side in ("base", "head")]
    if not all(re.fullmatch(r"[0-9a-f]{40}", sha) for sha in shas):
        raise ValueError("Complete main base and pull request head SHAs are required")
    # The PR checkout is shallow. Fetch only the two event-bound commits, without
    # changing the checkout or trusting the API's capped changed-file listing.
    subprocess.run(["git", "fetch", "--no-tags", "--depth=1", "origin", *shas],
                   cwd=root, capture_output=True, text=True, timeout=60, check=True)
    result = subprocess.run(["git", "diff", "--no-renames", "--name-only", "-z", *shas, "--"],
                            cwd=root, capture_output=True, text=True, timeout=30, check=True)
    if result.stdout and not result.stdout.endswith("\0"):
        raise ValueError("Incomplete changed-path output")
    return result.stdout.split("\0")[:-1]


def version_key(version):
    match = VERSION.fullmatch(version) if isinstance(version, str) else None
    if match is None:
        raise ValueError("Main requires a canonical stable or X.Y.Z-beta.N package version")
    return tuple(map(int, match.group(1, 2, 3))) + (match[4] is None, int(match[4] or 0))


def validate_version(version, base_version, head, tag_exists):
    if version_key(version) <= version_key(base_version):
        raise ValueError("Main promotion must advance the package version")
    if tag_exists:
        raise ValueError("The release tag already exists; choose a new package version")
    if "-beta." not in version and head != "release/" + version:
        raise ValueError("Stable preparation requires the matching release/X.Y.Z branch and manual publication")


def check_promotion_version(event, root):
    pull = event["pull_request"]
    if pull["base"]["ref"] != "main":
        return
    base_sha = pull["base"].get("sha", "")
    if not re.fullmatch(r"[0-9a-f]{40}", base_sha):
        raise ValueError("A complete main base SHA is required")
    if website_only_paths(promotion_changed_paths(pull, root)):
        print("Website-only promotion: package version and release tag are unchanged")
        return
    repository = pull["base"]["repo"]["full_name"]
    # GitHub supplies the repository identity and base SHA in the actual event.
    prefix = "repos/" + repository
    result = subprocess.run(["gh", "api", prefix + "/contents/package.json?ref=" + base_sha],
                            capture_output=True, text=True, timeout=30, check=True)
    baseline = json.loads(base64.b64decode(json.loads(result.stdout)["content"]))["version"]
    version = json.loads((root / "package.json").read_text(encoding="utf-8"))["version"]
    version_key(version)
    tag = subprocess.run(["gh", "api", prefix + "/git/ref/tags/v" + version],
                         capture_output=True, text=True, timeout=30, check=False)
    if tag.returncode and "(HTTP 404)" not in tag.stderr:
        raise ValueError("Could not check whether the release tag exists")
    validate_version(version, baseline, pull["head"]["ref"], tag.returncode == 0)
    notes = (root / "CHANGELOG.md").read_text(encoding="utf-8")
    heading = "## " + version + "\n"
    if heading not in notes or not notes.split(heading, 1)[1].split("\n## ", 1)[0].strip():
        raise ValueError("Main promotion needs release notes for its package version")


def valid_branch(value):
    """Check the branch-name constraints relevant to an unprefixed Git ref."""
    return (isinstance(value, str) and bool(value) and value != "@"
            and not value.startswith(("-", "/")) and not value.endswith(("/", "."))
            and not any(token in value for token in ("..", "@{", "//"))
            and not re.search(r"[\x00-\x20\x7f~^:?*\[\\]", value)
            and all(not part.startswith(".") and not part.endswith(".lock")
                    for part in value.split("/")))


def repository_identity(value):
    if (not isinstance(value, dict) or type(value.get("id")) is not int
            or value["id"] <= 0 or not isinstance(value.get("full_name"), str)
            or not value["full_name"].strip()):
        raise ValueError("A complete GitHub repository identity is required for main promotion")
    return value["id"], value["full_name"]


def validate(event, event_name):
    if event_name != "pull_request" or not isinstance(event, dict):
        raise ValueError("Gitflow validation requires an actual pull_request event")
    pull = event.get("pull_request")
    if not isinstance(pull, dict):
        raise ValueError("The pull request payload is missing")
    base, head = pull.get("base"), pull.get("head")
    if (not isinstance(base, dict) or not isinstance(head, dict)
            or not valid_branch(base.get("ref")) or not valid_branch(head.get("ref"))):
        raise ValueError("The pull request needs valid base and head branch refs")
    if base["ref"] != "main":
        return
    if repository_identity(base.get("repo")) != repository_identity(head.get("repo")):
        raise ValueError("Main promotion must originate in this repository")
    if not (RELEASE.fullmatch(head["ref"]) or HOTFIX.fullmatch(head["ref"])):
        raise ValueError("Main accepts only release/X.Y.Z or hotfix/<slug> pull requests")


def main():
    event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    validate(event, os.environ.get("GITHUB_EVENT_NAME"))
    check_promotion_version(event, Path(__file__).resolve().parents[1])
    print("Gitflow pull request target passed")


if __name__ == "__main__":
    main()
