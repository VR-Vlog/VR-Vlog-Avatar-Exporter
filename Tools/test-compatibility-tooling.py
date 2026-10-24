import importlib.util
import json
import math
import re
import sys
from pathlib import Path
import unittest
from unittest import mock
import tempfile
import hashlib
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('upstream', Path(__file__).with_name('check-upstream-dependencies.py'))
upstream = importlib.util.module_from_spec(spec)
spec.loader.exec_module(upstream)
spec = importlib.util.spec_from_file_location('generator', Path(__file__).with_name('generate-compatibility.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
spec = importlib.util.spec_from_file_location('unity_runner', Path(__file__).with_name('run-unity-compatibility.py'))
unity_runner = importlib.util.module_from_spec(spec)
spec.loader.exec_module(unity_runner)
spec = importlib.util.spec_from_file_location('version_projects', Path(__file__).with_name('create-liltoon-version-projects.py'))
version_projects = importlib.util.module_from_spec(spec)
spec.loader.exec_module(version_projects)
REVIEWED_LILTOON_SHA256 = dict(version_projects.RELEASE_SHA256)


class UnityResultTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.xml = Path(self.temp.name) / 'result.xml'
        self.root = ET.Element('test-run', result='Passed')
        methods = {
            'DependencyEnvironmentTests': ('ActualInstalledPackagesMatchRequestedTestEnvironment', 1),
            'DependencyStartupTests': ('MenuResolvesBackendWithoutInitializationRegistration', 1),
            'DependencyRoundTripTests': ('SupportedBackendPreservesMeshesMorphsAndMaterialBindingsOnReimport', 2),
            'RendererSelectionTests': ('RendererCase', 4),
            'SkinnedMeshFallbackWeightTests': ('SkinCase', 14),
            'LilToonStructureCheckTests': ('StructureCase', 11),
        }
        for suite, (method, count) in methods.items():
            name = 'VRVlog.LilToonExporter.Tests.' + suite
            for index in range(count):
                ET.SubElement(self.root, 'test-case', classname=name, fullname=name + '.' + method + '(' + str(index) + ')', result='Passed')

    def check(self):
        ET.ElementTree(self.root).write(self.xml)
        return unity_runner.validate_result(self.xml)

    def test_all_required_suites_pass(self):
        self.assertEqual(len(self.check()), 33)

    def test_no_suite_can_disappear_from_a_passing_result(self):
        for name in ['DependencyEnvironmentTests', 'DependencyStartupTests', 'DependencyRoundTripTests', 'RendererSelectionTests', 'SkinnedMeshFallbackWeightTests', 'LilToonStructureCheckTests']:
            with self.subTest(suite=name):
                removed = [c for c in self.root if c.get('classname').endswith('.' + name)]
                for case in removed: self.root.remove(case)
                with self.assertRaises(SystemExit): self.check()
                for case in removed: self.root.append(case)

    def test_one_missing_skin_case_is_not_success(self):
        self.root.remove(self.root[-1])
        with self.assertRaises(SystemExit): self.check()

    def test_skipped_or_failed_case_is_not_success(self):
        for result in ['Skipped', 'Failed']:
            self.root[-1].set('result', result)
            with self.assertRaises(SystemExit): self.check()

    def test_missing_result_is_not_success(self):
        with self.assertRaises(FileNotFoundError): unity_runner.validate_result(self.xml)


class ExporterProfileResultTests(unittest.TestCase):
    # These XML fixtures test the gate, not the Unity exporter or installed SDKs.
    def setUp(self):
        UnityResultTests.setUp(self)
        self.profile = 'exporter-integration'
        methods_by_suite = {suite: dict(methods) for suite, methods in unity_runner.BEHAVIOR_CASES.items()}
        for suite, methods in unity_runner.INTEGRATION_CASES.items():
            methods_by_suite.setdefault(suite, {}).update(methods)
        for suite, methods in methods_by_suite.items():
            name = unity_runner.NAMESPACE + suite
            for method, count in methods.items():
                variants = unity_runner.INTEGRATION_VARIANTS.get(suite, {}).get(method)
                for index in range(count):
                    ET.SubElement(self.root, 'test-case', classname=name,
                                  fullname=name + '.' + method + '(' + (variants[index] if variants else str(index)) + ')', result='Passed')

    def check(self):
        ET.ElementTree(self.root).write(self.xml)
        return unity_runner.validate_result(self.xml, self.profile)

    def remove(self, suite, method=None):
        name = unity_runner.NAMESPACE + suite
        removed = [c for c in self.root if c.get('classname') == name and
                   (method is None or c.get('fullname').startswith(name + '.' + method + '('))]
        for case in removed: self.root.remove(case)
        return removed

    def test_all_required_suites_pass(self):
        expected = 33 + sum(sum(methods.values()) for methods in
                            [*unity_runner.BEHAVIOR_CASES.values(), *unity_runner.INTEGRATION_CASES.values()])
        self.assertEqual(len(self.check()), expected)

    def test_no_regression_class_can_disappear(self):
        for suite in [*unity_runner.BEHAVIOR_CASES, *unity_runner.INTEGRATION_CASES]:
            with self.subTest(suite=suite):
                removed = self.remove(suite)
                with self.assertRaises(SystemExit): self.check()
                self.root.extend(removed)

    def test_no_required_regression_or_parameter_variant_can_disappear(self):
        required = unity_runner.required_regressions(self.profile)
        for suite, methods in required.items():
            for method in methods:
                with self.subTest(suite=suite, method=method):
                    removed = self.remove(suite, method)
                    with self.assertRaises(SystemExit): self.check()
                    self.root.extend(removed)
                    if len(removed) > 1:
                        self.root.remove(removed[-1])
                        with self.assertRaises(SystemExit): self.check()
                        self.root.append(removed[-1])

    def test_only_behavior_does_not_claim_installed_aao_integration(self):
        for suite, methods in unity_runner.INTEGRATION_CASES.items():
            for method in methods: self.remove(suite, method)
        self.profile = 'exporter-behavior'
        self.assertGreater(len(self.check()), 20)
        self.profile = 'exporter-integration'
        with self.assertRaises(SystemExit): self.check()

    def test_shared_physbone_suite_selects_behavior_without_claiming_optional_integration(self):
        behavior = unity_runner.profile_filters('exporter-behavior')
        name = unity_runner.NAMESPACE + 'PhysBoneSpringExportTests'
        self.assertNotIn(name, behavior)
        self.assertIn(name + '.ActualExportImportsNestedOwnersWithoutSharedJointsOrChangedAppearance', behavior)
        self.assertNotIn(name + '.InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport', behavior)
        self.assertIn(name, unity_runner.profile_filters('exporter-integration'))
        required = unity_runner.required_regressions('exporter-integration')['PhysBoneSpringExportTests']
        self.assertIn('ActualExportImportsNestedOwnersWithoutSharedJointsOrChangedAppearance', required)
        self.assertIn('InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport', required)

    def test_synthetic_mesh_replacement_cannot_replace_official_mesh_deleter_cases(self):
        cases = self.remove('MeshDeleterIntegrationTests', 'DeletedSharedMeshAndMorphSurviveOneClickExportAndReimport')
        self.assertEqual(len(cases), 4)
        self.root.extend(cases[:2])
        # Missing installed-tool variants cannot be covered by the two passing
        # synthetic mesh replacements, even with a Passed XML root.
        with self.assertRaises(SystemExit): self.check()
        self.root.extend(cases[2:])
        self.check()
        for result in ('Skipped', 'Failed', 'Inconclusive'):
            with self.subTest(result=result):
                cases[-1].set('result', result)
                with self.assertRaises(SystemExit): self.check()
        cases[-1].set('result', 'Passed')

    def test_installed_ma_appearance_cannot_disappear_or_skip(self):
        method = 'InstalledMeshCutterPreservesMorphsAndSourceMesh'
        cases = self.remove('AppearancePreparationTests', method)
        self.assertEqual(len(cases), 2)
        with self.assertRaises(SystemExit): self.check()
        self.root.extend(cases)
        cases[-1].set('result', 'Skipped')
        with self.assertRaises(SystemExit): self.check()

    def test_preview_disabled_deletion_variants_cannot_disappear_or_skip(self):
        method = 'InstalledDeletionSurvivesOneClickAndLiveMorphsRegardlessOfPreviewSettings'
        cases = self.remove('MaDeletionExportTests', method)
        self.assertEqual(len(cases), 6)
        self.root.extend(cases[:2])
        with self.assertRaises(SystemExit): self.check()
        self.root.extend(cases[2:])
        self.check()
        cases[-1].set('result', 'Skipped')
        with self.assertRaises(SystemExit): self.check()

    def test_other_parameter_values_cannot_replace_official_or_preview_disabled_cases(self):
        for suite, method in (
            ('MeshDeleterIntegrationTests', 'DeletedSharedMeshAndMorphSurviveOneClickExportAndReimport'),
            ('MaDeletionExportTests', 'InstalledDeletionSurvivesOneClickAndLiveMorphsRegardlessOfPreviewSettings'),
            ('MaPermanentAppearanceExportTests', 'MergedPermanentPupilHideSurvivesVrmReloadBlinkAndMenuGeometry'),
        ):
            with self.subTest(suite=suite):
                cases = self.remove(suite, method)
                original = cases[-1].get('fullname')
                cases[-1].set('fullname', unity_runner.NAMESPACE + suite + '.' + method + '(True,99)')
                self.root.extend(cases)
                with self.assertRaises(SystemExit): self.check()
                cases[-1].set('fullname', original)
                self.check()

    def test_additive_case_cannot_be_replaced_by_duplicate_override_case(self):
        suite = 'MergedFxDefaultsTests'
        method = 'FractionalStationaryFxGeometrySurvivesOneClickExportAndVrmReimport'
        cases = self.remove(suite, method)
        cases[1].set('fullname', cases[0].get('fullname'))
        self.root.extend(cases)
        with self.assertRaises(SystemExit): self.check()

    def test_similarly_named_method_cannot_satisfy_the_required_case(self):
        case = self.root[-1]
        case.set('fullname', case.get('fullname').replace('(', 'Unexpected('))
        with self.assertRaises(SystemExit): self.check()

    def test_unlisted_case_in_selected_class_must_also_pass(self):
        name = unity_runner.NAMESPACE + 'NeutralShapeSamplerTests'
        case = ET.SubElement(self.root, 'test-case', classname=name, fullname=name + '.AdditionalRegression', result='Passed')
        self.check()
        for result in ['Skipped', 'Failed', 'Inconclusive']:
            with self.subTest(result=result):
                case.set('result', result)
                with self.assertRaises(SystemExit): self.check()

    def test_skipped_or_failed_required_case_is_not_success(self):
        for result in ['Skipped', 'Failed', 'Inconclusive']:
            with self.subTest(result=result):
                self.root[-1].set('result', result)
                with self.assertRaises(SystemExit): self.check()

    def test_unselected_class_is_rejected(self):
        name = unity_runner.NAMESPACE + 'UnrequestedTests'
        ET.SubElement(self.root, 'test-case', classname=name, fullname=name + '.Unexpected', result='Passed')
        with self.assertRaises(SystemExit): self.check()

    def test_root_failure_is_not_hidden_by_passing_cases(self):
        self.root.set('result', 'Failed')
        with self.assertRaises(SystemExit): self.check()

    def test_missing_identity_and_wrong_class_are_rejected(self):
        for field, value in [('fullname', ''), ('classname', unity_runner.NAMESPACE + 'WrongTests')]:
            with self.subTest(field=field):
                case = self.root[-1]
                original = case.get(field)
                case.set(field, value)
                with self.assertRaises(SystemExit): self.check()
                case.set(field, original)

    def test_malformed_xml_is_not_success(self):
        self.xml.write_text('<test-run result="Passed"><test-case', encoding='utf-8')
        with self.assertRaises(ET.ParseError): unity_runner.validate_result(self.xml, self.profile)


class UnityProfilePolicyTests(unittest.TestCase):
    def test_default_filter_set_is_unchanged(self):
        self.assertEqual(unity_runner.profile_filters('compatibility'), [
            unity_runner.NAMESPACE + suite for suite in (
                'DependencyEnvironmentTests', 'DependencyStartupTests', 'DependencyRoundTripTests',
                'RendererSelectionTests', 'SkinnedMeshFallbackWeightTests', 'LilToonStructureCheckTests')])

    def test_unknown_profile_cannot_start(self):
        with self.assertRaises(SystemExit): unity_runner.profile_filters('unknown')

    def test_whole_required_classes_are_selected(self):
        filters = unity_runner.profile_filters('exporter-integration')
        self.assertEqual(len(filters), len(set(filters)))
        for suite in [*unity_runner.BEHAVIOR_CASES, *unity_runner.INTEGRATION_CASES]:
            self.assertIn(unity_runner.NAMESPACE + suite, filters)
        # Additional-playable proof is an exporter behavior requirement in both
        # profiles; it cannot move behind an optional integration-only gate.
        for profile in ('exporter-behavior', 'exporter-integration'):
            for suite, variants in (('AdditionalPlayableCallbackTests', 41), ('AdditionalParameterConstantTests', 66), ('TemporalNeutralShapeTests', 23), ('NeutralLayerControlTests', 62), ('SelectedLayerControlTests', 12), ('SelectedExpressionAppearanceTests', 52), ('HeldAutomaticExpressionTests', 98), ('FacialDepthEnvelopeTests', 20), ('ParameterDriverExpressionTests', 32), ('VrChatMenuExpressionTests', 36)):
                self.assertIn(unity_runner.NAMESPACE + suite,
                              unity_runner.profile_filters(profile))
                self.assertEqual(sum(unity_runner.required_regressions(profile)[suite].values()), variants)

    def test_mesh_deleter_and_ma_appearance_are_explicit_integration_requirements(self):
        behavior = unity_runner.profile_filters('exporter-behavior')
        integration = unity_runner.profile_filters('exporter-integration')
        for suite in ('MeshDeleterIntegrationTests', 'MaDeletionExportTests', 'MaPermanentAppearanceExportTests', 'AppearancePreparationTests'):
            self.assertNotIn(unity_runner.NAMESPACE + suite, behavior)
            self.assertIn(unity_runner.NAMESPACE + suite, integration)
        required = unity_runner.required_regressions('exporter-integration')
        self.assertEqual(required['MeshDeleterIntegrationTests'], {
            'DeletedSharedMeshAndMorphSurviveOneClickExportAndReimport': 4})
        self.assertEqual(sum(required['AppearancePreparationTests'].values()), 12)
        self.assertEqual(required['MaDeletionExportTests'], {
            'InstalledDeletionSurvivesOneClickAndLiveMorphsRegardlessOfPreviewSettings': 6})
        self.assertEqual(required['MaPermanentAppearanceExportTests'], {
            'MergedPermanentPupilHideSurvivesVrmReloadBlinkAndMenuGeometry': 4})

    def test_required_case_names_and_counts_match_checked_in_fixtures(self):
        methods_by_suite = {suite: dict(methods) for suite, methods in unity_runner.BEHAVIOR_CASES.items()}
        for suite, methods in unity_runner.INTEGRATION_CASES.items():
            methods_by_suite.setdefault(suite, {}).update(methods)
        for suite, methods in methods_by_suite.items():
            source = (ROOT / 'Tests/Editor' / (suite + '.cs')).read_text(encoding='utf-8-sig')
            # Line-bounded attributes avoid ambiguous nested whitespace repeats
            # when scanning a long class for a later method.
            declarations = dict((method, attributes) for attributes, method in re.findall(
                r'((?:^[ \t]*\[(?:Test|UnityTest|TestCase\([^\r\n]*\)|TestCaseSource\([^\r\n]*\))\][ \t]*(?:\r?\n)?)+)'
                r'[ \t]*public[ \t]+(?:async[ \t]+)?(?:void|Task|IEnumerator)[ \t]+(\w+)\(', source, re.MULTILINE))
            if suite not in unity_runner.INTEGRATION_CASES:
                self.assertEqual(set(methods), set(declarations),
                                 'Whole-class filters must pin every checked-in regression, not only a required minimum')
            for method, count in methods.items():
                with self.subTest(suite=suite, method=method):
                    self.assertIn(method, declarations, 'Required regression must remain a real checked-in test')
                    sources = re.findall(r'\[TestCaseSource\(nameof\((\w+)\)\)\]', declarations[method])
                    if sources:
                        self.assertEqual(len(sources), 1, 'A source case must have one auditable generator')
                        generator = re.search(r'private static IEnumerable<TestCaseData> ' + re.escape(sources[0]) +
                                              r'\(\)\s*\{(.*?)^        \}', source, re.MULTILINE | re.DOTALL)
                        self.assertIsNotNone(generator, 'The native matrix generator must remain checked in')
                        dimensions = re.findall(r'foreach\s*\(var \w+ in new\[\]\s*\{([^}]*)\}\)', generator[1])
                        self.assertTrue(dimensions, 'A changed generator requires a new explicit native count audit')
                        self.assertEqual(len(re.findall(r'yield return new TestCaseData\(', generator[1])), 1)
                        self.assertEqual(math.prod(len(dimension.split(',')) for dimension in dimensions), count)
                    else:
                        self.assertEqual(len(re.findall(r'\[(?:Test|UnityTest|TestCase\()', declarations[method])), count)
                    variants = unity_runner.INTEGRATION_VARIANTS.get(suite, {}).get(method)
                    if variants is not None:
                        arguments = re.findall(r'\[TestCase\(([^\r\n]*)\)\]', declarations[method])
                        normalized = {','.join(part.strip().replace('false', 'False').replace('true', 'True')
                                               for part in args.split(',')) for args in arguments}
                        self.assertEqual(normalized, set(variants), 'Pinned integration combinations must remain real test attributes')


class RunnerSourceIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        (self.root / 'package.json').write_text('{"version":"0.11.11-dev.1"}', encoding='utf-8')

    def test_no_git_reports_version_without_machine_paths(self):
        with mock.patch.object(unity_runner.subprocess, 'run', side_effect=FileNotFoundError):
            identity = unity_runner.source_identity(self.root)
        self.assertEqual(identity, {'packageVersion': '0.11.11-dev.1', 'gitCommit': None, 'gitDirty': None})
        self.assertNotIn(str(self.root), json.dumps(identity))

    def test_archive_in_ancestor_repository_does_not_claim_its_commit(self):
        with mock.patch.object(unity_runner.subprocess, 'run', return_value=mock.Mock(returncode=0, stdout=str(self.root.parent))):
            self.assertIsNone(unity_runner.source_identity(self.root)['gitCommit'])

    def test_commit_and_dirty_state_use_only_process_scoped_safe_directory(self):
        commit = 'a' * 40
        for status, dirty in [('', False), (' M Editor/NeutralShapeSampler.cs', True)]:
            with self.subTest(dirty=dirty):
                results = [mock.Mock(returncode=0, stdout=value) for value in [str(self.root), commit, status]]
                with mock.patch.object(unity_runner.subprocess, 'run', side_effect=results) as run:
                    identity = unity_runner.source_identity(self.root)
                self.assertEqual(identity['gitCommit'], commit)
                self.assertEqual(identity['gitDirty'], dirty)
                for call in run.call_args_list:
                    self.assertEqual(call.args[0][:5], ['git', '-c', 'safe.directory=' + self.root.as_posix(), '-C', str(self.root)])
                    self.assertNotIn('\\', call.args[0][2], 'Git safe.directory must survive a different Windows checkout owner')
                    self.assertNotIn('--global', call.args[0])

    def test_checkout_cannot_report_success_without_verified_git_identity(self):
        # A worktree has a .git file, while a regular checkout has a directory.
        for marker in ('file', 'directory'):
            with self.subTest(marker=marker):
                git_marker = self.root / '.git'
                if marker == 'file':
                    git_marker.write_text('gitdir: private-test-worktree', encoding='utf-8')
                else:
                    git_marker.mkdir()
                failures = [FileNotFoundError(), unity_runner.subprocess.TimeoutExpired('git', 10),
                            mock.Mock(returncode=128, stdout='')]
                for failure in failures:
                    with self.subTest(failure=type(failure).__name__):
                        option = {'side_effect': failure} if isinstance(failure, Exception) else {'return_value': failure}
                        with mock.patch.object(unity_runner.subprocess, 'run', **option):
                            with self.assertRaisesRegex(SystemExit, 'Cannot verify runner Git source identity'):
                                unity_runner.source_identity(self.root)
                if marker == 'file':
                    git_marker.unlink()
                else:
                    git_marker.rmdir()

    def test_checkout_rejects_wrong_root_invalid_commit_or_unavailable_status(self):
        (self.root / '.git').mkdir()
        for values in ([str(self.root.parent)], [str(self.root), 'not-a-commit'],
                       [str(self.root), 'a' * 40, None]):
            with self.subTest(values=values):
                results = [mock.Mock(returncode=0 if value is not None else 128, stdout=value or '')
                           for value in values]
                with mock.patch.object(unity_runner.subprocess, 'run', side_effect=results):
                    with self.assertRaisesRegex(SystemExit, 'Cannot verify runner Git source identity'):
                        unity_runner.source_identity(self.root)

    def test_real_checkout_identity_tracks_a_native_source_edit(self):
        # Exercise the actual Git command and dirty-state parsing, rather than
        # accepting a mocked commit/status response as native identity proof.
        import shutil
        if shutil.which('git') is None:
            self.skipTest('Git is not installed on this host')
        def git(*args):
            return unity_runner.subprocess.run(
                ['git', '-c', 'safe.directory=' + self.root.as_posix(), '-C', str(self.root), *args],
                capture_output=True, text=True, check=True, timeout=10).stdout.strip()
        git('init')
        git('add', 'package.json')
        git('-c', 'user.name=Runner Test', '-c', 'user.email=runner-test@example.invalid',
            '-c', 'commit.gpgSign=false', 'commit', '-m', 'Native runner identity fixture')
        commit = git('rev-parse', 'HEAD')
        self.assertEqual(unity_runner.source_identity(self.root), {
            'packageVersion': '0.11.11-dev.1', 'gitCommit': commit, 'gitDirty': False})
        (self.root / 'package.json').write_text('{"version":"0.11.11-dev.2"}', encoding='utf-8')
        self.assertEqual(unity_runner.source_identity(self.root), {
            'packageVersion': '0.11.11-dev.2', 'gitCommit': commit, 'gitDirty': True})


def write_project(project, liltoon='2.2.1', source='embedded', name='jp.lilxyzw.liltoon'):
    """Write a resolved project fixture; only the lock and package.json files matter here."""
    packages = Path(project) / 'Packages'
    packages.mkdir(parents=True, exist_ok=True)
    entry = {'source': source, 'depth': 0, 'dependencies': {}}
    if source == 'embedded':
        folder = packages / 'jp.lilxyzw.liltoon'
        entry['version'] = 'file:jp.lilxyzw.liltoon'
    elif source == 'local':
        folder = Path(project).parent / ('lil-' + liltoon)
        entry['version'] = 'file:' + folder.resolve().as_posix()
    elif source == 'git':
        entry.update(version='https://example.invalid/lilToon.git#' + liltoon, hash='0123456789abcdef')
        folder = Path(project) / 'Library' / 'PackageCache' / 'jp.lilxyzw.liltoon@0123456789'
    else:
        entry['version'] = liltoon
        folder = None
    if folder is not None:
        folder.mkdir(parents=True, exist_ok=True)
        (folder / 'package.json').write_text(json.dumps({'name': name, 'version': liltoon}), encoding='utf-8')
    (packages / 'packages-lock.json').write_text(json.dumps({'dependencies': {'jp.lilxyzw.liltoon': entry}}), encoding='utf-8')


class LilToonExpectationTests(unittest.TestCase):
    # These fixtures test the pre-run gate on the resolved project, not Unity or lilToon.
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.project = Path(self.temp.name) / 'project'

    def test_every_package_source_reports_the_installed_release(self):
        for source in ('embedded', 'local', 'git', 'registry'):
            with self.subTest(source=source):
                project = Path(self.temp.name) / source / 'project'
                write_project(project, '2.1.10', source)
                self.assertEqual(unity_runner.installed_liltoon(project, '2.1.10'),
                                 {'expected': '2.1.10', 'installed': '2.1.10', 'source': source})

    def test_a_different_installed_release_stops_before_unity(self):
        write_project(self.project, '2.3.4')
        for expected in ('2.2.1', '2.3.40', '2.3'):
            with self.subTest(expected=expected):
                with self.assertRaises(SystemExit):
                    unity_runner.installed_liltoon(self.project, expected)

    def test_prerelease_or_malformed_expectation_is_rejected(self):
        write_project(self.project, '2.3.4')
        for expected in ('2.3.4-beta.1', 'v2.3.4', '2.3.4 ', '02.3.4', ''):
            with self.subTest(expected=expected):
                with self.assertRaisesRegex(SystemExit, 'release version'):
                    unity_runner.installed_liltoon(self.project, expected)

    def test_missing_lock_package_or_mislabelled_folder_cannot_satisfy_an_expectation(self):
        with self.assertRaisesRegex(SystemExit, 'Cannot read'):
            unity_runner.installed_liltoon(self.project, '2.3.4')
        write_project(self.project, '2.3.4', name='jp.lilxyzw.liltoon.fork')
        with self.assertRaisesRegex(SystemExit, 'Cannot read'):
            unity_runner.installed_liltoon(self.project, '2.3.4')
        (self.project / 'Packages' / 'packages-lock.json').write_text('{"dependencies": {}}', encoding='utf-8')
        with self.assertRaisesRegex(SystemExit, 'None does not match'):
            unity_runner.installed_liltoon(self.project, '2.3.4')

    def test_git_package_must_be_in_the_package_cache(self):
        lock = {'dependencies': {'jp.lilxyzw.liltoon': {
            'version': 'https://example.invalid/lilToon.git#2.3.0', 'source': 'git', 'hash': 'fedcba9876543210'}}}
        (self.project / 'Packages').mkdir(parents=True)
        (self.project / 'Packages' / 'packages-lock.json').write_text(json.dumps(lock), encoding='utf-8')
        with self.assertRaisesRegex(SystemExit, 'PackageCache'):
            unity_runner.installed_liltoon(self.project, '2.3.0')

    def test_without_expectation_the_receipt_still_names_the_installed_release(self):
        self.assertEqual(unity_runner.installed_liltoon(self.project)['installed'], None)
        write_project(self.project, '2.0.0')
        self.assertEqual(unity_runner.installed_liltoon(self.project),
                         {'expected': None, 'installed': '2.0.0', 'source': 'embedded'})

    def run_main(self, expected, unity_changes_to=None, univrm='0.131.1', profile=None):
        output = Path(self.temp.name) / 'out'
        argv = ['run-unity-compatibility.py', '--unity', 'Unity', '--project', str(self.project),
                '--expect-univrm', univrm, '--expect-unity', '2022.3.22f1', '--output', str(output)]
        if expected:
            argv += ['--expect-liltoon', expected]
        if profile:
            argv += ['--profile', profile]
        required = unity_runner.required_regressions('compatibility')
        def unity(command, **_):
            if profile:
                return mock.Mock(returncode=1)  # Only whether Unity was launched matters.
            if unity_changes_to:
                write_project(self.project, unity_changes_to)
            xml = Path(command[command.index('-testResults') + 1])
            root = ET.Element('test-run', result='Passed')
            for suite, count in unity_runner.compatibility_counts().items():
                name = unity_runner.NAMESPACE + suite
                method = next(iter(required.get(suite, {'Case': 0})))
                for index in range(count):
                    ET.SubElement(root, 'test-case', classname=name, fullname=name + '.' + method + '(' + str(index) + ')', result='Passed')
            ET.ElementTree(root).write(xml)
            return mock.Mock(returncode=0)
        identity = {'packageVersion': '0.0.0', 'gitCommit': None, 'gitDirty': None}
        self.unity_calls = 0
        with mock.patch.object(unity_runner, 'source_identity', return_value=identity), \
             mock.patch.object(unity_runner.subprocess, 'run', side_effect=unity) as run, \
             mock.patch.object(sys, 'argv', argv), mock.patch('builtins.print'):
            try:
                unity_runner.main()
            finally:
                self.unity_calls = run.call_count
        return [json.loads(p.read_text(encoding='utf-8')) for p in output.glob('*.json')]

    def test_receipt_records_the_installed_release(self):
        write_project(self.project, '2.2.1')
        reports = self.run_main('2.2.1')
        self.assertEqual(len(reports), 1)
        self.assertEqual(reports[0]['lilToon'], {'expected': '2.2.1', 'installed': '2.2.1', 'source': 'embedded'})

    def test_any_project_univrm_or_none_runs_every_suite_and_is_recorded(self):
        # The exporter writes with its bundled UniVRM; the project's own copy is only recorded.
        write_project(self.project, '2.3.4')
        for univrm in ('missing', '0.131.1', '0.132.0', '0.131.4-beta.1'):
            with self.subTest(univrm=univrm):
                for path in (Path(self.temp.name) / 'out').glob('*.json'):
                    path.unlink()
                reports = self.run_main(None, univrm=univrm)
                self.assertEqual([(r['expectedUniVrm'], r['tests']) for r in reports],
                                 [(univrm, sum(unity_runner.compatibility_counts().values()))])
                self.assertNotIn('uniVrmSupported', reports[0])

    def test_behavior_profile_starts_unity_whatever_univrm_the_project_has(self):
        write_project(self.project, '2.3.4')
        for univrm in ('missing', '0.132.0'):
            with self.subTest(univrm=univrm):
                with self.assertRaisesRegex(SystemExit, 'Unity failed'):
                    self.run_main(None, univrm=univrm, profile='exporter-behavior')
                self.assertEqual(self.unity_calls, 1)

    def test_mismatch_never_launches_unity_or_writes_a_receipt(self):
        write_project(self.project, '2.3.4')
        with self.assertRaises(SystemExit):
            self.run_main('2.2.1')
        self.assertEqual(self.unity_calls, 0)
        self.assertEqual(list((Path(self.temp.name) / 'out').glob('*.json')), [])

    def test_package_change_during_the_run_is_not_success(self):
        write_project(self.project, '2.2.1')
        with self.assertRaises(SystemExit):
            self.run_main(None, unity_changes_to='2.3.4')
        self.assertEqual(list((Path(self.temp.name) / 'out').glob('*.json')), [])

    def test_project_first_resolved_during_the_run_keeps_the_existing_flow(self):
        # A project Unity has never opened has no lock; Unity writes it while running.
        (self.project / 'Packages').mkdir(parents=True)
        reports = self.run_main(None, unity_changes_to='2.3.4')
        self.assertEqual(reports[0]['lilToon'], {'expected': None, 'installed': '2.3.4', 'source': 'embedded'})
        with self.assertRaisesRegex(SystemExit, 'Cannot read'):
            unity_runner.installed_liltoon(Path(self.temp.name) / 'unopened', '2.3.4')

    def test_malformed_lock_is_unreadable_not_a_crash(self):
        packages = self.project / 'Packages'
        packages.mkdir(parents=True)
        for text in ('[]', '{"dependencies": []}', '{"dependencies": {"jp.lilxyzw.liltoon": "2.3.4"}}', '{'):
            with self.subTest(text=text):
                (packages / 'packages-lock.json').write_text(text, encoding='utf-8')
                self.assertIn('unavailable', unity_runner.installed_liltoon(self.project))
                with self.assertRaisesRegex(SystemExit, 'Cannot read'):
                    unity_runner.installed_liltoon(self.project, '2.3.4')


class VersionProjectTests(unittest.TestCase):
    # Synthetic packages and archives test the project layout, not real UniVRM or lilToon.
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.base = Path(self.temp.name).resolve()
        self.cache = self.base / 'cache'
        self.cache.mkdir()
        self.deps = self.dependencies('deps')
        # Synthetic archives replace the reviewed release hashes for these tests only.
        patcher = mock.patch.dict(version_projects.RELEASE_SHA256, clear=True)
        patcher.start()
        self.addCleanup(patcher.stop)

    def package(self, folder, name, version):
        folder.mkdir(parents=True, exist_ok=True)
        (folder / 'package.json').write_text(json.dumps({'name': name, 'version': version}), encoding='utf-8')
        (folder / 'Runtime.txt').write_text(name, encoding='utf-8')

    def dependencies(self, label, **changes):
        folder = self.base / label
        packages = {'com.vrmc.gltf': '0.131.2', 'com.vrmc.vrm': '0.131.2', 'nadena.dev.ndmf': '1.14.8', **changes}
        for name, version in packages.items():
            if version is not None:
                self.package(folder / name, name, version)
        return folder

    def archive(self, version, members=None, name='jp.lilxyzw.liltoon', folder=None):
        path = (folder or self.cache) / ('jp.lilxyzw.liltoon-' + version + '.zip')
        path.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path, 'w') as bundle:
            bundle.writestr('package.json', json.dumps({'name': name, 'version': version}))
            for member in members or ['Shader/lts.shader']:
                bundle.writestr(member, 'shader')
        version_projects.RELEASE_SHA256[version] = hashlib.sha256(path.read_bytes()).hexdigest()
        return path

    def run_script(self, *args):
        argv = ['create-liltoon-version-projects.py', *args]
        with mock.patch.object(sys, 'argv', argv), mock.patch('builtins.print'):
            version_projects.main()

    def test_dependencies_are_optional_known_packages_with_any_one_univrm(self):
        self.assertEqual(set(version_projects.dependency_packages(self.deps)),
                         {'com.vrmc.gltf', 'com.vrmc.vrm', 'nadena.dev.ndmf'})
        self.assertEqual(version_projects.dependency_packages(None), {})
        # The exporter bundles UniVRM, so a project needs none and may carry any version.
        for index, changes in enumerate(({'com.vrmc.gltf': None, 'com.vrmc.vrm': None},
                                         {'com.vrmc.vrm': '0.132.0', 'com.vrmc.gltf': '0.132.0'},
                                         {'com.vrmc.vrm': '0.131.4-beta.1', 'com.vrmc.gltf': '0.131.4-beta.1'})):
            with self.subTest(changes=changes):
                found = version_projects.dependency_packages(self.dependencies('any' + str(index), **changes))
                self.assertEqual(found.get('com.vrmc.vrm', (None, None))[1], changes['com.vrmc.vrm'])
        cases = [
            ({'com.vrmc.vrm': '0.131.1'}, 'same version'),
            ({'com.vrmc.gltf': None}, 'same version'),
            ({'jp.lilxyzw.liltoon': '2.3.4'}, 'Unexpected package'),
            ({'com.vrvlog.liltoon-vrm-exporter': '0.0.0'}, 'Unexpected package'),
        ]
        for index, (changes, message) in enumerate(cases):
            with self.subTest(changes=changes):
                with self.assertRaisesRegex(SystemExit, message):
                    version_projects.dependency_packages(self.dependencies('bad' + str(index), **changes))

    def test_manifest_pins_test_packages_and_tests_the_referenced_exporter(self):
        manifest = version_projects.manifest(ROOT)
        self.assertEqual(manifest['testables'], ['com.vrvlog.liltoon-vrm-exporter'])
        self.assertEqual(manifest['dependencies']['com.vrvlog.liltoon-vrm-exporter'], 'file:' + ROOT.as_posix())
        self.assertEqual(manifest['dependencies']['com.unity.test-framework'], '1.4.6')
        self.assertEqual(manifest['dependencies']['com.unity.collections'], '2.1.4')
        self.assertNotIn('jp.lilxyzw.liltoon', manifest['dependencies'], 'lilToon is embedded, not resolved from a registry')
        self.assertFalse(any('render-pipelines' in name for name in manifest['dependencies']), 'Built-in RP only')

    def test_archive_must_be_the_requested_release_without_unsafe_paths(self):
        cases = [
            (['../escape.txt'], 'jp.lilxyzw.liltoon', '2.1.0', 'Unsafe'),
            (['/abs.txt'], 'jp.lilxyzw.liltoon', '2.1.0', 'Unsafe'),
            (['C:/abs.txt'], 'jp.lilxyzw.liltoon', '2.1.0', 'Unsafe'),
            (None, 'jp.lilxyzw.liltoon', '2.1.1', 'expected 2.1.0'),
            (None, 'other.package', '2.1.0', 'expected 2.1.0'),
        ]
        for index, (members, name, version, message) in enumerate(cases):
            with self.subTest(members=members, name=name, version=version):
                archive = self.archive(version, members, name, self.base / ('zips' + str(index)))
                with self.assertRaisesRegex(SystemExit, message):
                    version_projects.extract_package(archive, self.base / ('extract' + str(index)) / 'pkg', '2.1.0')
                self.assertFalse((self.base / ('extract' + str(index)) / 'escape.txt').exists())
                self.assertFalse((self.base / ('extract' + str(index))).exists(), 'Nothing is extracted from a rejected archive')

    def test_archive_must_match_the_reviewed_hash(self):
        archive = self.archive('2.3.4')
        version_projects.RELEASE_SHA256['2.3.4'] = '0' * 64
        args = ['--output', str(self.base / 'out'), '--dependencies', str(self.deps), '--versions', '2.3.4',
                '--liltoon-cache', str(self.cache)]
        with self.assertRaisesRegex(SystemExit, 'SHA-256 is'):
            self.run_script(*args)
        self.assertFalse((self.base / 'out' / 'liltoon-2.3.4').exists())
        del version_projects.RELEASE_SHA256['2.3.4']
        with self.assertRaisesRegex(SystemExit, 'No reviewed SHA-256'):
            self.run_script(*args)
        digest = hashlib.sha256(archive.read_bytes()).hexdigest()
        for bad in ('2.3.0=' + digest, '2.3.4=' + digest.upper(), '2.3.4=' + digest[:-1]):
            with self.subTest(sha256=bad), self.assertRaisesRegex(SystemExit, '--sha256 must be'):
                self.run_script(*args, '--sha256', bad)
        self.run_script(*args, '--sha256', '2.3.4=' + digest)
        record = json.loads((self.base / 'out' / 'liltoon-2.3.4' / version_projects.RECORD).read_text())
        self.assertEqual(record['lilToon']['sha256'], digest)

    def test_reviewed_hashes_cover_the_default_versions(self):
        for version in version_projects.DEFAULT_VERSIONS:
            self.assertRegex(REVIEWED_LILTOON_SHA256.get(version, ''), r'\A[0-9a-f]{64}\Z')

    def test_projects_embed_each_release_and_record_its_source_and_hash(self):
        for version in ('2.0.0', '2.3.4'):
            self.archive(version)
        out = self.base / 'out'
        args = ['--output', str(out), '--dependencies', str(self.deps), '--versions', '2.0.0,2.3.4',
                '--liltoon-cache', str(self.cache)]
        with mock.patch.object(version_projects.urllib.request, 'urlopen', side_effect=AssertionError('cached archives are used')):
            self.run_script(*args)
        summary = json.loads((out / 'projects.json').read_text(encoding='utf-8'))
        self.assertEqual([r['lilToon']['version'] for r in summary], ['2.0.0', '2.3.4'])
        for record in summary:
            version = record['lilToon']['version']
            project = out / ('liltoon-' + version)
            self.assertEqual(record['lilToon']['source'],
                             'https://github.com/lilxyzw/lilToon/releases/download/' + version + '/jp.lilxyzw.liltoon-' + version + '.zip')
            self.assertEqual(record['lilToon']['sha256'],
                             hashlib.sha256((self.cache / ('jp.lilxyzw.liltoon-' + version + '.zip')).read_bytes()).hexdigest())
            self.assertIsNone(record['resolved'], 'Without Unity the record must not claim resolved versions')
            self.assertEqual(record['requested']['com.vrmc.vrm'], '0.131.2')
            self.assertEqual(json.loads((project / 'Packages/jp.lilxyzw.liltoon/package.json').read_text())['version'], version)
            self.assertTrue((project / 'Packages/nadena.dev.ndmf/Runtime.txt').is_file())
            self.assertEqual(json.loads((project / 'Packages/manifest.json').read_text())['testables'],
                             ['com.vrvlog.liltoon-vrm-exporter'])
            self.assertTrue((project / 'Assets').is_dir())
        with self.assertRaisesRegex(SystemExit, 'existing project path'):
            self.run_script(*args)

    def test_recorded_versions_come_from_what_unity_resolved(self):
        self.archive('2.2.1')
        out = self.base / 'out'
        def unity(command, **_):
            project = Path(command[command.index('-projectPath') + 1])
            lock = {'dependencies': {name: {'version': 'file:' + name, 'source': 'embedded'}
                                     for name in ('jp.lilxyzw.liltoon', 'com.vrmc.gltf', 'com.vrmc.vrm', 'nadena.dev.ndmf')}}
            lock['dependencies']['com.vrvlog.liltoon-vrm-exporter'] = {'version': 'file:' + ROOT.as_posix(), 'source': 'local'}
            (project / 'Packages/packages-lock.json').write_text(json.dumps(lock), encoding='utf-8')
            (project / 'ProjectSettings').mkdir()
            (project / 'ProjectSettings/ProjectVersion.txt').write_text(
                'm_EditorVersion: 2022.3.22f1\nm_EditorVersionWithRevision: 2022.3.22f1 (887be4894c44)\n', encoding='utf-8')
            return mock.Mock(returncode=0)
        with mock.patch.object(version_projects, 'exporter_identity', return_value={}), \
             mock.patch.object(version_projects.subprocess, 'run', side_effect=unity):
            self.run_script('--output', str(out), '--dependencies', str(self.deps), '--versions', '2.2.1',
                            '--liltoon-cache', str(self.cache), '--unity', 'Unity')
        resolved = json.loads((out / 'liltoon-2.2.1' / version_projects.RECORD).read_text())['resolved']
        self.assertEqual(resolved['unity'], '2022.3.22f1')
        self.assertEqual(resolved['unityRevision'], '887be4894c44')
        self.assertEqual(resolved['packages']['jp.lilxyzw.liltoon'], '2.2.1')
        self.assertEqual(resolved['packages']['com.vrmc.vrm'], '0.131.2')

    def test_unity_import_failure_is_not_recorded_as_a_project(self):
        self.archive('2.3.0')
        with mock.patch.object(version_projects, 'exporter_identity', return_value={}), \
             mock.patch.object(version_projects.subprocess, 'run', return_value=mock.Mock(returncode=1)):
            with self.assertRaisesRegex(SystemExit, 'Unity exited with 1'):
                self.run_script('--output', str(self.base / 'out'), '--dependencies', str(self.deps),
                                '--versions', '2.3.0', '--liltoon-cache', str(self.cache), '--unity', 'Unity')
        self.assertFalse((self.base / 'out' / 'liltoon-2.3.0' / version_projects.RECORD).exists())

    def test_versions_must_be_distinct_releases(self):
        for versions in ('2.3.4,2.3.4', '2.3.4-beta', 'latest'):
            with self.subTest(versions=versions):
                with self.assertRaises(SystemExit):
                    self.run_script('--output', str(self.base / 'out'), '--dependencies', str(self.deps), '--versions', versions)


class UpstreamTests(unittest.TestCase):
    def setUp(self):
        self.config = json.loads((ROOT / 'Compatibility/dependencies.json').read_text())
        self.releases = {name: {'tag_name': 'v0.0.0', 'draft': False, 'prerelease': False} for name in upstream.REPOSITORIES}

    def test_new_release_requests_review_without_modifying_policy(self):
        before = json.dumps(self.config)
        major, minor, patch = upstream.release_version(self.config['lilToon']['verified']['maximum'])
        self.releases['lilToon']['tag_name'] = '%d.%d.%d' % (major, minor, patch + 1)
        result = upstream.compare(self.config, self.releases)
        self.assertTrue(result[0]['needsCompatibilityReview'])
        self.assertEqual(before, json.dumps(self.config))

    def test_numeric_version_order_and_existing_release(self):
        self.assertGreater(upstream.release_version('v0.131.10'), upstream.release_version('0.131.2'))
        # UniVRM is compared with the bundled copy, not with a dependency listed in the manifest.
        self.releases['UniVRM']['tag_name'] = 'v' + upstream.bundled_univrm()
        self.assertFalse(upstream.compare(self.config, self.releases)[1]['needsCompatibilityReview'])
        self.releases['UniVRM']['tag_name'] = 'v0.131.10'
        self.assertTrue(upstream.compare(self.config, self.releases, univrm='0.131.4')[1]['needsCompatibilityReview'])

    def test_unrecognized_and_preview_release_never_reports_safe(self):
        for tag in ['0.131.2-preview.1', '0.131', 'latest', ' 0.131.2']:
            with self.assertRaises(ValueError):
                upstream.release_version(tag)
        self.releases['lilToon']['draft'] = True
        with self.assertRaises(ValueError):
            upstream.compare(self.config, self.releases)


class LilToonRangeGeneratorTests(unittest.TestCase):
    FILES = ['Compatibility/dependencies.json', 'Formats/Runtime/LilToon234Catalogue.cs']

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name in self.FILES:
            (self.root / name).parent.mkdir(parents=True, exist_ok=True)
            (self.root / name).write_bytes((ROOT / name).read_bytes())

    def generate(self, **verified):
        path = self.root / 'Compatibility/dependencies.json'
        config = json.loads(path.read_text(encoding='utf-8'))
        config['lilToon']['verified'].update(verified)
        path.write_text(json.dumps(config), encoding='utf-8')
        return generator.outputs(self.root)['Editor/Compatibility/DependencyPolicy.Generated.cs']

    def test_catalogue_version_is_generated_and_the_verified_range_is_not(self):
        # Nothing in the Exporter stops on the installed lilToon version; the verified
        # range is only read by the structure check.
        for text in [self.generate(), self.generate(maximum='2.10.0')]:
            self.assertIn('LilToonVersion = "2.3.4";', text)
            self.assertNotIn('LilToonMinimum', text)
            self.assertNotIn('LilToonMaximum', text)

    def test_range_must_contain_the_catalogue_and_use_plain_version_numbers(self):
        for verified in [{'minimum': '2.3.5'}, {'maximum': '2.3.3'}, {'maximum': '2.3.4-beta'},
                         {'minimum': '2.0'}, {'minimum': '02.0.0'}, {'minimum': 'v2.0.0'}]:
            with self.subTest(verified=verified), self.assertRaises(AssertionError):
                self.generate(**verified)

    def test_version_order_is_numeric(self):
        self.assertGreater(generator.version_tuple('2.10.0'), generator.version_tuple('2.3.4'))

    def test_univrm_is_bundled_so_no_assembly_or_package_dependency_is_generated(self):
        outputs = generator.outputs(self.root)
        self.assertEqual(list(outputs), ['Editor/Compatibility/DependencyPolicy.Generated.cs'])
        self.assertNotIn('UniVrm', outputs['Editor/Compatibility/DependencyPolicy.Generated.cs'])
        path = self.root / 'Compatibility/dependencies.json'
        config = json.loads(path.read_text(encoding='utf-8'))
        config['uniVrm'] = {'vpmRange': '0.131.x'}
        path.write_text(json.dumps(config), encoding='utf-8')
        with self.assertRaises(AssertionError):
            generator.outputs(self.root)


if __name__ == '__main__':
    unittest.main()
