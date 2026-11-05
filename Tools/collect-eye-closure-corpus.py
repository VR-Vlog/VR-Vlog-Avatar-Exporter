"""Collect the names an avatar uses for its eyes, without opening Unity.

For each prefab this follows the asset references (prefabs, animator
controllers, animation clips, expression menus, mesh assets, FBX models) and
writes one JSON file with:

  meshes   BlendShape names per mesh, and for each shape the side its moved
           vertices sit on (x of their centre relative to the mesh's own
           centre line; avatars face +Z, so the avatar's left is -X in Unity
           and +X in the FBX file as most exporters write it)
  clips    animation clips that set BlendShapes, with the controller state
           and layer names that play them and the gesture conditions
  menus    expression menu control names and their parameters

The corpus stays outside the repository: it holds the names of real avatars.
Tools/summarize-eye-closure-corpus.py reads it for the thresholds and words
that Editor/EyeClosureSides.cs relies on.

  uv run --no-project python Tools/collect-eye-closure-corpus.py \
      --project C:/path/to/UnityProject --prefab Assets/A/A.prefab --out DIR/A.json
  uv run --no-project python Tools/collect-eye-closure-corpus.py --jobs JOBS.tsv --out DIR
      (JOBS.tsv: id<TAB>project<TAB>prefab, one avatar per line)
"""
import argparse
import json
import re
import struct
import sys
import zlib
from pathlib import Path

GUID = re.compile(rb"guid: ([0-9a-f]{32})")
FOLLOW = {".prefab", ".controller", ".overridecontroller", ".anim", ".asset", ".fbx", ".unity"}
DOC = re.compile(rb"^--- !u!(\d+) &(-?\d+)", re.M)


def unquote(value):
    value = value.strip()
    if value.startswith('"') and value.endswith('"'):
        body = value[1:-1]
        try:
            return json.loads('"' + body.replace("\\x", "\\u00") + '"')
        except ValueError:
            return body
    if value.startswith("'") and value.endswith("'"):
        return value[1:-1].replace("''", "'")
    return value


class Project:
    def __init__(self, root):
        self.root = Path(root)
        self._guids = None

    def guid_path(self, guid):
        if self._guids is None:
            self._guids = {}
            for top in ("Assets", "Packages"):
                base = self.root / top
                if not base.exists():
                    continue
                for meta in base.rglob("*.meta"):
                    try:
                        head = meta.read_bytes()[:400]
                    except OSError:
                        continue
                    found = GUID.search(head)
                    if found:
                        self._guids[found.group(1).decode()] = meta.with_suffix("")
        return self._guids.get(guid)

    def closure(self, start):
        seen, order, stack = set(), [], [self.root / start]
        while stack:
            path = stack.pop()
            key = str(path).lower()
            if key in seen or not path.exists() or path.suffix.lower() not in FOLLOW:
                continue
            seen.add(key)
            order.append(path)
            if path.suffix.lower() == ".fbx":
                continue
            try:
                data = path.read_bytes()
            except OSError:
                continue
            if not data.startswith(b"%YAML"):
                continue
            for guid in set(GUID.findall(data)):
                target = self.guid_path(guid.decode())
                if target is not None:
                    stack.append(target)
        return order


# ---- FBX (binary) -------------------------------------------------------

def _fbx_props(data, pos, count):
    props = []
    for _ in range(count):
        kind = chr(data[pos]); pos += 1
        if kind in "YCILFD":
            size = {"Y": 2, "C": 1, "I": 4, "L": 8, "F": 4, "D": 8}[kind]
            fmt = {"Y": "<h", "C": "<?", "I": "<i", "L": "<q", "F": "<f", "D": "<d"}[kind]
            props.append(struct.unpack_from(fmt, data, pos)[0]); pos += size
        elif kind in "fdlib":
            length, encoding, clen = struct.unpack_from("<III", data, pos); pos += 12
            raw = data[pos:pos + clen]; pos += clen
            if encoding == 1:
                raw = zlib.decompress(raw)
            fmt = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "?"}[kind]
            props.append(struct.unpack("<%d%s" % (length, fmt), raw[:length * struct.calcsize(fmt)]))
        elif kind in "SR":
            (length,) = struct.unpack_from("<I", data, pos); pos += 4
            raw = data[pos:pos + length]; pos += length
            props.append(raw.decode("utf-8", "replace") if kind == "S" else raw)
        else:
            raise ValueError("FBX property " + kind)
    return props, pos


