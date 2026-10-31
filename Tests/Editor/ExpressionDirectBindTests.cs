using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.Expressions;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Resolves direct references by renderer name in a glb written straight
    // from the export copy, where no optimizer has moved any shape.
    internal sealed class NamedDirectBindings : VrmMenuExpressions.IDirectBindings
    {
        public Dictionary<string, object> MenuBinding(Dictionary<string, object> json, SkinnedMeshRenderer renderer, string shape, float weight)
        {
            var nodes = (List<object>)json["nodes"]; var meshes = (List<object>)json["meshes"];
            var matches = new List<(long Node, long Index)>();
            for (var node = 0; node < nodes.Count; node++)
            {
                var value = (Dictionary<string, object>)nodes[node];
                if (!value.TryGetValue("mesh", out var mesh) || !value.TryGetValue("name", out var name) || !string.Equals(name as string, renderer.name, StringComparison.Ordinal)) continue;
                var names = (List<object>)((Dictionary<string, object>)((Dictionary<string, object>)meshes[Convert.ToInt32(mesh)])["extras"])["targetNames"];
                var index = names.FindIndex(target => string.Equals(target as string, shape, StringComparison.Ordinal));
                if (index >= 0) matches.Add((node, index));
            }
            Assert.That(matches, Has.Count.EqualTo(1), renderer.name + " / " + shape);
            return new Dictionary<string, object> { ["node"] = matches[0].Node, ["index"] = matches[0].Index, ["weight"] = (double)weight };
        }
    }

    // A menu channel that only raises one single-frame shape toward its frame
    // is exported as that shape at (w - rest) / (F - rest); everything else
    // stays a baked target. The expected poses below evaluate the authored
    // source frames independently of the exporter.
    public sealed class ExpressionDirectBindTests
    {
        const float Tolerance = .000001f;
        static readonly string[] Shapes = { "Raise", "Lower", "Curve", "Wide" };

        sealed class Scene : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Avatar = new AttachmentConnectionTests.Fixture();
            internal readonly List<Mesh> Owned = new List<Mesh>();
            internal readonly float[] Rest;

            internal Scene(float raise = 0, float lower = 0, float curve = 0, float wide = 0)
            {
                Rest = new[] { raise, lower, curve, wide };
                var mesh = Avatar.Mesh; var count = mesh.vertexCount; mesh.ClearBlendShapes();
                Vector3[] Ramp(Vector3 value) => Enumerable.Range(1, count).Select(index => value * index).ToArray();
                mesh.AddBlendShapeFrame("Raise", 100, Ramp(new Vector3(0, .03f, 0)), Ramp(new Vector3(0, 0, .1f)), null);
                mesh.AddBlendShapeFrame("Lower", 100, Ramp(new Vector3(.04f, 0, 0)), null, null);
                // Two frames that are not collinear: a curved channel.
                mesh.AddBlendShapeFrame("Curve", 50, Ramp(new Vector3(0, 0, .01f)), null, null);
                mesh.AddBlendShapeFrame("Curve", 100, Ramp(new Vector3(.03f, 0, .05f)), null, null);
                mesh.AddBlendShapeFrame("Wide", 50, Ramp(new Vector3(.02f, .02f, 0)), null, null);
                foreach (var skin in Avatar.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Concat(Avatar.Skins))
                    for (var shape = 0; shape < Rest.Length; shape++) skin.SetBlendShapeWeight(shape, Rest[shape]);
            }

            internal SkinnedMeshRenderer Copy(string path) => Avatar.Copy.transform.Find(path).GetComponent<SkinnedMeshRenderer>();

            internal List<VrmMenuExpressions.Expression> Bake(params VrChatExpressionMenu.Entry[] entries)
            {
                var menu = new VrChatExpressionMenu.Source(); menu.Entries.AddRange(entries);
                AvatarBaseShape.Preserve(Avatar.Source, Avatar.Copy, Owned, null);
                return VrChatExpressionBaker.Bake(Avatar.Source, Avatar.Copy, menu, Owned, null);
            }

            // The authored source pose: rest weights with the entry values on
            // this renderer, evaluated from the original frames.
            internal Vector3[] Expected(VrChatExpressionMenu.Entry entry, string path)
            {
                var weights = (float[])Rest.Clone();
                foreach (var value in entry.Values.Where(value => value.Path == path)) weights[Array.IndexOf(Shapes, value.Shape)] = value.Weight;
                return Pose(Avatar.Mesh, weights);
            }

            public void Dispose() { Avatar.Dispose(); foreach (var mesh in Owned) Object.DestroyImmediate(mesh); }
        }

        static VrChatExpressionMenu.Entry Entry(string name, params (string Path, string Shape, float Weight)[] values)
        {
            var entry = new VrChatExpressionMenu.Entry { Id = name, Name = name };
            foreach (var value in values) entry.Values.Add(new VrChatExpressionMenu.MorphValue { Path = value.Path, Shape = value.Shape, Weight = value.Weight });
            return entry;
        }

        static Vector3[] Pose(Mesh mesh, float[] weights)
        {
            var result = mesh.vertices;
            for (var shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                var frames = mesh.GetBlendShapeFrameCount(shape);
                var knots = new List<(float Weight, Vector3[] Delta)> { (0, new Vector3[mesh.vertexCount]) };
                for (var frame = 0; frame < frames; frame++)
                {
                    var value = new Vector3[mesh.vertexCount]; mesh.GetBlendShapeFrameVertices(shape, frame, value, null, null);
                    knots.Add((mesh.GetBlendShapeFrameWeight(shape, frame), value));
                }
                Assert.That(weights[shape], Is.InRange(0f, knots[knots.Count - 1].Weight), "Fixtures stay inside the authored frames.");
                var upper = Math.Max(1, knots.FindIndex(knot => knot.Weight >= weights[shape]));
                var lower = knots[upper - 1]; var factor = (weights[shape] - lower.Weight) / (knots[upper].Weight - lower.Weight);
                for (var vertex = 0; vertex < result.Length; vertex++) result[vertex] += Vector3.LerpUnclamped(lower.Delta[vertex], knots[upper].Delta[vertex], factor);
            }
            return result;
        }

        // glTF semantics on the export copy: a bind of weight x adds x times
        // the last frame of its morph; generated targets bind at weight 1.
        static Vector3[] Exported(SkinnedMeshRenderer copy, VrmMenuExpressions.Expression expression)
        {
            var mesh = copy.sharedMesh; var result = mesh.vertices; var delta = new Vector3[mesh.vertexCount];
            void Add(string shape, float weight)
            {
                var index = mesh.GetBlendShapeIndex(shape); Assert.That(index, Is.GreaterThanOrEqualTo(0), shape);
                mesh.GetBlendShapeFrameVertices(index, mesh.GetBlendShapeFrameCount(index) - 1, delta, null, null);
                for (var vertex = 0; vertex < result.Length; vertex++) result[vertex] += delta[vertex] * weight;
            }
            foreach (var target in expression.Targets.Where(target => mesh.GetBlendShapeIndex(target) >= 0)) Add(target, 1);
            foreach (var direct in expression.Direct.Where(direct => direct.Renderer == copy)) Add(direct.Shape, direct.Weight);
            return result;
        }

        static void AssertVertices(Vector3[] expected, Vector3[] actual, string message)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), message);
            for (var vertex = 0; vertex < expected.Length; vertex++)
                Assert.That(Vector3.Distance(actual[vertex], expected[vertex]), Is.LessThanOrEqualTo(Tolerance), message + " / vertex " + vertex);
        }

        static void AssertDirect(VrmMenuExpressions.Expression expression, SkinnedMeshRenderer renderer, string shape, float weight)
        {
            var direct = expression.Direct.Single(value => value.Renderer == renderer && value.Shape == shape);
            Assert.That(direct.Weight, Is.EqualTo(weight).Within(Tolerance), shape);
        }

        [Test]
        public void ZeroRestSingleFrameShapesReferenceTheirFrameWithoutATarget()
        {
            using var scene = new Scene();
            var entry = Entry("Raise and widen", ("Front", "Raise", 40), ("Front", "Wide", 20));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Targets, Is.Empty);
            Assert.That(expression.Direct, Has.Count.EqualTo(2));
            AssertDirect(expression, copy, "Raise", 40f / 100f);
            AssertDirect(expression, copy, "Wide", 20f / 50f);
            Assert.That(scene.Avatar.Copy.GetComponentsInChildren<SkinnedMeshRenderer>().All(skin => skin.sharedMesh.blendShapeCount == Shapes.Length), Is.True,
                "No generated shape is added for a pose that is a direct reference.");
            AssertVertices(scene.Expected(entry, "Front"), Exported(copy, expression), "Front");
        }

        [Test]
        public void NonZeroRestScalesTheRebasedShapeFromRestToItsFrame()
        {
            using var scene = new Scene(raise: 20, wide: 10);
            var entry = Entry("Raise from rest", ("Front", "Raise", 60), ("Front", "Wide", 30));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Targets, Is.Empty);
            AssertDirect(expression, copy, "Raise", (60f - 20) / (100 - 20));
            AssertDirect(expression, copy, "Wide", (30f - 10) / (50 - 10));
            Assert.That(copy.sharedMesh.blendShapeCount, Is.EqualTo(Shapes.Length));
            AssertVertices(scene.Expected(entry, "Front"), Exported(copy, expression), "Front");
            AssertVertices(Pose(scene.Avatar.Mesh, scene.Rest), Exported(copy, new VrmMenuExpressions.Expression()), "Neutral");
        }

        [Test]
        public void OnlyTheChannelLoweredBelowRestBecomesATarget()
        {
            using var scene = new Scene(raise: 30, lower: 60);
            var entry = Entry("Raise and lower", ("Front", "Raise", 80), ("Front", "Lower", 20));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Direct, Has.Count.EqualTo(1));
            AssertDirect(expression, copy, "Raise", (80f - 30) / (100 - 30));
            Assert.That(expression.Targets, Has.Count.EqualTo(1));
            Assert.That(copy.sharedMesh.GetBlendShapeIndex(expression.Targets.Single()), Is.EqualTo(Shapes.Length));
            AssertVertices(scene.Expected(entry, "Front"), Exported(copy, expression), "Front");
        }

        [Test]
        public void MultipleFrameShapesStayTargets()
        {
            using var scene = new Scene();
            var entry = Entry("Curve", ("Front", "Curve", 75), ("Front", "Raise", 50));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Targets, Has.Count.EqualTo(1));
            Assert.That(expression.Direct.Single().Shape, Is.EqualTo("Raise"));
            AssertDirect(expression, copy, "Raise", .5f);
            AssertVertices(scene.Expected(entry, "Front"), Exported(copy, expression), "Front");
        }

        // sil, aa and a shape outside the viseme group, with the given rests.
        static Mesh Visemes(Scene scene,string sil = "vrc.v_sil", string aa = "vrc.v_aa", float silRest = 100)
        {
            var mesh = scene.Avatar.Mesh; var count = mesh.vertexCount; mesh.ClearBlendShapes();
            Vector3[] Ramp(Vector3 value) => Enumerable.Range(1, count).Select(index => value * index).ToArray();
            mesh.AddBlendShapeFrame(sil, 100, Ramp(new Vector3(.03f, 0, 0)), Ramp(new Vector3(0, .1f, 0)), null);
            mesh.AddBlendShapeFrame(aa, 100, Ramp(new Vector3(0, .02f, 0)), Ramp(new Vector3(.1f, 0, 0)), null);
            mesh.AddBlendShapeFrame("Other", 100, Ramp(new Vector3(0, 0, .01f)), null, null);
            foreach (var skin in scene.Avatar.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Concat(scene.Avatar.Skins))
            {
                skin.SetBlendShapeWeight(0, silRest); skin.SetBlendShapeWeight(1, 0); skin.SetBlendShapeWeight(2, 0);
            }
            return mesh;
        }

        [Test]
        public void AMenuVisemeThatResetsTheRestVisemeEndsAtTheAuthoredPose()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene);
            var expression = scene.Bake(Entry("Open", ("Front", "vrc.v_sil", 0), ("Front", "vrc.v_aa", 100))).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Direct, Is.Empty, "The exported viseme already has the rest viseme taken off.");
            AssertVertices(Pose(mesh, new[] { 0f, 100f, 0f }), Exported(copy, expression), "Open");
        }

        [Test]
        public void AMenuVisemeOverTheRestVisemeKeepsBothAsAuthored()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene);
            var expression = scene.Bake(Entry("Half", ("Front", "vrc.v_aa", 50))).Single();
            AssertVertices(Pose(mesh, new[] { 100f, 50f, 0f }), Exported(scene.Copy("Front"), expression), "Half");
        }

        [Test]
        public void ADescriptorVisemeNamedOutsideTheConventionEndsAtTheAuthoredPose()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene, "Mouth_rest", "Mouth_open");
            var slots = new[] { "Mouth_rest", "Mouth_open" };
            AvatarBaseShape.Preserve(scene.Avatar.Source, scene.Avatar.Copy, scene.Owned, null,
                visemeRenderer: scene.Avatar.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(), visemeShapes: slots);
            var copy = scene.Copy("Front");
            var menu = new VrChatExpressionMenu.Source();
            menu.Entries.Add(Entry("Open", ("Front", "Mouth_rest", 0), ("Front", "Mouth_open", 100)));
            var expression = VrChatExpressionBaker.Bake(scene.Avatar.Source, scene.Avatar.Copy, menu, scene.Owned, null, null, copy, slots).Single();
            Assert.That(expression.Direct, Is.Empty);
            AssertVertices(Pose(mesh, new[] { 0f, 100f, 0f }), Exported(copy, expression), "Open");
        }

        [Test]
        public void ShapesOutsideTheVisemeGroupStayDirectBesideTheRestViseme()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene);
            var expression = scene.Bake(Entry("Closed", ("Front", "vrc.v_sil", 0), ("Front", "Other", 100))).Single();
            var copy = scene.Copy("Front");
            AssertDirect(expression, copy, "Other", 1);
            AssertVertices(Pose(mesh, new[] { 0f, 0f, 100f }), Exported(copy, expression), "Closed");
        }

        [Test]
        public void VisemesWithoutARestStayDirect()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene, silRest: 0);
            var expression = scene.Bake(Entry("Open", ("Front", "vrc.v_sil", 0), ("Front", "vrc.v_aa", 100))).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Targets, Is.Empty);
            AssertDirect(expression, copy, "vrc.v_aa", 1);
            AssertVertices(Pose(mesh, new[] { 0f, 100f, 0f }), Exported(copy, expression), "Open");
        }

        [Test]
        public void TheOnlyVisemeAtRestStaysDirectAgainstItsOwnRest()
        {
            using var scene = new Scene();
            var mesh = Visemes(scene, silRest: 40);
            var expression = scene.Bake(Entry("Raise", ("Front", "vrc.v_sil", 70))).Single();
            var copy = scene.Copy("Front");
            AssertDirect(expression, copy, "vrc.v_sil", (70f - 40) / (100 - 40));
            AssertVertices(Pose(mesh, new[] { 70f, 0f, 0f }), Exported(copy, expression), "Raise");
        }

        [Test]
        public void AnAnimatedVisemeOverTheRestVisemeIsNotADirectPoint()
        {
            using var scene = new Scene();
            Visemes(scene);
            var entry = Entry("Talk", ("Front", "vrc.v_aa", 0));
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = 100, OutTangent = 100 });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1, Value = 100, InTangent = 100, OutTangent = 100 });
            entry.Animation.Add(new VrChatExpressionMenu.AnimatedMorph { Path = "Front", Shape = "vrc.v_aa", Curve = curve });
            entry.Duration = 1;
            var expression = scene.Bake(entry).Single();
            Assert.That(expression.DirectPoints, Is.Empty);
            Assert.That(expression.Animation.Channels.Single().Points.Any(point => point.Target != null), Is.True);
        }

        [Test]
        public void EntriesWhoseEveryWeightIsZeroStayRegisteredThroughTheZeroMarker()
        {
            using var scene = new Scene(raise: 30);
            var entries = new[] { Entry("At rest", ("Front", "Raise", 30)), Entry("At rest again", ("Front", "Raise", 30), ("Front", "Wide", 0)) };
            var expressions = scene.Bake(entries);
            var copy = scene.Copy("Front"); var mesh = copy.sharedMesh;
            Assert.That(expressions, Has.Count.EqualTo(2));
            Assert.That(expressions.All(expression => expression.Direct.Count == 0), Is.True);
            Assert.That(expressions.Select(expression => expression.Targets.Single()).Distinct().Count(), Is.EqualTo(1));
            var marker = mesh.GetBlendShapeIndex(expressions[0].Targets.Single());
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));
            var vertices = new Vector3[mesh.vertexCount]; var normals = new Vector3[mesh.vertexCount]; var tangents = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(marker, 0, vertices, normals, tangents);
            Assert.That(vertices.Concat(normals).Concat(tangents).All(value => value == Vector3.zero), Is.True);
            var names = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).Cast<object>().ToList();
            var root = new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object> { ["name"] = "Front", ["mesh"] = 0L } },
                ["meshes"] = new List<object> { new Dictionary<string, object>
                    { ["extras"] = new Dictionary<string, object> { ["targetNames"] = names },
                      ["primitives"] = new List<object> { new Dictionary<string, object>
                          { ["targets"] = names.Select(_ => (object)new Dictionary<string, object>()).ToList() } } } },
                ["extensions"] = new Dictionary<string, object> { ["VRMC_vrm"] = new Dictionary<string, object>
                    { ["expressions"] = new Dictionary<string, object> { ["custom"] = new Dictionary<string, object>() } } }
            };
            var bytes = VrmMenuExpressions.Add(GlbDocument.Create(root, Array.Empty<byte>()).Write(), expressions);
            // Both entries are the rest face, so they are written once.
            Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(1));
            Assert.That(expressions[1].MergedInto, Is.EqualTo(expressions[0].RegisteredName));
        }

        [Test]
        public async Task ExportedBindWeightsReproduceTheBakedPose()
        {
            using var scene = new Scene(raise: 30, lower: 60, wide: 10);
            var entry = Entry("Combined", ("Front", "Raise", 80), ("Front", "Lower", 20), ("Back", "Wide", 30), ("Back", "Curve", 75));
            var expression = scene.Bake(entry).Single();
            Assert.That(expression.Targets, Has.Count.EqualTo(2), "Lower and Curve stay targets.");
            Assert.That(expression.Direct, Has.Count.EqualTo(2), "Raise and Wide are direct references.");
            foreach (var path in new[] { "Front", "Back" })
                AssertVertices(scene.Expected(entry, path), Exported(scene.Copy(path), expression), "Copy " + path);
            var bytes = VrmMenuExpressions.Add(Vrm10Exporter.Export(new GltfExportSettings(), scene.Avatar.Copy,
                textureSerializer: new MobileTextureSerializer(null),
                vrmMeta: new VRM10ObjectMeta { Name = "Direct bind fixture", Version = "1", Authors = new List<string> { "Tests" } }),
                new[] { expression }, new NamedDirectBindings());
            var json = GlbDocument.Read(bytes).Json;
            var nodes = (List<object>)json["nodes"];
            var custom = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])["VRMC_vrm"])["expressions"])["custom"];
            var binds = ((List<object>)((Dictionary<string, object>)custom["Combined"])["morphTargetBinds"]).Cast<Dictionary<string, object>>().ToArray();
            Assert.That(binds, Has.Length.EqualTo(4));
            Assert.That(binds.Count(bind => Convert.ToDouble(bind["weight"]) == 1), Is.EqualTo(2));
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                foreach (var path in new[] { "Front", "Back" })
                {
                    var skin = imported.GetComponentsInChildren<SkinnedMeshRenderer>().Single(value => value.name == path); var mesh = skin.sharedMesh;
                    var actual = mesh.vertices; var delta = new Vector3[mesh.vertexCount];
                    foreach (var bind in binds.Where(bind => (string)((Dictionary<string, object>)nodes[Convert.ToInt32(bind["node"])])["name"] == path))
                    {
                        var index = Convert.ToInt32(bind["index"]);
                        Assert.That(mesh.GetBlendShapeFrameCount(index), Is.EqualTo(1));
                        mesh.GetBlendShapeFrameVertices(index, 0, delta, null, null);
                        for (var vertex = 0; vertex < actual.Length; vertex++) actual[vertex] += delta[vertex] * (float)Convert.ToDouble(bind["weight"]);
                    }
                    AssertVertices(scene.Expected(entry, path), actual, "Imported " + path);
                }
            }
            finally { Object.DestroyImmediate(imported.gameObject); }
        }
    }
}
