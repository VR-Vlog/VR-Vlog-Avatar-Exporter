"""Compare two exports of the same avatar made with different installed lilToon versions.

Export A comes from a Unity project with lilToon X; export B comes from the same project after
upgrading it to lilToon 2.3.4. The only allowed difference is
`asset.extras.VRVLOG_lilToonInstalledVersion`. Everything else must match exactly: the whole
glTF JSON (floats compared with ==) and the bytes of every bufferView, including the raw DEFLATE
chunks of `VRVLOG_materials_liltoon`. The exporter's per-export placeholder morph target names
(`__VRVlog_BlinkNone_<GUID>`, `__VRVlog_Empty_<GUID>`) are renumbered before comparing. Exporters
before 2026-10-18 used a new GUID there on every export; newer ones derive the name from the
renderer path and source name, so the renumbering only matters for older files.

Pass two .glb/.vrm files or two directories (files are paired by relative path). Exit codes:
0 = identical except the allowed key, 1 = differences or version mismatches,
2 = usage, I/O or format errors.
"""
import argparse
import hashlib
import json
import re
import struct
import sys
import zlib
from pathlib import Path

ALLOWED_KEY = "VRVLOG_lilToonInstalledVersion"
EXTENSION = "VRVLOG_materials_liltoon"
GLB_MAGIC = b"glTF"
JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
SUFFIXES = (".glb", ".vrm")
MAX_REPORTED = 500
# Exporters before 2026-10-18 named their placeholder morph targets with a new GUID on
# every export (BlinkExportSession, ExportOptimizationBindings); newer ones use a hash of
# the renderer path and source name. They are renumbered in order of appearance, so the
# structure still has to match while the 32-digit part itself is ignored.
RANDOM_NAME = re.compile(r"^(__VRVlog_(?:BlinkNone|Empty)_)[0-9a-f]{32}$")
_MISSING = object()


def read_glb(source):
    """Return (json_dict, bin_bytes) from a GLB path or bytes; raise ValueError when malformed."""
    data = bytes(source) if isinstance(source, (bytes, bytearray, memoryview)) else Path(source).read_bytes()
    if len(data) < 12:
        raise ValueError("GLB is shorter than its 12-byte header")
    magic, version, length = struct.unpack_from("<4sII", data, 0)
    if magic != GLB_MAGIC:
        raise ValueError("GLB magic is %r, expected b'glTF'" % magic)
    if version != 2:
        raise ValueError("GLB version is %d, expected 2" % version)
    if length != len(data):
        raise ValueError("GLB header length %d does not match the file size %d" % (length, len(data)))
    chunks = []
    offset = 12
    while offset < length:
        if offset + 8 > length:
            raise ValueError("GLB chunk header at byte %d is truncated" % offset)
        size, kind = struct.unpack_from("<II", data, offset)
        start = offset + 8
        if start + size > length:
            raise ValueError("GLB chunk at byte %d extends past the end of the file" % offset)
        chunks.append((kind, data[start:start + size]))
        offset = start + size
    if not chunks or chunks[0][0] != JSON_CHUNK:
        raise ValueError("GLB first chunk is not JSON (0x4E4F534A)")
    try:
        document = json.loads(chunks[0][1].decode("utf-8"))
    except (UnicodeDecodeError, ValueError) as error:
        raise ValueError("GLB JSON chunk is not valid UTF-8 JSON: %s" % error) from None
    if not isinstance(document, dict):
        raise ValueError("GLB JSON chunk is not an object")
    binary = b""
    if len(chunks) > 1:
        if chunks[1][0] != BIN_CHUNK:
            raise ValueError("GLB second chunk is not BIN (0x004E4942)")
        binary = chunks[1][1]
    return document, binary


def _sha256(data):
    return None if data is None else hashlib.sha256(data).hexdigest()


def _is_number(value):
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _equal_scalar(a, b):
    # 1 and 1.0 are equal (JSON writers vary); bool never equals a number.
    if _is_number(a) and _is_number(b):
        return a == b
    return type(a) is type(b) and a == b


def _named_keys(items):
    """Return the item names when every item is a dict with a unique string name, else None."""
    names = []
    for item in items:
        if not isinstance(item, dict) or not isinstance(item.get("name"), str):
            return None
        names.append(item["name"])
    return names if len(set(names)) == len(names) else None


class _Collector:
    def __init__(self, cap=MAX_REPORTED):
        self.items = []
        self.total = 0
        self.cap = cap

    def add(self, path, a, b, reason):
        self.total += 1
        if len(self.items) < self.cap:
            self.items.append({"path": path, "a": None if a is _MISSING else a,
                               "b": None if b is _MISSING else b, "reason": reason})


