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
            for directory, first, second, target in ((self.a, guids[0], guids[1], guids[0]),
                                                     (self.b, guids[2], guids[3], target_b)):
                doc = document()
                doc['nodes'] = [{'name': 'Hips'}, {'name': 'Hair$' + first}, {'name': 'Hair$' + second},
                                {'name': 'Hair$' + first + '-VRVlog Spring Endpoint'}]
                write(directory, 'A1_1', result('A1', 1, warnings=['Hair$' + target + text]), doc)
            return run_main([self.a, self.b])

        code, out, _ = pair(guids[2])
        self.assertEqual(0, code, out)
        code, out, _ = pair(guids[3])
        self.assertEqual(1, code, out)
        self.assertIn('warnings are about different nodes, first at #0', out)
        self.assertIn('(nodes [1, 3])', out)
        self.assertIn('(nodes [2])', out)

    def test_guid_elsewhere_in_a_warning_exit_1(self):
        for form in ('Skirt.L$%s was merged', 'Merged %s: done', 'Skirt.L$ConstraintRef %s: done', 'Skirt.L$%sx: done',
                     'Head: no reference to Skirt.L$%s: skipped',
                     # In a list, the GUID has to be followed by ", " or end the warning.
                     'Kept: Skirt.L$%sx, Tail', 'Kept: Skirt.L$%s,Tail', 'Kept: Skirt.L$%s and Tail', 'Kept: Tail, Skirt.L$%sx',
                     'Kept: Tail, Skirt.L$ConstraintRef %s', 'Kept: Tail, %s'):
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_B])
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

        def pair(listed_b):
            for directory, first, second, tail, listed in ((self.a, guids[0], guids[1], guids[2], guids[0]),
                                                           (self.b, guids[3], guids[4], guids[5], listed_b)):
                doc = document()
                doc['nodes'] = [{'name': 'Hips'}, {'name': 'Hair$' + first}, {'name': 'Hair$' + second},
                                {'name': 'Tail$' + tail}]
                warning = 'Parts kept where they are: Tail$%s, Hair$%s' % (tail, listed)
                write(directory, 'A1_1', result('A1', 1, warnings=[warning]), doc)
            return run_main([self.a, self.b])

        code, out, _ = pair(guids[3])
        self.assertEqual(0, code, out)
        code, out, _ = pair(guids[4])
        self.assertEqual(1, code, out)
        self.assertIn('warnings are about different nodes, first at #0', out)
        self.assertIn('(nodes [3] / [1])', out)
        self.assertIn('(nodes [3] / [2])', out)

    def test_masked_guid_that_no_node_has_is_compared(self):
        # Only the GUID in the name of an exported bone is known to change on every export, so a
        # GUID that sits where a bone's name would end but is in no node's name has to be the same.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s',
                     'Lookup failed: asset$%s', 'Lookup failed: asset$%s, other'):
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_B], bones=False)
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('warnings are about different nodes, first at #0', out)
            self.assertIn('(no node has ' + self.GUID_A + ')', out)
            self.assertIn('(no node has ' + self.GUID_B + ')', out)
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % self.GUID_A], bones=False)
            self.assertEqual(0, code, form + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)

    def test_masked_guid_that_only_one_output_has_exit_1(self):
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 5)]
        for directory, tail, skirt, kept in ((self.a, guids[0], guids[1], True), (self.b, guids[2], guids[3], False)):
            doc = document()
            doc['nodes'] += [{'name': 'Tail$' + tail}] + ([{'name': 'Skirt.L$' + skirt}] if kept else [])
            warning = 'Parts kept where they are: Tail$%s, Skirt.L$%s' % (tail, skirt)
            write(directory, 'A1_1', result('A1', 1, warnings=[warning]), doc)
        code, out, _ = run_main([self.a, self.b])
        self.assertEqual(1, code, out)
        self.assertIn('warnings are about different nodes, first at #0', out)
        self.assertIn('(nodes [1] / [2])', out)
        self.assertIn('(nodes [1] / no node has ' + guids[3] + ')', out)

    def test_pair_that_failed_to_export_compares_masked_guids(self):
        # Without an output there is no node name to find the GUID in, so it has to be the same.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s'):
            for directory, guid in ((self.a, self.GUID_A), (self.b, self.GUID_B)):
                write(directory, 'A2_1', result('A2', 1, ok=False, warnings=[form % guid]))
            code, out, _ = run_main([self.a, self.b])
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('3 pairs, 1 mismatched', out)
            self.assertIn('warnings are about different nodes, first at #0', out)
            self.assertIn('(no node has ' + self.GUID_A + ')', out)
            self.assertIn('(no node has ' + self.GUID_B + ')', out)
            write(self.b, 'A2_1', result('A2', 1, ok=False, warnings=[form % self.GUID_A]))
            code, out, _ = run_main([self.a, self.b])
            self.assertEqual(0, code, form + ': ' + out)
            self.assertIn('3 pairs, 0 mismatched', out)

    def test_warning_that_already_is_the_masked_text_exit_1(self):
        # "$<guid>" is what the mask leaves; a warning that already has it names no bone.
        for form in ('Skirt.L$%s: Gravity is not carried over', 'Parts kept where they are: Tail, Skirt.L$%s'):
            code, out, _ = self.warning_pair([form % self.GUID_A], [form % '<guid>'], bones=False)
            self.assertEqual(1, code, form + ': ' + out)
            self.assertIn('warnings are about different nodes, first at #0', out)
            self.assertIn('(no node has ' + self.GUID_A + ')', out)
            self.assertIn('(nodes None)', out)

    def test_masked_guid_that_changes_places_with_text_like_the_mask_exit_1(self):
        # A bone can be named "Hair$<guid>", which is also what the mask leaves of "Hair$" and a
        # GUID. With the two names in the other order, the warning is the same text after masking
        # and its masked GUID is in the name of the same node; only the place of the masked GUID
        # tells the two warnings apart.
        def pair(warning_a, warning_b):
            for directory, warning in ((self.a, warning_a), (self.b, warning_b)):
                doc = document()
                doc['nodes'] = [{'name': 'Hips'}, {'name': 'Hair$' + self.GUID_A}, {'name': 'Hair$<guid>'}]
                write(directory, 'A1_1', result('A1', 1, warnings=[warning]), doc)
            return run_main([self.a, self.b])

        for first, second, places in (('Parts kept where they are: Hair$%s, Hair$<guid>',
                                       'Parts kept where they are: Hair$<guid>, Hair$%s', ('(at [38])', '(at [51])')),
                                      ('Hair$%s: Collides with Hair$<guid>',
                                       'Hair$<guid>: Collides with Hair$%s', ('(at [11])', '(at [38])'))):
            for form_a, form_b in ((first, second), (second, first)):
                code, out, _ = pair(form_a % self.GUID_A, form_b % self.GUID_A)
                self.assertEqual(1, code, form_a + ': ' + out)
                self.assertIn('warnings have the masked GUIDs at different places, first at #0', out)
                for place in places:
                    self.assertIn(place, out)
            code, out, _ = pair(first % self.GUID_A, first % self.GUID_A)
            self.assertEqual(0, code, first + ': ' + out)
            self.assertIn('2 pairs, 0 mismatched', out)

    def test_masked_guids_are_found_at_their_places(self):
        # The GUID in front of the first ": " is masked first and the ones in front of ", " after
        # it, although one of them can come earlier in the text. Each is still found where its
        # replacement ends in the masked warning, in the order of the text.
        guids = ['%08x-89ab-cdef-0123-456789abcdef' % number for number in range(1, 4)]
        subs = compare.warning_subs()
        for form in ('Skirt.L$%s, Tail$%s: Collide with Hair$%s', 'Skirt.L$%s: Collides with Tail$%s, Hair$%s',
                     'Parts kept where they are: Skirt.L$%s, Tail$%s, Hair$%s'):
            text, spots = compare.mask_warning(form % tuple(guids), subs)
            self.assertEqual(form % (('<guid>',) * 3), text)
            self.assertEqual(guids, [one for _, one in spots], form)
            self.assertEqual([found.end() for found in re.finditer(r'\$<guid>', text)], [end for end, _ in spots], form)
            code, out, _ = self.warning_pair([form % tuple(guids)], [form % tuple(reversed(guids))])
            self.assertEqual(0, code, form + ': ' + out)
        # A GUID that is not masked, and text that already is what the mask leaves, are not listed.
        text, spots = compare.mask_warning('Skirt.L$<guid>, Tail$%s: Collide with %s' % (guids[0], guids[1]), subs)
        self.assertEqual('Skirt.L$<guid>, Tail$<guid>: Collide with ' + guids[1], text)
        self.assertEqual([(len('Skirt.L$<guid>, Tail$<guid>'), guids[0])], spots)

    def clip_pair(self, name_a, name_b, extension='VRVLOG_avatar_animator', key='clips', duration_b=1.0):
        for directory, name, duration in ((self.a, name_a, 1.0), (self.b, name_b, duration_b)):
            doc = document()
            doc['extensions'][extension] = {'schemaVersion': 1, key: [{'name': 'Idle', 'duration': 2.0},
                                                                    {'name': name, 'duration': duration}]}
            write(directory, 'A1_1', result('A1', 1), doc)
        return run_main([self.a, self.b])

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


if __name__ == '__main__':
    unittest.main()
