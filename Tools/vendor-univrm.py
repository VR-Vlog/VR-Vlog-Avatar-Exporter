"""Copy the parts of UniVRM the exporter needs into Editor/Vendor/UniVRM, renamed so they never meet the user's UniVRM.

The exporter writes VRM 1.0 with UniVRM's own code, but the user may have any version of UniVRM
installed in any way, or none. This script copies the runtime sources of UniGLTF and VRM-1.0 from a
UniVRM checkout and renames everything that is global in a Unity project:

* namespaces (UniGLTF -> VRVlog.LilToonExporter.Vendor.UniGLTF, ...) and assembly names
* asset GUIDs (derived from the upstream GUID, so re-running gives the same GUIDs)
* shader names, the Resources path, the menus and the Add Component entries

The assemblies compile only in the Editor (UNITY_EDITOR) and are not auto-referenced, so only the exporter can use them.
Importers, inspectors, menus, samples and tests are not copied.

Usage:
  git clone --depth 1 --branch v0.131.4 https://github.com/vrm-c/UniVRM.git <dir>
  python3 Tools/vendor-univrm.py --source <dir>            # write Editor/Vendor/UniVRM (delete the folder first)
  python3 Tools/vendor-univrm.py --source <dir> --check    # fail if the tree differs from a fresh copy
"""
import argparse
import filecmp
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / "Editor" / "Vendor" / "UniVRM"
PREFIX = "VRVlog.LilToonExporter.Vendor"
SHADER_PREFIX = "Hidden/VRVlog/LilToonExporter/Vendor/"
RESOURCES_DIR = "VRVlogLilToonExporterVendor"

# Upstream folder (under Packages/) -> folder under TARGET. Each copied folder keeps its own layout.
COPIES = [
    ("UniGLTF/Runtime", "UniGLTF/Runtime"),
    ("UniGLTF/UniUnlit/Runtime", "UniGLTF/UniUnlit/Runtime"),
    ("UniGLTF/UniUnlit/Shaders", "UniGLTF/UniUnlit/Shaders"),
    ("UniGLTF/Resources/UniGLTF", "UniGLTF/Shaders"),
    ("VRM10/Runtime", "VRM10/Runtime"),
    ("VRM10/MToon10/Runtime", "VRM10/MToon10/Runtime"),
    ("VRM10/MToon10/Shaders", "VRM10/MToon10/Shaders"),
    ("VRM10/vrmlib/Runtime", "VRM10/vrmlib/Runtime"),
]
# Scenes for UniVRM's own look development; not used by the exporter.
SKIPPED_DIRS = {"Scenes"}
SKIPPED_FILES = {".gitignore"}

ROOT_NAMESPACES = ("UniGLTF", "UniVRM10", "UniJSON", "UniHumanoid", "VrmLib", "VRM10")

SHADERS = {
    "VRM10/MToon10": SHADER_PREFIX + "MToon10",
    "VRM10/Universal Render Pipeline/MToon10": SHADER_PREFIX + "MToon10 URP",
    "UniGLTF/UniUnlit": SHADER_PREFIX + "UniUnlit",
    "Hidden/UniGLTF/NormalMapExporter": SHADER_PREFIX + "NormalMapExporter",
    "Hidden/UniGLTF/StandardMapExporter": SHADER_PREFIX + "StandardMapExporter",
    "Hidden/UniGLTF/StandardMapImporter": SHADER_PREFIX + "StandardMapImporter",
}

UPSTREAM_URL = "https://github.com/vrm-c/UniVRM"


def new_guid(guid):
    return hashlib.md5(f"vrvlog-liltoon-exporter-vendor:{guid}".encode()).hexdigest()


def read_text(path):
    data = path.read_bytes()
    bom = data.startswith(b"\xef\xbb\xbf")
    return data.decode("utf-8-sig"), bom


def write_text(path, text, bom=False):
    path.parent.mkdir(parents=True, exist_ok=True)
    data = text.encode("utf-8")
    path.write_bytes((b"\xef\xbb\xbf" if bom else b"") + data)


def collect(packages):
    files = []
    for source, target in COPIES:
        base = packages / source
        if not base.is_dir():
            raise SystemExit(f"missing upstream folder: {base}")
        for path in sorted(base.rglob("*")):
            rel = path.relative_to(base)
            if any(part in SKIPPED_DIRS for part in rel.parts) or path.name in SKIPPED_FILES:
                continue
            if path.is_dir() or path.name.endswith(".meta"):
                continue
            files.append((path, Path(target) / rel))
    # Folders need their own .meta files too.
    return files


