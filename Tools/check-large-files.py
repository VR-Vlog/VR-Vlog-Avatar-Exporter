"""Keep model files, packages, archives and files over 1 MiB out of tracked public source.

Existing large documentation assets are listed with their current size as the
ceiling, so a pull request cannot grow them or add new ones without editing this list.
"""
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LIMIT = 1024 * 1024
BLOCKED_SUFFIXES = {
    ".vrm", ".vrma", ".fbx", ".blend", ".obj", ".glb", ".gltf", ".dae", ".3ds", ".max", ".ma", ".mb",
    ".abc", ".pmx", ".pmd", ".vmd", ".unitypackage", ".zip", ".7z", ".rar", ".gz", ".tgz",
}
APPROVED_LARGE = {
    "Documentation~/Verification/export-notes-graphical.png": 1283440,
    "Schema/LilToon234Catalogue.json": 1218892,
    "Website/assets/capture-sunset.webp": 1353906,
    "Website/assets/vr-vlog-phone-framed.png": 1368988,
    "Website/assets/vr-vlog-screenshot-ocean-background.png": 2297861,
}


def inspect(name, size):
    findings = []
    if Path(name).suffix.lower() in BLOCKED_SUFFIXES:
        findings.append("model, package or archive file")
    if size > APPROVED_LARGE.get(name, LIMIT):
        findings.append("file over 1 MiB" if name not in APPROVED_LARGE else "approved large file grew")
    return findings


def tracked_sizes(root=ROOT):
    names = subprocess.check_output(["git", "-C", str(root), "ls-files", "-z"]).decode("utf-8").split("\0")
    for name in filter(None, names):
        path = root / name
        # A tracked file removed from the working tree has nothing left to publish.
        if path.is_file() or path.is_symlink():
            yield name, path.lstat().st_size


if __name__ == "__main__":
    findings = [(name, label) for name, size in tracked_sizes() for label in inspect(name, size)]
    for name, label in findings:
        print(f"{name}: {label}")
    if findings:
        raise SystemExit(1)
    print("Large file check passed (no models, packages or archives; files up to 1 MiB).")