def _label(path, key, index, item):
    # Material entries carry both the index and the name, e.g. materials[3:Body].
    if key == "materials" and isinstance(item, dict) and isinstance(item.get("name"), str):
        return "%s[%d:%s]" % (path, index, item["name"])
    return "%s[%d]" % (path, index)


def _diff(a, b, path, key, out):
    if a is _MISSING or b is _MISSING:
        out.add(path, a, b, "missing")
    elif isinstance(a, dict) and isinstance(b, dict):
        for name in sorted(set(a) | set(b), key=str):
            child = "%s.%s" % (path, name) if path else name
            _diff(a.get(name, _MISSING), b.get(name, _MISSING), child, name, out)
    elif isinstance(a, list) and isinstance(b, list):
        _diff_list(a, b, path, key, out)
    elif isinstance(a, (dict, list)) or isinstance(b, (dict, list)):
        out.add(path, a, b, "type")
    elif not _equal_scalar(a, b):
        same_kind = (_is_number(a) and _is_number(b)) or type(a) is type(b)
        out.add(path, a, b, "value" if same_kind else "type")


def _diff_list(a, b, path, key, out):
    names_a, names_b = _named_keys(a), _named_keys(b)
    if names_a is not None and names_b is not None and (a or b):
        index_a = {name: i for i, name in enumerate(names_a)}
        index_b = {name: i for i, name in enumerate(names_b)}
        for name in names_a + [n for n in names_b if n not in index_a]:
            i = index_a.get(name, index_b.get(name))
            if key == "materials":
                child = "%s[%d:%s]" % (path, i, name)
            else:
                child = "%s[%s]" % (path, name)
            item_a = a[index_a[name]] if name in index_a else _MISSING
            item_b = b[index_b[name]] if name in index_b else _MISSING
            _diff(item_a, item_b, child, None, out)
        common_a = [n for n in names_a if n in index_b]
        common_b = [n for n in names_b if n in index_a]
        if common_a != common_b:
            # Name matching must not hide a reordering; indices are referenced elsewhere.
            out.add(path, names_a, names_b, "order")
        return
    if len(a) != len(b):
        out.add(path, len(a), len(b), "length")
    for i in range(max(len(a), len(b))):
        item_a = a[i] if i < len(a) else _MISSING
        item_b = b[i] if i < len(b) else _MISSING
        _diff(item_a, item_b, _label(path, key, i, item_a if item_a is not _MISSING else item_b), None, out)


def _strip_allowed(document, allowed_key):
    """Return (copy without the allowed key, its value or _MISSING)."""
    copy = json.loads(json.dumps(document))
    extras = copy.get("asset", {}).get("extras") if isinstance(copy.get("asset"), dict) else None
    if isinstance(extras, dict) and allowed_key in extras:
        return copy, extras.pop(allowed_key)
    return copy, _MISSING


def _normalize_random_names(document):
    """Replace per-export GUID names in place; return how many distinct names were replaced."""
    seen = {}

    def walk(value):
        if isinstance(value, dict):
            for key in value:
                value[key] = walk(value[key])
        elif isinstance(value, list):
            for i, item in enumerate(value):
                value[i] = walk(item)
        elif isinstance(value, str):
            match = RANDOM_NAME.match(value)
            if match:
                return seen.setdefault(value, "%s#%d" % (match.group(1), len(seen)))
        return value

    walk(document)
    return len(seen)


def _canonical_sha(document):
    text = json.dumps(document, sort_keys=True, separators=(",", ":"), ensure_ascii=False)
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def _view_bytes(document, binary, index):
    views = document.get("bufferViews") or []
    if index >= len(views):
        return None
    view = views[index]
    if not isinstance(view, dict):
        raise ValueError("bufferViews[%d] is not an object" % index)
    buffer = view.get("buffer", 0)
    if buffer != 0:
        raise ValueError("bufferViews[%d] uses buffer %r; only the GLB BIN buffer is supported" % (index, buffer))
    start = view.get("byteOffset", 0)
    length = view.get("byteLength")
    if not _is_number(start) or not _is_number(length) or start < 0 or length < 0 or start + length > len(binary):
        raise ValueError("bufferViews[%d] range is outside the BIN chunk" % index)
    return binary[int(start):int(start + length)]


def _chunk_refs(value, key=""):
    """Yield every integer found under a key whose name contains 'chunk'."""
    if isinstance(value, dict):
        for name, child in value.items():
            yield from _chunk_refs(child, name if "chunk" in str(name).lower() else key)
    elif isinstance(value, list):
        for child in value:
            yield from _chunk_refs(child, key)
    elif _is_number(value) and isinstance(value, int) and key:
        yield value