def guid_of(meta):
    match = re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8-sig"), re.M)
    return match.group(1) if match else None


def namespace_pattern():
    roots = "|".join(ROOT_NAMESPACES)
    return re.compile(r"(\bnamespace\s+|\busing\s+(?:static\s+)?|\busing\s+\w+\s*=\s*|global::)(" + roots + r")\b")


NAMESPACE = namespace_pattern()
ASSEMBLY_ATTRIBUTE = re.compile(r"^\s*\[assembly:\s*InternalsVisibleTo\([^)]*\)\]\s*\r?\n", re.M)
MENU_ATTRIBUTE = re.compile(r"^(\s*)\[(?:UnityEditor\.)?MenuItem\([^\]]*\)\]\s*\r?\n", re.M)
CREATE_ASSET_MENU = re.compile(r"^(\s*)\[CreateAssetMenu\([^\]]*\)\]\s*\r?\n", re.M)
ADD_COMPONENT_MENU = re.compile(r"\[AddComponentMenu\(\"[^\"]*\"\)\]")
COMPONENT_CLASS = re.compile(
    r"^(?P<indent>[ \t]*)(?P<decl>(?:public |internal |sealed |abstract |partial )*class\s+\w+[^\n{]*:\s*(?:UnityEngine\.)?MonoBehaviour\b)",
    re.M)


def rewrite_cs(text):
    text = NAMESPACE.sub(lambda m: m.group(1) + PREFIX + "." + m.group(2), text)
    text = ASSEMBLY_ATTRIBUTE.sub("", text)
    text = MENU_ATTRIBUTE.sub("", text)
    text = CREATE_ASSET_MENU.sub("", text)
    text = ADD_COMPONENT_MENU.sub('[AddComponentMenu("")]', text)
    # Components without AddComponentMenu would still appear under Scripts/<namespace>.
    def hide(match):
        before = text[:match.start()].rstrip()
        if before.endswith('[AddComponentMenu("")]') or re.search(r'\[AddComponentMenu\(""\)\]\s*(\[[^\]]*\]\s*)*$', before):
            return match.group(0)
        return f'{match.group("indent")}[UnityEngine.AddComponentMenu("")]\n{match.group(0)}'
    text = COMPONENT_CLASS.sub(hide, text)
    for old, new in SHADERS.items():
        text = text.replace(f'"{old}"', f'"{new}"')
    text = text.replace('"UniHumanoid/T-Pose.pose"', f'"{RESOURCES_DIR}/UniHumanoid/T-Pose.pose"')
    return text


SHADER_NAME = re.compile(r'^(\s*Shader\s+)"([^"]+)"', re.M)
CUSTOM_EDITOR = re.compile(r'^\s*CustomEditor\s+"[^"]*"\s*\r?\n', re.M)


def rewrite_shader(text, rel):
    def rename(match):
        name = match.group(2)
        if name not in SHADERS:
            raise SystemExit(f"unknown shader {name} in {rel}")
        return f'{match.group(1)}"{SHADERS[name]}"'
    text = SHADER_NAME.sub(rename, text, count=1)
    # The inspectors are not vendored; never let the user's UniVRM inspector take these shaders.
    return CUSTOM_EDITOR.sub("", text)


def rewrite_asmdef(text, guids):
    data = json.loads(text)
    data["name"] = f"{PREFIX}.{data['name']}"
    data["rootNamespace"] = ""
    # Not Editor-only: Unity refuses AddComponent for MonoBehaviours of Editor-only assemblies
    # (the exporter adds Humanoid and Vrm10Instance to its copy). UNITY_EDITOR still keeps them out of builds.
    data["includePlatforms"] = []
    data["excludePlatforms"] = []
    data["defineConstraints"] = ["UNITY_EDITOR"] + [d for d in data.get("defineConstraints", []) if d != "UNITY_EDITOR"]
    data["autoReferenced"] = False
    data["references"] = [
        "GUID:" + guids[ref[5:]] if ref.startswith("GUID:") and ref[5:] in guids else ref
        for ref in data.get("references", [])]
    return json.dumps(data, indent=4, ensure_ascii=False) + "\n"


