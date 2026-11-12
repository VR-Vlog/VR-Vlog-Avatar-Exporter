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
  --allow-added-extension NAME (repeatable, with two directories only)
      For a change that adds the root extension NAME and should change nothing else. NAME must
      start with VRVLOG_. It cannot be used with --runs: two runs of the same package must have
      the same extensions, so an extension in only one of them is a difference. When B
      has extensions[NAME] and A does not, it is removed from B before hashing, together with
      NAME in extensionsUsed and in asset.extras.VRVLOG_requiredAppVersions when A does not
      have those entries (a list or object left empty is removed when A does not have it).
      NAME that A also has, and every other extension, are compared as before; warnings,
      expressions and the BIN chunk are never relaxed. Each row lists the removed names with
      the length of their JSON as information only, and the summary counts the pairs where an
      extension was removed.

Checked for each pair:
  1. success or failure, and the failure text
  2. warnings (the entries of the export details log), in order. A GUID in a warning is not
     compared where it belongs to a merged bone of the same output: where a node has it at the
     end of the bone's name and the warning has that name in front of it. Which nodes have it is
     compared in its place (see warning_parts). Every other GUID is compared as it is.
  3. expressions: count, names, preset or custom, order, and every field of each expression
     (morphTargetBinds with node, index and weight, materialColorBinds, textureTransformBinds,
     isBinary, overrideBlink / overrideLookAt / overrideMouth and anything else). VRM 0.x
     blendShapeGroups are compared the same way.
  4. sha256 of the glb JSON chunk after removing VOLATILE paths and masking the VOLATILE_SUB
     parts of string values, and sha256 of the BIN chunk. A value with a masked part in only
     one of the two is a difference, even when the text after masking is the same.
Whole-file byte equality is reported as information only.

