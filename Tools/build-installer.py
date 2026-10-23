"""Build the installer unitypackage that adds the VPM repository and the exporter to a project.

The unitypackage comes from VPMPackageAutoInstaller's own creator (a pinned, SHA-256
checked release file run with Node.js); this script only writes its config and checks the result.
"""
import argparse
import hashlib
import io
import json
import subprocess
import tarfile
import tempfile
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CREATOR_VERSION = "1.1.6"
CREATOR_URL = ("https://github.com/anatawa12/VPMPackageAutoInstaller/releases/download/v"
               + CREATOR_VERSION + "/creator.mjs")
CREATOR_SHA256 = "416709b3a70dd7b3023e84b04d077f6b38873b15fb7f1042c6900e72f00285af"
REPOSITORY_URL = "https://vrvlog.fun/exporter/index.json"
MAX_CREATOR_BYTES = 2 * 1024 * 1024
MAX_INSTALLER_BYTES = 2 * 1024 * 1024


def installer_config(package):
    version = package["version"]
    return {
        "vpmRepositories": [REPOSITORY_URL],
        "vpmDependencies": {package["name"]: "^" + version},
        # A beta installer must be able to resolve its own prerelease version.
        "includePrerelease": "-" in version,
        "minimumUnity": package["unity"],
    }


def verified_creator(data):
    if len(data) > MAX_CREATOR_BYTES or hashlib.sha256(data).hexdigest() != CREATOR_SHA256:
        raise ValueError("Installer creator SHA-256 mismatch")
    return data


def installer_configs(data):
    """Return every config.json the unitypackage would import."""
    if len(data) > MAX_INSTALLER_BYTES:
        raise ValueError("Installer is too large")
    entries = {}
    with tarfile.open(fileobj=io.BytesIO(data), mode="r:gz") as archive:
        for member in archive.getmembers():
            guid, _, name = member.name.lstrip("./").partition("/")
            if member.isfile() and name in {"pathname", "asset"}:
                entries.setdefault(guid, {})[name] = archive.extractfile(member).read()
    configs = []
    for files in entries.values():
        pathname = files.get("pathname", b"").decode("utf-8").splitlines()
        if pathname and pathname[0].endswith("/config.json") and "asset" in files:
            configs.append(json.loads(files["asset"].decode("utf-8-sig")))
    return configs


def check_installer(data, config):
    configs = installer_configs(data)
    if configs != [config]:
        raise ValueError("Installer config differs from the requested one: " + json.dumps(configs))


def build(package, output, creator=None):
    config = installer_config(package)
    if creator is None:
        with urllib.request.urlopen(CREATOR_URL, timeout=60) as response:
            creator = response.read(MAX_CREATOR_BYTES + 1)
    with tempfile.TemporaryDirectory(prefix="exporter-installer-") as directory:
        script = Path(directory) / "creator.mjs"
        script.write_bytes(verified_creator(creator))
        config_path = Path(directory) / "config.json"
        config_path.write_text(json.dumps(config), encoding="utf-8")
        result_path = Path(directory) / "installer.unitypackage"
        subprocess.run(["node", str(script), str(config_path), str(result_path)], timeout=120, check=True)
        data = result_path.read_bytes()
    check_installer(data, config)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(data)
    return config


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--version", help="Release version; must match package.json")
    args = parser.parse_args()
    package = json.loads((ROOT / "package.json").read_text(encoding="utf-8"))
    if args.version is not None and args.version != package["version"]:
        raise SystemExit("Installer version differs from package.json: " + args.version)
    config = build(package, args.output)
    print("Installer built: " + str(args.output) + " " + json.dumps(config))
