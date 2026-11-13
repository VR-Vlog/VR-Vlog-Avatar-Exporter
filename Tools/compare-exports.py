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
  --allow-kept-morphs (with two directories only)
      For a change that makes VRVLOG_avatar_animator write the BlendShapes that the FX moves, which
      keeps their morph targets in the file. A morph target that a "morph" target of B's extension
      names, on a mesh whose A does not have a target of that name, is removed from B (primitive
      targets, targetNames, mesh and node weights; a list or extras left empty is removed, as the
      exporter writes none) and the expression binds after it are numbered again. Then, on both
      sides, the extension's "morph" targets, their tracks and the omitted
      "blendShape" entry are removed and the other tracks are numbered again. When a morph target
      was removed, accessors and bufferViews are compared by their content (their own fields and
      bytes) in place of their index, and the BIN chunk through them, and the MB in the warning
      about packed vertex data is not compared; the Console line about unused BlendShapes left
      out is dropped from both sides when B's count is A's less the removed targets. Each row lists, as
      information only, the removed morph targets and the morph targets and omitted blendShape
      count of each side; the summary counts the pairs with morph targets.

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
     one of the two is a difference, even when the text after masking is the same. The parts
     that change only between launches of the editor (VOLATILE_SUB_BETWEEN_LAUNCHES) are masked
     when two directories are compared and compared as they are with --runs.
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
]

