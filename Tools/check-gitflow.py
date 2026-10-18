"""Allow main promotions only from this repository's release or hotfix branches."""
import json
import os
import re
from pathlib import Path


NUMBER = r"(?:0|[1-9][0-9]*)"
RELEASE = re.compile(rf"release/{NUMBER}\.{NUMBER}\.{NUMBER}")
HOTFIX = re.compile(r"hotfix/[A-Za-z0-9][A-Za-z0-9._-]*")


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
    print("Gitflow pull request target passed")


if __name__ == "__main__":
    main()
