import importlib.util
import io
import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(file))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


runner = load('parity_runner', 'run-liltoon-version-parity.py')
glbs = load('parity_tests', 'test-liltoon-version-parity.py')

LILTOON = 'jp.lilxyzw.liltoon'
MANIFEST = (b'{\n'
            b'  "dependencies": {\n'
            b'    "com.unity.ugui": "1.0.0",\n'
            b'    "com.vrvlog.liltoon-vrm-exporter": "file:../../exporter",\n'
            b'    "com.vrmc.gltf": "file:com.vrmc.gltf"\n'
            b'  },\n'
            b'  "testables": [\n'
            b'    "com.vrvlog.liltoon-vrm-exporter"\n'
            b'  ]\n'
            b'}\n')
GROUPS = ('backlight-color-a050', 'fur')


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')


def make_package(folder, name, version, extra='old.txt'):
    write_json(folder / 'package.json', {'name': name, 'version': version})
    (folder / extra).write_text(version, encoding='utf-8')
    return folder


def make_project(root, version='2.1.0'):
    """A version project as create-liltoon-version-projects.py leaves it after one Unity import."""
    packages = root / 'Packages'
    packages.mkdir(parents=True)
    (packages / 'manifest.json').write_bytes(MANIFEST)
    write_json(packages / 'packages-lock.json', {'dependencies': {
        LILTOON: {'version': 'file:' + LILTOON, 'depth': 0, 'source': 'embedded', 'dependencies': {}}}})
    make_package(packages / LILTOON, LILTOON, version)
    (root / 'ProjectSettings').mkdir()
    (root / 'ProjectSettings' / 'ProjectVersion.txt').write_text(
        'm_EditorVersion: 2022.3.62f3\nm_EditorVersionWithRevision: 2022.3.62f3 (96770f904ca7)\n', encoding='utf-8')
    for folder in ('Assets/Logs', 'Library', 'Temp', 'Logs'):
        (root / folder).mkdir(parents=True, exist_ok=True)
        (root / folder / 'marker.txt').write_text(folder, encoding='utf-8')
    return root


class FakeUnity:
    """Stands in for the harness: writes GLBs and a manifest stamped with the embedded lilToon version."""

    def __init__(self, mutate_b=None, codes=None, timeout_side=None, no_manifest_side=None):
        self.mutate_b, self.codes, self.timeout_side = mutate_b, codes or {}, timeout_side
        self.no_manifest_side = no_manifest_side
        self.commands = []

    def __call__(self, command, timeout):
        self.commands.append((command, timeout))
        argument = lambda name: command[command.index(name) + 1]
        project, output = Path(argument('-projectPath')), Path(argument('-parityOutput'))
        side = 'A' if argument('-executeMethod').endswith('.CreateAndExport') else 'B'
        if side == self.timeout_side:
            raise subprocess.TimeoutExpired(command, timeout)
        version = json.loads((project / 'Packages' / LILTOON / 'package.json').read_text(encoding='utf-8'))['version']
        Path(argument('-logFile')).write_text('log', encoding='utf-8')
        for target in ((side, side + '-again') if side == 'A' else (side,)):
            (output / target).mkdir(parents=True)
            for name in GROUPS:
                mutate = self.mutate_b if side == 'B' and name == 'fur' else None
                (output / target / (name + '.glb')).write_bytes(glbs.build(version, mutate=mutate))
        groups = [{'name': name, 'lilToonVersionProperty': 41.0 if side == 'A' else 44.0,
                   'missing': ['_FurMask'] if side == 'A' and name == 'fur' else []} for name in GROUPS]
        failures = 1 if self.codes.get(side) == 1 else 0
        if failures:
            groups[0]['error'] = 'material not found'
        if side != self.no_manifest_side:
            write_json(output / ('manifest-' + side + '.json'), {'side': side, 'groups': groups, 'failures': failures})
        return self.codes.get(side, 0)


class ManifestTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        (self.root / 'Packages').mkdir()
        self.manifest = self.root / 'Packages' / 'manifest.json'

    def test_only_the_exporter_value_changes(self):
        for original in (MANIFEST, b'\xef\xbb\xbf' + MANIFEST.replace(b'\n', b'\r\n')):
            with self.subTest(bom=original.startswith(b'\xef')):
                self.manifest.write_bytes(original)
                exporter = self.root / 'exporter checkout'
                old, new = runner.point_exporter(self.root, exporter)
                self.assertEqual('file:../../exporter', old)
                self.assertEqual('file:' + exporter.resolve().as_posix(), new)
                expected = original.replace(b'"file:../../exporter"', json.dumps(new).encode('utf-8'))
                self.assertEqual(expected, self.manifest.read_bytes())

    def test_missing_dependency_or_testable_fails(self):
        cases = {
            'dependency': MANIFEST.replace(b'    "com.vrvlog.liltoon-vrm-exporter": "file:../../exporter",\n', b''),
            'testable': MANIFEST.replace(b'    "com.vrvlog.liltoon-vrm-exporter"\n', b''),
        }
        for name, data in cases.items():
            with self.subTest(name):
                self.manifest.write_bytes(data)
                with self.assertRaises(runner.ParityError):
                    runner.point_exporter(self.root, self.root)
                self.assertEqual(data, self.manifest.read_bytes())


class CopyAndSwapTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.project = make_project(self.root / 'liltoon-2.1.0')

    def test_copy_skips_temp_and_logs_and_keeps_library(self):
        copy = runner.copy_project(self.project, self.root / 'work' / 'copy')
        self.assertFalse((copy / 'Temp').exists())
        self.assertFalse((copy / 'Logs').exists())
        self.assertTrue((copy / 'Library' / 'marker.txt').is_file())
        # Only the top-level Unity folders are skipped.
        self.assertTrue((copy / 'Assets' / 'Logs' / 'marker.txt').is_file())
        self.assertEqual(MANIFEST, (copy / 'Packages' / 'manifest.json').read_bytes())

    def test_copy_refuses_existing_destination_and_inside_project(self):
        existing = self.root / 'existing'
        existing.mkdir()
        with self.assertRaises(runner.ParityError):
            runner.copy_project(self.project, existing)
        with self.assertRaises(runner.ParityError):
            runner.copy_project(self.project, self.project / 'Assets' / 'copy')
        self.assertFalse((self.project / 'Assets' / 'copy').exists())

    def test_swap_validates_package_and_removes_stale_files(self):
        target = self.project / 'Packages' / LILTOON
        for name, version in ((LILTOON, '2.3.3'), ('jp.example.other', '2.3.4')):
            with self.subTest(name=name, version=version):
                package = make_package(self.root / (name + version), name, version, extra='new.txt')
                with self.assertRaises(runner.ParityError):
                    runner.swap_liltoon(self.project, package, '2.3.4')
                self.assertTrue((target / 'old.txt').is_file())
        package = make_package(self.root / 'upgrade', LILTOON, '2.3.4', extra='new.txt')
        runner.swap_liltoon(self.project, package, '2.3.4')
        self.assertFalse((target / 'old.txt').exists())
        self.assertTrue((target / 'new.txt').is_file())
        self.assertEqual('2.3.4', runner.installed_liltoon(self.project)['version'])

    def test_unity_command_lines(self):
        unity, project, output = Path('Unity.exe'), Path('copy'), Path('out')
        common = ['Unity.exe', '-batchmode', '-nographics', '-projectPath', 'copy', '-executeMethod']
        self.assertEqual(common + [runner.HARNESS + 'CreateAndExport', '-parityOutput', 'out',
                                   '-logFile', str(output / 'unity-A.log')],
                         runner.unity_command(unity, project, output, 'A'))
        self.assertEqual(common + [runner.HARNESS + 'ExportSaved', '-parityOutput', 'out',
                                   '-logFile', str(output / 'unity-B.log')],
                         runner.unity_command(unity, project, output, 'B'))
        self.assertEqual('VRVlog.LilToonExporter.Tests.LilToonVersionParityHarness.', runner.HARNESS)
        self.assertNotIn('-quit', runner.unity_command(unity, project, output, 'A'))


class RunTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.project = make_project(self.root / 'projects' / 'liltoon-2.1.0')
        self.upgrade = make_package(self.root / 'upgrade', LILTOON, '2.3.4', extra='new.txt')
        self.exporter = self.root / 'exporter'
        write_json(self.exporter / 'package.json', {'name': runner.EXPORTER, 'version': '0.10.0-preview.1'})
        self.work, self.output = self.root / 'work', self.root / 'out'
        self.copy = self.work / 'liltoon-2.1.0'

    def run_parity(self, unity, **options):
        self.lines = []
        code = runner.run_parity(Path('Unity.exe'), self.project, self.upgrade, self.work, self.output,
                                 exporter=self.exporter, runner=unity, log=self.lines.append, **options)
        return code, json.loads((self.output / runner.RECEIPT).read_text(encoding='utf-8'))

    def assert_original_untouched(self):
        self.assertEqual(MANIFEST, (self.project / 'Packages' / 'manifest.json').read_bytes())
        self.assertEqual('2.1.0', runner.installed_liltoon(self.project)['version'])
        self.assertTrue((self.project / 'Library' / 'marker.txt').is_file())

    def test_identical_exports_pass_and_delete_only_the_copy_library(self):
        unity = FakeUnity()
        code, receipt = self.run_parity(unity, timeout=60)
        self.assertEqual(0, code, self.lines)
        self.assertEqual('Passed', receipt['result'])
        self.assertIsNone(receipt['error'])
        self.assertEqual(['CreateAndExport', 'ExportSaved'],
                         [command[command.index('-executeMethod') + 1].rsplit('.', 1)[1] for command, _ in unity.commands])
        self.assertEqual([60, 60], [timeout for _, timeout in unity.commands])
        self.assertEqual(('2.1.0', 'embedded'), (receipt['lilToon']['before']['version'], receipt['lilToon']['before']['source']))
        self.assertEqual('2.3.4', receipt['lilToon']['after']['version'])
        self.assertEqual({'version': '2022.3.62f3', 'revision': '96770f904ca7'}, receipt['unity'])
        self.assertEqual({'A': 0, 'B': 0}, receipt['unityExitCodes'])
        self.assertEqual([], receipt['pairCountMismatches'])
        self.assertEqual('0.10.0-preview.1', receipt['exporter']['packageVersion'])
        self.assertEqual('file:' + self.exporter.resolve().as_posix(), receipt['exporter']['manifestReference']['after'])
        manifest = json.loads((self.copy / 'Packages' / 'manifest.json').read_text(encoding='utf-8'))
        self.assertEqual(receipt['exporter']['manifestReference']['after'], manifest['dependencies'][runner.EXPORTER])
        self.assertEqual({'failures': 0, 'groups': 2, 'errors': [], 'missingProperties': {'fur': ['_FurMask']},
                          'lilToonVersionProperty': {'41.0': 2}}, receipt['manifests']['A'])
        self.assertEqual(0, receipt['manifests']['B']['failures'])
        for name, expect_b in (('A-again', '2.1.0'), ('B', '2.3.4')):
            comparison = receipt['comparisons'][name]
            self.assertTrue(comparison['ok'], name)
            self.assertEqual(2, comparison['pairs'])
            self.assertEqual({'a': '2.1.0', 'b': expect_b}, comparison['expect'])
            self.assertEqual([], comparison['failing'])
        self.assertTrue((self.output / 'compare-A-B.json').is_file())
        self.assertEqual({'copy', 'manifest', 'unityA', 'swap', 'unityB', 'compare', 'cleanup'}, set(receipt['seconds']))
        self.assertTrue(receipt['copyLibraryDeleted'])
        self.assertFalse((self.copy / 'Library').exists())
        self.assertTrue((self.copy / 'Assets').is_dir())
        self.assert_original_untouched()

    def test_keep_copy_keeps_library(self):
        code, receipt = self.run_parity(FakeUnity(), keep_copy=True)
        self.assertEqual(0, code)
        self.assertFalse(receipt['copyLibraryDeleted'])
        self.assertTrue((self.copy / 'Library' / 'marker.txt').is_file())

    def test_b_difference_fails_with_first_differences(self):
        def mutate(document):
            document['extensions'][glbs.parity.EXTENSION]['materials'][1]['values'][0]['value'][1] = 0.5
        code, receipt = self.run_parity(FakeUnity(mutate_b=mutate))
        self.assertEqual(1, code)
        self.assertEqual('Failed', receipt['result'])
        self.assertTrue(receipt['comparisons']['A-again']['ok'])
        comparison = receipt['comparisons']['B']
        self.assertFalse(comparison['ok'])
        self.assertEqual(2, comparison['pairs'])
        (failing,) = comparison['failing']
        self.assertEqual('fur.glb', failing['name'])
        self.assertEqual(1, failing['jsonDifferences'])
        self.assertEqual('extensions.VRVLOG_materials_liltoon.materials[1:Body].values[_BacklightColor].value[1]',
                         failing['firstJsonDifferences'][0]['path'])
        # A failed run keeps the copy for inspection.
        self.assertFalse(receipt['copyLibraryDeleted'])
        self.assertTrue((self.copy / 'Library').is_dir())
        self.assert_original_untouched()

    def test_harness_failure_count_fails(self):
        class Failing(FakeUnity):
            def __call__(self, command, timeout):
                code = super().__call__(command, timeout)
                output = Path(command[command.index('-parityOutput') + 1])
                if command[command.index('-executeMethod') + 1].endswith('ExportSaved'):
                    path = output / 'manifest-B.json'
                    manifest = json.loads(path.read_text(encoding='utf-8'))
                    manifest['failures'] = 1
                    manifest['groups'][0]['error'] = 'material not found'
                    write_json(path, manifest)
                return code
        code, receipt = self.run_parity(Failing())
        self.assertEqual(1, code)
        self.assertEqual([{'name': 'backlight-color-a050', 'error': 'material not found'}], receipt['manifests']['B']['errors'])

    def reset(self):
        for path in (self.work, self.output):
            if path.exists():
                runner._remove_tree(path)

    def test_timeout_and_unexpected_exit_stop_with_a_receipt(self):
        cases = (('timeout', FakeUnity(timeout_side='A'), 'did not exit within 5 s', {'A': None, 'B': None}),
                 ('exit 3', FakeUnity(codes={'B': 3}), 'Unity B exited with 3;', {'A': 0, 'B': 3}),
                 ('exit 1 without manifest', FakeUnity(codes={'B': 1}, no_manifest_side='B'),
                  'Unity B exited with 1 without writing manifest-B.json', {'A': 0, 'B': 1}))
        for name, unity, message, exit_codes in cases:
            with self.subTest(name):
                self.reset()
                code, receipt = self.run_parity(unity, timeout=5)
                self.assertEqual(2, code)
                self.assertEqual('Error', receipt['result'])
                self.assertIn(message, receipt['error'])
                self.assertEqual(exit_codes, receipt['unityExitCodes'])
                self.assertIsNone(receipt['comparisons']['B'])
                self.assertTrue((self.copy / 'Library').is_dir())
                self.assert_original_untouched()
        self.assertEqual('2.1.0', receipt['lilToon']['before']['version'])

    def test_exit_1_with_manifest_is_a_harness_failure(self):
        # A: the upgrade and B are skipped.
        unity = FakeUnity(codes={'A': 1})
        code, receipt = self.run_parity(unity)
        self.assertEqual(1, code, self.lines)
        self.assertEqual('Failed', receipt['result'])
        self.assertIsNone(receipt['error'])
        self.assertEqual({'A': 1, 'B': None}, receipt['unityExitCodes'])
        self.assertEqual(1, receipt['manifests']['A']['failures'])
        self.assertEqual([{'name': 'backlight-color-a050', 'error': 'material not found'}], receipt['manifests']['A']['errors'])
        self.assertIsNone(receipt['manifests']['B'])
        self.assertEqual(['CreateAndExport'], [c[c.index('-executeMethod') + 1].rsplit('.', 1)[1] for c, _ in unity.commands])
        self.assertNotIn('swap', receipt['seconds'])
        self.assertEqual({'A-again': None, 'B': None}, receipt['comparisons'])
        self.assertEqual('2.1.0', runner.installed_liltoon(self.copy)['version'])
        self.assertTrue((self.copy / 'Library').is_dir())
        self.assert_original_untouched()
        # B: the comparisons still run and the manifest failure fails the run.
        self.reset()
        code, receipt = self.run_parity(FakeUnity(codes={'B': 1}))
        self.assertEqual(1, code, self.lines)
        self.assertEqual('Failed', receipt['result'])
        self.assertIsNone(receipt['error'])
        self.assertEqual({'A': 0, 'B': 1}, receipt['unityExitCodes'])
        self.assertEqual(1, receipt['manifests']['B']['failures'])
        self.assertTrue(receipt['comparisons']['B']['ok'])
        self.assertTrue((self.copy / 'Library').is_dir())

    def test_pair_count_must_match_manifest_groups(self):
        class Dropping(FakeUnity):
            """Loses fur.glb on every side while the manifests still list two groups without failures."""
            def __call__(self, command, timeout):
                code = super().__call__(command, timeout)
                output = Path(command[command.index('-parityOutput') + 1])
                for path in output.glob('*/fur.glb'):
                    path.unlink()
                return code
        code, receipt = self.run_parity(Dropping())
        self.assertEqual(1, code, self.lines)
        self.assertEqual('Failed', receipt['result'])
        self.assertTrue(all(c['ok'] and c['pairs'] == 1 for c in receipt['comparisons'].values()))
        self.assertEqual(['A vs A-again compared 1 pairs but manifest-A lists 2 groups',
                          'A vs B compared 1 pairs but manifest-A lists 2 groups',
                          'A vs B compared 1 pairs but manifest-B lists 2 groups'], receipt['pairCountMismatches'])
        self.assertIn('[parity] A vs B compared 1 pairs but manifest-B lists 2 groups', self.lines)
        self.assertEqual(['A vs B compared 2 pairs but manifest-B has no group count'],
                         runner.pair_count_mismatches({'B': {'pairs': 2}}, {'A': {'groups': 2}, 'B': None}))

    def test_invalid_liltoon_version_in_project_stops_before_copy(self):
        for version in ('2.1.0-beta.1', '..'):
            with self.subTest(version):
                self.reset()
                package = self.project / 'Packages' / LILTOON / 'package.json'
                write_json(package, {'name': LILTOON, 'version': version})
                unity = FakeUnity()
                code, receipt = self.run_parity(unity)
                self.assertEqual(2, code)
                self.assertEqual('Error', receipt['result'])
                self.assertIn('expected a release such as 2.1.0', receipt['error'])
                self.assertIn(repr(version), receipt['error'])
                self.assertIsNone(receipt['copy'])
                self.assertEqual([], unity.commands)
                self.assertFalse(self.work.exists())

    def test_old_python_stops_before_any_work(self):
        unity = FakeUnity()
        lines = []
        with mock.patch.object(runner.sys, 'version_info', (3, 11, 9)):
            code = runner.run_parity(Path('Unity.exe'), self.project, self.upgrade, self.work, self.output,
                                     exporter=self.exporter, runner=unity, log=lines.append)
            with mock.patch.object(runner, 'run_parity') as run_parity, \
                    mock.patch('sys.stderr', new_callable=io.StringIO) as stderr:
                main_code = runner.main(['--unity', 'Unity.exe', '--project', str(self.project),
                                         '--upgrade-package', str(self.upgrade), '--work', str(self.work),
                                         '--output', str(self.output)])
        self.assertEqual((2, 2), (code, main_code))
        self.assertEqual(['ERROR Python 3.12 or later is required'], lines)
        self.assertIn('Python 3.12 or later is required', stderr.getvalue())
        run_parity.assert_not_called()
        self.assertEqual([], unity.commands)
        self.assertFalse(self.output.exists())
        self.assertFalse(self.work.exists())

    def test_invalid_inputs_stop_before_unity(self):
        unity = FakeUnity()
        code, receipt = self.run_parity(unity, upgrade_version='2.3.3')
        self.assertEqual(2, code)
        self.assertIn('2.3.3', receipt['error'])
        self.assertEqual([], unity.commands)
        self.assertFalse(self.copy.exists())
        # A non-empty output is refused because its stale GLBs would be compared.
        self.assertEqual(2, runner.run_parity(Path('Unity.exe'), self.project, self.upgrade, self.work, self.output,
                                              exporter=self.exporter, runner=unity, log=lambda line: None))
        # An existing copy is refused.
        runner._remove_tree(self.output)
        self.copy.mkdir(parents=True)
        code, receipt = self.run_parity(unity)
        self.assertEqual(2, code)
        self.assertIn('Refusing to reuse an existing copy', receipt['error'])
        self.assertEqual([], unity.commands)


