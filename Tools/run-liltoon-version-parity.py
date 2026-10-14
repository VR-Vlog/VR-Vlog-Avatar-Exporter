"""Run the lilToon version parity check on a copy of one version project (#149, WBS 1-8).

The version project (Tools/create-liltoon-version-projects.py, opened once in Unity) is copied
to WORK/liltoon-<version> without Temp and Logs; Library is kept so Unity does not reimport
everything. In the copy the exporter dependency is pointed at --exporter, Unity runs
LilToonVersionParityHarness.CreateAndExport (OUTPUT/A and OUTPUT/A-again), the embedded
Packages/jp.lilxyzw.liltoon is replaced with --upgrade-package, and Unity runs ExportSaved
(OUTPUT/B). A is then compared with A-again (same-session control) and with B using
Tools/compare-liltoon-version-exports.py. The original project is never modified.

OUTPUT/parity.json records the lilToon versions, Unity version, exporter source, harness
manifest counts, both comparisons and seconds per step. Exit codes: 0 = both comparisons
identical and no harness failures, 1 = differences or harness failures, 2 = a step could not
run (bad input, Unity failed or timed out).
"""
import argparse
import importlib.util
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import time
from pathlib import Path

import unity_packages

ROOT = Path(__file__).resolve().parents[1]
EXPORTER = 'com.vrvlog.liltoon-vrm-exporter'
HARNESS = 'VRVlog.LilToonExporter.Tests.LilToonVersionParityHarness.'
# Unity recreates these; Library is kept to avoid a full reimport.
SKIPPED = ('Temp', 'Logs')
DEFAULT_UPGRADE = '2.3.4'
DEFAULT_TIMEOUT = 2700
FIRST_DIFFERENCES = 5
RECEIPT = 'parity.json'


def _load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


compare_exports = _load('compare_liltoon_version_exports', 'compare-liltoon-version-exports.py')
compatibility = _load('run_unity_compatibility', 'run-unity-compatibility.py')


class ParityError(Exception):
    """A step could not run; the receipt records the message and the exit code is 2."""


def run_unity(command, timeout):
    """Run Unity without a console window; on timeout kill only that process and re-raise."""
    flags = {'creationflags': subprocess.CREATE_NO_WINDOW} if os.name == 'nt' else {}
    process = subprocess.Popen(command, **flags)
    try:
        return process.wait(timeout=timeout)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
        raise


def _remove_tree(path):
    def retry(function, target, _):
        # Unity and git leave read-only files that rmtree cannot delete on Windows.
        os.chmod(target, stat.S_IWRITE)
        function(target)
    shutil.rmtree(path, onexc=retry)


def read_package(folder, name, version=None):
    """Return package.json of folder; raise ParityError unless it is name (and version)."""
    try:
        data = json.loads((Path(folder) / 'package.json').read_text(encoding='utf-8-sig'))
    except (OSError, ValueError) as error:
        raise ParityError('Cannot read package.json in ' + str(folder) + ': ' + str(error))
    found = (data.get('name'), data.get('version')) if isinstance(data, dict) else (None, None)
    if found[0] != name or (version is not None and found[1] != version):
        raise ParityError(str(folder) + ' contains ' + repr(found) + ', expected ' + repr((name, version or found[1])))
    return data


def installed_liltoon(project):
    try:
        installed = unity_packages.installed_package(project, unity_packages.LILTOON)
    except (OSError, ValueError) as error:
        raise ParityError('Cannot read the installed lilToon in ' + str(project) + ': ' + str(error))
    if installed is None:
        raise ParityError('packages-lock.json of ' + str(project) + ' has no ' + unity_packages.LILTOON)
    return installed


