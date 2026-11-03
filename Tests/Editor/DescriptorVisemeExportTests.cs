using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class DescriptorVisemeExportTests
    {
        static readonly string[] LipNames = { "lip_aa", "lip_ee", "lip_ih", "lip_oh", "lip_ou" };
        static readonly string[] KanaNames = { "あ", "え", "い", "お", "う" };
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        static Type DescriptorType()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("VRChat SDK is required for the descriptor viseme test.");
            return type;
        }

        // shapes are aa, ee, ih, oh, ou (VisemeBlendShapes slots 10..14).
        static Component Descriptor(GameObject avatar, string lipSync, SkinnedMeshRenderer renderer, params string[] shapes)
        {
            var type = DescriptorType();
            var descriptor = avatar.AddComponent(type);
            var field = type.GetField("lipSync");
            field.SetValue(descriptor, Enum.Parse(field.FieldType, lipSync));
            type.GetField("VisemeSkinnedMesh").SetValue(descriptor, renderer);
            var visemes = new string[15];
            for (var i = 0; i < 10; i++) visemes[i] = "lip_other_" + i;
            Array.Copy(shapes, 0, visemes, 10, shapes.Length);
            type.GetField("VisemeBlendShapes").SetValue(descriptor, visemes);
            return descriptor;
        }

        static Dictionary<string, object> Presets(GlbDocument glb) =>
            (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)
                ((Dictionary<string, object>)glb.Json["extensions"])["VRMC_vrm"])["expressions"])["preset"];

        static string BoundShape(GlbDocument glb, Dictionary<string, object> preset)
        {
            var binds = (List<object>)preset["morphTargetBinds"];
            Assert.That(binds.Count, Is.EqualTo(1));
            var bind = (Dictionary<string, object>)binds[0];
            var node = (Dictionary<string, object>)((List<object>)glb.Json["nodes"])[Convert.ToInt32(bind["node"])];
            var mesh = (Dictionary<string, object>)((List<object>)glb.Json["meshes"])[Convert.ToInt32(node["mesh"])];
            return (string)((List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"])[Convert.ToInt32(bind["index"])];
        }

        // The export fixture with lilToon materials and the given shapes on
        // its shared mesh, and a Lip Sync descriptor on its Front renderer.
        AttachmentConnectionTests.Fixture ExportAvatar(IEnumerable<string> shapes, string[] visemes)
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Install lilToon for the full exporter entry point.");
            var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(shader); owned.Add(material);
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
            var zeros = new Vector3[fixture.Mesh.vertexCount];
            foreach (var name in shapes) fixture.Mesh.AddBlendShapeFrame(name, 100, zeros, zeros, zeros);
            Descriptor(fixture.Source, "VisemeBlendShape", fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(), visemes);
            return fixture;
        }

        [TestCase("JawFlapBlendShape")]
        public void CaptureIgnoresOtherLipSyncModes(string lipSync)
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var face = new GameObject("face"); face.transform.SetParent(avatar.transform, false);
            Descriptor(avatar, lipSync, face.AddComponent<SkinnedMeshRenderer>(), LipNames);
            Assert.That(VrChatVisemeExport.Capture(avatar, _ => 0), Is.Null);
        }

        // sil and the consonants are not exported as presets, but VRChat drives
        // them with the vowels one at a time, so the base shape reads them all.
        [Test] public void ReadListsEveryVisemeShapeOnce()
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var face = new GameObject("face"); face.transform.SetParent(avatar.transform, false);
            Descriptor(avatar, "VisemeBlendShape", face.AddComponent<SkinnedMeshRenderer>(), "lip_aa", "lip_other_0", "", "lip_oh", "lip_ou");
            var visemes = VrChatVisemeExport.Read(avatar);
            CollectionAssert.AreEqual(Enumerable.Range(0, 10).Select(i => "lip_other_" + i).Concat(new[] { "lip_aa", "lip_oh", "lip_ou" }), visemes.Exclusive);
            var copy = visemes.ForClone(avatar, avatar);
            CollectionAssert.AreEqual(visemes.Exclusive, copy.Exclusive);
        }

        // The exporter can remove the descriptor from its copy and pass the
        // captured visemes instead. Their slots still rebase the copy's face,
        // here with shapes that are not named as VRChat visemes.
        [Test] public void CapturedVisemesRebaseTheCloneWithoutItsDescriptor()
        {
            var type = DescriptorType();
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var face = new GameObject("face"); face.transform.SetParent(avatar.transform, false);
            var skin = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = BaseShapeFixture.VisemeMesh(BaseShapeFixture.MouthGroup); owned.Add(mesh);
            skin.sharedMesh = mesh;
            var weights = new[] { 100f, 30f, 0f, 0f, 50f };
            for (var i = 0; i < weights.Length; i++) skin.SetBlendShapeWeight(i, weights[i]);
            var descriptor = Descriptor(avatar, "VisemeBlendShape", skin, "mouth_aa", "mouth_E", "lip_ih", "lip_oh", "mouth_ou");
            var slots = (string[])type.GetField("VisemeBlendShapes").GetValue(descriptor);
            slots[0] = "mouth_sil";
            type.GetField("VisemeBlendShapes").SetValue(descriptor, slots);
            var captured = VrChatVisemeExport.Read(avatar);
            var clone = Object.Instantiate(avatar); owned.Add(clone);
            Object.DestroyImmediate(clone.GetComponent(type));
            Assert.That(VrChatVisemeExport.Read(clone), Is.Null);
            var visemes = captured.ForClone(avatar, clone);
            var copy = clone.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.AreSame(copy, visemes.Renderer);
            var temporary = new List<Mesh>();
            try
            {
                AvatarBaseShape.Preserve(clone, clone, temporary, null, visemeRenderer: visemes.Renderer, visemeShapes: visemes.Exclusive);
                Assert.AreNotSame(mesh, copy.sharedMesh);
                Assert.IsNull(BaseShapeFixture.RebasedAsVrChatShows(mesh, weights, BaseShapeFixture.MouthGroup, new string[0], copy.sharedMesh));
                Assert.AreSame(mesh, skin.sharedMesh);
            }
            finally { foreach (var item in temporary) Object.DestroyImmediate(item); }
        }

        [Test] public void CaptureIgnoresAnUnsetFaceMesh()
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            Descriptor(avatar, "VisemeBlendShape", null, LipNames);
            Assert.That(VrChatVisemeExport.Capture(avatar, _ => 0), Is.Null);
        }

        [TestCase("kana")]
        public void ActualExportBindsDescriptorVisemesThatAreNotKnownAliases(string kind)
        {
            var names = kind == "lip" ? LipNames : KanaNames;
            DescriptorType();
            using var fixture = ExportAvatar(names, names);
            var warnings = new List<string>();
            var glb = GlbDocument.Read(UniVrmOneClickExporter.Export(fixture.Source, "Descriptor visemes", "Tests", warnings,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }));
            var presets = Presets(glb);
            for (var i = 0; i < names.Length; i++)
                Assert.That(BoundShape(glb, (Dictionary<string, object>)presets[VrmExpressionBindings.DescriptorVisemes.Slots[i].Preset]),
                    Is.EqualTo(names[i]));
            Assert.That(warnings, Does.Not.Contain(VrmExpressionBindings.UnboundVisemeWarning));
        }

        [Test] public void ActualExportKeepsAnAuthoredEmptyMouthPreset()
        {
            DescriptorType();
            using var fixture = ExportAvatar(LipNames, LipNames);
            var vrm = ScriptableObject.CreateInstance<VRM10Object>(); owned.Add(vrm);
            var clip = ScriptableObject.CreateInstance<VRM10Expression>(); owned.Add(clip);
            clip.MorphTargetBindings = Array.Empty<MorphTargetBinding>();
            vrm.Expression.Aa = clip; fixture.Source.AddComponent<Vrm10Instance>().Vrm = vrm;
            var glb = GlbDocument.Read(UniVrmOneClickExporter.Export(fixture.Source, "Authored mouth", "Tests",
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }));
            var presets = Presets(glb);
            var aa = (Dictionary<string, object>)presets["aa"];
            Assert.That(aa.TryGetValue("morphTargetBinds", out var raw) ? ((List<object>)raw).Count : 0, Is.EqualTo(0));
            Assert.That(BoundShape(glb, (Dictionary<string, object>)presets["ou"]), Is.EqualTo("lip_ou"));
        }

        [Test] public void ActualExportWarnsWhenADeclaredVisemeIsNotInTheOutput()
        {
            DescriptorType();
            using var fixture = ExportAvatar(LipNames.Take(4), LipNames);
            var warnings = new List<string>();
            var glb = GlbDocument.Read(UniVrmOneClickExporter.Export(fixture.Source, "Missing viseme", "Tests", warnings,
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }));
            var presets = Presets(glb);
            Assert.That(presets.ContainsKey("aa") && !presets.ContainsKey("ou"), Is.True);
            Assert.That(warnings, Does.Contain(VrmExpressionBindings.UnboundVisemeWarning));
        }

        // The expression board's export binds the Lip Sync the same way, or
        // warns when it cannot.
        [TestCase(5)][TestCase(4)]
        public void BoardExportBindsDescriptorVisemes(int present)
        {
            DescriptorType();
            using var fixture = ExportAvatar(LipNames.Take(present), LipNames);
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
            var warnings = new List<string>();
            var glb = GlbDocument.Read(session.Export("Board visemes", "Tests", Array.Empty<(string, AnimationClip, float)>(), warnings,
                new BlinkExportOptions { Mode = BlinkExportMode.None }));
            var presets = Presets(glb);
            for (var i = 0; i < present; i++)
                Assert.That(BoundShape(glb, (Dictionary<string, object>)presets[VrmExpressionBindings.DescriptorVisemes.Slots[i].Preset]),
                    Is.EqualTo(LipNames[i]));
            if (present == LipNames.Length)
                Assert.That(warnings, Does.Not.Contain(VrmExpressionBindings.UnboundVisemeWarning));
            else
            {
                Assert.That(presets.ContainsKey("ou"), Is.False);
                Assert.That(warnings, Does.Contain(VrmExpressionBindings.UnboundVisemeWarning));
            }
            Assert.That(warnings, Does.Not.Contain(VrmExpressionBindings.UnexportedVisemeMeshWarning));
        }

        // The FX default state holds a mouth shape and an eye shape. A vowel at
        // 1 shows the viseme without the default mouth, as the face would with
        // that shape at 0, and keeps the default eye. The neutral face keeps
        // both. Vertices 0 and 1 are the mouth, vertex 2 the eye.
        [Test] public async Task ActualExportTakesTheDefaultMouthOffAVowel()
        {
            var type = DescriptorType();
            using var fixture = ExportAvatar(Array.Empty<string>(), LipNames);
            var mesh = fixture.Mesh;
            Vector3[] Moves(Vector3 mouth, Vector3 eye) => new[] { mouth, new Vector3(-mouth.x, mouth.y, mouth.z), eye };
            mesh.AddBlendShapeFrame("lip_aa", 100, Moves(new Vector3(0, -.02f, 0), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("lip_ee", 100, Moves(new Vector3(-.01f, 0, 0), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("lip_ih", 100, Moves(new Vector3(-.005f, -.005f, 0), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("lip_oh", 100, Moves(new Vector3(.005f, -.015f, 0), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("lip_ou", 100, Moves(new Vector3(.01f, -.005f, .005f), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("MouthDefault", 100, Moves(new Vector3(-.01f, .01f, 0), Vector3.zero), null, null);
            mesh.AddBlendShapeFrame("EyeDefault", 100, Moves(Vector3.zero, new Vector3(0, -.01f, 0)), null, null);
            Vector3 Frame(string shape, int vertex)
            {
                var moves = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex(shape), 0, moves, null, null);
                return moves[vertex];
            }
            var folderName = "__DescriptorVisemeExport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            var folder = "Assets/" + folderName;
            Vrm10Instance imported = null;
            try
            {
                var clip = new AnimationClip { name = "Idle" };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.MouthDefault"),
                    AnimationCurve.Constant(0, 1, 60));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.EyeDefault"),
                    AnimationCurve.Constant(0, 1, 40));
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                AssetDatabase.AddObjectToAsset(clip, controller);
                var state = controller.layers[0].stateMachine.AddState("Idle");
                state.writeDefaultValues = false; state.motion = clip;
                controller.layers[0].stateMachine.defaultState = state;
                using (var data = new SerializedObject(fixture.Source.GetComponent(type)))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers");
                    layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0);
                    var kind = layer.FindPropertyRelative("type");
                    kind.enumValueIndex = Array.IndexOf(kind.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Vowel default mouth", "Tests", new List<string>(),
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                var front = imported.GetComponentsInChildren<SkinnedMeshRenderer>().Single(skin => skin.name == "Front");
                Vector3 Shown(int vertex)
                {
                    var result = front.sharedMesh.vertices[vertex];
                    var moves = new Vector3[front.sharedMesh.vertexCount];
                    for (var shape = 0; shape < front.sharedMesh.blendShapeCount; shape++)
                    {
                        var weight = front.GetBlendShapeWeight(shape);
                        if (weight == 0f) continue;
                        Assert.That(front.sharedMesh.GetBlendShapeFrameCount(shape), Is.EqualTo(1));
                        front.sharedMesh.GetBlendShapeFrameVertices(shape, 0, moves, null, null);
                        result += moves[vertex] * weight / front.sharedMesh.GetBlendShapeFrameWeight(shape, 0);
                    }
                    return result;
                }
                // Hair detail rests at 35 on the renderer itself and is kept throughout.
                Vector3 Expected(int vertex, float aa, float mouth) => mesh.vertices[vertex] + Frame("Hair detail", vertex) * .35f +
                    Frame("EyeDefault", vertex) * .4f + Frame("MouthDefault", vertex) * mouth + Frame("lip_aa", vertex) * aa;
                foreach (var (aa, mouth) in new[] { (0f, .6f), (1f, 0f), (.8f, .6f * .2f) })
                {
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, aa);
                    imported.Runtime.Process();
                    for (var vertex = 0; vertex < 3; vertex++)
                        Assert.That(Vector3.Distance(Expected(vertex, aa, mouth), Shown(vertex)), Is.LessThan(.00001f),
                            $"aa at {aa}, vertex {vertex}: expected {Expected(vertex, aa, mouth):F5}, shown {Shown(vertex):F5}");
                }
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
