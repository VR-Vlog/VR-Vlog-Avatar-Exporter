import copy
import importlib.util
import io
import json
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import liltoon_catalogue as lc  # noqa: E402

spec = importlib.util.spec_from_file_location('generator', Path(__file__).with_name('generate-liltoon-catalogue.py'))
generator = importlib.util.module_from_spec(spec)
spec.loader.exec_module(generator)
spec = importlib.util.spec_from_file_location('upstream', Path(__file__).with_name('check-upstream-dependencies.py'))
upstream = importlib.util.module_from_spec(spec)
spec.loader.exec_module(upstream)
spec = importlib.util.spec_from_file_location('structure_check', Path(__file__).with_name('check-liltoon-structure.py'))
structure_check = importlib.util.module_from_spec(spec)
spec.loader.exec_module(structure_check)

# Small synthetic declarations in the shape of lilToon's Built-in RP shaders.
PASS_LIBRARY = '''Shader "Hidden/ltspass_opaque"
{
    Properties
    {
        _DummyProperty ("If you are seeing this, some script is broken.", Float) = 0
        _DummyProperty ("", Float) = 0
        [lilToggle]     _Invisible ("sInvisible", Int) = 0
    }
    SubShader
    {
        HLSLINCLUDE
            // Pass "FAKE" { Name "NOT_A_PASS" }
        ENDHLSL
        Pass
        {
            Name "FORWARD"
            Tags {"LightMode" = "ForwardBase"}
            Stencil { Pass [_StencilPass] }
            HLSLPROGRAM
            #pragma vertex vert
            ENDHLSL
        }
        Pass
        {
            Name "SHADOW_CASTER"
            Tags {"LightMode" = "ShadowCaster"}
            HLSLPROGRAM
            ENDHLSL
        }
    }
}
'''
MAIN = '''Shader "lilToon"
{
    Properties
    {
        //----------------------------------------------------------------------------------------------------------------------
        // Base
        [lilToggle]     _Invisible                  ("sInvisible", Int) = 0
                        _AsUnlit                    ("sAsUnlit (0-1)", Range(0, 1)) = 0
        [lilHDR]        _Color                      ("Color", Color) = (1,1,1,1)
        [MainTexture]   _MainTex                    ("Texture", 2D) = "white" {}
        [NoScaleOffset] _ReflectionCubeTex          ("Cubemap Fallback", Cube) = "black" {}
        [lilVec3]       _LightDirectionOverride     ("sLightDirectionOverride", Vector) = (0.001,0.002,0.001,0)
        /* _Commented ("Not declared", Float) = 0 */
                        _EnvRimBorder               ("[VRCLV] Rim Border", Range(0, 3)) = 3.0
        [HideInInspector] _lilToonVersion ("Version", Int) = 45
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Pass", Float) = 0
    }
    SubShader
    {
        UsePass "Hidden/ltspass_opaque/FORWARD"
        UsePass "Hidden/ltspass_opaque/SHADOW_CASTER"
        Pass
        {
            Tags { "LightMode" = "Never" }
        }
    }
    Fallback "Unlit/Texture"
    CustomEditor "lilToon.lilToonInspector"
}
'''
GEM = '''Shader "Hidden/lilToonGem"
{
    Properties
    {
        [lilToggle]     _Invisible ("sInvisible", Int) = 0
        [lilHDR]        _Color     ("Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        GrabPass {"_lilBackgroundTexture"}
        Pass
        {
            Name "FORWARD_PRE"
            Tags {"LightMode" = "ForwardBase"}
            HLSLPROGRAM
            ENDHLSL
        }
    }
}
'''
BAKER = '''Shader "Hidden/ltsother_baker"
{
    Properties
    {
        [NoScaleOffset] _MainTex3D ("Texture", 3D) = "white" {}
        _ResX ("ResX", Int) = 16
    }
    SubShader { Pass { CGPROGRAM ENDCG } }
}
'''