def _usages(document):
    """Map bufferView index -> human labels, plus bufferView index -> lilToon chunk index."""
    usage = {}
    chunk_of_view = {}

    def add(view, label):
        if isinstance(view, int) and not isinstance(view, bool):
            usage.setdefault(view, []).append(label)

    for i, image in enumerate(document.get("images") or []):
        if isinstance(image, dict):
            name = image.get("name")
            add(image.get("bufferView"), "image[%d:%s]" % (i, name) if isinstance(name, str) else "image[%d]" % i)
    for i, accessor in enumerate(document.get("accessors") or []):
        if isinstance(accessor, dict):
            add(accessor.get("bufferView"), "accessor[%d]" % i)
            sparse = accessor.get("sparse")
            if isinstance(sparse, dict):
                for part in ("indices", "values"):
                    if isinstance(sparse.get(part), dict):
                        add(sparse[part].get("bufferView"), "accessor[%d].sparse.%s" % (i, part))
    extension = (document.get("extensions") or {}).get(EXTENSION)
    if isinstance(extension, dict):
        textures = extension.get("textures") or []
        for k, chunk in enumerate(extension.get("chunks") or []):
            if not isinstance(chunk, dict):
                continue
            view = chunk.get("bufferView")
            add(view, "liltoon.chunk[%d]" % k)
            if isinstance(view, int) and not isinstance(view, bool):
                chunk_of_view[view] = k
            for j, texture in enumerate(textures):
                if k in set(_chunk_refs(texture)):
                    name = texture.get("name") if isinstance(texture, dict) else None
                    add(view, "liltoon.texture[%d:%s]" % (j, name) if isinstance(name, str) else "liltoon.texture[%d]" % j)
    return usage, chunk_of_view


def _decoded_sha(data):
    if data is None:
        return None
    try:
        return _sha256(zlib.decompress(data, -15))
    except zlib.error:
        return "undecodable"


def _binary_differences(doc_a, bin_a, doc_b, bin_b):
    count = max(len(doc_a.get("bufferViews") or []), len(doc_b.get("bufferViews") or []))
    usage_a, chunks_a = _usages(doc_a)
    usage_b, chunks_b = _usages(doc_b)
    differences = []
    for index in range(count):
        data_a = _view_bytes(doc_a, bin_a, index)
        data_b = _view_bytes(doc_b, bin_b, index)
        if data_a == data_b:
            continue
        labels = list(dict.fromkeys(usage_a.get(index, []) + usage_b.get(index, [])))
        record = {"bufferView": index, "usage": labels,
                  "a_sha256": _sha256(data_a), "b_sha256": _sha256(data_b),
                  "a_length": None if data_a is None else len(data_a),
                  "b_length": None if data_b is None else len(data_b)}
        if index in chunks_a or index in chunks_b:
            record["a_decoded_sha256"] = _decoded_sha(data_a) if index in chunks_a else None
            record["b_decoded_sha256"] = _decoded_sha(data_b) if index in chunks_b else None
        differences.append(record)
    return differences


def compare(a_bytes, b_bytes, allowed_key=ALLOWED_KEY):
    """Compare two GLB payloads (bytes or paths); every difference except allowed_key is reported."""
    doc_a, bin_a = read_glb(a_bytes)
    doc_b, bin_b = read_glb(b_bytes)
    stripped_a, value_a = _strip_allowed(doc_a, allowed_key)
    stripped_b, value_b = _strip_allowed(doc_b, allowed_key)
    normalized = {"a": _normalize_random_names(stripped_a), "b": _normalize_random_names(stripped_b)}
    out = _Collector()
    if value_a is _MISSING or value_b is _MISSING:
        out.add("asset.extras." + allowed_key, value_a, value_b, "missing")
    _diff(stripped_a, stripped_b, "", None, out)
    binary = _binary_differences(doc_a, bin_a, doc_b, bin_b)
    return {
        "allowed": {"key": allowed_key,
                    "a": None if value_a is _MISSING else value_a,
                    "b": None if value_b is _MISSING else value_b},
        "normalized_random_names": normalized,
        "json_differences": out.items,
        "json_difference_count": out.total,
        "binary_differences": binary,
        "bin_sha256": {"a": _sha256(bin_a), "b": _sha256(bin_b)},
        "json_sha256": {"a": _canonical_sha(stripped_a), "b": _canonical_sha(stripped_b)},
        "identical_except_allowed": out.total == 0 and not binary,
    }