Exit codes: 0 = every pair matches, 1 = differences, missing results or no pairs,
2 = usage, I/O or format errors.
"""
import argparse
import copy
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
# asset.extras key that maps each VRVLOG_ extension in extensionsUsed to the lowest app version
# (Formats/Runtime/AvatarFormatVersions.cs).
REQUIRED_APP_VERSIONS = "VRVLOG_requiredAppVersions"

# The form of the GUID that the avatar's build steps put in the names they generate.
GUID = r"[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}"

# What the exporter puts behind the name of a bone for the end joint it adds below it (and " 2",
# " 3" when the name is taken).
END_JOINT = r"(-VRVlog Spring Endpoint(?: [0-9]+)?)?"

# The name of a node for a bone that the build steps merged into another armature, or for the end
# joint below it: the bone's name with "$" (group 1) and a GUID that is new on every build
# (group 2). A warning about the bone has the same name and GUID.
MERGED_BONE = re.compile(r"(.*\$)(" + GUID + r")" + END_JOINT, re.DOTALL)

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
    # A bone that the avatar's build steps merge into another armature gets "$" and a new random
    # GUID at the end of its name on every build, before the exporter sees it. The name in front
    # of "$" and everything else about the node are still compared. Found the same way, on
    # avatars whose outfits are merged at build time. Two more forms of the same name were found
    # by comparing two exports of the same avatars: an object the build steps add for a constraint
    # has "ConstraintRef " between "$" and the GUID, and the end joint the exporter adds below
    # such a bone has the bone's name followed by "-VRVlog Spring Endpoint" (and " 2", " 3" when
    # the name is taken).
    ("/nodes/*/name", r"\$(ConstraintRef )?" + GUID + END_JOINT + r"$", r"$\1<guid>\2"),
    # A spring is named after its first bone, so it carries the same GUID.
    ("/extensions/VRMC_springBone/springs/*/name", r"\$" + GUID + r"$", r"$<guid>"),
    # A clip that the avatar's build steps write anew on every build gets a name that ends with a
    # new 32-digit ID, a new GUID and a number. The text in front of them and the clip itself are
    # still compared, and the rest of the file refers to clips by index. Found by comparing two
    # exports of the same avatars made in two launches of the editor; two exports made in one
    # launch give the same names.
    ("/extensions/VRVLOG_avatar_animator/clips/*/name", r"__[0-9a-f]{32}__" + GUID + r"_[0-9]+$", r"__<generated>"),
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


def strip_volatile(value, patterns=None, path="", subs=None, masked=None):
    """Copy of value without the dictionary entries whose path matches a VOLATILE pattern, and
    with the VOLATILE_SUB replacements applied to matching string values. The path of each value
    that a replacement changed is added to the list masked, when it is given."""
    if patterns is None:
        patterns = volatile_patterns()
    if subs is None:
        subs = volatile_subs()
    if isinstance(value, dict):
        return {key: strip_volatile(item, patterns, "%s/%s" % (path, key), subs, masked)
                for key, item in value.items() if not any(p.match("%s/%s" % (path, key)) for p in patterns)}
    if isinstance(value, list):
        return [strip_volatile(item, patterns, "%s/%d" % (path, index), subs, masked)
                for index, item in enumerate(value)]
    if isinstance(value, str):
        before = value
        for path_pattern, regex, replacement in subs:
            if path_pattern.match(path):
                value = regex.sub(replacement, value)
        if masked is not None and value != before:
            masked.append(path)
    return value


def merged_bones(document):
    """GUID -> [(the bone's name with "$", node index)] for the nodes of the document that have the
    name of a merged bone or of the end joint below one (MERGED_BONE)."""
    bones = {}
    for index, node in enumerate(document.get("nodes") or []):
        name = node.get("name") if isinstance(node, dict) else None
        match = MERGED_BONE.fullmatch(name) if isinstance(name, str) else None
        if match:
            bones.setdefault(match.group(2), []).append((match.group(1), index))
    return bones


def warning_parts(warning, bones):
    """The warning cut at each GUID that belongs to a merged bone of the same output (bones is the
    merged_bones of that output): the text up to the GUID, the indices of that bone's nodes, the
    text up to the next such GUID, ..., the rest of the text. A GUID belongs to a bone when a node
    has it at the end of the bone's name and the warning has that name in front of it.

    The build steps make that GUID anew on every build, so it differs between any two exports and
    says nothing by itself; which nodes have it says what the warning is about. Two bones with the
    same name differ only in that GUID, so the indices also tell which of them a warning is about.
    Every other GUID stays in the text and is compared as it is, because it is not known to change
    on every export. The indices are a tuple, which no warning read from JSON can be, so text is
    never taken for them."""
    if not isinstance(warning, str):
        return [warning]
    parts, last = [], 0
    for match in re.finditer(GUID, warning):
        nodes = tuple(index for name, index in bones.get(match.group(0), ())
                      if warning.endswith(name, 0, match.start()))
        if nodes:
            parts += [warning[last:match.start()], nodes]
            last = match.end()
    return parts + [warning[last:]]


def warning_issues(warnings_a, warnings_b, json_a, json_b):
    """The issue, in a list, for the first warning that differs between two results, or an empty
    list. The warnings are compared in order as warning_parts gives them, each with the merged
    bones of its own output. A result without an output is given an empty document: it has no
    bones, so every GUID in its warnings is compared as it is."""
    bones_a, bones_b = merged_bones(json_a), merged_bones(json_b)
    parts_a = [warning_parts(warning, bones_a) for warning in warnings_a]
    parts_b = [warning_parts(warning, bones_b) for warning in warnings_b]
    if parts_a == parts_b:
        return []
    first = next((i for i, (x, y) in enumerate(zip(parts_a, parts_b)) if x != y), min(len(parts_a), len(parts_b)))

    def shown(warnings, parts):
        """The warning as it was written, with the nodes that stand for its GUIDs."""
        if first >= len(warnings):
            return "None"
        nodes = [str(list(part)) for part in parts[first] if isinstance(part, tuple)]
        return repr(warnings[first]) + (" (nodes %s)" % " / ".join(nodes) if nodes else "")

    return ["warnings differ (%d vs %d), first at #%d: %s vs %s" % (
        len(warnings_a), len(warnings_b), first, shown(warnings_a, parts_a), shown(warnings_b, parts_b))]


def remove_added_extensions(json_a, json_b, names):
    """Return (copy of json_b, [{"name", "bytes"}]) without the root extensions in names that B has
    and A does not. Their extensionsUsed entries and asset.extras.VRVLOG_requiredAppVersions
    entries are removed only when A does not have them either; a list or object left empty is
    removed only when A does not have that key (asset.extras too, which the exporter creates for
    the versions when it is missing). "bytes" is the length of the extension's compact JSON.
    Anything that A also has is kept and compared."""
    def member(container, key):
        return container.get(key) if isinstance(container, dict) else None

    json_b = copy.deepcopy(json_b)
    removed = []
    ext_a, ext_b = member(json_a, "extensions"), member(json_b, "extensions")
    used_a, used_b = member(json_a, "extensionsUsed"), member(json_b, "extensionsUsed")
    asset_b = member(json_b, "asset")
    extras_a, extras_b = member(member(json_a, "asset"), "extras"), member(asset_b, "extras")
    req_a, req_b = member(extras_a, REQUIRED_APP_VERSIONS), member(extras_b, REQUIRED_APP_VERSIONS)
    emptied_used = emptied_req = False
    for name in names:
        # The entries that go with an extension are only removed together with the extension itself.
        if not (isinstance(ext_b, dict) and name in ext_b) or (isinstance(ext_a, dict) and name in ext_a):
            continue
        text = json.dumps(ext_b.pop(name), sort_keys=True, ensure_ascii=False, separators=(",", ":"))
        removed.append({"name": name, "bytes": len(text.encode("utf-8"))})
        if isinstance(used_b, list) and name in used_b and not (isinstance(used_a, list) and name in used_a):
            # One entry goes with one extension; a second entry with the same name stays a difference.
            used_b.remove(name)
            emptied_used = not used_b
        if isinstance(req_b, dict) and name in req_b and not (isinstance(req_a, dict) and name in req_a):
            del req_b[name]
            emptied_req = not req_b
    # Only a container that this removal emptied goes away; one that B already had empty stays a difference.
    if removed and ext_b == {} and "extensions" not in json_a:
        del json_b["extensions"]
    if emptied_used and "extensionsUsed" not in json_a:
        del json_b["extensionsUsed"]
    if emptied_req and not (isinstance(extras_a, dict) and REQUIRED_APP_VERSIONS in extras_a):
        del extras_b[REQUIRED_APP_VERSIONS]
        if extras_b == {} and not (isinstance(member(json_a, "asset"), dict) and "extras" in json_a["asset"]):
            del asset_b["extras"]
    return json_b, removed


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


def compare_pair(dir_a, stem_a, dir_b, stem_b, patterns=None, subs=None, allowed_extensions=()):
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
    row["ok"] = ra.get("ok")
    row["warnings"] = len(wa)
    file_a, json_a, bin_a = read_glb(va) if va is not None else (b"", {}, b"")
    file_b, json_b, bin_b = read_glb(vb) if vb is not None else (b"", {}, b"")
    issues += warning_issues(wa, wb, json_a, json_b)
    if (va is None) != (vb is None):
        issues.append("vrm missing in " + ("A" if va is None else "B"))
        return row
    if va is None:
        return row
    ea, eb = expressions(json_a), expressions(json_b)
    row["expressions"] = len(ea)
    issues += compare_expressions(ea, eb)
    if allowed_extensions:
        json_b, removed = remove_added_extensions(json_a, json_b, allowed_extensions)
        if removed:
            row["allowed_extensions"] = removed
    pa, pb = [], []
    sa = strip_volatile(json_a, patterns, subs=subs, masked=pa)
    sb = strip_volatile(json_b, patterns, subs=subs, masked=pb)
    ha, hb = normalized_json_hash(sa), normalized_json_hash(sb)
    row["json_hash"] = ha[:16]
    row["bin_hash"] = sha256(bin_a)[:16]
    if ha != hb:
        paths = diff_paths(sa, sb)
        issues.append("normalized JSON chunk differs at %d paths: %s" % (len(paths), ", ".join(paths[:MAX_PATHS])))
    elif set(pa) != set(pb):
        # The text is the same after masking, but only one of the two had the part that is masked:
        # the other already was the text that the replacement gives.
        paths = sorted(set(pa) ^ set(pb))
        issues.append("VOLATILE_SUB masked a value in only one of the two at %d paths: %s" % (
            len(paths), ", ".join(paths[:MAX_PATHS])))
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
        for extension in row.get("allowed_extensions", []):
            print("     i added extension allowed: %s (%d bytes of JSON)" % (extension["name"], extension["bytes"]))


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
    parser.add_argument("--allow-added-extension", action="append", default=[], metavar="NAME",
                        help="remove the root extension NAME from B before hashing when A does not have it"
                             " (repeatable; not used by --diff-paths)")
    try:
        args = parser.parse_args(argv)
    except SystemExit as exit_:
        return 0 if exit_.code == 0 else 2
    modes = sum((bool(args.dirs), bool(args.runs), bool(args.diff_paths)))
    if modes != 1 or (args.dirs and len(args.dirs) != 2):
        parser.print_usage(sys.stderr)
        print("ERROR pass two result directories, --runs or --diff-paths", file=sys.stderr)
        return 2
    if args.runs and args.allow_added_extension:
        print("ERROR --allow-added-extension cannot be used with --runs", file=sys.stderr)
        return 2
    not_vrvlog = [name for name in args.allow_added_extension if not name.startswith("VRVLOG_")]
    if not_vrvlog:
        print("ERROR --allow-added-extension takes only VRVLOG_ extensions: " + ", ".join(not_vrvlog), file=sys.stderr)
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
        rows = [compare_pair(*pair, patterns=patterns, subs=subs, allowed_extensions=args.allow_added_extension)
                for pair in plan_pairs(args)]
    except (OSError, ValueError) as error:
        print("ERROR " + str(error), file=sys.stderr)
        return 2
    mismatched = [row for row in rows if row["issues"]]
    if args.json:
        report = {"pairs": len(rows), "mismatched": len(mismatched), "rows": rows}
        text = json.dumps(report, ensure_ascii=False, indent=1)
        try:
            text.encode(getattr(sys.stdout, "encoding", None) or "utf-8")
        except UnicodeEncodeError:
            # The console cannot show every character: escape them so the output stays JSON.
            text = json.dumps(report, indent=1)
        print(text)
    else:
        print_rows(rows)
        print("%d pairs, %d mismatched" % (len(rows), len(mismatched)))
        if args.allow_added_extension:
            allowed = sum(1 for row in rows if row.get("allowed_extensions"))
            print("allowed extension removed in %d of %d pairs" % (allowed, len(rows)))
    return 0 if rows and not mismatched else 1


if __name__ == "__main__":
    # Names and warnings can hold characters that the console's code page cannot encode; print
    # those escaped instead of stopping in the middle of the report.
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(errors="backslashreplace")
    raise SystemExit(main())
