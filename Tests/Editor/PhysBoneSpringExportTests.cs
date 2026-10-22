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
    public sealed class PhysBoneSpringExportTests
    {
        static Type Sdk(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
        static Component PhysBone(GameObject target)
        {
            var type = Sdk(PhysBoneSpringExport.PhysBoneType);
            if (type == null) Assert.Ignore("Real VRChat SDK integration test; SDK-free export is covered separately.");
            return target.AddComponent(type);
        }
        static Transform Child(Transform root, string name, Vector3 position)
        { var t = new GameObject(name).transform; t.SetParent(root, false); t.localPosition = position; return t; }
        static void AssertUniqueJoints(GameObject avatar)
        {
            var joints = avatar.GetComponent<Vrm10Instance>().SpringBone.Springs.SelectMany(s => s.Joints).ToArray();
            Assert.That(joints.Distinct().Count(), Is.EqualTo(joints.Length), "VRM also prohibits sharing terminal joints.");
        }
        static void References(Component component, string name, params Object[] values)
        {
            using var data = new SerializedObject(component);
            var list = data.FindProperty(name); list.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Set(Component component, string name, object value)
        {
            using var data = new SerializedObject(component);
            var p = data.FindProperty(name);
            // VRChat SDK 3.7.x has no such field and always behaves as true, the exporter's fallback.
            if (p == null && name == "ignoreOtherPhysBones") Assert.Ignore("ignoreOtherPhysBones needs VRChat SDK 3.8.0 or later.");
            Assert.That(p, Is.Not.Null, name);
            if (value is Vector3 v) p.vector3Value = v;
            else if (value is float f) p.floatValue = f;
            else if (value is bool b) p.boolValue = b;
            else if (value is int n) p.intValue = n;
            else if (value is AnimationCurve curve) p.animationCurveValue = curve;
            else if (value is Object reference) p.objectReferenceValue = reference;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        // Real VRChat avatars select NDMF's VRChat platform. Fill the playable
        // layers as the SDK inspector does for a new avatar (all default), so
        // AAO 1.7 reads its default controllers instead of a null layer.
        internal static Component AddVrChatAvatarDescriptor(GameObject root)
        {
            var type = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (type == null) Assert.Ignore("Install the real VRChat SDK.");
            var layerType = type.GetNestedType("AnimLayerType");
            Assert.That(layerType, Is.Not.Null, "The installed VRChat SDK must expose its playable layer types.");
            var descriptor = root.AddComponent(type);
            using var data = new SerializedObject(descriptor);
            void Layers(string name, params string[] types)
            {
                var list = data.FindProperty(name); Assert.That(list, Is.Not.Null, name);
                list.arraySize = types.Length;
                for (var i = 0; i < types.Length; i++)
                {
                    var layer = list.GetArrayElementAtIndex(i);
                    layer.FindPropertyRelative("type").intValue = Convert.ToInt32(Enum.Parse(layerType, types[i]));
                    layer.FindPropertyRelative("isDefault").boolValue = true;
                    layer.FindPropertyRelative("isEnabled").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = null;
                    layer.FindPropertyRelative("mask").objectReferenceValue = null;
                }
            }
            data.FindProperty("customizeAnimationLayers").boolValue = false;
            Layers("baseAnimationLayers", "Base", "Additive", "Gesture", "Action", "FX");
            Layers("specialAnimationLayers", "Sitting", "TPose", "IKPose");
            data.ApplyModifiedPropertiesWithoutUndo();
            return descriptor;
        }

        // A real avatar body is skinned to its humanoid bones. Weight one
        // triangle to each mapped bone so the body, not the test, owns them.
        static SkinnedMeshRenderer AddWeightedBody(GameObject root, Material material)
        {
            var animator = root.GetComponent<Animator>();
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(bone => animator.GetBoneTransform((HumanBodyBones)bone))
                .Where(bone => bone != null).ToArray();
            Assert.That(UniVrmOneClickExporter.RequiredHumanBones.All(bone => bones.Contains(animator.GetBoneTransform(bone))), Is.True);
            var body = new GameObject("Body").AddComponent<SkinnedMeshRenderer>();
            body.transform.SetParent(root.transform, false);
            var vertices = new List<Vector3>(); var weights = new List<BoneWeight>(); var triangles = new List<int>();
            for (var i = 0; i < bones.Length; i++)
            {
                var center = root.transform.InverseTransformPoint(bones[i].position);
                triangles.AddRange(new[] { vertices.Count, vertices.Count + 1, vertices.Count + 2 });
                vertices.AddRange(new[] { center + new Vector3(-.02f, 0, .02f), center + new Vector3(.02f, 0, .02f), center + new Vector3(0, .03f, .02f) });
                weights.AddRange(Enumerable.Repeat(new BoneWeight { boneIndex0 = i, weight0 = 1 }, 3));
            }
            var mesh = new Mesh { name = "Weighted humanoid body" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(Enumerable.Repeat(Vector3.forward, vertices.Count).ToList());
            mesh.SetUVs(0, Enumerable.Repeat(Vector2.zero, vertices.Count).ToList());
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * body.transform.localToWorldMatrix).ToArray();
            body.sharedMesh = mesh; body.bones = bones; body.rootBone = bones[0];
            body.sharedMaterials = new[] { material };
            return body;
        }

        sealed class OwnedMesh : IDisposable
        {
            internal readonly Mesh Mesh;
            internal OwnedMesh(Mesh mesh) => Mesh = mesh;
            public void Dispose() { if (Mesh != null) Object.DestroyImmediate(Mesh); }
        }

        [Test]
        public void PreparedCopyWithoutRequiredHumanBonesOrRootAnimatorStopsAndNamesThem()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            Assert.DoesNotThrow(() => UniVrmOneClickExporter.ValidatePreparedHumanoid(fixture.Source, fixture.Copy));
            var copyAnimator = fixture.Copy.GetComponent<Animator>();
            Object.DestroyImmediate(copyAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).gameObject);
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidatePreparedHumanoid(fixture.Source, fixture.Copy));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.LostRequiredHumanBones + "LeftUpperLeg, LeftLowerLeg, LeftFoot"));
            Assert.That(error.Message, Does.Contain("元のアバターは変更していません"));
            Assert.That(fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftFoot) != null, Is.True,
                "Only the disposable copy may lose bones.");
            Object.DestroyImmediate(copyAnimator);
            error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidatePreparedHumanoid(fixture.Source, fixture.Copy));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.LostHumanoidAnimator));
            Assert.That(error.Message, Does.Contain("元のアバターは変更していません"));
        }

        [Test]
        public void NonHumanoidSourceStopsBeforeExportAndNamesMissingBones()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            Assert.DoesNotThrow(() => UniVrmOneClickExporter.ValidateSourceHumanoid(fixture.Source));
            var animator = fixture.Source.GetComponent<Animator>();
            Object.DestroyImmediate(animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).gameObject);
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidateSourceHumanoid(fixture.Source));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.SourceMissingHumanBones + "LeftUpperLeg, LeftLowerLeg, LeftFoot"));
            var avatar = animator.avatar;
            animator.avatar = null;
            error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidateSourceHumanoid(fixture.Source));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.SourceNotHumanoid));
            animator.avatar = avatar;
            Object.DestroyImmediate(animator);
            error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidateSourceHumanoid(fixture.Source));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.SourceNotHumanoid));
        }

        // A humanoid whose only renderer has no MeshFilter used to be saved as a
        // VRM with no mesh. ExportSettings.Export stops it before any stage runs.
        [Test]
        public void MeshRendererWithoutMeshFilterSourceStopsBeforeExport()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(skin);
            var body = new GameObject("Body");
            body.transform.SetParent(fixture.Source.transform, false);
            body.AddComponent<MeshRenderer>();
            Assert.DoesNotThrow(() => UniVrmOneClickExporter.ValidateSourceHumanoid(fixture.Source));
            var warnings = new List<string>();
            var error = Assert.Throws<InvalidOperationException>(() => new ExportSettings().Export(fixture.Source, "Empty", "Tests", warnings, null, null));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.SourceHasNoMesh));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void NullSkinnedMeshSourceStopsBeforeExport()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Object.DestroyImmediate(skins[1]);
            skins[0].sharedMesh = null;
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidateSourceHasMesh(fixture.Source));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.SourceHasNoMesh));
        }

        [Test]
        public void MeshOnInactiveChildPassesSourceMeshCheck()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Object.DestroyImmediate(skins[1]);
            skins[0].gameObject.SetActive(false);
            Assert.DoesNotThrow(() => UniVrmOneClickExporter.ValidateSourceHasMesh(fixture.Source));
        }

        [Test]
        public void OutputWithoutMeshIsRejected()
        {
            var asset = new Dictionary<string, object> { ["version"] = "2.0" };
            var empty = GlbDocument.Create(new Dictionary<string, object> { ["asset"] = asset }, null).Write();
            var error = Assert.Throws<InvalidOperationException>(() => UniVrmOneClickExporter.ValidateOutputHasMesh(empty));
            Assert.That(error.Message, Is.EqualTo(UniVrmOneClickExporter.OutputHasNoMesh));
            var withMesh = GlbDocument.Create(new Dictionary<string, object>
                { ["asset"] = asset, ["meshes"] = new List<object> { new Dictionary<string, object>() } }, null).Write();
            Assert.DoesNotThrow(() => UniVrmOneClickExporter.ValidateOutputHasMesh(withMesh));
        }

        // A generic AAO avatar uses its root Animator controller, unlike a
        // VRChat descriptor or authored Vrm10Instance. Give the synthetic
        // generic fixture a real idle controller without altering AAO settings.
        sealed class StaticAnimatorController : IDisposable
        {
            internal readonly AnimatorController Controller;
            readonly Animator animator;
            readonly RuntimeAnimatorController previous;
            readonly AnimatorStateMachine machine;
            readonly AnimatorState idle;

            internal StaticAnimatorController(Animator target)
            {
                animator = target; previous = target.runtimeAnimatorController;
                Controller = new AnimatorController { name = "PhysBone generic fixture controller" };
                Controller.AddLayer("Base Layer");
                machine = Controller.layers[0].stateMachine;
                idle = machine.AddState("Idle"); idle.writeDefaultValues = false;
                machine.defaultState = idle;
                animator.runtimeAnimatorController = Controller;
            }

            public void Dispose()
            {
                if (animator != null) animator.runtimeAnimatorController = previous;
                Object.DestroyImmediate(idle); Object.DestroyImmediate(machine); Object.DestroyImmediate(Controller);
            }
        }

        [TestCase(0, 2)]
        [TestCase(1, 2)]
        [TestCase(2, 3)]
        public void BranchesAndVirtualEndpointsHaveOneOwnerAndKeepSource(int multi, int expectedChains)
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "Root", Vector3.zero);
                Child(root, "a", new Vector3(-.1f, -.1f, 0));
                Child(root, "b", new Vector3(.1f, -.1f, 0));
                var pb = PhysBone(root.gameObject);
                Set(pb, "multiChildType", multi); Set(pb, "endpointPosition", new Vector3(0, -.1f, 0));
                copy = Object.Instantiate(source);
                var report = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                Assert.That(report.Chains, Is.EqualTo(expectedChains));
                var springs = copy.GetComponent<Vrm10Instance>().SpringBone.Springs;
                var moving = springs.SelectMany(s => s.Joints.Take(s.Joints.Count - 1)).ToArray();
                Assert.That(moving.Distinct().Count(), Is.EqualTo(moving.Length));
                AssertUniqueJoints(copy);
                Assert.That(springs.All(s => s.Center == null), Is.True);
                Assert.That(source.GetComponentsInChildren<Transform>().Length, Is.EqualTo(4));
                Assert.That(source.GetComponentsInChildren<VRM10SpringBoneJoint>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void CurvesCollidersAndLimitsUseRealSdkSerializedValues()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "Hair", Vector3.zero);
                Child(root, "HairTip", Vector3.down * .1f);
                var pb = PhysBone(root.gameObject);
                Set(pb, "endpointPosition", Vector3.down * .1f);
                Set(pb, "pull", .4f); Set(pb, "pullCurve", AnimationCurve.Linear(0, 1, 1, .5f));
                Set(pb, "radius", .02f); Set(pb, "limitType", 1); Set(pb, "maxAngleX", 30f);
                var collider = source.AddComponent(Sdk("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider"));
                Set(collider, "shapeType", 1); Set(collider, "height", .3f); Set(collider, "radius", .05f);
                using (var data = new SerializedObject(pb))
                {
                    var list = data.FindProperty("colliders"); list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = collider; data.ApplyModifiedPropertiesWithoutUndo();
                }
                copy = Object.Instantiate(source);
                var report = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                Assert.That(report.Colliders, Is.EqualTo(1));
                var spring = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single();
                Assert.That(spring.Joints[0].m_stiffnessForce, Is.EqualTo(1.6f).Within(.001));
                Assert.That(spring.Joints[1].m_stiffnessForce, Is.LessThan(spring.Joints[0].m_stiffnessForce));
                Assert.That(spring.Joints[0].m_pitch, Is.EqualTo(30 * Mathf.Deg2Rad).Within(.001));
                var output = spring.ColliderGroups.Single().Colliders.Single();
                Assert.That(output.ColliderType, Is.EqualTo(VRM10SpringBoneColliderTypes.Capsule));
                Assert.That(Vector3.Distance(output.Offset, output.Tail), Is.EqualTo(.2f).Within(.001));
                Assert.That(output.transform.parent, Is.EqualTo(copy.transform));
                Assert.That(output.transform.localToWorldMatrix, Is.EqualTo(copy.transform.localToWorldMatrix));
                Assert.That(source.GetComponentsInChildren<VRM10SpringBoneCollider>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        const string AllMotionImmobileWarning = ": Immobile（All Motion）は揺れの強さと減衰を上げて近似しました。";
        const string WorldImmobileWarning = ": Immobile（World）はアプリの安定化（揺れの基準をモデルのルートにする）に委ねました。";

        // World is left to the app, which centres exported springs on the
        // model root; only All Motion raises the exported stiffness and drag.
        [TestCase(0, 1.9f, .78f)]
        [TestCase(1, 1.6f, .48f)]
        public void AllMotionImmobileRaisesStiffnessAndDragWhileWorldIsLeftToTheApp(int immobileType, float stiffness, float drag)
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "Hair", Vector3.zero);
                Child(root, "HairTip", Vector3.down * .1f);
                var pb = PhysBone(root.gameObject);
                Set(pb, "endpointPosition", Vector3.down * .1f);
                Set(pb, "pull", .4f); Set(pb, "spring", .2f); Set(pb, "gravity", 0f);
                Set(pb, "immobileType", immobileType); Set(pb, "immobile", .6f); Set(pb, "gravityFalloff", .5f);
                copy = Object.Instantiate(source);
                var warnings = new List<string>();
                PhysBoneSpringExport.Convert(source, copy, warnings);
                var joint = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single().Joints[0];
                Assert.That(joint.m_stiffnessForce, Is.EqualTo(stiffness).Within(.001));
                Assert.That(joint.m_dragForce, Is.EqualTo(drag).Within(.001));
                Assert.That(warnings.Contains("Hair" + AllMotionImmobileWarning), Is.EqualTo(immobileType == 0));
                Assert.That(warnings.Contains("Hair" + WorldImmobileWarning), Is.EqualTo(immobileType != 0));
                Assert.That(warnings.Contains("Hair: Gravity Falloffは重力の減衰として近似しました。"), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void AllMotionImmobileDragNeverExceedsOne()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "Hair", Vector3.zero);
                Child(root, "HairTip", Vector3.down * .1f);
                var pb = PhysBone(root.gameObject);
                Set(pb, "endpointPosition", Vector3.down * .1f);
                Set(pb, "pull", .4f); Set(pb, "spring", 0f);
                Set(pb, "immobileType", 0); Set(pb, "immobile", 1f);
                copy = Object.Instantiate(source);
                PhysBoneSpringExport.Convert(source, copy, new List<string>());
                var joint = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single().Joints[0];
                Assert.That(joint.m_stiffnessForce, Is.EqualTo(2.1f).Within(.001));
                Assert.That(joint.m_dragForce, Is.EqualTo(1f).Within(.001));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [TestCase(2, true)][TestCase(7, false)]
        public void PlaneInsideBoundsAndUnknownShapesDoNotStopConversion(int shape, bool inside)
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "Hair", Vector3.zero);
                Child(root, "HairTip", Vector3.down * .1f);
                var pb = PhysBone(root.gameObject);
                Set(pb, "endpointPosition", Vector3.down * .1f);
                var holder = Child(source.transform, "Collider_plane", Vector3.zero);
                var collider = holder.gameObject.AddComponent(Sdk("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider"));
                Set(collider, "shapeType", shape); Set(collider, "insideBounds", inside);
                References(pb, "colliders", collider);
                copy = Object.Instantiate(source);
                var warnings = new List<string>();
                var fallbacks = new ExportFallbackReport();
                var report = PhysBoneSpringExport.Convert(source, copy, warnings, fallbacks);
                var spring = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single();
                Assert.That(spring.Joints, Is.Not.Empty);
                // The same pass that warns also records what the confirmation dialog lists.
                var fallback = fallbacks.Items.Single();
                Assert.That(fallback.Kind, Is.EqualTo(shape == 2 ? ExportFallbackKind.PlaneInsideBounds : ExportFallbackKind.ColliderSkipped));
                Assert.That(fallback.Names, Is.EqualTo(new[] { "Collider_plane" }));
                Assert.That(fallback.Paths, Is.EqualTo(new[] { "Collider_plane" }));
                Assert.That(ExportFallbackConfirmation.Targets(fallbacks, source), Is.EqualTo(new[] { holder.gameObject }));
                if (shape == 2)
                {
                    Assert.That(report.Colliders, Is.EqualTo(1));
                    Assert.That(spring.ColliderGroups.Single().Colliders.Single().ColliderType, Is.EqualTo(VRM10SpringBoneColliderTypes.Plane));
                    Assert.That(warnings.Any(w => w.Contains("Inside Bounds") && w.Contains("Collider_plane")), Is.True);
                }
                else
                {
                    Assert.That(report.Colliders, Is.EqualTo(0));
                    Assert.That(spring.ColliderGroups, Is.Empty);
                    Assert.That(warnings.Any(w => w.Contains("当たり判定から外しました") && w.Contains("Collider_plane")), Is.True);
                }
                Assert.That(source.GetComponentsInChildren<VRM10SpringBoneCollider>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ExistingVrmSettingsWinAndRemovedExplicitRootsDoNotAnimateTheBody()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var hair = Child(source.transform, "Hair", Vector3.up);
                var pb = PhysBone(source); Set(pb, "rootTransform", hair);
                Set(pb, "endpointPosition", Vector3.up * .1f);
                copy = Object.Instantiate(source);
                var copiedHair = copy.transform.Find("Hair");
                var instance = copy.AddComponent<Vrm10Instance>();
                var authored = copiedHair.gameObject.AddComponent<VRM10SpringBoneJoint>(); authored.m_dragForce = .123f;
                var tip = Child(copiedHair, "Tip", Vector3.up * .1f).gameObject.AddComponent<VRM10SpringBoneJoint>();
                instance.SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("authored") { Joints = { authored, tip } });
                Assert.That(PhysBoneSpringExport.Convert(source, copy, new List<string>()).Converted, Is.Zero);
                Assert.That(authored.m_dragForce, Is.EqualTo(.123f));
                PhysBoneSpringExport.PruneRemovedRoots(copy, t => t == copiedHair, new List<string>());
                Assert.That(copy.GetComponents<Component>().Any(PhysBoneSpringExport.IsPhysBone), Is.False);
                Assert.That(source.GetComponents<Component>().Any(PhysBoneSpringExport.IsPhysBone), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void AbsentSdkAndRigidModelsRemainValidWithoutInventedSprings()
        {
            var source = new GameObject("rigid"); var copy = Object.Instantiate(source);
            try
            {
                Assert.That(PhysBoneSpringExport.Convert(source, copy, new List<string>()).Sources, Is.Zero);
                Assert.That(copy.GetComponent<Vrm10Instance>(), Is.Null);
                Assert.Throws<InvalidOperationException>(() => PhysBoneSpringExport.Convert(source, source, null));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void IgnoredSubtreesDisabledBonesAndOverlappingOwnersAreExplicit()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.zero);
                var ignored = Child(root, "ignored", Vector3.left);
                Child(ignored, "ignoredTip", Vector3.down);
                var kept = Child(root, "kept", Vector3.right);
                Child(kept, "keptTip", Vector3.down);
                var pb = PhysBone(root.gameObject);
                Set(pb, "endpointPosition", Vector3.down * .1f);
                using (var data = new SerializedObject(pb))
                {
                    var list = data.FindProperty("ignoreTransforms"); list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = ignored;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var disabled = PhysBone(Child(source.transform, "disabled", Vector3.zero).gameObject);
                ((Behaviour)disabled).enabled = false;
                copy = Object.Instantiate(source);
                var warnings = new List<string>();
                var report = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(report.Converted, Is.EqualTo(1)); Assert.That(report.Skipped, Is.EqualTo(1));
                Assert.That(copy.transform.Find("root/ignored").GetComponentsInChildren<VRM10SpringBoneJoint>(), Is.Empty);
                Assert.That(warnings.Any(w => w.Contains("無効または非表示")), Is.True);
                Object.DestroyImmediate(copy);
                var second = PhysBone(source); Set(second, "rootTransform", root);
                copy = Object.Instantiate(source);
                report = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(report.Converted, Is.EqualTo(2), "The second PB can retain the first PB's ignored branch.");
                AssertUniqueJoints(copy);
                Assert.That(warnings.Any(w => w.Contains("優先対象")), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedOwnersKeepRestShapeForcesAndCollidersRegardlessOfComponentTraversal(bool childComponentFirst)
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "parent", Vector3.up);
                root.localRotation = Quaternion.Euler(10, 20, 30); root.localScale = new Vector3(1.2f, .8f, 1.1f);
                var middle = Child(root, "middle", Vector3.down * .12f);
                var childRoot = Child(middle, "child", Vector3.down * .17f);
                Child(childRoot, "authoredEnd", Vector3.down * .21f);
                var parent = PhysBone(root.gameObject);
                var child = PhysBone(childComponentFirst ? source : childRoot.gameObject);
                Set(child, "rootTransform", childRoot);
                foreach (var pb in new[] { parent, child })
                { Set(pb, "ignoreOtherPhysBones", false); Set(pb, "endpointPosition", Vector3.down * .2f); }
                Set(parent, "pull", .4f); Set(parent, "pullCurve", AnimationCurve.Linear(0, 1, 1, .5f));
                Set(parent, "limitType", 1); Set(parent, "maxAngleX", 30f);
                Set(child, "pull", .8f); Set(child, "limitType", 1); Set(child, "maxAngleX", 65f);
                var colliderType = Sdk("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider");
                var a = Child(source.transform, "parentCollider", Vector3.left).gameObject.AddComponent(colliderType);
                var b = Child(source.transform, "childCollider", Vector3.right).gameObject.AddComponent(colliderType);
                Set(a, "radius", .025f); Set(b, "radius", .045f);
                References(parent, "colliders", a); References(child, "colliders", b);
                var sourceState = source.GetComponentsInChildren<Transform>().ToDictionary(t => t, t => t.localToWorldMatrix);
                var sourceParent = EditorJsonUtility.ToJson(parent); var sourceChild = EditorJsonUtility.ToJson(child);
                copy = Object.Instantiate(source);
                var copiedState = copy.GetComponentsInChildren<Transform>().ToDictionary(t => t, t => t.localToWorldMatrix);
                var warnings = new List<string>();
                var result = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(result.Converted, Is.EqualTo(2)); Assert.That(result.Chains, Is.EqualTo(2));
                Assert.That(result.Joints, Is.EqualTo(4)); AssertUniqueJoints(copy);
                var springs = copy.GetComponent<Vrm10Instance>().SpringBone.Springs;
                var parentSpring = springs.Single(s => s.Joints[0].name == "parent");
                var childSpring = springs.Single(s => s.Joints[0].name == "child");
                Assert.That(parentSpring.Joints.Select(j => j.name).Take(2), Is.EqualTo(new[] { "parent", "middle" }));
                var parentTail = parentSpring.Joints.Last().transform;
                var copiedChild = copy.transform.Find("parent/middle/child");
                Assert.That(parentTail, Is.Not.SameAs(copiedChild));
                Assert.That(parentTail.parent, Is.SameAs(copiedChild.parent));
                Assert.That(Vector3.Distance(parentTail.position, copiedChild.position), Is.LessThan(1e-6));
                Assert.That(Vector3.Distance(parentTail.position, parentTail.parent.position),
                    Is.EqualTo(Vector3.Distance(childRoot.position, middle.position)).Within(1e-6));
                Assert.That(parentSpring.Joints[1].m_stiffnessForce, Is.EqualTo(1.4f).Within(1e-5), "Keep original depth 1/4 after splitting.");
                Assert.That(childSpring.Joints[0].m_stiffnessForce, Is.EqualTo(3.2f).Within(1e-5));
                Assert.That(parentSpring.Joints[0].m_pitch, Is.EqualTo(30 * Mathf.Deg2Rad).Within(1e-5));
                Assert.That(childSpring.Joints[0].m_pitch, Is.EqualTo(65 * Mathf.Deg2Rad).Within(1e-5));
                Assert.That(parentSpring.ColliderGroups.Single().Colliders.Single().Radius, Is.EqualTo(.025f));
                Assert.That(childSpring.ColliderGroups.Single().Colliders.Single().Radius, Is.EqualTo(.045f));
                foreach (var pair in sourceState) Assert.That(pair.Key.localToWorldMatrix, Is.EqualTo(pair.Value));
                foreach (var pair in copiedState) Assert.That(pair.Key.localToWorldMatrix, Is.EqualTo(pair.Value));
                Assert.That(EditorJsonUtility.ToJson(parent), Is.EqualTo(sourceParent));
                Assert.That(EditorJsonUtility.ToJson(child), Is.EqualTo(sourceChild));
                Assert.That(warnings.Any(w => w.Contains("より局所")), Is.True);
                Assert.That(warnings.Any(w => w.Contains("専用末端")), Is.True);
                Assert.That(source.GetComponentsInChildren<VRM10SpringBoneJoint>(), Is.Empty);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SameRootPrefersAttachedComponentThenStableComponentOrder(bool bothAttached)
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.down);
                var remoteOrFirst = PhysBone(bothAttached ? root.gameObject : source);
                var attachedOrSecond = PhysBone(root.gameObject);
                Set(remoteOrFirst, "rootTransform", root);
                Set(remoteOrFirst, "pull", .2f); Set(attachedOrSecond, "pull", .7f);
                foreach (var pb in new[] { remoteOrFirst, attachedOrSecond }) Set(pb, "endpointPosition", Vector3.down * .1f);
                copy = Object.Instantiate(source); var warnings = new List<string>();
                var result = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(result.Converted, Is.EqualTo(1)); Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single().Joints[0].m_stiffnessForce,
                    Is.EqualTo(bothAttached ? .8f : 2.8f).Within(1e-5));
                AssertUniqueJoints(copy);
                Assert.That(warnings.Any(w => w.Contains("同じRoot")), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void PreexistingVrmImplicitJointsAndTerminalsWinWhileDisjointSegmentsSurvive()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.up);
                var existingRoot = Child(root, "existing", Vector3.down * .1f);
                var implicitJoint = Child(existingRoot, "implicit", Vector3.down * .1f);
                var existingTip = Child(implicitJoint, "existingTip", Vector3.down * .1f);
                var free = Child(existingTip, "free", Vector3.down * .1f);
                Child(free, "leaf", Vector3.down * .1f);
                var a = existingRoot.gameObject.AddComponent<VRM10SpringBoneJoint>(); a.m_stiffnessForce = .123f;
                var b = existingTip.gameObject.AddComponent<VRM10SpringBoneJoint>(); b.m_dragForce = .234f;
                source.AddComponent<Vrm10Instance>().SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("authored") { Joints = { a, b } });
                var pb = PhysBone(root.gameObject); Set(pb, "endpointPosition", Vector3.down * .1f);
                copy = Object.Instantiate(source); var warnings = new List<string>();
                var result = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(result.Converted, Is.EqualTo(1)); Assert.That(result.Chains, Is.EqualTo(2));
                Assert.That(result.Joints, Is.EqualTo(3)); AssertUniqueJoints(copy);
                var original = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single(s => s.Name == "authored");
                Assert.That(original.Joints[0].m_stiffnessForce, Is.EqualTo(.123f));
                Assert.That(original.Joints[1].m_dragForce, Is.EqualTo(.234f));
                Assert.That(copy.transform.Find("root/existing/implicit").GetComponent<VRM10SpringBoneJoint>(), Is.Null);
                Assert.That(warnings.Any(w => w.Contains("既存VRM")), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void GeneratedEndpointsDoNotTurnAnInertNestedPhysBoneIntoAnotherDriver()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.up);
                var leaf = Child(root, "leaf", Vector3.down * .1f);
                var parent = PhysBone(root.gameObject); Set(parent, "ignoreOtherPhysBones", false);
                Set(parent, "endpointPosition", Vector3.down * .1f);
                PhysBone(leaf.gameObject);
                copy = Object.Instantiate(source);
                var result = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                Assert.That(result.Converted, Is.EqualTo(1)); Assert.That(result.Skipped, Is.EqualTo(1));
                Assert.That(result.Joints, Is.EqualTo(2)); AssertUniqueJoints(copy);
                Assert.That(copy.GetComponentsInChildren<Transform>().Count(t => t.name.EndsWith(PhysBoneSpringExport.EndpointName)), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void GeneratedEndpointsGetUniqueDeterministicNamesWithoutRenamingTheAvatar()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var preferred = "Tip-" + PhysBoneSpringExport.EndpointName;
                // Same-named leaves and an authored node that already holds the preferred name.
                Child(source.transform, preferred, Vector3.zero);
                var skirt = Child(source.transform, "Skirt.L", Vector3.up);
                Child(Child(skirt, "Skirt_A.L", new Vector3(-.1f, -.1f, 0)), "Tip", Vector3.down * .1f);
                Child(Child(skirt, "Skirt_B.L", new Vector3(.1f, -.1f, 0)), "Tip", Vector3.down * .1f);
                var hair = Child(source.transform, "Hair_01", Vector3.up * 1.5f);
                Set(PhysBone(skirt.gameObject), "endpointPosition", Vector3.down * .1f);
                Set(PhysBone(hair.gameObject), "endpointPosition", Vector3.down * .1f);
                copy = Object.Instantiate(source);
                var authored = copy.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                var result = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                Assert.That(result.Chains, Is.EqualTo(3)); AssertUniqueJoints(copy);
                var tails = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Select(s => s.Joints.Last().transform).ToArray();
                Assert.That(tails.Select(t => t.name), Is.EqualTo(new[] { preferred + " 2", preferred + " 3", "Hair_01-" + PhysBoneSpringExport.EndpointName }));
                var names = copy.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
                Assert.That(names.Except(authored), Is.EquivalentTo(tails.Select(t => t.name)), "Only generated endpoints are new.");
                Assert.That(names.Count(n => n.Contains(PhysBoneSpringExport.EndpointName)), Is.EqualTo(names.Where(n => n.Contains(PhysBoneSpringExport.EndpointName)).Distinct().Count()));
                Assert.That(copy.transform.Find(preferred).GetComponent<VRM10SpringBoneJoint>(), Is.Null);
                var again = Object.Instantiate(source);
                try
                {
                    PhysBoneSpringExport.Convert(source, again, new List<string>());
                    Assert.That(again.GetComponent<Vrm10Instance>().SpringBone.Springs.Select(s => s.Joints.Last().transform.name),
                        Is.EqualTo(tails.Select(t => t.name)), "A second export names the endpoints the same way.");
                }
                finally { Object.DestroyImmediate(again); }
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void AParentTailCannotShareTheChildOwnersFirstJoint()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.up);
                var child = Child(root, "child", Vector3.down * .17f);
                var parent = PhysBone(root.gameObject); Set(parent, "ignoreOtherPhysBones", false);
                var local = PhysBone(child.gameObject); Set(local, "endpointPosition", Vector3.down * .1f);
                copy = Object.Instantiate(source);
                var result = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                Assert.That(result.Converted, Is.EqualTo(2)); Assert.That(result.Joints, Is.EqualTo(2));
                AssertUniqueJoints(copy);
                var parentTail = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.First().Joints.Last().transform;
                Assert.That(Vector3.Distance(parentTail.position, child.position), Is.LessThan(1e-6));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ZeroLengthPairsKeepBonesAndOriginalDepthForTheRemainingChain()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.up);
                var coincident = Child(root, "coincident", Vector3.zero);
                Child(coincident, "leaf", Vector3.down * .1f);
                var pb = PhysBone(root.gameObject); Set(pb, "endpointPosition", Vector3.down * .1f);
                Set(pb, "pull", .6f); Set(pb, "pullCurve", AnimationCurve.Linear(0, 1, 1, 0));
                copy = Object.Instantiate(source); var warnings = new List<string>();
                var result = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(result.Converted, Is.EqualTo(1)); Assert.That(result.Joints, Is.EqualTo(2));
                Assert.That(copy.transform.Find("root").GetComponent<VRM10SpringBoneJoint>(), Is.Null);
                var first = copy.GetComponent<Vrm10Instance>().SpringBone.Springs.Single().Joints[0];
                Assert.That(first.name, Is.EqualTo("coincident"));
                Assert.That(first.m_stiffnessForce, Is.EqualTo(1.6f).Within(1e-5), "Original curve coordinate remains 1/3.");
                Assert.That(copy.transform.Find("root/coincident").localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(warnings.Any(w => w.Contains("長さゼロ")), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [TestCase("endpointPosition")]
        [TestCase("rootTransform")]
        [TestCase("multiChildType")]
        public void MalformedPhysBoneDataStillStopsConversion(string field)
        {
            var source = new GameObject("source"); var outside = new GameObject("outside"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.up);
                var pb = PhysBone(root.gameObject); Set(pb, "endpointPosition", Vector3.down * .1f);
                if (field == "endpointPosition") Set(pb, field, new Vector3(float.NaN, 0, 0));
                else if (field == "rootTransform") Set(pb, field, outside.transform);
                else Set(pb, field, 99);
                copy = Object.Instantiate(source);
                Assert.Throws<InvalidOperationException>(() => PhysBoneSpringExport.Convert(source, copy, new List<string>()));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(outside); Object.DestroyImmediate(source); }
        }

        [Test]
        public void EmptyOrTruncatedSerializedSpringsPreventSaving()
        {
            var result = new PhysBoneSpringExport.Result { Converted = 1, Chains = 1, Joints = 2 };
            var document = GlbDocument.Create(new Dictionary<string, object>(), null);
            Assert.Throws<InvalidOperationException>(() => PhysBoneSpringExport.VerifyOutput(document.Write(), result));
            document.Json["extensions"] = new Dictionary<string, object>
            {
                ["VRMC_springBone"] = new Dictionary<string, object>
                { ["springs"] = new List<object> { new Dictionary<string, object> { ["joints"] = new List<object>() } } }
            };
            Assert.Throws<InvalidOperationException>(() => PhysBoneSpringExport.VerifyOutput(document.Write(), result));
        }

        [Test]
        public void InactiveExplicitRootsChildrenAndColliderRootsAreSkipped()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var hidden = Child(source.transform, "hidden", Vector3.up); hidden.gameObject.SetActive(false);
                Child(hidden, "tip", Vector3.down);
                var externalHost = PhysBone(source); Set(externalHost, "rootTransform", hidden);
                var active = Child(source.transform, "active", Vector3.zero);
                var pb = PhysBone(active.gameObject); Set(pb, "endpointPosition", Vector3.down * .1f);
                var hiddenChild = Child(active, "hiddenChild", Vector3.down); hiddenChild.gameObject.SetActive(false);
                var collider = source.AddComponent(Sdk("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider"));
                Set(collider, "rootTransform", hidden);
                using (var data = new SerializedObject(pb))
                {
                    var list = data.FindProperty("colliders"); list.arraySize = 1;
                    list.GetArrayElementAtIndex(0).objectReferenceValue = collider; data.ApplyModifiedPropertiesWithoutUndo();
                }
                copy = Object.Instantiate(source); var warnings = new List<string>();
                var result = PhysBoneSpringExport.Convert(source, copy, warnings);
                Assert.That(result.Skipped, Is.EqualTo(1)); Assert.That(result.Chains, Is.EqualTo(1));
                Assert.That(result.Colliders, Is.Zero);
                Assert.That(copy.transform.Find("hidden").GetComponentsInChildren<VRM10SpringBoneJoint>(true), Is.Empty);
                Assert.That(copy.transform.Find("active/hiddenChild").GetComponent<VRM10SpringBoneJoint>(), Is.Null);
                Assert.That(warnings.Any(w => w.Contains("Root Transformが非表示")), Is.True);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public void ExistingTerminalFieldsAndVerificationBaselinesAreProtected()
        {
            var source = new GameObject("source"); GameObject copy = null;
            try
            {
                var root = Child(source.transform, "root", Vector3.zero);
                var terminal = Child(root, "terminal", Vector3.down);
                var a = root.gameObject.AddComponent<VRM10SpringBoneJoint>();
                var b = terminal.gameObject.AddComponent<VRM10SpringBoneJoint>(); b.m_dragForce = .123f; b.m_pitch = .234f;
                source.AddComponent<Vrm10Instance>().SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("authored") { Joints = { a, b } });
                source.AddComponent<VRM10SpringBoneCollider>();
                var pb = PhysBone(terminal.gameObject); Set(pb, "endpointPosition", Vector3.down * .1f);
                var independent = Child(source.transform, "independent", Vector3.right);
                var other = PhysBone(independent.gameObject); Set(other, "endpointPosition", Vector3.down * .1f);
                copy = Object.Instantiate(source);
                var result = PhysBoneSpringExport.Convert(source, copy, new List<string>());
                var kept = copy.transform.Find("root/terminal").GetComponent<VRM10SpringBoneJoint>();
                Assert.That(kept.m_dragForce, Is.EqualTo(.123f)); Assert.That(kept.m_pitch, Is.EqualTo(.234f));
                Assert.That(result.Converted, Is.EqualTo(1)); Assert.That(result.ExistingChains, Is.EqualTo(1));
                Assert.That(result.ExistingJoints, Is.EqualTo(1)); Assert.That(result.ExistingColliders, Is.EqualTo(1));
                // A serialized old chain alone must not mask the newly converted chain disappearing.
                var document = GlbDocument.Create(new Dictionary<string, object> {
                    ["extensions"] = new Dictionary<string, object> {
                        ["VRMC_springBone"] = new Dictionary<string, object> {
                            ["springs"] = new List<object> { new Dictionary<string, object> {
                                ["joints"] = new List<object> { new Dictionary<string, object>(), new Dictionary<string, object>() } } },
                            ["colliders"] = new List<object> { new Dictionary<string, object>() }
                        } } }, null);
                Assert.Throws<InvalidOperationException>(() => PhysBoneSpringExport.VerifyOutput(document.Write(), result));
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [Test]
        public async Task AuthoredRootColliderSurvivesWithoutAnyPhysBoneOrSdkDependency()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var hair = fixture.Source.transform.Find("Independent hair/Head");
            var head = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            hair.parent.SetParent(head, true);
            var authored = fixture.Source.AddComponent<Vrm10Instance>();
            var joint = hair.gameObject.AddComponent<VRM10SpringBoneJoint>();
            var tip = Child(hair, "Authored tip", Vector3.down * .15f).gameObject.AddComponent<VRM10SpringBoneJoint>();
            var collider = fixture.Source.AddComponent<VRM10SpringBoneCollider>();
            collider.Radius = .025f; collider.Offset = Vector3.left;
            var group = fixture.Source.AddComponent<VRM10SpringBoneColliderGroup>(); group.Colliders.Add(collider);
            authored.SpringBone.ColliderGroups.Add(group);
            authored.SpringBone.Springs.Add(new Vrm10InstanceSpringBone.Spring("authored") {
                Joints = { joint, tip }, ColliderGroups = { group }
            });
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            Vrm10Instance imported = null;
            try
            {
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Authored spring", "Tests",
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                var restored = imported.SpringBone.Springs.Single().ColliderGroups.Single().Colliders.Single();
                Assert.That(restored.Radius, Is.EqualTo(collider.Radius).Within(.00001));
                Assert.That(Vector3.Distance(restored.transform.TransformPoint(restored.Offset),
                    collider.transform.TransformPoint(collider.Offset)), Is.LessThan(.00001));
                Assert.That(fixture.Source.GetComponent<VRM10SpringBoneCollider>(), Is.SameAs(collider));
                Assert.That(group.Colliders.Single(), Is.SameAs(collider));
                Assert.That(fixture.Source.transform.Find("VRVlog Spring Colliders"), Is.Null);
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); }
        }

        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        [TestCase(true, false, true)]
        public Task ActualExportImportsSpringsAndMovesHairWithoutChangingOriginal(bool full, bool modularAvatar, bool rootCollider) =>
            ExportImportsSpringsAndMovesHair(full, modularAvatar, rootCollider, false);

        [TestCase(false)]
        public Task ActualExportImportsNestedOwnersWithoutSharedJointsOrChangedAppearance(bool full) =>
            ExportImportsSpringsAndMovesHair(full, false, false, false, true);

        [TestCase(false)]
        [TestCase(true)]
        public async Task InstalledAvatarOptimizerPreservesSpringMotionAndCollisionsAfterFullExport(bool full)
        {
            if (!UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Any(package =>
                package.name == "com.anatawa12.avatar-optimizer"))
                Assert.Ignore("Optional AAO integration requires the real registered Avatar Optimizer package.");
            var optimizer = Sdk("Anatawa12.AvatarOptimizer.TraceAndOptimize");
            Assert.That(optimizer, Is.Not.Null, "The installed AAO package must load its real component assembly.");
            Assert.That(Sdk("nadena.dev.ndmf.BuildContext"), Is.Not.Null, "Installed AAO requires its NDMF dependency.");
            Assert.That(ExportOptimizationMarker.AvatarOptimizerAdapterAvailable, Is.True,
                "The installed AAO compatibility adapter must compile and register.");
            // UTF 1.4.6 cannot handle a synchronously thrown IgnoreException in
            // a Task-returning test. An async test reports the real missing
            // dependency instead of returning null to its task wrapper.
            await ExportImportsSpringsAndMovesHair(full, false, false, true);
        }

        [Test]
        public async Task InstalledAvatarOptimizerWithoutAvatarDescriptorStopsInsteadOfSavingAVrmWithoutHumanoid()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().FirstOrDefault(value =>
                value.name == "com.anatawa12.avatar-optimizer");
            // Keep the async wrapper for the UTF 1.4.6 IgnoreException caveat above.
            if (package == null) Assert.Ignore("Optional AAO integration requires the real registered Avatar Optimizer package.");
            Assert.That(Sdk("Anatawa12.AvatarOptimizer.TraceAndOptimize"), Is.Not.Null, "The installed AAO package must load its real component assembly.");
            Assert.That(Sdk("nadena.dev.ndmf.BuildContext"), Is.Not.Null, "Installed AAO requires its NDMF dependency.");
            Assert.That(ExportOptimizationMarker.AvatarOptimizerAdapterAvailable, Is.True,
                "The installed AAO compatibility adapter must compile and register.");
            Assert.That(Version.TryParse(package.version.Split('-', '+')[0], out var version), Is.True, "AAO must report a numeric package version.");
            // Without a descriptor NDMF selects its generic platform. AAO 1.9+
            // runs there and removes the root Animator; older AAO skips its
            // passes, so the optimized expression mapping is never applied.
            await ExportImportsSpringsAndMovesHair(false, false, false, true, expectedFailure: version >= new Version(1, 9)
                ? UniVrmOneClickExporter.LostHumanoidAnimator : "最適化後の表情の移動情報を取得できませんでした。");
        }

        async Task ExportImportsSpringsAndMovesHair(bool full, bool modularAvatar, bool rootCollider, bool avatarOptimizer, bool nestedPhysBones = false,
            string expectedFailure = null)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var controller = avatarOptimizer ? new StaticAnimatorController(fixture.Source.GetComponent<Animator>()) : null;
            var hair = fixture.Source.transform.Find("Independent hair/Head");
            var head = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            if (modularAvatar)
            {
                var proxyType = Sdk("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
                var markerType = Sdk("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot");
                if (proxyType == null || markerType == null) Assert.Ignore("Optional MA integration requires MA/NDMF.");
                fixture.Source.AddComponent(markerType);
                var proxy = hair.parent.gameObject.AddComponent(proxyType);
                proxyType.GetProperty("target").SetValue(proxy, head);
                var mode = proxyType.GetField("attachmentMode");
                mode.SetValue(proxy, Enum.Parse(mode.FieldType, "AsChildKeepWorldPose"));
            }
            else hair.parent.SetParent(head, true);
            var originalTip = Child(hair, "Spring tip", Vector3.down * .15f);
            var pb = PhysBone(hair.gameObject);
            Set(pb, "pull", .25f); Set(pb, "spring", .6f); Set(pb, "gravity", 0f);
            Set(pb, "immobile", 0f); Set(pb, "limitType", 1); Set(pb, "maxAngleX", 60f);
            Component nested = null;
            if (nestedPhysBones)
            {
                Child(originalTip, "Nested tip", Vector3.down * .1f);
                nested = PhysBone(originalTip.gameObject);
                Set(pb, "ignoreOtherPhysBones", false);
                Set(nested, "endpointPosition", Vector3.down * .1f);
                Set(nested, "pull", .35f); Set(nested, "spring", .5f); Set(nested, "gravity", 0f);
            }
            var collider = (rootCollider ? fixture.Source : head.gameObject).AddComponent(Sdk("VRC.SDK3.Dynamics.PhysBone.Components.VRCPhysBoneCollider"));
            Set(collider, "radius", .025f); Set(collider, "position", Vector3.left);
            Component duplicateCollider = null;
            SkinnedMeshRenderer body = null;
            if (avatarOptimizer)
            {
                // Real VRChat avatars select NDMF's VRChat platform. On the
                // generic platform AAO < 1.9 skips its passes and 1.9 can
                // remove the root Animator before UniVRM reads the humanoid.
                if (expectedFailure == null) AddVrChatAvatarDescriptor(fixture.Source);
                duplicateCollider = head.gameObject.AddComponent(collider.GetType());
                Set(duplicateCollider, "radius", .025f); Set(duplicateCollider, "position", Vector3.left);
                fixture.Source.AddComponent(Sdk("Anatawa12.AvatarOptimizer.TraceAndOptimize"));
                Assert.That(NdmfExportPreparation.NeedsProcessing(fixture.Source), Is.True,
                    "AAO authoring alone must start the NDMF build.");
                Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().All(skin =>
                    skin.bones[0] == hair && skin.sharedMesh.boneWeights.All(weight => weight.boneIndex0 == 0 && weight.weight0 == 1)), Is.True,
                    "The PhysBone must drive weighted visible geometry, so AAO cannot discard it as unused.");
                if (expectedFailure == null)
                    body = AddWeightedBody(fixture.Source, fixture.Source.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial);
            }
            using var bodyMesh = new OwnedMesh(body != null ? body.sharedMesh : null);
            using (var data = new SerializedObject(pb))
            {
                var list = data.FindProperty("colliders"); list.arraySize = avatarOptimizer ? 2 : 1;
                list.GetArrayElementAtIndex(0).objectReferenceValue = collider;
                if (avatarOptimizer) list.GetArrayElementAtIndex(1).objectReferenceValue = duplicateCollider;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            // A scene-root collider must survive on an identity child; an
            // exported bone's orphan is serialized even without a group.
            if (!avatarOptimizer)
            {
                fixture.Source.AddComponent<VRM10SpringBoneCollider>().Radius = .123f;
                head.gameObject.AddComponent<VRM10SpringBoneCollider>().Radius = .234f;
            }
            var positions = fixture.Source.GetComponentsInChildren<Transform>().Select(t => t.localPosition).ToArray();
            var originalParent = hair.parent.parent;
            var sourceSkins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Where(skin => skin != body).ToArray();
            var sourceBones = sourceSkins.Select(skin => skin.bones).ToArray();
            var bodyBones = body != null ? body.bones : null;
            var bodyWeights = body != null ? body.sharedMesh.boneWeights : null;
            var bodyVertices = body != null ? body.sharedMesh.vertices : null;
            var sourceVertices = fixture.Mesh.vertices;
            var sourceWeights = fixture.Mesh.boneWeights;
            var sourceBinds = fixture.Mesh.bindposes;
            var sourcePhysBone = EditorJsonUtility.ToJson(pb);
            var sourceNested = nested != null ? EditorJsonUtility.ToJson(nested) : null;
            var sourceCollider = EditorJsonUtility.ToJson(collider);
            var sourceDuplicate = duplicateCollider != null ? EditorJsonUtility.ToJson(duplicateCollider) : null;
            var warnings = new List<string>(); Vrm10Instance imported = null;
            if (expectedFailure != null)
            {
                string[] ExportFolders() => AssetDatabase.GetSubFolders("Assets")
                    .Where(path => path.StartsWith("Assets/VRVlogExportTemp-", StringComparison.Ordinal)).ToArray();
                var temporaryFolders = ExportFolders();
                byte[] failed = null;
                var error = Assert.Throws<InvalidOperationException>(() => failed = UniVrmOneClickExporter.Export(fixture.Source, "Spring test", "Tests", warnings,
                    exporterVersion: full ? "test" : null, lilToonVersion: full ? "2.3.4" : null,
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }));
                Assert.That(failed, Is.Null, "An optimization that loses the humanoid must never produce an apparently usable VRM.");
                Assert.That(error.Message, Is.EqualTo(expectedFailure));
                Assert.That(fixture.Source.GetComponent<Animator>().runtimeAnimatorController, Is.SameAs(controller.Controller));
                Assert.That(EditorJsonUtility.ToJson(pb), Is.EqualTo(sourcePhysBone));
                Assert.That(fixture.Source.GetComponent<ExportOptimizationMarker>(), Is.Null);
                Assert.That(ExportFolders(), Is.EquivalentTo(temporaryFolders), "A stopped export must release every exporter temporary asset folder.");
                return;
            }
            try
            {
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Spring test", "Tests", warnings,
                    exporterVersion: full ? "test" : null, lilToonVersion: full ? "2.3.4" : null,
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var motionSolver = new Vrm10FastSpringboneRuntimeStandalone();
                using (motionSolver)
                {
                    imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller(), springboneRuntime: motionSolver);
                    Assert.That(imported.SpringBone.Springs.Count, Is.EqualTo(nestedPhysBones ? 2 : 1));
                    AssertUniqueJoints(imported.gameObject);
                    Assert.That(imported.GetComponentsInChildren<VRM10SpringBoneCollider>().Length, Is.EqualTo(avatarOptimizer ? 1 : 3),
                        avatarOptimizer ? "AAO must actually merge the two referenced equivalent colliders before VRM conversion." :
                        "Both authored unreferenced colliders and the converted collider must survive export.");
                    imported.UpdateType = Vrm10Instance.UpdateTypes.None;
                    Assert.That(motionSolver.ReconstructSpringBone(), Is.True);
                    for (var i = 0; i < 30; i++) motionSolver.Process(1f / 60);
                    var joint = imported.SpringBone.Springs.Single(s => s.Name == pb.name).Joints[0].transform;
                    Assert.That(joint.GetComponent<VRM10SpringBoneJoint>().m_pitch, Is.EqualTo(60 * Mathf.Deg2Rad).Within(.001));
                    var rest = joint.localRotation;
                    Assert.That(imported.TryGetBoneTransform(HumanBodyBones.Head, out var importedHead), Is.True);
                    importedHead.localRotation *= Quaternion.Euler(0, 0, 25);
                    motionSolver.Process(1f / 60);
                    Assert.That(Quaternion.Angle(rest, joint.localRotation), Is.GreaterThan(1), "A serialized count alone does not prove secondary motion.");
                    for (var i = 0; i < 240; i++) motionSolver.Process(1f / 60);
                    Assert.That(Quaternion.Angle(rest, joint.localRotation), Is.LessThan(1), "Hair must settle after the parent stops.");
                }
                Assert.That(motionSolver.m_bufferCombiner.Combined, Is.Null, "The motion solver must release its combined native buffers.");
                var importedParent = imported.SpringBone.Springs.Single(s => s.Name == pb.name);
                var importedCollider = importedParent.ColliderGroups.Single().Colliders.Single();
                Assert.That(importedCollider.Radius, Is.EqualTo(.025f).Within(.00001),
                    "Omitted scene-root components must not shift the group's collider index.");
                var tip = importedParent.Joints.Last().transform;
                importedCollider.Offset = importedCollider.transform.InverseTransformPoint(tip.position + Vector3.right * .01f);
                // The pinned UniVRM rebuild backs up the previous buffer after
                // disposing it. Give the changed collider a cold, owned solver
                // so no removed model can acquire orphaned backup allocations.
                var collisionSolver = new Vrm10FastSpringboneRuntimeStandalone();
                using (collisionSolver)
                {
                    await collisionSolver.InitializeAsync(imported, new ImmediateCaller());
                    Assert.That(collisionSolver.ReconstructSpringBone(), Is.True);
                    for (var i = 0; i < 60; i++) collisionSolver.Process(1f / 60);
                    Assert.That(Vector3.Distance(tip.position, importedCollider.transform.TransformPoint(importedCollider.Offset)),
                        Is.GreaterThan(.024f), "Imported sphere must push the hair endpoint outside its radius.");
                }
                Assert.That(collisionSolver.m_bufferCombiner.Combined, Is.Null, "The collision solver must release its combined native buffers.");
                Assert.That(fixture.Source.GetComponentsInChildren<Transform>().Select(t => t.localPosition), Is.EqualTo(positions));
                Assert.That(hair.parent.parent, Is.EqualTo(originalParent));
                Assert.That(sourceSkins.All(skin => skin.sharedMesh == fixture.Mesh), Is.True);
                for (var i = 0; i < sourceSkins.Length; i++) Assert.That(sourceSkins[i].bones, Is.EqualTo(sourceBones[i]));
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(sourceVertices));
                Assert.That(fixture.Mesh.boneWeights, Is.EqualTo(sourceWeights));
                Assert.That(fixture.Mesh.bindposes, Is.EqualTo(sourceBinds));
                Assert.That(EditorJsonUtility.ToJson(pb), Is.EqualTo(sourcePhysBone));
                if (nested != null) Assert.That(EditorJsonUtility.ToJson(nested), Is.EqualTo(sourceNested));
                Assert.That(EditorJsonUtility.ToJson(collider), Is.EqualTo(sourceCollider));
                if (avatarOptimizer)
                {
                    Assert.That(fixture.Source.GetComponent<Animator>().runtimeAnimatorController, Is.SameAs(controller.Controller));
                    Assert.That(controller.Controller.layers.Single().stateMachine.states.Single().state.name, Is.EqualTo("Idle"));
                    Assert.That(duplicateCollider != null, Is.True);
                    Assert.That(EditorJsonUtility.ToJson(duplicateCollider), Is.EqualTo(sourceDuplicate));
                    Assert.That(fixture.Source.GetComponentsInChildren<Component>().Count(component => component != null &&
                        component.GetType() == collider.GetType()), Is.EqualTo(2), "Only the disposable clone may merge colliders.");
                    Assert.That(fixture.Source.GetComponent<ExportOptimizationMarker>(), Is.Null);
                    Assert.That(body.sharedMesh, Is.SameAs(bodyMesh.Mesh));
                    Assert.That(body.bones, Is.EqualTo(bodyBones));
                    Assert.That(bodyMesh.Mesh.boneWeights, Is.EqualTo(bodyWeights));
                    Assert.That(bodyMesh.Mesh.vertices, Is.EqualTo(bodyVertices));
                }
                Assert.That(fixture.Source.GetComponentsInChildren<VRM10SpringBoneJoint>(), Is.Empty);
                Assert.That(warnings.Any(w => w.Contains("PhysBone変換:")), Is.True);
                var artifact = Environment.GetEnvironmentVariable("VRVLOG_SPRING_FIXTURE");
                if (full && !string.IsNullOrEmpty(artifact)) System.IO.File.WriteAllBytes(artifact, bytes);
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); }
        }
    }
}