def _exports(directory):
    directory = Path(directory)
    return {path.relative_to(directory).as_posix(): path
            for path in sorted(directory.rglob("*"))
            if path.is_file() and path.suffix.lower() in SUFFIXES}


def compare_dirs(a_dir, b_dir, allowed_key=ALLOWED_KEY):
    """Pair .glb/.vrm files by relative path; a file on only one side is a failure."""
    files_a, files_b = _exports(a_dir), _exports(b_dir)
    pairs = {name: compare(files_a[name], files_b[name], allowed_key)
             for name in sorted(set(files_a) & set(files_b))}
    missing_in_a = sorted(set(files_b) - set(files_a))
    missing_in_b = sorted(set(files_a) - set(files_b))
    ok = (bool(pairs) and not missing_in_a and not missing_in_b
          and all(report["identical_except_allowed"] for report in pairs.values()))
    return {"pairs": pairs, "missing_in_a": missing_in_a, "missing_in_b": missing_in_b, "ok": ok}


def version_mismatches(pairs, expect_a, expect_b):
    mismatches = []
    for name, report in pairs.items():
        for side, expected in (("a", expect_a), ("b", expect_b)):
            if expected is not None and report["allowed"][side] != expected:
                mismatches.append({"file": name, "side": side, "expected": expected,
                                   "actual": report["allowed"][side]})
    return mismatches


def _summary(result, limit=20):
    lines = []
    for name in result["missing_in_a"]:
        lines.append("MISSING in A: " + name)
    for name in result["missing_in_b"]:
        lines.append("MISSING in B: " + name)
    for item in result["version_mismatches"]:
        lines.append("VERSION %s side %s: expected %r, found %r"
                     % (item["file"], item["side"], item["expected"], item["actual"]))
    shown = 0
    for name, report in result["pairs"].items():
        allowed = report["allowed"]
        state = "OK" if report["identical_except_allowed"] else "DIFF"
        lines.append("%s %s: %s %r -> %r, json %d, binary %d"
                     % (state, name, allowed["key"], allowed["a"], allowed["b"],
                        report["json_difference_count"], len(report["binary_differences"])))
        for item in report["json_differences"]:
            if shown >= limit:
                break
            shown += 1
            lines.append("  json %s (%s): %s != %s" % (item["path"], item["reason"],
                                                       json.dumps(item["a"])[:80], json.dumps(item["b"])[:80]))
        for item in report["binary_differences"]:
            if shown >= limit:
                break
            shown += 1
            lines.append("  bufferView %d %s: %s != %s" % (item["bufferView"], ",".join(item["usage"]) or "unused",
                                                         item["a_sha256"], item["b_sha256"]))
    if not result["pairs"]:
        lines.append("No .glb/.vrm pairs were compared")
    lines.append("RESULT " + ("OK" if result["ok"] else "FAIL"))
    return "\n".join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("a", type=Path, help="export made with the older lilToon (file or directory)")
    parser.add_argument("b", type=Path, help="export made after upgrading to lilToon 2.3.4 (file or directory)")
    parser.add_argument("--report", type=Path, help="write the full JSON report here")
    parser.add_argument("--expect-a-version", help="required value of the allowed key in A")
    parser.add_argument("--expect-b-version", help="required value of the allowed key in B")
    parser.add_argument("--allowed-key", default=ALLOWED_KEY)
    try:
        args = parser.parse_args(argv)
    except SystemExit as exit_:
        return 0 if exit_.code == 0 else 2
    try:
        if args.a.is_dir() and args.b.is_dir():
            result = compare_dirs(args.a, args.b, args.allowed_key)
        elif args.a.is_file() and args.b.is_file():
            name = args.a.name if args.a.name == args.b.name else args.a.name + " <> " + args.b.name
            report = compare(args.a, args.b, args.allowed_key)
            result = {"pairs": {name: report}, "missing_in_a": [], "missing_in_b": [],
                      "ok": report["identical_except_allowed"]}
        else:
            print("ERROR A and B must both be existing files or both be directories", file=sys.stderr)
            return 2
    except (OSError, ValueError) as error:
        print("ERROR " + str(error), file=sys.stderr)
        return 2
    result["version_mismatches"] = version_mismatches(result["pairs"], args.expect_a_version, args.expect_b_version)
    result["ok"] = result["ok"] and not result["version_mismatches"]
    print(_summary(result))
    if args.report:
        try:
            args.report.parent.mkdir(parents=True, exist_ok=True)
            args.report.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        except OSError as error:
            print("ERROR " + str(error), file=sys.stderr)
            return 2
    return 0 if result["ok"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
