"""Generate Formats/Runtime/LilToon234Catalogue.cs and the per-tag lilToon structure table.

Catalogue from one lilToon tag (a local folder, a tag of a local clone, or a GitHub download):
  python3 Tools/generate-liltoon-catalogue.py catalogue --fetch 2.3.4 --check
  python3 Tools/generate-liltoon-catalogue.py catalogue --source <lilToon folder> --commit <sha> --output <file>
Structure hashes for tags (Compatibility/liltoon-structure.json):
  python3 Tools/generate-liltoon-catalogue.py structure --fetch --write
  python3 Tools/generate-liltoon-catalogue.py structure --git <lilToon clone> --tags 2.0.0 2.3.4

Downloads go to a temporary folder outside the repository. lilToon itself is never committed.
"""
import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import liltoon_catalogue as lc  # noqa: E402

ROOT = Path(__file__).resolve().parents[1]
# Tags hashed into the structure table: every stable tag of the verified range
# (lilToon.verified in Compatibility/dependencies.json), plus the last 1.x release to show
# that the hash separates a different structure.
COMPARISON_TAGS = ("1.10.3",)


def source_catalogue(args):
    if args.fetch:
        with tempfile.TemporaryDirectory(prefix="liltoon-" + args.fetch + "-") as temp:
            commit = lc.fetch_tag_source(args.fetch, temp)
            return lc.build_catalogue(lc.load_source(temp), commit)
    if args.git:
        return lc.catalogue_for_tag(args.tag, git_repository=args.git)
    commit = args.commit
    if not commit:
        commit = subprocess.check_output(["git", "-C", str(args.source), "rev-parse", "HEAD"], text=True).strip()
    return lc.build_catalogue(lc.load_source(args.source), commit)


def command_catalogue(args):
    catalogue = source_catalogue(args)
    rendered = lc.render_catalogue(catalogue).encode("ascii")
    if args.output:
        args.output.write_bytes(rendered)
    if args.check:
        # The catalogue is committed with LF; a CRLF checkout is compared as LF.
        current = (ROOT / lc.CATALOGUE_PATH).read_bytes().replace(b"\r\n", b"\n")
        if current != rendered:
            committed = lc.parse_catalogue(current.decode("ascii"))
            for line in lc.structure_difference(committed, catalogue)[:50]:
                print(line)
            print("%s differs from lilToon %s (%s)" % (lc.CATALOGUE_PATH, catalogue["version"], catalogue["commit"]))
            return 1
        print("%s matches lilToon %s byte for byte" % (lc.CATALOGUE_PATH, catalogue["version"]))
    print("structure hash %s %s" % (catalogue["version"], lc.structure_hash(catalogue)))
    return 0


def list_tags(args):
    if args.tags:
        return args.tags
    if args.git:
        names = subprocess.check_output(["git", "-C", str(args.git), "tag", "--list"], text=True).split()
    else:
        names = lc.remote_tags()
    config = json.loads((ROOT / "Compatibility/dependencies.json").read_text(encoding="utf-8"))
    bounds = lc.verified_range(config)
    stable = [n for n in names if _stable(n)]
    chosen = [n for n in stable if lc.in_range(n, bounds)]
    chosen += [n for n in COMPARISON_TAGS if n in stable]
    return sorted(set(chosen), key=lc.parse_version)


def _stable(name):
    try:
        lc.parse_version(name)
        return not name.startswith("v")
    except lc.CatalogueError:
        return False


def build_table(entries, reference):
    reference_hash = lc.structure_hash(reference)
    return {
        "format": 1,
        "definition": "Documentation~/decisions/2026-10-14-liltoon-structure-hash.md",
        "reference": {"tag": reference["version"], "commit": reference["commit"], "structureHash": reference_hash},
        "tags": [{"tag": tag, "commit": cat["commit"], "structureHash": lc.structure_hash(cat),
                  "matchesReference": lc.structure_hash(cat) == reference_hash}
                 for tag, cat in sorted(entries.items(), key=lambda item: lc.parse_version(item[0]))],
    }


def command_structure(args):
    reference = lc.parse_catalogue((ROOT / lc.CATALOGUE_PATH).read_text(encoding="ascii"))
    entries = {}
    for tag in list_tags(args):
        catalogue = lc.catalogue_for_tag(tag, git_repository=args.git)
        entries[tag] = catalogue
        same = lc.structure_hash(catalogue) == lc.structure_hash(reference)
        print("%-8s %s %s %s" % (tag, catalogue["commit"][:12], lc.structure_hash(catalogue)[:16],
                                 "same as " + reference["version"] if same else "DIFFERS"))
        if not same:
            for line in lc.structure_difference(reference, catalogue)[:args.max_differences]:
                print("    " + line)
    table = build_table(entries, reference)
    text = json.dumps(table, indent=2) + "\n"
    if args.write:
        (ROOT / lc.STRUCTURE_PATH).write_text(text, encoding="utf-8", newline="\n")
        print("wrote " + lc.STRUCTURE_PATH)
    elif args.output:
        args.output.write_text(text, encoding="utf-8", newline="\n")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)
    catalogue = commands.add_parser("catalogue", help="render the catalogue from one lilToon tag")
    origin = catalogue.add_mutually_exclusive_group(required=True)
    origin.add_argument("--source", type=Path, help="extracted lilToon repository or package folder")
    origin.add_argument("--fetch", metavar="TAG", help="download this lilToon tag from GitHub")
    origin.add_argument("--git", type=Path, help="local lilToon clone; use with --tag")
    catalogue.add_argument("--tag", default=lc.REFERENCE_VERSION, help="tag to read from --git")
    catalogue.add_argument("--commit", help="commit of --source (default: git rev-parse HEAD in --source)")
    catalogue.add_argument("--output", type=Path, help="write the rendered catalogue here")
    catalogue.add_argument("--check", action="store_true", help="fail unless %s is byte-identical" % lc.CATALOGUE_PATH)
    catalogue.set_defaults(handler=command_catalogue)
    structure = commands.add_parser("structure", help="hash the structure of lilToon tags")
    where = structure.add_mutually_exclusive_group(required=True)
    where.add_argument("--git", type=Path, help="local lilToon clone with tags")
    where.add_argument("--fetch", action="store_true", help="download tags from GitHub")
    structure.add_argument("--tags", nargs="+", help="tags to hash (default: the verified range and %s)"
                           % ", ".join(COMPARISON_TAGS))
    structure.add_argument("--write", action="store_true", help="write %s" % lc.STRUCTURE_PATH)
    structure.add_argument("--output", type=Path, help="write the table to another file")
    structure.add_argument("--max-differences", type=int, default=20)
    structure.set_defaults(handler=command_structure)
    args = parser.parse_args(argv)
    if args.command == "catalogue" and args.commit and not args.source:
        parser.error("--commit is only used with --source")
    return args.handler(args)


if __name__ == "__main__":
    raise SystemExit(main())
