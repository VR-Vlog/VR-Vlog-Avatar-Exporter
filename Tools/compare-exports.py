"""Check that two package versions export the same VRM files for the same avatars.

Tools/run-export-regression.ps1 (with ExportRegressionRunner.cs) exports each job to
<ID>_<run>.vrm and records <ID>_<run>.json next to it. This script pairs those results:

  compare-exports.py <dirA> <dirB> [--ignore-missing] [--json]
      Pairs <ID>_<run> of dirA with the same name in dirB (for example the base package
      against the changed package).
  compare-exports.py --runs <dir> <runA> <runB> [--json]
      Pairs <ID>_<runA> with <ID>_<runB> inside one directory (for example run 1 against
      run 2 of the same package, to confirm the base is deterministic).
  compare-exports.py --diff-paths <vrmA> <vrmB>
      Prints every glb JSON path that differs between two files. Use it on two runs of the
      same package to find paths that change on every export (see VOLATILE and VOLATILE_SUB).
      The JSON is compared as is, without removing or masking anything.

Checked for each pair:
  1. success or failure, and the failure text
  2. warnings (the entries of the export details log), in order
  3. expressions: count, names, preset or custom, order, and every field of each expression
     (morphTargetBinds with node, index and weight, materialColorBinds, textureTransformBinds,
     isBinary, overrideBlink / overrideLookAt / overrideMouth and anything else). VRM 0.x
     blendShapeGroups are compared the same way.
  4. sha256 of the glb JSON chunk after removing VOLATILE paths and masking the VOLATILE_SUB
     parts of string values, and sha256 of the BIN chunk
Whole-file byte equality is reported as information only.

Exit codes: 0 = every pair matches, 1 = differences, missing results or no pairs,
2 = usage, I/O or format errors.
"""
import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

GLB_MAGIC = b"glTF"
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
MAX_PATHS = 8

# glb JSON paths that change on every export even with the same input and package. Found by
# running each avatar twice with the same package and comparing run 1 with run 2 using
# --diff-paths; "*" matches any one key or index. Only these are removed before hashing.
VOLATILE = []

# (path, regex, replacement) for string values that only partly change on every export. The
# regex is applied to the value before hashing, so only the part that changes is masked and the
# rest of the value is still compared. Paths use the same "*" wildcard as VOLATILE.
VOLATILE_SUB = [
    # The baked emission texture gets a new random GUID at the end of its image name on every
    # export, while the image bytes in BIN stay the same. Found with --diff-paths on two runs of
    # one package.
    ("/images/*/name", r"(__VRVlog_Emission)_[0-9a-f]{32}$", r"\1_<guid>"),
]


def read_glb(path):
    """Return (file_bytes, json_dict, bin_bytes); raise ValueError when the file is not a glb."""
    data = Path(path).read_bytes()
    if len(data) < 12:
        raise ValueError("%s: shorter than the 12-byte glb header" % path)
    magic, _version, _length = struct.unpack_from("<4sII", data, 0)
    if magic != GLB_MAGIC:
        raise ValueError("%s: not a glb (magic %r)" % (path, magic))
    chunks, offset = {}, 12
    while offset + 8 <= len(data):
        size, kind = struct.unpack_from("<II", data, offset)
        chunks.setdefault(kind, data[offset + 8: offset + 8 + size])
        offset += 8 + size
    if JSON_CHUNK not in chunks:
        raise ValueError("%s: no JSON chunk" % path)
    try:
        document = json.loads(chunks[JSON_CHUNK].rstrip(b" \x00").decode("utf-8"))
    except (UnicodeDecodeError, ValueError) as error:
        raise ValueError("%s: JSON chunk is not valid UTF-8 JSON: %s" % (path, error)) from None
    return data, document, chunks.get(BIN_CHUNK, b"")


def diff_paths(a, b, path=""):
    """Every JSON path where a and b differ; a list length change is reported once."""
    if type(a) is not type(b):
        return [path or "/"]
    if isinstance(a, dict):
        result = []
        for key in sorted(set(a) | set(b)):
            if key not in a or key not in b:
                result.append("%s/%s" % (path, key))
            else:
                result += diff_paths(a[key], b[key], "%s/%s" % (path, key))
        return result
    if isinstance(a, list):
        if len(a) != len(b):
            return ["%s (length %d vs %d)" % (path, len(a), len(b))]
        result = []
        for index, (x, y) in enumerate(zip(a, b)):
            result += diff_paths(x, y, "%s/%d" % (path, index))
        return result
    return [] if a == b else [path or "/"]


def pattern(volatile_path):
    parts = volatile_path.split("/")
    return re.compile("^" + "/".join("[^/]+" if part == "*" else re.escape(part) for part in parts) + "$")


def volatile_patterns(paths=None):
    return [pattern(p) for p in (VOLATILE if paths is None else paths)]


