using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UniGLTF;
using UniVRM10;
using UnityEngine;
using VRVlog.AvatarFormats;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Schema 2 of the expression animation extension: animated channels move
    // the original shape directly when its single frame covers the curve,
    // other channels are baked relative to rest, and the VRM expression holds
    // the animation's last frame. Expected poses evaluate the authored source
    // frames independently of the exporter.
    public sealed class ExpressionAnimationV2WriterTests
    {
        const float Tolerance = .00001f;
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

            internal byte[] Export(IList<VrmMenuExpressions.Expression> expressions, ICollection<string> warnings = null) =>
                VrmMenuExpressions.Add(Vrm10Exporter.Export(new GltfExportSettings(), Avatar.Copy,
                    textureSerializer: new MobileTextureSerializer(null),
                    vrmMeta: new VRM10ObjectMeta { Name = "Animation v2 fixture", Version = "1", Authors = new List<string> { "Tests" } }),
                    expressions, new NamedDirectBindings(), warnings);

            // The authored source pose at time: rest weights, the entry's
            // fixed values, and each animated shape at its curve value.
            internal Vector3[] Expected(VrChatExpressionMenu.Entry entry, string path, double time)
            {
                var weights = (float[])Rest.Clone();
                foreach (var value in entry.Values.Where(value => value.Path == path)) weights[Array.IndexOf(Shapes, value.Shape)] = value.Weight;
                foreach (var animated in entry.Animation.Where(value => value.Path == path))
                    weights[Array.IndexOf(Shapes, animated.Shape)] = (float)animated.Curve.Evaluate(time);
                return Pose(Avatar.Mesh, weights);
            }

            public void Dispose() { Avatar.Dispose(); foreach (var mesh in Owned) Object.DestroyImmediate(mesh); }
        }

        static ExpressionAnimationData.Curve Curve(params (double Time, double Value)[] keys)
        {
            var curve = new ExpressionAnimationData.Curve();
            for (var i = 0; i < keys.Length; i++)
            {
                // Linear segments: each key's tangents follow its neighbours.
                var incoming = i == 0 ? 0 : (keys[i].Value - keys[i - 1].Value) / (keys[i].Time - keys[i - 1].Time);
                var outgoing = i == keys.Length - 1 ? 0 : (keys[i + 1].Value - keys[i].Value) / (keys[i + 1].Time - keys[i].Time);
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = keys[i].Time, Value = keys[i].Value,
                    InTangent = i == 0 ? outgoing : incoming, OutTangent = i == keys.Length - 1 ? incoming : outgoing });
            }
            return curve;
        }

        // A menu entry with fixed values and animated shapes. The fixed value
        // of an animated shape is its first pose, as the readers record it.
        static VrChatExpressionMenu.Entry Entry(string name, double duration, bool loop, (string Path, string Shape, float Weight)[] values,
            params (string Path, string Shape, ExpressionAnimationData.Curve Curve)[] animated)
        {
            var entry = new VrChatExpressionMenu.Entry { Id = name, Name = name, Duration = duration, Loop = loop };
            foreach (var value in values) entry.Values.Add(new VrChatExpressionMenu.MorphValue { Path = value.Path, Shape = value.Shape, Weight = value.Weight });
            foreach (var value in animated)
            {
                entry.Values.Add(new VrChatExpressionMenu.MorphValue { Path = value.Path, Shape = value.Shape, Weight = (float)value.Curve.Evaluate(0) });
                entry.Animation.Add(new VrChatExpressionMenu.AnimatedMorph { Path = value.Path, Shape = value.Shape, Curve = value.Curve });
            }
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

        static void AssertVertices(Vector3[] expected, Vector3[] actual, string message)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length), message);
            for (var vertex = 0; vertex < expected.Length; vertex++)
                Assert.That(Vector3.Distance(actual[vertex], expected[vertex]), Is.LessThanOrEqualTo(Tolerance), message + " / vertex " + vertex);
        }

        static Dictionary<string, object> Extension(byte[] bytes) =>
            (Dictionary<string, object>)((Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"])[ExpressionAnimationData.Extension];

        static Dictionary<string, object>[] Binds(Dictionary<string, object> json, string name)
        {
            var custom = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)json["extensions"])["VRMC_vrm"])["expressions"])["custom"];
            return ((List<object>)((Dictionary<string, object>)custom[name])["morphTargetBinds"]).Cast<Dictionary<string, object>>().ToArray();
        }

        static string NodeName(Dictionary<string, object> json, int node) => (string)((Dictionary<string, object>)((List<object>)json["nodes"])[node])["name"];

        static string MorphName(Dictionary<string, object> json, ExpressionAnimationData.MorphReference morph)
        {
            var node = (Dictionary<string, object>)((List<object>)json["nodes"])[morph.Node];
            var mesh = (Dictionary<string, object>)((List<object>)json["meshes"])[Convert.ToInt32(node["mesh"])];
            return (string)((List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"])[morph.Index];
        }

        // How the app plays a schema 2 expression on the imported renderer:
        // the VRM binds that no point moves, plus each channel's two points at
        // the weights Channel.Weights picks. withoutAnimation applies the VRM
        // expression alone, as other apps do.
        static Vector3[] Played(Vrm10Instance imported, Dictionary<string, object> json, ExpressionAnimationDocument document,
            string expression, string path, double time, bool withoutAnimation = false)
        {
            var mesh = imported.GetComponentsInChildren<SkinnedMeshRenderer>().Single(value => value.name == path).sharedMesh;
            var result = mesh.vertices; var delta = new Vector3[mesh.vertexCount];
            void Add(int node, int index, float weight)
            {
                if (NodeName(json, node) != path || weight == 0) return;
                Assert.That(mesh.GetBlendShapeFrameCount(index), Is.EqualTo(1));
                mesh.GetBlendShapeFrameVertices(index, 0, delta, null, null);
                for (var vertex = 0; vertex < result.Length; vertex++) result[vertex] += delta[vertex] * weight;
            }
            var animation = withoutAnimation ? null : document.Animations.SingleOrDefault(value => value.Expression == expression);
            var animated = new HashSet<(int, int)>((animation?.Channels ?? new List<ExpressionAnimationData.Channel>())
                .SelectMany(channel => channel.Points).Where(point => point.Morph != null).Select(point => (point.Morph.Node, point.Morph.Index)));
            foreach (var bind in Binds(json, expression))
            {
                var node = Convert.ToInt32(bind["node"]); var index = Convert.ToInt32(bind["index"]);
                if (!animated.Contains((node, index))) Add(node, index, (float)Convert.ToDouble(bind["weight"]));
            }
            if (animation == null) return result;
            foreach (var channel in animation.Channels)
            {
                channel.Weights(time, out var left, out var right, out var blend);
                void Point(ExpressionAnimationData.Point point, float weight) { if (point.Morph != null) Add(point.Morph.Node, point.Morph.Index, weight); }
                Point(channel.Points[left], 1 - blend);
                Point(channel.Points[right], blend);
            }
            return result;
        }

        // (a) A single-frame shape raised from rest within its frame is
        // moved directly by two points; nothing is baked, and the VRM
        // expression holds the last frame at (w - rest) / (F - rest).
        [Test]
        public async Task SingleFrameCurveMovesTheOriginalShapeWithTwoPoints()
        {
            using var scene = new Scene(raise: 20);
            var entry = Entry("Raise", 1, false, new[] { ("Front", "Wide", 30f) }, ("Front", "Raise", Curve((0, 20), (1, 80))));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            Assert.That(expression.Targets, Is.Empty);
            // The fixed channel is a static direct reference.
            Assert.That((expression.Direct.Single().Renderer, expression.Direct.Single().Shape), Is.EqualTo((copy, "Wide")));
            Assert.That(expression.Direct.Single().Weight, Is.EqualTo(30f / 50f).Within(Tolerance));
            var channel = expression.Animation.Channels.Single();
            Assert.That(channel.Points.Select(point => point.Value), Is.EqualTo(new[] { 20d, 100d }));
            Assert.That(channel.Points.All(point => point.Target == null), Is.True);
            Assert.That(expression.DirectPoints.Single().Key, Is.SameAs(channel.Points[1]));
            Assert.That(expression.DirectPoints.Single().Value, Is.EqualTo((copy, "Raise")));
            Assert.That(scene.Avatar.Copy.GetComponentsInChildren<SkinnedMeshRenderer>().All(skin => skin.sharedMesh.blendShapeCount == Shapes.Length), Is.True,
                "No generated shape is added.");
            Assert.That(expression.FinalPoints().Single().Weight, Is.EqualTo(60f / 80f).Within(Tolerance));

            var bytes = scene.Export(new[] { expression });
            var json = GlbDocument.Read(bytes).Json;
            var binds = Binds(json, "Raise");
            Assert.That(binds, Has.Length.EqualTo(2));
            Assert.That(binds.Select(bind => MorphName(json, new ExpressionAnimationData.MorphReference { Node = Convert.ToInt32(bind["node"]), Index = Convert.ToInt32(bind["index"]) })),
                Is.EquivalentTo(new[] { "Raise", "Wide" }));
            Assert.That(Convert.ToDouble(binds.Single(bind => MorphName(json, new ExpressionAnimationData.MorphReference
                { Node = Convert.ToInt32(bind["node"]), Index = Convert.ToInt32(bind["index"]) }) == "Raise")["weight"]), Is.EqualTo(.75).Within(Tolerance));
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            var points = document.Animations.Single().Channels.Single().Points;
            Assert.That(points[0].Morph, Is.Null);
            Assert.That(NodeName(json, points[1].Morph.Node), Is.EqualTo("Front"));
            Assert.That(MorphName(json, points[1].Morph), Is.EqualTo("Raise"));
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                AssertVertices(scene.Expected(entry, "Front", 1), Played(imported, json, document, "Raise", "Front", 0, withoutAnimation: true),
                    "The VRM expression is the last frame.");
            }
            finally { Object.DestroyImmediate(imported.gameObject); }
        }

        // (b) A curve below rest, or a shape with several frames, is baked
        // into generated morphs measured from rest; the point at rest moves nothing.
        [TestCase("below rest")]
        [TestCase("several frames")]
        public void OtherCurvesAreBakedRelativeToRest(string kind)
        {
            using var scene = kind == "below rest" ? new Scene(raise: 60) : new Scene();
            var entry = kind == "below rest"
                ? Entry("Baked", 1, false, new (string, string, float)[0], ("Front", "Raise", Curve((0, 60), (1, 20))))
                : Entry("Baked", 1, false, new (string, string, float)[0], ("Front", "Curve", Curve((0, 0), (1, 75))));
            var expression = scene.Bake(entry).Single();
            var copy = scene.Copy("Front");
            var rest = kind == "below rest" ? 60d : 0d;
            var channel = expression.Animation.Channels.Single();
            Assert.That(expression.DirectPoints, Is.Empty);
            Assert.That(expression.Targets, Is.Empty);
            Assert.That(expression.Direct, Is.Empty);
            Assert.That(channel.Points.Select(point => point.Value),
                Is.EqualTo(kind == "below rest" ? new[] { 0d, 60d } : new[] { 0d, 50d, 100d }));
            Assert.That(channel.Points.All(point => (point.Target == null) == (point.Value == rest)), Is.True);
            Assert.That(copy.sharedMesh.blendShapeCount, Is.GreaterThan(Shapes.Length));
            foreach (var point in channel.Points.Where(point => point.Target != null))
                Assert.That(copy.sharedMesh.GetBlendShapeFrameCount(copy.sharedMesh.GetBlendShapeIndex(point.Target)), Is.EqualTo(1));
        }

        // (c) Played through the written schema 2 data, every curve matches
        // the source at the start, the middle and the end of the animation.
        [TestCase("direct")]
        [TestCase("below rest")]
        [TestCase("several frames")]
        [TestCase("eased")]
        public async Task PlayedAnimationMatchesTheSourceAtStartMiddleAndEnd(string kind)
        {
            Scene scene;
            VrChatExpressionMenu.Entry entry;
            var none = new (string, string, float)[0];
            switch (kind)
            {
                case "direct":
                    scene = new Scene(raise: 20, wide: 10);
                    entry = Entry("Played", 1, true, new[] { ("Front", "Wide", 40f) }, ("Front", "Raise", Curve((0, 20), (1, 80))));
                    break;
                case "below rest":
                    scene = new Scene(raise: 60);
                    entry = Entry("Played", 1, false, none, ("Front", "Raise", Curve((0, 60), (1, 20))), ("Back", "Raise", Curve((0, 60), (1, 90))));
                    break;
                case "several frames":
                    scene = new Scene(curve: 10);
                    entry = Entry("Played", 1, false, new[] { ("Front", "Lower", 50f) }, ("Front", "Curve", Curve((0, 10), (1, 85))));
                    break;
                default:
                    scene = new Scene(lower: 10);
                    entry = Entry("Played", 2, false, none, ("Front", "Lower", Curve((0, 10), (.5, 90), (2, 40))));
                    break;
            }
            using (scene)
            {
                var expression = scene.Bake(entry).Single();
                var bytes = scene.Export(new[] { expression });
                var json = GlbDocument.Read(bytes).Json;
                var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
                Assert.That(document.Animations.Single().Expression, Is.EqualTo("Played"));
                var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                try
                {
                    var duration = entry.Duration;
                    foreach (var path in new[] { "Front", "Back" })
                    {
                        foreach (var time in new[] { 0, duration / 4, duration / 2, duration })
                            AssertVertices(scene.Expected(entry, path, time), Played(imported, json, document, "Played", path, time), path + " at " + time);
                        AssertVertices(scene.Expected(entry, path, duration), Played(imported, json, document, "Played", path, 0, withoutAnimation: true),
                            path + " VRM expression");
                    }
                }
                finally { Object.DestroyImmediate(imported.gameObject); }
            }
        }

        // (d) A loop that ends on the neutral face stays registered through
        // the zero marker and is listed before saving as approximated.
        [Test]
        public async Task ExpressionEndingNeutralIsRegisteredAndListedBeforeSaving()
        {
            using var scene = new Scene();
            var neutral = Entry("Loop", 1, true, new (string, string, float)[0], ("Front", "Raise", Curve((0, 0), (.5, 100), (1, 0))));
            var moving = Entry("Moving", 1, true, new (string, string, float)[0], ("Front", "Raise", Curve((0, 0), (1, 100))));
            var expressions = scene.Bake(neutral, moving);
            Assert.That(expressions.Select(expression => expression.Name), Is.EqualTo(new[] { "Loop", "Moving" }));
            var loop = expressions[0];
            Assert.That(loop.EndsNeutral, Is.True);
            Assert.That(expressions[1].EndsNeutral, Is.False);
            Assert.That(loop.Direct, Is.Empty);
            Assert.That(loop.FinalPoints(), Is.Empty);
            var marker = scene.Copy("Front").sharedMesh.GetBlendShapeIndex(loop.Targets.Single());
            Assert.That(marker, Is.GreaterThanOrEqualTo(0));

            var bytes = scene.Export(expressions);
            Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(2));
            var json = GlbDocument.Read(bytes).Json;
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            Assert.That(document.Animations.Select(animation => animation.Expression), Is.EquivalentTo(new[] { "Loop", "Moving" }));
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                AssertVertices(scene.Expected(neutral, "Front", .5), Played(imported, json, document, "Loop", "Front", .5), "Moves in VR Vlog");
                AssertVertices(scene.Expected(neutral, "Front", 1), Played(imported, json, document, "Loop", "Front", 0, withoutAnimation: true),
                    "Other apps keep the neutral face.");
            }
            finally { Object.DestroyImmediate(imported.gameObject); }

            var report = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordExpressionsEndingNeutral(expressions, report);
            Assert.That(report.Fallbacks.Items.Single().Kind, Is.EqualTo(ExportFallbackKind.ExpressionEndsNeutral));
            Assert.That(report.Fallbacks.Items.Single().Names, Is.EqualTo(new[] { "Loop" }));
            var batch = ExportFallbackConfirmation.IsBatchMode;
            try
            {
                ExportFallbackConfirmation.IsBatchMode = () => false;
                Assert.That(ExportFallbackConfirmation.NeedsAnswer(report.Fallbacks, interactive: true), Is.True);
            }
            finally { ExportFallbackConfirmation.IsBatchMode = batch; }
            var row = ExportResultList.Build(report.Fallbacks, null).Rows.Single();
            Assert.That(row.Outcome, Is.EqualTo(ExportOutcome.Approximated));
            Assert.That(row.Group, Is.EqualTo("表情"));
            Assert.That(row.TargetLabel, Is.EqualTo("Loop"));
            Assert.That(row.Content, Is.EqualTo(ExporterLocalization.T(ExportFallbackConfirmation.ExpressionEndsNeutralText)));
        }

        // A curve that ends at rest between two baked knots ends on two binds
        // that cancel out; it is still the neutral face and is listed.
        [Test]
        public async Task CurveEndingAtRestBetweenKnotsEndsNeutral()
        {
            using var scene = new Scene(raise: 50);
            var entry = Entry("Back", 1, false, new (string, string, float)[0], ("Front", "Raise", Curve((0, 0), (.5, 100), (1, 50))));
            var expression = scene.Bake(entry).Single();
            Assert.That(expression.FinalPoints().Count(), Is.EqualTo(2), "The last frame is half of each knot.");
            Assert.That(expression.EndsNeutral, Is.True);
            Assert.That(expression.Targets, Is.Empty, "The final binds keep it selectable without the zero marker.");

            var bytes = scene.Export(new[] { expression });
            Assert.That(VrmMenuExpressions.CountRegistered(bytes), Is.EqualTo(1));
            var json = GlbDocument.Read(bytes).Json;
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            var imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
            try
            {
                AssertVertices(scene.Expected(entry, "Front", .5), Played(imported, json, document, "Back", "Front", .5), "Moves in VR Vlog");
                AssertVertices(scene.Expected(entry, "Front", 1), Played(imported, json, document, "Back", "Front", 0, withoutAnimation: true),
                    "Other apps keep the neutral face.");
            }
            finally { Object.DestroyImmediate(imported.gameObject); }

            var report = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordExpressionsEndingNeutral(new[] { expression }, report);
            Assert.That(report.Fallbacks.Items.Single().Names, Is.EqualTo(new[] { "Back" }));
        }

        // A constant curve below rest cannot be moved directly, and baking it
        // would leave one point; it stops with a message instead.
        [Test]
        public void ConstantCurveOutsideTheDirectRangeIsRejected()
        {
            using var scene = new Scene(raise: 60);
            var entry = Entry("Flat", 1, false, new (string, string, float)[0], ("Front", "Raise", Curve((0, 40), (1, 40))));
            Assert.That(Assert.Throws<InvalidOperationException>(() => scene.Bake(entry)).Message, Does.Contain("変化しません"));
        }

        // (e) Every registered expression is listed with its source, animated
        // or not, including the duplicate-name suffix.
        [Test]
        public void EveryRegisteredExpressionIsListedWithItsSource()
        {
            using var scene = new Scene();
            var none = new (string, string, float)[0];
            var menu = Entry("Smile", 1, false, new[] { ("Front", "Raise", 40f) });
            var gesture = Entry("Smile", 1, true, none, ("Front", "Raise", Curve((0, 0), (1, 100))));
            gesture.Id = "Smile gesture"; gesture.Source = ExpressionSource.Gesture;
            var faceEmo = Entry("Wide", 1, false, new[] { ("Front", "Wide", 20f) });
            faceEmo.Source = ExpressionSource.FaceEmo;
            var bytes = scene.Export(scene.Bake(menu, gesture, faceEmo));
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            Assert.That(document.SchemaVersion, Is.EqualTo(2));
            Assert.That(document.Expressions.Select(value => (value.Expression, value.Source)), Is.EqualTo(new[]
            {
                ("Smile", ExpressionSource.Menu), ("Smile (2)", ExpressionSource.Gesture), ("Wide", ExpressionSource.FaceEmo)
            }));
            Assert.That(document.Animations.Single().Expression, Is.EqualTo("Smile (2)"));
        }

        // (f) The final bytes name the lowest app version for each VRVLOG_
        // extension used; without one there is no key.
        [Test]
        public void RequiredAppVersionsFollowTheWrittenExtensions()
        {
            using var scene = new Scene();
            var entry = Entry("Raise", 1, false, new (string, string, float)[0], ("Front", "Raise", Curve((0, 0), (1, 100))));
            var bytes = AvatarFormatRequirements.Write(scene.Export(scene.Bake(entry)));
            var asset = (Dictionary<string, object>)GlbDocument.Read(bytes).Json["asset"];
            var required = (Dictionary<string, object>)((Dictionary<string, object>)asset["extras"])[AvatarFormatVersions.RequiredAppVersionsKey];
            Assert.That(required.Keys, Is.EqualTo(new[] { ExpressionAnimationData.Extension }));
            Assert.That(required[ExpressionAnimationData.Extension], Is.EqualTo("1.0.5"));
            Assert.That(AvatarFormatRequirements.Write(bytes), Is.SameAs(bytes), "Unchanged requirements keep the bytes.");

            var plain = Vrm10Exporter.Export(new GltfExportSettings(), scene.Avatar.Copy, textureSerializer: new MobileTextureSerializer(null),
                vrmMeta: new VRM10ObjectMeta { Name = "No extension fixture", Version = "1", Authors = new List<string> { "Tests" } });
            Assert.That(AvatarFormatRequirements.Write(plain), Is.SameAs(plain));
            var plainAsset = (Dictionary<string, object>)GlbDocument.Read(plain).Json["asset"];
            Assert.That(plainAsset.TryGetValue("extras", out var extras) && ((Dictionary<string, object>)extras).ContainsKey(AvatarFormatVersions.RequiredAppVersionsKey), Is.False);

            // A key left from an earlier write is removed with the extension.
            var glb = GlbDocument.Read(plain);
            ((Dictionary<string, object>)glb.Json["asset"])["extras"] = new Dictionary<string, object>
                { [AvatarFormatVersions.RequiredAppVersionsKey] = new Dictionary<string, object> { [ExpressionAnimationData.Extension] = "1.0.5" }, ["other"] = "kept" };
            var cleaned = (Dictionary<string, object>)((Dictionary<string, object>)GlbDocument.Read(AvatarFormatRequirements.Write(glb.Write())).Json["asset"])["extras"];
            Assert.That(cleaned.ContainsKey(AvatarFormatVersions.RequiredAppVersionsKey), Is.False);
            Assert.That(cleaned["other"], Is.EqualTo("kept"));
        }

        // (h) The written extension is schema 2: ReadDocument reads it and
        // the schema 1 reader rejects it. No entries write no extension.
        [Test]
        public void WrittenExtensionIsSchemaTwoOnly()
        {
            using var scene = new Scene();
            var entry = Entry("Raise", 1, false, new (string, string, float)[0], ("Front", "Raise", Curve((0, 0), (1, 100))));
            var bytes = scene.Export(scene.Bake(entry));
            var json = GlbDocument.Read(bytes).Json;
            Assert.That(((List<object>)json["extensionsUsed"]).Count(value => (string)value == ExpressionAnimationData.Extension), Is.EqualTo(1));
            Assert.That(json.TryGetValue("extensionsRequired", out var required) && ((List<object>)required).Contains(ExpressionAnimationData.Extension), Is.False);
            var extension = Extension(bytes);
            Assert.That(Convert.ToInt64(extension["schemaVersion"]), Is.EqualTo(2));
            Assert.That(ExpressionAnimationData.ReadDocument(extension).Animations.Single().Expression, Is.EqualTo("Raise"));
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.Read(extension));
            var empty = GlbDocument.Read(scene.Export(new List<VrmMenuExpressions.Expression>())).Json;
            Assert.That(((Dictionary<string, object>)empty["extensions"]).ContainsKey(ExpressionAnimationData.Extension), Is.False);
        }

        // (g) Optimizer routes: points that move an original shape follow the
        // merge to the renamed output morph. When the merge coalesces two
        // animated shapes into one morph, the app could not move them apart,
        // so the animation is left out with a warning and the VRM expression
        // keeps its first bind.
        sealed class Merge : IDisposable
        {
            readonly List<Object> owned = new List<Object>();
            internal readonly GameObject Source;
            internal GameObject Clone;

            internal Merge() { Source = Own(new GameObject("source")); }

            T Own<T>(T value) where T : Object { owned.Add(value); return value; }

            internal SkinnedMeshRenderer Skin(GameObject root, string name, params string[] shapes)
            {
                var child = new GameObject(name); child.transform.SetParent(root.transform, false);
                var skin = child.AddComponent<SkinnedMeshRenderer>();
                var mesh = Own(new Mesh { name = name + " mesh" });
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };
                foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.forward * .1f, 3).ToArray(), null, null);
                skin.sharedMesh = mesh;
                return skin;
            }

            public void Dispose()
            {
                if (Clone != null) Object.DestroyImmediate(Clone);
                foreach (var value in owned.AsEnumerable().Reverse()) if (value != null) Object.DestroyImmediate(value);
            }
        }

        static byte[] MergedOutput(params string[] names) => GlbDocument.Create(new Dictionary<string, object> {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
            ["nodes"] = new List<object> { new Dictionary<string, object> { ["name"] = "other" }, new Dictionary<string, object> { ["mesh"] = 0L } },
            ["meshes"] = new List<object> { new Dictionary<string, object> {
                ["extras"] = new Dictionary<string, object> { ["targetNames"] = names.Cast<object>().ToList() },
                ["primitives"] = new List<object> { new Dictionary<string, object> { ["targets"] = names.Select(_ => (object)new Dictionary<string, object>()).ToList() } }
            } },
            ["extensions"] = new Dictionary<string, object> { ["VRMC_vrm"] = new Dictionary<string, object> {
                ["expressions"] = new Dictionary<string, object> { ["custom"] = new Dictionary<string, object>() } } }
        }, Array.Empty<byte>()).Write();

        // One channel per original renderer, moving its Smile from 0 to 50.
        static VrmMenuExpressions.Expression AnimatedSmile(IEnumerable<SkinnedMeshRenderer> originals)
        {
            var expression = new VrmMenuExpressions.Expression { Name = "Smile",
                Animation = new ExpressionAnimationData { Duration = 1, Loop = false } };
            foreach (var original in originals)
            {
                var channel = new ExpressionAnimationData.Channel { Curve = Curve((0, 0), (1, 50)) };
                var full = new ExpressionAnimationData.Point { Value = 100 };
                channel.Points.Add(new ExpressionAnimationData.Point { Value = 0 }); channel.Points.Add(full);
                expression.Animation.Channels.Add(channel);
                expression.DirectPoints.Add(full, (original, "Smile"));
            }
            return expression;
        }

        [Test]
        public void AnimatedPointsFollowRenamedMergedShapes()
        {
            using var merge = new Merge();
            merge.Skin(merge.Source, "face", "Smile"); merge.Skin(merge.Source, "body", "Smile");
            merge.Clone = Object.Instantiate(merge.Source);
            var originals = merge.Clone.GetComponentsInChildren<SkinnedMeshRenderer>();
            using var bindings = ExportOptimizationBindings.Capture(merge.Clone, menuMorphs: originals.Select(skin => (skin, "Smile")));
            var marker = merge.Clone.GetComponent<ExportOptimizationMarker>();
            var target = merge.Skin(merge.Clone, "merged", "Other", "Smile (face)", "Smile (body)");
            foreach (var route in marker.Morphs)
            {
                route.Shape = route.SourceRendererId == originals[0].GetInstanceID() ? "Smile (face)" : "Smile (body)";
                route.Renderer = target;
            }
            Object.DestroyImmediate(originals[0].gameObject); Object.DestroyImmediate(originals[1].gameObject);
            bindings.ValidateAndApply();
            bindings.BindNodes(skin => skin == target ? 1 : -1);
            var warnings = new List<string>();
            var bytes = VrmMenuExpressions.Add(MergedOutput("Other", "Smile (face)", "Smile (body)"), new[] { AnimatedSmile(originals) }, bindings, warnings);
            Assert.That(warnings, Is.Empty);
            var channels = ExpressionAnimationData.ReadDocument(Extension(bytes)).Animations.Single().Channels;
            Assert.That(channels.Select(channel => channel.Points[0].Morph), Is.All.Null);
            Assert.That(channels.Select(channel => (channel.Points[1].Morph.Node, channel.Points[1].Morph.Index)), Is.EqualTo(new[] { (1, 1), (1, 2) }));
            var binds = Binds(GlbDocument.Read(bytes).Json, "Smile");
            Assert.That(binds.Select(bind => (Convert.ToInt32(bind["node"]), Convert.ToInt32(bind["index"]))), Is.EquivalentTo(new[] { (1, 1), (1, 2) }));
            Assert.That(binds.Select(bind => Convert.ToDouble(bind["weight"])), Is.All.EqualTo(.5).Within(Tolerance));
        }

        [Test]
        public void AnimatedShapesCoalescedIntoOneMorphKeepOnlyTheLastFrame()
        {
            using var merge = new Merge();
            merge.Skin(merge.Source, "face", "Smile"); merge.Skin(merge.Source, "body", "Smile");
            merge.Clone = Object.Instantiate(merge.Source);
            var originals = merge.Clone.GetComponentsInChildren<SkinnedMeshRenderer>();
            using var bindings = ExportOptimizationBindings.Capture(merge.Clone, menuMorphs: originals.Select(skin => (skin, "Smile")));
            var target = merge.Skin(merge.Clone, "merged", "Smile");
            foreach (var route in merge.Clone.GetComponent<ExportOptimizationMarker>().Morphs) route.Renderer = target;
            Object.DestroyImmediate(originals[0].gameObject); Object.DestroyImmediate(originals[1].gameObject);
            bindings.ValidateAndApply();
            bindings.BindNodes(skin => skin == target ? 1 : -1);
            var warnings = new List<string>();
            var bytes = VrmMenuExpressions.Add(MergedOutput("Smile"), new[] { AnimatedSmile(originals) }, bindings, warnings);
            Assert.That(warnings, Has.Exactly(1).Contains("Smile"));
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            Assert.That(document.Animations, Is.Empty);
            Assert.That(document.Expressions.Single().Expression, Is.EqualTo("Smile"));
            var bind = Binds(GlbDocument.Read(bytes).Json, "Smile").Single();
            Assert.That(Convert.ToInt32(bind["node"]), Is.EqualTo(1));
            Assert.That(Convert.ToInt32(bind["index"]), Is.EqualTo(0));
            Assert.That(Convert.ToDouble(bind["weight"]), Is.EqualTo(.5).Within(Tolerance));
        }

        // A fixed shape and an animated one coalesced into one morph: the
        // reader would write the curve over the fixed value, so the animation
        // is left out, and the second weight on that morph is reported.
        [Test]
        public void FixedAndAnimatedShapesCoalescedIntoOneMorphKeepOnlyTheLastFrame()
        {
            using var merge = new Merge();
            merge.Skin(merge.Source, "face", "Smile"); merge.Skin(merge.Source, "body", "Smile");
            merge.Clone = Object.Instantiate(merge.Source);
            var originals = merge.Clone.GetComponentsInChildren<SkinnedMeshRenderer>();
            using var bindings = ExportOptimizationBindings.Capture(merge.Clone, menuMorphs: originals.Select(skin => (skin, "Smile")));
            var target = merge.Skin(merge.Clone, "merged", "Smile");
            foreach (var route in merge.Clone.GetComponent<ExportOptimizationMarker>().Morphs) route.Renderer = target;
            Object.DestroyImmediate(originals[0].gameObject); Object.DestroyImmediate(originals[1].gameObject);
            bindings.ValidateAndApply();
            bindings.BindNodes(skin => skin == target ? 1 : -1);
            var expression = new VrmMenuExpressions.Expression { Name = "Smile",
                Animation = new ExpressionAnimationData { Duration = 1, Loop = false } };
            expression.Direct.Add((originals[0], "Smile", .25f));
            var channel = new ExpressionAnimationData.Channel { Curve = Curve((0, 0), (1, 50)) };
            var full = new ExpressionAnimationData.Point { Value = 100 };
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 0 }); channel.Points.Add(full);
            expression.Animation.Channels.Add(channel);
            expression.DirectPoints.Add(full, (originals[1], "Smile"));
            var warnings = new List<string>();
            var bytes = VrmMenuExpressions.Add(MergedOutput("Smile"), new[] { expression }, bindings, warnings);
            Assert.That(warnings, Has.Exactly(1).Contains("アニメーションを省いて"));
            Assert.That(warnings, Has.Exactly(1).Contains("最初の値だけ"));
            Assert.That(warnings.Count, Is.EqualTo(2));
            var document = ExpressionAnimationData.ReadDocument(Extension(bytes));
            Assert.That(document.Animations, Is.Empty);
            Assert.That(document.Expressions.Single().Expression, Is.EqualTo("Smile"));
            var bind = Binds(GlbDocument.Read(bytes).Json, "Smile").Single();
            Assert.That(Convert.ToInt32(bind["node"]), Is.EqualTo(1));
            Assert.That(Convert.ToInt32(bind["index"]), Is.EqualTo(0));
            Assert.That(Convert.ToDouble(bind["weight"]), Is.EqualTo(.25).Within(Tolerance));
        }
    }
}
