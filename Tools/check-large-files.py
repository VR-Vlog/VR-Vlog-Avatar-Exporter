"""Keep model files, packages, archives and files over 1 MiB out of tracked public source.

Sizes are read from the committed blobs, so line-ending conversion on checkout cannot
change the result. Existing large documentation assets are listed with their current
blob size as the ceiling, so a pull request cannot grow them or add new ones without
editing this list. Regenerating Schema/LilToon234Catalogue.json for a new lilToon
release needs its entry updated in the same pull request.
"""
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LIMIT = 1024 * 1024
REGULAR_MODES = {"100644", "100755"}
BLOCKED_SUFFIXES = {
    ".vrm", ".vrma", ".fbx", ".blend", ".obj", ".glb", ".gltf", ".dae", ".3ds", ".max", ".ma", ".mb",
    ".abc", ".pmx", ".pmd", ".vmd", ".usd", ".usda", ".usdc", ".usdz", ".stl", ".ply", ".bvh",
    ".unitypackage", ".apk", ".ipa", ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst",
}
APPROVED_LARGE = {
    "Documentation~/Verification/export-notes-graphical.png": 1283440,
    "Schema/LilToon234Catalogue.json": 1175183,
    "Website/assets/capture-sunset.webp": 1353906,
    "Website/assets/vr-vlog-phone-framed.png": 1368988,
    "Website/assets/vr-vlog-screenshot-ocean-background.png": 2297861,
}


def inspect(name, size, mode="100644"):
    findings = []
    if mode not in REGULAR_MODES:
        findings.append("not a regular file (submodule or symbolic link)")
    if Path(name).suffix.lower() in BLOCKED_SUFFIXES:
        findings.append("model, package or archive file")
    if size > APPROVED_LARGE.get(name, LIMIT):
        findings.append("file over 1 MiB" if name not in APPROVED_LARGE else "approved large file grew")
    return findings


def inspect_attributes(text):
    # Git LFS keeps the real file outside the repository, where the size check cannot see it.
    return ["Git LFS is not used in this repository"] if "filter=lfs" in text.replace(" ", "") else []


def git(root, *args, data=None):
    return subprocess.run(["git", "-C", str(root), *args], input=data, capture_output=True, check=True).stdout


def tracked_entries(root=ROOT):
    entries = []
    for entry in filter(None, git(root, "ls-files", "-s", "-z").split(b"\0")):
        meta, name = entry.split(b"\t", 1)
        mode, oid, _stage = meta.decode("ascii").split()
        entries.append((name.decode("utf-8"), mode, oid))
    blobs = [oid for _name, mode, oid in entries if mode in REGULAR_MODES]
    sizes = {}
    for line in git(root, "cat-file", "--batch-check", data=("\n".join(blobs) + "\n").encode()).decode().splitlines():
        oid, _type, size = line.split()
        sizes[oid] = int(size)
    return [(name, mode, oid, sizes.get(oid, 0)) for name, mode, oid in entries]


def findings(root=ROOT):
    found = []
    for name, mode, oid, size in tracked_entries(root):
        found += [(name, label) for label in inspect(name, size, mode)]
        if Path(name).name == ".gitattributes" and mode in REGULAR_MODES:
            found += [(name, label) for label in inspect_attributes(git(root, "cat-file", "blob", oid).decode("utf-8", "replace"))]
    return found


if __name__ == "__main__":
    found = findings()
    for name, label in found:
        print(f"{name}: {label}")
    if found:
        raise SystemExit(1)
    print("Large file check passed (no models, packages, archives, submodules or files over 1 MiB).")
