using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class NdmfBlinkPreparationTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject source, clone;

        [TearDown]
        public void TearDown()
        {
            if (clone != null) Object.DestroyImmediate(clone);
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }

        static Type InstalledType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        void AddHumanoidAnimator(GameObject root)
        {
            var human = new List<HumanBone>();
            Transform Bone(HumanBodyBones kind, Transform parent, Vector3 position)
            {
                var bone = new GameObject(kind.ToString()).transform; bone.SetParent(parent, false); bone.position = position;
                human.Add(new HumanBone { boneName = bone.name, humanName = HumanTrait.BoneName[(int)kind], limit = new HumanLimit { useDefaultValues = true } });
                return bone;
            }
            var hips = Bone(HumanBodyBones.Hips, root.transform, new Vector3(0, 1, 0));
            var spine = Bone(HumanBodyBones.Spine, hips, new Vector3(0, 1.2f, 0));
            var chest = Bone(HumanBodyBones.Chest, spine, new Vector3(0, 1.4f, 0));
            var neck = Bone(HumanBodyBones.Neck, chest, new Vector3(0, 1.6f, 0));
            Bone(HumanBodyBones.Head, neck, new Vector3(0, 1.75f, 0));
            foreach (var left in new[] { true, false })
            {
                var x = left ? -1f : 1f;
                var arm = Bone(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, chest, new Vector3(x * .25f, 1.5f, 0));
                var forearm = Bone(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, arm, new Vector3(x * .55f, 1.5f, 0));
                Bone(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, forearm, new Vector3(x * .8f, 1.5f, 0));
                var leg = Bone(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, hips, new Vector3(x * .12f, .9f, 0));
                var shin = Bone(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, leg, new Vector3(x * .12f, .45f, 0));
                Bone(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, shin, new Vector3(x * .12f, .1f, .1f));
            }
            var avatar = Own(AvatarBuilder.BuildHumanAvatar(root, new HumanDescription
            {
                human = human.ToArray(),
                skeleton = root.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f
            }));
            Assert.That(avatar.isValid && avatar.isHuman, Is.True);
            root.AddComponent<Animator>().avatar = avatar;
        }

        SkinnedMeshRenderer CreateFixture()
        {
            var mergeType = InstalledType("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            if (mergeType == null || InstalledType("nadena.dev.ndmf.BuildContext") == null)
                Assert.Ignore("Install supported AAO and NDMF for the native preview/optimization regression.");
            var initialize = mergeType.GetMethod("Initialize", new[] { typeof(int) });
            if (initialize == null || mergeType.GetProperty("MergeBlendShapes") == null)
                Assert.Ignore("This fixture requires the AAO 1.8+ merge configuration API.");
            Assert.That(ExportOptimizationMarker.AvatarOptimizerAdapterAvailable, Is.True);
            source = Own(new GameObject("blink preparation source"));
            // A real VRChat avatar has a humanoid Animator at its root; AAO's
            // animator parser reports an error without one.
            AddHumanoidAnimator(source);
            // Real VRChat avatars select NDMF's VRChat platform; AAO < 1.9
            // does not run its passes on the generic platform.
            PhysBoneSpringExportTests.AddVrChatAvatarDescriptor(source);
            var material = Own(new Material(Shader.Find("Unlit/Color")));
            SkinnedMeshRenderer Skin(string name)
            {
                var child = new GameObject(name); child.transform.SetParent(source.transform, false);
                var skin = child.AddComponent<SkinnedMeshRenderer>();
                var mesh = Own(new Mesh { name = name + " mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 }, normals = Enumerable.Repeat(Vector3.forward, 3).ToArray() });
                mesh.AddBlendShapeFrame("Blink", 100, Enumerable.Repeat(Vector3.up * .2f, 3).ToArray(), new Vector3[3], new Vector3[3]);
                skin.sharedMesh = mesh; skin.sharedMaterial = material; skin.SetBlendShapeWeight(0, 20);
                return skin;
            }
            var first = Skin("selected eyelid"); var second = Skin("other eyelid");
            var output = new GameObject("merged"); output.transform.SetParent(source.transform, false); output.AddComponent<SkinnedMeshRenderer>();
            var merge = output.AddComponent(mergeType); initialize.Invoke(merge, new object[] { 2 });
            mergeType.GetProperty("MergeBlendShapes").SetValue(merge, false);
            mergeType.GetProperty("RemoveEmptyRendererObject").SetValue(merge, true);
            var renderers = mergeType.GetProperty("SourceSkinnedMeshRenderers").GetValue(merge);
            var add = renderers.GetType().GetMethod("Add", new[] { typeof(SkinnedMeshRenderer) });
            Assert.That(add, Is.Not.Null);
            add.Invoke(renderers, new object[] { first }); add.Invoke(renderers, new object[] { second });
            return first;
        }

        static BlinkExportOptions Options(SkinnedMeshRenderer selected)
        {
            var options = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
            options.Both.Add(new BlinkShapeBinding { Renderer = selected, Shape = "Blink", Weight = 60 });
            return options;
        }

        [Test]
        public void InstalledAaoMergeKeepsTheSelectedRendererInCallbackFreeBlinkPreview()
        {
            var selected = CreateFixture();
            var window = ScriptableObject.CreateInstance<BlinkPreviewWindow>();
            try
            {
                // This is the actual preview call path, including its blink
                // selection captured before callback-free NDMF preparation.
                window.Prepare(source, Options(selected), Array.Empty<GameObject>(), new ExportGimmickOptions { AutoExclude = false });
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var copy = (GameObject)typeof(BlinkPreviewWindow).GetField("copy", flags).GetValue(window);
                var prepared = copy.transform.Find("selected eyelid").GetComponent<SkinnedMeshRenderer>();
                Assert.That(prepared != null, Is.True, "Preview must retain the captured renderer instead of running the AAO merge.");
                Assert.That(copy.transform.Find("other eyelid").GetComponent<SkinnedMeshRenderer>() != null, Is.True);
                typeof(BlinkPreviewWindow).GetField("closure", flags).SetValue(window, 1f);
                typeof(BlinkPreviewWindow).GetMethod("ApplyPose", flags).Invoke(window, null);
                Assert.That(prepared.GetBlendShapeWeight(prepared.sharedMesh.GetBlendShapeIndex("Blink")), Is.EqualTo(60));
                Assert.That(selected.GetBlendShapeWeight(0), Is.EqualTo(20));
                Assert.That(selected.sharedMesh.GetBlendShapeName(0), Is.EqualTo("Blink"));
            }
            finally { window.Cleanup(); Object.DestroyImmediate(window); }
        }

        [Test]
        public void InstalledAaoMergeStillOptimizesAndRemapsBlinkForTheExportCallback()
        {
            var selected = CreateFixture();
            using var resolved = BlinkExportSession.Resolve(source, Options(selected));
            clone = Object.Instantiate(source);
            using var blink = resolved.ForClone(source, clone);
            var capturedRenderer = blink.Slots[0].Single().Renderer;
            ExportOptimizationBindings mappings = null;
            var generatedMeshes = new List<Mesh>();
            try
            {
                using var preparation = NdmfExportPreparation.Prepare(source, clone, afterTransforming: lease =>
                {
                    blink.Bake(clone, generatedMeshes);
                    mappings = ExportOptimizationBindings.Capture(clone);
                });
                mappings.ValidateAndApply(); blink.Remap(mappings); blink.Validate(clone);
                var binding = blink.Slots[0].Single();
                Assert.That(capturedRenderer == null, Is.True, "The export callback must still run the actual AAO renderer merge.");
                Assert.That(binding.Renderer, Is.SameAs(clone.transform.Find("merged").GetComponent<SkinnedMeshRenderer>()));
                Assert.That(binding.Renderer.sharedMesh.GetBlendShapeIndex(binding.Shape), Is.GreaterThanOrEqualTo(0));
                Assert.That(binding.Weight, Is.EqualTo(100));
                Assert.That(selected != null && selected.sharedMesh != null, Is.True);
                Assert.That(selected.GetBlendShapeWeight(0), Is.EqualTo(20));
                Assert.That(selected.sharedMesh.GetBlendShapeName(0), Is.EqualTo("Blink"));
                Assert.That(source.transform.Find("other eyelid").GetComponent<SkinnedMeshRenderer>() != null, Is.True);
            }
            finally
            {
                mappings?.Dispose();
                foreach (var mesh in generatedMeshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }
    }
}
