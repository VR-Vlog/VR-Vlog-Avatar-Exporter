"""Create one empty Unity test project per lilToon release, for the focused runner.

Each project is Unity 2022.3 with the Built-in render pipeline. It embeds the
official lilToon VPM release ZIP for that version, copies the optional packages
supplied by the caller (VRChat SDK / NDMF / Modular Avatar / AAO, and a project
UniVRM of any version; the exporter bundles its own UniVRM), and references this
exporter checkout with `file:` and `testables`.
lilToon is downloaded from its GitHub release (or a pre-filled cache); the ZIP
must match the SHA-256 listed below (or one given with --sha256) and is
recorded. Nothing is committed or installed into this repository.

With --unity the script opens each new project once in batch mode so Unity
resolves and imports the packages, then records the versions Unity actually
resolved (packages-lock.json, the package folders and ProjectVersion.txt).
"""
import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import time
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

import unity_packages

ROOT = Path(__file__).resolve().parents[1]
DEFAULT_VERSIONS = ('2.0.0', '2.1.0', '2.1.10', '2.2.1', '2.3.0', '2.3.4')
RELEASE_URL = 'https://github.com/lilxyzw/lilToon/releases/download/{0}/jp.lilxyzw.liltoon-{0}.zip'
# SHA-256 of the official release ZIPs, reviewed when each version was added.
RELEASE_SHA256 = {
    '1.10.3': '25264cf7f463a97110b7b7313e6a49ea14d76e515634319043379546294c65e0',
    '2.0.0': 'a45b411d9c433732d92c332114c3709e83161e4f9ec1bbe23725531337810004',
    '2.1.0': '3c18da9be8dce0b921459fddbbbae928c558f2b1888f588f26c173a1ac8ec836',
    '2.1.10': '975cb1e06597f3ce7405cff67073627ca88e8097fb49d1dae88a8141fdfabbd9',
    '2.2.1': '2aab5ddd64ba6aaed0278301c425bb3188ee8542251d38d9f18a12ff6f10f7b9',
    '2.3.0': '211c4e5c0168dfa58701b30e13660b4917b409484c910a6d2121e9f783297945',
    '2.3.4': '34d172761c51aa9469a904704109086aafa6125a4fa0e058766e2ddc73d3b303',
    '2.4.0': '1d1e718ddc0f1d3c7ac7b18f3ed1041daac5f258039537eb74562c7d7981491f',
    '2.4.1': '90f8bed7cdd7435f4904bca3d885a81a1811bd25036d712f21942c531d940247',
}
EXPORTER = 'com.vrvlog.liltoon-vrm-exporter'
# A project's own UniVRM is optional and may be any version; it only checks that
# the bundled copy coexists with it.
UNIVRM = ('com.vrmc.gltf', 'com.vrmc.vrm')
# Optional authoring packages needed by the exporter-behavior/integration profiles.
OPTIONAL = ('com.vrchat.base', 'com.vrchat.avatars', 'nadena.dev.ndmf',
            'nadena.dev.modular-avatar', 'com.anatawa12.avatar-optimizer')
# Fixed registry packages from Compatibility/README.md step 3 plus the modules
# the bundled UniVRM, VRChat SDK and exporter assemblies load.
REGISTRY = {
    'com.unity.collections': '2.1.4',
    'com.unity.test-framework': '1.4.6',
    'com.unity.textmeshpro': '3.0.6',
    'com.unity.timeline': '1.7.6',
    'com.unity.ugui': '1.0.0',
    'com.unity.xr.legacyinputhelpers': '2.1.10',
}
MODULES = ('ai', 'androidjni', 'animation', 'assetbundle', 'audio', 'cloth', 'director',
           'imageconversion', 'imgui', 'jsonserialize', 'particlesystem', 'physics',
           'physics2d', 'screencapture', 'terrain', 'terrainphysics', 'tilemap', 'ui',
           'uielements', 'umbra', 'unityanalytics', 'unitywebrequest',
           'unitywebrequestassetbundle', 'unitywebrequestaudio', 'unitywebrequesttexture',
           'unitywebrequestwww', 'vehicles', 'video', 'vr', 'wind', 'xr')
RECORD = 'liltoon-version-project.json'


def read_package(folder):
    data = json.loads((folder / 'package.json').read_text(encoding='utf-8-sig'))
    if not isinstance(data.get('name'), str) or not isinstance(data.get('version'), str):
        raise SystemExit('package.json needs a name and version: ' + str(folder))
    return data