class RunUnityTests(unittest.TestCase):
    def test_timeout_kills_the_process(self):
        started = time.monotonic()
        with self.assertRaises(subprocess.TimeoutExpired):
            runner.run_unity([sys.executable, '-c', 'import time; time.sleep(60)'], 1)
        self.assertLess(time.monotonic() - started, 30)
        self.assertEqual(3, runner.run_unity([sys.executable, '-c', 'raise SystemExit(3)'], 30))

    def test_timeout_kills_the_tree_of_the_started_process(self):
        process = mock.Mock(pid=4321)
        process.wait.side_effect = [subprocess.TimeoutExpired('Unity', 5), 1]
        popen, kill = mock.Mock(return_value=process), mock.Mock()
        with self.assertRaises(subprocess.TimeoutExpired):
            runner.run_unity(['Unity.exe'], 5, popen=popen, kill=kill)
        kill.assert_called_once_with(process)
        self.assertEqual([mock.call(timeout=5), mock.call()], process.wait.call_args_list)
        flags = popen.call_args.kwargs
        if os.name == 'nt':
            self.assertEqual({'creationflags': subprocess.CREATE_NO_WINDOW}, flags)
        else:
            self.assertEqual({'start_new_session': True}, flags)

    def test_windows_kill_uses_taskkill_on_the_tree_without_a_window(self):
        process = mock.Mock(pid=4321)
        run = mock.Mock(return_value=subprocess.CompletedProcess([], 0))
        runner.kill_tree(process, windows=True, run=run)
        run.assert_called_once()
        self.assertEqual(['taskkill', '/T', '/F', '/PID', '4321'], run.call_args.args[0])
        self.assertEqual(getattr(subprocess, 'CREATE_NO_WINDOW', 0), run.call_args.kwargs['creationflags'])
        self.assertFalse(run.call_args.kwargs['check'])
        process.kill.assert_not_called()
        # taskkill missing or failing falls back to killing the process itself.
        for result in (OSError('taskkill not found'), subprocess.CompletedProcess([], 128)):
            with self.subTest(result=result):
                process.reset_mock()
                fallback = mock.Mock(side_effect=result) if isinstance(result, Exception) else mock.Mock(return_value=result)
                runner.kill_tree(process, windows=True, run=fallback)
                process.kill.assert_called_once_with()

    def test_posix_kill_signals_the_process_group(self):
        process = mock.Mock(pid=4321)
        with mock.patch.object(runner.signal, 'SIGKILL', 9, create=True), \
                mock.patch.object(runner.os, 'killpg', create=True) as killpg:
            runner.kill_tree(process, windows=False, run=mock.Mock())
            killpg.assert_called_once_with(4321, 9)
            process.kill.assert_not_called()
            killpg.side_effect = ProcessLookupError()
            runner.kill_tree(process, windows=False, run=mock.Mock())
            process.kill.assert_called_once_with()


if __name__ == '__main__':
    unittest.main()