def _fbx_nodes(data, pos, end, wide):
    nodes = []
    head = "<QQQB" if wide else "<IIIB"
    hsize = 25 if wide else 13
    while pos < end:
        node_end, count, _, name_len = struct.unpack_from(head, data, pos)
        if node_end == 0:
            break
        name = data[pos + hsize:pos + hsize + name_len].decode("ascii", "replace")
        props, cursor = _fbx_props(data, pos + hsize + name_len, count)
        children = _fbx_nodes(data, cursor, node_end, wide) if cursor < node_end else []
        nodes.append((name, props, children))
        pos = node_end
    return nodes


def _child(node, name):
    return next((c for c in node[2] if c[0] == name), None)


def fbx_shapes(path):
    data = path.read_bytes()
    if not data.startswith(b"Kaydara FBX Binary"):
        return []
    version = struct.unpack_from("<I", data, 23)[0]
    top = _fbx_nodes(data, 27, len(data), version >= 7500)
    objects = next((n for n in top if n[0] == "Objects"), None)
    connections = next((n for n in top if n[0] == "Connections"), None)
    if objects is None or connections is None:
        return []
    info = {}
    for node in objects[2]:
        if len(node[1]) >= 3:
            info[node[1][0]] = (node[0], node[1][1].split("\x00\x01")[0], node[1][2], node)
    parents = {}
    for c in connections[2]:
        if c[0] == "C" and c[1][0] == "OO":
            parents.setdefault(c[1][1], []).append(c[1][2])
    children = {}
    for child, ps in parents.items():
        for p in ps:
            children.setdefault(p, []).append(child)
    meshes = []
    for gid, (kind, name, cls, node) in info.items():
        if kind != "Geometry" or cls != "Mesh":
            continue
        verts = _child(node, "Vertices")
        base = verts[1][0] if verts else ()
        xs = base[0::3]
        centre = (min(xs) + max(xs)) / 2 if xs else 0.0
        model = next((info[p][1] for p in parents.get(gid, []) if p in info and info[p][0] == "Model"), name)
        shapes = []
        for deformer in children.get(gid, []):
            if deformer not in info or info[deformer][2] != "BlendShape":
                continue
            for channel in children.get(deformer, []):
                if channel not in info or info[channel][2] != "BlendShapeChannel":
                    continue
                side, deltas = None, {}
                for shape in children.get(channel, []):
                    if shape in info and info[shape][2] == "Shape":
                        side = _shape_side(info[shape][3], base, centre)
                        deltas = _shape_deltas(info[shape][3])
                        break
                shapes.append({"name": info[channel][1], "side": side, "_deltas": deltas})
        if shapes:
            splits = _splits(shapes)
            for shape in shapes:
                del shape["_deltas"]
            meshes.append({"file": path.name, "mesh": model, "shapes": shapes, "splits": splits})
    return meshes


def _shape_deltas(node):
    indexes = _child(node, "Indexes"); deltas = _child(node, "Vertices")
    if indexes is None or deltas is None:
        return {}
    idx, d = indexes[1][0], deltas[1][0]
    moves = {idx[i]: (d[3 * i], d[3 * i + 1], d[3 * i + 2]) for i in range(len(idx))}
    largest = max((x * x + y * y + z * z for x, y, z in moves.values()), default=0)
    return {v: m for v, m in moves.items() if m[0] ** 2 + m[1] ** 2 + m[2] ** 2 > largest * 1e-4}