# Like VOLATILE_SUB, for string values that change only from one launch of the editor to the next.
# Two directories hold the results of two launches, so these are masked when two directories are
# compared. The two runs that --runs compares come from one launch and give the same value, so
# they are compared there as they are.
VOLATILE_SUB_BETWEEN_LAUNCHES = [
    # A clip that the avatar's build steps write anew gets a name that ends with a new 32-digit ID,
    # a new GUID and a number. The text in front of them and the clip itself are still compared,
    # and the rest of the file refers to clips by index. Found by comparing two exports of the same
    # avatars made in two launches of the editor.
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
    with the replacements of subs (VOLATILE_SUB when it is not given) applied to matching string
    values. The path of each value that a replacement changed is added to the list masked, when
    it is given."""
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


ANIMATOR = "VRVLOG_avatar_animator"


def _member(container, key):
    return container.get(key) if isinstance(container, dict) else None


def _index(value):
    return value if isinstance(value, int) and not isinstance(value, bool) and value >= 0 else None


def _target_names(mesh):
    names = _member(_member(mesh, "extras"), "targetNames")
    if names is None:
        primitives = _member(mesh, "primitives") or []
        names = _member(_member(primitives[0] if primitives else None, "extras"), "targetNames")
    return names if isinstance(names, list) else None


def _filtered(values, keep):
    return [value for i, value in enumerate(values) if i >= len(keep) or keep[i]]


def _binds(document):
    """Every {node, index} morph bind: VRM expressions and the expression animations."""
    binds = []
    expressions = _member(_member(_member(document, "extensions"), "VRMC_vrm"), "expressions") or {}
    for group in ("preset", "custom"):
        for expression in (_member(expressions, group) or {}).values():
            binds += [b for b in (_member(expression, "morphTargetBinds") or []) if isinstance(b, dict)]

    def collect(value):
        if isinstance(value, list):
            for item in value:
                collect(item)
        elif isinstance(value, dict):
            if "node" in value and "index" in value:
                binds.append(value)
            for item in value.values():
                collect(item)
    collect(_member(_member(document, "extensions"), "VRVLOG_expression_animations"))
    return binds


def _filter_key(owner, key, keep):
    """Filter owner[key] by keep; a list left empty is removed, as the exporter writes none."""
    if isinstance(_member(owner, key), list):
        owner[key] = _filtered(owner[key], keep)
        if not owner[key]:
            del owner[key]


def _filter_names(owner, keep):
    """Filter owner.extras.targetNames by keep; targetNames and then extras left empty are removed."""
    extras = _member(owner, "extras")
    if isinstance(extras, dict):
        _filter_key(extras, "targetNames", keep)
        if not extras:
            del owner["extras"]


def remove_kept_morphs(json_a, json_b):
    """Return (copy of json_b, number of morph targets removed). A morph target is removed from B
    when a "morph" target of B's VRVLOG_avatar_animator names it and the same mesh of A has no
    target of its name: the export kept it only for the extension. Binds after it are numbered
    again; a bind to a removed target is left as it is, to show as a difference."""
    json_b = copy.deepcopy(json_b)
    nodes = _member(json_b, "nodes") or []
    meshes_a, meshes_b = _member(json_a, "meshes") or [], _member(json_b, "meshes") or []
    named = {}
    for target in _member(_member(_member(json_b, "extensions"), ANIMATOR), "targets") or []:
        node, morph = _index(_member(target, "node")), _index(_member(target, "morph"))
        if _member(target, "property") != "morph" or node is None or morph is None or node >= len(nodes):
            continue
        mesh = _index(_member(nodes[node], "mesh"))
        if mesh is not None and mesh < len(meshes_b):
            named.setdefault(mesh, set()).add(morph)
    maps, removed = {}, 0
    for mesh, morphs in sorted(named.items()):
        names_b = _target_names(meshes_b[mesh])
        names_a = _target_names(meshes_a[mesh]) if mesh < len(meshes_a) else None
        if names_b is None:
            continue
        gone = {i for i in morphs if i < len(names_b) and names_b[i] not in (names_a or [])}
        if not gone:
            continue
        keep = [i not in gone for i in range(len(names_b))]
        number, mapping = 0, []
        for kept in keep:
            mapping.append(number if kept else None)
            number += kept
        maps[mesh] = mapping
        removed += len(gone)
        body = meshes_b[mesh]
        for primitive in _member(body, "primitives") or []:
            _filter_key(primitive, "targets", keep)
            _filter_names(primitive, keep)
        _filter_key(body, "weights", keep)
        _filter_names(body, keep)
        for node in nodes:
            if _index(_member(node, "mesh")) == mesh:
                _filter_key(node, "weights", keep)
    for bind in _binds(json_b):
        node, index = _index(bind.get("node")), _index(bind.get("index"))
        mesh = _index(_member(nodes[node], "mesh")) if node is not None and node < len(nodes) else None
        mapping = maps.get(mesh)
        if mapping is not None and index is not None and index < len(mapping) and mapping[index] is not None:
            bind["index"] = mapping[index]
    return json_b, removed


def remove_morph_tracks(document):
    """Return (copy of document, morph target count, omitted blendShape count) without the "morph"
    targets of VRVLOG_avatar_animator, their tracks and the omitted "blendShape" entry; the other
    tracks point at the targets numbered again."""
    document = copy.deepcopy(document)
    extension = _member(_member(document, "extensions"), ANIMATOR)
    targets = _member(extension, "targets")
    if not isinstance(targets, list):
        return document, 0, 0
    mapping, number = [], 0
    for target in targets:
        morph = _member(target, "property") == "morph"
        mapping.append(None if morph else number)
        number += not morph
    extension["targets"] = [t for t, m in zip(targets, mapping) if m is not None]
    for clip in _member(extension, "clips") or []:
        tracks = _member(clip, "tracks")
        if not isinstance(tracks, list):
            continue
        kept = []
        for track in tracks:
            target = _index(_member(track, "target"))
            if target is not None and target < len(mapping):
                if mapping[target] is None:
                    continue
                track["target"] = mapping[target]
            kept.append(track)
        clip["tracks"] = kept
    omitted = _member(extension, "omitted")
    blend = 0
    if isinstance(omitted, list):
        blend = sum(_member(o, "count") or 0 for o in omitted if _member(o, "kind") == "blendShape")
        extension["omitted"] = [o for o in omitted if _member(o, "kind") != "blendShape"]
    return document, len(targets) - number, blend


def content_addressed(document, binary):
    """Return a copy of document where every accessor and bufferView reference is the hash of what
    it points at (its own fields and bytes), without the accessors, bufferViews and buffers lists."""
    document = copy.deepcopy(document)
    view_keys = []
    for view in _member(document, "bufferViews") or []:
        data = b""
        if isinstance(view, dict) and view.get("buffer", 0) == 0:
            start = view.get("byteOffset", 0)
            data = binary[start:start + view.get("byteLength", 0)]
        fields = {k: v for k, v in view.items() if k not in ("byteOffset", "buffer")} if isinstance(view, dict) else view
        view_keys.append("view:" + sha256(json.dumps(fields, sort_keys=True).encode("utf-8") + b"\0" + data)[:16])

    def view_key(owner):
        index = _index(owner.get("bufferView"))
        if index is not None and index < len(view_keys):
            owner["bufferView"] = view_keys[index]

    accessor_keys = []
    for accessor in _member(document, "accessors") or []:
        fields = copy.deepcopy(accessor)
        if isinstance(fields, dict):
            view_key(fields)
            sparse = _member(fields, "sparse")
            for part in (_member(sparse, "indices"), _member(sparse, "values")):
                if isinstance(part, dict):
                    view_key(part)
        accessor_keys.append("accessor:" + sha256(json.dumps(fields, sort_keys=True).encode("utf-8"))[:16])
    for key in ("accessors", "bufferViews", "buffers"):
        document.pop(key, None)

    def accessor_key(owner, key):
        index = _index(owner.get(key))
        if index is not None and index < len(accessor_keys):
            owner[key] = accessor_keys[index]

    for mesh in _member(document, "meshes") or []:
        for primitive in _member(mesh, "primitives") or []:
            attributes = _member(primitive, "attributes")
            if isinstance(attributes, dict):
                for key in list(attributes):
                    accessor_key(attributes, key)
            if isinstance(primitive, dict) and "indices" in primitive:
                accessor_key(primitive, "indices")
            for target in _member(primitive, "targets") or []:
                if isinstance(target, dict):
                    for key in list(target):
                        accessor_key(target, key)
    for skin in _member(document, "skins") or []:
        if isinstance(skin, dict) and "inverseBindMatrices" in skin:
            accessor_key(skin, "inverseBindMatrices")
    for animation in _member(document, "animations") or []:
        for sampler in _member(animation, "samplers") or []:
            for key in ("input", "output"):
                if isinstance(sampler, dict) and key in sampler:
                    accessor_key(sampler, key)

    def views_in(value):
        if isinstance(value, list):
            for item in value:
                views_in(item)
        elif isinstance(value, dict):
            if "bufferView" in value:
                view_key(value)
            for item in value.values():
                views_in(item)
    views_in(document)
    return document


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


# ExportNoteText.VertexDataLightened: the MB it saves changes with the morph targets B keeps.
VERTEX_NOTE = re.compile(r"^(メッシュの頂点データを詰めてVRMを)[0-9.]+(MB小さくしました)$")


def _masked_size(warning):
    return VERTEX_NOTE.sub(r"\1#\2", warning) if isinstance(warning, str) else warning


# UnusedMorphTargets' Console line: B leaves out the shapes A does, less the ones it kept.
UNUSED_NOTE = re.compile(r"^VR Vlog: left (\d+) unused BlendShapes out of the VRM: ", re.S)


def _without_unused_note(wa, wb, removed):
    """Drops the line from both sides when B's count is A's less the removed targets."""
    def notes(warnings):
        return [(i, int(m.group(1))) for i, w in enumerate(warnings) if isinstance(w, str) for m in [UNUSED_NOTE.match(w)] if m]
    na, nb = notes(wa), notes(wb)
    if len(na) > 1 or len(nb) > 1 or not na:
        return wa, wb
    if (na[0][1] if na else 0) - (nb[0][1] if nb else 0) != removed:
        return wa, wb
    drop = lambda warnings, found: [w for i, w in enumerate(warnings) if not found or i != found[0][0]]
    return drop(wa, na), drop(wb, nb)


def compare_pair(dir_a, stem_a, dir_b, stem_b, patterns=None, subs=None, allowed_extensions=(), kept_morphs=False):
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
    removed_morphs, morphs_a, morphs_b, blend_a, blend_b = 0, 0, 0, 0, 0
    if kept_morphs:
        json_b, removed_morphs = remove_kept_morphs(json_a, json_b)
        json_a, morphs_a, blend_a = remove_morph_tracks(json_a)
        json_b, morphs_b, blend_b = remove_morph_tracks(json_b)
        if morphs_a or morphs_b or removed_morphs:
            row["kept_morphs"] = {"removed": removed_morphs, "targets": [morphs_a, morphs_b], "blendShape": [blend_a, blend_b]}
        if removed_morphs:
            wa, wb = [_masked_size(w) for w in wa], [_masked_size(w) for w in wb]
            wa, wb = _without_unused_note(wa, wb, removed_morphs)
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
    if removed_morphs:
        json_a, json_b = content_addressed(json_a, bin_a), content_addressed(json_b, bin_b)
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
    if not removed_morphs and sha256(bin_a) != sha256(bin_b):
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
        if "kept_morphs" in row:
            kept = row["kept_morphs"]
            print("     i morph targets: %d removed from B, %d vs %d in the extension, omitted blendShape %d vs %d" % (
                kept["removed"], kept["targets"][0], kept["targets"][1], kept["blendShape"][0], kept["blendShape"][1]))


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
    parser.add_argument("--allow-kept-morphs", action="store_true",
                        help="remove from B the morph targets kept only for VRVLOG_avatar_animator, and the"
                             " extension's morph targets and tracks from both, before comparing")
    try:
        args = parser.parse_args(argv)
    except SystemExit as exit_:
        return 0 if exit_.code == 0 else 2
    modes = sum((bool(args.dirs), bool(args.runs), bool(args.diff_paths)))
    if modes != 1 or (args.dirs and len(args.dirs) != 2):
        parser.print_usage(sys.stderr)
        print("ERROR pass two result directories, --runs or --diff-paths", file=sys.stderr)
        return 2
    if args.runs and args.allow_kept_morphs:
        print("ERROR --allow-kept-morphs cannot be used with --runs", file=sys.stderr)
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
        patterns = volatile_patterns()
        subs = volatile_subs(VOLATILE_SUB if args.runs else VOLATILE_SUB + VOLATILE_SUB_BETWEEN_LAUNCHES)
        rows = [compare_pair(*pair, patterns=patterns, subs=subs, allowed_extensions=args.allow_added_extension,
                             kept_morphs=args.allow_kept_morphs)
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
        if args.allow_kept_morphs:
            kept = sum(1 for row in rows if row.get("kept_morphs"))
            print("morph targets in the extension in %d of %d pairs" % (kept, len(rows)))
    return 0 if rows and not mismatched else 1


if __name__ == "__main__":
    # Names and warnings can hold characters that the console's code page cannot encode; print
    # those escaped instead of stopping in the middle of the report.
    for stream in (sys.stdout, sys.stderr):
        stream.reconfigure(errors="backslashreplace")
    raise SystemExit(main())