def write_source(folder, extra=None):
    shader = Path(folder) / 'Assets/lilToon/Shader'
    shader.mkdir(parents=True)
    (shader.parent / 'package.json').write_text(json.dumps({'version': '9.9.9'}), encoding='utf-8')
    files = {'lts.shader': MAIN, 'lts_gem.shader': GEM, 'ltspass_opaque.shader': PASS_LIBRARY,
             'ltspass_baker.shader': BAKER}
    files.update(extra or {})
    for name, text in files.items():
        (shader / name).write_text(text, encoding='utf-8', newline='\n')
    return Path(folder)


class SyntheticSourceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.source = lc.load_source(write_source(self.temp.name))
        self.catalogue = lc.build_catalogue(self.source, 'a' * 40)

    def test_properties_keep_declaration_order_types_and_dimensions(self):
        main = next(s for s in self.source['shaders'] if s['name'] == 'lilToon')
        self.assertEqual(['_Invisible', '_AsUnlit', '_Color', '_MainTex', '_ReflectionCubeTex', '_LightDirectionOverride',
                          '_EnvRimBorder', '_lilToonVersion', '_StencilPass'], [p[0] for p in main['properties']])
        types = {name: (kind, dimension) for name, kind, dimension in main['properties']}
        self.assertEqual(('float', None), types['_Invisible'])
        self.assertEqual(('float', None), types['_AsUnlit'])
        self.assertEqual(('texture', '2D'), types['_MainTex'])
        self.assertEqual(('texture', 'Cube'), types['_ReflectionCubeTex'])
        baker = next(s for s in self.source['shaders'] if s['name'] == 'Hidden/ltsother_baker')
        self.assertEqual(('_MainTex3D', 'texture', '3D'), baker['properties'][0])

    def test_use_pass_resolves_names_and_light_modes_and_skips_unnamed_passes(self):
        shaders = {s['name']: s for s in self.catalogue['shaders']}
        self.assertEqual([('FORWARD', 'ForwardBase'), ('SHADOW_CASTER', 'ShadowCaster')], shaders['lilToon']['passes'])
        self.assertEqual([('FORWARD_PRE', 'ForwardBase')], shaders['Hidden/lilToonGem']['passes'])

    def test_pass_libraries_are_not_material_shaders_but_declare_properties(self):
        self.assertEqual(['lilToon', 'Hidden/lilToonGem'], [s['name'] for s in self.catalogue['shaders']])
        roles = {p[0]: p[3] for p in self.catalogue['properties']}
        self.assertEqual('editor', roles['_MainTex3D'])
        self.assertEqual('external', roles['_EnvRimBorder'])
        self.assertNotIn('_DummyProperty', roles)
        self.assertNotIn('_Commented', roles)

    def test_required_properties_are_the_runtime_properties_of_each_shader(self):
        main = next(s for s in self.catalogue['shaders'] if s['name'] == 'lilToon')
        self.assertEqual(['_Invisible', '_AsUnlit', '_Color', '_MainTex', '_ReflectionCubeTex',
                          '_LightDirectionOverride', '_StencilPass'], main['required'])

    def test_rendered_catalogue_reads_back_with_the_same_structure(self):
        text = lc.render_catalogue(self.catalogue)
        self.assertTrue(text.startswith('// Generated from the official lilToon 9.9.9 shader declarations.\n'))
        self.assertIn('internal static class LilToon999Catalogue', text)
        self.assertIn('{"_ReflectionCubeTex", 6},', text)
        self.assertIn('{"Hidden/lilToonGem", "FORWARD_PRE".Split(', text)
        back = lc.parse_catalogue(text)
        self.assertEqual(lc.structure_hash(self.catalogue), lc.structure_hash(back))

    def test_conflicting_property_types_stop_generation(self):
        conflicting = GEM.replace('_Color     ("Color", Color)', '_Color     ("Color", Vector)')
        with tempfile.TemporaryDirectory() as temp:
            source = lc.load_source(write_source(temp, {'lts_gem.shader': conflicting}))
        with self.assertRaises(lc.CatalogueError):
            lc.build_catalogue(source, 'a' * 40)

    def test_unknown_property_type_and_unresolved_use_pass_stop_parsing(self):
        with self.assertRaises(lc.CatalogueError):
            lc.parse_shader(MAIN.replace('Vector) = (0.001', 'Matrix) = (0.001'))
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(lc.CatalogueError):
                lc.load_source(write_source(temp, {'lts.shader': MAIN.replace('/SHADOW_CASTER"', '/META"')}))

    def test_unreadable_declarations_and_unexpected_passes_stop_parsing(self):
        multi_line = MAIN.replace('[lilVec3]       _LightDirectionOverride',
                                  '[Enum(A, 0,\n            B, 1)] _LightDirectionOverride')
        self.assertIn('_LightDirectionOverride', [p[0] for p in lc.parse_shader(multi_line)['properties']])
        broken = {
            'missing default': MAIN.replace('("Color", Color) = (1,1,1,1)', '("Color", Color)'),
            'unknown syntax': MAIN.replace('[MainTexture]   _MainTex', '[MainTexture]   _MainTex Texture'),
            'second SubShader': MAIN.replace('    Fallback "Unlit/Texture"', '    SubShader { Pass { Name "X" } }\n    Fallback "Unlit/Texture"'),
        }
        for label, text in broken.items():
            with self.subTest(case=label), self.assertRaises(lc.CatalogueError):
                lc.parse_shader(text)
        unnamed = MAIN.replace('Tags { "LightMode" = "Never" }', 'Tags { "LightMode" = "ShadowCaster" }')
        with tempfile.TemporaryDirectory() as temp:
            source = lc.load_source(write_source(temp, {'lts.shader': unnamed}))
        with self.assertRaises(lc.CatalogueError):
            lc.build_catalogue(source, 'a' * 40)

    def test_each_structural_change_changes_the_hash_and_values_do_not(self):
        base = lc.structure_hash(self.catalogue)
        changes = {
            'property type': MAIN.replace('("sAsUnlit (0-1)", Range(0, 1))', '("sAsUnlit (0-1)", Vector)'),
            'texture dimension': MAIN.replace('("Cubemap Fallback", Cube)', '("Cubemap Fallback", 2D)'),
            'property removed': MAIN.replace('_AsUnlit                    ("sAsUnlit (0-1)", Range(0, 1)) = 0', ''),
            'property added': MAIN.replace('    }\n    SubShader', '        _NewProperty ("New", Float) = 0\n    }\n    SubShader', 1),
            'pass removed': MAIN.replace('UsePass "Hidden/ltspass_opaque/SHADOW_CASTER"', ''),
            'shader renamed': MAIN.replace('Shader "lilToon"', 'Shader "lilToonRenamed"'),
        }
        for label, text in changes.items():
            with self.subTest(change=label), tempfile.TemporaryDirectory() as temp:
                changed = lc.build_catalogue(lc.load_source(write_source(temp, {'lts.shader': text})), 'a' * 40)
                self.assertNotEqual(base, lc.structure_hash(changed))
        library = PASS_LIBRARY.replace('"LightMode" = "ShadowCaster"', '"LightMode" = "Meta"')
        with tempfile.TemporaryDirectory() as temp:
            changed = lc.build_catalogue(lc.load_source(write_source(temp, {'ltspass_opaque.shader': library})), 'a' * 40)
            self.assertNotEqual(base, lc.structure_hash(changed))
        same = MAIN.replace('= 3.0', '= 0.85').replace('("sInvisible", Int) = 0', '("Hidden", Int) = 1')
        with tempfile.TemporaryDirectory() as temp:
            unchanged = lc.build_catalogue(lc.load_source(write_source(temp, {'lts.shader': same})), 'b' * 40)
        self.assertEqual(base, lc.structure_hash(unchanged))


class CommittedCatalogueTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / lc.CATALOGUE_PATH).read_text(encoding='ascii')
        self.catalogue = lc.parse_catalogue(self.text)
        self.table = json.loads((ROOT / lc.STRUCTURE_PATH).read_text(encoding='utf-8'))

    def test_catalogue_counts_and_round_trip(self):
        self.assertEqual('2.3.4', self.catalogue['version'])
        self.assertEqual(52, len(self.catalogue['shaders']))
        self.assertEqual(614, len(self.catalogue['properties']))
        # parse_catalogue keeps row order, so rendering the parsed model reproduces the file.
        self.assertEqual(self.text, lc.render_catalogue(self.catalogue))

    def test_structure_table_passes_for_the_committed_files(self):
        self.assertEqual([], lc.check_structure_table(self.table, self.catalogue, lc.DEFAULT_SUPPORTED_RANGE))
        self.assertEqual([], structure_check.check()[2])

    def test_one_changed_catalogue_entry_fails_the_check(self):
        edits = {
            'property type': ('{"_Cutoff", "float:runtime"}', '{"_Cutoff", "vector:runtime"}'),
            'property role': ('{"_AsUnlit", "float:runtime"}', '{"_AsUnlit", "float:editor"}'),
            'texture faces': ('{"_ReflectionCubeTex", 6}', '{"_ReflectionCubeTex", 1}'),
            'light mode': ('{"lilToon", new Dictionary<string, string>(StringComparer.Ordinal) {{"FORWARD", "ForwardBase"}',
                           '{"lilToon", new Dictionary<string, string>(StringComparer.Ordinal) {{"FORWARD", "ForwardAdd"}'),
            'shader file': ('{"Hidden/lilToonGem", "lts_gem"}', '{"Hidden/lilToonGem", "lts_gem2"}'),
            'required property': ('{"_lil/[Optional] lilToonFakeShadow", "_Invisible,', '{"_lil/[Optional] lilToonFakeShadow", "'),
        }
        for label, (old, new) in edits.items():
            with self.subTest(edit=label):
                self.assertEqual(1, self.text.count(old))
                # Each edit still reads as a catalogue, so only the structure hash can catch it.
                changed = lc.parse_catalogue(self.text.replace(old, new))
                self.assertNotEqual([], lc.check_structure_table(self.table, changed, lc.DEFAULT_SUPPORTED_RANGE))

    def test_inconsistent_pass_sections_cannot_be_read(self):
        old = '{"lilToon", "FORWARD,FORWARD_ADD,META,SHADOW_CASTER".Split('
        self.assertEqual(1, self.text.count(old))
        with self.assertRaises(lc.CatalogueError):
            lc.parse_catalogue(self.text.replace(old, '{"lilToon", "FORWARD,META,SHADOW_CASTER".Split('))