def _splits(shapes):
    """Bilateral shapes that one +X shape plus one -X shape reproduce.

    Name-blind: every shape whose moved weight sits on both sides is tried
    against the one-sided shapes moving mostly the same vertices. The
    residual is |dA + dB - dS|^2 / |dS|^2 over the moved vertices."""
    def overlap(a, b):
        return sum(1 for v in a if v in b) / len(a) if a else 0
    one_sided = [(i, s) for i, s in enumerate(shapes) if s["side"] is not None and s["_deltas"] and (s["side"] >= .9 or s["side"] <= .1)]
    result = []
    for i, both in enumerate(shapes):
        if both["side"] is None or not .3 <= both["side"] <= .7 or len(both["_deltas"]) < 8:
            continue
        moved = both["_deltas"]
        plus = [(j, s) for j, s in one_sided if s["side"] >= .9 and overlap(s["_deltas"], moved) >= .7]
        minus = [(j, s) for j, s in one_sided if s["side"] <= .1 and overlap(s["_deltas"], moved) >= .7]
        if not plus or not minus:
            continue
        scale = sum(x * x + y * y + z * z for x, y, z in moved.values())
        best = None
        for j, a in plus[:40]:
            for k, b in minus[:40]:
                error = 0.0
                for v in set(moved) | set(a["_deltas"]) | set(b["_deltas"]):
                    s, p, q = moved.get(v, (0, 0, 0)), a["_deltas"].get(v, (0, 0, 0)), b["_deltas"].get(v, (0, 0, 0))
                    error += (p[0] + q[0] - s[0]) ** 2 + (p[1] + q[1] - s[1]) ** 2 + (p[2] + q[2] - s[2]) ** 2
                residual = error / scale if scale else 9
                if best is None or residual < best[2]:
                    best = (a["name"], b["name"], residual)
        if best and best[2] < .35:
            result.append({"both": both["name"], "plus": best[0], "minus": best[1], "residual": round(best[2], 3)})
    return result


def _shape_side(node, base, centre):
    """Share of the moved weight on +X of the mesh centre (0..1), or None."""
    indexes = _child(node, "Indexes"); deltas = _child(node, "Vertices")
    if indexes is None or deltas is None:
        return None
    idx, d = indexes[1][0], deltas[1][0]
    moves = [(idx[i], (d[3 * i] ** 2 + d[3 * i + 1] ** 2 + d[3 * i + 2] ** 2) ** .5) for i in range(len(idx))]
    if not moves:
        return None
    largest = max(m for _, m in moves)
    if largest <= 0:
        return None
    plus = minus = 0.0
    for vertex, move in moves:
        if move < largest * .05 or 3 * vertex >= len(base):
            continue
        if base[3 * vertex] >= centre:
            plus += move
        else:
            minus += move
    total = plus + minus
    return round(plus / total, 3) if total else None


# ---- Unity YAML ---------------------------------------------------------

def yaml_docs(data):
    marks = list(DOC.finditer(data))
    for i, mark in enumerate(marks):
        end = marks[i + 1].start() if i + 1 < len(marks) else len(data)
        yield int(mark.group(1)), int(mark.group(2)), data[mark.end():end].decode("utf-8", "replace")


def field(text, name):
    found = re.search(r"^\s*" + name + r": ?(.*)$", text, re.M)
    return unquote(found.group(1)) if found else None


def ref(text, name):
    found = re.search(r"^\s*" + name + r": \{fileID: (-?\d+)(?:, guid: ([0-9a-f]{32}))?", text, re.M)
    return (int(found.group(1)), found.group(2)) if found else (0, None)


def yaml_mesh_shapes(path, data):
    meshes = []
    for cls, _, text in yaml_docs(data):
        if cls != 43:
            continue
        names = [unquote(n) for n in re.findall(r"^\s+- serializedVersion: 2\n\s+name: (.*)$", text.split("m_Shapes:", 1)[-1].split("m_BindPose", 1)[0], re.M)]
        if names:
            meshes.append({"file": path.name, "mesh": field(text, "m_Name"), "shapes": [{"name": n, "side": None} for n in names]})
    return meshes


def clip_curves(data):
    name = None
    curves = []
    for cls, _, text in yaml_docs(data):
        if cls != 74:
            continue
        name = field(text, "m_Name")
        for block in re.split(r"\n  - serializedVersion: \d+\n",text.split("m_FloatCurves:", 1)[-1].split("m_PPtrCurves:", 1)[0]):
            attribute = field(block, "attribute")
            if not attribute or not attribute.startswith("blendShape."):
                continue
            values = [float(v) for v in re.findall(r"^\s+value: (-?[\d.eE+-]+)$", block, re.M)]
            curves.append({"path": field(block, "path") or "", "shape": attribute[len("blendShape."):],
                           "max": max(values) if values else None, "min": min(values) if values else None})
    return name, curves


