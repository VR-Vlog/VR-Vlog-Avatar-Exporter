using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The copy after every build step an upload runs, read through the
    // installed VRChat SDK, NDMF and Avatar Optimizer with no test doubles.
    public sealed class UploadedAvatarPlayablesTests
    {
        readonly List<Object> owned = new List<Object>();
        string folder;

        [TearDown]
        public void TearDown()
        {
            foreach (var item in owned.AsEnumerable().Reverse()) if (item != null) Object.DestroyImmediate(item);
            owned.Clear();
            if (folder != null) AssetDatabase.DeleteAsset(folder);
            folder = null;
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }

        static void SetLayer(SerializedObject data, string type, RuntimeAnimatorController controller)
        {
            var layers = data.FindProperty("baseAnimationLayers");
            for (var i = 0; i < layers.arraySize; i++)
            {
                var layer = layers.GetArrayElementAtIndex(i);
                var kind = layer.FindPropertyRelative("type");
                if (kind.enumNames[kind.enumValueIndex] != type) continue;
                layer.FindPropertyRelative("isDefault").boolValue = false;
                layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
            }
        }

        [Test]
        public void PlayablesAreTheCustomLayersAndTheExpressionsTheDescriptorGivesVrchat()
        {
            var menuType = OptionalTools.Require("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu", "Install the real VRChat SDK.");
            var parametersType = OptionalTools.Require("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters", "Install the real VRChat SDK.");
            var root = Own(new GameObject("Avatar"));
            Assert.That(AvatarPlayables.Read(root), Is.Null, "An avatar without a descriptor has no playables.");
            var descriptor = VrcAvatar.AddDescriptor(root);
            var fx = Own(new AnimatorController { name = "FX" });
            var gesture = Own(new AnimatorController { name = "Gesture" });
            var action = Own(new AnimatorController { name = "Action" });
            var menu = Own(ScriptableObject.CreateInstance(menuType));
            var parameters = Own(ScriptableObject.CreateInstance(parametersType));
            using (var data = new SerializedObject(descriptor))
            {
                SetLayer(data, "FX", fx); SetLayer(data, "Gesture", gesture); SetLayer(data, "Action", action);
                data.FindProperty("expressionsMenu").objectReferenceValue = menu;
                data.FindProperty("expressionParameters").objectReferenceValue = parameters;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            // VRChat plays its own layers and shows no menu until each is switched on.
            var defaults = AvatarPlayables.Read(root);
            Assert.That(new Object[] { defaults.Fx, defaults.Gesture, defaults.Action, defaults.Menu, defaults.Parameters }, Is.All.Null);

            VrcAvatar.Set(descriptor, "customizeAnimationLayers", true);
            VrcAvatar.Set(descriptor, "customExpressions", true);
            var custom = AvatarPlayables.Read(root);
            Assert.That(custom.Fx, Is.SameAs(fx));
            Assert.That(custom.Gesture, Is.SameAs(gesture));
            Assert.That(custom.Action, Is.SameAs(action));
            Assert.That(custom.Menu, Is.SameAs(menu));
            Assert.That(custom.Parameters, Is.SameAs(parameters));
        }

        // The completion check of the uploaded copy: after AAO merges the two
        // renderers the FX animates, every BlendShape curve of the final FX
        // names the merged renderer, which holds the shape in the mesh the export writes.
        [Test]
        public void FinalFxPointsAtTheShapesOfTheMeshTheExportWrites()
        {
            var mergeType = OptionalTools.Find("Anatawa12.AvatarOptimizer.MergeSkinnedMesh");
            if (VrcAvatar.DescriptorType == null || mergeType == null || OptionalTools.Find("nadena.dev.ndmf.BuildContext") == null)
                Assert.Ignore("Install the real VRChat SDK, NDMF and Avatar Optimizer to run their real build.");
            var initialize = mergeType.GetMethod("Initialize", new[] { typeof(int) });
            if (initialize == null || mergeType.GetProperty("MergeBlendShapes") == null)
                Assert.Ignore("This fixture requires the AAO 1.8+ merge configuration API.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            folder = TempAssetFolder.Create("__UploadedPlayables_");
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var back = fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
            var backMesh = Own(Object.Instantiate(fixture.Mesh));
            backMesh.ClearBlendShapes();
            backMesh.AddBlendShapeFrame("Back detail", 100, Enumerable.Repeat(new Vector3(0, .02f, 0), 3).ToArray(), null, null);
            back.sharedMesh = backMesh;

            var clip = new AnimationClip { name = "Details" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                AnimationCurve.Constant(0, 1, 60));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Back", typeof(SkinnedMeshRenderer), "blendShape.Back detail"),
                AnimationCurve.Constant(0, 1, 40));
            var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            AssetDatabase.AddObjectToAsset(clip, fx);
            var state = fx.layers[0].stateMachine.AddState("Details"); state.writeDefaultValues = false; state.motion = clip;
            VrcAvatar.SetFx(VrcAvatar.AddDescriptor(fixture.Source), fx);

            var output = new GameObject("merged"); output.transform.SetParent(fixture.Source.transform, false);
            output.AddComponent<SkinnedMeshRenderer>();
            var merge = output.AddComponent(mergeType); initialize.Invoke(merge, new object[] { 2 });
            mergeType.GetProperty("MergeBlendShapes").SetValue(merge, false);
            mergeType.GetProperty("RemoveEmptyRendererObject").SetValue(merge, true);
            var renderers = mergeType.GetProperty("SourceSkinnedMeshRenderers").GetValue(merge);
            var add = renderers.GetType().GetMethod("Add", new[] { typeof(SkinnedMeshRenderer) });
            add.Invoke(renderers, new object[] { front }); add.Invoke(renderers, new object[] { back });
            Object.DestroyImmediate(fixture.Copy);
            fixture.Copy = Object.Instantiate(fixture.Source); fixture.Copy.name = fixture.Source.name;

            var warnings = new List<string>();
            using (var lease = NdmfExportPreparation.Prepare(fixture.Source, fixture.Copy, warnings, afterTransforming: _ => { }))
            {
                Assert.That(lease.Completed, Is.True, string.Join("\n", warnings));
                var final = lease.Playables().Fx;
                Assert.That(final, Is.Not.Null.And.Not.SameAs(fx), "The build copies the FX it edits.");
                var shapes = final.animationClips.SelectMany(AnimationUtility.GetCurveBindings)
                    .Where(binding => binding.type == typeof(SkinnedMeshRenderer) && binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                    .Select(binding => (binding.path, Shape: binding.propertyName.Substring("blendShape.".Length))).Distinct().ToArray();
                // AAO renames the shapes it keeps apart; the curves follow the new names.
                Assert.That(shapes.Length, Is.EqualTo(2), string.Join(", ", shapes));
                Assert.That(shapes.Select(binding => binding.path), Is.All.EqualTo("merged"));
                foreach (var (path, shape) in shapes)
                {
                    var renderer = fixture.Copy.transform.Find(path)?.GetComponent<SkinnedMeshRenderer>();
                    Assert.That(renderer != null && renderer.sharedMesh != null, Is.True, path);
                    Assert.That(renderer.sharedMesh.GetBlendShapeIndex(shape), Is.GreaterThanOrEqualTo(0), path + " / " + shape);
                }
                Assert.That(fixture.Copy.transform.Find("Front"), Is.Null, "AAO removed the renderers it merged.");
            }
            Assert.That(fixture.Source.GetComponent(VrcAvatar.DescriptorType), Is.Not.Null);
            Assert.That(front.sharedMesh, Is.SameAs(fixture.Mesh), "The source avatar is not edited.");
        }

        // An upload removes EditorOnly objects before the export's read: in
        // NDMF's Resolving, or at -1024 in the VRChat SDK when NDMF is not
        // installed. A VRChat avatar that needs no NDMF build has them removed
        // where NDMF removes them, in an export and in a preview alike, so the
        // export never reads a renderer the upload does not have.
        [TestCase(false)]
        [TestCase(true)]
        public void EditorOnlyObjectsAreRemovedBeforeTheExportReadsTheCopy(bool preview)
        {
            if (VrcAvatar.DescriptorType == null) Assert.Ignore("Install the real VRChat SDK to run its registered callbacks.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            VrcAvatar.AddDescriptor(fixture.Source);
            AddEditorOnly(fixture);
            Assert.That(NdmfExportPreparation.NeedsProcessing(fixture.Copy), Is.False);
            var read = true;
            var warnings = new List<string>();
            using (var lease = NdmfExportPreparation.Prepare(fixture.Source, fixture.Copy, warnings, afterTransforming: _ =>
            {
                read = fixture.Copy.transform.Find("Reference") != null || fixture.Copy.transform.Find("Hidden/Nested") != null;
            }, stopBeforeOptimizing: preview))
            {
                Assert.That(read, Is.False, "The export reads the copy without its EditorOnly objects.");
                Assert.That(fixture.Copy.transform.Find("Hidden"), Is.Null);
                Assert.That(lease.Completed, Is.EqualTo(!preview), string.Join("
", warnings));
                Assert.That(warnings.Where(warning => warning.Contains("VRChat SDK")), Is.Empty);
                if (!preview) Assert.That(lease.Playables(), Is.Not.Null, "The copy needs no NDMF build and still gives its playables.");
            }
            Assert.That(fixture.Source.transform.Find("Reference"), Is.Not.Null, "The source avatar is not edited.");
            Assert.That(fixture.Source.transform.Find("Hidden/Nested"), Is.Not.Null);
        }

        // Without a VRC Avatar Descriptor no upload removes anything, and the
        // export keeps the objects as it did before.
        [Test]
        public void EditorOnlyObjectsStayOnAnAvatarWithoutTheVrchatDescriptor()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            AddEditorOnly(fixture);
            Assert.That(NdmfExportPreparation.NeedsProcessing(fixture.Copy), Is.False);
            var read = false;
            using (NdmfExportPreparation.Prepare(fixture.Source, fixture.Copy, afterTransforming: _ =>
            {
                read = fixture.Copy.transform.Find("Reference") != null;
            })) { }
            Assert.That(read, Is.True);
            Assert.That(fixture.Copy.transform.Find("Hidden/Nested"), Is.Not.Null);
        }

        // An inactive EditorOnly object, and one under an untagged inactive
        // parent, as avatars ship spare bodies and outfits.
        static void AddEditorOnly(AttachmentConnectionTests.Fixture fixture)
        {
            var reference = new GameObject("Reference") { tag = "EditorOnly" };
            reference.transform.SetParent(fixture.Source.transform, false);
            reference.SetActive(false);
            var hidden = new GameObject("Hidden");
            hidden.transform.SetParent(fixture.Source.transform, false);
            hidden.SetActive(false);
            new GameObject("Nested") { tag = "EditorOnly" }.transform.SetParent(hidden.transform, false);
            Object.DestroyImmediate(fixture.Copy);
            fixture.Copy = Object.Instantiate(fixture.Source); fixture.Copy.name = fixture.Source.name;
        }
    }
}
