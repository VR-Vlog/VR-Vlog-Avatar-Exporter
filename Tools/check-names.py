"""Find third-party avatar, outfit, product and creator names before they reach the public repository.

The name list is kept outside the repository and is never committed. Pass it with
--names or the EXPORTER_BANNED_NAMES environment variable. One name per line;
text after # is a comment. ASCII-only names match whole words, case-insensitively;
other names match as substrings.

  python3 Tools/check-names.py --names FILE              # tracked files
  python3 Tools/check-names.py --names FILE --text -     # stdin (an issue or PR body)
  python3 Tools/check-names.py --names FILE --pre-push   # git pre-push hook (reads stdin)
"""
import argparse
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ZERO = "0" * 40


def load_names(path):
    patterns = []
    for number, line in enumerate(Path(path).read_text(encoding="utf-8-sig").splitlines(), 1):
        name = line.split("#", 1)[0].strip()
        if not name:
            continue
        if name.isascii():
            pattern = re.compile(r"(?<![A-Za-z0-9])" + re.escape(name) + r"(?![A-Za-z0-9])", re.IGNORECASE)
        else:
            pattern = re.compile(re.escape(name))
        patterns.append((number, name, pattern))
    return patterns


def scan(label, text, patterns):
    findings = []
    for line_number, line in enumerate(text.splitlines(), 1):
        for number, name, pattern in patterns:
            if pattern.search(line):
                findings.append((label, line_number, number, name))
    return findings


def git(*args):
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True).stdout.decode("utf-8", errors="replace")


def tracked_files(patterns):
    findings = []
    for name in filter(None, git("ls-files", "-z").split("\0")):
        path = ROOT / name
        if path.is_symlink() or not path.is_file():
            continue
        findings += scan(name, name, patterns)
        findings += scan(name, path.read_bytes().decode("utf-8", errors="replace"), patterns)
    return findings


def pre_push(remote, lines, patterns):
    findings = []
    for line in lines:
        parts = line.split()
        if len(parts) != 4 or parts[1] == ZERO:
            continue  # malformed line or branch deletion
        local_ref, local_sha, remote_ref, remote_sha = parts
        findings += scan("ref", f"{local_ref}\n{remote_ref}", patterns)
        if remote_sha != ZERO:
            revs = [f"{remote_sha}..{local_sha}"]
        else:
            revs = [local_sha, "--not", f"--remotes={remote}" if remote else "--remotes"]
        for sha in filter(None, git("rev-list", *revs).split()):
            findings += scan(f"commit {sha[:10]} message", git("log", "-1", "--format=%B", sha), patterns)
            diff = git("show", "--format=", "--no-color", "--unified=0", "--no-ext-diff", sha)
            added = "\n".join(l[1:] for l in diff.splitlines() if l.startswith("+") and not l.startswith("+++"))
            paths = git("show", "--format=", "--name-only", sha)
            findings += scan(f"commit {sha[:10]} added lines", added, patterns)
            findings += scan(f"commit {sha[:10]} paths", paths, patterns)
    return findings


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--names", default=os.environ.get("EXPORTER_BANNED_NAMES"), help="name list (outside the repository)")
    parser.add_argument("--text", help="scan this file instead of tracked files; - reads stdin")
    parser.add_argument("--pre-push", action="store_true", help="scan refs, commit messages and added lines from git pre-push stdin")
    parser.add_argument("--show", action="store_true", help="print the matched name, not only its line in the name list")
    parser.add_argument("remote", nargs="*", help="pre-push hook arguments (remote name and URL)")
    args = parser.parse_args()

    if not args.names:
        print("check-names: no name list (set --names or EXPORTER_BANNED_NAMES); skipped.", file=sys.stderr)
        return 0
    names_path = Path(args.names).resolve()
    if names_path.is_relative_to(ROOT.resolve()):
        print("check-names: the name list must live outside the repository.", file=sys.stderr)
        return 2
    patterns = load_names(names_path)

    if args.pre_push:
        findings = pre_push(args.remote[0] if args.remote else None, sys.stdin.read().splitlines(), patterns)
    elif args.text:
        text = sys.stdin.read() if args.text == "-" else Path(args.text).read_text(encoding="utf-8-sig")
        findings = scan(args.text, text, patterns)
    else:
        findings = tracked_files(patterns)

    for label, line, number, name in findings:
        print(f"{label}:{line}: name list line {number}" + (f" ({name})" if args.show else ""))
    if findings:
        print(f"check-names: {len(findings)} match(es). Replace them with generic words such as 'avatar A' or 'outfit B'.", file=sys.stderr)
        return 1
    print("check-names: no listed names found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
