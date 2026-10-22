using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;
using Fixture = VRVlog.LilToonExporter.Tests.AttachmentConnectionTests.Fixture;

namespace VRVlog.LilToonExporter.Tests
{
    // Loads a small exported VRM through the registered backend, the same way
    // the preview window does.
    public sealed class AppPreviewTests
    {
        const float Step = 1f / 30;

        [Test]
        public async Task AppModeShowsLilToonAndMToonModeIgnoresIt()
        {
            var bytes = ExportFixture();
            using (var load = await Load(bytes, false))
            {
                Assert.That(load.Root, Is.Not.Null, load.ErrorCode + " " + load.ErrorMessage);
                Assert.That(load.MaterialStatus, Does.StartWith("full-liltoon:"));
                Assert.That(AppPreviewWindow.MaterialSummary(load.MaterialStatus), Is.EqualTo(ExporterLocalization.T("lilToon で表示")));
            }
            using (var load = await Load(bytes, true))
            {
                Assert.That(load.Root, Is.Not.Null, load.ErrorCode + " " + load.ErrorMessage);
                Assert.That(load.MaterialStatus, Is.EqualTo("standard-mtoon"));
            }
        }

        [Test]
        public async Task ExpressionAndBlinkChangeBlendShapeWeights()
        {
            var bytes = ExportFixture();
            using var load = await Load(bytes, false);
            Assert.That(load.Root, Is.Not.Null, load.ErrorCode + " " + load.ErrorMessage);
            Assert.That(load.Expressions, Is.Not.Empty);
            var name = load.Expressions.FirstOrDefault(e => e.IndexOf("happy", StringComparison.OrdinalIgnoreCase) >= 0) ?? load.Expressions[0];

            load.Tick(Step);
            var before = Weights(load.Root);
            load.ClearExpressions();
            load.SetExpression(name, 1);
            load.Tick(Step);
            Assert.That(Changed(before, Weights(load.Root)), Is.True, "The expression " + name + " moves no blend shape.");

            load.ClearExpressions();
            load.Tick(Step);
            before = Weights(load.Root);
            load.SetEyeBlink(1, 1);
            load.Tick(Step);
            Assert.That(Changed(before, Weights(load.Root)), Is.True, "Closing both eyes moves no blend shape.");
        }

        [Test]
        public async Task TickMovesASpringJointRelativeToTheRoot()
        {
            var bytes = ExportFixture();
            using var load = await Load(bytes, false);
            Assert.That(load.Root, Is.Not.Null, load.ErrorCode + " " + load.ErrorMessage);
            if (!load.HasSpringBones) Assert.Ignore("The exported fixture has no spring bones.");
            var tip = load.Root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Authored tip");
            var root = load.Root.transform;
            load.SetSpringBonesEnabled(true);
            for (var i = 0; i < 3; i++) load.Tick(Step);
            var rest = root.InverseTransformPoint(tip.position);
            root.position += Vector3.right * .25f;
            for (var i = 0; i < 3; i++) load.Tick(Step);
            Assert.That(Vector3.Distance(rest, root.InverseTransformPoint(tip.position)), Is.GreaterThan(1e-4f));
        }

        [Test]
        public async Task ClosingTheWindowLeavesNoHiddenObjects()
        {
            var bytes = ExportFixture();
            // The first load may create objects the reader package keeps for the
            // whole session. Only the second open and close is counted.
            await OpenAndClose(bytes);
            var before = HiddenObjects();
            var window = await OpenAndClose(bytes);
            Assert.That(window == null, Is.True);
            Assert.That(HiddenObjects(), Is.EqualTo(before));
        }

        static async Task<IAppPreviewSession> Load(byte[] bytes, bool standardMToon)
        {
            Assert.That(AppPreviewEntry.Backend, Is.InstanceOf<AvatarRuntimeBackend>());
            var load = await AppPreviewEntry.Backend.LoadAsync(bytes, "app-preview.vrm", standardMToon, CancellationToken.None);
            Assert.That(load, Is.Not.Null);
            return load;
        }

        static async Task<AppPreviewWindow> OpenAndClose(byte[] bytes)
        {
            var window = AppPreviewWindow.Open(bytes, "app-preview.vrm");
            try
            {
                await window.LastLoad;
                Assert.That(window.Error, Is.Null);
                Assert.That(window.Load?.Root, Is.Not.Null);
            }
            finally { window.Close(); }
            return window;
        }

        static int HiddenObjects() => Resources.FindObjectsOfTypeAll<GameObject>()
            .Count(o => (o.hideFlags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave);

        static float[] Weights(GameObject root)
        {
            var weights = new List<float>();
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                for (var i = 0; i < (skin.sharedMesh != null ? skin.sharedMesh.blendShapeCount : 0); i++)
                    weights.Add(skin.GetBlendShapeWeight(i));
            return weights.ToArray();
        }

        static bool Changed(float[] before, float[] after) =>
            before.Length != after.Length || before.Where((weight, i) => Mathf.Abs(weight - after[i]) > .01f).Any();

        // A humanoid with lilToon materials, one authored expression, a manual
        // blink and one two-joint spring.
        static byte[] ExportFixture()
        {
            var lilToon = Shader.Find("lilToon");
            if (lilToon == null) Assert.Ignore("Install lilToon 2.3.4.");
            using var fixture = new Fixture();
            var shape = Enumerable.Repeat(new Vector3(0, .03f, 0), 3).ToArray();
            fixture.Mesh.AddBlendShapeFrame("Smile", 100, shape, new Vector3[3], new Vector3[3]);
            fixture.Mesh.AddBlendShapeFrame("Blink", 100, shape, new Vector3[3], new Vector3[3]);
            var hair = fixture.Source.transform.Find("Independent hair/Head");
            var head = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            hair.parent.SetParent(head, true);
            var authored = fixture.Source.AddComponent<Vrm10Instance>();
            var joint = hair.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var tip = Child(hair, "Authored tip", Vector3.down * .15f).gameObject.AddComponent<VRM10SpringBoneJoint>();
            authored.SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("authored") { Joints = { joint, tip } });
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var expression = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                expression.name = "Smile";
                expression.MorphTargetBindings = new[] { new MorphTargetBinding { RelativePath = "Front", Index = fixture.Mesh.GetBlendShapeIndex("Smile"), Weight = 1 } };
                settings.Expression.Happy = expression;
                authored.Vrm = settings;
                var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var skin in skins) skin.sharedMaterial.shader = lilToon;
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
                blink.Both.Add(new BlinkShapeBinding { Renderer = skins.Single(s => s.name == "Front"), Shape = "Blink" });
                return UniVrmOneClickExporter.Export(fixture.Source, "App preview", "Tests", new List<string>(),
                    exporterVersion: "app-preview-test", lilToonVersion: "2.3.4", blinkOptions: blink);
            }
            finally
            {
                Object.DestroyImmediate(expression);
                Object.DestroyImmediate(settings);
            }
        }

        static Transform Child(Transform root, string name, Vector3 position)
        {
            var value = new GameObject(name).transform;
            value.SetParent(root, false);
            value.localPosition = position;
            return value;
        }
    }
}
