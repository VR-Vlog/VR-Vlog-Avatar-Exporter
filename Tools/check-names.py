"""Find third-party avatar, outfit, product and creator names before they reach the public repository.

The name list is kept outside the repository and is never committed. Pass it with
--names or the EXPORTER_BANNED_NAMES environment variable. One name per line;
text after # is a comment. ASCII-only names match case-insensitively as words,
including words inside CamelCase identifiers and paths (OutfitNameFix); other
names match as substrings.

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
    names = []
    for number, line in enumerate(Path(path).read_text(encoding="utf-8-sig").splitlines(), 1):
        name = line.split("#", 1)[0].strip()
        if name:
            names.append((number, name, re.compile(re.escape(name), re.IGNORECASE if name.isascii() else 0)))
    return names


def word_boundary(text, start, end):
    """True when text[start:end] is a word on its own, or a CamelCase part of a longer identifier."""
    before = text[start - 1] if start else ""
    after = text[end] if end < len(text) else ""
    first, last = text[start], text[end - 1]
    starts = not before.isascii() or not before.isalnum() or before.isdigit() or (before.islower() and first.isupper())
    ends = not after.isascii() or not after.isalnum() or after.isdigit() or (after.isupper() and last.islower())
    return starts and ends


def scan(label, text, names):
    findings = []
    for line_number, line in enumerate(text.splitlines(), 1):
        for number, name, pattern in names:
            for match in pattern.finditer(line):
                if not name.isascii() or word_boundary(line, match.start(), match.end()):
                    findings.append((label, line_number, number, name))
                    break
    return findings


def git(*args):
    return subprocess.run(["git", "-C", str(ROOT), *args], check=True, capture_output=True).stdout.decode("utf-8", errors="replace")


def has_commit(sha):
    return subprocess.run(["git", "-C", str(ROOT), "cat-file", "-e", f"{sha}^{{commit}}"], capture_output=True).returncode == 0


def tracked_files(names):
    findings = []
    for name in filter(None, git("ls-files", "-z").split("\0")):
        path = ROOT / name
        if path.is_symlink() or not path.is_file():
            continue
        findings += scan(name, name, names)
        findings += scan(name, path.read_bytes().decode("utf-8", errors="replace"), names)
    return findings


def pre_push(remote, lines, names):
    # A remote given as a URL has no remote-tracking refs, so fall back to all of them.
    exclude = f"--remotes={remote}" if remote and re.fullmatch(r"[\w.-]+", remote) else "--remotes"
    findings = []
    for line in lines:
        parts = line.split()
        if len(parts) != 4 or parts[1] == ZERO:
            continue  # malformed line or branch deletion
        local_ref, local_sha, remote_ref, remote_sha = parts
        findings += scan("ref", f"{local_ref}\n{remote_ref}", names)
        if local_ref.startswith("refs/tags/") and git("cat-file", "-t", local_sha).strip() == "tag":
            findings += scan(f"tag {local_ref[10:]} message", git("cat-file", "-p", local_sha), names)
        if remote_sha != ZERO and has_commit(remote_sha):
            revs = [f"{remote_sha}..{local_sha}"]
        else:  # new branch, or a remote commit this clone has not fetched
            revs = [local_sha, "--not", exclude]
        for sha in filter(None, git("rev-list", *revs).split()):
            findings += scan(f"commit {sha[:10]} message", git("log", "-1", "--format=%B", sha), names)
            diff = git("show", "--format=", "--no-color", "--unified=0", "--no-ext-diff", sha)
            added = "\n".join(l[1:] for l in diff.splitlines()
                              if l.startswith("+") and not re.match(r"\+\+\+ (?:b/|/dev/null)", l))
            findings += scan(f"commit {sha[:10]} added lines", added, names)
            findings += scan(f"commit {sha[:10]} paths", git("show", "--format=", "--name-only", sha), names)
    return findings


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--names", default=os.environ.get("EXPORTER_BANNED_NAMES"), help="name list (outside the repository)")
    parser.add_argument("--text", help="scan this file instead of tracked files; - reads stdin")
    parser.add_argument("--pre-push", action="store_true", help="scan refs, commit messages and added lines from git pre-push stdin")
    parser.add_argument("--show", action="store_true", help="print the matched name, not only its line in the name list")
    parser.add_argument("remote", nargs="*", help="pre-push hook arguments (remote name and URL)")
    args = parser.parse_args()
    sys.stdout.reconfigure(errors="replace")
    sys.stderr.reconfigure(errors="replace")

    if not args.names:
        print("check-names: no name list (set --names or EXPORTER_BANNED_NAMES); skipped.", file=sys.stderr)
        return 0
    names_path = Path(args.names).resolve()
    if not names_path.is_file():
        print(f"check-names: name list not found ({args.names}); skipped.", file=sys.stderr)
        return 0
    if names_path.is_relative_to(ROOT.resolve()):
        print("check-names: the name list must live outside the repository.", file=sys.stderr)
        return 2
    names = load_names(names_path)

    try:
        if args.pre_push:
            stdin = sys.stdin.buffer.read().decode("utf-8", errors="replace")
            findings = pre_push(args.remote[0] if args.remote else None, stdin.splitlines(), names)
        elif args.text:
            data = sys.stdin.buffer.read() if args.text == "-" else Path(args.text).read_bytes()
            findings = scan(args.text, data.decode("utf-8-sig", errors="replace"), names)
        else:
            findings = tracked_files(names)
    except subprocess.CalledProcessError as error:
        detail = error.stderr.decode("utf-8", errors="replace").strip()
        print(f"check-names: git {' '.join(error.cmd[3:])} failed: {detail}", file=sys.stderr)
        return 2

    for label, line, number, name in findings:
        print(f"{label}:{line}: name list line {number}" + (f" ({name})" if args.show else ""))
    if findings:
        print(f"check-names: {len(findings)} match(es). Replace them with generic words such as 'avatar A' or 'outfit B'.", file=sys.stderr)
        return 1
    print("check-names: no listed names found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
