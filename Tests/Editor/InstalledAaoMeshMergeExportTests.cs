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
    // The automatic mesh merge with the installed AAO and NDMF, through the
    // public export: the face and a toggled hat stay separate, the rest becomes
    // one renderer, and the shape stays where it was.
    public sealed class InstalledAaoMeshMergeExportTests
    {
        sealed class Fixture : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Avatar = new AttachmentConnectionTests.Fixture();
            internal readonly string Folder = TempAssetFolder.Create("__AaoMeshMerge_");
            readonly List<Object> owned = new List<Object>();
            internal SkinnedMeshRenderer Face;

            internal Fixture()
            {
                var descriptorType = OptionalTools.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                var traceType = OptionalTools.Find("Anatawa12.AvatarOptimizer.TraceAndOptimize");
                var head = Avatar.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
                Face = Skin("Face", head, Vector3.forward * .1f, "Blink");
                Skin("Hat", head, Vector3.up * .2f);
                var controller = AnimatorController.CreateAnimatorControllerAtPath(Folder + "/FX.controller");
                var clip = new AnimationClip { name = "Hat off" }; AssetDatabase.AddObjectToAsset(clip, controller);
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 0));
                var machine = controller.layers[0].stateMachine;
                machine.defaultState = machine.AddState("On");
                machine.AddState("Off").motion = clip;
                using (var data = new SerializedObject(Avatar.Source.AddComponent(descriptorType)))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0); var kind = layer.FindPropertyRelative("type");
                    kind.enumValueIndex = Array.IndexOf(kind.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                // Trace and Optimize marks the avatar as an AAO avatar. Its own
                // automatic merge is off so that only the exporter's merge counts.
                // Its cleanup is off too: no mesh here is weighted to the arms or
                // legs, so it would remove those humanoid bones and every
                // optimized export would fall back to the unoptimized one.
                using (var trace = new SerializedObject(Avatar.Source.AddComponent(traceType)))
                {
                    trace.FindProperty("mergeSkinnedMesh").boolValue = false;
                    trace.FindProperty("removeUnusedObjects").boolValue = false;
                    trace.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            SkinnedMeshRenderer Skin(string name, Transform bone, Vector3 offset, params string[] shapes)
            {
                var node = new GameObject(name).transform; node.SetParent(Avatar.Source.transform, false);
                var mesh = new Mesh { name = name };
                var center = bone.position + offset;
                mesh.vertices = new[] { center + Vector3.left * .05f, center + Vector3.right * .05f, center + Vector3.up * .05f };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
                mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 3).ToArray();
                mesh.bindposes = new[] { bone.worldToLocalMatrix };
                foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.down * .02f, 3).ToArray(), null, null);
                var skin = node.gameObject.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = bone;
                skin.sharedMaterial = Avatar.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial;
                owned.Add(mesh);
                return skin;
            }

            public void Dispose()
            {
                Avatar.Dispose();
                foreach (var item in owned) Object.DestroyImmediate(item);
                AssetDatabase.DeleteAsset(Folder);
            }
        }

        static void RequireInstalledTools()
        {
            if (OptionalTools.Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") == null ||
                OptionalTools.Find("Anatawa12.AvatarOptimizer.MergeSkinnedMesh") == null ||
                OptionalTools.Find("Anatawa12.AvatarOptimizer.TraceAndOptimize") == null || OptionalTools.Find("nadena.dev.ndmf.BuildContext") == null)
                Assert.Ignore("Install the real VRChat SDK, AAO and NDMF for this native integration test.");
            Assert.That(ExportMeshMerge.Available, Is.True, "The AAO adapter registers the merge.");
        }

        static async Task<(Vrm10Instance Imported, ExportRecoveryReport Report)> Export(Fixture fixture, bool merge)
        {
            var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
            var blink = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
            blink.Both.Add(new BlinkShapeBinding { Renderer = fixture.Face, Shape = "Blink" });
            var bytes = UniVrmOneClickExporter.Export(fixture.Avatar.Source, "AAO mesh merge", "Tests", warnings: new List<string>(),
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                blinkOptions: blink, recoveryReport: report, mergeMeshes: merge);
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            imported.Runtime.Process();
            return (imported, report);
        }

        static IEnumerable<ExportStage?> Stages(ExportRecoveryReport report) =>
            report.Fallbacks.Results.Where(result => result.Stage != null).Select(result => result.Stage);

        // AAO reads the vertex buffers back from the GPU (MeshInfo2), so
        // without one Optimizing fails: the export leaves it out, and with it
        // the merge, and still succeeds.
        static void IgnoreWithoutGraphics(params (Vrm10Instance Imported, ExportRecoveryReport Report)[] exports)
        {
            if (!NoGraphicsDevice.Missing) return;
            foreach (var (imported, report) in exports)
            {
                Assert.That(report.Succeeded, Is.True);
                Assert.That(Stages(report), Does.Contain(ExportStage.Optimizing));
                Assert.That(Renderers(imported), Does.Not.Contain(ExportMeshMerge.MergedName));
            }
            Assert.Ignore("Avatar Optimizer reads the vertex buffers back from the GPU; without one Optimizing is left out.");
        }

        static string[] Renderers(Vrm10Instance imported) =>
            imported.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        static List<Vector3> Triangles(Vrm10Instance imported)
        {
            var result = new List<Vector3>(); var baked = new Mesh();
            try
            {
                foreach (var skin in imported.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    skin.BakeMesh(baked); var vertices = baked.vertices;
                    result.AddRange(baked.triangles.Select(index => skin.transform.TransformPoint(vertices[index])));
                }
                return result;
            }
            finally { Object.DestroyImmediate(baked); }
        }

        static void SameShape(List<Vector3> actual, List<Vector3> expected, string message)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), message);
            var unmatched = expected.ToList();
            foreach (var point in actual)
            {
                var index = unmatched.FindIndex(value => Vector3.Distance(value, point) < .0001f);
                Assert.That(index, Is.GreaterThanOrEqualTo(0), message + ": unexpected vertex " + point.ToString("G9"));
                unmatched.RemoveAt(index);
            }
        }

        [Test]
        public async Task TheFaceAndTheToggledHatStaySeparateAndTheRestBecomesOne()
        {
            RequireInstalledTools();
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new Fixture();
            Vrm10Instance separate = null, merged = null;
            try
            {
                ExportRecoveryReport separateReport, mergedReport;
                (separate, separateReport) = await Export(fixture, false);
                (merged, mergedReport) = await Export(fixture, true);
                IgnoreWithoutGraphics((separate, separateReport), (merged, mergedReport));
                foreach (var report in new[] { separateReport, mergedReport })
                    Assert.That(Stages(report), Is.Empty, "Both exports keep the optimizer.");
                Assert.That(Renderers(separate), Is.EqualTo(new[] { "Back", "Face", "Front", "Hat" }));
                Assert.That(Renderers(merged), Is.EqualTo(new[] { "Face", "Hat", ExportMeshMerge.MergedName }));
                SameShape(Triangles(merged), Triangles(separate), "The merge must not move any vertex");
                foreach (var imported in new[] { separate, merged })
                {
                    Assert.That(imported.Vrm.Expression.Blink, Is.Not.Null, "The blink still reaches the face.");
                    var before = Triangles(imported);
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process();
                    var after = Triangles(imported);
                    imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0); imported.Runtime.Process();
                    Assert.That(before.Zip(after, (a, b) => Vector3.Distance(a, b)).Count(distance => distance > .001f), Is.EqualTo(3),
                        "The blink moves the three face vertices and nothing else.");
                }
                // The source keeps its renderers; only the export copy is merged.
                Assert.That(fixture.Avatar.Source.transform.Find(ExportMeshMerge.MergedName), Is.Null);
                Assert.That(fixture.Avatar.Source.transform.Find("Front"), Is.Not.Null);
            }
            finally
            {
                if (separate != null) Object.DestroyImmediate(separate.gameObject);
                if (merged != null) Object.DestroyImmediate(merged.gameObject);
            }
        }

        // A failure after the merge was added leaves out the merge first and
        // keeps the rest of the optimizer.
        [Test]
        public async Task AFailureAfterTheMergeLeavesOutOnlyTheMerge()
        {
            RequireInstalledTools();
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new Fixture();
            Vrm10Instance imported = null;
            var failures = 0;
            ExportTestHooks.ConversionFaultInjection = copy =>
            {
                if (failures++ == 0) throw new InvalidOperationException("Injected conversion failure");
            };
            try
            {
                ExportRecoveryReport report;
                (imported, report) = await Export(fixture, true);
                IgnoreWithoutGraphics((imported, report));
                Assert.That(report.Succeeded, Is.True);
                Assert.That(Stages(report), Is.EqualTo(new ExportStage?[] { ExportStage.MeshMerge }));
                Assert.That(Renderers(imported), Is.EqualTo(new[] { "Back", "Face", "Front", "Hat" }));
            }
            finally
            {
                ExportTestHooks.ConversionFaultInjection = null;
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
            }
        }
    }
}
