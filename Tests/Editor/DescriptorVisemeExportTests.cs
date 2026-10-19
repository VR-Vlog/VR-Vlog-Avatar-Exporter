using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniVRM10;
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
            var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")); owned.Add(material);
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
            var zeros = new Vector3[fixture.Mesh.vertexCount];
            foreach (var name in shapes) fixture.Mesh.AddBlendShapeFrame(name, 100, zeros, zeros, zeros);
            Descriptor(fixture.Source, "VisemeBlendShape", fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(), visemes);
            return fixture;
        }

        [TestCase("lip")][TestCase("kana")]
        public void CaptureReadsBlendShapeVisemesWhateverTheirNames(string kind)
        {
            var names = kind == "lip" ? LipNames : KanaNames;
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var face = new GameObject("face"); face.transform.SetParent(avatar.transform, false);
            var skin = face.AddComponent<SkinnedMeshRenderer>();
            Descriptor(avatar, "VisemeBlendShape", skin, names);
            var visemes = VrChatVisemeExport.Capture(avatar, go => go == face ? 7 : -1);
            Assert.That(visemes.Node, Is.EqualTo(7));
            var expected = VrmExpressionBindings.DescriptorVisemes.Slots.Select((slot, i) => (slot.Preset, names[i]));
            Assert.That(visemes.Shapes.Select(p => (p.Key, p.Value)), Is.EquivalentTo(expected));
        }

        [TestCase("Default")][TestCase("JawFlapBlendShape")][TestCase("VisemeParameterOnly")]
        public void CaptureIgnoresOtherLipSyncModes(string lipSync)
        {
            var avatar = new GameObject("avatar"); owned.Add(avatar);
            var face = new GameObject("face"); face.transform.SetParent(avatar.transform, false);
            Descriptor(avatar, lipSync, face.AddComponent<SkinnedMeshRenderer>(), LipNames);
            Assert.That(VrChatVisemeExport.Capture(avatar, _ => 0), Is.Null);
        }

        [TestCase("lip")][TestCase("kana")]
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
    }
}