def rewrite_meta(text, guids):
    def remap(match):
        guid = match.group(1)
        return f"guid: {guids[guid]}" if guid in guids else match.group(0)
    text = re.sub(r"\bguid: ([0-9a-f]{32})", remap, text)
    # Script icons point at UniVRM's own icon textures, which are not vendored.
    text = re.sub(r"^(\s*icon: )\{fileID: \d+, guid: [0-9a-f]{32}, type: \d\}", r"\1{instanceID: 0}", text, flags=re.M)
    return text


def rewrite_asset(text, guids):
    return re.sub(r"\bguid: ([0-9a-f]{32})", lambda m: f"guid: {guids.get(m.group(1), m.group(1))}", text)


def generate(source, target):
    packages = source / "Packages"
    files = collect(packages)
    # Map every upstream GUID that is copied (files and the folders that contain them).
    guids = {}
    folders = set()
    for path, rel in files:
        guid = guid_of(path.with_name(path.name + ".meta"))
        if guid is None:
            raise SystemExit(f"missing meta for {path}")
        guids[guid] = new_guid(guid)
        parent = rel.parent
        while parent != Path("."):
            folders.add(parent)
            parent = parent.parent

    if target.exists() and any(target.iterdir()):
        raise SystemExit(f"{target} is not empty; delete the folder first so no stale file stays")
    resources = Path("UniGLTF/Runtime/UniHumanoid/Resources")
    for path, rel in files:
        if resources in rel.parents:
            rel = resources / RESOURCES_DIR / rel.relative_to(resources)
            for parent in [resources / RESOURCES_DIR]:
                folders.add(parent)
        out = target / rel
        meta_text, meta_bom = read_text(path.with_name(path.name + ".meta"))
        if path.suffix in {".cs", ".shader", ".hlsl", ".asmdef", ".asset"}:
            text, bom = read_text(path)
            if path.suffix == ".cs":
                text = rewrite_cs(text)
            elif path.suffix == ".shader":
                text = rewrite_shader(text, rel)
            elif path.suffix == ".asmdef":
                text = rewrite_asmdef(text, guids)
            else:
                text = rewrite_asset(text, guids)
            write_text(out, text, bom)
        else:
            out.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(path, out)
        write_text(out.with_name(out.name + ".meta"), rewrite_meta(meta_text, guids), meta_bom)

    for folder in sorted(folders):
        meta = target / folder.with_name(folder.name + ".meta")
        guid = new_guid(hashlib.md5(f"folder:{folder.as_posix()}".encode()).hexdigest())
        write_text(meta, f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n"
                         "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")

    commit = subprocess.run(["git", "-C", str(source), "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    version = json.loads((packages / "VRM10" / "package.json").read_text(encoding="utf-8-sig"))["version"]
    shutil.copyfile(source / "LICENSE.txt", target / "LICENSE.txt")
    write_text(target / "LICENSE.txt.meta", _text_meta("LICENSE.txt"))
    write_text(target / "VERSION.json", json.dumps({
        "upstream": UPSTREAM_URL, "version": version, "commit": commit,
        "namespacePrefix": PREFIX, "generator": "Tools/vendor-univrm.py"}, indent=2) + "\n")
    write_text(target / "VERSION.json.meta", _text_meta("VERSION.json"))
    return len(files), version, commit


def _text_meta(name):
    guid = new_guid(hashlib.md5(f"file:{name}".encode()).hexdigest())
    return (f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n"
            "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--source", type=Path, required=True, help="UniVRM checkout (the folder that has Packages/)")
    parser.add_argument("--check", action="store_true", help="compare a fresh copy with the tree instead of writing")
    args = parser.parse_args()
    if args.check:
        with tempfile.TemporaryDirectory() as temp:
            fresh = Path(temp) / "UniVRM"
            generate(args.source, fresh)
            diff = filecmp.dircmp(fresh, TARGET)
            problems = []
            def walk(d, prefix=""):
                problems.extend(prefix + n for n in d.left_only + d.right_only + d.diff_files)
                for name, sub in d.subdirs.items():
                    walk(sub, f"{prefix}{name}/")
            walk(diff)
            if problems:
                print("Editor/Vendor/UniVRM differs from a fresh copy:", *problems[:50], sep="\n  ")
                return 1
            print("Editor/Vendor/UniVRM matches the upstream copy")
            return 0
    count, version, commit = generate(args.source, TARGET)
    print(f"vendored {count} files from UniVRM {version} ({commit}) into {TARGET.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
