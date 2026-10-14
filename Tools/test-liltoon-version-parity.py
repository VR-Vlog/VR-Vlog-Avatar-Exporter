import contextlib
import importlib.util
import io
import json
import struct
import tempfile
import unittest
import zlib
from pathlib import Path

spec = importlib.util.spec_from_file_location('parity', Path(__file__).with_name('compare-liltoon-version-exports.py'))
parity = importlib.util.module_from_spec(spec)
spec.loader.exec_module(parity)

IMAGE = b'\x89PNG synthetic image bytes'
POSITIONS = struct.pack('<9f', 0, 0, 0, 1, 0, 0, 0, 1, 0)


def deflate(data):
    compressor = zlib.compressobj(9, zlib.DEFLATED, -15)
    return compressor.compress(data) + compressor.flush()


def pack_glb(document, binary=b''):
    """Pack a glTF JSON object and BIN payload into a GLB with 4-byte chunk padding."""
    text = json.dumps(document).encode('utf-8')
    text += b' ' * (-len(text) % 4)
    chunks = struct.pack('<II', len(text), parity.JSON_CHUNK) + text
    if binary:
        binary += b'\0' * (-len(binary) % 4)
        chunks += struct.pack('<II', len(binary), parity.BIN_CHUNK) + binary
    return struct.pack('<4sII', b'glTF', 2, 12 + len(chunks)) + chunks


def build(version='2.1.0', chunk_payloads=(b'texture texels', b'vertex ids'), image=IMAGE, mutate=None):
    """Return GLB bytes in the shape of an exporter output with one material and two chunks."""
    views, binary = [], b''

    def append(data):
        nonlocal binary
        binary += b'\0' * (-len(binary) % 4)
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': len(data)})
        binary += data
        return len(views) - 1

    image_view = append(image)
    position_view = append(POSITIONS)
    chunks = [{'bufferView': append(deflate(payload)), 'decodedBytes': len(payload), 'codec': 'deflate'}
              for payload in chunk_payloads]
    document = {
        'asset': {'version': '2.0', 'generator': 'synthetic',
                  'extras': {parity.ALLOWED_KEY: version, 'exporter': 'test'}},
        'buffers': [{'byteLength': len(binary)}],
        'bufferViews': views,
        'images': [{'name': 'Body_MainTex', 'mimeType': 'image/png', 'bufferView': image_view}],
        'accessors': [{'bufferView': position_view, 'componentType': 5126, 'count': 3, 'type': 'VEC3'}],
        'materials': [{'name': 'Face'}, {'name': 'Body'}],
        'extensions': {parity.EXTENSION: {
            'schemaMajor': 1, 'schemaMinor': 0, 'sourceLilToonVersion': '2.3.4',
            'sourceCommit': 'abc', 'exporterVersion': '1.0.0',
            'materials': [
                {'name': 'Face', 'values': [{'name': '_Cutoff', 'value': 0.5}], 'textures': [], 'passes': []},
                {'name': 'Body',
                 'values': [{'name': '_BacklightColor', 'value': [0.85, 0.8, 0.7, 1.0]},
                            {'name': '_UseBacklight', 'value': 1}],
                 'textures': [{'name': '_MainTex', 'texture': 0}],
                 'passes': [{'name': 'FORWARD', 'enabled': True}]},
            ],
            'textures': [{'name': 'Body_MainTex', 'width': 4, 'height': 4, 'chunks': [0]}],
            'bindings': [{'vertexIds': 1}],
            'chunks': chunks,
        }},
    }
    if mutate:
        mutate(document)
    return pack_glb(document, binary)


def run_cli(*args):
    stdout, stderr = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
        code = parity.main([str(arg) for arg in args])
    return code, stdout.getvalue(), stderr.getvalue()