class StructureTableTests(unittest.TestCase):
    def setUp(self):
        self.catalogue = lc.parse_catalogue((ROOT / lc.CATALOGUE_PATH).read_text(encoding='ascii'))
        self.table = json.loads((ROOT / lc.STRUCTURE_PATH).read_text(encoding='utf-8'))

    def problems(self, table, bounds=lc.DEFAULT_SUPPORTED_RANGE):
        return lc.check_structure_table(table, self.catalogue, bounds)

    def entry(self, table, tag):
        return next(e for e in table['tags'] if e['tag'] == tag)

    def test_committed_table_covers_every_tag_of_the_supported_range(self):
        tags = [e['tag'] for e in self.table['tags']]
        expected = ['2.0.0'] + ['2.1.%d' % n for n in range(11)] + ['2.2.0', '2.2.1'] + ['2.3.%d' % n for n in range(5)]
        self.assertEqual(['1.10.3'] + expected, tags)
        self.assertFalse(self.entry(self.table, '1.10.3')['matchesReference'])

    def test_a_differing_tag_inside_the_range_fails(self):
        table = copy.deepcopy(self.table)
        entry = self.entry(table, '2.1.5')
        entry['structureHash'] = 'f' * 64
        entry['matchesReference'] = False
        self.assertTrue(any('2.1.5 is in the supported range' in p for p in self.problems(table)))

    def test_a_differing_tag_outside_the_range_is_recorded_without_failing(self):
        self.assertEqual([], self.problems(self.table))
        self.assertTrue(any('1.10.3' in p for p in self.problems(self.table, ('1.10.3', '2.3.4'))))

    def test_wrong_flag_missing_bound_order_and_shape_fail(self):
        cases = []
        table = copy.deepcopy(self.table)
        self.entry(table, '1.10.3')['matchesReference'] = True
        cases.append(table)
        table = copy.deepcopy(self.table)
        table['tags'] = [e for e in table['tags'] if e['tag'] != '2.0.0']
        cases.append(table)
        table = copy.deepcopy(self.table)
        table['tags'].reverse()
        cases.append(table)
        table = copy.deepcopy(self.table)
        table['tags'].append(dict(table['tags'][-1]))
        cases.append(table)
        table = copy.deepcopy(self.table)
        table['tags'][1]['extra'] = 1
        cases.append(table)
        table = copy.deepcopy(self.table)
        table['reference']['structureHash'] = 'e' * 64
        cases.append(table)
        table = copy.deepcopy(self.table)
        del table['definition']
        cases.append(table)
        for index, table in enumerate(cases):
            with self.subTest(case=index):
                self.assertNotEqual([], self.problems(table))

    def test_supported_range_reads_dependencies_or_falls_back(self):
        self.assertEqual(('2.0.0', '2.3.4'), lc.supported_range({'lilToon': {'version': '2.3.4'}}))
        self.assertEqual(('2.1.0', '2.3.4'), lc.supported_range(
            {'lilToon': {'supportedRange': {'minimum': '2.1.0', 'maximum': '2.3.4'}}}))
        self.assertTrue(lc.in_range('2.1.10', ('2.0.0', '2.3.4')))
        self.assertFalse(lc.in_range('2.3.5', ('2.0.0', '2.3.4')))
        with self.assertRaises(lc.CatalogueError):
            lc.parse_version('2.4.0-beta')


