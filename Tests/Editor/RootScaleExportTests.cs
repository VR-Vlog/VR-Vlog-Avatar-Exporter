using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UniGLTF;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using VRVlog.Poses;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class RootScaleExportTests
    {
        [Test]
        public void UniformRootScaleReachesSceneRootNodesAndIsRestored()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            f.Copy.transform.localScale = Vector3.one * .5f;
            var hips = f.Copy.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            Assert.That(hips.parent, Is.SameAs(f.Copy.transform));
            f.PrepareMeshesForExport();
            var locals = f.Copy.GetComponentsInChildren<Transform>(true).ToDictionary(t => t, t => (t.localPosition, t.localRotation, t.localScale));
            var bytes = Vrm10AppearanceExporter.Export(new GltfExportSettings(), f.Copy, new BuiltInVrm10MaterialExporter(),
                new MobileTextureSerializer(null), new VRM10ObjectMeta { Name = "Root scale fixture", Version = "1", Authors = new List<string> { "Tests" } });
            var json = GlbDocument.Read(bytes).Json;
            var nodes = ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().ToList();
            var node = nodes.Single(n => (string)n["name"] == "Hips");
            var roots = (List<object>)((Dictionary<string, object>)((List<object>)json["scenes"])[0])["nodes"];
            Assert.That(roots.Select(value => Convert.ToInt32(value)), Does.Contain(nodes.IndexOf(node)), "Hips is a glTF scene root.");
            Assert.That(Numbers(node, "translation"), Is.EqualTo(new[] { 0, .5, 0 }).Within(1e-6));
            Assert.That(Numbers(node, "scale"), Is.EqualTo(new[] { .5, .5, .5 }).Within(1e-6));
            Assert.That(f.Copy.transform.localScale, Is.EqualTo(Vector3.one * .5f));
            foreach (var pair in locals) Assert.That((pair.Key.localPosition, pair.Key.localRotation, pair.Key.localScale), Is.EqualTo(pair.Value), pair.Key.name);
        }

        [Test]
        public void PoseAndAnimationHipsOffsetsAreExportedInTheScaledModelSpace()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var animator = f.Source.GetComponent<Animator>();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips); var hipY = hips.localPosition.y;
            var arm = AnimationUtility.CalculateTransformPath(animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), f.Source.transform);
            var hip = EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(hips, f.Source.transform), typeof(Transform), "m_LocalPosition.y");
            var still = new AnimationClip { name = "Lowered" }; var moving = new AnimationClip { name = "Lowering" };
            try
            {
                AnimationUtility.SetEditorCurve(still, EditorCurveBinding.FloatCurve(arm, typeof(Transform), "localEulerAnglesRaw.z"), AnimationCurve.Constant(0, 1, 40));
                AnimationUtility.SetEditorCurve(still, hip, AnimationCurve.Constant(0, 1, hipY - .2f));
                AnimationUtility.SetEditorCurve(moving, EditorCurveBinding.FloatCurve(arm, typeof(Transform), "localEulerAnglesRaw.z"), AnimationCurve.Linear(0, 0, 1, 60));
                AnimationUtility.SetEditorCurve(moving, hip, AnimationCurve.Linear(0, hipY, 1, hipY - .4f));
                var options = new PoseExportOptions();
                options.Manual.Add(new ManualPose { Clip = still, Name = "Lowered" });
                options.Manual.Add(new ManualPose { Clip = moving, Name = "Lowering" });
                using var session = new PoseExportSession(f.Source, options); session.CollectPrepared(f.Copy);
                Assert.That(session.Entries.Select(row => row.Error), Is.All.Null);
                var pose = session.Selected().Single(); var animation = session.SelectedAnimations().Single();
                var sampledPose = pose.HipsOffset.ToArray();
                var sampledFrames = animation.Frames.Select(frame => frame.HipsOffset.ToArray()).ToArray();
                Assert.That(sampledPose[1], Is.EqualTo(-.2).Within(.001), "Sampling stays root-scale free.");
                Assert.That(sampledFrames.Last()[1], Is.EqualTo(-.4).Within(.001));
                f.PrepareMeshesForExport();
                f.Copy.transform.localScale = Vector3.one * .5f;
                var bytes = Vrm10AppearanceExporter.Export(new GltfExportSettings(), f.Copy, new BuiltInVrm10MaterialExporter(),
                    new MobileTextureSerializer(null), new VRM10ObjectMeta { Name = "Root scale pose fixture", Authors = new List<string> { "Tests" } },
                    afterExport: session.Bind);
                bytes = session.Inject(bytes, f.Copy.transform.localScale);
                var extensions = (Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var exportedPose = HumanoidPoseData.Read(extensions[HumanoidPoseData.Extension]).Single();
                Assert.That(exportedPose.HipsOffset, Is.EqualTo(sampledPose.Select(value => value * .5)).Within(1e-6));
                var exportedFrames = HumanoidAnimationData.Read(extensions[HumanoidAnimationData.Extension]).Single().Frames;
                Assert.That(exportedFrames.Count, Is.EqualTo(sampledFrames.Length));
                for (var i = 0; i < sampledFrames.Length; i++)
                    Assert.That(exportedFrames[i].HipsOffset, Is.EqualTo(sampledFrames[i].Select(value => value * .5)).Within(1e-6), "frame " + i);
                Assert.That(pose.HipsOffset, Is.EqualTo(sampledPose), "Stored pose data stays root-scale free.");
                Assert.That(animation.Frames.Select(frame => frame.HipsOffset), Is.EqualTo(sampledFrames));
            }
            finally { Object.DestroyImmediate(still); Object.DestroyImmediate(moving); }
        }

        [Test]
        public void AxisAlignedNonUniformScaleKeepsWorldMatricesAndRestores()
        {
            var root = new GameObject("Scaled root");
            try
            {
                root.transform.localScale = new Vector3(1, 2, 3);
                var child = new GameObject("Turned").transform; child.SetParent(root.transform, false);
                child.localPosition = new Vector3(.1f, .2f, .3f); child.localRotation = Quaternion.Euler(0, 90, 0);
                child.localScale = new Vector3(1, .5f, 2);
                var leaf = new GameObject("Leaf").transform; leaf.SetParent(child, false); leaf.localPosition = new Vector3(.4f, .5f, .6f);
                var world = new[] { child.localToWorldMatrix, leaf.localToWorldMatrix };
                var position = child.localPosition; var scale = child.localScale;
                using (RootScaleBake.Apply(root))
                {
                    Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
                    AssertMatrix(world[0], child.localToWorldMatrix);
                    AssertMatrix(world[1], leaf.localToWorldMatrix);
                }
                Assert.That(root.transform.localScale, Is.EqualTo(new Vector3(1, 2, 3)));
                Assert.That(child.localPosition, Is.EqualTo(position));
                Assert.That(child.localScale, Is.EqualTo(scale));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase("rotated")]
        [TestCase("zero")]
        public void UnrepresentableRootScaleThrowsWithoutChangingTheHierarchy(string kind)
        {
            var root = new GameObject("Scaled root");
            try
            {
                root.transform.localScale = kind == "zero" ? new Vector3(1, 0, 1) : new Vector3(1, 2, 1);
                var child = new GameObject("Tilted").transform; child.SetParent(root.transform, false);
                child.localPosition = Vector3.up; child.localRotation = Quaternion.Euler(0, 0, 30);
                var error = Assert.Throws<InvalidOperationException>(() => RootScaleBake.Apply(root));
                StringAssert.Contains("Scaled root", error.Message);
                if (kind == "rotated") StringAssert.Contains("回転と不均等な拡縮", error.Message);
                Assert.That(child.localPosition, Is.EqualTo(Vector3.up));
                Assert.That(child.localScale, Is.EqualTo(Vector3.one));
                Assert.That(root.transform.localScale, Is.EqualTo(kind == "zero" ? new Vector3(1, 0, 1) : new Vector3(1, 2, 1)));
            }
            finally { Object.DestroyImmediate(root); }
        }

        static double[] Numbers(Dictionary<string, object> node, string key) =>
            ((List<object>)node[key]).Select(value => Convert.ToDouble(value)).ToArray();

        static void AssertMatrix(Matrix4x4 expected, Matrix4x4 actual)
        {
            for (var i = 0; i < 16; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-5f), "element " + i);
        }
    }
}
