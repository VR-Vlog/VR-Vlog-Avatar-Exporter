import contextlib
import copy
import importlib.util
import io
import json
import struct
import tempfile
import unittest
from pathlib import Path
from unittest import mock

spec = importlib.util.spec_from_file_location('compare_exports', Path(__file__).with_name('compare-exports.py'))
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)

BINARY = struct.pack('<9f', 0, 0, 0, 1, 0, 0, 0, 1, 0)


def pack_glb(document, binary=BINARY):
    """Pack a glTF JSON object and BIN payload into a GLB with 4-byte chunk padding."""
    text = json.dumps(document).encode('utf-8')
    text += b' ' * (-len(text) % 4)
    chunks = struct.pack('<II', len(text), compare.JSON_CHUNK) + text
    if binary:
        binary += b'\0' * (-len(binary) % 4)
        chunks += struct.pack('<II', len(binary), compare.BIN_CHUNK) + binary
    return struct.pack('<4sII', b'glTF', 2, 12 + len(chunks)) + chunks


def document():
    """A VRM 1.0 shaped document with one preset and one custom expression."""
    return {
        'asset': {'version': '2.0', 'generator': 'synthetic', 'extras': {'exportedAt': 'first'}},
        'buffers': [{'byteLength': len(BINARY)}],
        'meshes': [{'name': 'Face'}],
        'nodes': [{'name': 'Face', 'mesh': 0}],
        'extensions': {'VRMC_vrm': {'expressions': {
            'preset': {'happy': {'morphTargetBinds': [{'node': 0, 'index': 1, 'weight': 1.0}],
                                 'isBinary': False, 'overrideBlink': 'block'}},
            'custom': {'Wink': {'morphTargetBinds': [{'node': 0, 'index': 2, 'weight': 0.5}],
                                'isBinary': True}},
        }}},
    }


def result(ident, run, ok=True, warnings=('Shape A was dropped',)):
    return {'label': 'test', 'id': ident, 'prefab': 'Assets/Avatar.prefab', 'run': run, 'ok': ok,
            'failure': None if ok else 'failed', 'warnings': list(warnings), 'export_seconds': 1.25}


def write(directory, stem, res, doc=None):
    directory.mkdir(parents=True, exist_ok=True)
    (directory / (stem + '.json')).write_text(json.dumps(res), encoding='utf-8')
    if doc is not None:
        (directory / (stem + '.vrm')).write_bytes(pack_glb(doc))


def run_main(argv):
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        code = compare.main([str(a) for a in argv])
    return code, out.getvalue(), err.getvalue()


class CompareExportsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.a, self.b = self.root / 'a', self.root / 'b'
        for directory in (self.a, self.b):
            write(directory, 'A1_1', result('A1', 1), document())
            write(directory, 'A1_2', result('A1', 2), document())
            (directory / 'A1_open.json').write_text('{"open_seconds": 2.5}', encoding='utf-8')

    def tearDown(self):
        self.temp.cleanup()

    def test_identical_results_exit_0(self):
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)

    def test_changed_morph_target_bind_weight_names_the_expression(self):
        doc = document()
        doc['extensions']['VRMC_vrm']['expressions']['custom']['Wink']['morphTargetBinds'][0]['weight'] = 0.25
        write(self.b, 'A1_2', result('A1', 2), doc)
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('custom:Wink morphTargetBinds', out)
        self.assertIn('0.25', out)
        self.assertIn('2 pairs, 1 mismatched', out)

    def test_extra_warning_exit_1(self):
        write(self.b, 'A1_1', result('A1', 1, warnings=('Shape A was dropped', 'Material B was baked')), document())
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('warnings differ (1 vs 2)', out)

    def test_volatile_path_difference_is_ignored(self):
        doc = document()
        doc['asset']['extras']['exportedAt'] = 'second'
        write(self.b, 'A1_1', result('A1', 1), doc)
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, 'a non-volatile difference must still be reported: ' + out)
        self.assertIn('/asset/extras/exportedAt', out)
        with mock.patch.object(compare, 'VOLATILE', ['/asset/extras/exportedAt']):
            code, out, _ = run_main([self.a, self.b])
        self.assertEqual(0, code, out)

    def test_strip_volatile_wildcard_matches_any_key_or_index(self):
        doc = {'nodes': [{'name': 'A', 'extras': {'id': 1}}, {'name': 'B', 'extras': {'id': 2}}]}
        stripped = compare.strip_volatile(doc, compare.volatile_patterns(['/nodes/*/extras/id']))
        self.assertEqual({'nodes': [{'name': 'A', 'extras': {}}, {'name': 'B', 'extras': {}}]}, stripped)
        self.assertEqual(doc, compare.strip_volatile(copy.deepcopy(doc), compare.volatile_patterns([])))

    def emission_image_pair(self, name_a, name_b):
        for directory, name in ((self.a, name_a), (self.b, name_b)):
            doc = document()
            doc['images'] = [{'name': 'Body_Main'}, {'name': name, 'mimeType': 'image/png'}]
            write(directory, 'A1_1', result('A1', 1), doc)
        return run_main([self.a, self.b])

    def test_emission_guid_suffix_difference_is_ignored(self):
        code, out, _ = self.emission_image_pair('Body_Main__VRVlog_Emission_' + '0123456789abcdef' * 2,
                                                'Body_Main__VRVlog_Emission_' + 'fedcba9876543210' * 2)
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)

    def test_emission_name_with_other_prefix_exit_1(self):
        code, out, _ = self.emission_image_pair('Body_Main__VRVlog_Emission_' + '0123456789abcdef' * 2,
                                                'Hair_Main__VRVlog_Emission_' + 'fedcba9876543210' * 2)
        self.assertEqual(1, code, out)
        self.assertIn('/images/1/name', out)
        code, out, _ = self.emission_image_pair('Body_Main__VRVlog_Emission_' + '0123456789abcdef' * 2,
                                                'Body_Main__VRVlog_Glow_' + 'fedcba9876543210' * 2)
        self.assertEqual(1, code, out)
        self.assertIn('/images/1/name', out)

    def test_diff_paths_does_not_mask_volatile_sub(self):
        self.emission_image_pair('Body_Main__VRVlog_Emission_' + '0123456789abcdef' * 2,
                                 'Body_Main__VRVlog_Emission_' + 'fedcba9876543210' * 2)
        code, out, _ = run_main(['--diff-paths', self.a / 'A1_1.vrm', self.b / 'A1_1.vrm'])
        self.assertEqual((1, ['/images/1/name']), (code, out.split()))

    def merged_bone_pair(self, name_a, name_b):
        for directory, name in ((self.a, name_a), (self.b, name_b)):
            doc = document()
            doc['nodes'] = [{'name': 'Hips'}, {'name': name, 'translation': [0, 1, 0]}]
            write(directory, 'A1_1', result('A1', 1), doc)
        return run_main([self.a, self.b])

    def test_merged_bone_guid_suffix_difference_is_ignored(self):
        code, out, _ = self.merged_bone_pair('Skirt.L$01234567-89ab-cdef-0123-456789abcdef',
                                             'Skirt.L$fedcba98-7654-3210-fedc-ba9876543210')
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)

    def test_merged_bone_with_other_name_or_other_suffix_exit_1(self):
        code, out, _ = self.merged_bone_pair('Skirt.L$01234567-89ab-cdef-0123-456789abcdef',
                                             'Skirt.R$fedcba98-7654-3210-fedc-ba9876543210')
        self.assertEqual(1, code, out)
        self.assertIn('/nodes/1/name', out)
        code, out, _ = self.merged_bone_pair('Skirt.L$01234567-89ab-cdef-0123-456789abcdef', 'Skirt.L')
        self.assertEqual(1, code, out)
        self.assertIn('/nodes/1/name', out)
        code, out, _ = self.merged_bone_pair('Skirt.L$1', 'Skirt.L$2')
        self.assertEqual(1, code, out)
        self.assertIn('/nodes/1/name', out)

    def test_missing_vrm_exit_1(self):
        (self.b / 'A1_1.vrm').unlink()
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('vrm missing in B', out)

    def test_missing_result_json_exit_1_unless_ignored(self):
        write(self.a, 'A2_1', result('A2', 1), document())
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('result json missing in B', out)
        code, out, _ = run_main([self.a, self.b, '--ignore-missing'])
        self.assertEqual(0, code, out)

    def test_runs_mode_pairs_two_runs_in_one_directory(self):
        code, out, _ = run_main(['--runs', self.a, '1', '2'])
        self.assertEqual(0, code, out)
        self.assertIn('A1_1 vs A1_2', out)
        self.assertIn('1 pairs, 0 mismatched', out)

    def test_bin_chunk_difference_exit_1(self):
        (self.b / 'A1_1.vrm').write_bytes(pack_glb(document(), BINARY[:-4] + struct.pack('<f', 2)))
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('BIN chunk differs', out)

    def test_json_output(self):
        code, out, _ = run_main([self.a, self.b, '--json'])
        self.assertEqual(0, code)
        report = json.loads(out)
        self.assertEqual({'pairs': 2, 'mismatched': 0}, {k: report[k] for k in ('pairs', 'mismatched')})
        self.assertTrue(report['rows'][0]['file_equal'])

    def test_diff_paths_lists_changed_paths(self):
        doc = document()
        doc['asset']['extras']['exportedAt'] = 'second'
        (self.root / 'x.vrm').write_bytes(pack_glb(doc))
        code, out, _ = run_main(['--diff-paths', self.a / 'A1_1.vrm', self.root / 'x.vrm'])
        self.assertEqual(1, code)
        self.assertEqual(['/asset/extras/exportedAt'], out.split())
        code, out, _ = run_main(['--diff-paths', self.a / 'A1_1.vrm', self.a / 'A1_2.vrm'])
        self.assertEqual((0, ''), (code, out))

    def test_usage_and_format_errors_exit_2(self):
        self.assertEqual(2, run_main([self.a])[0])
        self.assertEqual(2, run_main([self.a, self.root / 'none'])[0])
        self.assertEqual(2, run_main([self.a, self.b, '--runs', self.a, '1', '2'])[0])
        (self.b / 'A1_1.vrm').write_bytes(b'not a glb file')
        code, _, err = run_main([self.a, self.b])
        self.assertEqual(2, code)
        self.assertIn('not a glb', err)

    def test_runs_mode_allows_added_extension(self):
        write(self.a, 'A1_2', result('A1', 2), with_added(document()))
        code, out, _ = run_main(['--runs', self.a, '1', '2'])
        self.assertEqual(1, code, out)
        code, out, _ = run_main(['--runs', self.a, '1', '2', '--allow-added-extension', ADDED])
        self.assertEqual(0, code, out)
        self.assertIn('added extension allowed: %s (%d bytes of JSON)' % (ADDED, ADDED_BYTES), out)

    def test_no_pairs_exit_1(self):
        empty_a, empty_b = self.root / 'ea', self.root / 'eb'
        empty_a.mkdir()
        empty_b.mkdir()
        code, out, _ = run_main([empty_a, empty_b])
        self.assertEqual(1, code)
        self.assertIn('0 pairs', out)


