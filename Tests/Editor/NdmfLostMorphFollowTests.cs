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
    // Runs the installed MA/NDMF pipeline with a test pass after MA that
    // strips, renames or merges the face renderer. A lenient export keeps the
    // build passes, follows the shapes by name and drops only lost ones.
    public sealed class NdmfLostMorphFollowTests
    {
        const string PluginType = "VRVlog.LilToonExporter.Tests.LostMorphTestPlugin";
        const string Marker = "__VRVlogLostMorphTest_";
        const string OldGenerated = "__VRVlog_Menu_old";
        const string Renamed = "Face renamed";
        const string Missing = "Mouth missing";
        const string Stripped = "Mouth stripped";

        static Type InstalledType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        static void SetFx(Component descriptor, RuntimeAnimatorController controller)
        {
            using var data = new SerializedObject(descriptor);
            data.FindProperty("customizeAnimationLayers").boolValue = true;
            var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
            var layer = layers.GetArrayElementAtIndex(0);
            var type = layer.FindPropertyRelative("type"); type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
            layer.FindPropertyRelative("isDefault").boolValue = false;
            layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        static AnimatorController DefaultController(string asset)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(asset);
            var machine = controller.layers[0].stateMachine;
            var state = machine.AddState("Neutral"); state.writeDefaultValues = false;
            machine.defaultState = state;
            return controller;
        }

        static Mesh ShapedMesh(Mesh template, params (string Name, Vector3 Delta)[] shapes)
        {
            var mesh = Object.Instantiate(template); mesh.name = template.name;
            mesh.ClearBlendShapes();
            foreach (var shape in shapes)
                mesh.AddBlendShapeFrame(shape.Name, 100, Enumerable.Repeat(shape.Delta, mesh.vertexCount).ToArray(), null, null);
            return mesh;
        }

        sealed class Scene : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Fixture = new AttachmentConnectionTests.Fixture();
            internal SkinnedMeshRenderer Front, Back;
            internal readonly List<Object> Owned = new List<Object>();
            internal string Folder;

            // visemes names the Lip Sync shapes (aa, ee, ih, oh, ou); Front
            // gets each non-null one except Missing, which only the descriptor names.
            internal Scene(string mode, Type descriptorType, Type proxyType, string[] visemes = null)
            {
                var folderName = "__NdmfLostMorph_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", folderName); Folder = "Assets/" + folderName;
                var source = Fixture.Source;
                Front = source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                Back = source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
                Front.sharedMesh = ShapedMesh(Fixture.Mesh, new[] { ("Face blink", new Vector3(0, -.04f, 0)), ("Face smile", new Vector3(0, 0, .05f)),
                    ("Unused", new Vector3(.03f, 0, 0)), (OldGenerated, new Vector3(-.03f, 0, 0)) }.Concat((visemes ?? Array.Empty<string>())
                    .Where(name => name != null && name != Missing).Select(name => (name, new Vector3(0, .05f, 0)))).ToArray());
                // In "twin" Back has its own shape with the face blink name.
                Back.sharedMesh = mode == "twin"
                    ? ShapedMesh(Fixture.Mesh, ("Back detail", new Vector3(.02f, .01f, 0)), ("Face blink", new Vector3(0, -.04f, 0)))
                    : ShapedMesh(Fixture.Mesh, ("Back detail", new Vector3(.02f, .01f, 0)));
                Owned.Add(Front.sharedMesh); Owned.Add(Back.sharedMesh);
                foreach (var skin in new[] { Front, Back })
                {
                    for (var index = 0; index < skin.sharedMesh.blendShapeCount; index++) skin.SetBlendShapeWeight(index, 0);
                    skin.sharedMaterial.shader = Shader.Find("lilToon");
                }
                var descriptor = source.AddComponent(descriptorType);
                SetFx(descriptor, DefaultController(Folder + "/Base.controller"));
                if (visemes != null)
                {
                    var lipSync = descriptorType.GetField("lipSync");
                    lipSync.SetValue(descriptor, Enum.Parse(lipSync.FieldType, "VisemeBlendShape"));
                    descriptorType.GetField("VisemeSkinnedMesh").SetValue(descriptor, Front);
                    var slots = new string[15];
                    Array.Copy(visemes, 0, slots, 10, visemes.Length);
                    descriptorType.GetField("VisemeBlendShapes").SetValue(descriptor, slots);
                }
                // MA moves Back under the head, so the output shows that MA ran.
                var proxy = Back.gameObject.AddComponent(proxyType);
                proxyType.GetField("boneReference").SetValue(proxy, HumanBodyBones.Head);
                var attachment = proxyType.GetField("attachmentMode");
                attachment.SetValue(proxy, Enum.Parse(attachment.FieldType, "AsChildKeepWorldPose"));
                var vrm = ScriptableObject.CreateInstance<VRM10Object>(); Owned.Add(vrm);
                VRM10Expression Clip(string name, params MorphTargetBinding[] binds)
                {
                    var clip = ScriptableObject.CreateInstance<VRM10Expression>(); clip.name = name; Owned.Add(clip);
                    clip.MorphTargetBindings = binds; vrm.Expression.CustomClips.Add(clip); return clip;
                }
                Clip("Smile", new MorphTargetBinding("Front", 1, 1));
                Clip("Mixed", new MorphTargetBinding("Front", 1, 1), new MorphTargetBinding("Front", 2, 1));
                Clip("Unused only", new MorphTargetBinding("Front", 2, 1));
                source.AddComponent<Vrm10Instance>().Vrm = vrm;
                new GameObject(Marker + mode).transform.SetParent(source.transform, false);
            }

            internal byte[] Export(List<string> warnings, ExportRecoveryReport report, VrChatVisemeExport.Captured visemes = null)
            {
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
                blink.Both.Add(new BlinkShapeBinding { Renderer = Front, Shape = "Face blink", Weight = 100 });
                return UniVrmOneClickExporter.Export(Fixture.Source, "NDMF lost morph", "Tests", warnings,
                    blinkOptions: blink, recoveryReport: report, descriptorVisemes: visemes);
            }

            public void Dispose()
            {
                Fixture.Dispose();
                foreach (var value in Owned) if (value != null) Object.DestroyImmediate(value);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        // The test pass stays inert outside these tests.
        static void Arm(bool armed) => InstalledType(PluginType)?.GetField("Armed")?.SetValue(null, armed);

        [SetUp]
        public void SetUp() => Arm(true);

        [TearDown]
        public void TearDown() => Arm(false);

        static (Type Descriptor, Type Proxy) RequireInstalled()
        {
            var descriptorType = InstalledType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            var proxyType = InstalledType("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (descriptorType == null || proxyType == null || InstalledType(PluginType) == null)
                Assert.Ignore("Install the real VRChat SDK, Modular Avatar and NDMF to run their real processing pipeline.");
            return (descriptorType, proxyType);
        }

        // The output shape a bind addresses moves only three vertices, along
        // the expected axis. Generated endpoints may rename the shape itself.
        static void AssertShape(Vrm10Instance imported, MorphTargetBinding bind, int axis, string message)
        {
            var skin = imported.transform.Find(bind.RelativePath)?.GetComponent<SkinnedMeshRenderer>();
            Assert.That(skin, Is.Not.Null, message + ": " + bind.RelativePath);
            var mesh = skin.sharedMesh;
            Assert.That(bind.Index, Is.InRange(0, mesh.blendShapeCount - 1), message);
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(bind.Index, mesh.GetBlendShapeFrameCount(bind.Index) - 1, deltas, null, null);
            var moving = deltas.Where(delta => delta.magnitude > 1e-4f).ToArray();
            message += " (" + bind.RelativePath + " #" + bind.Index + " of " + string.Join(", ", Enumerable.Range(0, mesh.blendShapeCount).Select(index =>
            {
                var values = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, values, null, null);
                return mesh.GetBlendShapeName(index) + ":" + values.Count(value => value.magnitude > 1e-4f);
            })) + ", vertices " + mesh.vertexCount + ")";
            Assert.That(moving.Length, Is.EqualTo(3), message);
            foreach (var delta in moving)
                Assert.That(Mathf.Abs(delta[axis]), Is.GreaterThan(.9f * delta.magnitude), message);
        }

        static void AssertBuildKept(ExportRecoveryReport report, List<string> warnings, Vrm10Instance imported)
        {
            Assert.That(report.Fallbacks.Results.Where(result => result.Stage != null).Select(result => result.Stage.ToString()),
                Is.Empty, string.Join("\n", warnings));
            var back = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(skin => skin.name == "Back");
            Assert.That(AnimationUtility.CalculateTransformPath(back.transform.parent, imported.transform),
                Is.EqualTo("Hips/Spine/Chest/Neck/Head"), "The MA Bone Proxy must still be applied.");
        }

        // The summed motion of the binds; generated endpoints may combine them into one shape.
        static Vector3[] Combined(Vrm10Instance imported, IEnumerable<MorphTargetBinding> binds)
        {
            Vector3[] sum = null;
            foreach (var bind in binds)
            {
                var mesh = imported.transform.Find(bind.RelativePath).GetComponent<SkinnedMeshRenderer>().sharedMesh;
                var deltas = new Vector3[mesh.vertexCount];
                mesh.GetBlendShapeFrameVertices(bind.Index, mesh.GetBlendShapeFrameCount(bind.Index) - 1, deltas, null, null);
                sum = sum == null ? deltas.Select(delta => delta * bind.Weight).ToArray() : sum.Zip(deltas, (a, b) => a + b * bind.Weight).ToArray();
            }
            return sum ?? Array.Empty<Vector3>();
        }

        static MorphTargetBinding[] Clip(Vrm10Instance imported, string name) =>
            imported.Vrm.Expression.CustomClips.Single(clip => clip.name == name).MorphTargetBindings;

        [TestCase("strip")]
        [TestCase("rename")]
        [TestCase("merge")]
        public async Task LenientExportFollowsShapesAfterAPassBeyondModularAvatar(string mode)
        {
            var installed = RequireInstalled();
            using var scene = new Scene(mode, installed.Descriptor, installed.Proxy);
            Vrm10Instance imported = null;
            try
            {
                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var bytes = scene.Export(warnings, report);
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null, string.Join("\n", warnings));
                AssertBuildKept(report, warnings, imported);
                var log = string.Join("\n", warnings);
                var face = mode == "merge" ? "Back" : mode == "rename" ? Renamed : "Front";

                var blink = imported.Vrm.Expression.Blink.MorphTargetBindings;
                Assert.That(blink, Is.Not.Empty, log);
                Assert.That(blink.Select(bind => bind.RelativePath).Distinct(), Is.EqualTo(new[] { PathOf(imported, face) }), log);
                AssertShape(imported, blink.Single(), 1, "Blink must close with the face shape");

                var smile = Clip(imported, "Smile");
                Assert.That(smile.Length, Is.EqualTo(1), log);
                Assert.That(smile[0].RelativePath, Is.EqualTo(PathOf(imported, face)), log);
                AssertShape(imported, smile[0], 2, "Smile must point at the face smile shape");

                var mixed = Clip(imported, "Mixed");
                Assert.That(mixed.Any(bind => bind.RelativePath == PathOf(imported, face)), Is.True, log);
                var results = report.Fallbacks.Results;
                if (mode == "strip")
                {
                    // Only the stripped binds go; the expression with nothing left is listed as omitted.
                    Assert.That(mixed.Length, Is.EqualTo(1), log);
                    AssertShape(imported, mixed[0], 2, "Mixed keeps its smile shape");
                    Assert.That(results.Any(result => result.Outcome == ExportOutcome.Omitted && result.Category == "表情" && result.Name == "Unused only"), Is.True, log);
                    Assert.That(results.Any(result => result.Outcome == ExportOutcome.Approximated && result.Name == "Mixed"), Is.True, log);
                    Assert.That(results.Any(result => result.Outcome == ExportOutcome.Omitted && result.Name == OldGenerated), Is.True, log);
                    Assert.That(warnings.Any(warning => warning.Contains("Unused only")), Is.True, log);
                    Assert.That(warnings.Any(warning => warning.Contains("Mixed") && warning.Contains("Unused")), Is.True, log);
                    Assert.That(warnings.Any(warning => warning.Contains(OldGenerated)), Is.True, log);
                }
                else
                {
                    // Nothing is lost; each moved bind is listed as approximated.
                    // Mixed still moves both the smile (z) and the unused shape (x).
                    var motion = Combined(imported, mixed).Where(delta => delta.magnitude > 1e-4f).ToArray();
                    Assert.That(motion.Length, Is.EqualTo(3), log);
                    Assert.That(motion.All(delta => Mathf.Abs(delta.x) > .01f && Mathf.Abs(delta.z) > .01f), Is.True, log);
                    foreach (var name in new[] { "Smile", "Mixed", "Unused only" })
                    {
                        Assert.That(results.Any(result => result.Outcome == ExportOutcome.Approximated && result.Category == "表情" && result.Name == name), Is.True, name + "\n" + log);
                        Assert.That(warnings.Any(warning => warning.Contains("「" + name + "」")), Is.True, name + "\n" + log);
                    }
                    Assert.That(results.Any(result => result.Outcome == ExportOutcome.Approximated && result.Category == "瞬き"), Is.True, log);
                    Assert.That(results.Any(result => result.Outcome == ExportOutcome.Omitted), Is.False, log);
                }
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
            }
        }

        // Front loses its blink shape while Back, whose renderer is replaced,
        // has a shape of the same name. Back does not carry Front's other
        // shapes, so blink is left out instead of moving to Back.
        [Test]
        public async Task LenientExportDoesNotFollowAShapeIntoAnotherMeshWithTheSameName()
        {
            var installed = RequireInstalled();
            using var scene = new Scene("twin", installed.Descriptor, installed.Proxy);
            Vrm10Instance imported = null;
            try
            {
                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var bytes = scene.Export(warnings, report);
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null, string.Join("\n", warnings));
                AssertBuildKept(report, warnings, imported);
                var log = string.Join("\n", warnings);
                Assert.That(warnings.Any(warning => warning.StartsWith(ExporterLocalization.T(UniVrmOneClickExporter.LostBlinkWarning), StringComparison.Ordinal)), Is.True, log);
                Assert.That(report.Fallbacks.Results.Any(result => result.Outcome == ExportOutcome.Omitted && result.Category == "瞬き"), Is.True, log);
                Assert.That(Combined(imported, imported.Vrm.Expression.Blink.MorphTargetBindings).All(delta => delta.magnitude <= 1e-4f), Is.True, log);
                var smile = Clip(imported, "Smile");
                Assert.That(smile.Length, Is.EqualTo(1), log);
                Assert.That(smile[0].RelativePath, Is.EqualTo(PathOf(imported, "Front")), log);
                AssertShape(imported, smile[0], 2, "Smile stays on the face");
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
            }
        }

        // The Lip Sync captured before the build passes, as the recorded
        // expression export does, follows the face to the renamed or merged
        // renderer. A name the face never had is left out on its own.
        [TestCase("rename", false)]
        [TestCase("merge", true)]
        public async Task LenientExportFollowsLipSyncToTheRenamedOrMergedFace(string mode, bool missing)
        {
            var installed = RequireInstalled();
            var mouths = new[] { "Mouth a", "Mouth e", "Mouth i", "Mouth o", missing ? Missing : "Mouth u" };
            using var scene = new Scene(mode, installed.Descriptor, installed.Proxy, mouths);
            Vrm10Instance imported = null;
            try
            {
                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var bytes = scene.Export(warnings, report, VrChatVisemeExport.Read(scene.Fixture.Source));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null, string.Join("\n", warnings));
                var log = string.Join("\n", warnings);
                var face = PathOf(imported, mode == "merge" ? "Back" : Renamed);
                var expression = imported.Vrm.Expression;
                var bound = new[] { expression.Aa, expression.Ee, expression.Ih, expression.Oh, expression.Ou };
                for (var index = 0; index < mouths.Length; index++)
                {
                    var binds = bound[index].MorphTargetBindings;
                    if (mouths[index] == Missing)
                    {
                        Assert.That(binds, Is.Null.Or.Empty, log);
                        continue;
                    }
                    Assert.That(binds.Length, Is.EqualTo(1), mouths[index] + "\n" + log);
                    Assert.That(binds[0].RelativePath, Is.EqualTo(face), mouths[index] + "\n" + log);
                    var mesh = imported.transform.Find(face).GetComponent<SkinnedMeshRenderer>().sharedMesh;
                    Assert.That(mesh.GetBlendShapeName(binds[0].Index), Is.EqualTo(mouths[index]), log);
                    AssertShape(imported, binds[0], 1, mouths[index] + " must open the mouth");
                }
                bool Warned(string message) => warnings.Contains(message) || warnings.Contains(ExporterLocalization.T(message));
                Assert.That(Warned(VrmExpressionBindings.UnexportedVisemeMeshWarning), Is.False, log);
                Assert.That(Warned(VrmExpressionBindings.UnboundVisemeWarning), Is.EqualTo(missing), log);
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
            }
        }

        // A pass strips one Lip Sync shape from the face but keeps its
        // renderer. The other mouth shapes stay on that renderer and only the
        // stripped one is left out; the Lip Sync mesh is still exported.
        [Test]
        public async Task LenientExportKeepsTheOtherMouthShapesWhenAPassStripsOne()
        {
            var installed = RequireInstalled();
            var mouths = new[] { "Mouth a", "Mouth e", "Mouth i", "Mouth o", Stripped };
            using var scene = new Scene("strip", installed.Descriptor, installed.Proxy, mouths);
            Vrm10Instance imported = null;
            try
            {
                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var bytes = scene.Export(warnings, report, VrChatVisemeExport.Read(scene.Fixture.Source));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null, string.Join("\n", warnings));
                var log = string.Join("\n", warnings);
                var face = PathOf(imported, "Front");
                var expression = imported.Vrm.Expression;
                var bound = new[] { expression.Aa, expression.Ee, expression.Ih, expression.Oh, expression.Ou };
                for (var index = 0; index < mouths.Length; index++)
                {
                    var binds = bound[index].MorphTargetBindings;
                    if (mouths[index] == Stripped)
                    {
                        Assert.That(binds, Is.Null.Or.Empty, log);
                        continue;
                    }
                    Assert.That(binds.Length, Is.EqualTo(1), mouths[index] + "\n" + log);
                    Assert.That(binds[0].RelativePath, Is.EqualTo(face), mouths[index] + "\n" + log);
                    var mesh = imported.transform.Find(face).GetComponent<SkinnedMeshRenderer>().sharedMesh;
                    Assert.That(mesh.GetBlendShapeName(binds[0].Index), Is.EqualTo(mouths[index]), log);
                    AssertShape(imported, binds[0], 1, mouths[index] + " must open the mouth");
                }
                bool Warned(string message) => warnings.Contains(message) || warnings.Contains(ExporterLocalization.T(message));
                Assert.That(Warned(VrmExpressionBindings.UnexportedVisemeMeshWarning), Is.False, log);
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
            }
        }

        // The strict setting keeps stopping on a merge it cannot prove.
        [Test]
        public void StrictExportStillStopsWhenTheFaceIsMerged()
        {
            var installed = RequireInstalled();
            using var scene = new Scene("merge", installed.Descriptor, installed.Proxy);
            Assert.Throws(Is.InstanceOf<Exception>(), () => scene.Export(new List<string>(), new ExportRecoveryReport()));
        }

        // An Optimizing pass renames the face shapes, as optimizers do when they
        // merge shapes animated together. The board reads the avatar's own
        // clips on its copy as the export reads them, after Transforming, so
        // the face stays a face; the export follows Optimizing on its own.
        [Test]
        public void BoardReadsTheAvatarClipsBeforeOptimizingPassesRenameTheirShapes()
        {
            var installed = RequireInstalled();
            using var scene = new Scene(LostMorphTestPluginOptimize, installed.Descriptor, installed.Proxy);
            var source = scene.Fixture.Source;
            var smile = new AnimationClip { name = "Smile clip" };
            smile.SetCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Face smile", AnimationCurve.Constant(0, 0, 100));
            AssetDatabase.CreateAsset(smile, scene.Folder + "/Smile.anim");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(scene.Folder + "/Base.controller");
            controller.layers[0].stateMachine.AddState("Smile").motion = smile;

            // The export's own preparation runs the pass: the test would
            // otherwise prove nothing.
            var exportCopy = NdmfExportPreparation.InstantiateOwnedCopy(source);
            try
            {
                using (NdmfExportPreparation.Prepare(source, exportCopy, afterTransforming: _ => { }))
                    Assert.That(exportCopy.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMesh.GetBlendShapeIndex("Face smile" + OptimizedSuffix),
                        Is.GreaterThanOrEqualTo(0), "The test Optimizing pass must rename the shapes of an export.");
            }
            finally { Object.DestroyImmediate(exportCopy); }

            using var session = new ExperimentalExpressionCaptureSession(source, false);
            Assert.That(session.ClipError(smile, 0), Is.Null);
            var face = session.Copy.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMesh;
            Assert.That(face.GetBlendShapeIndex("Face smile"), Is.GreaterThanOrEqualTo(0), "The board's copy stops before Optimizing.");
        }

        const string LostMorphTestPluginOptimize = "optimize";
        const string OptimizedSuffix = " (optimized)";

        static string PathOf(Vrm10Instance imported, string name) => AnimationUtility.CalculateTransformPath(
            imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(skin => skin.name == name).transform, imported.transform);
    }
}