def volatile_subs(entries=None):
    return [(pattern(p), re.compile(regex), replacement)
            for p, regex, replacement in (VOLATILE_SUB if entries is None else entries)]


def strip_volatile(value, patterns=None, path="", subs=None):
    """Copy of value without the dictionary entries whose path matches a VOLATILE pattern, and
    with the VOLATILE_SUB replacements applied to matching string values."""
    if patterns is None:
        patterns = volatile_patterns()
    if subs is None:
        subs = volatile_subs()
    if isinstance(value, dict):
        return {key: strip_volatile(item, patterns, "%s/%s" % (path, key), subs) for key, item in value.items()
                if not any(p.match("%s/%s" % (path, key)) for p in patterns)}
    if isinstance(value, list):
        return [strip_volatile(item, patterns, "%s/%d" % (path, index), subs) for index, item in enumerate(value)]
    if isinstance(value, str):
        for path_pattern, regex, replacement in subs:
            if path_pattern.match(path):
                value = regex.sub(replacement, value)
    return value


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def normalized_json_hash(stripped):
    text = json.dumps(stripped, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
    return sha256(text.encode("utf-8"))


def expressions(document):
    """Key -> expression object, with the group in the key. Keeps the output order."""
    extensions = document.get("extensions") or {}
    result = {}
    if "VRMC_vrm" in extensions:
        groups = extensions["VRMC_vrm"].get("expressions") or {}
        for group in ("preset", "custom"):
            for name, expression in (groups.get(group) or {}).items():
                result["%s:%s" % (group, name)] = expression
    if "VRM" in extensions:
        master = extensions["VRM"].get("blendShapeMaster") or {}
        for index, group in enumerate(master.get("blendShapeGroups") or []):
            result["vrm0:%s:%s" % (group.get("presetName", ""), group.get("name", index))] = group
    return result


def compare_expressions(ea, eb):
    issues = []
    if len(ea) != len(eb):
        issues.append("expression count %d vs %d" % (len(ea), len(eb)))
    only_a = [key for key in ea if key not in eb]
    only_b = [key for key in eb if key not in ea]
    if only_a:
        issues.append("expressions only in A: " + ", ".join(only_a))
    if only_b:
        issues.append("expressions only in B: " + ", ".join(only_b))
    common_a = [key for key in ea if key in eb]
    common_b = [key for key in eb if key in ea]
    if common_a != common_b:
        issues.append("expression order differs")
    for key in common_a:
        a, b = ea[key], eb[key]
        for field in sorted(set(a) | set(b)):
            va, vb = a.get(field), b.get(field)
            if va == vb:
                continue
            if isinstance(va, list) and isinstance(vb, list):
                first = next(("#%d %s vs %s" % (i, json.dumps(x, ensure_ascii=False), json.dumps(y, ensure_ascii=False))
                              for i, (x, y) in enumerate(zip(va, vb)) if x != y), "in the tail")
                issues.append("%s %s: %d vs %d entries, first difference %s" % (key, field, len(va), len(vb), first))
            else:
                issues.append("%s %s: %s vs %s" % (key, field, json.dumps(va, ensure_ascii=False),
                                                   json.dumps(vb, ensure_ascii=False)))
    return issues


def load_result(directory, stem):
    """Return (result dict or None, vrm path or None)."""
    result_path = directory / (stem + ".json")
    if not result_path.exists():
        return None, None
    result = json.loads(result_path.read_text(encoding="utf-8-sig"))
    vrm = directory / (stem + ".vrm")
    return result, (vrm if vrm.exists() else None)


def compare_pair(dir_a, stem_a, dir_b, stem_b, patterns=None, subs=None):
    if patterns is None:
        patterns = volatile_patterns()
    if subs is None:
        subs = volatile_subs()
    ra, va = load_result(dir_a, stem_a)
    rb, vb = load_result(dir_b, stem_b)
    row = {"a": stem_a, "b": stem_b, "issues": []}
    issues = row["issues"]
    if ra is None or rb is None:
        issues.append("result json missing in " + ("A" if ra is None else "B"))
        return row
    if ra.get("ok") != rb.get("ok"):
        issues.append("ok %s vs %s" % (ra.get("ok"), rb.get("ok")))
    if ra.get("failure") != rb.get("failure"):
        issues.append("failure text differs: %r vs %r" % (ra.get("failure"), rb.get("failure")))
    wa, wb = ra.get("warnings") or [], rb.get("warnings") or []
    if wa != wb:
        first = next((i for i, (x, y) in enumerate(zip(wa, wb)) if x != y), min(len(wa), len(wb)))
        issues.append("warnings differ (%d vs %d), first at #%d: %r vs %r" % (
            len(wa), len(wb), first, wa[first] if first < len(wa) else None, wb[first] if first < len(wb) else None))
    row["ok"] = ra.get("ok")
    row["warnings"] = len(wa)
    if (va is None) != (vb is None):
        issues.append("vrm missing in " + ("A" if va is None else "B"))
        return row
    if va is None:
        return row
    file_a, json_a, bin_a = read_glb(va)
    file_b, json_b, bin_b = read_glb(vb)
    ea, eb = expressions(json_a), expressions(json_b)
    row["expressions"] = len(ea)
    issues += compare_expressions(ea, eb)
    sa, sb = strip_volatile(json_a, patterns, subs=subs), strip_volatile(json_b, patterns, subs=subs)
    ha, hb = normalized_json_hash(sa), normalized_json_hash(sb)
    row["json_hash"] = ha[:16]
    row["bin_hash"] = sha256(bin_a)[:16]
    if ha != hb:
        paths = diff_paths(sa, sb)
        issues.append("normalized JSON chunk differs at %d paths: %s" % (len(paths), ", ".join(paths[:MAX_PATHS])))
    if sha256(bin_a) != sha256(bin_b):
        issues.append("BIN chunk differs (%d vs %d bytes)" % (len(bin_a), len(bin_b)))
    row["file_equal"] = file_a == file_b
    row["bytes"] = len(file_a)
    return row


def result_stems(directory):
    # <ID>_open.json records the board opening, not an export.
    return {p.stem for p in directory.glob("*.json") if not p.stem.endswith("_open")}


def plan_pairs(args):
    if args.runs:
        directory, run_a, run_b = Path(args.runs[0]), args.runs[1], args.runs[2]
        if not directory.is_dir():
            raise OSError("%s is not a directory" % directory)
        suffix = "_%s.json" % run_a
        return [(directory, p.name[:-len(suffix)] + "_" + run_a, directory, p.name[:-len(suffix)] + "_" + run_b)
                for p in sorted(directory.glob("*" + suffix))]
    dir_a, dir_b = Path(args.dirs[0]), Path(args.dirs[1])
    for directory in (dir_a, dir_b):
        if not directory.is_dir():
            raise OSError("%s is not a directory" % directory)
    pairs = []
    for stem in sorted(result_stems(dir_a) | result_stems(dir_b)):
        both = (dir_a / (stem + ".json")).exists() and (dir_b / (stem + ".json")).exists()
        if args.ignore_missing and not both:
            continue
        pairs.append((dir_a, stem, dir_b, stem))
    return pairs


def print_rows(rows):
    for row in rows:
        mark = "OK  " if not row["issues"] else "DIFF"
        print("%s %s vs %s  ok=%s warn=%s expr=%s file_equal=%s" % (
            mark, row["a"], row["b"], row.get("ok"), row.get("warnings"), row.get("expressions"), row.get("file_equal")))
        for issue in row["issues"]:
            print("     - " + issue)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("dirs", nargs="*", type=Path, metavar="DIR", help="result directories A and B")
    parser.add_argument("--runs", nargs=3, metavar=("DIR", "RUN_A", "RUN_B"),
                        help="compare two runs inside one result directory")
    parser.add_argument("--diff-paths", nargs=2, type=Path, metavar=("VRM_A", "VRM_B"),
                        help="print every glb JSON path that differs between two files")
    parser.add_argument("--ignore-missing", action="store_true",
                        help="skip results that exist in only one of the two directories")
    parser.add_argument("--json", action="store_true", help="print the full result as JSON")
    try:
        args = parser.parse_args(argv)
    except SystemExit as exit_:
        return 0 if exit_.code == 0 else 2
    modes = sum((bool(args.dirs), bool(args.runs), bool(args.diff_paths)))
    if modes != 1 or (args.dirs and len(args.dirs) != 2):
        parser.print_usage(sys.stderr)
        print("ERROR pass two result directories, --runs or --diff-paths", file=sys.stderr)
        return 2
    try:
        if args.diff_paths:
            _, json_a, _ = read_glb(args.diff_paths[0])
            _, json_b, _ = read_glb(args.diff_paths[1])
            paths = diff_paths(json_a, json_b)
            for path in paths:
                print(path)
            return 1 if paths else 0
        patterns, subs = volatile_patterns(), volatile_subs()
        rows = [compare_pair(*pair, patterns=patterns, subs=subs) for pair in plan_pairs(args)]
    except (OSError, ValueError) as error:
        print("ERROR " + str(error), file=sys.stderr)
        return 2
    mismatched = [row for row in rows if row["issues"]]
    if args.json:
        print(json.dumps({"pairs": len(rows), "mismatched": len(mismatched), "rows": rows}, ensure_ascii=False, indent=1))
    else:
        print_rows(rows)
        print("%d pairs, %d mismatched" % (len(rows), len(mismatched)))
    return 0 if rows and not mismatched else 1


if __name__ == "__main__":
    raise SystemExit(main())