def dependency_packages(folder):
    """Return {name: (path, version)} for the caller's optional packages; None means none."""
    if folder is None:
        return {}
    if not Path(folder).is_dir():
        raise SystemExit('--dependencies is not a folder: ' + str(folder))
    found = {}
    for child in sorted(Path(folder).iterdir()):
        if not (child / 'package.json').is_file():
            continue
        data = read_package(child)
        name = data['name']
        if name not in (*UNIVRM, *OPTIONAL):
            raise SystemExit('Unexpected package in --dependencies (lilToon and the exporter are added by this script): ' + name)
        if name in found:
            raise SystemExit('Duplicate package in --dependencies: ' + name)
        found[name] = (child, data['version'])
    present = [name for name in UNIVRM if name in found]
    if present and (len(present) != len(UNIVRM) or len({found[name][1] for name in UNIVRM}) != 1):
        raise SystemExit('A project UniVRM needs com.vrmc.gltf and com.vrmc.vrm of the same version')
    return found


def manifest(exporter):
    dependencies = dict(REGISTRY)
    dependencies.update({'com.unity.modules.' + name: '1.0.0' for name in MODULES})
    dependencies[EXPORTER] = 'file:' + exporter.resolve().as_posix()
    return {'dependencies': dict(sorted(dependencies.items())), 'testables': [EXPORTER]}


def sha256(path):
    digest = hashlib.sha256()
    with open(path, 'rb') as stream:
        for block in iter(lambda: stream.read(1 << 20), b''):
            digest.update(block)
    return digest.hexdigest()


def fetch_liltoon(version, cache):
    """Return (zip path, source URL); download the official release ZIP only when not cached."""
    url = RELEASE_URL.format(version)
    target = cache / ('jp.lilxyzw.liltoon-' + version + '.zip')
    if not target.is_file():
        cache.mkdir(parents=True, exist_ok=True)
        partial = target.with_suffix('.zip.partial')
        try:
            with urllib.request.urlopen(url, timeout=120) as response, open(partial, 'wb') as stream:
                shutil.copyfileobj(response, stream)
        except OSError as error:
            partial.unlink(missing_ok=True)
            raise SystemExit('Could not download lilToon ' + version + ' from ' + url + ': ' + str(error))
        partial.replace(target)
    return target, url


def verify_archive(archive, version, expected):
    actual = sha256(archive)
    if expected is None:
        raise SystemExit('No reviewed SHA-256 for lilToon ' + version + '; review the release and pass --sha256 ' + version + '=' + actual)
    if actual != expected:
        raise SystemExit('lilToon ' + version + ' archive SHA-256 is ' + actual + ', expected ' + expected + ': ' + str(archive))
    return actual


def extract_package(archive, destination, version):
    with zipfile.ZipFile(archive) as bundle:
        for member in bundle.namelist():
            path = PurePosixPath(member)
            if path.is_absolute() or '..' in path.parts or ':' in member or '\\' in member:
                raise SystemExit('Unsafe path in lilToon archive: ' + member)
        # Check the package identity before writing anything into the project.
        try:
            data = json.loads(bundle.read('package.json').decode('utf-8-sig'))
        except (KeyError, ValueError):
            raise SystemExit('lilToon archive has no readable package.json at its root: ' + str(archive))
        if not isinstance(data, dict) or data.get('name') != unity_packages.LILTOON or data.get('version') != version:
            found = (data.get('name'), data.get('version')) if isinstance(data, dict) else data
            raise SystemExit('lilToon archive contains ' + repr(found) + ', expected ' + version)
        bundle.extractall(destination)


def exporter_identity(exporter):
    def git(*args):
        result = subprocess.run(['git', '-c', 'safe.directory=' + exporter.resolve().as_posix(), '-C', str(exporter), *args],
                                capture_output=True, text=True, timeout=10)
        return result.stdout.strip() if result.returncode == 0 else None
    try:
        commit = git('rev-parse', 'HEAD') if (exporter / '.git').exists() else None
        dirty = bool(git('status', '--porcelain')) if commit else None
    except (OSError, subprocess.SubprocessError):
        commit = dirty = None
    return {'packageVersion': read_package(exporter)['version'], 'gitCommit': commit, 'gitDirty': dirty}


def create_project(project, version, archive, dependencies, exporter):
    if project.exists() and (not project.is_dir() or any(project.iterdir())):
        raise SystemExit('Refusing to reuse an existing project path: ' + str(project))
    packages = project / 'Packages'
    (project / 'Assets').mkdir(parents=True)
    packages.mkdir()
    extract_package(archive, packages / unity_packages.LILTOON, version)
    for name, (source, _) in dependencies.items():
        # Copy the package only, not a checkout's history or Unity caches.
        shutil.copytree(source, packages / name, ignore=shutil.ignore_patterns('.git', 'Library', 'Temp', 'Logs'))
    (packages / 'manifest.json').write_text(json.dumps(manifest(exporter), indent=2) + '\n', encoding='utf-8')


