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

    def test_no_pairs_exit_1(self):
        empty_a, empty_b = self.root / 'ea', self.root / 'eb'
        empty_a.mkdir()
        empty_b.mkdir()
        code, out, _ = run_main([empty_a, empty_b])
        self.assertEqual(1, code)
        self.assertIn('0 pairs', out)


if __name__ == '__main__':
    unittest.main()