def controller_states(project, data):
    """(layer, state, conditions, clip path) for every state with a motion."""
    docs = {fid: (cls, text) for cls, fid, text in yaml_docs(data)}
    layer_of, entered = {}, {}
    for fid, (cls, text) in docs.items():
        if cls == 91:
            for block in text.split("m_AnimatorLayers:", 1)[-1].split("\n  - serializedVersion")[1:]:
                machine = ref(block, "m_StateMachine")[0]
                layer_of[machine] = field(block, "m_Name")
    for fid, (cls, text) in docs.items():
        if cls == 1101:
            dst = ref(text, "m_DstState")[0]
            conds = re.findall(r"m_ConditionMode: (\d+)\n\s+m_ConditionEvent: (.*)\n\s+m_EventTreshold: (.*)", text)
            entered.setdefault(dst, []).append([{"mode": int(m), "param": unquote(p), "value": float(v)} for m, p, v in conds])
    state_layer = {}
    for fid, (cls, text) in docs.items():
        if cls == 1107:
            name = layer_of.get(fid, field(text, "m_Name"))
            for state in re.findall(r"m_State: \{fileID: (-?\d+)\}", text):
                state_layer[int(state)] = name
    result = []
    for fid, (cls, text) in docs.items():
        if cls == 1102:
            motion_id, guid = ref(text, "m_Motion")
            motions = []
            if guid:
                motions.append(guid)
            elif motion_id in docs and docs[motion_id][0] == 206:
                motions += re.findall(r"m_Motion: \{fileID: \d+, guid: ([0-9a-f]{32})", docs[motion_id][1])
            for g in motions:
                target = project.guid_path(g)
                if target is not None:
                    result.append({"layer": state_layer.get(fid), "state": field(text, "m_Name"),
                                   "conditions": entered.get(fid, []), "clip": str(target)})
    return result


def menu_controls(data):
    controls = []
    for cls, _, text in yaml_docs(data):
        if cls != 114 or "controls:" not in text:
            continue
        for block in text.split("controls:", 1)[1].split("\n  - name: ")[1:]:
            name = unquote(block.split("\n", 1)[0])
            parameter = re.search(r"parameter:\n\s+name: (.*)", block)
            value = re.search(r"\n\s+value: (.*)", block)
            controls.append({"name": name, "parameter": unquote(parameter.group(1)) if parameter else None,
                             "value": float(value.group(1)) if value else None})
    return controls


_projects = {}
_fbx_cache = {}


def collect(project_root, prefab):
    project = _projects.setdefault(project_root, Project(project_root))
    meshes, clips, menus, states = [], {}, [], []
    for path in project.closure(prefab):
        suffix = path.suffix.lower()
        try:
            if suffix == ".fbx":
                key = str(path).lower()
                if key not in _fbx_cache:
                    _fbx_cache[key] = fbx_shapes(path)
                meshes += _fbx_cache[key]
                continue
            data = path.read_bytes()
        except (OSError, ValueError, struct.error, zlib.error) as error:
            print("skip", path, error, file=sys.stderr)
            continue
        if suffix == ".anim":
            name, curves = clip_curves(data)
            if curves:
                clips[str(path)] = {"name": name, "curves": curves, "states": []}
        elif suffix in (".controller", ".overridecontroller"):
            states += controller_states(project, data)
        elif suffix == ".asset":
            meshes += yaml_mesh_shapes(path, data)
            menus += menu_controls(data)
    for state in states:
        clip = clips.get(state.pop("clip"))
        if clip is not None:
            clip["states"].append(state)
    return {"meshes": meshes, "clips": list(clips.values()), "menus": menus}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project"); parser.add_argument("--prefab"); parser.add_argument("--jobs")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    jobs = []
    if args.jobs:
        for line in Path(args.jobs).read_text(encoding="utf-8").splitlines():
            if line.strip() and not line.startswith("#"):
                jobs.append(tuple(line.split("\t")[:3]))
        out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    else:
        jobs.append((Path(args.out).stem, args.project, args.prefab))
        out = Path(args.out).parent; out.mkdir(parents=True, exist_ok=True)
    for job_id, project, prefab in jobs:
        result = collect(project, prefab)
        (out / (job_id + ".json")).write_text(json.dumps(result, ensure_ascii=False, indent=1), encoding="utf-8")
        print(job_id, len(result["meshes"]), "meshes", len(result["clips"]), "clips", len(result["menus"]), "menu controls", flush=True)


if __name__ == "__main__":
    main()