def open_once(unity, project, log, timeout):
    """Let Unity resolve, import and compile; a compile error makes batch mode exit non-zero."""
    command = [str(unity), '-batchmode', '-nographics', '-quit', '-projectPath', str(project), '-logFile', str(log)]
    flags = {'creationflags': subprocess.CREATE_NO_WINDOW} if sys.platform == 'win32' else {}
    started = time.monotonic()
    result = subprocess.run(command, timeout=timeout, **flags)
    if result.returncode != 0:
        raise SystemExit('Unity exited with ' + str(result.returncode) + ' while importing ' + str(project) + '; see ' + str(log))
    return round(time.monotonic() - started)


def resolved_versions(project, names):
    editor, revision = unity_packages.editor_version(project)
    packages = {}
    for name in names:
        installed = unity_packages.installed_package(project, name)
        packages[name] = installed['version'] if installed else None
    return {'unity': editor, 'unityRevision': revision, 'packages': packages}


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--output', required=True, type=Path, help='Folder that receives one project per version')
    parser.add_argument('--dependencies', type=Path,
                        help='Folder of unpacked optional packages: VRChat SDK, NDMF, MA, AAO, or a project UniVRM '
                             '(com.vrmc.gltf and com.vrmc.vrm of any one version)')
    parser.add_argument('--versions', default=','.join(DEFAULT_VERSIONS), help='Comma-separated lilToon releases')
    parser.add_argument('--exporter', type=Path, default=ROOT, help='Exporter checkout referenced with file: (default: this checkout)')
    parser.add_argument('--liltoon-cache', type=Path, help='Folder of jp.lilxyzw.liltoon-<version>.zip (default: OUTPUT/_liltoon)')
    parser.add_argument('--unity', type=Path, help='Unity 2022.3 executable; open each project once and record resolved versions')
    parser.add_argument('--timeout', type=int, default=3600, help='Seconds per Unity import')
    parser.add_argument('--sha256', action='append', default=[], metavar='VERSION=SHA256',
                        help='Reviewed SHA-256 of a requested lilToon release ZIP that this script does not list')
    args = parser.parse_args()

    versions = [v.strip() for v in args.versions.split(',') if v.strip()]
    for version in versions:
        try:
            unity_packages.parse_version(version)
        except ValueError:
            raise SystemExit('lilToon versions must be releases such as 2.3.4: ' + repr(version))
    if len(set(versions)) != len(versions):
        raise SystemExit('Duplicate lilToon version')
    pinned = dict(RELEASE_SHA256)
    for item in args.sha256:
        version, _, digest = item.partition('=')
        if version not in versions or not re.fullmatch(r'[0-9a-f]{64}', digest):
            raise SystemExit('--sha256 must be VERSION=<64 lowercase hex digits> for a requested version: ' + item)
        pinned[version] = digest
    dependencies = dependency_packages(args.dependencies)
    exporter = args.exporter.resolve()
    if read_package(exporter)['name'] != EXPORTER:
        raise SystemExit('--exporter is not the exporter package: ' + str(exporter))
    output = args.output.resolve()
    cache = (args.liltoon_cache or output / '_liltoon').resolve()
    logs = output / 'logs'
    logs.mkdir(parents=True, exist_ok=True)
    names = [unity_packages.LILTOON, *dependencies, EXPORTER]
    # Keep records of projects created by earlier runs into the same folder.
    summary_path = output / 'projects.json'
    summary = json.loads(summary_path.read_text(encoding='utf-8')) if summary_path.is_file() else []
    for version in versions:
        project = output / ('liltoon-' + version)
        archive, url = fetch_liltoon(version, cache)
        digest = verify_archive(archive, version, pinned.get(version))
        create_project(project, version, archive, dependencies, exporter)
        record = {
            'schema': 1,
            'project': project.name,
            'lilToon': {'version': version, 'source': url, 'sha256': digest},
            'requested': {name: pinned for name, (_, pinned) in dependencies.items()},
            'exporter': exporter_identity(exporter),
            'resolved': None,
        }
        if args.unity:
            seconds = open_once(args.unity.resolve(), project, logs / (project.name + '.log'), args.timeout)
            record['resolved'] = resolved_versions(project, names)
            record['importSeconds'] = seconds
            actual = record['resolved']['packages']
            if actual[unity_packages.LILTOON] != version:
                raise SystemExit('Unity resolved lilToon ' + str(actual[unity_packages.LILTOON]) + ' in ' + project.name)
            changed = [n for n, (_, pinned) in dependencies.items() if actual[n] != pinned]
            if changed:
                raise SystemExit('Unity resolved different dependency versions in ' + project.name + ': ' + ', '.join(changed))
        (project / RECORD).write_text(json.dumps(record, indent=2) + '\n', encoding='utf-8')
        summary.append(record)
        summary_path.write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
        print(json.dumps({'project': project.name, 'lilToon': version,
                          'resolved': record['resolved']}), flush=True)


if __name__ == '__main__':
    main()
