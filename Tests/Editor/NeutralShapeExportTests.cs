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
    public sealed class NeutralShapeExportTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task FxDefaultOpeningShapeSurvivesExportWithoutAnExpressionMenu(bool independentBlink, bool resetClosingShape)
        {
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
                .FirstOrDefault(type => type != null);
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folderName = "__NeutralShapeExport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            var folder = "Assets/" + folderName;
            Vrm10Instance imported = null;
            try
            {
                // Opening can raise an Open morph or reset an initially applied
                // closing morph. The default FX pose supplies both changes.
                var shape = resetClosingShape ? "Blink" : "Open";
                var neutralWeight = resetClosingShape ? 0 : 100;
                fixture.Mesh.ClearBlendShapes();
                fixture.Mesh.AddBlendShapeFrame(shape, 100,
                    Enumerable.Repeat(Vector3.up * .03f, fixture.Mesh.vertexCount).ToArray(), null, null);
                if (independentBlink)
                    fixture.Mesh.AddBlendShapeFrame("Blink", 100,
                        Enumerable.Repeat(Vector3.down * .04f, fixture.Mesh.vertexCount).ToArray(), null, null);
                var sourceSkins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
                foreach (var skin in sourceSkins)
                {
                    skin.SetBlendShapeWeight(0, resetClosingShape ? 100 : 0);
                    skin.sharedMaterial.shader = Shader.Find("lilToon");
                }
                var vertices = fixture.Mesh.vertices;
                var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                var clip = new AnimationClip { name = "Open at startup" };
                foreach (var path in new[] { "Front", "Back" })
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape),
                        AnimationCurve.Constant(0, 1, neutralWeight));
                AssetDatabase.AddObjectToAsset(clip, controller);
                var machine = controller.layers[0].stateMachine;
                var state = machine.AddState("Neutral open eyes");
                state.writeDefaultValues = false;
                state.motion = clip;
                machine.defaultState = state;
                if (independentBlink)
                {
                    controller.AddLayer("Independent automatic blink");
                    var layers = controller.layers; layers[1].defaultWeight = 1; controller.layers = layers;
                    var blinkClip = new AnimationClip { name = "Automatic blink" };
                    foreach (var path in new[] { "Front", "Back" })
                        AnimationUtility.SetEditorCurve(blinkClip,
                            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.Blink"),
                            AnimationCurve.Linear(0, 0, 10, 100));
                    AssetDatabase.AddObjectToAsset(blinkClip, controller);
                    var blinkMachine = layers[1].stateMachine;
                    var blinking = blinkMachine.AddState("Blinking"); blinking.motion = blinkClip;
                    blinking.writeDefaultValues = false; blinkMachine.defaultState = blinking;
                }
                var descriptor = fixture.Source.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers");
                    layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0);
                    var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Neutral opening regression", "Tests",
                    exporterVersion: null,
                    lilToonVersion: null,
                    blinkOptions: independentBlink || resetClosingShape ? null : new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                imported.Runtime.Process();
                foreach (var skin in imported.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Assert.That(skin.sharedMesh.vertices[0].y, Is.EqualTo(vertices[0].y + .03f * neutralWeight / 100).Within(.00001),
                        "Default display must preserve the FX opening pose even with no expression menu.");
                Assert.That(sourceSkins.All(skin => skin.sharedMesh == fixture.Mesh && skin.GetBlendShapeWeight(0) == (resetClosingShape ? 100 : 0)), Is.True);
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(vertices));
                Assert.That(state.motion, Is.SameAs(clip));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // The idle FX state holds the mouth at 100 while another shape in the
        // same clip changes over time. The held mouth is the normal face.
        [Test]
        public async Task FxDefaultConstantShapeBesideATimeVaryingCurveIsBakedIntoTheBase()
        {
            var descriptorType = DescriptorType();
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folderName = "__NeutralShapeExport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            var folder = "Assets/" + folderName;
            Vrm10Instance imported = null;
            try
            {
                var sourceSkins = MouthAndBrow(fixture, 0);
                var vertices = fixture.Mesh.vertices;
                var clip = new AnimationClip { name = "Idle" };
                foreach (var path in new[] { "Front", "Back" })
                {
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.Mouth"),
                        AnimationCurve.Constant(0, 1, 100));
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.Brow"),
                        AnimationCurve.Linear(0, 0, 1, 100));
                }
                SetFx(fixture.Source, descriptorType, folder, clip);
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "FX default constant shape", "Tests", warnings,
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                imported.Runtime.Process();
                foreach (var skin in imported.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    Assert.That(skin.sharedMesh.vertices[0].y, Is.EqualTo(vertices[0].y + .03f).Within(.00001),
                        "The mouth the idle FX state holds must be part of the base.");
                    Assert.That(skin.sharedMesh.vertices[0].x, Is.EqualTo(vertices[0].x).Within(.00001),
                        "The shape that changes over time keeps the copy's value.");
                }
                Assert.That(warnings.Any(warning => warning.Contains("時間で変わる")), Is.True, string.Join("\n", warnings));
                Assert.That(sourceSkins.All(skin => skin.GetBlendShapeWeight(0) == 0 && skin.GetBlendShapeWeight(1) == 0), Is.True);
                Assert.That(fixture.Mesh.vertices, Is.EqualTo(vertices));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // When reading the FX defaults fails, the export goes on without them:
        // the copy's own values are the base, and the stage is listed as left out.
        [Test]
        public async Task FailedFxRestStillWritesTheFileWithTheCopysValues()
        {
            var descriptorType = DescriptorType();
            if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var folderName = "__NeutralShapeExport_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folderName);
            var folder = "Assets/" + folderName;
            Vrm10Instance imported = null;
            try
            {
                MouthAndBrow(fixture, 40);
                var vertices = fixture.Mesh.vertices;
                var clip = new AnimationClip { name = "Idle" };
                foreach (var path in new[] { "Front", "Back" })
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape.Mouth"),
                        AnimationCurve.Constant(0, 1, 100));
                SetFx(fixture.Source, descriptorType, folder, clip);
                var fired = 0;
                ExportStageGuard.FaultInjection = stage =>
                {
                    if (stage != ExportStage.FxRest) return;
                    fired++;
                    throw new InvalidOperationException("injected " + stage);
                };
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "FX rest left out", "Tests", new List<string>(),
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);
                Assert.That(fired, Is.EqualTo(1));
                Assert.That(report.Succeeded, Is.True);
                Assert.That(report.Fallbacks.Results.Single(result => result.Stage != null).Stage, Is.EqualTo(ExportStage.FxRest));
                Assert.That(report.Fallbacks.Items.Single(item => item.Kind == ExportFallbackKind.StageOmitted).Names,
                    Is.EqualTo(new[] { ExportStageGuard.DisplayName(ExportStage.FxRest) }));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                imported.Runtime.Process();
                foreach (var skin in imported.GetComponentsInChildren<SkinnedMeshRenderer>())
                    Assert.That(skin.sharedMesh.vertices[0].y, Is.EqualTo(vertices[0].y + .03f * .4f).Within(.00001),
                        "Without the FX defaults the copy's mouth at 40 is the base.");
            }
            finally
            {
                ExportStageGuard.FaultInjection = null;
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        static Type DescriptorType() => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
            .FirstOrDefault(type => type != null);

        // "Mouth" moves every vertex up and "Brow" moves it right.
        static SkinnedMeshRenderer[] MouthAndBrow(AttachmentConnectionTests.Fixture fixture, float mouth)
        {
            fixture.Mesh.ClearBlendShapes();
            fixture.Mesh.AddBlendShapeFrame("Mouth", 100, Enumerable.Repeat(Vector3.up * .03f, fixture.Mesh.vertexCount).ToArray(), null, null);
            fixture.Mesh.AddBlendShapeFrame("Brow", 100, Enumerable.Repeat(Vector3.right * .02f, fixture.Mesh.vertexCount).ToArray(), null, null);
            var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var skin in skins)
            {
                skin.SetBlendShapeWeight(0, mouth); skin.SetBlendShapeWeight(1, 0);
                skin.sharedMaterial.shader = Shader.Find("lilToon");
            }
            return skins;
        }

        // An FX whose only layer starts in a write-defaults-off state playing the clip.
        static void SetFx(GameObject source, Type descriptorType, string folder, AnimationClip clip)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            AssetDatabase.AddObjectToAsset(clip, controller);
            var state = controller.layers[0].stateMachine.AddState("Idle");
            state.writeDefaultValues = false; state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            var descriptor = source.AddComponent(descriptorType);
            using var data = new SerializedObject(descriptor);
            data.FindProperty("customizeAnimationLayers").boolValue = true;
            var layers = data.FindProperty("baseAnimationLayers");
            layers.arraySize = 1;
            var layer = layers.GetArrayElementAtIndex(0);
            var type = layer.FindPropertyRelative("type");
            type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
            layer.FindPropertyRelative("isDefault").boolValue = false;
            layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