def tar_gz(members, commit='c' * 40):
    data = io.BytesIO()
    with tarfile.open(fileobj=data, mode='w:gz', format=tarfile.PAX_FORMAT, pax_headers={'comment': commit}) as archive:
        for name, text in members.items():
            payload = text.encode('utf-8')
            info = tarfile.TarInfo(name)
            info.size = len(payload)
            archive.addfile(info, io.BytesIO(payload))
    return data.getvalue()


class FetchTests(unittest.TestCase):
    def response(self, payload):
        context = mock.MagicMock()
        context.__enter__.return_value.read.return_value = payload
        return context

    def test_archive_commit_and_files_are_read_without_the_top_folder(self):
        payload = tar_gz({'lilToon-9.9.9/Assets/lilToon/Shader/lts.shader': MAIN})
        with tempfile.TemporaryDirectory() as temp, \
                mock.patch.object(lc.urllib.request, 'urlopen', return_value=self.response(payload)) as urlopen:
            self.assertEqual('c' * 40, lc.fetch_tag_source('9.9.9', temp))
            self.assertTrue((Path(temp) / 'Assets/lilToon/Shader/lts.shader').is_file())
        self.assertIn('/lilxyzw/lilToon/tar.gz/refs/tags/9.9.9', urlopen.call_args[0][0].full_url)

    def test_archive_escape_missing_commit_and_bad_tag_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            escape = tar_gz({'lilToon-9.9.9/../../outside.txt': 'x'})
            with mock.patch.object(lc.urllib.request, 'urlopen', return_value=self.response(escape)):
                with self.assertRaises(lc.CatalogueError):
                    lc.fetch_tag_source('9.9.9', temp)
            self.assertFalse((Path(temp).parent / 'outside.txt').exists())
            unsigned = tar_gz({'lilToon-9.9.9/a.txt': 'x'}, commit='')
            with mock.patch.object(lc.urllib.request, 'urlopen', return_value=self.response(unsigned)):
                with self.assertRaises(lc.CatalogueError):
                    lc.fetch_tag_source('9.9.9', temp)
            with self.assertRaises(lc.CatalogueError):
                lc.fetch_tag_source('../9.9.9', temp)
            large = tar_gz({'lilToon-9.9.9/a.txt': 'x' * 64})
            with mock.patch.object(lc.urllib.request, 'urlopen', return_value=self.response(large)), \
                    mock.patch.object(lc, 'MAX_EXTRACTED_BYTES', 32), self.assertRaises(lc.CatalogueError):
                lc.fetch_tag_source('9.9.9', temp)


