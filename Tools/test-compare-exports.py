import contextlib
import copy
import importlib.util
import io
import json
import os
import re
import struct
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

spec = importlib.util.spec_from_file_location('compare_exports', Path(__file__).with_name('compare-exports.py'))
compare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare)

BINARY = struct.pack('<9f', 0, 0, 0, 1, 0, 0, 0, 1, 0)
# A bone's name with the GUID that the avatar's build steps put at its end, as it appears in a warning.
BONE = re.compile(r'[^ ,:]+\$' + compare.GUID)


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

    def test_merged_bone_guid_followed_by_other_text_exit_1(self):
        code, out, _ = self.merged_bone_pair('Skirt.L$01234567-89ab-cdef-0123-456789abcdefx',
                                             'Skirt.L$fedcba98-7654-3210-fedc-ba9876543210x')
        self.assertEqual(1, code, out)
        self.assertIn('/nodes/1/name', out)

    def test_guid_suffix_outside_nodes_exit_1(self):
        for directory, guid in ((self.a, '01234567-89ab-cdef-0123-456789abcdef'),
                                (self.b, 'fedcba98-7654-3210-fedc-ba9876543210')):
            doc = document()
            doc['meshes'][0]['name'] = 'Face$' + guid
            write(directory, 'A1_1', result('A1', 1), doc)
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('/meshes/0/name', out)

    GUID_A, GUID_B = '01234567-89ab-cdef-0123-456789abcdef', 'fedcba98-7654-3210-fedc-ba9876543210'

    def test_constraint_helper_guid_difference_is_ignored(self):
        code, out, _ = self.merged_bone_pair('Hand$ConstraintRef ' + self.GUID_A, 'Hand$ConstraintRef ' + self.GUID_B)
        self.assertEqual(0, code, out)
        for name_b in ('Foot$ConstraintRef ' + self.GUID_B, 'Hand$OtherRef ' + self.GUID_B,
                       'Hand$ConstraintRef  ' + self.GUID_B, 'Hand$' + self.GUID_B):
            code, out, _ = self.merged_bone_pair('Hand$ConstraintRef ' + self.GUID_A, name_b)
            self.assertEqual(1, code, name_b + ': ' + out)
            self.assertIn('/nodes/1/name', out)
        # A form that was not seen stays a difference even when only the GUID differs.
        for form in ('Hand$OtherRef %s', 'Hand$ConstraintRef  %s', 'Hand$%s-Other Endpoint', 'Hand$%sx'):
            code, out, _ = self.merged_bone_pair(form % self.GUID_A, form % self.GUID_B)
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('/nodes/1/name', out)

    def test_spring_end_joint_below_a_merged_bone_is_ignored(self):
        for suffix in ('-VRVlog Spring Endpoint', '-VRVlog Spring Endpoint 2', '-VRVlog Spring Endpoint 13'):
            code, out, _ = self.merged_bone_pair('Tail$' + self.GUID_A + suffix, 'Tail$' + self.GUID_B + suffix)
            self.assertEqual(0, code, suffix + ': ' + out)
        for suffix_a, suffix_b in (('-VRVlog Spring Endpoint', '-VRVlog Spring Endpoint 2'),
                                   ('-VRVlog Spring Endpoint', ''),
                                   ('-VRVlog Spring Endpoint x', '-VRVlog Spring Endpoint x'),
                                   ('-Other Endpoint', '-Other Endpoint'),
                                   (' VRVlog Spring Endpoint', ' VRVlog Spring Endpoint')):
            code, out, _ = self.merged_bone_pair('Tail$' + self.GUID_A + suffix_a, 'Tail$' + self.GUID_B + suffix_b)
            self.assertEqual(1, code, repr((suffix_a, suffix_b)) + ': ' + out)
            self.assertIn('/nodes/1/name', out)

    def spring_pair(self, name_a, name_b, key='springs'):
        for directory, name in ((self.a, name_a), (self.b, name_b)):
            doc = document()
            doc['extensions']['VRMC_springBone'] = {'specVersion': '1.0', key: [{'name': 'Hair'}, {'name': name}]}
            write(directory, 'A1_1', result('A1', 1), doc)
        return run_main([self.a, self.b])

    def test_spring_named_after_a_merged_bone_is_ignored(self):
        code, out, _ = self.spring_pair('Skirt.L$' + self.GUID_A, 'Skirt.L$' + self.GUID_B)
        self.assertEqual(0, code, out)
        for name_b in ('Skirt.R$' + self.GUID_B, 'Skirt.L$' + self.GUID_B + 'x', 'Skirt.L'):
            code, out, _ = self.spring_pair('Skirt.L$' + self.GUID_A, name_b)
            self.assertEqual(1, code, name_b + ': ' + out)
            self.assertIn('/extensions/VRMC_springBone/springs/1/name', out)
        # A spring name takes only the plain form, not the forms of node names.
        for form in ('Skirt.L$%sx', 'Skirt.L$ConstraintRef %s', 'Skirt.L$%s-VRVlog Spring Endpoint'):
            code, out, _ = self.spring_pair(form % self.GUID_A, form % self.GUID_B)
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('/extensions/VRMC_springBone/springs/1/name', out)
        # Only the names of springs carry the GUID; the other lists of the extension are compared as is.
        code, out, _ = self.spring_pair('Skirt.L$' + self.GUID_A, 'Skirt.L$' + self.GUID_B, key='colliderGroups')
        self.assertEqual(1, code, out)
        self.assertIn('/extensions/VRMC_springBone/colliderGroups/1/name', out)

    def warning_pair(self, warnings_a, warnings_b, bones=True):
        """One pair with the given warnings. With bones, each output has a node for every
        "<name>$<GUID>" in its warnings, as an export has for the bones its warnings name."""
        for directory, warnings in ((self.a, warnings_a), (self.b, warnings_b)):
            doc = document()
            if bones:
                doc['nodes'] += [{'name': name} for warning in warnings for name in BONE.findall(warning)]
            write(directory, 'A1_1', result('A1', 1, warnings=warnings), doc)
        return run_main([self.a, self.b])

    def node_pair(self, names_a, names_b, warning_a, warning_b):
        """One pair with one warning each and, behind "Hips", nodes of the given names."""
        for directory, names, warning in ((self.a, names_a, warning_a), (self.b, names_b, warning_b)):
            doc = document()
            doc['nodes'] = [{'name': 'Hips'}] + [{'name': name} for name in names]
            write(directory, 'A1_1', result('A1', 1, warnings=[warning]), doc)
        return run_main([self.a, self.b])

    def test_warning_about_a_merged_bone_ignores_only_the_guid(self):
        text = ': Gravity is not carried over'
        code, out, _ = self.warning_pair(['Skirt.L$' + self.GUID_A + text, 'Shape A was dropped'],
                                         ['Skirt.L$' + self.GUID_B + text, 'Shape A was dropped'])
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)
        for warnings_b in (['Skirt.R$' + self.GUID_B + text, 'Shape A was dropped'],
                           ['Skirt.L$' + self.GUID_B + ': Stiffness is not carried over', 'Shape A was dropped'],
                           ['Shape A was dropped', 'Skirt.L$' + self.GUID_B + text],
                           ['Skirt.L$' + self.GUID_B + text],
                           ['Skirt.L' + text, 'Shape A was dropped']):
            code, out, _ = self.warning_pair(['Skirt.L$' + self.GUID_A + text, 'Shape A was dropped'], warnings_b)
            self.assertEqual(1, code, repr(warnings_b) + ': ' + out)
            self.assertIn('warnings differ', out)

    def test_warning_that_moves_to_another_bone_of_the_same_name_exit_1(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        text = ': Gravity is not carried over'

        def pair(target_b):
            names_a = ['Hair$' + guids[0], 'Hair$' + guids[1], 'Hair$' + guids[0] + '-VRVlog Spring Endpoint']
            names_b = ['Hair$' + guids[2], 'Hair$' + guids[3], 'Hair$' + guids[2] + '-VRVlog Spring Endpoint']
            return self.node_pair(names_a, names_b, 'Hair$' + guids[0] + text, 'Hair$' + target_b + text)

        code, out, _ = pair(guids[2])
        self.assertEqual(0, code, out)
        code, out, _ = pair(guids[3])
        self.assertEqual(1, code, out)
        self.assertIn('warnings differ (1 vs 1), first at #0', out)
        self.assertIn('(nodes [1, 3])', out)
        self.assertIn('(nodes [2])', out)

    def test_guid_of_a_merged_bone_is_ignored_wherever_the_warning_has_it(self):
        # What tells that a GUID changes on every build is the node that has it, not where it is in
        # the warning: at the start, in a list, or in the middle of the text.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Skirt.L$%s was merged',
                     'Head: no reference to Skirt.L$%s: skipped', 'Kept: Skirt.L$%s,Tail', 'Kept: Skirt.L$%s and Tail',
                     'Kept: Tail, Skirt.L$%s', 'Follows Skirt.L$%s -> Hips'):
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_B])
            self.assertEqual(0, code, form + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)
            # The text around the GUID is still compared.
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_B + '.'])
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('warnings differ (1 vs 1), first at #0', out)

    def test_warning_that_lists_merged_bones_ignores_only_the_guids(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        form = 'Parts kept where they are: Skirt.L$%s, Tail$%s'
        code, out, _ = self.warning_pair([form % (guids[0], guids[1]), 'Shape A was dropped'],
                                         [form % (guids[2], guids[3]), 'Shape A was dropped'])
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)
        one = 'Parts kept where they are: Tail$%s'
        code, out, _ = self.warning_pair([one % guids[0]], [one % guids[2]])
        self.assertEqual(0, code, out)
        # A warning that starts with a bone's name can also name another bone at its end.
        both = 'Skirt.L$%s: Collides with Tail$%s'
        code, out, _ = self.warning_pair([both % (guids[0], guids[1])], [both % (guids[2], guids[3])])
        self.assertEqual(0, code, out)
        for form_b in ('Parts kept where they are: Skirt.R$%s, Tail$%s',
                       'Parts kept where they are: Tail$%s, Skirt.L$%s',
                       'Parts moved: Skirt.L$%s, Tail$%s',
                       'Parts kept where they are: Skirt.L$%s, Tail$%s, Ribbon',
                       'Parts kept where they are: Skirt.L$%s, Tail$%sx',
                       'Parts kept where they are: Skirt.L$%s and Tail$%s'):
            code, out, _ = self.warning_pair([form % (guids[0], guids[1])], [form_b % (guids[2], guids[3])])
            self.assertEqual(1, code, form_b + ': ' + out)
            self.assertIn('warnings differ (1 vs 1), first at #0', out)
        for warnings_b in (['Parts kept where they are: Skirt.L$' + guids[2]],
                           ['Parts kept where they are: Skirt.L, Tail$' + guids[3]],
                           [form % (guids[2], guids[3]), 'Shape A was dropped']):
            code, out, _ = self.warning_pair([form % (guids[0], guids[1])], warnings_b)
            self.assertEqual(1, code, repr(warnings_b) + ': ' + out)
            self.assertIn('warnings differ', out)

    def test_listed_bone_that_moves_to_another_bone_of_the_same_name_exit_1(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 7)]
        form = 'Parts kept where they are: Tail$%s, Hair$%s'

        def pair(listed_b):
            return self.node_pair(['Hair$' + guids[0], 'Hair$' + guids[1], 'Tail$' + guids[2]],
                                  ['Hair$' + guids[3], 'Hair$' + guids[4], 'Tail$' + guids[5]],
                                  form % (guids[2], guids[0]), form % (guids[5], listed_b))

        code, out, _ = pair(guids[3])
        self.assertEqual(0, code, out)
        code, out, _ = pair(guids[4])
        self.assertEqual(1, code, out)
        self.assertIn('warnings differ (1 vs 1), first at #0', out)
        self.assertIn('(nodes [3] / [1])', out)
        self.assertIn('(nodes [3] / [2])', out)

    def test_guid_that_no_node_has_is_compared(self):
        # Only the GUID in the name of an exported bone is known to change on every build, so a GUID
        # that no node has in its name has to be the same, wherever it is in the warning.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s',
                     'Lookup failed: asset$%s', 'Lookup failed: asset$%s, other', 'Merged %s: done', 'Kept: Tail, %s'):
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_B], bones=False)
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('warnings differ (1 vs 1), first at #0', out)
            self.assertIn(self.GUID_A, out)
            self.assertIn(self.GUID_B, out)
            self.assertNotIn('(nodes', out)
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_A], bones=False)
            self.assertEqual(0, code, form + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)

    def test_guid_of_a_bone_that_only_one_output_has_exit_1(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        form = 'Parts kept where they are: Tail$%s, Skirt.L$%s'
        code, out, _ = self.node_pair(['Tail$' + guids[0], 'Skirt.L$' + guids[1]], ['Tail$' + guids[2]],
                                      form % (guids[0], guids[1]), form % (guids[2], guids[3]))
        self.assertEqual(1, code, out)
        self.assertIn('warnings differ (1 vs 1), first at #0', out)
        self.assertIn('(nodes [1] / [2])', out)
        self.assertIn('(nodes [1])', out)

    def test_pair_that_failed_to_export_compares_every_guid(self):
        # Without an output there is no node that has the GUID, so it has to be the same.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s'):
            for directory, guid in ((self.a, self.GUID_A), (self.b, self.GUID_B)):
                write(directory, 'A2_1', result('A2', 1, ok=False, warnings=[form % guid]))
            code, out, _ = run_main([self.a, self.b])
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('3 pairs, 1 mismatched', out)
            self.assertIn('warnings differ (1 vs 1), first at #0', out)
            self.assertIn(self.GUID_A, out)
            self.assertIn(self.GUID_B, out)
            write(self.b, 'A2_1', result('A2', 1, ok=False, warnings=[form % self.GUID_A]))
            code, out, _ = run_main([self.a, self.b])
            self.assertEqual(0, code, form + ': ' + out)
            self.assertIn('3 pairs, 0 mismatched', out)

    def test_text_in_place_of_the_guid_exit_1(self):
        # Nothing is written in place of a GUID before the warnings are compared, so no text in a
        # warning can pass for the GUID of a bone.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s'):
            for text in ('<guid>', '(1,)', '[1]', ''):
                for bones in (True, False):
                    code, out, _ = self.warning_pair([form % self.GUID_A], [form % text], bones=bones)
                    self.assertEqual(1, code, form + ' ' + text + ': ' + out)
                    self.assertIn('warnings differ (1 vs 1), first at #0', out)

    def test_guid_that_changes_places_with_other_text_exit_1(self):
        # A bone can be named "Hair$<guid>" or anything else. With the two names in the other
        # order, the same node stands for the GUID in both warnings, but at another place.
        names = ['Hair$' + self.GUID_A, 'Hair$<guid>']
        for first, second in (('Parts kept where they are: Hair$%s, Hair$<guid>',
                               'Parts kept where they are: Hair$<guid>, Hair$%s'),
                              ('Hair$%s: Collides with Hair$<guid>', 'Hair$<guid>: Collides with Hair$%s')):
            for form_a, form_b in ((first, second), (second, first)):
                code, out, _ = self.node_pair(names, names, form_a % self.GUID_A, form_b % self.GUID_A)
                self.assertEqual(1, code, form_a + ': ' + out)
                self.assertIn('warnings differ (1 vs 1), first at #0', out)
                self.assertEqual(2, out.count('(nodes [1])'), out)
            code, out, _ = self.node_pair(names, names, first % self.GUID_A, first % self.GUID_A)
            self.assertEqual(0, code, first + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)

    def test_warning_parts_cut_the_warning_at_the_guids_of_merged_bones(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        doc = {'nodes': [{'name': 'Hips'}, {'name': 'Skirt.L$' + guids[0]}, {'name': 'Tail$' + guids[1]},
                         {'name': 'Hair$' + guids[2]}, {'name': 'Hair$' + guids[2] + '-VRVlog Spring Endpoint 2'},
                         {'name': 'Hair$ConstraintRef ' + guids[2]}, {'name': 'Hair$' + guids[2] + 'x'}, {}, 'no node']}
        bones = compare.merged_bones(doc)
        self.assertEqual({guids[0]: [('Skirt.L$', 1)], guids[1]: [('Tail$', 2)], guids[2]: [('Hair$', 3), ('Hair$', 4)]}, bones)
        for form, texts in (('Skirt.L$%s, Tail$%s: Collide with Hair$%s', ('Skirt.L$', ', Tail$', ': Collide with Hair$', '')),
                            ('Skirt.L$%s: Collides with Tail$%s, Hair$%s', ('Skirt.L$', ': Collides with Tail$', ', Hair$', '')),
                            ('Parts kept where they are: Skirt.L$%s, Tail$%s, Hair$%s.',
                             ('Parts kept where they are: Skirt.L$', ', Tail$', ', Hair$', '.'))):
            self.assertEqual([texts[0], (1,), texts[1], (2,), texts[2], (3, 4), texts[3]],
                             compare.warning_parts(form % tuple(guids[:3]), bones), form)
        # A GUID that is not behind the name of its bone, and one that no bone has, stay in the text.
        text = 'Tail$%s: no reference to %s, Skirt.L$%s or Hair$%s' % (guids[1], guids[2], guids[2], guids[3])
        self.assertEqual(['Tail$', (2,), ': no reference to %s, Skirt.L$%s or Hair$%s' % (guids[2], guids[2], guids[3])],
                         compare.warning_parts(text, bones))
        self.assertEqual([text], compare.warning_parts(text, {}))
        self.assertEqual(['Shape A was dropped'], compare.warning_parts('Shape A was dropped', bones))
        # An entry that is not text is compared as it is.
        self.assertEqual([{'text': text}], compare.warning_parts({'text': text}, bones))
        self.assertEqual({}, compare.merged_bones({}))

    def test_guid_of_a_node_that_the_warning_does_not_name_is_compared(self):
        # A GUID belongs to a bone only when a node has the name that the warning has in front of
        # the GUID, with the GUID at its end. A node that has the GUID elsewhere in its name, has
        # another name in front of it, or has "ConstraintRef " in front of it is not that bone.
        both = (self.GUID_A, self.GUID_B)
        for names in (['Archive$%s$%s' % both], ['Archive$%s$%s' % both, 'Archive$%s$%s' % both[::-1]],
                      ['Tail$%s' % self.GUID_A, 'Tail$%s' % self.GUID_B, 'Pair %s %s' % both],
                      ['asset$ConstraintRef %s' % self.GUID_A, 'asset$ConstraintRef %s' % self.GUID_B],
                      ['asset$%sx' % self.GUID_A, 'asset$%sx' % self.GUID_B],
                      ['Merged$%s' % self.GUID_A, 'Merged$%s' % self.GUID_B]):
            for form in ('Lookup failed: asset$%s', 'asset$%s: Gravity is not carried over', 'Lookup failed: asset$%s, other',
                         'Lookup failed: asset$ConstraintRef %s', 'Merged %s: done'):
                code, out, _ = self.node_pair(names, names, form % self.GUID_A, form % self.GUID_B)
                self.assertEqual(1, code, repr(names) + ' ' + form + ': ' + out)
                self.assertIn('warnings differ (1 vs 1), first at #0', out)
                self.assertNotIn('(nodes', out)
                code, out, _ = self.node_pair(names, names, form % self.GUID_A, form % self.GUID_A)
                self.assertEqual(0, code, repr(names) + ' ' + form + ': ' + out)
        # The bone itself and the end joint below it are the nodes of that bone.
        names = ['asset$%s', 'asset$%s-VRVlog Spring Endpoint 2', 'asset$ConstraintRef %s']
        code, out, _ = self.node_pair([name % self.GUID_A for name in names], [name % self.GUID_B for name in names],
                                      'Lookup failed: asset$' + self.GUID_A, 'Lookup failed: asset$' + self.GUID_B)
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)

    def test_guid_behind_a_longer_name_is_compared_by_its_node(self):
        # Where a name starts in a warning cannot be told from its text, so "asset-Hair$<GUID>" has
        # the name of the node "Hair$<GUID>" in front of the GUID. The GUID is still that node's in
        # both outputs, or the pair differs: the build steps give every bone its own GUID.
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        form = 'Lookup failed: asset-Hair$%s'
        code, out, _ = self.node_pair(['Hair$' + guids[0]], ['Hair$' + guids[1]], form % guids[0], form % guids[1])
        self.assertEqual(0, code, out)
        # The GUID of another node.
        code, out, _ = self.node_pair(['Hair$' + guids[0], 'Hair$' + guids[2]], ['Hair$' + guids[1], 'Hair$' + guids[3]],
                                      form % guids[0], form % guids[3])
        self.assertEqual(1, code, out)
        self.assertIn('(nodes [1])', out)
        self.assertIn('(nodes [2])', out)
        # A GUID that no node of that output has.
        for names_b, guid_b in ((['Hair$' + guids[0]], guids[1]), (['Hair$' + guids[1]], guids[0]), (['Hair$' + guids[1]], guids[3])):
            code, out, _ = self.node_pair(['Hair$' + guids[0]], names_b, form % guids[0], form % guid_b)
            self.assertEqual(1, code, repr(names_b) + ' ' + guid_b + ': ' + out)
            self.assertIn('warnings differ (1 vs 1), first at #0', out)

    def clip_pair(self, name_a, name_b, extension='VRVLOG_avatar_animator', key='clips', duration_b=1.0, runs=False):
        """Two exports with a clip of each name: A1_1 of two directories (two launches of the editor),
        or with runs the two runs of one directory (one launch)."""
        places = ((self.a, 1), (self.a, 2)) if runs else ((self.a, 1), (self.b, 1))
        for (directory, run), name, duration in zip(places, (name_a, name_b), (1.0, duration_b)):
            doc = document()
            doc['extensions'][extension] = {'schemaVersion': 1, key: [{'name': 'Idle', 'duration': 2.0},
                                                                    {'name': name, 'duration': duration}]}
            write(directory, 'A1_%d' % run, result('A1', run), doc)
        return run_main(['--runs', self.a, '1', '2'] if runs else [self.a, self.b])

    def test_generated_clip_name_difference_is_ignored(self):
        made_a = 'Made__0123456789abcdef0123456789abcdef__' + self.GUID_A + '_13'
        made_b = 'Made__fedcba9876543210fedcba9876543210__' + self.GUID_B + '_21'
        path = '/extensions/VRVLOG_avatar_animator/clips/1/'
        code, out, _ = self.clip_pair(made_a, made_b)
        self.assertEqual(0, code, out)
        self.assertIn('2 pairs, 0 mismatched', out)
        for name_b in ('Other' + made_b[4:], made_b + 'x', made_b[:-3], made_b.replace('__f', '_f'), made_b.replace(self.GUID_B, '1'),
                       'Made__fedcba98__' + self.GUID_B + '_21', 'Made'):
            code, out, _ = self.clip_pair(made_a, name_b)
            self.assertEqual(1, code, name_b + ': ' + out)
            self.assertIn(path + 'name', out)
        # The clip under a generated name is still compared.
        code, out, _ = self.clip_pair(made_a, made_b, duration_b=1.5)
        self.assertEqual(1, code, out)
        self.assertIn(path + 'duration', out)
        # Only the names of the clips of this extension take the form.
        for extension, key in (('VRVLOG_avatar_animator', 'layers'), ('VRVLOG_other', 'clips')):
            code, out, _ = self.clip_pair(made_a, made_b, extension=extension, key=key)
            self.assertEqual(1, code, extension + ' ' + key + ': ' + out)
            self.assertIn('/extensions/%s/%s/1/name' % (extension, key), out)

    def test_generated_clip_name_is_compared_between_two_runs_of_one_launch(self):
        # The names change from one launch of the editor to the next. --runs compares two runs of
        # one launch, where they stay the same, so a difference there is reported.
        made_a = 'Made__0123456789abcdef0123456789abcdef__' + self.GUID_A + '_13'
        for made_b in (made_a[:-2] + '21', 'Made__fedcba9876543210fedcba9876543210__' + self.GUID_B + '_21'):
            code, out, _ = self.clip_pair(made_a, made_b, runs=True)
            self.assertEqual(1, code, made_b + ': ' + out)
            self.assertIn('/extensions/VRVLOG_avatar_animator/clips/1/name', out)
            self.assertIn('1 pairs, 1 mismatched', out)
        code, out, _ = self.clip_pair(made_a, made_a, runs=True)
        self.assertEqual(0, code, out)
        self.assertIn('1 pairs, 0 mismatched', out)
        # A name that changes on every export is masked between two runs as well.
        for run, guid in ((1, self.GUID_A), (2, self.GUID_B)):
            doc = document()
            doc['nodes'].append({'name': 'Skirt.L$' + guid})
            write(self.a, 'A1_%d' % run, result('A1', run), doc)
        code, out, _ = run_main(['--runs', self.a, '1', '2'])
        self.assertEqual(0, code, out)
        self.assertIn('1 pairs, 0 mismatched', out)

    def test_value_that_already_is_the_masked_text_exit_1(self):
        # A rule replaces the part that changes with a fixed text. A value that already is that
        # text has no such part, so it is not the same as a value that had one.
        made = 'Made__0123456789abcdef0123456789abcdef__' + self.GUID_A + '_13'
        pairs = ((self.emission_image_pair, 'Body_Main__VRVlog_Emission_' + '0123456789abcdef' * 2,
                  'Body_Main__VRVlog_Emission_<guid>', '/images/1/name'),
                 (self.merged_bone_pair, 'Skirt.L$' + self.GUID_A, 'Skirt.L$<guid>', '/nodes/1/name'),
                 (self.merged_bone_pair, 'Hand$ConstraintRef ' + self.GUID_A, 'Hand$ConstraintRef <guid>', '/nodes/1/name'),
                 (self.spring_pair, 'Skirt.L$' + self.GUID_A, 'Skirt.L$<guid>', '/extensions/VRMC_springBone/springs/1/name'),
                 (self.clip_pair, made, 'Made__<generated>', '/extensions/VRVLOG_avatar_animator/clips/1/name'))
        for pair, name_a, name_b, path in pairs:
            for names in ((name_a, name_b), (name_b, name_a)):
                code, out, _ = pair(*names)
                self.assertEqual(1, code, path + ': ' + out)
                self.assertIn('VOLATILE_SUB masked a value in only one of the two at 1 paths: ' + path, out)
            # The same text in both is the same value.
            code, out, _ = pair(name_b, name_b)
            self.assertEqual(0, code, path + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)

    def test_differing_warning_reports_the_unmasked_text(self):
        text = ': Gravity is not carried over'
        code, out, _ = self.warning_pair(['Skirt.L$' + self.GUID_A + text, 'Shape A was dropped'],
                                         ['Skirt.L$' + self.GUID_B + text, 'Shape B was dropped'])
        self.assertEqual(1, code, out)
        self.assertIn('first at #1', out)
        self.assertIn('Shape B was dropped', out)
        code, out, _ = self.warning_pair(['Skirt.L$' + self.GUID_A + text], ['Skirt.R$' + self.GUID_B + text])
        self.assertIn(self.GUID_A, out)
        self.assertIn(self.GUID_B, out)

    def run_script(self, argv, encoding):
        env = dict(os.environ, PYTHONIOENCODING=encoding, PYTHONUTF8='0')
        return subprocess.run([sys.executable, str(Path(__file__).with_name('compare-exports.py'))] + [str(a) for a in argv],
                              capture_output=True, env=env)

    def test_text_the_console_cannot_encode_does_not_stop_the_report(self):
        # U+2014 is in cp932 on Windows only as a different character and not at all in ascii.
        write(self.b, 'A1_1', result('A1', 1, warnings=('Stage left out — é failed',)), document())
        done = self.run_script([self.a, self.b], 'ascii')
        self.assertEqual(1, done.returncode, done.stderr)
        self.assertEqual(b'', done.stderr)
        self.assertIn(b'2 pairs, 1 mismatched', done.stdout)
        self.assertIn(b'\\u2014', done.stdout)
        done = self.run_script([self.a, self.b, '--json'], 'ascii')
        self.assertEqual(1, done.returncode, done.stderr)
        report = json.loads(done.stdout.decode('ascii'))
        self.assertEqual(1, report['mismatched'])
        self.assertIn('— é', report['rows'][0]['issues'][0])
        done = self.run_script([self.a, self.b, '--json'], 'utf-8')
        self.assertIn('— é'.encode('utf-8'), done.stdout)

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

    def test_runs_mode_with_allowed_extension_exit_2(self):
        write(self.a, 'A1_2', result('A1', 2), with_added(document()))
        code, out, err = run_main(['--runs', self.a, '1', '2', '--allow-added-extension', ADDED])
        self.assertEqual((2, ''), (code, out))
        self.assertIn('cannot be used with --runs', err)

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

    def test_name_without_vrvlog_prefix_exit_2(self):
        code, out, err = run_main([self.a, self.b, '--allow-added-extension', 'VRMC_vrm'])
        self.assertEqual((2, ''), (code, out))
        self.assertIn('VRMC_vrm', err)

    def test_summary_counts_pairs_with_removed_extension(self):
        write(self.a, 'A002_1', result('A002', 1), document())
        write(self.b, 'A002_1', result('A002', 1), document())
        code, out, _ = self.run_allowed()
        self.assertEqual(0, code, out)
        self.assertEqual(['2 pairs, 0 mismatched', 'allowed extension removed in 1 of 2 pairs'],
                         out.splitlines()[-2:])
        write(self.b, 'A001_1', result('A001', 1), document())
        code, out, _ = self.run_allowed()
        self.assertEqual(0, code, out)
        self.assertEqual('allowed extension removed in 0 of 2 pairs', out.splitlines()[-1])
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual('2 pairs, 0 mismatched', out.splitlines()[-1])

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

    def test_other_extension_stays_when_a_has_no_extensions(self):
        doc_a, doc_b = document(), with_added(document())
        del doc_a['extensions']
        del doc_b['extensions']['VRMC_vrm']
        doc_b['extensions']['VRVLOG_other'] = {'schemaVersion': 1}
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        write(self.b, 'A001_1', result('A001', 1), doc_b)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/extensions', out)

    def test_other_required_app_version_stays_when_a_has_none(self):
        doc = with_added(document())
        doc['asset']['extras']['VRVLOG_requiredAppVersions']['VRVLOG_other'] = '1.0.0'
        write(self.b, 'A001_1', result('A001', 1), doc)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/asset/extras/VRVLOG_requiredAppVersions', out)

    def test_other_asset_extras_stay_when_a_has_no_extras(self):
        doc_a = document()
        del doc_a['asset']['extras']
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        code, out, _ = self.run_allowed()
        self.assertEqual(1, code, out)
        self.assertIn('/asset/extras', out)

    def test_name_twice_in_b_when_a_lists_it_without_the_extension_mismatches(self):
        doc_a, doc_b = document(), with_added(document())
        doc_a['extensionsUsed'] = [ADDED]
        doc_b['extensionsUsed'].append(ADDED)
        write(self.a, 'A001_1', result('A001', 1), doc_a)
        write(self.b, 'A001_1', result('A001', 1), doc_b)
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



def morph_glb(names, kept=(), animator=None, faces=('Smile',), changed=None):
    """A one-mesh VRM with a vec3 POSITION and one POSITION morph target per name, each its own
    accessor and bufferView in names order. faces are expressions bound to those names; changed
    gives other bytes to the target of that name. Returns (document, binary)."""
    binary, accessors, views = b'', [], []

    def add(values):
        nonlocal binary
        views.append({'buffer': 0, 'byteOffset': len(binary), 'byteLength': 12})
        accessors.append({'bufferView': len(views) - 1, 'componentType': 5126, 'count': 1, 'type': 'VEC3'})
        binary += struct.pack('<3f', *values)
        return len(accessors) - 1

    position = add((0, 0, 0))
    targets = [{'POSITION': add((ord(name[0]), 2 if name == changed else 0, len(name)))} for name in names]
    doc = document()
    doc['buffers'] = [{'byteLength': len(binary)}]
    doc['accessors'], doc['bufferViews'] = accessors, views
    doc['meshes'] = [{'name': 'Face', 'weights': [0.0] * len(names), 'extras': {'targetNames': list(names)},
                      'primitives': [{'attributes': {'POSITION': position}, 'targets': targets,
                                      'extras': {'targetNames': list(names)}}]}]
    doc['extensions']['VRMC_vrm']['expressions'] = {'preset': {}, 'custom': {
        face: {'morphTargetBinds': [{'node': 0, 'index': names.index(face), 'weight': 1.0}], 'isBinary': False}
        for face in faces}}
    if animator is not None:
        doc['extensions'][compare.ANIMATOR] = animator
    return doc, binary


def animator(morphs=(), value=1.0, blend=2):
    """VRVLOG_avatar_animator with one active target and track, then a morph target and track for
    each morph index on node 0, and the omitted blendShape count."""
    targets = [{'node': 0, 'property': 'active', 'rest': True}]
    tracks = [{'target': 0, 'curves': [{'keys': [{'time': 0, 'value': value}]}]}]
    for morph in morphs:
        targets.append({'node': 0, 'property': 'morph', 'morph': morph, 'rest': 0, 'baked': 0, 'end': 100})
        tracks.append({'target': len(targets) - 1, 'curves': [{'keys': [{'time': 0, 'value': 80}]}]})
    omitted = [{'kind': 'blendShape', 'count': blend}] if blend else []
    return {'schemaVersion': 1, 'targets': targets, 'clips': [{'name': 'On', 'tracks': tracks}],
            'omitted': omitted + [{'kind': 'material', 'count': 1}]}


class AllowKeptMorphsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.a, self.b = self.root / 'a', self.root / 'b'

    def tearDown(self):
        self.temp.cleanup()

    def put(self, directory, doc_and_binary, stem='A001_1'):
        doc, binary = doc_and_binary
        directory.mkdir(parents=True, exist_ok=True)
        (directory / (stem + '.json')).write_text(json.dumps(result(stem.split('_')[0], 1)), encoding='utf-8')
        (directory / (stem + '.vrm')).write_bytes(pack_glb(doc, binary))

    def warn(self, directory, *warnings, stem='A001_1'):
        """Write the result of stem again with these warnings."""
        doc = dict(result(stem.split('_')[0], 1), warnings=list(warnings))
        (directory / (stem + '.json')).write_text(json.dumps(doc), encoding='utf-8')

    def compare_kept(self, *extra):
        code, out, _ = run_main([self.a, self.b, '--allow-kept-morphs', '--json'] + list(extra))
        return code, json.loads(out)['rows'][0]

    def base(self):
        # A: the export before, which left out "Kept" and counted its curve.
        self.put(self.a, morph_glb(['Smile', 'Wink'], animator=animator()))

    def test_a_target_kept_for_the_extension_is_removed_and_the_rest_matches(self):
        self.base()
        # B keeps "Kept" in front of the others, so every bind, accessor and bufferView after it moves.
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1)))
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        self.assertEqual({'removed': 1, 'targets': [0, 1], 'blendShape': [2, 1]}, row['kept_morphs'])
        code, out, _ = run_main([self.a, self.b, '--allow-kept-morphs'])
        self.assertEqual(0, code, out)
        self.assertIn('morph targets: 1 removed from B, 0 vs 1 in the extension, omitted blendShape 2 vs 1', out)
        self.assertIn('morph targets in the extension in 1 of 1 pairs', out)
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)

    def test_other_bytes_of_a_target_that_stays_are_a_difference(self):
        self.base()
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1), changed='Wink'))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('normalized JSON chunk differs' in issue for issue in row['issues']), row['issues'])

    def test_a_changed_active_track_is_a_difference(self):
        self.base()
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], value=0.0, blend=1)))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('VRVLOG_avatar_animator' in issue for issue in row['issues']), row['issues'])

    def test_a_bind_to_another_target_is_a_difference(self):
        self.base()
        # The face binds "Wink" in B: after the removal it still points at a different name than in A.
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1), faces=('Wink',)))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(row['issues'])

    def test_a_target_that_a_has_is_not_removed_and_the_bin_is_compared_as_it_is(self):
        self.base()
        # The extension names "Smile", which a face already kept: nothing to remove.
        self.put(self.b, morph_glb(['Smile', 'Wink'], animator=animator([0], blend=1)))
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        self.assertEqual({'removed': 0, 'targets': [0, 1], 'blendShape': [2, 1]}, row['kept_morphs'])
        self.put(self.b, morph_glb(['Smile', 'Wink'], animator=animator([0], blend=1), changed='Wink'))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('BIN chunk differs' in issue for issue in row['issues']), row['issues'])

    def test_a_pair_without_morph_targets_is_compared_as_before(self):
        self.base()
        self.put(self.b, morph_glb(['Smile', 'Wink'], animator=animator()))
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        self.assertNotIn('kept_morphs', row)

    def test_the_removal_leaves_a_unchanged_and_renumbers_b(self):
        doc_a, _ = morph_glb(['Smile', 'Wink'])
        doc_b, _ = morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0]))
        before = copy.deepcopy(doc_a)
        stripped, removed = compare.remove_kept_morphs(doc_a, doc_b)
        self.assertEqual((before, 1), (doc_a, removed))
        mesh = stripped['meshes'][0]
        self.assertEqual(['Smile', 'Wink'], mesh['extras']['targetNames'])
        self.assertEqual(['Smile', 'Wink'], mesh['primitives'][0]['extras']['targetNames'])
        self.assertEqual(2, len(mesh['primitives'][0]['targets']))
        self.assertEqual([0.0, 0.0], mesh['weights'])
        self.assertEqual(0, stripped['extensions']['VRMC_vrm']['expressions']['custom']['Smile']['morphTargetBinds'][0]['index'])
        self.assertEqual(3, len(doc_b['meshes'][0]['extras']['targetNames']), 'B itself is not changed')

    def test_a_mesh_whose_targets_are_all_removed_has_no_empty_lists(self):
        # A kept no target of this mesh, so it wrote no targets, weights or extras at all.
        doc, binary = morph_glb([], faces=())
        self.put(self.a, (doc, binary))
        doc, binary = morph_glb(['Kept'], animator=animator([0], blend=1), faces=())
        self.put(self.b, (doc, binary))
        mesh = doc['meshes'][0]
        stripped, removed = compare.remove_kept_morphs(morph_glb([], faces=())[0], doc)
        body = stripped['meshes'][0]
        self.assertEqual(1, removed)
        self.assertNotIn('weights', body)
        self.assertNotIn('extras', body)
        self.assertNotIn('targets', body['primitives'][0])
        self.assertNotIn('extras', body['primitives'][0])
        self.assertEqual(['Kept'], mesh['extras']['targetNames'], 'B itself is not changed')

    def test_the_mb_of_the_vertex_note_is_not_compared_when_a_target_was_removed(self):
        note = 'メッシュの頂点データを詰めてVRMを%sMB小さくしました'
        self.base()
        self.warn(self.a, note % '2.8')
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1)))
        self.warn(self.b, note % '2.9')
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        # Without a removed target the number is compared as before.
        self.put(self.b, morph_glb(['Smile', 'Wink'], animator=animator()))
        self.warn(self.b, note % '2.9')
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('warnings differ' in issue for issue in row['issues']), row['issues'])

    def test_the_unused_shape_line_is_dropped_only_when_b_kept_the_removed_targets(self):
        line = 'VR Vlog: left %d unused BlendShapes out of the VRM: %s'
        self.base()
        self.warn(self.a, line % (3, 'Body/Kept, Body/A, Body/B'))
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1)))
        self.warn(self.b, line % (2, 'Body/A, Body/B'))
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        # A count that the kept target does not explain is still a difference.
        self.warn(self.b, line % (1, 'Body/A'))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('warnings differ' in issue for issue in row['issues']), row['issues'])

    def test_the_unused_shape_line_b_no_longer_writes_is_dropped_only_when_a_left_out_the_kept_targets(self):
        line = 'VR Vlog: left %d unused BlendShapes out of the VRM: %s'
        self.base()
        self.warn(self.a, line % (1, 'Body/Kept'))
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1)))
        self.warn(self.b)
        code, row = self.compare_kept()
        self.assertEqual((0, []), (code, row['issues']))
        # B writing the line when A did not is a difference.
        self.warn(self.a)
        self.warn(self.b, line % (1, 'Body/Other'))
        code, row = self.compare_kept()
        self.assertEqual(1, code)
        self.assertTrue(any('warnings differ' in issue for issue in row['issues']), row['issues'])

    def test_the_extension_counts_are_taken_before_an_added_extension_is_removed(self):
        # A is from before the extension; B adds it and keeps one target for it.
        self.put(self.a, morph_glb(['Smile', 'Wink']))
        self.put(self.b, morph_glb(['Kept', 'Smile', 'Wink'], animator=animator([0], blend=1)))
        code, out, _ = run_main([self.a, self.b, '--allow-kept-morphs', '--json',
                                 '--allow-added-extension', compare.ANIMATOR])
        row = json.loads(out)['rows'][0]
        self.assertEqual((0, []), (code, row['issues']))
        self.assertEqual({'removed': 1, 'targets': [0, 1], 'blendShape': [0, 1]}, row['kept_morphs'])

    def test_runs_mode_rejects_the_option(self):
        self.base()
        code, out, err = run_main(['--runs', self.a, '1', '2', '--allow-kept-morphs'])
        self.assertEqual((2, ''), (code, out))
        self.assertIn('--allow-kept-morphs', err)


if __name__ == '__main__':
    unittest.main()
