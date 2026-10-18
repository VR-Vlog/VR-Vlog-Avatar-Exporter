using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.AvatarFormats;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter.Tests
{
    // Format-only tests for VRVLOG_expression_animations schema 2 and the
    // required app versions in asset.extras. No Unity API is used.
    public sealed class ExpressionAnimationDocumentTests
    {
        private static ExpressionAnimationData.MorphReference Morph(int node, int index) =>
            new ExpressionAnimationData.MorphReference { Node = node, Index = index };

        // One linear 0 -> 1 channel over one second; its Bezier range is 0..1.
        private static ExpressionAnimationData.Channel Channel(params (double Value, ExpressionAnimationData.MorphReference Morph)[] points)
        {
            var channel = new ExpressionAnimationData.Channel { Curve = new ExpressionAnimationData.Curve() };
            channel.Curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = 1, OutTangent = 1 });
            channel.Curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1, Value = 1, InTangent = 1, OutTangent = 1 });
            foreach (var point in points) channel.Points.Add(new ExpressionAnimationData.Point { Value = point.Value, Morph = point.Morph });
            return channel;
        }

        private static ExpressionAnimationData Animation(string expression, params ExpressionAnimationData.Channel[] channels)
        {
            var animation = new ExpressionAnimationData { Expression = expression, Duration = 1, Loop = false };
            animation.Channels.AddRange(channels);
            return animation;
        }

        private static ExpressionAnimationDocument Document()
        {
            var document = new ExpressionAnimationDocument();
            document.Expressions.Add(new ExpressionSource { Expression = "Smile", Source = ExpressionSource.Menu });
            document.Expressions.Add(new ExpressionSource { Expression = "Fist", Source = ExpressionSource.Gesture });
            document.Expressions.Add(new ExpressionSource { Expression = "Wink", Source = ExpressionSource.FaceEmo });
            document.Animations.Add(Animation("Smile",
                Channel((0, null), (1, Morph(3, 7))),
                Channel((0, Morph(4, 0)), (0.5, null), (1, Morph(4, 1)))));
            // The same morph may be used again by another, mutually exclusive animation.
            document.Animations.Add(Animation("Wink", Channel((0, null), (1, Morph(3, 7)))));
            return document;
        }

        // Valid schema 2 JSON parsed back into the generic JSON object model.
        private static Dictionary<string, object> Json() =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(Document())));

        private static Dictionary<string, object> At(Dictionary<string, object> root, string list, int index) =>
            (Dictionary<string, object>)((List<object>)root[list])[index];

        private static Dictionary<string, object> PointAt(Dictionary<string, object> root, int animation, int channel, int point) =>
            (Dictionary<string, object>)((List<object>)((Dictionary<string, object>)((List<object>)At(root, "animations", animation)["channels"])[channel])["points"])[point];

        private static void Rejects(Action<Dictionary<string, object>> change)
        {
            var json = Json();
            change(json);
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.ReadDocument(json));
        }

        [Test]
        public void SchemaTwoRoundTripsThroughJson()
        {
            var written = ExpressionAnimationData.WriteDocument(Document());
            var text = JsonDom.Serialize(written);
            var read = ExpressionAnimationData.ReadDocument(JsonDom.Parse(text));

            Assert.That(read.SchemaVersion, Is.EqualTo(2));
            Assert.That(read.Expressions.Select(e => e.Expression + "/" + e.Source),
                Is.EqualTo(new[] { "Smile/menu", "Fist/gesture", "Wink/faceEmo" }));
            Assert.That(read.Animations.Select(a => a.Expression), Is.EqualTo(new[] { "Smile", "Wink" }));
            var points = read.Animations[0].Channels[1].Points;
            Assert.That(points.Select(p => p.Value), Is.EqualTo(new[] { 0, 0.5, 1 }));
            Assert.That(points.Select(p => p.Morph == null ? "null" : p.Morph.Node + ":" + p.Morph.Index), Is.EqualTo(new[] { "4:0", "null", "4:1" }));
            Assert.That(points.All(p => p.Target == null));
            Assert.That(read.Animations[0].Channels[0].Curve.Keys.Select(k => k.Value), Is.EqualTo(new[] { 0.0, 1.0 }));
            Assert.That(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(read)), Is.EqualTo(text));
        }

        [Test]
        public void SchemaTwoAllowsExpressionsWithoutAnimations()
        {
            var document = new ExpressionAnimationDocument();
            document.Expressions.Add(new ExpressionSource { Expression = "Smile", Source = ExpressionSource.Menu });
            var read = ExpressionAnimationData.ReadDocument(JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(document))));
            Assert.That(read.Expressions.Count, Is.EqualTo(1));
            Assert.That(read.Animations, Is.Empty);
        }

        [Test]
        public void SchemaTwoNullPointNeedNotMatchTheFirstPose()
        {
            // Schema 1 requires a null point to equal Evaluate(0); here the curve starts at 0.
            var document = new ExpressionAnimationDocument();
            document.Expressions.Add(new ExpressionSource { Expression = "Smile", Source = ExpressionSource.Menu });
            document.Animations.Add(Animation("Smile", Channel((0, Morph(0, 0)), (1, null))));
            Assert.That(ExpressionAnimationData.ReadDocument(ExpressionAnimationData.WriteDocument(document)).Animations.Count, Is.EqualTo(1));
        }

        [Test]
        public void SchemaOneStillReadsThroughBothReaders()
        {
            var animation = Animation("VRChat / Smile", Channel((0, null), (1, null)));
            animation.Channels[0].Points[1].Target = ExpressionAnimationData.TargetPrefix + "Smile_1";
            var json = JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.Write(new[] { animation })));

            var document = ExpressionAnimationData.ReadDocument(json);
            Assert.That(document.SchemaVersion, Is.EqualTo(1));
            Assert.That(document.Expressions, Is.Empty);
            Assert.That(document.Animations.Single().Expression, Is.EqualTo("VRChat / Smile"));
            Assert.That(document.Animations[0].Channels[0].Points.Select(p => p.Target), Is.EqualTo(new[] { null, ExpressionAnimationData.TargetPrefix + "Smile_1" }));
            Assert.That(document.Animations[0].Channels[0].Points.All(p => p.Morph == null));

            var animations = ExpressionAnimationData.Read(json);
            Assert.That(animations.Single().Channels[0].Points[1].Target, Is.EqualTo(ExpressionAnimationData.TargetPrefix + "Smile_1"));
        }

        [Test]
        public void SchemaOneReaderRejectsSchemaTwo()
        {
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.Read(Json()));
        }

        [Test]
        public void SchemaTwoRejectsUnknownVersion()
        {
            Rejects(json => json["schemaVersion"] = 3L);
        }

        [Test]
        public void SchemaTwoRejectsDuplicateExpressions()
        {
            Rejects(json => At(json, "expressions", 1)["expression"] = "Smile");
        }

        [Test]
        public void SchemaTwoRejectsUnknownSource()
        {
            Rejects(json => At(json, "expressions", 0)["source"] = "Menu");
            Rejects(json => At(json, "expressions", 0)["source"] = "other");
        }

        [Test]
        public void SchemaTwoRejectsAnimationWithoutListedExpression()
        {
            Rejects(json => ((List<object>)json["expressions"]).RemoveAt(2));
        }

        [Test]
        public void SchemaTwoRejectsSameMorphTwiceInOneAnimation()
        {
            Rejects(json => PointAt(json, 0, 1, 2)["target"] = new Dictionary<string, object> { ["node"] = 3L, ["index"] = 7L });
        }

        [Test]
        public void SchemaTwoRejectsNegativeOrFractionalMorphReferences()
        {
            Rejects(json => ((Dictionary<string, object>)PointAt(json, 0, 0, 1)["target"])["node"] = -1L);
            Rejects(json => ((Dictionary<string, object>)PointAt(json, 0, 0, 1)["target"])["index"] = -1L);
            Rejects(json => ((Dictionary<string, object>)PointAt(json, 0, 0, 1)["target"])["node"] = 1.5);
            Rejects(json => ((Dictionary<string, object>)PointAt(json, 0, 0, 1)["target"])["index"] = 0.25);
        }

        [Test]
        public void SchemaTwoRejectsNamedTargets()
        {
            Rejects(json => PointAt(json, 0, 0, 1)["target"] = ExpressionAnimationData.TargetPrefix + "Smile_1");
        }

        [Test]
        public void SchemaTwoRejectsEmptyDocument()
        {
            Rejects(json => { ((List<object>)json["expressions"]).Clear(); ((List<object>)json["animations"]).Clear(); });
        }

        [Test]
        public void WriteDocumentRejectsSchemaOneContent()
        {
            var named = Document();
            named.Animations[0].Channels[0].Points[1].Target = ExpressionAnimationData.TargetPrefix + "Smile_1";
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.WriteDocument(named));
            var versionOne = Document();
            versionOne.SchemaVersion = 1;
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.WriteDocument(versionOne));
        }

        [Test]
        public void AppVersionsCompareNumerically()
        {
            Assert.That(AvatarFormatVersions.Compare("1.0.5", "1.0.10"), Is.LessThan(0));
            Assert.That(AvatarFormatVersions.Compare("1.10.0", "1.9.9"), Is.GreaterThan(0));
            Assert.That(AvatarFormatVersions.Compare("2.0.0", "1.99.99"), Is.GreaterThan(0));
            Assert.That(AvatarFormatVersions.Compare("1.0.3", "1.0.3"), Is.EqualTo(0));
        }

        [TestCase("1.0")]
        [TestCase("1.0.0.0")]
        [TestCase("1.0.a")]
        [TestCase("1..0")]
        [TestCase("01.0.0")]
        [TestCase("-1.0.0")]
        [TestCase("+1.0.0")]
        [TestCase(" 1.0.0")]
        [TestCase("1.0.0-beta")]
        [TestCase("")]
        [TestCase(null)]
        public void AppVersionsRejectOtherForms(string version)
        {
            Assert.Throws<FormatException>(() => AvatarFormatVersions.Compare(version, "1.0.0"));
            Assert.Throws<FormatException>(() => AvatarFormatVersions.Compare("1.0.0", version));
        }

        [Test]
        public void RequiredAppVersionsCoverTheWrittenSchemas()
        {
            Assert.That(AvatarFormatVersions.RequiredAppVersionsKey, Is.EqualTo("VRVLOG_requiredAppVersions"));
            Assert.That(AvatarFormatVersions.RequiredAppVersion("VRVLOG_materials_liltoon", 2), Is.EqualTo("1.0.0"));
            Assert.That(AvatarFormatVersions.RequiredAppVersion(ExpressionAnimationData.Extension, 1), Is.EqualTo("1.0.0"));
            Assert.That(AvatarFormatVersions.RequiredAppVersion(ExpressionAnimationData.Extension, 2), Is.EqualTo("1.0.5"));
            Assert.That(AvatarFormatVersions.RequiredAppVersion("VRVLOG_humanoid_poses", 1), Is.EqualTo("1.0.0"));
            Assert.That(AvatarFormatVersions.RequiredAppVersion("VRVLOG_humanoid_animations", 1), Is.EqualTo("1.0.4"));
            Assert.Throws<InvalidOperationException>(() => AvatarFormatVersions.RequiredAppVersion(ExpressionAnimationData.Extension, 3));
            Assert.Throws<InvalidOperationException>(() => AvatarFormatVersions.RequiredAppVersion("VRVLOG_unknown", 1));
        }
    }
}