class UpstreamTests(unittest.TestCase):
    def setUp(self):
        self.text = (ROOT / lc.CATALOGUE_PATH).read_text(encoding='ascii')
        self.reference = lc.parse_catalogue(self.text)

    def rows(self, newer):
        return [{'package': 'lilToon', 'reference': '2.3.4', 'upstream': '2.3.5' if newer else '2.3.4',
                 'needsCompatibilityReview': newer}]

    def test_newer_liltoon_reports_whether_its_structure_matches(self):
        load = mock.Mock(return_value=self.reference)
        row = upstream.add_liltoon_structure(self.rows(True), self.text, load)
        load.assert_called_once_with('2.3.5')
        self.assertTrue(row['structureMatchesReference'])
        self.assertEqual([], row['structureDifferences'])
        changed = copy.deepcopy(self.reference)
        changed['shaders'][0]['passes'] = changed['shaders'][0]['passes'][:-1]
        row = upstream.add_liltoon_structure(self.rows(True), self.text, mock.Mock(return_value=changed))
        self.assertFalse(row['structureMatchesReference'])
        self.assertTrue(row['structureDifferences'][0].startswith('shader lilToon passes'))

    def test_current_release_is_not_downloaded_and_failures_are_reported(self):
        load = mock.Mock()
        row = upstream.add_liltoon_structure(self.rows(False), self.text, load)
        load.assert_not_called()
        self.assertNotIn('structureHash', row)
        row = upstream.add_liltoon_structure(self.rows(True), self.text, mock.Mock(side_effect=OSError('offline')))
        self.assertEqual('OSError: offline', row['structureError'])


class GeneratorCommandTests(unittest.TestCase):
    def test_check_fails_for_a_different_source_and_writes_output(self):
        with tempfile.TemporaryDirectory() as temp:
            source = write_source(Path(temp) / 'src')
            output = Path(temp) / 'out.cs'
            with mock.patch('sys.stdout', new=io.StringIO()):
                code = generator.main(['catalogue', '--source', str(source), '--commit', 'd' * 40,
                                       '--output', str(output), '--check'])
            self.assertEqual(1, code)
            self.assertIn('LilToon999Catalogue', output.read_text(encoding='ascii'))

    def test_structure_table_marks_the_reference(self):
        reference = lc.parse_catalogue((ROOT / lc.CATALOGUE_PATH).read_text(encoding='ascii'))
        changed = copy.deepcopy(reference)
        changed['properties'] = changed['properties'][:-1]
        changed['commit'] = 'e' * 40
        table = generator.build_table({'2.3.4': reference, '1.0.0': changed}, reference)
        self.assertEqual(['1.0.0', '2.3.4'], [e['tag'] for e in table['tags']])
        self.assertEqual([False, True], [e['matchesReference'] for e in table['tags']])


if __name__ == '__main__':
    unittest.main()