def copy_project(project, destination):
    """Copy project to destination without the top-level Temp and Logs folders."""
    project, destination = Path(project).resolve(), Path(destination).resolve()
    if destination.exists():
        raise ParityError('Refusing to reuse an existing copy: ' + str(destination))
    if destination.is_relative_to(project):
        raise ParityError('The copy must be outside the version project: ' + str(destination))
    if not (project / 'Packages' / 'manifest.json').is_file():
        raise ParityError('Not a Unity project (Packages/manifest.json is missing): ' + str(project))

    def ignore(directory, names):
        return [name for name in names if name in SKIPPED] if Path(directory).resolve() == project else []
    shutil.copytree(project, destination, ignore=ignore)
    return destination


def point_exporter(project, exporter):
    """Set the exporter dependency in Packages/manifest.json to file:<exporter>.

    Only the value string changes; key order, indentation, newlines and a BOM are kept.
    Returns (old value, new value).
    """
    path = Path(project) / 'Packages' / 'manifest.json'
    raw = path.read_bytes()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw[3:].decode('utf-8') if bom else raw.decode('utf-8')
    try:
        before = json.loads(text)
    except ValueError as error:
        raise ParityError('Packages/manifest.json is not valid JSON: ' + str(error))
    dependencies = before.get('dependencies') if isinstance(before, dict) else None
    if not isinstance(dependencies, dict) or not isinstance(dependencies.get(EXPORTER), str):
        raise ParityError('Packages/manifest.json has no ' + EXPORTER + ' dependency: ' + str(path))
    if EXPORTER not in (before.get('testables') or []):
        raise ParityError('Packages/manifest.json does not list ' + EXPORTER + ' in testables, so the harness is not compiled')
    value = 'file:' + Path(exporter).resolve().as_posix()
    pattern = re.compile(r'("' + re.escape(EXPORTER) + r'"\s*:\s*)"(?:[^"\\]|\\.)*"')
    matches = list(pattern.finditer(text))
    if len(matches) != 1:
        raise ParityError('Expected one ' + EXPORTER + ' key in Packages/manifest.json, found ' + str(len(matches)))
    match = matches[0]
    edited = text[:match.start()] + match.group(1) + json.dumps(value, ensure_ascii=False) + text[match.end():]
    after = json.loads(edited)
    old = dependencies[EXPORTER]
    dependencies[EXPORTER] = value
    if after != before:
        raise ParityError('Editing Packages/manifest.json changed more than the ' + EXPORTER + ' value')
    path.write_bytes((b'\xef\xbb\xbf' if bom else b'') + edited.encode('utf-8'))
    return old, value


def swap_liltoon(project, package, version):
    """Replace the embedded lilToon with package after checking it is lilToon version."""
    read_package(package, unity_packages.LILTOON, version)
    target = Path(project) / 'Packages' / unity_packages.LILTOON
    package = Path(package).resolve()
    if not target.is_dir():
        raise ParityError('lilToon is not embedded at ' + str(target))
    if package.is_relative_to(target.resolve()):
        raise ParityError('--upgrade-package must not be inside the project copy: ' + str(package))
    _remove_tree(target)
    shutil.copytree(package, target)


def unity_command(unity, project, output, side):
    """Command line for one harness run; side A creates and exports, side B exports the saved materials."""
    method = {'A': 'CreateAndExport', 'B': 'ExportSaved'}[side]
    return [str(unity), '-batchmode', '-nographics', '-projectPath', str(project),
            '-executeMethod', HARNESS + method, '-parityOutput', str(output),
            '-logFile', str(Path(output) / ('unity-' + side + '.log'))]


def run_side(unity, project, output, side, timeout, runner):
    command = unity_command(unity, project, output, side)
    log = Path(output) / ('unity-' + side + '.log')
    try:
        code = runner(command, timeout)
    except subprocess.TimeoutExpired:
        raise ParityError('Unity ' + side + ' did not exit within ' + str(timeout) + ' s and was stopped; see ' + str(log))
    except OSError as error:
        raise ParityError('Could not start Unity: ' + str(error))
    if code != 0:
        raise ParityError('Unity ' + side + ' exited with ' + str(code) + '; see ' + str(log))
    if not any((Path(output) / side).glob('*.glb')):
        raise ParityError('Unity ' + side + ' wrote no ' + side + '/*.glb; see ' + str(log))