ADDED = 'VRVLOG_added'
ADDED_VALUE = {'schemaVersion': 1, 'items': [{'name': 'Item', 'value': 0.5}]}
ADDED_BYTES = len(json.dumps(ADDED_VALUE, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode('utf-8'))


def with_added(doc, name=ADDED, value=None, version='1.0.0'):
    """Add a root extension the way the exporter does: extensions, extensionsUsed and the
    per-extension entry of asset.extras.VRVLOG_requiredAppVersions."""
    doc['extensions'][name] = copy.deepcopy(ADDED_VALUE if value is None else value)
    doc.setdefault('extensionsUsed', []).append(name)
    doc['asset']['extras'].setdefault('VRVLOG_requiredAppVersions', {})[name] = version
    return doc


class AllowAddedExtensionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.a, self.b = self.root / 'a', self.root / 'b'
        write(self.a, 'A001_1', result('A001', 1), document())
        write(self.b, 'A001_1', result('A001', 1), with_added(document()))

    def tearDown(self):
        self.temp.cleanup()

    def run_allowed(self, *extra):
        return run_main([self.a, self.b, '--allow-added-extension', ADDED] + list(extra))

    def test_only_added_extension_matches_and_reports_name_and_length(self):
        code, out, _ = self.run_allowed()
        self.assertEqual(0, code, out)
        self.assertIn('1 pairs, 0 mismatched', out)
        self.assertIn('added extension allowed: %s (%d bytes of JSON)' % (ADDED, ADDED_BYTES), out)
        code, out, _ = self.run_allowed('--json')
        self.assertEqual(0, code, out)
        row = json.loads(out)['rows'][0]
        self.assertEqual([{'name': ADDED, 'bytes': ADDED_BYTES}], row['allowed_extensions'])
        self.assertEqual([], row['issues'])

    def test_without_option_added_extension_is_a_difference(self):
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('/extensions/' + ADDED, out)
        self.assertNotIn('added extension allowed', out)
        code, out, _ = run_main([self.a, self.b, '--json'])
        self.assertNotIn('allowed_extensions', json.loads(out)['rows'][0])

    def test_other_difference_still_mismatches(self):
        def other_extension(doc):
            doc['extensions']['VRMC_vrm']['specVersion'] = '1.0'

        def node_name(doc):
            doc['nodes'][0]['name'] = 'Face2'

        for change, expected in ((other_extension, '/extensions/VRMC_vrm/specVersion'), (node_name, '/nodes/0/name')):
            with self.subTest(change.__name__):
                doc = with_added(document())
                change(doc)
                write(self.b, 'A001_1', result('A001', 1), doc)
                code, out, _ = self.run_allowed()
                self.assertEqual(1, code, out)
                self.assertIn(expected, out)
                self.assertIn('added extension allowed', out)

    def test_bin_difference_still_mismatches(self):
        (self.b / 'A001_1.vrm').write_bytes(pack_glb(with_added(document()), BINARY[:-4] + struct.pack('<f', 2)))
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('BIN chunk differs', out)

    def test_expression_difference_still_mismatches(self):
        doc = with_added(document())
        doc['extensions']['VRMC_vrm']['expressions']['custom']['Wink']['isBinary'] = False
        write(self.b, 'A001_1', result('A001', 1), doc)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('custom:Wink isBinary', out)

    def test_warning_difference_still_mismatches(self):
        write(self.b, 'A001_1', result('A001', 1, warnings=()), with_added(document()))
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('warnings differ', out)

    def test_entries_added_without_adding_the_extension_stay_a_difference(self):
        def body_in_both(doc_a, doc_b):
            doc_a['extensions'][ADDED] = copy.deepcopy(ADDED_VALUE)
            doc_b['extensions'][ADDED] = copy.deepcopy(ADDED_VALUE)

        def body_in_neither(doc_a, doc_b):
            pass

        def version(doc):
            doc['asset']['extras']['VRVLOG_requiredAppVersions'] = {ADDED: '2.0.0'}

        def used(doc):
            doc['extensionsUsed'] = [ADDED]

        for body in (body_in_both, body_in_neither):
            for entry, expected in ((version, '/asset/extras/VRVLOG_requiredAppVersions'), (used, '/extensionsUsed')):
                with self.subTest('%s %s' % (body.__name__, entry.__name__)):
                    doc_a, doc_b = document(), document()
                    body(doc_a, doc_b)
                    entry(doc_b)
                    write(self.a, 'A001_1', result('A001', 1), doc_a)
                    write(self.b, 'A001_1', result('A001', 1), doc_b)
                    code, out, _ = self.run_allowed()
                    self.assertEqual(1, code, out)
                    self.assertIn(expected, out)
                    self.assertNotIn('added extension allowed', out)

    def test_extension_listed_twice_in_extensions_used_stays_a_difference(self):
        doc = with_added(document())
        doc['extensionsUsed'].append(ADDED)
        write(self.b, 'A001_1', result('A001', 1), doc)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensionsUsed', out)

    def test_extension_in_both_with_changed_content_mismatches(self):
        write(self.a, 'A001_1', result('A001', 1), with_added(document(), value={'schemaVersion': 1, 'items': []}))
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensions/%s/items' % ADDED, out)
        self.assertNotIn('added extension allowed', out)

    def test_extension_only_in_a_mismatches(self):
        write(self.a, 'A001_1', result('A001', 1), with_added(document()))
        write(self.b, 'A001_1', result('A001', 1), document())
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensions/' + ADDED, out)
        self.assertNotIn('added extension allowed', out)

    def test_other_added_extension_mismatches(self):
        write(self.b, 'A001_1', result('A001', 1), with_added(document(), name='VRVLOG_other'))
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensions/VRVLOG_other', out)
        self.assertIn('/extensionsUsed', out)
        self.assertIn('/asset/extras/VRVLOG_requiredAppVersions', out)
        write(self.b, 'A001_1', result('A001', 1), with_added(with_added(document()), name='VRVLOG_other'))
        code, out, _ = run_main([self.a, self.b, '--allow-added-extension', ADDED,
                                 '--allow-added-extension', 'VRVLOG_other'])
        self.assertEqual(0, code, out)
        self.assertIn('added extension allowed: VRVLOG_other', out)

    def test_extensions_used_keeps_entries_that_a_also_has(self):
        doc_a, doc_b = document(), with_added(document())
        doc_a['extensionsUsed'] = ['VRMC_vrm']
        doc_b['extensionsUsed'] = ['VRMC_vrm', ADDED]
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        self.assertEqual(0, self.run_allowed()[0])
        # NAME is removed even when it is not last, and the order of the rest is compared.
        doc_b['extensionsUsed'] = [ADDED, 'VRMC_vrm']
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        self.assertEqual(0, self.run_allowed()[0])
        # An empty list in A is kept in B after removing NAME, so the two still match.
        doc_a['extensionsUsed'] = []
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        doc_b['extensionsUsed'] = [ADDED]
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        self.assertEqual(0, self.run_allowed()[0])
        # Another added entry in extensionsUsed is still a difference.
        doc_b['extensionsUsed'] = [ADDED, 'VRVLOG_other']
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensionsUsed', out)

    def test_required_app_versions_allows_only_the_entry_of_name(self):
        doc_a, doc_b = document(), with_added(document())
        doc_a['asset']['extras']['VRVLOG_requiredAppVersions'] = {'VRVLOG_kept': '1.0.0'}
        doc_b['asset']['extras']['VRVLOG_requiredAppVersions']['VRVLOG_kept'] = '1.0.0'
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        self.assertEqual(0, self.run_allowed()[0])
        # A changed version of another extension is still a difference.
        doc_b['asset']['extras']['VRVLOG_requiredAppVersions']['VRVLOG_kept'] = '1.0.1'
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/asset/extras/VRVLOG_requiredAppVersions/VRVLOG_kept', out)
        # A version for NAME that A already has is compared, not removed.
        doc_a['asset']['extras']['VRVLOG_requiredAppVersions'][ADDED] = '0.9.0'
        doc_b['asset']['extras']['VRVLOG_requiredAppVersions']['VRVLOG_kept'] = '1.0.0'
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/asset/extras/VRVLOG_requiredAppVersions/' + ADDED, out)

    def test_required_app_versions_not_in_the_per_extension_form_is_a_difference(self):
        doc = with_added(document())
        doc['asset']['extras']['VRVLOG_requiredAppVersions'] = '1.0.0'
        write(self.b, 'A001_1', result('A001', 1), doc)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/asset/extras/VRVLOG_requiredAppVersions', out)

    def test_diff_paths_is_not_affected(self):
        code, out, _ = run_main(['--diff-paths', self.a / 'A001_1.vrm', self.b / 'A001_1.vrm',
                                 '--allow-added-extension', ADDED])
        self.assertEqual(1, code, out)
        self.assertEqual(['/asset/extras/VRVLOG_requiredAppVersions', '/extensions/' + ADDED, '/extensionsUsed'],
                         out.split())

    def test_input_documents_are_not_changed(self):
        doc_a, doc_b = document(), with_added(document())
        before = copy.deepcopy(doc_b)
        _, removed = compare.remove_added_extensions(doc_a, doc_b, [ADDED])
        self.assertEqual(before, doc_b)
        self.assertEqual([{'name': ADDED, 'bytes': ADDED_BYTES}], removed)


    def test_container_that_b_already_had_empty_stays_a_difference(self):
        doc_a = {'asset': {'version': '2.0'}}
        doc_b = {'asset': {'version': '2.0', 'extras': {compare.REQUIRED_APP_VERSIONS: {}}},
                 'extensions': {}, 'extensionsUsed': []}
        stripped, removed = compare.remove_added_extensions(doc_a, doc_b, [ADDED])
        self.assertEqual(doc_b, stripped)
        self.assertEqual([], removed)

    def test_containers_emptied_by_the_removal_go_away_when_a_lacks_them(self):
        doc_a = {'asset': {'version': '2.0'}}
        doc_b = {'asset': {'version': '2.0', 'extras': {compare.REQUIRED_APP_VERSIONS: {ADDED: '1.0.0'}}},
                 'extensions': {ADDED: {'schemaVersion': 1}}, 'extensionsUsed': [ADDED]}
        stripped, removed = compare.remove_added_extensions(doc_a, doc_b, [ADDED])
        self.assertEqual(doc_a, stripped)
        self.assertEqual([ADDED], [item['name'] for item in removed])


if __name__ == '__main__':
    unittest.main()
