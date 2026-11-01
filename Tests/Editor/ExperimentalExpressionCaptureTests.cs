using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRVlog.LilToonExporter.Vendor.VRM10.MToon10;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExperimentalExpressionCaptureTests
    {
        [Test]
        public void APresetShapeTheCopyCannotResolveIsLeftOutWithAWarning()
        {
            var root = new GameObject("Avatar");
            var mesh = new Mesh { vertices = new Vector3[3], triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Smile", 100f, new Vector3[3], null, null);
            var assets = new System.Collections.Generic.List<Object>();
            try
            {
                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                var warnings = new System.Collections.Generic.List<string>();
                ExperimentalExpressionCaptureSession.AuthorPresets(root, new[] { (ExpressionPreset.happy, new System.Collections.Generic.List<(string, string, float)> {
                    ("Face", "Smile", 100f), ("Face", "Missing shape", 100f), ("Missing renderer", "Smile", 100f) }) }, assets, warnings);
                var bindings = root.GetComponent<Vrm10Instance>().Vrm.Expression.Happy.MorphTargetBindings;
                Assert.That(bindings.Select(binding => (binding.RelativePath, binding.Index)), Is.EqualTo(new[] { ("Face", 0) }));
                Assert.That(warnings, Has.Count.EqualTo(2));
            }
            finally
            {
                foreach (var asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        // A zero the clip keys stays a bind: the export resets that shape of the
        // default face with it. A zero on a missing shape changes nothing.
        [Test]
        public void APresetKeepsTheZerosItsClipKeys()
        {
            var root = new GameObject("Avatar");
            var mesh = new Mesh { vertices = new Vector3[3], triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Smile", 100f, new Vector3[3], null, null);
            mesh.AddBlendShapeFrame("Default", 100f, new Vector3[3], null, null);
            var assets = new System.Collections.Generic.List<Object>();
            try
            {
                var face = new GameObject("Face");
                face.transform.SetParent(root.transform, false);
                face.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
                var warnings = new System.Collections.Generic.List<string>();
                ExperimentalExpressionCaptureSession.AuthorPresets(root, new[] { (ExpressionPreset.happy, new System.Collections.Generic.List<(string, string, float)> {
                    ("Face", "Smile", 100f), ("Face", "Default", 0f), ("Face", "Missing shape", 0f) }) }, assets, warnings);
                var bindings = root.GetComponent<Vrm10Instance>().Vrm.Expression.Happy.MorphTargetBindings;
                Assert.That(bindings.Select(binding => (binding.Index, binding.Weight)), Is.EqualTo(new[] { (0, 1f), (1, 0f) }));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                foreach (var asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(mesh);
            }
        }

        // The board's list: each clip at its last frame, as DefaultTime reads it.
        static (string, AnimationClip, float)[] Listed(params AnimationClip[] clips) =>
            clips.Select(clip => (clip.name, clip, ExperimentalExpressionCaptureWindow.DefaultTime(clip))).ToArray();

        // A face on the fixture's front mesh, for the board's list.
        static AnimationClip FaceClip(string name, float weight)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, weight));
            return clip;
        }

        // A saved face record: the first shape of each row at the given weight.
        static ExperimentalExpressionCaptureSession.Pose Record(ExperimentalExpressionCaptureSession session, string name, params float[] weights)
        {
            var pose = ExperimentalExpressionCaptureSession.ClonePose(session.Baseline); pose.Name = name;
            for (var row = 0; row < weights.Length; row++) pose.Rows[row].Weights[0] = weights[row];
            session.Expressions.Add(pose);
            return pose;
        }

        [Test]
        public void OwnedCopyKeepsAValidIsolatedSceneAndPreservesSource()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var originalScene = fixture.Source.scene;
            var stamp = ExportSourceFingerprint.Compute(fixture.Source);
            GameObject copy = null, otherCopy = null;
            try
            {
                copy = NdmfExportPreparation.InstantiateOwnedCopy(fixture.Source);
                Assert.That(copy.scene.IsValid(), Is.True);
                Assert.That(copy.scene.isLoaded, Is.True);
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(copy.scene), Is.True);
                Assert.That(copy.scene, Is.Not.EqualTo(originalScene));
                Assert.That(copy.scene.GetRootGameObjects(), Does.Contain(copy));
                otherCopy = NdmfExportPreparation.InstantiateOwnedCopy(fixture.Source);
                Assert.That(otherCopy.name, Is.EqualTo(copy.name));
                Assert.That(otherCopy.scene, Is.Not.EqualTo(copy.scene), "Concurrent copies must not share ambiguous diagnostic roots.");
                var releasedScene = otherCopy.scene;
                Object.DestroyImmediate(otherCopy); otherCopy = null;
                NdmfExportPreparation.ReleaseUnusedCopyScene();
                Assert.That(releasedScene.IsValid(), Is.False, "A copy without retained diagnostics releases its scene.");
                Assert.That(copy.scene.IsValid(), Is.True, "Another live copy retains only its own scene.");
                Assert.That(fixture.Source.scene, Is.EqualTo(originalScene));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally
            {
                if (copy != null) Object.DestroyImmediate(copy);
                if (otherCopy != null) Object.DestroyImmediate(otherCopy);
                NdmfExportPreparation.ReleaseUnusedCopyScene();
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InstalledNdmfReportRemainsSafeAfterItsOwnedCopyIsDestroyed(bool showPreview)
        {
            var reportType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("nadena.dev.ndmf.ErrorReport"))
                .FirstOrDefault(type => type != null);
            if (reportType == null) Assert.Ignore("Requires the installed NDMF integration package.");
            var source = new GameObject("Owned diagnostic scene regression");
            GameObject copy = null;
            PreviewRenderUtility preview = null;
            try
            {
                copy = NdmfExportPreparation.InstantiateOwnedCopy(source);
                var scene = copy.scene;
                var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                var report = reportType.GetMethod("Create", flags).Invoke(null, new object[] { copy, true });
                typeof(NdmfExportPreparation).GetMethod("RetainCopySceneForReport", flags)
                    .Invoke(null, new object[] { copy, new { ErrorReport = report } });
                var resolve = reportType.GetMethod("TryResolveAvatar");
                if (showPreview)
                {
                    preview = new PreviewRenderUtility();
                    NdmfExportPreparation.ShowOwnedCopyInPreview(copy, preview);
                    Assert.That(copy.scene, Is.EqualTo(scene), "Preview rendering must not move NDMF's diagnostic root.");
                    Assert.That(preview.camera.overrideSceneCullingMask,
                        Is.EqualTo(UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(scene) |
                            UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(preview.camera.scene)));
                }
                var resolved = new object[] { null };
                Assert.That(resolve.Invoke(report, resolved), Is.EqualTo(true));
                Assert.That(resolved[0], Is.SameAs(copy), "Notifications must navigate to the live preview avatar.");
                preview?.Cleanup(); preview = null;
                Assert.That(copy != null, Is.True, "The session owns the avatar, independently of preview camera cleanup.");
                Assert.That(resolve.Invoke(report, new object[] { null }), Is.EqualTo(true));
                Object.DestroyImmediate(copy); copy = null;
                NdmfExportPreparation.ReleaseUnusedCopyScene();
                Assert.That(scene.IsValid(), Is.True, "The console can still retain this report after export cleanup.");
                Assert.That(resolve.Invoke(report, new object[] { null }), Is.EqualTo(false));
                GC.KeepAlive(report);
            }
            finally { preview?.Cleanup(); if (copy != null) Object.DestroyImmediate(copy); Object.DestroyImmediate(source); }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AuxiliaryPreviewsDoNotInvokeAvatarEditorCallbacks(bool blinkPreview)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
            fixture.Source.AddComponent<RecoveryCallbackProbe>().SharedMaterial = material;
            EditorWindow window = null;
            try
            {
                var stamp = ExportSourceFingerprint.Compute(fixture.Source); var color = material.GetColor("_Color");
                RecoveryCallbackProbe.ResetCounters(); RecoveryCallbackProbe.Armed = true;
                if (blinkPreview)
                {
                    var preview = ScriptableObject.CreateInstance<BlinkPreviewWindow>(); window = preview;
                    var options = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
                    options.Both.Add(new BlinkShapeBinding { Renderer = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(), Shape = "Hair detail", Weight = 80 });
                    preview.Prepare(fixture.Source, options, Array.Empty<GameObject>(), new ExportGimmickOptions());
                }
                else
                {
                    var preview = ScriptableObject.CreateInstance<PoseReviewWindow>(); window = preview;
                    var type = typeof(PoseReviewWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    type.GetField("source", flags).SetValue(preview, fixture.Source);
                    type.GetField("options", flags).SetValue(preview, new PoseExportOptions());
                    type.GetField("excluded", flags).SetValue(preview, Array.Empty<GameObject>());
                    type.GetField("gimmicks", flags).SetValue(preview, new ExportGimmickOptions());
                    type.GetMethod("PreparePreview", flags).Invoke(preview, null);
                    Assert.That(type.GetField("error", flags).GetValue(preview), Is.Null);
                    Assert.That(type.GetField("copy", flags).GetValue(preview), Is.Not.Null);
                }
                Assert.That(RecoveryCallbackProbe.AwakeCalls, Is.Zero); Assert.That(RecoveryCallbackProbe.EnableCalls, Is.Zero);
                Assert.That(material.GetColor("_Color"), Is.EqualTo(color)); Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally { if (window != null) Object.DestroyImmediate(window); RecoveryCallbackProbe.ResetCounters(); }
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ExportRetainsTheAuthoredVrmConstraintEnabledState(bool recordFace, bool enabled)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var original = fixture.Source.transform.Find("Independent hair").gameObject.AddComponent<Vrm10RotationConstraint>();
            original.Source = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            original.enabled = enabled;
            var clip = new AnimationClip { name = "Constrained face" };
            try
            {
                var stamp = ExportSourceFingerprint.Compute(fixture.Source);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.Copy.GetComponentInChildren<Vrm10RotationConstraint>().enabled, Is.EqualTo(enabled));
                if (recordFace)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 80));
                var bytes = session.Export("Constraint export", "Tests", recordFace ? Listed(clip) : Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var nodes = (System.Collections.Generic.List<object>)GlbDocument.Read(bytes).Json["nodes"];
                var count = nodes.Cast<System.Collections.Generic.Dictionary<string, object>>().Count(node =>
                    node.TryGetValue("extensions", out var extensions) &&
                    ((System.Collections.Generic.Dictionary<string, object>)extensions).ContainsKey("VRMC_node_constraint"));
                Assert.That(count, Is.EqualTo(enabled ? 1 : 0));
                Assert.That(original.enabled, Is.EqualTo(enabled)); Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(settings); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparingAndExportingDoNotInvokeAvatarEditorCallbacks(bool recordFace)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var material = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
            var probe = fixture.Source.AddComponent<RecoveryCallbackProbe>(); probe.SharedMaterial = material;
            AppearancePreparationTests.AddRule(fixture.Source, "ModularAvatarShapeChanger", "Shapes", "ChangedShape",
                fixture.Source.transform.Find("Front").gameObject, ("ShapeName", "Hair detail"), ("ChangeType", 1), ("Value", 50f));
            var clip = new AnimationClip { name = "Callback-safe face" };
            try
            {
                var stamp = ExportSourceFingerprint.Compute(fixture.Source); var color = material.GetColor("_Color");
                RecoveryCallbackProbe.ResetCounters(); RecoveryCallbackProbe.Armed = true;
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                if (recordFace)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 80));
                var bytes = session.Export("Callback-safe export", "Tests", recordFace ? Listed(clip) : Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.DoesNotThrow(() => LilToonGlbExtension.Validate(bytes));
                Assert.That(RecoveryCallbackProbe.AwakeCalls, Is.Zero); Assert.That(RecoveryCallbackProbe.EnableCalls, Is.Zero);
                Assert.That(material.GetColor("_Color"), Is.EqualTo(color)); Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally { RecoveryCallbackProbe.ResetCounters(); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void AnUnsupportedFileIsNotAddedAndItsReasonIsShown()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var face = new AnimationClip { name = "Face only" }; var bone = new AnimationClip { name = "Face and bone" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            var unsupported = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r");
            try
            {
                AnimationUtility.SetEditorCurve(face, binding, AnimationCurve.Constant(0, 1, 60));
                AnimationUtility.SetEditorCurve(bone, binding, AnimationCurve.Constant(0, 1, 60));
                AnimationUtility.SetEditorCurve(bone, unsupported, AnimationCurve.Constant(0, 1, .3f));
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                var reason = session.ClipError(bone, bone.length);
                Assert.That(reason, Is.Not.Null);
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);

                var single = Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("AddClip", flags).Invoke(window, new object[] { bone }));
                Assert.That(single.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(single.InnerException.Message, Is.EqualTo(ExperimentalExpressionCaptureWindow.RejectedMessage(new[] { (bone.name, reason) })));
                Assert.That(inputs, Is.Empty, "An unsupported file is not added.");

                // Dropping several files adds the usable ones, says how many, and names the rest.
                var several = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                    type.GetMethod("AddClips", flags).Invoke(window, new object[] { new[] { face, bone } }));
                var added = string.Format(ExporterLocalization.T("{0}件の表情を追加しました"), 1);
                Assert.That(several.InnerException.Message, Is.EqualTo(added + "\n" + ExperimentalExpressionCaptureWindow.RejectedMessage(new[] { (bone.name, reason) })));
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { face }));
                Assert.That(inputs.Single().Error, Is.Null);
                Assert.That((string)type.GetField("status", flags).GetValue(window), Is.EqualTo(added));

                // There is no limit on the number of faces: the 100th is added too.
                var fillers = Enumerable.Range(0, 98).Select(index =>
                {
                    var filler = new AnimationClip { name = "Filler " + index };
                    AnimationUtility.SetEditorCurve(filler, binding, AnimationCurve.Constant(0, 1, index));
                    return filler;
                }).ToArray();
                try
                {
                    foreach (var filler in fillers) inputs.Add(new ExperimentalExpressionCaptureSession.ClipInput { Clip = filler, Name = filler.name });
                    var last = new AnimationClip { name = "Last face" };
                    AnimationUtility.SetEditorCurve(last, binding, AnimationCurve.Constant(0, 1, 30));
                    try
                    {
                        Assert.Throws<System.Reflection.TargetInvocationException>(() =>
                            type.GetMethod("AddClips", flags).Invoke(window, new object[] { new[] { last, bone } }));
                        Assert.That(inputs.Count, Is.EqualTo(100));
                        Assert.That(inputs.Last().Clip, Is.EqualTo(last));
                    }
                    finally { Object.DestroyImmediate(last); }
                }
                finally { foreach (var filler in fillers) Object.DestroyImmediate(filler); }
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(face); Object.DestroyImmediate(bone); }
        }

        [Test]
        public void AnAddedFileThatBecomesUnsupportedStaysOnTheBoardWithItsReason()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Later with bone" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            var unsupported = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 60));
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.PlaceOf(clip).Kind, Is.EqualTo(ExpressionBoard.PlaceKind.Other));
                Assert.That(window.BlocksExport, Is.False);

                AnimationUtility.SetEditorCurve(clip, unsupported, AnimationCurve.Constant(0, 1, .3f));
                var reason = session.ClipError(clip, clip.length);
                Assert.That(reason, Is.Not.Null);
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                // The list, the board, its count and the route all see the same file.
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }), "The file stays until the user removes it.");
                var shown = board.ItemsIn(ExpressionBoard.PlaceKind.Other).Single();
                Assert.That(shown.Clip, Is.SameAs(clip)); Assert.That(shown.Error, Is.EqualTo(reason));
                Assert.That(board.OtherCount, Is.EqualTo(1), "The title counts the tiles shown.");
                Assert.That(board.UsesRecordRoute, Is.True);
                // The save stops on the file instead of writing a VRM without it.
                Assert.That(window.BlocksExport, Is.True);
                Assert.That(window.BlockReason(), Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.UnexportedFileFormat), clip.name, reason)));

                // Moved to the faces not exported, it no longer stops the save but stays listed.
                type.GetMethod("MoveTo", flags).Invoke(window, new object[] { clip, ExpressionBoard.Place.Excluded });
                Assert.That(board.ItemsIn(ExpressionBoard.PlaceKind.Excluded).Single().Clip, Is.SameAs(clip));
                Assert.That(window.BlocksExport, Is.False);
                type.GetMethod("MoveTo", flags).Invoke(window, new object[] { clip, ExpressionBoard.Place.Other });

                // A repaired file is a usable face again where it was.
                AnimationUtility.SetEditorCurve(clip, unsupported, null);
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                Assert.That(inputs.Single().Error, Is.Null);
                Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { clip }));
                Assert.That(window.BlocksExport, Is.False);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void AnAddedFileInASlotStaysThereAndStopsTheSave()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Later with bone" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            var unsupported = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 60));
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                type.GetMethod("MoveTo", flags).Invoke(window, new object[] { clip, ExpressionBoard.Place.Slot(ExpressionPreset.happy) });

                AnimationUtility.SetEditorCurve(clip, unsupported, AnimationCurve.Constant(0, 1, .3f));
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                // The slot the user chose keeps the file and names its reason.
                Assert.That(board.PlaceOf(clip), Is.EqualTo(ExpressionBoard.Place.Slot(ExpressionPreset.happy)));
                Assert.That(window.BlocksExport, Is.True);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void RemovingAnAddedFileTakesItsSlotExclusionNameAndRecordWithIt()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var kept = new AnimationClip { name = "Kept" }; var removed = new AnimationClip { name = "Removed" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            var unsupported = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r");
            try
            {
                AnimationUtility.SetEditorCurve(kept, binding, AnimationCurve.Constant(0, 1, 30));
                AnimationUtility.SetEditorCurve(removed, binding, AnimationCurve.Constant(0, 1, 60));
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClips", flags).Invoke(window, new object[] { new[] { kept, removed } });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.Rename(removed, "Renamed"), Is.True);
                type.GetMethod("RecordBoardClips", flags).Invoke(window, null);
                Assert.That(session.Expressions.Count, Is.EqualTo(2));

                // Removed from the faces not exported, as the × there does.
                type.GetMethod("MoveTo", flags).Invoke(window, new object[] { removed, ExpressionBoard.Place.Excluded });
                AnimationUtility.SetEditorCurve(removed, unsupported, AnimationCurve.Constant(0, 1, .3f));
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                type.GetMethod("RemoveAddedClip", flags).Invoke(window, new object[] { removed });

                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { kept }));
                Assert.That(board.Find(removed), Is.Null);
                Assert.That(board.Excluded, Has.No.Member(removed));
                Assert.That(board.Names.ContainsKey(removed), Is.False);
                Assert.That(session.Expressions.Select(pose => pose.Name), Is.EqualTo(new[] { "Kept" }), "Its record goes too.");
                Assert.That(window.BlocksExport, Is.False);

                // In a slot the user chose, the slot is emptied.
                type.GetMethod("MoveTo", flags).Invoke(window, new object[] { kept, ExpressionBoard.Place.Slot(ExpressionPreset.happy) });
                type.GetMethod("RemoveAddedClip", flags).Invoke(window, new object[] { kept });
                Assert.That(inputs, Is.Empty);
                Assert.That(board.PlaceOf(kept).Kind, Is.Not.EqualTo(ExpressionBoard.PlaceKind.Slot));
                Assert.That(board.UsesRecordRoute, Is.False);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(kept); Object.DestroyImmediate(removed); }
        }

        [Test]
        public void ADeletedAddedFileIsRemovedAndItsSlotStillShowsTheMissingFile()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folder = "Assets/VRVlogDeletedAdded_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Face" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 60));
                AssetDatabase.CreateAsset(clip, folder + "/Face.anim");
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                session.SetPreset(ExpressionPreset.happy, ExperimentalExpressionCaptureSession.PresetAssignment.ForClip(ExpressionPreset.happy, clip));
                Assert.That(board.UsesRecordRoute, Is.True, "An added file is recorded.");

                Assert.That(AssetDatabase.DeleteAsset(folder + "/Face.anim"), Is.True);
                Assert.That(clip == null, Is.True, "Deleting the asset leaves a null clip in the list.");
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                Assert.That(inputs, Is.Empty, "A deleted file does not stay counted toward the limit.");
                Assert.That(board.UsesRecordRoute, Is.False, "Nothing added is left to record.");
                Assert.That((string)type.GetField("status", flags).GetValue(window), Is.EqualTo(ExperimentalExpressionCaptureWindow.RejectedMessage(
                    new[] { ("Face", ExporterLocalization.T(ExperimentalExpressionCaptureWindow.DeletedClipReason)) })));
                // The slot keeps the choice and names the file as missing, so the save stops with that reason.
                var slot = board.SlotValue(ExpressionPreset.happy);
                Assert.That(slot, Is.Not.Null); Assert.That(slot.IsNone, Is.False);
                Assert.That(ExperimentalExpressionCaptureSession.PresetDisplayName(slot), Is.EqualTo(ExporterLocalization.T("見つからないファイル")));
                Assert.That(window.BlocksExport, Is.True, "The kept slot stops the save.");
            }
            finally { Object.DestroyImmediate(window); if (clip != null) Object.DestroyImmediate(clip); AssetDatabase.DeleteAsset(folder); }
        }

        [Test]
        public void ReimportRefreshesExplicitRowsAndRecordsTheRepairedFile()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>(); var clip = new AnimationClip { name = "Explicit edited face" };
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 60));
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                var input = inputs.Single();
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 2, 90));
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                Assert.That(input.Error, Is.Null); Assert.That(input.Time, Is.EqualTo(2));
                type.GetMethod("RecordBoardClips", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.Expressions.Single().Rows.Single(row => row.Path == "Front").Weights[0], Is.EqualTo(90));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); }
        }

        [Test]
        public void ObjectsExcludedByHandLeaveThePreparedCopy()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var pet = new GameObject("Excluded pet"); pet.transform.SetParent(fixture.Source.transform, false);
            var stamp = ExportSourceFingerprint.Compute(fixture.Source);
            using (var kept = new ExperimentalExpressionCaptureSession(fixture.Source, false))
            {
                Assert.That(kept.Copy.transform.Find("Excluded pet"), Is.Not.Null);
                Assert.That(kept.ExcludedObjects, Is.Empty);
            }
            using (var session = new ExperimentalExpressionCaptureSession(fixture.Source, false, new[] { pet }, new ExportGimmickOptions()))
            {
                Assert.That(session.Copy.transform.Find("Excluded pet"), Is.Null, "The board previews what the export leaves out.");
                Assert.That(session.ExcludedObjects, Is.EqualTo(new[] { pet }), "The ordinary export route gets the same objects.");
            }
            Assert.That(fixture.Source.transform.Find("Excluded pet"), Is.SameAs(pet.transform));
            Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
        }

        [Test]
        public async Task ARepeatedRendererPathLeavesTheChannelsAndWarns()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            for (var i = 0; i < 2; i++)
            {
                var twin = new GameObject("Twin"); twin.transform.SetParent(fixture.Source.transform, false);
                var skin = twin.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = fixture.Mesh; skin.sharedMaterials = front.sharedMaterials;
                skin.bones = front.bones; skin.rootBone = front.rootBone; skin.SetBlendShapeWeight(0, 35);
            }
            var clip = new AnimationClip { name = "Repeated path face" };
            // The face also moves the repeated mesh: only that part is dropped.
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 80));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Twin", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70));
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            Vrm10Instance imported = null;
            try
            {
                var stamp = ExportSourceFingerprint.Compute(fixture.Source);
                ExperimentalExpressionCaptureSession session = null;
                Assert.DoesNotThrow(() => session = new ExperimentalExpressionCaptureSession(fixture.Source, false));
                using (session)
                {
                    Assert.That(session.Channels.Any(channel => channel.Path.Contains("Twin")), Is.False);
                    Assert.That(session.Channels.Select(channel => channel.Path), Does.Contain("Front"));
                    Assert.That(session.Warnings.Count(warning => warning.Contains("Twin")), Is.EqualTo(1));
                    Assert.That(session.ClipError(clip, clip.length), Is.Null, "A face touching the repeated mesh stays usable.");
                    session.ReplaceClipRecords(new[] { new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = clip.name, Time = clip.length } }, null);
                    var recorded = session.Expressions.Single();
                    Assert.That(recorded.Rows.Select(row => row.Path).Where(path => path.Contains("Twin")), Is.Empty);
                    Assert.That(recorded.Rows.Single(row => row.Path == "Front").Weights[0], Is.EqualTo(80));
                    var bytes = session.Export("Repeated path export", "Tests", Listed(clip), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                    Assert.DoesNotThrow(() => LilToonGlbExtension.Validate(bytes));
                    imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                    var face = imported.Vrm.Expression.CustomClips.Single(item => item.name == clip.name);
                    Assert.That(face.MorphTargetBindings, Is.Not.Empty);
                    Assert.That(face.MorphTargetBindings.Select(binding => binding.RelativePath), Is.All.EqualTo("Front"));
                }
                // The board lists the face as usable, so it is shown and exported.
                var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                Assert.That(inputs.Single().Error, Is.Null);
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(window); Object.DestroyImmediate(clip); }
        }

        // A "/" in an object name above a mesh keeps its path from naming it:
        // the board leaves that mesh out of recording and still opens.
        [Test]
        public void AMeshItsPathCannotNameIsLeftOutAndWarns()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var holder = new GameObject("Slash/Part"); holder.transform.SetParent(fixture.Source.transform, false);
            var face = new GameObject("Face"); face.transform.SetParent(holder.transform, false);
            var added = face.AddComponent<SkinnedMeshRenderer>();
            added.sharedMesh = fixture.Mesh; added.sharedMaterials = front.sharedMaterials;
            added.bones = front.bones; added.rootBone = front.rootBone;
            ExperimentalExpressionCaptureSession session = null;
            Assert.DoesNotThrow(() => session = new ExperimentalExpressionCaptureSession(fixture.Source, false));
            using (session)
            {
                Assert.That(session.Channels.Select(channel => channel.Path), Does.Not.Contain("Slash/Part/Face"));
                Assert.That(session.Channels.Select(channel => channel.Path), Does.Contain("Front"));
                Assert.That(session.Warnings.Any(warning => warning.Contains("Slash/Part/Face")), Is.True);
            }
        }

        // A mesh with a NaN weight cannot give a face to record: it is left out
        // and the board still opens with the other meshes.
        [Test]
        public void AMeshWithAnInvalidWeightIsLeftOutAndWarns()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var broken = new GameObject("Broken"); broken.transform.SetParent(fixture.Source.transform, false);
            var added = broken.AddComponent<SkinnedMeshRenderer>();
            added.sharedMesh = fixture.Mesh; added.sharedMaterials = front.sharedMaterials;
            added.bones = front.bones; added.rootBone = front.rootBone; added.SetBlendShapeWeight(0, float.NaN);
            Assume.That(float.IsNaN(added.GetBlendShapeWeight(0)), "Unity keeps the weight as set.");
            ExperimentalExpressionCaptureSession session = null;
            Assert.DoesNotThrow(() => session = new ExperimentalExpressionCaptureSession(fixture.Source, false));
            using (session)
            {
                Assert.That(session.Channels.Select(channel => channel.Path), Does.Not.Contain("Broken"));
                Assert.That(session.Channels.Select(channel => channel.Path), Does.Contain("Front"));
                Assert.That(session.Warnings.Any(warning => warning.Contains("Broken")), Is.True);
            }
        }

        [UnityTest]
        public IEnumerator CorrectedRecommendedAssetBecomesSelectableWithoutLosingExplicitFiles()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folder = "Assets/VRVlogCandidateRefresh_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            var clip = new AnimationClip { name = "Repair candidate" }; var retained = new AnimationClip { name = "Keep selected face" };
            var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic; var type = typeof(ExperimentalExpressionCaptureWindow);
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            var unsupported = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, 75));
                AnimationUtility.SetEditorCurve(clip, unsupported, AnimationCurve.Constant(0, 1, .3f)); AssetDatabase.CreateAsset(clip, folder + "/Face.anim");
                AnimationUtility.SetEditorCurve(retained, binding, AnimationCurve.Constant(0, 1, 50));
                var state = machine.AddState("Face"); state.motion = clip; machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
                fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { retained });
                // Show() restores the size saved for this window type; set ours after it.
                window.Show(); window.position = new Rect(40, 40, 700, 1100);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var errors = (System.Collections.Generic.Dictionary<AnimationClip, string>)type.GetField("clipErrors", flags).GetValue(window);
                Assert.That(errors[clip], Is.Not.Null);
                AnimationUtility.SetEditorCurve(clip, unsupported, null); EditorUtility.SetDirty(clip); AssetDatabase.SaveAssets(); AssetDatabase.ImportAsset(folder + "/Face.anim", ImportAssetOptions.ForceUpdate);
                for (var frame = 0; frame < 16; frame++) { window.Repaint(); yield return null; }
                Assert.That(errors[clip], Is.Null, "Reimport refreshes validation and the disabled toggle.");
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                Assert.That(inputs.Single().Clip, Is.SameAs(retained));
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.HasCurrentSource, Is.True);
                var rectangles = (System.Collections.Generic.Dictionary<AnimationClip, Rect>)type.GetField("candidateRects", flags).GetValue(window);
                Click(window, rectangles[clip]); for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.Channels.Single(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(75));
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                type.GetMethod("RecordBoardClips", flags).Invoke(window, null);
                Assert.That(session.Expressions.Count, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(window); AssetDatabase.DeleteAsset(folder); Object.DestroyImmediate(retained); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        [TestCase(false, "2nd", "stay")]
        [TestCase(false, "2nd", "close")]
        [TestCase(true, "2nd", "close")]
        [TestCase(false, "2nd", "comparison")]
        [TestCase(true, "2nd", "comparison")]
        [TestCase(false, "2nd", "change")]
        [TestCase(true, "2nd", "change")]
        [TestCase(true, "2nd", "stay")]
        [TestCase(false, "3rd", "stay")]
        [TestCase(true, "3rd", "stay")]
        public void PrimaryWindowOffersMaterialRecoveryAndSavesRetryWithoutChangingSource(bool recordFace, string layer, string ownerAction)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon"));
            var texture = new RenderTexture(16, 16, 0);
            var clip = new AnimationClip { name = "Recovery face" };
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-primary-recovery-" + Guid.NewGuid().ToString("N") + ".vrm");
            var property = "_UseMain" + layer + "Tex"; var slot = "_Main" + layer + "Tex";
            try
            {
                material.SetFloat(property, 1); material.SetTexture(slot, texture);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var stamp = ExportSourceFingerprint.Compute(fixture.Source);
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                if (recordFace)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
                    type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                }
                var recovery = window.SaveVrm(path);
                Assert.That(recovery, Is.Not.Null); Assert.That(File.Exists(path), Is.False);
                Assert.That(Resources.FindObjectsOfTypeAll<ExportFailureWindow>().Any(), Is.True, "Primary UI opens the existing recovery dialog.");
                var kind = layer == "2nd" ? ExportRecoveryActionKind.OmitSecondLayer : ExportRecoveryActionKind.OmitThirdLayer;
                var diagnostic = recovery.Report.Diagnostics.Single(item => item.Action?.Kind == kind);
                Assert.That(diagnostic.Action.Material, Is.SameAs(material));
                var target = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                if (ownerAction == "close" || ownerAction == "comparison") { Object.DestroyImmediate(window); window = null; }
                if (ownerAction == "change") type.GetMethod("ChangeAvatar", flags).Invoke(window, null);
                Assert.That(target.Copy, Is.Not.Null, "The recovery dialog retains the prepared copy after its owner closes.");
                var options = new ExportRecoveryOptions(); options.Actions.Add(diagnostic.Action);
                Assert.That(recovery.Attempt(options), Is.True, recovery.Failure?.ToString());
                if (ownerAction == "comparison")
                {
                    ExportRecoveryComparisonWindow.Show(recovery);
                    foreach (var failure in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failure);
                    Assert.That(target.Copy, Is.Not.Null, "The comparison owns the captured input after the failure window closes.");
                    ExportFailureWindow.Show(recovery);
                    foreach (var comparison in Resources.FindObjectsOfTypeAll<ExportRecoveryComparisonWindow>()) Object.DestroyImmediate(comparison);
                    Assert.That(target.Copy, Is.Not.Null, "Returning to remedies transfers ownership back before closing the comparison.");
                    Assert.That(recovery.Attempt(options), Is.True, recovery.Failure?.ToString());
                }
                Assert.DoesNotThrow(() => recovery.SavePending());
                Assert.DoesNotThrow(() => LilToonGlbExtension.Validate(File.ReadAllBytes(path)));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
                Assert.That(material.GetFloat(property), Is.EqualTo(1)); Assert.That(material.GetTexture(slot), Is.SameAs(texture));
                foreach (var failure in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failure);
                if (ownerAction != "stay") Assert.That(target.Copy, Is.Null, "Closing recovery releases its private preview scene and capture assets.");
            }
            finally
            {
                foreach (var comparison in Resources.FindObjectsOfTypeAll<ExportRecoveryComparisonWindow>()) Object.DestroyImmediate(comparison);
                foreach (var failure in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failure);
                if (window != null) Object.DestroyImmediate(window); Object.DestroyImmediate(clip); Object.DestroyImmediate(material); Object.DestroyImmediate(texture);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // Answers the result list at once, as if the user pressed 保存する.
        sealed class SavingFallbackDialog : IExportFallbackDialog
        {
            internal int Calls;
            public void Show(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings)
            {
                Calls++;
                save();
            }
        }

        // Both transfers ("かんたん" passes interactive false, "通常" true): a
        // failing stage follows the project-wide choice (省いて続ける). On, the
        // stage is left out and listed in the result list, which only the
        // interactive transfer asks about; off, both stop and open the
        // recovery window. interactive never changes whether the file is written.
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void TransferFollowsTheStageChoice(bool continueOnFailure, bool interactive)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon"));
            var texture = new RenderTexture(16, 16, 0);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-quick-stage-" + Guid.NewGuid().ToString("N") + ".vrm");
            var hadPreference = EditorPrefs.HasKey(ExportStageGuard.ContinuePreference);
            var previous = EditorPrefs.GetBool(ExportStageGuard.ContinuePreference, true);
            var originalDialog = ExportFallbackConfirmation.Dialog;
            var originalBatchMode = ExportFallbackConfirmation.IsBatchMode;
            var dialog = new SavingFallbackDialog();
            var saved = 0;
            try
            {
                // The test runner may be a batch run; pretend an Editor session so
                // the interactive transfer really asks.
                ExportFallbackConfirmation.Dialog = dialog;
                ExportFallbackConfirmation.IsBatchMode = () => false;
                EditorPrefs.SetBool(ExportStageGuard.ContinuePreference, continueOnFailure);
                material.SetFloat("_UseMain2ndTex", 1); material.SetTexture("_Main2ndTex", texture);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var failure = window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved++, interactive);
                if (continueOnFailure)
                {
                    Assert.That(failure, Is.Null);
                    Assert.That(saved, Is.EqualTo(1));
                    Assert.That(File.Exists(path), Is.True);
                    Assert.That(Resources.FindObjectsOfTypeAll<ExportFailureWindow>().Any(), Is.False);
                    Assert.That(dialog.Calls, Is.EqualTo(interactive ? 1 : 0), "Only the interactive transfer asks about the left-out stage.");
                }
                else
                {
                    Assert.That(failure, Is.Not.Null);
                    Assert.That(saved, Is.Zero);
                    Assert.That(File.Exists(path), Is.False);
                    Assert.That(Resources.FindObjectsOfTypeAll<ExportFailureWindow>().Any(), Is.True);
                    Assert.That(dialog.Calls, Is.Zero);
                }
            }
            finally
            {
                ExportFallbackConfirmation.Dialog = originalDialog;
                ExportFallbackConfirmation.IsBatchMode = originalBatchMode;
                if (hadPreference) EditorPrefs.SetBool(ExportStageGuard.ContinuePreference, previous);
                else EditorPrefs.DeleteKey(ExportStageGuard.ContinuePreference);
                foreach (var failureWindow in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failureWindow);
                Object.DestroyImmediate(window); Object.DestroyImmediate(material); Object.DestroyImmediate(texture);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // A lip-sync or blink switch holds shapes at the 0 they rest at and changes nothing, so the board
        // treats it as unsupported and does not show it. A clip that resets a shape resting away from 0,
        // moves one, or is entered by a gesture (the gesture route exports it) stays a face. A controller
        // whose gestures cannot be read keeps its clips and does not stop the board from opening.
        [TestCase(0f, 0f, "plain", false)]
        [TestCase(0f, 35f, "plain", true)]
        [TestCase(75f, 0f, "plain", true)]
        [TestCase(0f, 0f, "gesture", true)]
        [TestCase(0f, 0f, "unreadable", true)]
        [TestCase(0f, 0f, "bone", true)]
        [TestCase(0f, 0f, "rest bone", false)]
        public void ControllerClipsThatChangeNoFaceAreUnsupported(float clipWeight, float restWeight, string kind, bool usable)
        {
            using var on = new ExpressionTransformsSwitch(true);
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.SetBlendShapeWeight(0, restWeight);
            var clip = new AnimationClip { name = "Lip sync switch" }; var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, clipWeight));
                // A clip that moves a bone away from the prepared pose is a face the export writes; one that holds it there is not.
                if (kind == "bone" || kind == "rest bone")
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Independent hair/Head", typeof(Transform), kind == "bone" ? "localEulerAnglesRaw.y" : "m_LocalPosition.y"),
                        AnimationCurve.Constant(0, 1, kind == "bone" ? 20 : fixture.Source.transform.Find("Independent hair/Head").localPosition.y));
                var state = machine.AddState("Switch"); state.motion = clip; machine.defaultState = state;
                if (kind == "gesture")
                {
                    controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                    machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.Equals, 0, "GestureLeft");
                }
                // Unity itself logs the missing state machine whenever the controller is read.
                LogAssert.ignoreFailingMessages = kind == "unreadable";
                var layer = new AnimatorControllerLayer { name = "Switch", defaultWeight = 1, stateMachine = machine };
                controller.layers = kind == "unreadable"
                    ? new[] { layer, new AnimatorControllerLayer { name = "Broken", defaultWeight = 1 } }
                    : new[] { layer };
                fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                // Still a candidate, so an exclusion saved before keeps counting.
                Assert.That(session.RecommendClips().Any(item => item.Clip == clip), Is.True);
                var expected = usable ? null : ExporterLocalization.T(ExperimentalExpressionCaptureSession.NoFaceChange);
                Assert.That(session.ClipErrors(new[] { clip })[clip], Is.EqualTo(expected));
                Assert.That(session.ClipError(clip, 0), Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        // A face may also turn a bone, such as an eye highlight, or show an object. It is still a face on the
        // board, read from its BlendShapes alone, whether a gesture enters it or not; the export writes the
        // bone and the shown object beside it. A finger pose beside the face is left out; a body pose keeps a
        // clip no gesture enters unsupported. A material swap keeps the clip unsupported. A hand pose that
        // only holds the face shapes at rest is not a face.
        [TestCase("transform", true, 80f, "usable")]
        [TestCase("transform", false, 80f, "usable")]
        [TestCase("active", true, 80f, "usable")]
        [TestCase("active", false, 80f, "usable")]
        [TestCase("finger", true, 80f, "usable")]
        [TestCase("finger", false, 80f, "usable")]
        [TestCase("body", false, 80f, "unsupported")]
        [TestCase("material", true, 80f, "unsupported")]
        [TestCase("transform", true, 0f, "noface")]
        public void GestureFacesIgnoreBoneCurvesBesideTheFace(string extra, bool gesture, float weight, string expected)
        {
            using var on = new ExpressionTransformsSwitch(true);
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.SetBlendShapeWeight(0, 0);
            var clip = new AnimationClip { name = "Smile" }; var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, weight));
                var (binding, value) = CurveBesideTheFace(extra);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, value));
                var state = machine.AddState("Smile"); state.motion = clip; machine.defaultState = state;
                if (gesture)
                {
                    controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                    machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.Equals, 3, "GestureLeft");
                }
                controller.layers = new[] { new AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
                fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.RecommendClips().Any(item => item.Clip == clip), Is.True);
                var error = session.ClipErrors(new[] { clip })[clip];
                if (expected == "usable")
                {
                    Assert.That(error, Is.Null);
                    // A standard expression carries BlendShapes only. It takes a gesture face beside an eye
                    // highlight bone, not the angle as a weight; a bone outside a gesture face or a shown object
                    // keeps the clip out of it, as before, so the automatic choice of each preset is unchanged.
                    var preset = ExperimentalExpressionCaptureSession.PresetAssignment.ForClip(ExpressionPreset.happy, clip);
                    var presetUsable = gesture && (extra == "transform" || extra == "finger");
                    if (presetUsable) Assert.DoesNotThrow(() => session.BuildPresetPose(ExpressionPreset.happy, preset));
                    else Assert.That(session.PresetError(ExpressionPreset.happy, preset), Does.EndWith(binding.propertyName));
                    Assert.That(session.SuggestPresets(new[] { clip }).ContainsKey(ExpressionPreset.happy), Is.EqualTo(presetUsable));
                }
                else if (expected == "noface") Assert.That(error, Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureSession.NoFaceChange)));
                else Assert.That(error, Does.EndWith(binding.propertyName));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        // The curve a face clip carries beside its BlendShape, and the value it holds.
        static (EditorCurveBinding Binding, float Value) CurveBesideTheFace(string extra)
        {
            switch (extra)
            {
                case "transform": return (EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.x"), -30);
                case "active": return (EditorCurveBinding.FloatCurve("Front", typeof(GameObject), "m_IsActive"), 0);
                case "finger": return (EditorCurveBinding.FloatCurve("", typeof(Animator), "LeftHand.Index.1 Stretched"), -0.5f);
                case "body": return (EditorCurveBinding.FloatCurve("", typeof(Animator), "Spine Front-Back"), 0.3f);
                case "material": return (EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r"), 0.5f);
                default: throw new System.ArgumentOutOfRangeException(nameof(extra), extra, null);
            }
        }

        // While the export leaves bones and visibility out, the board treats them as 1.4.2 did: a gesture
        // face beside a bone is a face of its BlendShapes, a bone outside a gesture face or a shown object
        // keeps the clip unsupported, and a clip that only moves a bone changes no face.
        [TestCase("transform", true, 80f, "usable")]
        [TestCase("transform", false, 80f, "unsupported")]
        [TestCase("active", true, 80f, "unsupported")]
        [TestCase("active", false, 80f, "unsupported")]
        [TestCase("finger", false, 80f, "unsupported")]
        [TestCase("transform", true, 0f, "noface")]
        public void WhileTheExportLeavesBonesOutTheBoardTreatsThemAsBefore(string extra, bool gesture, float weight, string expected)
        {
            using var off = new ExpressionTransformsSwitch(false);
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.SetBlendShapeWeight(0, 0);
            var clip = new AnimationClip { name = "Smile" }; var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, weight));
                var (binding, value) = CurveBesideTheFace(extra);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, value));
                var state = machine.AddState("Smile"); state.motion = clip; machine.defaultState = state;
                if (gesture)
                {
                    controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                    machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.Equals, 3, "GestureLeft");
                }
                controller.layers = new[] { new AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
                fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                var error = session.ClipErrors(new[] { clip })[clip];
                if (expected == "usable")
                {
                    Assert.That(error, Is.Null);
                    Assert.DoesNotThrow(() => session.BuildPresetPose(ExpressionPreset.happy, ExperimentalExpressionCaptureSession.PresetAssignment.ForClip(ExpressionPreset.happy, clip)));
                }
                else if (expected == "noface") Assert.That(error, Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureSession.NoFaceChange)));
                else Assert.That(error, Does.EndWith(binding.propertyName));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ChildAnimatorClipTargetsItsOwnMeshEvenWithAnIdenticalRootMesh(bool competingRoot)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var child = new GameObject("Accessory"); child.transform.SetParent(fixture.Source.transform, false);
            var face = fixture.Source.transform.Find("Front"); face.SetParent(child.transform, false);
            SkinnedMeshRenderer competing = null;
            if (competingRoot)
            {
                var duplicate = Object.Instantiate(face.gameObject, fixture.Source.transform); duplicate.name = "Front";
                competing = duplicate.GetComponent<SkinnedMeshRenderer>();
            }
            var clip = new AnimationClip { name = "Relative face" }; var controller = new AnimatorController(); var machine = new AnimatorStateMachine();
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 75));
                var state = machine.AddState("Face"); state.motion = clip; machine.defaultState = state;
                controller.layers = new[] { new AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
                child.AddComponent<Animator>().runtimeAnimatorController = controller;
                var stamp = ExportSourceFingerprint.Compute(fixture.Source);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.RecommendClips().Any(item => item.Clip == clip), Is.True);
                Assert.That(session.ClipError(clip, clip.length), Is.Null);
                session.PreviewClip(clip, clip.length);
                Assert.That(session.Channels.Single(channel => channel.Path == "Accessory/Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(75));
                if (competingRoot) Assert.That(session.Channels.Single(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(35));
                session.ReplaceClipRecords(new[] { new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "Relative face", Time = clip.length } }, null);
                Assert.That(session.Expressions.Single().Rows.Single(row => row.Path == "Accessory/Front").Weights[0], Is.EqualTo(75));
                Assert.That(AnimationUtility.GetCurveBindings(clip).Single().path, Is.EqualTo("Front"));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(stamp));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        [TestCase(false, "APL", false)]
        [TestCase(true, "APL", false)]
        [TestCase(false, "menu", false)]
        [TestCase(true, "menu", false)]
        [TestCase(false, "APL", true)]
        [TestCase(true, "APL", true)]
        public async Task RegisteredAndManualBodyPosesSurviveWithOrWithoutRecordedFaces(bool recordFace, string registration, bool sameClip)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var registeredClip = HumanoidPoseTests.Clip(fixture.Source, 30); registeredClip.name = "Registered clip";
            var manualClip = sameClip ? registeredClip : HumanoidPoseTests.Clip(fixture.Source, 60);
            var assets = new System.Collections.Generic.List<Object>();
            var face = FaceClip("Selected face", 65);
            Vrm10Instance imported = null;
            try
            {
                if (registration == "APL")
                {
                    var type = Sdk(PoseExportSession.AplType);
                    if (type == null) Assert.Ignore("Install the real Avatar Pose Library package to verify its serialized registration.");
                    var child = new GameObject("Registered library"); child.transform.SetParent(fixture.Source.transform, false);
                    var component = child.AddComponent(type);
                    var data = Activator.CreateInstance(type.GetField("data").FieldType); SetPoseMember(component, "data", data);
                    var category = AddPoseListItem((IList)PoseMenuResolver.Member(data, "categories")); SetPoseMember(category, "name", "Body");
                    var entry = AddPoseListItem((IList)PoseMenuResolver.Member(category, "poses")); SetPoseMember(entry, "name", "Registered body"); SetPoseMember(entry, "animationClip", registeredClip);
                }
                else ConfigureRegisteredMenuPose(fixture.Source, registeredClip, assets);
                var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                session.PoseOptions.Manual.Add(new ManualPose { Clip = manualClip, Name = "Manual body" });
                var bytes = session.Export("Registered poses", "Tests", recordFace ? Listed(face) : Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var document = GlbDocument.Read(bytes);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)document.Json["extensions"];
                var poses = VRVlog.Poses.HumanoidPoseData.Read(extensions[VRVlog.Poses.HumanoidPoseData.Extension]);
                Assert.That(poses.Count, Is.EqualTo(sameClip ? 1 : 2));
                Assert.That(poses.Any(pose => pose.Name == "Registered body"), Is.True);
                if (!sameClip) Assert.That(poses.Any(pose => pose.Name == "Manual body"), Is.True);
                Assert.That(poses.All(pose => pose.Bones.Any(bone => bone.Name == "leftUpperArm")), Is.True);
                VRVlog.Poses.HumanoidPoseData.ValidateReferences(poses, document.Json);
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported.GetComponent<Animator>().isHuman, Is.True);
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                foreach (var asset in assets) Object.DestroyImmediate(asset);
                Object.DestroyImmediate(face);
                if (manualClip != registeredClip) Object.DestroyImmediate(manualClip); Object.DestroyImmediate(registeredClip);
            }
        }

        static void SetPoseMember(object target, string name, object value)
        {
            var field = target.GetType().GetField(name); field.SetValue(target, field.FieldType.IsEnum ? Enum.Parse(field.FieldType, value.ToString()) : value);
        }
        static object AddPoseListItem(IList list)
        {
            var value = Activator.CreateInstance(list.GetType().GetGenericArguments()[0]); list.Add(value); return value;
        }
        static void ConfigureRegisteredMenuPose(GameObject source, AnimationClip clip, System.Collections.Generic.List<Object> assets)
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var descriptor = (Component)typeof(PoseRegistrationTests).GetMethod("Descriptor", flags).Invoke(null, new object[] { source });
            var controller = new AnimatorController(); assets.Add(controller);
            controller.AddParameter("Pose", AnimatorControllerParameterType.Int);
            var machine = new AnimatorStateMachine(); assets.Add(machine);
            var idle = machine.AddState("Idle"); idle.writeDefaultValues = false; machine.defaultState = idle;
            var state = machine.AddState("Selected"); state.writeDefaultValues = false; state.motion = clip;
            var tracking = state.AddStateMachineBehaviour(Sdk("VRC.SDK3.Avatars.Components.VRCAnimatorTrackingControl"));
            foreach (var part in new[] { "trackingLeftHand", "trackingRightHand", "trackingHip", "trackingLeftFoot", "trackingRightFoot", "trackingLeftFingers", "trackingRightFingers" }) SetPoseMember(tracking, part, "Animation");
            var transition = machine.AddAnyStateTransition(state); transition.canTransitionToSelf = false; transition.hasExitTime = false; transition.duration = 0; transition.AddCondition(AnimatorConditionMode.Equals, 1, "Pose");
            controller.layers = new[] { new AnimatorControllerLayer { name = "Body pose", defaultWeight = 1, stateMachine = machine } };
            foreach (var key in new[] { "baseAnimationLayers", "specialAnimationLayers" })
            {
                var field = descriptor.GetType().GetField(key); var layers = (Array)field.GetValue(descriptor);
                for (var i = 0; i < layers.Length; i++)
                {
                    var layer = layers.GetValue(i); var empty = new AnimatorController(); assets.Add(empty);
                    SetPoseMember(layer, "isDefault", false); SetPoseMember(layer, "animatorController", PoseMenuResolver.Member(layer, "type").ToString() == "Gesture" ? controller : empty); layers.SetValue(layer, i);
                }
                field.SetValue(descriptor, layers);
            }
            var menu = ScriptableObject.CreateInstance(Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu")); assets.Add(menu);
            var control = AddPoseListItem((IList)PoseMenuResolver.Member(menu, "controls")); SetPoseMember(control, "name", "Registered body"); SetPoseMember(control, "type", "Toggle"); SetPoseMember(control, "value", 1f);
            var parameter = Activator.CreateInstance(control.GetType().GetField("parameter").FieldType); SetPoseMember(parameter, "name", "Pose"); SetPoseMember(control, "parameter", parameter); SetPoseMember(descriptor, "expressionsMenu", menu);
            var parameters = ScriptableObject.CreateInstance(Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters")); assets.Add(parameters);
            var parametersField = parameters.GetType().GetField("parameters"); var element = parametersField.FieldType.GetElementType(); var definition = Activator.CreateInstance(element); SetPoseMember(definition, "name", "Pose"); SetPoseMember(definition, "valueType", "Int");
            var definitions = Array.CreateInstance(element, 1); definitions.SetValue(definition, 0); parametersField.SetValue(parameters, definitions); SetPoseMember(descriptor, "expressionParameters", parameters);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReenablingBlinkAfterNoneRestoresAutomaticBlinkInSavedVrm(bool recordFace)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var sourceSkin = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(); sourceSkin.SetBlendShapeWeight(0, 0);
            var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var descriptor = fixture.Source.AddComponent(descriptorType);
            descriptorType.GetField("enableEyeLook").SetValue(descriptor, true);
            var eyeField = descriptorType.GetField("customEyeLookSettings"); var eye = eyeField.GetValue(descriptor);
            var lidType = eye.GetType().GetField("eyelidType"); lidType.SetValue(eye, Enum.Parse(lidType.FieldType, "Blendshapes"));
            eye.GetType().GetField("eyelidsSkinnedMesh").SetValue(eye, sourceSkin);
            eye.GetType().GetField("eyelidsBlendshapes").SetValue(eye, new[] { 0, -1, -1 }); eyeField.SetValue(descriptor, eye);
            var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Selected face" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-reenabled-blink-" + Guid.NewGuid().ToString("N") + ".vrm");
            Vrm10Instance imported = null;
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetField("author", flags).SetValue(window, "Tests");
                type.GetMethod("Prepare", flags).Invoke(window, null);
                if (recordFace)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
                    type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                }
                type.GetMethod("ShowBlinkConfiguration", flags).Invoke(window, null);
                var dialog = Resources.FindObjectsOfTypeAll<BlinkConfigurationWindow>().Single();
                dialog.Options.SelectMode(BlinkExportMode.None); dialog.ApplySettings();
                window.SaveVrm(path);
                imported = await Vrm10.LoadBytesAsync(File.ReadAllBytes(path), canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                // No-blink exports intentionally retain an inert standard clip.
                // Verify visible behavior rather than requiring absent metadata.
                var disabledSkin = VrChatExpressionSampler.FindRenderer(imported.gameObject, imported.Vrm.Expression.Blink.MorphTargetBindings[0].RelativePath);
                using (var disabledBaked = new BlinkTestMesh())
                {
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); disabledSkin.BakeMesh(disabledBaked.Mesh); var rest = disabledBaked.Mesh.vertices;
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process(); disabledSkin.BakeMesh(disabledBaked.Mesh);
                    Assert.That(disabledBaked.Mesh.vertices.Zip(rest, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                }
                Object.DestroyImmediate(imported.gameObject); imported = null;
                // The primary IMGUI checkbox writes this same bool directly.
                type.GetField("automaticBlink", flags).SetValue(window, true);
                type.GetMethod("ShowBlinkConfiguration", flags).Invoke(window, null);
                dialog = Resources.FindObjectsOfTypeAll<BlinkConfigurationWindow>().Single();
                Assert.That(dialog.Options.Mode, Is.EqualTo(BlinkExportMode.Auto)); dialog.Close();
                window.SaveVrm(path);
                imported = await Vrm10.LoadBytesAsync(File.ReadAllBytes(path), canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported.Vrm.Expression.Blink?.MorphTargetBindings, Is.Not.Empty);
                var outputSkin = VrChatExpressionSampler.FindRenderer(imported.gameObject, imported.Vrm.Expression.Blink.MorphTargetBindings[0].RelativePath);
                using var baked = new BlinkTestMesh();
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh); var before = baked.Mesh.vertices;
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f));
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(window); Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ChangingAvatarDoesNotCarryLicenseSelectionsIntoTheNextSavedVrm(bool recordFace, bool changeFromUnpreparedWindow)
        {
            using var first = new AttachmentConnectionTests.Fixture();
            using var second = new AttachmentConnectionTests.Fixture();
            foreach (var skin in first.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Concat(second.Source.GetComponentsInChildren<SkinnedMeshRenderer>()))
                skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Selected face" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-avatar-license-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                type.GetField("source", flags).SetValue(window, first.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var selected = new AvatarLicenseOptions {
                    AvatarPermission = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.AvatarPermissionType.everyone,
                    CommercialUsage = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.CommercialUsageType.corporation,
                    CreditNotation = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.CreditNotationType.unnecessary,
                    Modification = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.ModificationType.allowModificationRedistribution,
                    AllowExcessivelyViolentUsage = true, AllowExcessivelySexualUsage = true,
                    AllowPoliticalOrReligiousUsage = true, AllowAntisocialOrHateUsage = true, AllowRedistribution = true,
                    CopyrightInformation = "First avatar copyright", OtherLicenseUrl = "https://example.invalid/first-avatar", ThirdPartyLicenses = "First avatar licenses"
                };
                type.GetField("licenseOptions", flags).SetValue(window, selected);
                if (recordFace)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
                    type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                }
                window.SaveVrm(path);
                var initial = SavedMeta(path);
                Assert.That(initial["avatarPermission"], Is.EqualTo("everyone"));
                Assert.That(initial["thirdPartyLicenses"], Is.EqualTo(selected.ThirdPartyLicenses));
                type.GetMethod(changeFromUnpreparedWindow ? "Cleanup" : "ChangeAvatar", flags).Invoke(window, null);
                type.GetMethod("SelectSource", flags).Invoke(window, new object[] { second.Source });
                type.GetMethod("Prepare", flags).Invoke(window, null);
                window.SaveVrm(path);
                var next = SavedMeta(path);
                Assert.That(next["avatarPermission"], Is.EqualTo("onlyAuthor"));
                Assert.That(next["commercialUsage"], Is.EqualTo("personalNonProfit"));
                Assert.That(next["creditNotation"], Is.EqualTo("required"));
                Assert.That(next["modification"], Is.EqualTo("prohibited"));
                foreach (var key in new[] { "allowExcessivelyViolentUsage", "allowExcessivelySexualUsage", "allowPoliticalOrReligiousUsage", "allowAntisocialOrHateUsage", "allowRedistribution" })
                    Assert.That(next[key], Is.EqualTo(false), key);
                foreach (var key in new[] { "copyrightInformation", "otherLicenseUrl", "thirdPartyLicenses" })
                    Assert.That(!next.TryGetValue(key, out var value) || value == null || string.IsNullOrEmpty(value.ToString()), Is.True, key);
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        [TestCase(false, "", "avatar")]
        [TestCase(true, "", "avatar")]
        [TestCase(false, " \t ", "avatar")]
        [TestCase(true, " \t ", "avatar")]
        [TestCase(false, "  Named avatar  ", "Named avatar")]
        [TestCase(true, "  Named avatar  ", "Named avatar")]
        public void PrimaryWindowSavesBlankOrPaddedAvatarNames(bool recordFace, string rootName, string expectedName)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            fixture.Source.name = rootName;
            var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Selected face" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-avatar-name-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                if (recordFace)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
                    type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                }
                window.SaveVrm(path);
                Assert.That(SavedMeta(path)["name"], Is.EqualTo(expectedName));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        // The fixture has no expression menu. The list-only export writes the
        // listed faces anyway; a repeated name is numbered like a repeated menu face.
        [TestCase(false)]
        [TestCase(true)]
        public void ListOnlyExportWritesTheListWithoutAMenu(bool repeatedName)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clips = Enumerable.Range(0, repeatedName ? 2 : 1).Select(_ => new AnimationClip { name = "Listed face" }).ToArray();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-listed-faces-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                // The two faces differ in value; identical faces would be written once.
                for (var index = 0; index < clips.Length; index++) AnimationUtility.SetEditorCurve(clips[index],
                    EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65 + index * 10));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, clips.Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                window.SaveVrm(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(File.ReadAllBytes(path)).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = expressions.TryGetValue("custom", out var value) ? (System.Collections.Generic.Dictionary<string, object>)value
                    : new System.Collections.Generic.Dictionary<string, object>();
                var status = (string)type.GetField("status", flags).GetValue(window);
                Assert.That(custom.Keys.Where(key => key.StartsWith("Listed face")),
                    Is.EquivalentTo(repeatedName ? new[] { "Listed face", "Listed face (2)" } : new[] { "Listed face" }));
                Assert.That(status, Does.Not.Contain(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortRecordedFormat), 0, clips.Length)));
                Assert.That(((System.Collections.ICollection)type.GetField("fileRecords", flags).GetValue(window)).Count, Is.Zero, "Nothing is recorded.");
                Assert.That(status, Does.Not.Contain(ExporterLocalization.T("表情を記録・更新しました: ")));
                Assert.That(((SavedExpressionVrm)type.GetField("saved", flags).GetValue(window)).Expressions, Is.EqualTo(clips.Length));
            }
            finally { Object.DestroyImmediate(window); foreach (var clip in clips) Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        // Recording the list throws: the menu result is exported again and
        // saved, with the error and the counts. A canceled save stays canceled.
        [Test]
        public void MenuShortSaveExportsTheMenuAgainWhenRecordingThrows()
        {
            var menu = new byte[] { 1 }; var again = new byte[] { 2 };
            var restored = 0;
            var result = ExperimentalExpressionCaptureWindow.ResolveMenuShort(menu, 1, 2, () => 3,
                () => throw new InvalidOperationException("Recording failed"), () => { restored++; return again; });
            Assert.That(result.Exported, Is.SameAs(again));
            Assert.That(restored, Is.EqualTo(1));
            Assert.That(result.ListedFaces, Is.EqualTo(3));
            Assert.That(result.Recorded, Is.Null);
            Assert.That(result.Error, Is.EqualTo("Recording failed"));
            Assert.That(result.Kept, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortNotRecordedFormat), 1, 3)));

            restored = 0;
            Assert.Throws<OperationCanceledException>(() => ExperimentalExpressionCaptureWindow.ResolveMenuShort(menu, 1, 2, () => 3,
                () => throw new OperationCanceledException(), () => { restored++; return again; }));
            Assert.That(restored, Is.Zero, "A canceled save is not exported again.");
        }

        // A recovery attempt decides the count again from the board at that
        // time; after the board closed it only gives the counts.
        [Test]
        public void MenuShortSaveDecidesAgainFromTheBoardAtThatTime()
        {
            var menu = new byte[] { 1 }; var recordedBytes = new byte[] { 2 };
            var calls = 0;
            byte[] Record() { calls++; return recordedBytes; }
            byte[] Again() { calls++; return menu; }

            var many = ExperimentalExpressionCaptureWindow.ResolveMenuShort(menu, 1, 2, () => 65, Record, Again);
            Assert.That(many.Exported, Is.SameAs(recordedBytes));
            Assert.That(many.ListedFaces, Is.EqualTo(65));
            Assert.That(many.Recorded, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortRecordedFormat), 1, 65)));
            Assert.That(many.Kept, Is.Null);
            Assert.That(many.Error, Is.Null);
            Assert.That(calls, Is.EqualTo(1), "A list over 64 faces is recorded too.");

            calls = 0;
            var recorded = ExperimentalExpressionCaptureWindow.ResolveMenuShort(menu, 1, 2, () => 4, Record, Again);
            Assert.That(recorded.Exported, Is.SameAs(recordedBytes));
            Assert.That(recorded.ListedFaces, Is.EqualTo(4));
            Assert.That(recorded.Recorded, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortRecordedFormat), 1, 4)));
            Assert.That(recorded.Kept, Is.Null);
            Assert.That(calls, Is.EqualTo(1));

            calls = 0;
            var closed = ExperimentalExpressionCaptureWindow.ResolveMenuShort(menu, 1, 2, null, Record, Again);
            Assert.That(closed.Exported, Is.SameAs(menu));
            Assert.That(closed.ListedFaces, Is.EqualTo(2));
            Assert.That(closed.Recorded, Is.Null);
            Assert.That(closed.Error, Is.Null);
            Assert.That(closed.Kept, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortNotRecordedFormat), 1, 2)));
            Assert.That(calls, Is.Zero, "A closed board records nothing.");
        }

        // The expression menu has two faces. The list-only export plays no menu
        // face: the VRM has exactly the listed ones, and the saved count is those.
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ListOnlyExportLeavesOutMenuFacesTheListDoesNotHave(int listed)
        {
            var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var folderName = "__ExperimentalCapture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); var folder = "Assets/" + folderName;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-menu-faces-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var names = new[] { "Face A", "Face B" };
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                controller.AddParameter("Face", AnimatorControllerParameterType.Int);
                var machine = controller.layers[0].stateMachine;
                var neutral = machine.AddState("Neutral"); neutral.writeDefaultValues = false; machine.defaultState = neutral;
                var clips = new AnimationClip[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    var state = machine.AddState(names[index]); state.writeDefaultValues = false;
                    clips[index] = new AnimationClip { name = names[index] };
                    AnimationUtility.SetEditorCurve(clips[index], EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                        AnimationCurve.Constant(0, 1, 75 - index * 20));
                    AssetDatabase.AddObjectToAsset(clips[index], controller); state.motion = clips[index];
                    var transition = neutral.AddTransition(state); transition.hasExitTime = false; transition.duration = 0;
                    transition.AddCondition(AnimatorConditionMode.Equals, index + 1, "Face");
                }
                var menu = ScriptableObject.CreateInstance(Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu"));
                AssetDatabase.CreateAsset(menu, folder + "/Menu.asset");
                using (var data = new SerializedObject(menu))
                {
                    var controls = data.FindProperty("controls"); controls.arraySize = names.Length;
                    for (var index = 0; index < names.Length; index++)
                    {
                        var item = controls.GetArrayElementAtIndex(index); item.FindPropertyRelative("name").stringValue = names[index];
                        var control = item.FindPropertyRelative("type"); control.enumValueIndex = Array.IndexOf(control.enumNames, "Toggle");
                        item.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = "Face";
                        item.FindPropertyRelative("value").floatValue = index + 1;
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customExpressions").boolValue = true; data.FindProperty("expressionsMenu").objectReferenceValue = menu;
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                    var layer = list.GetArrayElementAtIndex(0); var kind = layer.FindPropertyRelative("type");
                    kind.enumValueIndex = Array.IndexOf(kind.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, clips.Take(listed).Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.UsesRecordRoute, Is.False);
                Assert.That(board.OtherCount, Is.EqualTo(listed));
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = expressions.TryGetValue("custom", out var value) ? (System.Collections.Generic.Dictionary<string, object>)value
                    : new System.Collections.Generic.Dictionary<string, object>();
                // Two listed faces are written as "A" and "B": the shared "Face " is left out (ExpressionNameAffix).
                Assert.That(custom.Keys.Where(key => names.Any(name => name == key || name.EndsWith(" " + key))),
                    Is.EquivalentTo(ExpressionNameAffix.Trim(names.Take(listed).ToArray())));
                Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(listed), "Only the listed faces are registered; no menu face is played.");
                Assert.That(((System.Collections.ICollection)type.GetField("fileRecords", flags).GetValue(window)).Count, Is.Zero);
                var status = (string)type.GetField("status", flags).GetValue(window);
                Assert.That(status, Does.Not.Contain(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuExceedsListFormat), names.Length, listed)));
                var saved = (SavedExpressionVrm)type.GetField("saved", flags).GetValue(window);
                Assert.That(saved.Expressions, Is.EqualTo(listed), "The saved count is the listed faces.");
            }
            finally { Object.DestroyImmediate(window); if (File.Exists(path)) File.Delete(path); AssetDatabase.DeleteAsset(folder); }
        }

        // The expression menu gives two faces and the board lists them with a
        // still face and a looping face the menu does not have. The list-only
        // export reads all four from their clips as the ordinary export reads
        // gesture clips: the looping face keeps its animation.
        [Test]
        public void ListOnlyExportWritesListFacesTheMenuDoesNotHave()
        {
            var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var folderName = "__ExperimentalCapture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); var folder = "Assets/" + folderName;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var listOnly = new AnimationClip { name = "Listed only" };
            var moving = new AnimationClip { name = "Listed moving" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-menu-kept-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var names = new[] { "Face A", "Face B" };
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                controller.AddParameter("Face", AnimatorControllerParameterType.Int);
                var machine = controller.layers[0].stateMachine;
                var neutral = machine.AddState("Neutral"); neutral.writeDefaultValues = false; machine.defaultState = neutral;
                var clips = new AnimationClip[names.Length];
                for (var index = 0; index < names.Length; index++)
                {
                    var state = machine.AddState(names[index]); state.writeDefaultValues = false;
                    clips[index] = new AnimationClip { name = names[index] };
                    AnimationUtility.SetEditorCurve(clips[index], EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                        AnimationCurve.Constant(0, 1, 75 - index * 20));
                    AssetDatabase.AddObjectToAsset(clips[index], controller); state.motion = clips[index];
                    var transition = neutral.AddTransition(state); transition.hasExitTime = false; transition.duration = 0;
                    transition.AddCondition(AnimatorConditionMode.Equals, index + 1, "Face");
                }
                // A face only the board lists; its weight differs from both menu faces.
                AnimationUtility.SetEditorCurve(listOnly, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Constant(0, 1, 15));
                AnimationUtility.SetEditorCurve(moving, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Linear(0, 0, 1, 60));
                var settings = AnimationUtility.GetAnimationClipSettings(moving); settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(moving, settings);
                var menu = ScriptableObject.CreateInstance(Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu"));
                AssetDatabase.CreateAsset(menu, folder + "/Menu.asset");
                using (var data = new SerializedObject(menu))
                {
                    var controls = data.FindProperty("controls"); controls.arraySize = names.Length;
                    for (var index = 0; index < names.Length; index++)
                    {
                        var item = controls.GetArrayElementAtIndex(index); item.FindPropertyRelative("name").stringValue = names[index];
                        var control = item.FindPropertyRelative("type"); control.enumValueIndex = Array.IndexOf(control.enumNames, "Toggle");
                        item.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = "Face";
                        item.FindPropertyRelative("value").floatValue = index + 1;
                    }
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customExpressions").boolValue = true; data.FindProperty("expressionsMenu").objectReferenceValue = menu;
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                    var layer = list.GetArrayElementAtIndex(0); var kind = layer.FindPropertyRelative("type");
                    kind.enumValueIndex = Array.IndexOf(kind.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, clips.Append(listOnly).Append(moving).Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.UsesRecordRoute, Is.False);
                Assert.That(board.OtherCount, Is.EqualTo(names.Length + 2));
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = (System.Collections.Generic.Dictionary<string, object>)expressions["custom"];
                Assert.That(custom.Keys, Is.SupersetOf(names.Append(listOnly.name).Append(moving.name)));
                Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(names.Length + 2), "Every listed face is registered as the ordinary export registers its faces.");
                var document = VRVlog.Expressions.ExpressionAnimationData.ReadDocument(extensions[VRVlog.Expressions.ExpressionAnimationData.Extension]);
                var animation = document.Animations.Single();
                Assert.That(animation.Expression, Is.EqualTo(moving.name));
                Assert.That(animation.Duration, Is.EqualTo(1).Within(1e-6));
                Assert.That(animation.Loop, Is.True);
                Assert.That(((System.Collections.ICollection)type.GetField("fileRecords", flags).GetValue(window)).Count, Is.Zero);
                var status = (string)type.GetField("status", flags).GetValue(window);
                var line = string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuShortKeptMenuFormat), names.Length, names.Length + 2);
                Assert.That(status, Does.Not.Contain(line));
                var saved = (SavedExpressionVrm)type.GetField("saved", flags).GetValue(window);
                Assert.That(saved.Expressions, Is.EqualTo(names.Length + 2), "The saved count is the listed faces.");
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(listOnly); Object.DestroyImmediate(moving); if (File.Exists(path)) File.Delete(path); AssetDatabase.DeleteAsset(folder); }
        }

        // Without the VRChat SDK the list-only export still reads the listed
        // clips: a still face keeps its value and a looping face is written
        // to VRVLOG_expression_animations, ending on its last frame.
        [Test]
        public void ListOnlyExportKeepsAMovingFaceAnimation()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var still = new AnimationClip { name = "Still face" };
            var moving = new AnimationClip { name = "Moving face" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-list-moving-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
                AnimationUtility.SetEditorCurve(still, binding, AnimationCurve.Constant(0, 1, 40));
                AnimationUtility.SetEditorCurve(moving, binding, AnimationCurve.Linear(0, 0, 2, 80));
                var settings = AnimationUtility.GetAnimationClipSettings(moving); settings.loopTime = true;
                AnimationUtility.SetAnimationClipSettings(moving, settings);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, new[] { still, moving }.Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.UsesRecordRoute, Is.False);
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = (System.Collections.Generic.Dictionary<string, object>)expressions["custom"];
                // Both names end with " face", which the export leaves out (ExpressionNameAffix).
                Assert.That(custom.Keys, Is.SupersetOf(new[] { "Still", "Moving" }));
                Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(2));
                var document = VRVlog.Expressions.ExpressionAnimationData.ReadDocument(extensions[VRVlog.Expressions.ExpressionAnimationData.Extension]);
                var animation = document.Animations.Single();
                Assert.That(animation.Expression, Is.EqualTo("Moving"));
                Assert.That(animation.Duration, Is.EqualTo(2).Within(1e-6));
                Assert.That(animation.Loop, Is.True);
                Assert.That(animation.Channels.Single().Curve.Keys.Select(key => key.Value), Is.EqualTo(new[] { 0.0, 80.0 }));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(still); Object.DestroyImmediate(moving); if (File.Exists(path)) File.Delete(path); }
        }

        // A clip only a Motion Time state plays (a Radial Puppet's) stays one
        // face at the board's frame, as the menu export keeps it, instead of
        // playing through the radial's range.
        [Test]
        public void ListOnlyExportHoldsAMotionTimeFaceAtOneFrame()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var radial = new AnimationClip { name = "Radial face" };
            var controller = new UnityEditor.Animations.AnimatorController { name = "Radial" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-list-radial-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                AnimationUtility.SetEditorCurve(radial, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Linear(0, 0, 2, 80));
                controller.AddParameter("Radial", AnimatorControllerParameterType.Float);
                controller.AddLayer("Base");
                var state = controller.layers[0].stateMachine.AddState("Radial");
                state.motion = radial; state.timeParameterActive = true; state.timeParameter = "Radial";
                fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>
                    { new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = radial, Source = "Fixture" } });
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                Assert.That(((System.Collections.Generic.Dictionary<string, object>)expressions["custom"]).Keys, Does.Contain(radial.name));
                Assert.That(extensions.ContainsKey(VRVlog.Expressions.ExpressionAnimationData.Extension) &&
                    VRVlog.Expressions.ExpressionAnimationData.ReadDocument(extensions[VRVlog.Expressions.ExpressionAnimationData.Extension]).Animations.Any(), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window); Object.DestroyImmediate(radial);
                foreach (var layer in controller.layers) Object.DestroyImmediate(layer.stateMachine);
                Object.DestroyImmediate(controller);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // A moving face put in an emotion slot is the still preset and, as in
        // the ordinary export, also a custom face that keeps its animation.
        [Test]
        public void ListOnlyExportKeepsAMovingSlotFaceMovingAsACustomFace()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var moving = new AnimationClip { name = "Moving joy" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-list-slot-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                AnimationUtility.SetEditorCurve(moving, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Linear(0, 0, 2, 80));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>
                    { new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = moving, Source = "Fixture" } });
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.Move(moving, ExpressionBoard.Place.Slot(ExpressionPreset.happy)), Is.True);
                Assert.That(board.OtherCount, Is.Zero);
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                Assert.That(((System.Collections.Generic.Dictionary<string, object>)expressions["preset"]).Keys, Does.Contain("happy"));
                Assert.That(((System.Collections.Generic.Dictionary<string, object>)expressions["custom"]).Keys, Does.Contain(moving.name));
                var animation = VRVlog.Expressions.ExpressionAnimationData.ReadDocument(extensions[VRVlog.Expressions.ExpressionAnimationData.Extension]).Animations.Single();
                Assert.That(animation.Expression, Is.EqualTo(moving.name));
                Assert.That(animation.Duration, Is.EqualTo(2).Within(1e-6));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(moving); if (File.Exists(path)) File.Delete(path); }
        }

        // A still face a hand gesture enters, put in an emotion slot by its
        // name, is also a custom face: the gesture table names only custom
        // faces, so without it the gesture's cell was null.
        [Test]
        public void ListOnlyExportKeepsAStillGestureSlotFaceInTheGestureTable()
        {
            var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var folderName = "__ExperimentalCapture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); var folder = "Assets/" + folderName;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-list-gesture-slot-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                foreach (var parameter in new[] { "GestureLeft", "GestureRight" }) controller.AddParameter(parameter, AnimatorControllerParameterType.Int);
                var machine = controller.layers[0].stateMachine;
                var empty = new AnimationClip { name = "Empty" }; AssetDatabase.AddObjectToAsset(empty, controller);
                var nagomi = new AnimationClip { name = "Nagomi" };
                AnimationUtility.SetEditorCurve(nagomi, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Constant(0, 1, 90));
                AssetDatabase.AddObjectToAsset(nagomi, controller);
                var idle = machine.AddState("Idle"); idle.motion = empty; idle.writeDefaultValues = true; machine.defaultState = idle;
                var thumbsUp = machine.AddState("Thumbs up"); thumbsUp.motion = nagomi; thumbsUp.writeDefaultValues = true;
                foreach (var (state, value) in new[] { (idle, 0), (thumbsUp, 7) })
                {
                    var transition = machine.AddAnyStateTransition(state);
                    transition.hasExitTime = false; transition.duration = 0; transition.canTransitionToSelf = false;
                    transition.AddCondition(AnimatorConditionMode.Equals, value, "GestureLeft");
                }
                foreach (var value in new[] { 1, 2, 3, 4, 5, 6 })
                {
                    var transition = machine.AddAnyStateTransition(idle);
                    transition.hasExitTime = false; transition.duration = 0; transition.canTransitionToSelf = false;
                    transition.AddCondition(AnimatorConditionMode.Equals, value, "GestureLeft");
                }
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                    var layer = list.GetArrayElementAtIndex(0); var kind = layer.FindPropertyRelative("type");
                    kind.enumValueIndex = Array.IndexOf(kind.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                AssetDatabase.SaveAssets();
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>
                    { new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = nagomi, Source = "Fixture" } });
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                var slot = board.SlotValue(ExpressionPreset.relaxed);
                Assert.That(slot?.ResolveClip(), Is.SameAs(nagomi), "The name puts the face in the relaxed slot.");
                Assert.That(slot.Automatic, Is.True);
                Assert.That(board.OtherCount, Is.Zero);
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                Assert.That(((System.Collections.Generic.Dictionary<string, object>)expressions["preset"]).Keys, Does.Contain("relaxed"));
                Assert.That(((System.Collections.Generic.Dictionary<string, object>)expressions["custom"]).Keys, Does.Contain(nagomi.name));
                var table = VRVlog.Expressions.ExpressionGestureData.Read(extensions[VRVlog.Expressions.ExpressionGestureData.Extension]);
                for (var right = 0; right < VRVlog.Expressions.ExpressionGestureData.Gestures; right++)
                    Assert.That(table.Cells[VRVlog.Expressions.ExpressionGestureData.Cell(7, right)]?.Select(reference => reference.Expression), Is.EqualTo(new[] { nagomi.name }),
                        "Left thumbs up with right " + right);
            }
            finally { Object.DestroyImmediate(window); if (File.Exists(path)) File.Delete(path); AssetDatabase.DeleteAsset(folder); }
        }

        // A listed face on a mesh the build tools move is read where the mesh
        // now is, as the board's presets are, instead of being dropped.
        [Test]
        public void ListOnlyExportReadsAFaceOnAMeshTheBuildToolsMove()
        {
            if (AppDomain.CurrentDomain.GetAssemblies().All(assembly => assembly.GetType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy") == null))
                Assert.Ignore("Install Modular Avatar.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            MoveFrontWithInstalledMa(fixture.Source);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var still = new AnimationClip { name = "Still face" };
            var moving = new AnimationClip { name = "Moving face" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-list-moved-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
                AnimationUtility.SetEditorCurve(still, binding, AnimationCurve.Constant(0, 1, 40));
                AnimationUtility.SetEditorCurve(moving, binding, AnimationCurve.Linear(0, 0, 2, 80));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, new[] { still, moving }.Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                window.SaveVrm(path);
                var bytes = File.ReadAllBytes(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = (System.Collections.Generic.Dictionary<string, object>)expressions["custom"];
                // Both names end with " face", which the export leaves out (ExpressionNameAffix).
                Assert.That(custom.Keys, Is.SupersetOf(new[] { "Still", "Moving" }));
                Assert.That((System.Collections.IList)((System.Collections.Generic.Dictionary<string, object>)custom["Still"])["morphTargetBinds"], Is.Not.Empty);
                var animation = VRVlog.Expressions.ExpressionAnimationData.ReadDocument(extensions[VRVlog.Expressions.ExpressionAnimationData.Extension]).Animations.Single();
                Assert.That(animation.Expression, Is.EqualTo("Moving"));
                Assert.That(animation.Channels.Single().Curve.Keys.Select(key => key.Value), Is.EqualTo(new[] { 0.0, 80.0 }));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(still); Object.DestroyImmediate(moving); if (File.Exists(path)) File.Delete(path); }
        }

        // With a face moved to the excluded list the board records its list;
        // two files with the same name are both saved, the second numbered.
        [Test]
        public void RecordRouteSavesRepeatedNamesNumbered()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clips = new[] { new AnimationClip { name = "Twin face" }, new AnimationClip { name = "Twin face" }, new AnimationClip { name = "Other face" } };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-repeated-names-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                for (var index = 0; index < clips.Length; index++) AnimationUtility.SetEditorCurve(clips[index],
                    EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65 + index * 10));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, clips.Select(clip =>
                    new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                type.GetMethod("RebuildBoard", flags).Invoke(window, null);
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.Move(clips[2], ExpressionBoard.Place.Excluded), Is.True);
                Assert.That(board.UsesRecordRoute, Is.True);
                Assert.That(window.BlockReason(), Is.Null, "A repeated name does not stop the save.");
                window.SaveVrm(path);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(File.ReadAllBytes(path)).Json["extensions"];
                var expressions = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
                var custom = (System.Collections.Generic.Dictionary<string, object>)expressions["custom"];
                Assert.That(custom.Keys.Where(key => key.EndsWith(" face") || key.Contains(" face (")), Is.EquivalentTo(new[] { "Twin face", "Twin face (2)" }));
                // The record route counts the faces it recorded.
                Assert.That(((SavedExpressionVrm)type.GetField("saved", flags).GetValue(window)).Expressions, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(window); foreach (var clip in clips) Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        static System.Collections.Generic.Dictionary<string, object> SavedMeta(string path)
        {
            var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(File.ReadAllBytes(path)).Json["extensions"];
            var vrm = (System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"];
            return (System.Collections.Generic.Dictionary<string, object>)vrm["meta"];
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task AutomaticAuxiliaryExclusionIsConsistentWithOrWithoutRecordedFaces(bool recordFace, bool moveRendererWithMa)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var originalSkin = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var hiddenMaterial = new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback"));
            var auxiliary = new GameObject("Auxiliary fallback"); auxiliary.transform.SetParent(fixture.Source.transform, false);
            var hiddenSkin = auxiliary.AddComponent<SkinnedMeshRenderer>();
            hiddenSkin.sharedMesh = originalSkin.sharedMesh; hiddenSkin.sharedMaterial = hiddenMaterial;
            hiddenSkin.bones = originalSkin.bones; hiddenSkin.rootBone = originalSkin.rootBone;
            if (moveRendererWithMa) MoveFrontWithInstalledMa(fixture.Source);
            var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
            var face = FaceClip("Selected face", 65);
            Vrm10Instance imported = null;
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.Copy.transform.Find("Auxiliary fallback")?.GetComponent<Renderer>(), Is.Null);
                Assert.That(session.Copy.GetComponentsInChildren<Renderer>(true).Any(renderer =>
                    renderer.sharedMaterials.Any(material => material != null && material.shader == hiddenMaterial.shader)), Is.False);
                Assert.That(session.Channels.Any(channel => channel.Renderer.name == "Auxiliary fallback"), Is.False);
                imported = await Vrm10.LoadBytesAsync(session.Export("Auxiliary exclusion", "Tests", recordFace ? Listed(face) : Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }),
                    canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.name == "Auxiliary fallback"), Is.False);
                Assert.That(imported.GetComponentsInChildren<Renderer>(true), Is.Not.Empty);
                if (recordFace) Assert.That(imported.Vrm.Expression.CustomClips.Any(clip => clip.name == "Selected face"), Is.True);
                Assert.That(hiddenSkin != null && hiddenSkin.sharedMaterial == hiddenMaterial, Is.True);
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(hiddenMaterial); Object.DestroyImmediate(face); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RecordedFacesRetainAuthoredMouthAndGazePresetsAfterReload(bool moveRendererWithMa)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 0);
            var presets = new[] { ExpressionPreset.aa, ExpressionPreset.ih, ExpressionPreset.ou, ExpressionPreset.ee, ExpressionPreset.oh,
                ExpressionPreset.lookUp, ExpressionPreset.lookDown, ExpressionPreset.lookLeft, ExpressionPreset.lookRight };
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            var clips = presets.Select(preset => {
                var clip = ScriptableObject.CreateInstance<VRM10Expression>();
                clip.name = "Authored " + preset; clip.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .8f) };
                authored.Expression.AddClip(preset, clip); return clip;
            }).ToArray();
            var face = FaceClip("Selected face", 65);
            Vrm10Instance imported = null;
            try
            {
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                if (moveRendererWithMa) MoveFrontWithInstalledMa(fixture.Source);
                var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                imported = await Vrm10.LoadBytesAsync(session.Export("Functional presets", "Tests", Listed(face), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }),
                    canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                var reloaded = imported.Vrm.Expression.Clips.ToDictionary(pair => pair.Preset, pair => pair.Clip);
                foreach (var preset in presets)
                {
                    Assert.That(reloaded.ContainsKey(preset), Is.True, preset.ToString());
                    Assert.That(reloaded[preset].MorphTargetBindings, Is.Not.Empty);
                }
                var skin = VrChatExpressionSampler.FindRenderer(imported.gameObject, reloaded[ExpressionPreset.aa].MorphTargetBindings[0].RelativePath);
                using var baked = new BlinkTestMesh();
                imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, 0); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh); var before = baked.Mesh.vertices;
                imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, 1); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f));
                imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, 0); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); foreach (var clip in clips) Object.DestroyImmediate(clip); Object.DestroyImmediate(authored); Object.DestroyImmediate(face); }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task ManualBlinkConfigurationFromPrimaryWindowSurvivesModelAndRecordedFaceSave(bool recordFace, bool moveRendererWithMa)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var sourceSkin = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(); sourceSkin.SetBlendShapeWeight(0, 0);
            if (moveRendererWithMa) MoveFrontWithInstalledMa(fixture.Source);
            var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-manual-blink-" + Guid.NewGuid().ToString("N") + ".vrm");
            var clip = new AnimationClip { name = "Selected face" }; Vrm10Instance imported = null;
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("author", flags).SetValue(window, "Tests");
                type.GetMethod("Prepare", flags).Invoke(window, null);
                if (recordFace)
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
                    type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                }
                type.GetMethod("ShowBlinkConfiguration", flags).Invoke(window, null);
                var dialog = Resources.FindObjectsOfTypeAll<BlinkConfigurationWindow>().Single();
                dialog.Options.SelectMode(BlinkExportMode.Manual);
                dialog.Options.Both[0].Renderer = sourceSkin; dialog.Options.Both[0].Shape = sourceSkin.sharedMesh.GetBlendShapeName(0); dialog.Options.Both[0].Weight = 80;
                dialog.ApplySettings();
                window.SaveVrm(path);
                imported = await Vrm10.LoadBytesAsync(File.ReadAllBytes(path), canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported.Vrm.Expression.Blink?.MorphTargetBindings, Is.Not.Empty);
                var skin = VrChatExpressionSampler.FindRenderer(imported.gameObject, imported.Vrm.Expression.Blink.MorphTargetBindings[0].RelativePath);
                using var baked = new BlinkTestMesh();
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh); var before = baked.Mesh.vertices;
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f));
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(window); Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ReadWriteDisabledFaceMeshesCanBePreparedExportedAndReimported(bool recordFace)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var meshes = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.sharedMesh).Distinct().ToArray();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            foreach (var mesh in meshes) mesh.UploadMeshData(true);
            Assert.That(meshes.All(mesh => !mesh.isReadable), Is.True);
            var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
            var face = FaceClip("Read only face", 65);
            Vrm10Instance imported = null;
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var bytes = session.Export("Read only meshes", "Tests", recordFace ? Listed(face) : Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                if (recordFace) Assert.That(imported.Vrm.Expression.CustomClips.Any(item => item.name == "Read only face"), Is.True);
                Assert.That(meshes.All(mesh => !mesh.isReadable), Is.True);
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(face); }
        }

        [TestCase(.25f)]
        [TestCase(2f)]
        public void EditedClipEndpointIsRefreshedForBothPreviewAndSaving(float newLength)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clip = new AnimationClip { name = "Edited face" };
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            try
            {
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0, 10, 1, 70));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetMethod("AddClip", flags).Invoke(window, new object[] { clip });
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Linear(0, 10, newLength, 95));
                type.GetMethod("PreviewClipInput", flags).Invoke(window, new object[] { inputs.Single() });
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.Channels.First(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(95));
                inputs.Single().Time = 1; // Saving must refresh independently of preview.
                type.GetMethod("RecordBoardClips", flags).Invoke(window, null);
                Assert.That(session.Expressions.Single().Rows.First(row => row.Path == "Front").Weights[0], Is.EqualTo(95));
                Assert.That(inputs.Single().Time, Is.EqualTo(newLength));
            }
            finally { Object.DestroyImmediate(window); Object.DestroyImmediate(clip); }
        }

        [TestCase("descriptor", false, false)]
        [TestCase("descriptor", true, false)]
        [TestCase("vrm", false, false)]
        [TestCase("vrm", true, false)]
        [TestCase("descriptor", false, true)]
        [TestCase("descriptor", true, true)]
        [TestCase("vrm", false, true)]
        [TestCase("vrm", true, true)]
        public async Task ExplicitBlinkMetadataSurvivesModelAndRecordedFaceExport(string configuration, bool recordFace, bool moveRendererWithMa)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            var blink = ScriptableObject.CreateInstance<VRM10Expression>();
            var face = FaceClip("Selected face", 65);
            Vrm10Instance imported = null;
            try
            {
                var sourceSkin = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                sourceSkin.SetBlendShapeWeight(0, 0);
                if (configuration == "vrm")
                {
                    blink.name = "Authored blink";
                    blink.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, 1f) };
                    authored.Expression.Blink = blink; fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                }
                else
                {
                    var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(value => value != null);
                    if (type == null) Assert.Ignore("Install the real VRChat SDK.");
                    var descriptor = fixture.Source.AddComponent(type);
                    type.GetField("enableEyeLook").SetValue(descriptor, true);
                    var field = type.GetField("customEyeLookSettings"); var settings = field.GetValue(descriptor);
                    var lidType = settings.GetType().GetField("eyelidType"); lidType.SetValue(settings, Enum.Parse(lidType.FieldType, "Blendshapes"));
                    settings.GetType().GetField("eyelidsSkinnedMesh").SetValue(settings, sourceSkin);
                    settings.GetType().GetField("eyelidsBlendshapes").SetValue(settings, new[] { 0, -1, -1 });
                    field.SetValue(descriptor, settings);
                }
                if (moveRendererWithMa) MoveFrontWithInstalledMa(fixture.Source);
                var sourceBefore = ExportSourceFingerprint.Compute(fixture.Source);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var bytes = session.Export("Explicit blink", "Tests", recordFace ? Listed(face) : Listed());
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported.Vrm.Expression.Blink, Is.Not.Null);
                Assert.That(imported.Vrm.Expression.Blink.MorphTargetBindings, Is.Not.Empty);
                var outputSkin = VrChatExpressionSampler.FindRenderer(imported.gameObject, imported.Vrm.Expression.Blink.MorphTargetBindings[0].RelativePath);
                var before = new Vector3[outputSkin.sharedMesh.vertexCount];
                using (var baked = new BlinkTestMesh())
                {
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh); before = baked.Mesh.vertices;
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh);
                    Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f), "Configured eyelids must still deform after VRM reload.");
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process(); outputSkin.BakeMesh(baked.Mesh);
                    Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                }
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(sourceBefore));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(blink); Object.DestroyImmediate(authored); Object.DestroyImmediate(face); }
        }

        sealed class BlinkTestMesh : IDisposable
        {
            internal readonly Mesh Mesh = new Mesh();
            public void Dispose() => Object.DestroyImmediate(Mesh);
        }

        static void MoveFrontWithInstalledMa(GameObject source)
        {
            var proxyType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy")).FirstOrDefault(type => type != null);
            var markerType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot")).FirstOrDefault(type => type != null);
            Assert.That(proxyType, Is.Not.Null);
            if (markerType == null) Assert.Ignore("NDMF before 1.8.0 has no NDMFAvatarRoot, and this fixture has no VRChat descriptor to identify the avatar.");
            if (source.GetComponent(markerType) == null) source.AddComponent(markerType);
            var proxy = source.transform.Find("Front").gameObject.AddComponent(proxyType);
            proxyType.GetProperty("target").SetValue(proxy, source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head));
            var mode = proxyType.GetField("attachmentMode"); mode.SetValue(proxy, Enum.Parse(mode.FieldType, "AsChildKeepWorldPose"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ModelOnlyExportReimportsWithoutRequiringFacialFiles(bool withAuthoredExpression)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skin.sharedMaterial.shader = Shader.Find("lilToon");
                skin.sharedMaterial.SetFloat("_UseAudioLink", 1);
            }
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            var face = ScriptableObject.CreateInstance<VRM10Expression>();
            Vrm10Instance imported = null;
            try
            {
                if (withAuthoredExpression)
                {
                    face.name = "Authored face";
                    face.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .8f) };
                    authored.Expression.CustomClips.Add(face);
                    fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                }
                else fixture.Mesh.ClearBlendShapes();
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.Expressions, Is.Empty);
                var before = ExportSourceFingerprint.Compute(fixture.Source);
                var bytes = session.Export("Model only", "Tests", Listed(), blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                Assert.That(imported.Vrm.Meta.Authors, Does.Contain("Tests"));
                Assert.That(imported.Vrm.Expression.CustomClips.Any(clip => clip.name == "Authored face"), Is.EqualTo(withAuthoredExpression));
                Assert.That(imported.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null && material.HasProperty("_UseAudioLink"))
                    .All(material => material.GetFloat("_UseAudioLink") == 0), Is.True);
                Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(before));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); Object.DestroyImmediate(face); Object.DestroyImmediate(authored); }
        }

        [Test]
        public void BatchClipValidationKeepsPerFileErrorsAndRejectsChangedSource()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var face = new AnimationClip(); var wrong = new AnimationClip();
            try
            {
                AnimationUtility.SetEditorCurve(face, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 75));
                AnimationUtility.SetEditorCurve(wrong, EditorCurveBinding.FloatCurve("Front", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, 1));
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var weights = session.Channels.Select(channel => channel.Renderer.GetBlendShapeWeight(0)).ToArray();
                var errors = session.ClipErrors(new[] { face, wrong, face });
                Assert.That(errors.Count, Is.EqualTo(2)); Assert.That(errors[face], Is.Null); Assert.That(errors[wrong], Is.Not.Null);
                Assert.That(session.Channels.Select(channel => channel.Renderer.GetBlendShapeWeight(0)).ToArray(), Is.EqualTo(weights));
                var vertices = fixture.Mesh.vertices; vertices[0].x += .1f; fixture.Mesh.vertices = vertices;
                Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(() => session.ClipErrors(new[] { face, wrong }));
            }
            finally { Object.DestroyImmediate(face); Object.DestroyImmediate(wrong); }
        }

        [TestCase("ja", "アプリに転送（通常）", "VRMを保存")]
        [TestCase("en", "Send to app (standard)", "Save VRM")]
        [TestCase("ko", "앱으로 전송 (일반)", "VRM 저장")]
        [TestCase("zh-Hans", "传输到应用（标准）", "保存 VRM")]
        [TestCase("zh-Hant", "傳送到 App（標準）", "儲存 VRM")]
        public void PrimaryWindowUsesInstalledLocaleTables(string locale, string title, string save)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var localeField = typeof(ExporterLocalization).GetField("_locale", flags);
            var messagesField = typeof(ExporterLocalization).GetField("_messages", flags);
            var previousLocale = localeField.GetValue(null); var previousMessages = messagesField.GetValue(null);
            ExperimentalExpressionCaptureWindow window = null;
            try
            {
                localeField.SetValue(null, locale); messagesField.SetValue(null, null);
                ExperimentalExpressionCaptureWindow.OpenForTransfer(null); window = Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Single();
                Assert.That(window.titleContent.text, Is.EqualTo(title));
                Assert.That(window.titleContent.image, Is.Null);
                Assert.That(ExporterLocalization.T("VRMを保存"), Is.EqualTo(save));
                Assert.That(ExporterLocalization.T("その他の表情"), Is.Not.Empty);
                if (locale != "ja") Assert.That(ExporterLocalization.T("その他の表情"), Is.Not.EqualTo("その他の表情"));
            }
            finally { window?.Close(); localeField.SetValue(null, previousLocale); messagesField.SetValue(null, previousMessages); }
        }

        // The logo is drawn in the header at the top of the window, not on the tab.
        // Closing a window keeps the shared texture, and after a script reload the
        // same texture is found again instead of being made twice.
        [Test]
        public void HeaderLogoIsSharedAndSurvivesScriptReload()
        {
            var icon = ExporterWindowIcon.Texture;
            Assert.That(icon, Is.Not.Null);
            Assert.That((icon.width, icon.height), Is.EqualTo((128, 128)));
            Assert.That(ExporterWindowHeader.Height, Is.EqualTo(61f));
            ExperimentalExpressionCaptureWindow window = null;
            try
            {
                ExperimentalExpressionCaptureWindow.OpenForTransfer(null); window = Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Single();
                window.Repaint();
                Assert.That(window.titleContent.image, Is.Null);
            }
            finally { window?.Close(); }
            Assert.That(icon != null, "closing a window must not destroy the shared icon");
            typeof(ExporterWindowIcon).GetField("texture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, null);
            Assert.That(ExporterWindowIcon.Texture, Is.SameAs(icon));
        }

        [Test]
        public void AudioLinkSuppressionCoversSharedInactiveAndNewMaterialsWithoutEditingTheSource()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var materials = new System.Collections.Generic.List<Material>();
            var first = new Material(Shader.Find("lilToon"));
            var generated = new Material(first);
            try
            {
                first.SetFloat("_UseAudioLink", 1); first.SetFloat("_AudioLink2Vertex", 1);
                generated.shader = Shader.Find("_lil/lilToonMulti"); Assert.That(generated.shader, Is.Not.Null);
                generated.SetFloat("_UseAudioLink", 1); generated.SetFloat("_AudioLink2Emission", 1);
                generated.EnableKeyword("_MAPPING_6_FRAMES_LAYOUT"); generated.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = first;
                foreach (var skin in fixture.Skins) skin.sharedMaterials = new[] { first, first, null };
                fixture.Skins[1].gameObject.SetActive(false);
                UniVrmOneClickExporter.DisableAudioLinkOnCopy(fixture.Copy, materials);
                Assert.That(materials.Count, Is.EqualTo(1));
                foreach (var skin in fixture.Skins)
                {
                    Assert.That(skin.sharedMaterials[0], Is.SameAs(materials[0]));
                    Assert.That(skin.sharedMaterials[1], Is.SameAs(materials[0]));
                    Assert.That(skin.sharedMaterials[2], Is.Null);
                    Assert.That(skin.sharedMaterial.GetFloat("_UseAudioLink"), Is.Zero);
                    Assert.That(skin.sharedMaterial.GetFloat("_AudioLink2Vertex"), Is.Zero);
                }
                // Simulate an authoring pass assigning a fresh material, and
                // another renderer restoring its source material reference.
                fixture.Skins[0].sharedMaterial = generated;
                fixture.Skins[1].sharedMaterial = first;
                UniVrmOneClickExporter.DisableAudioLinkOnCopy(fixture.Copy, materials);
                Assert.That(materials.Count, Is.EqualTo(3));
                Assert.That(fixture.Skins.All(skin => skin.sharedMaterial.GetFloat("_UseAudioLink") == 0), Is.True);
                Assert.That(fixture.Skins[0].sharedMaterial.GetFloat("_AudioLink2Emission"), Is.Zero);
                Assert.That(fixture.Skins[0].sharedMaterial.IsKeywordEnabled("_MAPPING_6_FRAMES_LAYOUT"), Is.False);
                Assert.That(fixture.Skins[0].sharedMaterial.IsKeywordEnabled("_SUNDISK_HIGH_QUALITY"), Is.False);
                UniVrmOneClickExporter.DisableAudioLinkOnCopy(fixture.Copy, materials);
                Assert.That(materials.Count, Is.EqualTo(3), "Repeated passes must not create extra material copies.");
                Assert.That(first.GetFloat("_UseAudioLink"), Is.EqualTo(1));
                Assert.That(first.GetFloat("_AudioLink2Vertex"), Is.EqualTo(1));
                Assert.That(generated.GetFloat("_UseAudioLink"), Is.EqualTo(1));
                Assert.That(generated.IsKeywordEnabled("_MAPPING_6_FRAMES_LAYOUT"), Is.True);
                // A stale Multi keyword must also be disabled when all toggles
                // already say off; the original still retains its keyword.
                generated.SetFloat("_UseAudioLink", 0); generated.SetFloat("_AudioLink2Emission", 0);
                fixture.Skins[0].sharedMaterial = generated;
                UniVrmOneClickExporter.DisableAudioLinkOnCopy(fixture.Copy, materials);
                Assert.That(materials.Count, Is.EqualTo(4));
                Assert.That(fixture.Skins[0].sharedMaterial.IsKeywordEnabled("_MAPPING_6_FRAMES_LAYOUT"), Is.False);
                Assert.That(generated.IsKeywordEnabled("_MAPPING_6_FRAMES_LAYOUT"), Is.True);
                Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().All(skin => skin.sharedMaterial == first), Is.True);
            }
            finally
            {
                foreach (var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(first); Object.DestroyImmediate(generated);
            }
        }

        [TestCase("lilToon")]
        [TestCase("_lil/lilToonMulti")]
        public async Task ExplicitExportWithAudioLinkEnabledReimportsWithoutChangingThePreviewOrWarning(string shaderName)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
            var extra = new Material(Shader.Find(shaderName));
            var smile = FaceClip("Smile", 90);
            AnimationUtility.SetEditorCurve(smile, EditorCurveBinding.FloatCurve("Back", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 0));
            Vrm10Instance imported = null;
            try
            {
                skins[0].sharedMaterial.shader = Shader.Find(shaderName);
                skins[0].sharedMaterial.SetFloat("_UseAudioLink", 1);
                skins[0].sharedMaterial.SetFloat("_AudioLink2Vertex", 1);
                extra.SetFloat("_UseAudioLink", 1); extra.SetFloat("_AudioLink2Emission", 1);
                if (shaderName == "_lil/lilToonMulti")
                    foreach (var material in new[] { skins[0].sharedMaterial, extra })
                    {
                        material.EnableKeyword("_MAPPING_6_FRAMES_LAYOUT"); material.EnableKeyword("_SUNDISK_HIGH_QUALITY");
                    }
                skins[1].sharedMaterial = extra;
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var warnings = new System.Collections.Generic.List<string>();
                var bytes = session.Export("Test", "Tests", Listed(smile), warnings, new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(warnings.Any(warning => warning.IndexOf("AudioLink", StringComparison.OrdinalIgnoreCase) >= 0), Is.False);
                Assert.That(session.Channels.All(channel => channel.Renderer.sharedMaterial.GetFloat("_UseAudioLink") == 1), Is.True);
                Assert.That(skins.All(skin => skin.sharedMaterial.GetFloat("_UseAudioLink") == 1), Is.True);
                using (var data = new GlbBinaryParser(bytes, "audiolink-copy.vrm").Parse())
                {
                    Assert.That(data.Json.Contains("_UseAudioLink"), Is.False, "External audio controls must not be emitted into the lilToon payload.");
                    Assert.That(data.Json.Contains("_MAPPING_6_FRAMES_LAYOUT"), Is.False);
                    Assert.That(data.Json.Contains("_SUNDISK_HIGH_QUALITY"), Is.False);
                }
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
                var expression = imported.Vrm.Expression.CustomClips.Single(clip => clip.name == "Smile");
                Assert.That(expression.MorphTargetBindings.Length, Is.EqualTo(2));
                imported.Runtime.Expression.SetWeight(ExpressionKey.CreateCustom(expression.name), 1);
                imported.Runtime.Process();
                Assert.That(imported.GetComponentsInChildren<SkinnedMeshRenderer>().Any(skin =>
                    Enumerable.Range(0, skin.sharedMesh.blendShapeCount).Any(index => skin.GetBlendShapeWeight(index) > 0)), Is.True);
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(extra); Object.DestroyImmediate(smile);
            }
        }

        [Test]
        public void RecordingAndSettingsRoundTripKeepIndependentMeshesAndSourceUntouched()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var before = fixture.Mesh.vertices;
            string json;
            using (var session = new ExperimentalExpressionCaptureSession(fixture.Source))
            {
                Assert.That(session.Channels.Count, Is.EqualTo(2));
                Record(session, "笑顔", 80, 0);
                json = session.SaveSettings();
            }
            // A fresh session has different Unity object identities. Settings resolve exact mesh content instead.
            using (var session = new ExperimentalExpressionCaptureSession(fixture.Source))
            {
                session.LoadSettings(json);
                Assert.That(session.Expressions.Single().Name, Is.EqualTo("笑顔"));
                Assert.That(session.Expressions.Single().Rows.Select(row => row.Weights[0]), Is.EqualTo(new[] { 80f, 0f }));
            }
            Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.GetBlendShapeWeight(0)), Is.EqualTo(new[] { 35f, 35f }));
            Assert.That(fixture.Mesh.vertices, Is.EqualTo(before));
            Assert.That(fixture.Mesh.blendShapeCount, Is.EqualTo(1));
        }

        [TestCase("duplicate")]
        [TestCase("missing-row")]
        [TestCase("wrong-shape")]
        [TestCase("wrong-mesh")]
        [TestCase("nonfinite")]
        [TestCase("wrong-version")]
        public void InvalidSettingsLeaveBaselinePreviewAndRecordedFacesUnchanged(string kind)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source);
            Record(session, "元の記録", 75);
            var settings = JsonUtility.FromJson<ExperimentalExpressionCaptureSession.Settings>(session.SaveSettings());
            switch (kind)
            {
                case "duplicate": settings.Expressions.Add(settings.Expressions[0]); break;
                case "missing-row": settings.Expressions[0].Rows.RemoveAt(0); break;
                case "wrong-shape": settings.Expressions[0].Rows[0].Shapes[0] = "different"; break;
                case "wrong-mesh": settings.MeshHashes[0] = "different"; break;
                case "nonfinite": settings.Expressions[0].Rows[0].Weights[0] = float.NaN; break;
                case "wrong-version": settings.Version = 999; break;
            }
            Assert.Throws<InvalidOperationException>(() => session.LoadSettings(JsonUtility.ToJson(settings)));
            Assert.That(session.Expressions.Single().Name, Is.EqualTo("元の記録"));
            Assert.That(session.Baseline.Rows[0].Weights[0], Is.EqualTo(35));
            Assert.That(session.Channels[0].Renderer.GetBlendShapeWeight(0), Is.EqualTo(35));
        }

        [Test]
        public void SourceMeshEditInvalidatesTheSessionAndOldSettingsWithoutMutatingPreview()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            string json;
            using (var session = new ExperimentalExpressionCaptureSession(fixture.Source))
            {
                Record(session, "face", 70); json = session.SaveSettings();
                var vertices = fixture.Mesh.vertices; vertices[0].x += .1f; fixture.Mesh.vertices = vertices;
                Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(() => session.Export("Test", "Tests", Listed()));
                Assert.That(session.Expressions.Count, Is.EqualTo(1));
                Assert.That(session.SaveSettings(), Is.EqualTo(json), "Source edits must not prevent saving recovery settings.");
            }
            using (var replacement = new ExperimentalExpressionCaptureSession(fixture.Source))
                Assert.Throws<InvalidOperationException>(() => replacement.LoadSettings(json));
        }

        // A face listed by its clip exports from an avatar with a menu and a
        // descriptor, and the source stays as it was. Listing a clip does not
        // play the menu, so the controller check only guards against that.
        [TestCase("listed")]
        [TestCase("audio-reactive")]
        public async Task ExistingMenuFaceExportsFromTheBoardListWithoutChangingItsController(string callback)
        {
            var descriptorType = Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            // VRCAnimatorPlayAudio first ships in VRChat SDK 3.5.2; 3.5.0 and 3.5.1 have no audio behaviour to omit.
            if (callback == "audio-reactive" && Sdk("VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio") == null)
                Assert.Ignore("VRCAnimatorPlayAudio is in VRChat SDK 3.5.2 and later.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folderName = "__ExperimentalCapture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); var folder = "Assets/" + folderName;
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                controller.AddParameter("Face", AnimatorControllerParameterType.Int);
                var machine = controller.layers[0].stateMachine;
                var neutral = machine.AddState("Neutral"); neutral.writeDefaultValues = false;
                var smile = machine.AddState("Smile"); smile.writeDefaultValues = false;
                if (callback == "audio-reactive") smile.AddStateMachineBehaviour(Sdk("VRC.SDK3.Avatars.Components.VRCAnimatorPlayAudio"));
                var clip = new AnimationClip();
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 75));
                AssetDatabase.AddObjectToAsset(clip, controller); smile.motion = clip; machine.defaultState = neutral;
                var transition = neutral.AddTransition(smile); transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.Equals, 1, "Face");
                var menu = ScriptableObject.CreateInstance(Sdk("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu"));
                AssetDatabase.CreateAsset(menu, folder + "/Menu.asset");
                using (var data = new SerializedObject(menu))
                {
                    var controls = data.FindProperty("controls"); controls.arraySize = 1;
                    var item = controls.GetArrayElementAtIndex(0); item.FindPropertyRelative("name").stringValue = "Smile";
                    var type = item.FindPropertyRelative("type"); type.enumValueIndex = Array.IndexOf(type.enumNames, "Toggle");
                    item.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = "Face";
                    item.FindPropertyRelative("value").floatValue = 1; data.ApplyModifiedPropertiesWithoutUndo();
                }
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customExpressions").boolValue = true; data.FindProperty("expressionsMenu").objectReferenceValue = menu;
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                    var layer = list.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                if (callback == "audio-reactive")
                {
                    var material = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
                    material.shader = Shader.Find("lilToon"); material.SetFloat("_UseAudioLink", 1);
                    Assert.Throws<InvalidOperationException>(() => new ExperimentalExpressionCaptureSession(fixture.Source));
                    Assert.That(smile.behaviours.Length, Is.EqualTo(1), "Refusing an audio-reactive face cannot remove the source callback.");
                    return;
                }
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source);
                var original = ExportSourceFingerprint.Compute(fixture.Source);
                var listed = new[] { ("Smile", clip, ExperimentalExpressionCaptureWindow.DefaultTime(clip)) };
                var bytes = session.Export("Existing menu", "Tests", listed, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var model = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                try
                {
                    var face = model.Vrm.Expression.CustomClips.Single(item => item.name.EndsWith("Smile", StringComparison.Ordinal));
                    var skin = model.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                    var key = ExpressionKey.CreateCustom(face.name);
                    using var baked = new BlinkTestMesh();
                    model.Runtime.Expression.SetWeight(key, 0); model.Runtime.Process(); skin.BakeMesh(baked.Mesh); var before = baked.Mesh.vertices;
                    model.Runtime.Expression.SetWeight(key, 1); model.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                    Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.GreaterThan(1e-8f));
                    model.Runtime.Expression.SetWeight(key, 0); model.Runtime.Process(); skin.BakeMesh(baked.Mesh);
                    Assert.That(baked.Mesh.vertices.Zip(before, (a,b) => (a-b).sqrMagnitude).Max(), Is.LessThan(1e-8f));
                    Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(original));
                    Assert.That(controller.layers[0].stateMachine.states.Length, Is.EqualTo(2));
                }
                finally { Object.DestroyImmediate(model.gameObject); }
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [TestCase("constant")]
        [TestCase("moving")]
        [TestCase("neutral")]
        public void ExplicitClipsMatchNativeValuesWithoutEvaluatingUnrelatedControllers(string mode)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var clip = new AnimationClip { name = "Authored face" };
            var controller = new AnimatorController();
            var machine = new AnimatorStateMachine(); var state = machine.AddState("Unknown unrelated idle");
            state.AddStateMachineBehaviour<UnknownStateCallbackProbe>();
            controller.layers = new[] { new AnimatorControllerLayer { name = "Unrelated tail", stateMachine = machine } };
            fixture.Source.GetComponent<Animator>().runtimeAnimatorController = controller;
            var tail = new GameObject("Tail"); tail.transform.SetParent(fixture.Source.transform);
            tail.transform.localRotation = Quaternion.Euler(0, 0, 359.9999f);
            var weight = mode == "neutral" ? 35 : 75;
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                mode == "moving" ? AnimationCurve.Linear(0, 35, 1, 75) : AnimationCurve.Constant(0, 1, weight));
            GameObject native = null;
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.RecommendClips(), Is.Empty, "A name does not make an unreferenced clip a recommendation.");
                session.Channels[1].Renderer.SetBlendShapeWeight(0, 99);
                session.PreviewClip(clip, .5f);
                native = Object.Instantiate(fixture.Source); native.GetComponent<Animator>().runtimeAnimatorController = null;
                clip.SampleAnimation(native, .5f);
                Assert.That(session.Channels.First(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0),
                    Is.EqualTo(native.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0)).Within(.001));
                Assert.That(session.Channels.First(channel => channel.Path == "Back").Renderer.GetBlendShapeWeight(0), Is.EqualTo(35), "Unspecified curves always start from the baseline.");
                var inputs = new[] { new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "Explicit", Time = .5f } };
                Assert.That(session.ReplaceClipRecords(inputs, null).Select(pose => pose.Name), Is.EqualTo(new[] { "Explicit" }));
                session.LoadSettings(session.SaveSettings()); Assert.That(session.Expressions.Single().Name, Is.EqualTo("Explicit"));
                Assert.That(fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0), Is.EqualTo(35));
                Assert.That(state.behaviours.Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(native); Object.DestroyImmediate(clip); Object.DestroyImmediate(controller); Object.DestroyImmediate(machine); }
        }

        [Test]
        public void ImportingOneHundredClipsRecordsEveryFace()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var clips = Enumerable.Range(0, 100).Select(index =>
            {
                var clip = new AnimationClip { name = "Face " + index };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"),
                    AnimationCurve.Constant(0, 1, index));
                return clip;
            }).ToArray();
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var inputs = clips.Select(clip => new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = clip.name, Time = 0 }).ToArray();
                var result = session.ReplaceClipRecords(inputs, null);
                Assert.That(result, Has.Length.EqualTo(100), "There is no limit on the number of recorded faces.");
                Assert.That(session.Expressions.Select(pose => pose.Name), Is.EqualTo(clips.Select(clip => clip.name)));
            }
            finally { foreach (var clip in clips) Object.DestroyImmediate(clip); }
        }

        // A bone curve is no error here: the export writes it beside the face.
        [TestCase("material")]
        [TestCase("display")]
        [TestCase("reference")]
        [TestCase("missing")]
        [TestCase("event")]
        [TestCase("wrong-shape")]
        [TestCase("bad-time")]
        public void ExplicitClipErrorsNeverPartiallyRecordOrAlterThePreview(string mode)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var valid = new AnimationClip(); var invalid = new AnimationClip();
            var binding = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");
            AnimationUtility.SetEditorCurve(valid, binding, AnimationCurve.Constant(0, 1, 80));
            AnimationUtility.SetEditorCurve(invalid, binding, AnimationCurve.Constant(0, 1, 65));
            if (mode == "material") AnimationUtility.SetEditorCurve(invalid, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "material._Color.r"), AnimationCurve.Constant(0, 1, .3f));
            if (mode == "display") AnimationUtility.SetEditorCurve(invalid, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "m_Enabled"), AnimationCurve.Constant(0, 1, 0));
            if (mode == "reference") AnimationUtility.SetObjectReferenceCurve(invalid, EditorCurveBinding.PPtrCurve("Front", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"), new[] { new ObjectReferenceKeyframe { time = 0, value = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial } });
            if (mode == "missing" || mode == "wrong-shape") AnimationUtility.SetEditorCurve(invalid,
                EditorCurveBinding.FloatCurve(mode == "missing" ? "Missing" : "Front", typeof(SkinnedMeshRenderer), mode == "wrong-shape" ? "blendShape.Missing" : "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 65));
            // Absent shapes are skipped next to real ones, so the face must have nothing else.
            if (mode == "wrong-shape") AnimationUtility.SetEditorCurve(invalid, binding, null);
            if (mode == "event") AnimationUtility.SetAnimationEvents(invalid, new[] { new AnimationEvent { functionName = "MustNeverRun", time = .1f } });
            try
            {
                session.Channels[0].Renderer.SetBlendShapeWeight(0, 50);
                var time = mode == "bad-time" ? float.NaN : 0;
                Assert.That(session.ClipError(invalid, time), Is.Not.Null);
                Assert.Throws<InvalidOperationException>(() => session.PreviewClip(invalid, time));
                Assert.Throws<InvalidOperationException>(() => session.ReplaceClipRecords(new[] {
                    new ExperimentalExpressionCaptureSession.ClipInput { Clip = valid, Name = "Valid" },
                    new ExperimentalExpressionCaptureSession.ClipInput { Clip = invalid, Name = "Invalid", Time = time } }, null));
                Assert.That(session.Expressions, Is.Empty);
                Assert.That(session.Channels[0].Renderer.GetBlendShapeWeight(0), Is.EqualTo(50));
            }
            finally { Object.DestroyImmediate(valid); Object.DestroyImmediate(invalid); }
        }

        // The menu export drops a reference to a shape the avatar never had and
        // still writes the face, so the board must list and record that face too.
        [Test]
        public void ShapesTheAvatarNeverHadAreSkippedLikeTheMenuExport()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var clip = new AnimationClip { name = "Partly stale" };
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Removed long ago"), AnimationCurve.Constant(0, 1, 100));
                Assert.That(session.ClipError(clip, clip.length), Is.Null);
                Assert.That(session.ClipErrors(new[] { clip })[clip], Is.Null);
                session.PreviewClip(clip, clip.length);
                Assert.That(session.Channels[0].Renderer.GetBlendShapeWeight(0), Is.EqualTo(70));
                session.ReplaceClipRecords(new[] { new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "Partly stale", Time = clip.length } }, null);
                var row = session.Expressions.Single().Rows.Single(value => value.Path == session.Channels[0].Path);
                Assert.That(row.Weights[Array.IndexOf(row.Shapes, "Hair detail")], Is.EqualTo(70));
                Assert.That(row.Shapes, Does.Not.Contain("Removed long ago"));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // A shape the renderer had before the build passes but lost in them is
        // not skipped: the menu export omits the face for it, so the board must
        // reject it too, even when another reference in the clip still resolves.
        [Test]
        public void ShapesLostByPreparationStayUnsupportedOnTheBoard()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var preparation = (NdmfExportPreparation)typeof(ExperimentalExpressionCaptureSession).GetField("preparation", flags).GetValue(session);
            var before = (System.Collections.Generic.Dictionary<SkinnedMeshRenderer, System.Collections.Generic.HashSet<string>>)typeof(NdmfExportPreparation).GetField("shapesBeforeProcessing", flags).GetValue(preparation);
            foreach (var shapes in before.Values) shapes.Add("Frozen in build");
            var clip = new AnimationClip { name = "Partly frozen" };
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Frozen in build"), AnimationCurve.Constant(0, 1, 100));
                var expected = ExporterLocalization.T("候補のBlendShapeが表示中のメッシュにありません: ");
                Assert.That(session.ClipError(clip, clip.length), Does.StartWith(expected));
                Assert.That(session.ClipErrors(new[] { clip })[clip], Does.StartWith(expected));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        [Test]
        public void RecommendationsFollowActualMergeAnimatorReferencesInsteadOfScanningByName()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folderName = "__ExplicitRecommendations_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName); var folder = "Assets/" + folderName;
            try
            {
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Faces.controller");
                var clip = new AnimationClip { name = "An arbitrary user name" };
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70));
                AssetDatabase.AddObjectToAsset(clip, controller); controller.layers[0].stateMachine.AddState("Face").motion = clip;
                var mergeType = Sdk("nadena.dev.modular_avatar.core.ModularAvatarMergeAnimator");
                Assert.That(mergeType, Is.Not.Null, "This experiment uses the real Modular Avatar integration.");
                // NDMF before 1.8.0 has no generic platform and builds the FX
                // that Merge Animator merges into from a VRChat descriptor.
                if (Sdk("nadena.dev.ndmf.runtime.components.NDMFAvatarRoot") == null)
                    typeof(PoseRegistrationTests).GetMethod("Descriptor", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).Invoke(null, new object[] { fixture.Source });
                var merge = fixture.Source.AddComponent(mergeType);
                using (var data = new SerializedObject(merge))
                {
                    data.FindProperty("animator").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                Assert.That(session.RecommendClips().Any(item => item.Clip == clip), Is.True);
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }

        [UnityTest]
        public IEnumerator CandidateSelectionBulkAddAndNamePreviewUseTheAutomaticFinalFrame()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clips = Enumerable.Range(0, 3).Select(index => new AnimationClip { name = "Candidate " + index }).ToArray();
            for (var index = 0; index < clips.Length; index++) AnimationUtility.SetEditorCurve(clips[index],
                EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Linear(0, 10, 1, 70 + index));
            try
            {
                // Show() restores the size saved for this window type; set ours after it.
                window.Show(); window.position = new Rect(40, 40, 700, 1100);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                type.GetField("recommendations", flags).SetValue(window, clips.Select(clip => new ExperimentalExpressionCaptureSession.ClipRecommendation { Clip = clip, Source = "Fixture" }).ToList());
                var errors = (System.Collections.Generic.Dictionary<AnimationClip, string>)type.GetField("clipErrors", flags).GetValue(window);
                foreach (var clip in clips) errors[clip] = null;
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var names = (System.Collections.Generic.Dictionary<AnimationClip, Rect>)type.GetField("candidateRects", flags).GetValue(window);
                Click(window, names[clips[0]]);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.Channels.First(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(70));
                var board = (ExpressionBoard)type.GetField("board", flags).GetValue(window);
                Assert.That(board.Move(clips[1], ExpressionBoard.Place.Excluded), Is.True);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                Assert.That(board.ExportedCustom(), Is.EqualTo(new[] { clips[0], clips[2] }));
                type.GetMethod("RecordBoardClips", flags).Invoke(window, null);
                Assert.That(session.Expressions.Count, Is.EqualTo(2));
                Assert.That(session.Expressions.Select(pose => pose.Rows.First(row => row.Path == "Front").Weights[0]), Is.EqualTo(new[] { 70f, 72f }));
                Assert.That(fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0), Is.EqualTo(35));
            }
            finally { window.Close(); foreach (var clip in clips) Object.DestroyImmediate(clip); }
        }

        // Runs a tile's × between two more button ids in a real IMGUI pass, and
        // keeps how far apart they are for each event type. Ids differ between
        // windows, so only the gap is compared. IMGUI skips ahead to the next id
        // with the same hint, so the probe asks with the hint GUI.Button uses,
        // as the × of the next tile does.
        sealed class CloseButtonProbe : EditorWindow
        {
            internal static readonly Rect Tile = new Rect(10, 10, 60, 60);
            internal bool Legacy;
            internal Vector2 Mouse;
            static readonly int ButtonHint = "Button".GetHashCode();
            internal readonly System.Collections.Generic.Dictionary<EventType, int> Next = new System.Collections.Generic.Dictionary<EventType, int>();

            void OnGUI()
            {
                var type = Event.current.type;
                // Set here so the check does not depend on how the window passes the mouse position.
                Event.current.mousePosition = Mouse;
                var button = new Rect(Tile.xMax - 18, Tile.y + 2, 16, 16);
                var before = GUIUtility.GetControlID(ButtonHint, FocusType.Passive);
                if (Legacy)
                {
                    // The earlier form: the button was left out of a repaint without the mouse.
                    if (type != EventType.Repaint || Tile.Contains(Event.current.mousePosition)) GUI.Button(button, "×");
                }
                else ExperimentalExpressionCaptureWindow.CloseButton(Tile, button, new GUIContent("×"), GUI.skin.label);
                Next[type] = GUIUtility.GetControlID(ButtonHint, FocusType.Passive) - before;
            }
        }

        [Test]
        public void TheHiddenCloseButtonKeepsTheIdsOfTheControlsAfterIt()
        {
            int Run(bool legacy, Vector2 mouse, EventType type)
            {
                var probe = ScriptableObject.CreateInstance<CloseButtonProbe>();
                try
                {
                    probe.Legacy = legacy; probe.Mouse = mouse; probe.Show(); probe.position = new Rect(40, 40, 200, 200);
                    probe.Next.Clear();
                    probe.SendEvent(new Event { type = type, mousePosition = mouse });
                    Assert.That(probe.Next.ContainsKey(type), Is.True, type + " reached OnGUI");
                    return probe.Next[type];
                }
                finally { probe.Close(); }
            }
            var outside = new Vector2(150, 150); var inside = CloseButtonProbe.Tile.center;
            Assert.That(CloseButtonProbe.Tile.Contains(outside), Is.False);
            // The probe sees the shift the earlier form made, so the check below is not empty.
            Assert.That(Run(true, outside, EventType.Repaint), Is.Not.EqualTo(Run(true, outside, EventType.Layout)));
            var layout = Run(false, outside, EventType.Layout);
            Assert.That(Run(false, outside, EventType.Repaint), Is.EqualTo(layout), "Hidden × still takes its id.");
            Assert.That(Run(false, inside, EventType.Repaint), Is.EqualTo(layout), "Shown × takes the same id.");
            Assert.That(Run(false, outside, EventType.MouseDown), Is.EqualTo(layout));
        }

        static void Click(EditorWindow window, Rect rect)
        {
            Assert.That(rect.width, Is.GreaterThan(0));
            window.SendEvent(new Event { type = EventType.MouseDown, button = 0, mousePosition = rect.center });
            window.SendEvent(new Event { type = EventType.MouseUp, button = 0, mousePosition = rect.center });
        }

        [UnityTest] public IEnumerator OneExplicitClipCanBeDroppedAndPreviewed() => VerifyExplicitDrop(1);
        [UnityTest] public IEnumerator FourExplicitClipsCanBeDroppedAndRecorded() => VerifyExplicitDrop(4);

        static IEnumerator VerifyExplicitDrop(int count)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var clips = Enumerable.Range(0, count).Select(index => new AnimationClip { name = "Dropped face " + index }).ToArray();
            for (var index = 0; index < clips.Length; index++) AnimationUtility.SetEditorCurve(clips[index],
                EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70 + index));
            try
            {
                // Show restores the place the editor saved for this window, so the
                // size is set after it; a shorter window would scroll the panels away.
                window.Show(); window.position = new Rect(40, 40, 700, 1100);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var drop = (Rect)type.GetField("expressionDropRect", flags).GetValue(window);
                Assert.That(drop.height, Is.GreaterThan(0));
                DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = clips.Cast<Object>().ToArray();
                window.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = drop.center });
                window.SendEvent(new Event { type = EventType.DragPerform, mousePosition = drop.center });
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var inputs = (System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>)type.GetField("clipInputs", flags).GetValue(window);
                Assert.That(inputs.Count, Is.EqualTo(count), "Actual IMGUI drag events must add the selected files.");
                Assert.That(inputs.All(input => input.Error == null), Is.True);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.Channels.First(channel => channel.Path == "Front").Renderer.GetBlendShapeWeight(0), Is.EqualTo(70 + count - 1), "Adding a valid file previews it.");
                Assert.That(session.ReplaceClipRecords(inputs, null).Length, Is.EqualTo(count));
                Assert.That(fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0), Is.EqualTo(35));
            }
            finally
            {
                DragAndDrop.objectReferences = Array.Empty<Object>(); window.Close();
                foreach (var clip in clips) Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void AtomicSaveReplacesTheIntendedFileAndCleansItsOwnTemporaryFile()
        {
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-capture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var path = Path.Combine(directory, "face.vrm"); File.WriteAllBytes(path, new byte[] { 1, 2 });
                ExperimentalExpressionCaptureWindow.AtomicWrite(path, new byte[] { 3, 4, 5 });
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 3, 4, 5 }));
                Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { path }));
            }
            finally { Directory.Delete(directory, true); }
        }

        // A menu entry that takes the board off its third page asks only while that page
        // uploads or shows its QR.
        [Test]
        public void LeavingTheTransferPageFromTheMenuAsksOnlyWhileItTransfers()
        {
            var dialog = ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var asked = new System.Collections.Generic.List<(string Title, string Body)>();
            var answer = false;
            try
            {
                ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer = (title, body) => { asked.Add((title, body)); return answer; };
                Assert.That(ExperimentalExpressionCaptureWindow.ConfirmLeavingTransfer(false), Is.True);
                Assert.That(asked, Is.Empty);
                Assert.That(ExperimentalExpressionCaptureWindow.ConfirmLeavingTransfer(true), Is.False);
                Assert.That(asked, Is.EqualTo(new[] { (ExporterLocalization.T(ExperimentalExpressionCaptureWindow.CancelTransferTitle),
                    ExporterLocalization.T(ExperimentalExpressionCaptureWindow.CancelTransferBody)) }));
            }
            finally
            {
                ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer = dialog;
                Object.DestroyImmediate(window);
            }
        }

        // The board's third page is the transfer panel, so the board's license is never shown: the panel
        // starts from the avatar's remembered terms instead of the board's untouched default.
        [Test]
        public void ABoardShowingThePanelDoesNotLendItsUntouchedLicense()
        {
            const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var avatar = new GameObject("LicenseAvatar");
            try
            {
                typeof(ExperimentalExpressionCaptureWindow).GetField("source", field).SetValue(window, avatar);
                typeof(ExperimentalExpressionCaptureWindow).GetField("licenseOptions", field).SetValue(window,
                    new AvatarLicenseOptions { AllowRedistribution = true, CopyrightInformation = "Board" });
                var transfer = new AppTransferExport();
                transfer.SelectAvatar(avatar);
                var license = (AvatarLicenseOptions)typeof(AppTransferExport).GetProperty("license", field).GetValue(transfer);
                Assert.That(license.CopyrightInformation, Is.Not.EqualTo("Board"));
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(avatar);
            }
        }

        // The panel's "戻る" asks while the page uploads or shows its QR. "転送を続ける" stays on the page;
        // "中止して移動" and a page with nothing to lose go to the objects page.
        [Test]
        public void ThePanelBackAsksWhileItTransfersAndKeepingStaysOnThePage()
        {
            const System.Reflection.BindingFlags field = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var dialog = ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var asked = 0;
            var answer = false;
            try
            {
                ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer = (title, body) => { asked++; return answer; };
                typeof(ExperimentalExpressionCaptureWindow).GetField("page", field).SetValue(window, ExperimentalExpressionCaptureWindow.InfoPage);
                typeof(ExperimentalExpressionCaptureWindow).GetField("pending", field).SetValue(window, null);
                asked = 0;
                answer = false;
                window.PanelBack(true);
                Assert.That(asked, Is.EqualTo(1));
                Assert.That(typeof(ExperimentalExpressionCaptureWindow).GetField("pending", field).GetValue(window), Is.Null, "Kept, nothing is queued.");
                Assert.That(typeof(ExperimentalExpressionCaptureWindow).GetField("page", field).GetValue(window), Is.EqualTo(ExperimentalExpressionCaptureWindow.InfoPage));
                answer = true;
                window.PanelBack(true);
                Assert.That(asked, Is.EqualTo(2));
                Assert.That(typeof(ExperimentalExpressionCaptureWindow).GetField("pending", field).GetValue(window), Is.Not.Null, "The move to the objects page is queued.");
                typeof(ExperimentalExpressionCaptureWindow).GetField("pending", field).SetValue(window, null);
                window.PanelBack(false);
                Assert.That(asked, Is.EqualTo(2));
                Assert.That(typeof(ExperimentalExpressionCaptureWindow).GetField("pending", field).GetValue(window), Is.Not.Null);
            }
            finally
            {
                ExperimentalExpressionCaptureWindow.ConfirmCancelTransfer = dialog;
                Object.DestroyImmediate(window);
            }
        }

        // The prepared board's third page is the transfer panel.
        [TestCase(ExperimentalExpressionCaptureWindow.InfoPage, true, true)]
        [TestCase(ExperimentalExpressionCaptureWindow.InfoPage, false, false)]
        [TestCase(ExperimentalExpressionCaptureWindow.ObjectsPage, true, false)]
        public void ThePreparedBoardsThirdPageIsThePanel(int page, bool prepared, bool panel)
        {
            Assert.That(ExperimentalExpressionCaptureWindow.PanelPage(page, prepared), Is.EqualTo(panel));
        }

        // The question before leaving the transfer: one line without 句点, buttons that say what they do.
        [Test]
        public void TheLeaveDialogTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var texts = new[]
            {
                ExperimentalExpressionCaptureWindow.CancelTransferTitle, ExperimentalExpressionCaptureWindow.CancelTransferBody,
                ExperimentalExpressionCaptureWindow.LeaveTransferText, ExperimentalExpressionCaptureWindow.KeepTransferText,
                ExperimentalExpressionCaptureWindow.NextLabel, StepIndicator.Names()[2],
            };
            foreach (var text in texts)
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|はい|いいえ"), Is.False, text);
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var table = File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var text in texts)
                    Assert.That(table, Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale + ": " + text);
            }
        }

        [Test]
        public void FileRecordsUpdateNamesAndTimeAndDeselectWithoutDeletingManualFaces()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Linear(0, 35, 1, 80));
            try
            {
                var manual = Record(session, "Manual", 60);
                var input = new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "Old", Time = 0 };
                var old = session.ReplaceClipRecords(new[] { input }, null);
                input.Name = "Updated"; input.Time = 1;
                var updated = session.ReplaceClipRecords(new[] { input }, old);
                Assert.That(session.Expressions.Select(pose => pose.Name), Is.EqualTo(new[] { "Manual", "Updated" }));
                Assert.That(updated.Single().Rows.First(row => row.Path == "Front").Weights[0], Is.EqualTo(80));
                session.ReplaceClipRecords(Array.Empty<ExperimentalExpressionCaptureSession.ClipInput>(), updated);
                Assert.That(session.Expressions, Is.EqualTo(new[] { manual }));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // The board name, not the file name, becomes the VRM custom expression name.
        [Test]
        public async Task ARenamedBoardFaceIsExportedUnderItsNewName()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var clip = new AnimationClip { name = "face_file_01" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 75));
            var presets = new System.Collections.Generic.Dictionary<ExpressionPreset, ExperimentalExpressionCaptureSession.PresetAssignment>();
            var board = new ExpressionBoard(presets, (preset, value) => { if (value == null) presets.Remove(preset); else presets[preset] = value; },
                candidates => new System.Collections.Generic.Dictionary<ExpressionPreset, ExperimentalExpressionCaptureSession.PresetAssignment>(), null);
            Vrm10Instance imported = null;
            try
            {
                board.SetItems(null, new[] { new ExpressionBoard.Item { Clip = clip, Source = "Fixture" } });
                Assert.That(board.Rename(clip, "笑顔 2"), Is.True);
                Assert.That(board.UsesRecordRoute, Is.True);
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, replayInstalledDefaults: false);
                var listed = new[] { (board.NameOf(clip), clip, ExperimentalExpressionCaptureWindow.DefaultTime(clip)) };
                var bytes = session.Export("Test", "Tests", listed, new System.Collections.Generic.List<string>(), new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                var names = imported.Vrm.Expression.CustomClips.Select(item => item.name).ToArray();
                Assert.That(names, Does.Contain("笑顔 2"));
                Assert.That(names, Does.Not.Contain("face_file_01"));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void InvalidUpdatesPreserveEveryPreviouslyRecordedFace()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 75));
            try
            {
                Record(session, "Manual", 60);
                var input = new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "File" };
                var previous = session.ReplaceClipRecords(new[] { input }, null);
                var all = session.Expressions.ToArray();
                input.Time = float.NaN;
                Assert.Throws<InvalidOperationException>(() => session.ReplaceClipRecords(new[] { input }, previous));
                Assert.That(session.Expressions, Is.EqualTo(all));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // A repeated name is numbered in list order, the same rule as the
        // final VRM names; a retained record keeps its name.
        [Test]
        public void ReplaceClipRecordsNumbersRepeatedNames()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var clips = new[] { new AnimationClip(), new AnimationClip() };
            for (var index = 0; index < clips.Length; index++) AnimationUtility.SetEditorCurve(clips[index],
                EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, 70 + index * 10));
            try
            {
                var inputs = clips.Select(clip => new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = "x" }).ToArray();
                var both = session.ReplaceClipRecords(inputs, null);
                Assert.That(both.Select(pose => pose.Name), Is.EqualTo(new[] { "x", "x (2)" }));
                Assert.That(session.Expressions.Select(pose => pose.Name), Is.EqualTo(new[] { "x", "x (2)" }));
                session.ReplaceClipRecords(Array.Empty<ExperimentalExpressionCaptureSession.ClipInput>(), both);

                Record(session, "x", 60);
                var staged = session.ReplaceClipRecords(new[] { inputs[0] }, null);
                Assert.That(staged.Single().Name, Is.EqualTo("x (2)"));
                Assert.That(session.Expressions.Select(pose => pose.Name), Is.EqualTo(new[] { "x", "x (2)" }));
            }
            finally { foreach (var clip in clips) Object.DestroyImmediate(clip); }
        }

        // Leaving out every mesh leaves no preview: the list is neither applied nor
        // saved, stays on screen with the reason, and the board keeps working.
        [Test]
        public void ExcludedObjectsThatLeaveNoPreviewAreNotSaved()
        {
            var scenePath = "Assets/ExcludedObjects-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var meshes = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.gameObject).ToList();
                type.GetField("excludedObjectEdits", flags).SetValue(window, meshes);
                type.GetMethod("ApplyExcludedObjects", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session, Is.Not.Null);
                Assert.That(session.ExcludedObjects, Is.Empty);
                Assert.That(type.GetField("board", flags).GetValue(window), Is.Not.Null);
                Assert.That(type.GetField("excludedObjectEdits", flags).GetValue(window), Is.EqualTo(meshes));
                Assert.That(type.GetField("excludedObjectError", flags).GetValue(window), Is.Not.Null.And.Not.Empty);
                Assert.That(ExportSettingsStore.Load(fixture.Source).Exclusions(), Is.Empty);
                Assert.That(window.BlocksExport, Is.True);
                Assert.That(window.TransferExport("Avatar", "SampleAuthor", new AvatarLicenseOptions(), Path.Combine(Path.GetTempPath(), "unused.vrm"), () => { }),
                    Is.EqualTo(ExporterLocalization.T(AppTransferExport.BoardNeedsFix)));
            }
            finally
            {
                Object.DestroyImmediate(window); fixture.Dispose();
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        // The pages move by 戻る and 次へ. Leaving the objects page applies its
        // edits first; a list the preview cannot take keeps the user on the page
        // with the edits and the reason, and the next try moves on once it is fixed.
        [Test]
        public void LeavingTheObjectsPageAppliesItsEditsFirst()
        {
            var scenePath = "Assets/ExcludedObjects-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            void Drain()
            {
                for (var step = 0; step < 10 && type.GetField("pending", flags).GetValue(window) != null; step++)
                    type.GetMethod("Advance", flags).Invoke(window, null);
                Assert.That(type.GetField("pending", flags).GetValue(window), Is.Null);
            }
            try
            {
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.ExpressionsPage));
                window.GoTo(ExperimentalExpressionCaptureWindow.ObjectsPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.ObjectsPage));

                var meshes = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.gameObject).ToList();
                type.GetField("excludedObjectEdits", flags).SetValue(window, meshes);
                Assert.That(window.Summary().Text, Is.EqualTo(ExporterLocalization.T("除外の変更はページを移るときにVRMへ反映します")));
                window.GoTo(ExperimentalExpressionCaptureWindow.InfoPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.ObjectsPage));
                Assert.That(type.GetField("excludedObjectEdits", flags).GetValue(window), Is.EqualTo(meshes));
                Assert.That(type.GetField("excludedObjectError", flags).GetValue(window), Is.Not.Null.And.Not.Empty);
                Assert.That(type.GetField("applyingOnLeave", flags).GetValue(window), Is.False);
                Assert.That(window.Summary().Text, Is.EqualTo(ExporterLocalization.T("VRMに反映できませんでした・理由は一覧の下にあります")));
                Assert.That(ExportSettingsStore.Load(fixture.Source).Exclusions(), Is.Empty);

                var kept = meshes.Skip(1).ToList();
                type.GetField("excludedObjectEdits", flags).SetValue(window, kept);
                type.GetField("excludedObjectError", flags).SetValue(window, null);
                window.GoTo(ExperimentalExpressionCaptureWindow.InfoPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.InfoPage));
                Assert.That(type.GetField("excludedObjectEdits", flags).GetValue(window), Is.Null);
                Assert.That(ExportSettingsStore.Load(fixture.Source).Exclusions(), Is.EquivalentTo(kept));

                // The status line (here a save report) stays with the page it came from.
                type.GetField("status", flags).SetValue(window, "saved");
                window.GoTo(ExperimentalExpressionCaptureWindow.ObjectsPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.ObjectsPage));
                Assert.That(type.GetField("status", flags).GetValue(window), Is.Null);
                window.GoTo(ExperimentalExpressionCaptureWindow.ExpressionsPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.ExpressionsPage));

                // An apply still waiting that another action replaces does not leave the board blocked.
                type.GetField("excludedObjectEdits", flags).SetValue(window, kept.ToList());
                type.GetMethod("ApplyThen", flags).Invoke(window, new object[] { (Action)(() => { }) });
                Assert.That(type.GetField("applyingOnLeave", flags).GetValue(window), Is.True);
                type.GetMethod("Queue", flags).Invoke(window, new object[] { (Action)(() => { }) });
                Assert.That(type.GetField("applyingOnLeave", flags).GetValue(window), Is.False);
                Drain();
                type.GetField("excludedObjectEdits", flags).SetValue(window, null);

                // The third page shows the transfer panel.
                window.GoTo(ExperimentalExpressionCaptureWindow.InfoPage); Drain();
                Assert.That(window.Page, Is.EqualTo(ExperimentalExpressionCaptureWindow.InfoPage));
            }
            finally
            {
                Object.DestroyImmediate(window); fixture.Dispose();
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        // A saved list that leaves no preview does not lock the avatar out of the
        // board: it opens with every object and the list waits to be fixed.
        [Test]
        public void SavedExcludedObjectsThatLeaveNoPreviewWaitInTheList()
        {
            var scenePath = "Assets/ExcludedObjects-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                var meshes = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.gameObject).ToArray();
                var settings = ExportSettingsStore.Load(fixture.Source);
                settings.ExcludedObjects.AddRange(meshes);
                ExportSettingsStore.Save(fixture.Source, settings);
                Assert.That(ExportSettingsStore.Load(fixture.Source).Exclusions(), Is.EquivalentTo(meshes));
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session, Is.Not.Null);
                Assert.That(session.ExcludedObjects, Is.Empty);
                Assert.That(type.GetField("excludedObjectEdits", flags).GetValue(window), Is.EquivalentTo(meshes));
                Assert.That(type.GetField("excludedObjectError", flags).GetValue(window), Is.Not.Null.And.Not.Empty);
                Assert.That(window.BlocksExport, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window); fixture.Dispose();
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        // An avatar edit that moves an excluded object out of the avatar rebuilds
        // the preview with every object; the board stays and the list waits with the reason.
        [Test]
        public void ExcludedObjectsBrokenByAnAvatarEditWaitInTheList()
        {
            var scenePath = "Assets/ExcludedObjects-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                Assert.That(UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                var front = fixture.Source.transform.Find("Front").gameObject;
                var settings = ExportSettingsStore.Load(fixture.Source);
                settings.ExcludedObjects.Add(front);
                ExportSettingsStore.Save(fixture.Source, settings);
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session.ExcludedObjects, Is.EquivalentTo(new[] { front }));
                front.transform.SetParent(null, true);
                Assert.That(session.HasCurrentSource, Is.False);
                type.GetMethod("RefreshCandidateValidation", flags).Invoke(window, null);
                session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                Assert.That(session, Is.Not.Null);
                Assert.That(session.ExcludedObjects, Is.Empty);
                Assert.That(type.GetField("board", flags).GetValue(window), Is.Not.Null);
                Assert.That(type.GetField("excludedObjectEdits", flags).GetValue(window), Is.EquivalentTo(new[] { front }));
                Assert.That(type.GetField("excludedObjectError", flags).GetValue(window), Is.Not.Null.And.Not.Empty);
                Assert.That(window.BlocksExport, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window); fixture.Dispose();
                UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void BottomBarNamesWhatBlocksTheSave()
        {
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var presets = new System.Collections.Generic.Dictionary<ExpressionPreset, ExperimentalExpressionCaptureSession.PresetAssignment>();
            var clips = Enumerable.Range(0, 66).Select(index => new AnimationClip { name = "Face " + index }).ToArray();
            var twin = new AnimationClip { name = "Face 0" };
            var board = new ExpressionBoard(presets, (preset, value) => { if (value == null) presets.Remove(preset); else presets[preset] = value; },
                candidates => new System.Collections.Generic.Dictionary<ExpressionPreset, ExperimentalExpressionCaptureSession.PresetAssignment>(), null);
            try
            {
                type.GetField("board", flags).SetValue(window, board);
                type.GetField("author", flags).SetValue(window, "");
                board.SetItems(null, clips.Take(3).Select(clip => new ExpressionBoard.Item { Clip = clip, Source = "Fixture" }));
                Assert.That(window.BlockReason(), Is.Null, "The transfer asks the author on its page, not on the board.");
                Assert.That(window.BlocksExport, Is.False, "The author is asked at the save, not on the board.");
                type.GetField("author", flags).SetValue(window, "Author");
                Assert.That(window.BlockReason(), Is.Null);
                Assert.That(window.BlocksExport, Is.False);

                board.SetItems(new[] { new ExpressionBoard.Item { Clip = twin, Added = true, Source = "Fixture" } }, clips.Take(3).Select(clip => new ExpressionBoard.Item { Clip = clip, Source = "Fixture" }));
                Assert.That(window.BlockReason(), Is.Null, "A repeated name is numbered when recorded, so it does not stop the save.");
                Assert.That(window.BlocksExport, Is.False);

                board.SetItems(null, clips.Select(clip => new ExpressionBoard.Item { Clip = clip, Source = "Fixture" }));
                board.Move(clips[0], ExpressionBoard.Place.Excluded);
                Assert.That(window.BlockReason(), Is.Null, "Recording more than 64 faces does not stop the save.");
                Assert.That(window.BlocksExport, Is.False);

                var errors = (System.Collections.Generic.Dictionary<ExpressionPreset, string>)type.GetField("presetErrors", flags).GetValue(window);
                errors[ExpressionPreset.sad] = null; errors[ExpressionPreset.aa] = "Mouth reason"; errors[ExpressionPreset.angry] = "Angry reason";
                Assert.That(window.BlockReason(), Is.EqualTo("Angry reason"), "The first broken slot in board order gives its own reason.");
                Assert.That(window.BlocksExport, Is.True);

                type.GetField("excludedObjectEdits", flags).SetValue(window, new System.Collections.Generic.List<GameObject>());
                Assert.That(window.BlockReason(), Is.EqualTo(ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません")));
                Assert.That(window.BlocksExport, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window); Object.DestroyImmediate(twin);
                foreach (var clip in clips) Object.DestroyImmediate(clip);
            }
        }

        // BlocksExport and BlockReason read one condition, so a window without a
        // board blocks the save and names why, instead of blocking in silence.
        [Test]
        public void AWindowWithoutABoardBlocksTheSaveAndSaysWhy()
        {
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            try
            {
                type.GetField("author", flags).SetValue(window, "Author");
                Assert.That(type.GetField("board", flags).GetValue(window), Is.Null);
                Assert.That(window.BlocksExport, Is.True);
                Assert.That(window.BlockReason(), Is.EqualTo(ExporterLocalization.T(AppTransferExport.BoardClosed)));
            }
            finally { Object.DestroyImmediate(window); }
        }

        static Type Sdk(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        // The quick geometry check skips the mesh copy only when a vertex moves;
        // every face gets the answer the copy gives.
        [Test]
        public void TheGeometryCheckAgreesWithTheMeshCopy()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var head = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            var mesh = new Mesh { name = "Face" };
            mesh.vertices = new[] { new Vector3(-.1f, 1.7f, .1f), new Vector3(.1f, 1.7f, .1f), new Vector3(0, 1.8f, .1f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
            mesh.bindposes = new[] { head.worldToLocalMatrix };
            mesh.AddBlendShapeFrame("Moves", 100, Enumerable.Repeat(new Vector3(0, .01f, 0), 3).ToArray(), null, null);
            mesh.AddBlendShapeFrame("Normals only", 100, new Vector3[3], Enumerable.Repeat(new Vector3(0, .1f, 0), 3).ToArray(), null);
            mesh.AddBlendShapeFrame("Still", 100, new Vector3[3], null, null);
            var material = new Material(Shader.Find(MToon10Meta.UnityShaderName));
            var face = new GameObject("Face"); face.transform.SetParent(fixture.Source.transform, false);
            var skin = face.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh; skin.bones = new[] { head }; skin.rootBone = head; skin.sharedMaterials = new[] { material };
            var check = typeof(ExperimentalExpressionCaptureSession).GetMethod("HasGeometryChange", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            try
            {
                using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                var index = session.Channels.FindIndex(channel => channel.Path == "Face");
                foreach (var (shape, expected) in new[] { ("Moves", true), ("Normals only", true), ("Still", false) })
                {
                    var pose = session.ThumbnailFace("Face", shape);
                    var copy = Object.Instantiate(session.Channels[index].Mesh);
                    try
                    {
                        AvatarBaseShape.AppendExpression(session.Channels[index].Mesh, copy, "Probe", session.Baseline.Rows[index].Weights, pose.Rows[index].Weights,
                            PlayerSettings.legacyClampBlendShapeWeights);
                        Assert.That(AvatarBaseShape.HasUsableMorphEndpoint(copy, copy.blendShapeCount - 1, 0, 100), Is.EqualTo(expected), shape + " with the mesh copy");
                    }
                    finally { Object.DestroyImmediate(copy); }
                    Assert.That((bool)check.Invoke(session, new object[] { pose }), Is.EqualTo(expected), shape);
                }
            }
            finally { Object.DestroyImmediate(face); Object.DestroyImmediate(mesh); Object.DestroyImmediate(material); }
        }

        // Inside one batch the source is hashed once; the next batch, and any
        // call outside a batch, still sees an edit.
        [Test]
        public void ABatchChecksTheSourceOnceAndTheNextOneSeesAnEdit()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var front = fixture.Source.transform.Find("Front");
            using (ExperimentalExpressionCaptureSession.Batch())
            {
                using (ExperimentalExpressionCaptureSession.Batch()) session.RestoreBaseline();
                front.name = "Front edited";
                Assert.DoesNotThrow(session.RestoreBaseline, "The inner batch ending leaves the outer one checked.");
            }
            Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(session.RestoreBaseline);
            using (ExperimentalExpressionCaptureSession.Batch()) Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(session.RestoreBaseline);
            front.name = "Front";
            using (ExperimentalExpressionCaptureSession.Batch()) Assert.DoesNotThrow(session.RestoreBaseline);
        }

        // Making the session hashes the source; the rest of that batch uses
        // that hash, and the next batch still sees an edit.
        [Test]
        public void ASessionMadeInABatchCountsAsThatBatchsCheck()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var front = fixture.Source.transform.Find("Front");
            ExperimentalExpressionCaptureSession session;
            using (ExperimentalExpressionCaptureSession.Batch())
            {
                session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
                front.name = "Front edited";
                Assert.DoesNotThrow(session.RestoreBaseline, "The batch the session was made in is already checked.");
            }
            using (session)
            {
                using (ExperimentalExpressionCaptureSession.Batch()) Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(session.RestoreBaseline);
                front.name = "Front";
                using (ExperimentalExpressionCaptureSession.Batch()) Assert.DoesNotThrow(session.RestoreBaseline);
            }
        }

        static int SourceChecks() => ExpressionBoardTimings.Last.Count(step => step.Step == "validate" || step.Step.EndsWith("/validate", StringComparison.Ordinal));

        // The transfer window calls the board outside its own batch. One
        // transfer hashes the source once, as the save starts: not again in
        // Export right after, nor once more per standard face.
        [Test]
        public void ATransferChecksTheSourceForTheRecordsAndTheExportOnly()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            fixture.Mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(new Vector3(0, .03f, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-checks-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var session = (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(window);
                session.SetPreset(ExpressionPreset.happy, ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile"));
                session.SetPreset(ExpressionPreset.sad, ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.sad, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.surprised, ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.surprised, "Front", "Smile"));
                var saved = false;
                ExpressionBoardTimings.Clear();
                Assert.That(window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved = true), Is.Null);
                Assert.That(saved, Is.True);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(SourceChecks(), Is.EqualTo(1), string.Join(", ", ExpressionBoardTimings.Last.Select(step => step.Step)));
            }
            finally { Object.DestroyImmediate(window); if (File.Exists(path)) File.Delete(path); }
        }

        // The board as the transfer screen opens it, with the happy slot set.
        // Edit() changes the scene the way a user would, leaving the slot's object in place.
        sealed class FollowBoard : IDisposable
        {
            static readonly System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            static readonly Type Type = typeof(ExperimentalExpressionCaptureWindow);
            internal readonly AttachmentConnectionTests.Fixture Fixture = new AttachmentConnectionTests.Fixture();
            internal readonly ExperimentalExpressionCaptureWindow Window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            int edits;

            internal FollowBoard()
            {
                foreach (var skin in Fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
                Fixture.Mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(new Vector3(0, .03f, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
                Set("source", Fixture.Source);
                Set("automaticBlink", false);
                Delegate<Action>("Prepare")();
                Session.SetPreset(ExpressionPreset.happy, ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile"));
            }

            internal ExperimentalExpressionCaptureSession Session => Get<ExperimentalExpressionCaptureSession>("session");
            internal string Error => Get<string>("error");
            internal T Get<T>(string field) => (T)Type.GetField(field, Flags).GetValue(Window);
            internal void Set(string field, object value) => Type.GetField(field, Flags).SetValue(Window, value);
            internal T Delegate<T>(string method) where T : Delegate => (T)System.Delegate.CreateDelegate(typeof(T), Window, Type.GetMethod(method, Flags));

            internal void Edit() => Fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 36 + edits++);
            internal void Advance() => Delegate<Action>("Advance")();
            internal void Run(Action action) { Delegate<Action<Action>>("Queue")(action); Advance(); }
            internal void Choose(ExpressionPreset preset, string path) =>
                Run(() => Delegate<Action<ExpressionPreset, ExperimentalExpressionCaptureSession.PresetAssignment>>("ChoosePreset")(preset,
                    ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(preset, path, "Smile")));

            internal static void ExpectFollowed() => LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("^" +
                System.Text.RegularExpressions.Regex.Escape(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.FollowedSourceFormat).Split('{')[0])));

            internal void AssertKept()
            {
                Assert.That(Error, Is.Null);
                Assert.That(Session.Presets.TryGetValue(ExpressionPreset.happy, out var happy) && happy.IsShape && happy.Path == "Front", Is.True, "The slot set before the edit stays.");
                Assert.That(Session.SourceDifference(), Is.Null, "The rebuilt copy is of the avatar as it is now.");
            }

            public void Dispose() { Object.DestroyImmediate(Window); Fixture.Dispose(); }
        }

        // The reported case: the avatar reads differently when the transfer saves. The
        // board rebuilds its copy with the same slots and the VRM is written.
        [Test]
        public void ATransferAfterAnEditOfTheSourceFollowsItWithTheSameChoices()
        {
            using var board = new FollowBoard();
            var before = board.Session;
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-edited-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                board.Edit();
                FollowBoard.ExpectFollowed();
                var saved = false;
                Assert.That(board.Window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved = true), Is.Null);
                Assert.That(saved, Is.True);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(board.Session, Is.Not.SameAs(before));
                board.AssertKept();
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        // An edit that leaves an added file unusable (its BlendShape is gone)
        // leaves the file on the board with its reason, and the transfer stops
        // on it instead of writing a VRM without that face.
        [Test]
        public void ATransferStopsWhenTheRebuiltCopyDropsAnAddedFile()
        {
            using var board = new FollowBoard();
            var clip = new AnimationClip { name = "Wink file" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-dropped-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var mesh = board.Fixture.Mesh;
                mesh.AddBlendShapeFrame("Wink", 100, Enumerable.Repeat(new Vector3(.03f, 0, 0), mesh.vertexCount).ToArray(), new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Wink"), AnimationCurve.Constant(0, 1, 100));
                // The board follows the avatar with the new BlendShape first.
                FollowBoard.ExpectFollowed();
                board.Delegate<Action>("RefreshCandidateValidation")();
                Assert.That(board.Session.SourceDifference(), Is.Null);
                board.Delegate<Action<AnimationClip>>("AddClip")(clip);
                var inputs = board.Get<System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>>("clipInputs");
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }));

                var shapes = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).Where(name => name != "Wink").Select(name =>
                {
                    var index = mesh.GetBlendShapeIndex(name);
                    var frames = Enumerable.Range(0, mesh.GetBlendShapeFrameCount(index)).Select(frame =>
                    {
                        var deltas = new Vector3[mesh.vertexCount]; var normals = new Vector3[mesh.vertexCount]; var tangents = new Vector3[mesh.vertexCount];
                        mesh.GetBlendShapeFrameVertices(index, frame, deltas, normals, tangents);
                        return (Weight: mesh.GetBlendShapeFrameWeight(index, frame), deltas, normals, tangents);
                    }).ToArray();
                    return (name, frames);
                }).ToArray();
                mesh.ClearBlendShapes();
                foreach (var (name, frames) in shapes)
                    foreach (var frame in frames) mesh.AddBlendShapeFrame(name, frame.Weight, frame.deltas, frame.normals, frame.tangents);

                FollowBoard.ExpectFollowed();
                var saved = false;
                var failure = Assert.Throws<InvalidOperationException>(() => board.Window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved = true));
                Assert.That(failure.Message, Does.Contain(clip.name));
                Assert.That(saved, Is.False);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }), "The file stays on the board with its reason, as when the board checks the files.");
                Assert.That(inputs.Single().Error, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        // A face mesh hidden on the avatar for a moment is not followed: a copy
        // without it would drop the files added for it, and the user could not
        // get them back by showing it again. The transfer stops naming the mesh
        // and the board keeps its files and slots for when it is shown again.
        [Test]
        public void ATransferStopsWithoutDroppingFilesWhenAShownFaceIsHidden()
        {
            using var board = new FollowBoard();
            var clip = new AnimationClip { name = "Smile file" };
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-hidden-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Constant(0, 1, 100));
                board.Delegate<Action<AnimationClip>>("AddClip")(clip);
                var inputs = board.Get<System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>>("clipInputs");
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }));
                var before = board.Session;
                var front = board.Fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();

                front.enabled = false;
                var saved = false;
                var failure = Assert.Throws<InvalidOperationException>(() => board.Window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved = true));
                Assert.That(failure.Message, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.HiddenFaceFormat), "Front")));
                Assert.That(saved, Is.False);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(board.Session, Is.SameAs(before), "The copy is not rebuilt without the face.");
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }));
                Assert.That(board.Session.Presets.TryGetValue(ExpressionPreset.happy, out var happy) && happy.Path == "Front", Is.True);

                front.enabled = true;
                Assert.That(board.Window.TransferExport("Avatar", "Tests", new AvatarLicenseOptions(), path, () => saved = true), Is.Null);
                Assert.That(saved, Is.True);
                Assert.That(File.Exists(path), Is.True);
                Assert.That(inputs.Select(input => input.Clip), Is.EqualTo(new[] { clip }));
            }
            finally { Object.DestroyImmediate(clip); if (File.Exists(path)) File.Delete(path); }
        }

        // Inside a batch the records and the standard faces use the batch's
        // check, but Export hashes the source again and sees an edit, with what differed.
        [Test]
        public void ExportInABatchStillSeesAnEditOfTheSource()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            fixture.Mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(new Vector3(0, .03f, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var assignment = ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile");
            using (ExperimentalExpressionCaptureSession.Batch())
            {
                session.ReplaceClipRecords(Array.Empty<ExperimentalExpressionCaptureSession.ClipInput>(), null);
                fixture.Source.transform.Find("Front").name = "Front edited";
                Assert.DoesNotThrow(() => session.BuildPresetPose(ExpressionPreset.happy, assignment), "The batch was checked already.");
                var exception = Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(() => session.Export("Test", "Tests", Listed()));
                Assert.That(exception.Message, Is.EqualTo(ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。")));
                Assert.That(exception.Detail, Is.Not.Null.And.Not.Empty);
            }
        }

        // A slot chosen after an edit: the choice reads the copy, finds it out
        // of date, and runs again on a rebuilt one instead of showing the message.
        [Test]
        public void AChoiceAfterAnEditOfTheSourceRunsOnARebuiltCopy()
        {
            using var board = new FollowBoard();
            var before = board.Session;
            board.Edit();
            FollowBoard.ExpectFollowed();
            board.Choose(ExpressionPreset.sad, "Front");
            Assert.That(board.Session, Is.Not.SameAs(before));
            board.AssertKept();
            Assert.That(board.Session.Presets[ExpressionPreset.sad].Path, Is.EqualTo("Front"), "The choice itself is made.");
        }

        // The files are checked again after an import or a change in the scene; an edit rebuilds the copy.
        [Test]
        public void CheckingTheFilesAfterAnEditRebuildsTheCopyWithoutAnError()
        {
            using var board = new FollowBoard();
            var before = board.Session;
            board.Edit();
            FollowBoard.ExpectFollowed();
            board.Set("candidateValidationPending", true);
            board.Advance();
            Assert.That(board.Session, Is.Not.SameAs(before));
            board.AssertKept();
        }

        // The blink line under unchosen blink slots is read while nothing is
        // queued. An out-of-date copy waits for the rebuild; the line never
        // becomes the changed-avatar message.
        [Test]
        public void TheBlinkLineNeverShowsTheChangedSourceMessage()
        {
            using var board = new FollowBoard();
            var message = ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。");
            board.Edit();
            board.Set("blinkFallbackStale", true);
            board.Advance();
            Assert.That(board.Get<string>("blinkFallback") ?? "", Does.Not.Contain(message));
            Assert.That(board.Get<bool>("candidateValidationPending"), Is.True);
            FollowBoard.ExpectFollowed();
            board.Advance();
            board.Advance();
            Assert.That(board.Get<bool>("blinkFallbackStale"), Is.False);
            Assert.That(board.Get<string>("blinkFallback") ?? "", Does.Not.Contain(message));
            board.AssertKept();
        }

        // The slot menu lists BlendShapes from the copy. An out-of-date one
        // opens no menu and no error; the copy is rebuilt on the next update.
        [Test]
        public void ThePresetMenuWaitsForTheRebuiltCopy()
        {
            using var board = new FollowBoard();
            var before = board.Session;
            board.Edit();
            board.Delegate<Action<ExpressionPreset, Rect>>("ShowPresetMenu")(ExpressionPreset.sad, new Rect(0, 0, 10, 10));
            Assert.That(board.Error, Is.Null);
            Assert.That(board.Get<bool>("candidateValidationPending"), Is.True);
            FollowBoard.ExpectFollowed();
            board.Advance();
            Assert.That(board.Session, Is.Not.SameAs(before));
            board.AssertKept();
        }

        // A value that reads differently every time does not rebuild the copy
        // over and over: the idle reads trust the copy built for it, and the next action checks again.
        [Test]
        public void AValueThatReadsDifferentlyEachTimeRebuildsOncePerAction()
        {
            using var board = new FollowBoard();
            board.Edit();
            FollowBoard.ExpectFollowed();
            board.Set("candidateValidationPending", true);
            board.Advance();
            var followed = board.Session;
            for (var i = 0; i < 3; i++)
            {
                board.Edit();
                board.Set("blinkFallbackStale", true);
                board.Advance();
                Assert.That(board.Session, Is.SameAs(followed), "Nothing was done on the board.");
                Assert.That(board.Get<bool>("candidateValidationPending"), Is.False);
            }
            FollowBoard.ExpectFollowed();
            board.Choose(ExpressionPreset.sad, "Front");
            Assert.That(board.Session, Is.Not.SameAs(followed));
            board.AssertKept();
        }

        static AnimationClip SmileFile(float weight)
        {
            var clip = new AnimationClip { name = "Smile file" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Constant(0, 1, weight));
            return clip;
        }

        static void Drop(FollowBoard board, AnimationClip clip, ExpressionPreset preset) =>
            board.Run(() => board.Delegate<Action<AnimationClip, ExpressionBoard.Place>>("MoveTo")(clip, ExpressionBoard.Place.Slot(preset)));

        // Moving a face on the board when the editor reported no edit: the face
        // moves without hashing the avatar, and the blink line, which does not
        // read the slots, is left as it is.
        [Test]
        public void ADropWithoutAnEditDoesNotHashTheSource()
        {
            using var board = new FollowBoard();
            var clip = SmileFile(100);
            try
            {
                board.Delegate<Action<AnimationClip>>("AddClip")(clip);
                board.Advance();
                board.Set("blinkFallbackStale", false);
                var before = board.Session;
                ExpressionBoardTimings.Clear();
                Drop(board, clip, ExpressionPreset.sad);
                board.Advance();
                Assert.That(SourceChecks(), Is.Zero, string.Join(", ", ExpressionBoardTimings.Last.Select(step => step.Step)));
                Assert.That(board.Get<bool>("blinkFallbackStale"), Is.False);
                Assert.That(board.Session, Is.SameAs(before));
                Assert.That(board.Session.Presets[ExpressionPreset.sad].ResolveClip(), Is.SameAs(clip));
                board.AssertKept();
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // An edit the editor reports (here through Undo) makes the next drop
        // hash the avatar, follow it and keep the slots.
        [UnityTest]
        public IEnumerator ADropAfterAReportedEditFollowsTheSource()
        {
            var board = new FollowBoard();
            var clip = SmileFile(100);
            try
            {
                board.Delegate<Action<AnimationClip>>("AddClip")(clip);
                var before = board.Session;
                var edits = ExperimentalExpressionCaptureSession.EditorEdits.Count;
                var back = board.Fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
                Undo.RecordObject(back, "Edit");
                back.SetBlendShapeWeight(0, 50);
                for (var frame = 0; frame < 30 && ExperimentalExpressionCaptureSession.EditorEdits.Count == edits; frame++) yield return null;
                Assert.That(ExperimentalExpressionCaptureSession.EditorEdits.Count, Is.Not.EqualTo(edits), "The editor reported the edit.");
                FollowBoard.ExpectFollowed();
                Drop(board, clip, ExpressionPreset.sad);
                Assert.That(board.Session, Is.Not.SameAs(before));
                Assert.That(board.Session.Presets[ExpressionPreset.sad].ResolveClip(), Is.SameAs(clip));
                board.AssertKept();
            }
            finally { board.Dispose(); Object.DestroyImmediate(clip); }
        }

        // With the window shown, drawing the faces, previewing them and moving
        // them on the copy are not edits of the source: the drops that follow
        // do not hash the avatar.
        [UnityTest]
        public IEnumerator TheBoardsOwnWorkIsNotReportedAsAnEdit()
        {
            var opening = ExperimentalExpressionCaptureSession.EditorEdits.Count;
            var board = new FollowBoard();
            var clip = SmileFile(100);
            try
            {
                board.Delegate<Action<AnimationClip>>("AddClip")(clip);
                board.Window.Show();
                for (var frame = 0; frame < 10; frame++) { board.Window.Repaint(); yield return null; }
                TestContext.WriteLine("Edits reported while the board opened: " + (ExperimentalExpressionCaptureSession.EditorEdits.Count - opening));
                var edits = ExperimentalExpressionCaptureSession.EditorEdits.Count;
                var before = board.Session;
                foreach (var preset in new[] { ExpressionPreset.sad, ExpressionPreset.angry, ExpressionPreset.relaxed })
                {
                    ExpressionBoardTimings.Clear();
                    Drop(board, clip, preset);
                    Assert.That(SourceChecks(), Is.Zero, string.Join(", ", ExpressionBoardTimings.Last.Select(step => step.Step)));
                    for (var frame = 0; frame < 10; frame++) { board.Window.Repaint(); yield return null; }
                }
                Assert.That(ExperimentalExpressionCaptureSession.EditorEdits.Count, Is.EqualTo(edits));
                Assert.That(board.Session, Is.SameAs(before));
                Assert.That(board.Session.Presets[ExpressionPreset.relaxed].ResolveClip(), Is.SameAs(clip));
            }
            finally { board.Dispose(); Object.DestroyImmediate(clip); }
        }

        // The slots reuse a face read once, as a copy, and read it again when
        // the file changes.
        [Test]
        public void ASlotReadsItsFaceAgainAfterTheFileChanges()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            fixture.Mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(new Vector3(0, .03f, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
            using var session = new ExperimentalExpressionCaptureSession(fixture.Source, false);
            var clip = SmileFile(100);
            try
            {
                var assignment = ExperimentalExpressionCaptureSession.PresetAssignment.ForClip(ExpressionPreset.happy, clip);
                var first = session.BuildPresetPose(ExpressionPreset.happy, assignment);
                first.Rows[0].Weights[0] = 12;
                var second = session.BuildPresetPose(ExpressionPreset.happy, assignment);
                Assert.That(second, Is.Not.SameAs(first));
                Assert.That(second.Rows[0].Weights[0], Is.Not.EqualTo(12), "A change to one result does not reach the next.");
                Assert.That(session.PresetError(ExpressionPreset.happy, assignment), Is.Null);

                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Constant(0, 1, 0));
                Assert.That(session.PresetError(ExpressionPreset.happy, assignment), Does.Contain(ExporterLocalization.T("0より大きい変形がありません。")));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        // A retry from the recovery window, perhaps with the board closed, after
        // the avatar changed: the same export on a new copy of the avatar as it is.
        [Test]
        public void ARetryFromTheRecoveryWindowAfterAnEditExportsTheAvatarAsItIs()
        {
            using var board = new FollowBoard();
            var target = board.Session;
            board.Edit();
            FollowBoard.ExpectFollowed();
            var used = new System.Collections.Generic.List<ExperimentalExpressionCaptureSession>();
            var warnings = new System.Collections.Generic.List<string>();
            var bytes = ExperimentalExpressionCaptureWindow.ExportOnTheAvatarAsItIs(target, false, warnings, (from, sourceChecked) =>
            {
                used.Add(from);
                return from.Export("Test", "Tests", Listed(), warnings, sourceChecked: sourceChecked);
            });
            Assert.That(bytes, Is.Not.Null.And.Not.Empty);
            Assert.That(used.Count, Is.EqualTo(2));
            Assert.That(used[0], Is.SameAs(target));
            Assert.That(used[1], Is.Not.SameAs(target));
            Assert.That(target.Copy, Is.Not.Null, "The board's own copy is left to the board.");
            // A first attempt the save has just checked runs as it is.
            Assert.Throws<ExperimentalExpressionCaptureSession.SourceChangedException>(() =>
                ExperimentalExpressionCaptureWindow.ExportOnTheAvatarAsItIs(target, true, warnings, (from, sourceChecked) =>
                    { if (from != target) Assert.Fail("Only a retry rebuilds."); throw new ExperimentalExpressionCaptureSession.SourceChangedException("m", "d"); }));
        }

        // Leaving objects out rebuilds the copy from the avatar. Nothing of the
        // avatar changes, so the next action finds the copy current.
        [Test]
        public void ExcludingAnObjectDoesNotLookLikeAnEditOfTheSource()
        {
            using var board = new FollowBoard();
            board.Set("excludedObjectEdits", new System.Collections.Generic.List<GameObject> { board.Fixture.Source.transform.Find("Back").gameObject });
            board.Run(board.Delegate<Action>("ApplyExcludedObjects"));
            Assert.That(board.Get<string>("excludedObjectError"), Is.Null);
            Assert.That(board.Session.ExcludedObjects.Count, Is.EqualTo(1));
            board.Advance();
            var excluded = board.Session;
            board.Choose(ExpressionPreset.sad, "Front");
            Assert.That(board.Session, Is.SameAs(excluded));
            board.Set("excludedObjectEdits", new System.Collections.Generic.List<GameObject>());
            board.Run(board.Delegate<Action>("ApplyExcludedObjects"));
            board.Advance();
            var included = board.Session;
            board.Choose(ExpressionPreset.surprised, "Front");
            Assert.That(board.Session, Is.SameAs(included));
            board.AssertKept();
        }

        // The objects page shows its own copy of the avatar, made when the page
        // opens and dropped when it is left; another session's copy comes and
        // goes the same way. Neither reads as an edit of the avatar.
        [Test]
        public void PagesAndOtherCopiesLeaveTheSourceAsItWas()
        {
            using var board = new FollowBoard();
            var before = board.Session;
            board.Set("page", ExperimentalExpressionCaptureWindow.ObjectsPage);
            board.Window.RefreshObjectsPreview();
            Assert.That(board.Window.ObjectsPreview, Is.Not.Null);
            board.Window.GoTo(ExperimentalExpressionCaptureWindow.InfoPage);
            board.Advance();
            Assert.That(board.Window.ObjectsPreview, Is.Null);
            board.Window.GoTo(ExperimentalExpressionCaptureWindow.ExpressionsPage);
            board.Advance();
            using (new ExperimentalExpressionCaptureSession(board.Fixture.Source, false)) { }
            board.Choose(ExpressionPreset.sad, "Front");
            Assert.That(board.Session, Is.SameAs(before));
            Assert.DoesNotThrow(board.Session.RequireCurrentSource);
            board.AssertKept();
        }

        // What a domain reload does to the stamp: the same avatar hashed again
        // matches, detailed or not, and a detailed stamp names the part that changed.
        [Test]
        public void ADetailedStampMatchesTheSameAvatarAndNamesWhatChanged()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var stamp = ExportRecoverySourceStamp.Capture(fixture.Source, detailed: true);
            var plain = ExportRecoverySourceStamp.Capture(fixture.Source);
            Assert.That(stamp.Difference(fixture.Source), Is.Null);
            Assert.That(ExportRecoverySourceStamp.Capture(fixture.Source, detailed: true).Difference(fixture.Source), Is.Null);
            Assert.That(plain.Matches(fixture.Source), Is.True);
            Assert.That(ExportSourceFingerprint.Compute(fixture.Source), Is.EqualTo(ExportSourceFingerprint.Compute(fixture.Source)));
            fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 50);
            Assert.That(stamp.Difference(fixture.Source), Is.EqualTo(typeof(SkinnedMeshRenderer).FullName + " 'Back'"));
            Assert.That(plain.Difference(fixture.Source), Is.EqualTo("source"));
            Assert.That(stamp.Matches(fixture.Source), Is.False);
        }
    }

}