def manifest_summary(output, side):
    """Group counts from the harness manifest-<side>.json, or None when it was not written."""
    path = Path(output) / ('manifest-' + side + '.json')
    if not path.is_file():
        return None
    try:
        manifest = json.loads(path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError) as error:
        return {'unreadable': str(error)}
    groups = [g for g in manifest.get('groups') or [] if isinstance(g, dict)]
    versions = {}
    for group in groups:
        key = json.dumps(group.get('lilToonVersionProperty'))
        versions[key] = versions.get(key, 0) + 1
    return {
        'failures': manifest.get('failures'),
        'groups': len(groups),
        'errors': [{'name': g.get('name'), 'error': g['error']} for g in groups if g.get('error')],
        'missingProperties': {g.get('name'): g['missing'] for g in groups if g.get('missing')},
        'lilToonVersionProperty': dict(sorted(versions.items())),
    }


def compare(output, a, b, expect_a, expect_b):
    """Compare OUTPUT/a with OUTPUT/b; write the full report and return the receipt summary."""
    result = compare_exports.compare_dirs(Path(output) / a, Path(output) / b)
    mismatches = compare_exports.version_mismatches(result['pairs'], expect_a, expect_b)
    ok = result['ok'] and not mismatches
    full = dict(result, version_mismatches=mismatches, ok=ok)
    (Path(output) / ('compare-' + a + '-' + b + '.json')).write_text(
        json.dumps(full, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    failing = []
    for name, report in result['pairs'].items():
        if report['identical_except_allowed']:
            continue
        failing.append({'name': name,
                        'jsonDifferences': report['json_difference_count'],
                        'binaryDifferences': len(report['binary_differences']),
                        'firstJsonDifferences': report['json_differences'][:FIRST_DIFFERENCES],
                        'firstBinaryDifferences': report['binary_differences'][:FIRST_DIFFERENCES]})
    return {'ok': ok, 'pairs': len(result['pairs']), 'expect': {'a': expect_a, 'b': expect_b},
            'missingInA': result['missing_in_a'], 'missingInB': result['missing_in_b'],
            'versionMismatches': mismatches, 'failing': failing}


def run_parity(unity, project, upgrade_package, work, output, exporter=ROOT, upgrade_version=DEFAULT_UPGRADE,
               timeout=DEFAULT_TIMEOUT, keep_copy=False, runner=run_unity, log=print):
    """Run every step and write OUTPUT/parity.json; return the exit code."""
    project, work, output = Path(project).resolve(), Path(work).resolve(), Path(output).resolve()
    exporter, upgrade_package = Path(exporter).resolve(), Path(upgrade_package).resolve()
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        # Stale GLBs from an earlier run would be compared as if this run wrote them.
        log('ERROR Refusing to reuse a non-empty --output: ' + str(output))
        return 2
    output.mkdir(parents=True, exist_ok=True)
    receipt = {'schema': 1, 'result': 'Error', 'error': None, 'project': str(project), 'copy': None,
               'copyLibraryDeleted': False, 'unity': None,
               'lilToon': {'before': None, 'after': None, 'upgradePackage': str(upgrade_package)},
               'exporter': None, 'manifests': {'A': None, 'B': None},
               'comparisons': {'A-again': None, 'B': None}, 'seconds': {}}
    code = 2

    def step(name, action):
        log('[parity] ' + name)
        started = time.monotonic()
        try:
            return action()
        finally:
            receipt['seconds'][name] = round(time.monotonic() - started, 1)

    try:
        try:
            unity_packages.parse_version(upgrade_version)
        except ValueError:
            raise ParityError('--upgrade-version must be a release such as 2.3.4: ' + repr(upgrade_version))
        read_package(exporter, EXPORTER)
        read_package(upgrade_package, unity_packages.LILTOON, upgrade_version)
        try:
            receipt['exporter'] = dict(compatibility.source_identity(exporter), path=str(exporter))
        except SystemExit as error:
            raise ParityError(str(error))
        original = installed_liltoon(project)
        copy = work / ('liltoon-' + original['version'])
        receipt['copy'] = str(copy)
        step('copy', lambda: copy_project(project, copy))
        before = installed_liltoon(copy)
        receipt['lilToon']['before'] = before
        editor, revision = unity_packages.editor_version(copy)
        receipt['unity'] = {'version': editor, 'revision': revision}
        old, new = step('manifest', lambda: point_exporter(copy, exporter))
        receipt['exporter']['manifestReference'] = {'before': old, 'after': new}
        try:
            step('unityA', lambda: run_side(unity, copy, output, 'A', timeout, runner))
        finally:
            receipt['manifests']['A'] = manifest_summary(output, 'A')
        step('swap', lambda: swap_liltoon(copy, upgrade_package, upgrade_version))
        try:
            step('unityB', lambda: run_side(unity, copy, output, 'B', timeout, runner))
        finally:
            receipt['manifests']['B'] = manifest_summary(output, 'B')
        after = installed_liltoon(copy)
        receipt['lilToon']['after'] = after
        if after['version'] != upgrade_version:
            raise ParityError('The copy resolved lilToon ' + str(after['version']) + ' after the upgrade, expected ' + upgrade_version)

        def comparisons():
            return (compare(output, 'A', 'A-again', before['version'], before['version']),
                    compare(output, 'A', 'B', before['version'], upgrade_version))
        receipt['comparisons']['A-again'], receipt['comparisons']['B'] = step('compare', comparisons)
        manifests_ok = all(m is not None and m.get('failures') == 0 for m in receipt['manifests'].values())
        passed = manifests_ok and all(c['ok'] for c in receipt['comparisons'].values())
        receipt['result'] = 'Passed' if passed else 'Failed'
        code = 0 if passed else 1
        if passed and not keep_copy and (copy / 'Library').is_dir():
            step('cleanup', lambda: _remove_tree(copy / 'Library'))
            receipt['copyLibraryDeleted'] = True
    except (ParityError, OSError, ValueError) as error:
        receipt['error'] = str(error)
        log('ERROR ' + str(error))
    (output / RECEIPT).write_text(json.dumps(receipt, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    for name, result in receipt['comparisons'].items():
        if result is not None:
            log('[parity] A vs %s: %s, %d pairs, %d failing' % (name, 'OK' if result['ok'] else 'FAIL',
                                                              result['pairs'], len(result['failing'])))
    log('RESULT ' + receipt['result'].upper() + ' (' + str(output / RECEIPT) + ')')
    return code


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--unity', required=True, type=Path, help='Unity executable of the version project')
    parser.add_argument('--project', required=True, type=Path, help='Version project with the older lilToon (not modified)')
    parser.add_argument('--upgrade-package', required=True, type=Path, help='Extracted jp.lilxyzw.liltoon package to upgrade to')
    parser.add_argument('--work', required=True, type=Path, help='Folder that receives the copy liltoon-<version>')
    parser.add_argument('--output', required=True, type=Path, help='Empty or new folder for GLBs, logs and parity.json')
    parser.add_argument('--exporter', type=Path, default=ROOT, help='Exporter checkout compiled into the copy (default: this checkout)')
    parser.add_argument('--upgrade-version', default=DEFAULT_UPGRADE, help='Required version of --upgrade-package')
    parser.add_argument('--timeout', type=int, default=DEFAULT_TIMEOUT, help='Seconds per Unity run')
    parser.add_argument('--keep-copy', action='store_true', help='Keep the copy\'s Library after a successful run')
    args = parser.parse_args(argv)
    return run_parity(args.unity.resolve(), args.project, args.upgrade_package, args.work, args.output,
                      exporter=args.exporter, upgrade_version=args.upgrade_version, timeout=args.timeout,
                      keep_copy=args.keep_copy, log=lambda line: print(line, flush=True))


if __name__ == '__main__':
    raise SystemExit(main())