class ReadGlbTests(unittest.TestCase):
    def test_reads_json_and_bin(self):
        document, binary = parity.read_glb(build())
        self.assertEqual('2.1.0', document['asset']['extras'][parity.ALLOWED_KEY])
        self.assertTrue(binary.startswith(IMAGE))

    def test_malformed_input_raises_value_error(self):
        good = build()
        cases = {
            'short': b'glTF',
            'magic': b'gLTF' + good[4:],
            'version': good[:4] + struct.pack('<I', 1) + good[8:],
            'length': good[:8] + struct.pack('<I', len(good) + 4) + good[12:],
            'truncated': good[:-4],
            'first chunk': pack_glb({}, b'')[:16] + struct.pack('<I', parity.BIN_CHUNK) + pack_glb({}, b'')[20:],
            'json': struct.pack('<4sII', b'glTF', 2, 24) + struct.pack('<II', 4, parity.JSON_CHUNK) + b'{{{{',
        }
        for name, data in cases.items():
            with self.subTest(name), self.assertRaises(ValueError):
                parity.read_glb(data)

    def test_accepts_vrm_paths(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'avatar.vrm'
            path.write_bytes(build())
            self.assertIn('asset', parity.read_glb(path)[0])


class CompareTests(unittest.TestCase):
    def test_identical_except_version_is_ok(self):
        report = parity.compare(build('2.1.0'), build('2.3.4'))
        self.assertTrue(report['identical_except_allowed'])
        self.assertEqual('2.1.0', report['allowed']['a'])
        self.assertEqual('2.3.4', report['allowed']['b'])
        self.assertEqual([], report['json_differences'])
        self.assertEqual([], report['binary_differences'])
        self.assertEqual(report['json_sha256']['a'], report['json_sha256']['b'])
        self.assertEqual(report['bin_sha256']['a'], report['bin_sha256']['b'])

    def test_per_export_placeholder_names_are_renumbered(self):
        def names(first, second):
            def mutate(document):
                document['meshes'] = [{'name': 'Hair', 'extras': {'targetNames': ['Hair detail', first, second]}}]
            return mutate
        guid = lambda c: c * 32
        a = build(mutate=names('__VRVlog_BlinkNone_' + guid('a'), '__VRVlog_Empty_' + guid('b')))
        b = build('2.3.4', mutate=names('__VRVlog_BlinkNone_' + guid('c'), '__VRVlog_Empty_' + guid('d')))
        report = parity.compare(a, b)
        self.assertTrue(report['identical_except_allowed'])
        self.assertEqual({'a': 2, 'b': 2}, report['normalized_random_names'])

    def test_placeholder_names_must_keep_their_structure(self):
        def names(first, second):
            def mutate(document):
                document['meshes'] = [{'name': 'Hair', 'extras': {'targetNames': [first, second]}}]
            return mutate
        one, two = '__VRVlog_BlinkNone_' + 'a' * 32, '__VRVlog_BlinkNone_' + 'b' * 32
        # Two distinct placeholders in A, the same one twice in B.
        report = parity.compare(build(mutate=names(one, two)), build('2.3.4', mutate=names(one, one)))
        self.assertEqual(1, report['json_difference_count'])
        # Only a full 32-digit lowercase GUID suffix is normalized.
        short = '__VRVlog_BlinkNone_' + 'a' * 31
        report = parity.compare(build(mutate=names(short, two)), build('2.3.4', mutate=names('__VRVlog_BlinkNone_' + 'c' * 31, two)))
        self.assertEqual(1, report['json_difference_count'])

    def test_material_value_change_names_material_and_property(self):
        def mutate(document):
            document['extensions'][parity.EXTENSION]['materials'][1]['values'][0]['value'][1] = 0.8000001
        report = parity.compare(build(), build('2.3.4', mutate=mutate))
        self.assertFalse(report['identical_except_allowed'])
        self.assertEqual(['extensions.VRVLOG_materials_liltoon.materials[1:Body].values[_BacklightColor].value[1]'],
                         [item['path'] for item in report['json_differences']])
        self.assertEqual((0.8, 0.8000001), (report['json_differences'][0]['a'], report['json_differences'][0]['b']))

    def test_image_byte_change_is_labelled_image(self):
        report = parity.compare(build(), build('2.3.4', image=IMAGE[:-1] + b'X'))
        self.assertEqual([], report['json_differences'])
        (difference,) = report['binary_differences']
        self.assertEqual(0, difference['bufferView'])
        self.assertEqual(['image[0:Body_MainTex]'], difference['usage'])
        self.assertNotEqual(difference['a_sha256'], difference['b_sha256'])
        self.assertNotIn('a_decoded_sha256', difference)

    def test_chunk_change_is_labelled_with_texture_and_decoded_hash(self):
        report = parity.compare(build(), build('2.3.4', chunk_payloads=(b'texture texelz', b'vertex ids')))
        self.assertEqual([], report['json_differences'])
        (difference,) = report['binary_differences']
        self.assertEqual(2, difference['bufferView'])
        self.assertEqual(['liltoon.chunk[0]', 'liltoon.texture[0:Body_MainTex]'], difference['usage'])
        self.assertNotEqual(difference['a_decoded_sha256'], difference['b_decoded_sha256'])
        self.assertEqual(parity._sha256(b'texture texels'), difference['a_decoded_sha256'])

    def test_chunk_not_referenced_by_texture_has_only_chunk_label(self):
        report = parity.compare(build(), build('2.3.4', chunk_payloads=(b'texture texels', b'vertex idz')))
        self.assertEqual(['liltoon.chunk[1]'], report['binary_differences'][0]['usage'])

    def test_accessor_bytes_are_compared(self):
        a, b = bytearray(build()), bytearray(build())
        document, binary = parity.read_glb(bytes(b))
        offset = len(b) - len(binary) + document['bufferViews'][1]['byteOffset']
        b[offset] ^= 1
        (difference,) = parity.compare(bytes(a), bytes(b))['binary_differences']
        self.assertEqual(['accessor[0]'], difference['usage'])

    def test_missing_allowed_key_is_a_difference(self):
        def mutate(document):
            del document['asset']['extras'][parity.ALLOWED_KEY]
        report = parity.compare(build(), build(mutate=mutate))
        self.assertFalse(report['identical_except_allowed'])
        self.assertIsNone(report['allowed']['b'])
        self.assertEqual([{'path': 'asset.extras.VRVLOG_lilToonInstalledVersion', 'a': '2.1.0', 'b': None,
                           'reason': 'missing'}], report['json_differences'])

    def test_other_extras_key_is_a_difference(self):
        def mutate(document):
            document['asset']['extras']['lilToonPath'] = 'Packages/jp.lilxyzw.liltoon'
        report = parity.compare(build(), build('2.3.4', mutate=mutate))
        self.assertEqual(['asset.extras.lilToonPath'], [item['path'] for item in report['json_differences']])

    def test_list_length_mismatch(self):
        def mutate(document):
            document['bufferViews'][0]['extra'] = [1, 2]
            document['buffers'].append({'byteLength': 0})
        report = parity.compare(build(), build('2.3.4', mutate=mutate))
        paths = {(item['path'], item['reason']) for item in report['json_differences']}
        self.assertIn(('buffers', 'length'), paths)
        self.assertIn(('buffers[1]', 'missing'), paths)
        self.assertIn(('bufferViews[0].extra', 'missing'), paths)

    def test_name_matched_item_on_one_side(self):
        def mutate(document):
            document['extensions'][parity.EXTENSION]['materials'][1]['values'].append({'name': '_BacklightBorder', 'value': 0.35})
        report = parity.compare(build(), build('2.3.4', mutate=mutate))
        (item,) = report['json_differences']
        self.assertEqual('extensions.VRVLOG_materials_liltoon.materials[1:Body].values[_BacklightBorder]', item['path'])
        self.assertIsNone(item['a'])
        self.assertEqual({'name': '_BacklightBorder', 'value': 0.35}, item['b'])

    def test_name_matched_reorder_is_a_difference(self):
        def mutate(document):
            document['materials'].reverse()
        report = parity.compare(build(), build('2.3.4', mutate=mutate))
        self.assertEqual([('materials', 'order')], [(i['path'], i['reason']) for i in report['json_differences']])

    def test_bool_and_number_differ_but_int_and_float_match(self):
        def to_bool(document):
            document['extensions'][parity.EXTENSION]['materials'][1]['values'][1]['value'] = True
        def to_float(document):
            document['extensions'][parity.EXTENSION]['materials'][1]['values'][1]['value'] = 1.0
        report = parity.compare(build(), build('2.3.4', mutate=to_bool))
        (item,) = report['json_differences']
        self.assertEqual(('extensions.VRVLOG_materials_liltoon.materials[1:Body].values[_UseBacklight].value', 'type'),
                         (item['path'], item['reason']))
        self.assertTrue(parity.compare(build(), build('2.3.4', mutate=to_float))['identical_except_allowed'])

    def test_reported_differences_are_capped_but_counted(self):
        def mutate(document):
            document['asset']['extras']['many'] = list(range(parity.MAX_REPORTED + 10))
        def base(document):
            document['asset']['extras']['many'] = [-1] * (parity.MAX_REPORTED + 10)
        report = parity.compare(build(mutate=base), build(mutate=mutate))
        self.assertEqual(parity.MAX_REPORTED, len(report['json_differences']))
        self.assertEqual(parity.MAX_REPORTED + 10, report['json_difference_count'])


class DirectoryAndCliTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.a = Path(self.tmp.name) / 'a'
        self.b = Path(self.tmp.name) / 'b'
        for root, version in ((self.a, '2.1.0'), (self.b, '2.3.4')):
            (root / 'nested').mkdir(parents=True)
            (root / 'Avatar.vrm').write_bytes(build(version))
            (root / 'nested' / 'Prop.glb').write_bytes(build(version))
            (root / 'notes.txt').write_text('ignored', encoding='utf-8')

    def test_directory_pairs_by_relative_name(self):
        result = parity.compare_dirs(self.a, self.b)
        self.assertTrue(result['ok'])
        self.assertEqual(['Avatar.vrm', 'nested/Prop.glb'], sorted(result['pairs']))

    def test_directory_file_on_one_side_fails(self):
        (self.b / 'nested' / 'Prop.glb').unlink()
        (self.b / 'OnlyB.glb').write_bytes(build('2.3.4'))
        result = parity.compare_dirs(self.a, self.b)
        self.assertFalse(result['ok'])
        self.assertEqual(['nested/Prop.glb'], result['missing_in_b'])
        self.assertEqual(['OnlyB.glb'], result['missing_in_a'])

    def test_cli_exit_codes_and_report(self):
        report_path = Path(self.tmp.name) / 'out' / 'report.json'
        code, stdout, _ = run_cli(self.a, self.b, '--report', report_path,
                                  '--expect-a-version', '2.1.0', '--expect-b-version', '2.3.4')
        self.assertEqual(0, code, stdout)
        self.assertIn('RESULT OK', stdout)
        self.assertTrue(json.loads(report_path.read_text(encoding='utf-8'))['ok'])

        code, stdout, _ = run_cli(self.a / 'Avatar.vrm', self.b / 'Avatar.vrm', '--expect-a-version', '2.0.0')
        self.assertEqual(1, code)
        self.assertIn('VERSION', stdout)

        (self.b / 'Avatar.vrm').write_bytes(build('2.3.4', image=b'other image bytes'))
        code, stdout, _ = run_cli(self.a / 'Avatar.vrm', self.b / 'Avatar.vrm')
        self.assertEqual(1, code)
        self.assertIn('image[0:Body_MainTex]', stdout)

    def test_cli_usage_and_format_errors_exit_2(self):
        self.assertEqual(2, run_cli(self.a, self.b / 'Avatar.vrm')[0])
        self.assertEqual(2, run_cli(self.a / 'missing.vrm', self.b / 'Avatar.vrm')[0])
        self.assertEqual(2, run_cli('--unknown')[0])
        (self.b / 'Avatar.vrm').write_bytes(b'not a glb at all')
        code, _, stderr = run_cli(self.a / 'Avatar.vrm', self.b / 'Avatar.vrm')
        self.assertEqual(2, code)
        self.assertIn('magic', stderr)


if __name__ == '__main__':
    unittest.main()
