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

        // The order key is written only when set and read back as any text;
        // a file without it, or with a value a player does not know, keeps
        // its faces.
        [Test]
        public void OrderIsOptionalAndReadAsText()
        {
            Assert.That(Json().ContainsKey("order"), Is.False);
            Assert.That(ExpressionAnimationData.ReadDocument(Json()).Order, Is.Null);
            var document = Document();
            document.Order = ExpressionAnimationDocument.SourceOrder;
            var json = (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(document)));
            Assert.That(json["order"], Is.EqualTo("source"));
            Assert.That(ExpressionAnimationData.ReadDocument(json).Order, Is.EqualTo(ExpressionAnimationDocument.SourceOrder));
            json["order"] = "a later order";
            var later = ExpressionAnimationData.ReadDocument(json);
            Assert.That(later.Order, Is.EqualTo("a later order"));
            Assert.That(later.Expressions.Count, Is.EqualTo(3));
            foreach (var notText in new object[] { null, "", 1.0, new Dictionary<string, object>() })
            {
                json["order"] = notText;
                var ignored = ExpressionAnimationData.ReadDocument(json);
                Assert.That(ignored.Order, Is.Null);
                Assert.That(ignored.Expressions.Count, Is.EqualTo(3));
            }
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
            var animation = Animation("Smile", Channel((0, null), (1, null)));
            animation.Channels[0].Points[1].Target = ExpressionAnimationData.TargetPrefix + "Smile_1";
            var json = JsonDom.Parse(JsonDom.Serialize(AnimatedExpressionFixture.SchemaOne(animation)));

            var document = ExpressionAnimationData.ReadDocument(json);
            Assert.That(document.SchemaVersion, Is.EqualTo(1));
            Assert.That(document.Expressions, Is.Empty);
            Assert.That(document.Animations.Single().Expression, Is.EqualTo("Smile"));
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

        private static ExpressionAnimationData.Curve Linear(double from, double to)
        {
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = from, InTangent = to - from, OutTangent = to - from });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1, Value = to, InTangent = to - from, OutTangent = to - from });
            return curve;
        }

        private static ExpressionAnimationData.Curve Held(double value)
        {
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
            return curve;
        }

        private static ExpressionTransformAnimation.Track Track(int node, string property, params ExpressionAnimationData.Curve[] curves)
        {
            var track = new ExpressionTransformAnimation.Track { Node = node, Property = property };
            track.Curves.AddRange(curves);
            return track;
        }

        // Smile animates a morph and, on its clock, a bone and a visibility;
        // Fist only holds a bone, so it has no animation and a still clock.
        private static ExpressionAnimationDocument WithTransforms()
        {
            var document = Document();
            var smile = new ExpressionTransformAnimation { Expression = "Smile", Duration = 1, Loop = false };
            smile.Tracks.Add(Track(41, ExpressionTransformAnimation.Euler, Linear(0, -44.7), Held(0), Held(0)));
            smile.Tracks.Add(Track(57, ExpressionTransformAnimation.Active, Held(1)));
            document.Transforms.Add(smile);
            var fist = new ExpressionTransformAnimation { Expression = "Fist", Duration = 0, Loop = false };
            fist.Tracks.Add(Track(42, ExpressionTransformAnimation.Translation, Held(0), Held(-0.01), Held(0)));
            document.Transforms.Add(fist);
            return document;
        }

        private static Dictionary<string, object> TransformsJson() =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(WithTransforms())));

        [Test]
        public void TransformsAreWrittenOnlyWhenPresent()
        {
            Assert.That(Json().ContainsKey("transforms"), Is.False);
            var json = TransformsJson();
            Assert.That(((List<object>)json["transforms"]).Count, Is.EqualTo(2));
        }

        [Test]
        public void TransformsRoundTripThroughJson()
        {
            var text = JsonDom.Serialize(ExpressionAnimationData.WriteDocument(WithTransforms()));
            var json = JsonDom.Parse(text);
            var read = ExpressionAnimationData.ReadDocument(json);
            // The BlendShape part reads as before; the transforms are a separate read.
            Assert.That(read.Expressions.Count, Is.EqualTo(3));
            Assert.That(read.Animations.Count, Is.EqualTo(2));
            Assert.That(read.Transforms, Is.Empty);
            var transforms = ExpressionAnimationData.ReadTransforms(json, read);
            Assert.That(transforms.Select(t => t.Expression), Is.EqualTo(new[] { "Smile", "Fist" }));
            Assert.That(transforms[0].Tracks.Select(t => t.Node + ":" + t.Property), Is.EqualTo(new[] { "41:euler", "57:active" }));
            var values = new double[3];
            transforms[0].Tracks[0].Evaluate(0.5, values);
            Assert.That(values[0], Is.EqualTo(-22.35).Within(1e-6));
            Assert.That(transforms[1].Duration, Is.EqualTo(0));
            Assert.That(transforms[1].TimeAt(5), Is.EqualTo(0));
            var copy = new ExpressionAnimationDocument();
            copy.Expressions.AddRange(read.Expressions);
            copy.Animations.AddRange(read.Animations);
            copy.Transforms.AddRange(transforms);
            Assert.That(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(copy)), Is.EqualTo(text));
        }

        [Test]
        public void LoopingTransformsWrapTheirTime()
        {
            var transforms = new ExpressionTransformAnimation { Expression = "Smile", Duration = 1, Loop = true };
            Assert.That(transforms.TimeAt(1.25), Is.EqualTo(0.25));
        }

        private static Action<Dictionary<string, object>> Break(string kind)
        {
            Dictionary<string, object> Entry(Dictionary<string, object> json, int index) => (Dictionary<string, object>)((List<object>)json["transforms"])[index];
            Dictionary<string, object> TrackAt(Dictionary<string, object> json, int entry, int index) => (Dictionary<string, object>)((List<object>)Entry(json, entry)["tracks"])[index];
            List<object> Key(double value) => new List<object> { 0L, value, 0L, 0L, 0.3, 0.3 };
            switch (kind)
            {
                case "unlisted expression": return json => Entry(json, 0)["expression"] = "Nope";
                case "duplicate expression": return json => Entry(json, 1)["expression"] = "Smile";
                case "another duration than the animation": return json => Entry(json, 0)["duration"] = 2.0;
                case "another loop than the animation": return json => Entry(json, 0)["loop"] = true;
                case "looping still clock": return json => Entry(json, 1)["loop"] = true;
                case "unknown property": return json => TrackAt(json, 0, 0)["property"] = "skew";
                case "wrong component count": return json => ((List<object>)TrackAt(json, 0, 0)["curves"]).RemoveAt(0);
                case "euler and rotation on one node": return json => ((List<object>)Entry(json, 0)["tracks"]).Add(new Dictionary<string, object>
                {
                    ["node"] = 41L, ["property"] = "rotation", ["curves"] = Enumerable.Range(0, 4).Select(_ => (object)new Dictionary<string, object>
                    {
                        ["preWrap"] = "clamp", ["postWrap"] = "clamp", ["keys"] = new List<object> { Key(0) }
                    }).ToList()
                });
                case "active above 1": return json => ((List<object>)((Dictionary<string, object>)((List<object>)TrackAt(json, 0, 1)["curves"])[0])["keys"])[0] = Key(2);
                case "negative node": return json => TrackAt(json, 0, 0)["node"] = -1L;
                case "no tracks": return json => Entry(json, 1)["tracks"] = new List<object>();
                case "empty transforms": return json => json["transforms"] = new List<object>();
                default: return json => json["transforms"] = "x";
            }
        }

        [TestCase("unlisted expression")]
        [TestCase("duplicate expression")]
        [TestCase("another duration than the animation")]
        [TestCase("another loop than the animation")]
        [TestCase("looping still clock")]
        [TestCase("unknown property")]
        [TestCase("wrong component count")]
        [TestCase("euler and rotation on one node")]
        [TestCase("active above 1")]
        [TestCase("negative node")]
        [TestCase("no tracks")]
        [TestCase("empty transforms")]
        [TestCase("transforms not an array")]
        public void TransformsRejectBrokenEntries(string kind)
        {
            var json = TransformsJson();
            Break(kind)(json);
            // A broken transforms key never stops the BlendShape part.
            var document = ExpressionAnimationData.ReadDocument(json);
            Assert.That(document.Animations.Count, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.ReadTransforms(json, document));
        }

        [Test]
        public void WriteDocumentRejectsTransformsOnAnotherClock()
        {
            var document = WithTransforms();
            document.Transforms[0].Duration = 3;
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.WriteDocument(document));
        }

        private static ExpressionTransformAnimation Preset(string name, params ExpressionTransformAnimation.Track[] tracks)
        {
            var preset = new ExpressionTransformAnimation { Expression = name, Duration = 0, Loop = false };
            preset.Tracks.AddRange(tracks);
            return preset;
        }

        // happy raises a bone and shows an object; aa holds a jaw bone.
        private static ExpressionAnimationDocument WithPresets(ExpressionAnimationDocument document)
        {
            document.PresetTransforms.Add(Preset("happy",
                Track(43, ExpressionTransformAnimation.Translation, Held(0), Held(0.02), Held(0)),
                Track(58, ExpressionTransformAnimation.Active, Held(1))));
            document.PresetTransforms.Add(Preset("aa", Track(44, ExpressionTransformAnimation.Euler, Held(12), Held(0), Held(0))));
            return document;
        }

        private static Dictionary<string, object> PresetsJson() =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(WithPresets(WithTransforms()))));

        [Test]
        public void PresetTransformsRoundTripApartFromTheCustomFaces()
        {
            Assert.That(TransformsJson().ContainsKey("presetTransforms"), Is.False);
            var text = JsonDom.Serialize(ExpressionAnimationData.WriteDocument(WithPresets(WithTransforms())));
            var json = JsonDom.Parse(text);
            var read = ExpressionAnimationData.ReadDocument(json);
            // Standard expressions never join the custom faces or their bones.
            Assert.That(read.Expressions.Select(e => e.Expression), Is.EqualTo(new[] { "Smile", "Fist", "Wink" }));
            Assert.That(ExpressionAnimationData.ReadTransforms(json, read).Select(t => t.Expression), Is.EqualTo(new[] { "Smile", "Fist" }));
            var presets = ExpressionAnimationData.ReadPresetTransforms(json, read);
            Assert.That(presets.Select(t => t.Expression), Is.EqualTo(new[] { "happy", "aa" }));
            Assert.That(presets[0].Tracks.Select(t => t.Node + ":" + t.Property), Is.EqualTo(new[] { "43:translation", "58:active" }));
            Assert.That(presets.All(t => t.Duration == 0 && !t.Loop), Is.True);
            var copy = new ExpressionAnimationDocument();
            copy.Expressions.AddRange(read.Expressions);
            copy.Animations.AddRange(read.Animations);
            copy.Transforms.AddRange(ExpressionAnimationData.ReadTransforms(json, read));
            copy.PresetTransforms.AddRange(presets);
            Assert.That(JsonDom.Serialize(ExpressionAnimationData.WriteDocument(copy)), Is.EqualTo(text));
        }

        [Test]
        public void ADocumentMayCarryOnlyPresetTransforms()
        {
            var json = (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(
                ExpressionAnimationData.WriteDocument(WithPresets(new ExpressionAnimationDocument()))));
            Assert.That(((List<object>)json["expressions"]).Count, Is.EqualTo(0));
            var read = ExpressionAnimationData.ReadDocument(json);
            Assert.That(read.Animations, Is.Empty);
            Assert.That(ExpressionAnimationData.ReadTransforms(json, read), Is.Empty);
            Assert.That(ExpressionAnimationData.ReadPresetTransforms(json, read).Count, Is.EqualTo(2));
        }

        [Test]
        public void ReadersThatSkipPresetTransformsKeepEverythingElse()
        {
            // A reader before presetTransforms runs ReadDocument and ReadTransforms
            // only; both read the same as without the key.
            var with = PresetsJson();
            var without = TransformsJson();
            var read = ExpressionAnimationData.ReadDocument(with);
            var before = ExpressionAnimationData.ReadDocument(without);
            Assert.That(read.Expressions.Select(e => e.Expression + "/" + e.Source), Is.EqualTo(before.Expressions.Select(e => e.Expression + "/" + e.Source)));
            Assert.That(read.Animations.Select(a => a.Expression), Is.EqualTo(before.Animations.Select(a => a.Expression)));
            Assert.That(ExpressionAnimationData.ReadTransforms(with, read).Select(t => t.Expression),
                Is.EqualTo(ExpressionAnimationData.ReadTransforms(without, before).Select(t => t.Expression)));
            // Files written before the key read no preset transforms.
            Assert.That(ExpressionAnimationData.ReadPresetTransforms(Json(), ExpressionAnimationData.ReadDocument(Json())), Is.Empty);
        }

        private static Action<Dictionary<string, object>> BreakPresets(string kind)
        {
            Dictionary<string, object> Entry(Dictionary<string, object> json, int index) => (Dictionary<string, object>)((List<object>)json["presetTransforms"])[index];
            switch (kind)
            {
                case "gaze": return json => Entry(json, 0)["expression"] = "lookUp";
                case "neutral": return json => Entry(json, 0)["expression"] = "neutral";
                case "custom face": return json => Entry(json, 0)["expression"] = "Smile";
                case "other case": return json => Entry(json, 0)["expression"] = "Happy";
                case "duplicate": return json => Entry(json, 1)["expression"] = "happy";
                case "a clock": return json => Entry(json, 0)["duration"] = 1.0;
                case "loop": return json => Entry(json, 0)["loop"] = true;
                case "no tracks": return json => Entry(json, 1)["tracks"] = new List<object>();
                case "unknown property": return json => ((Dictionary<string, object>)((List<object>)Entry(json, 0)["tracks"])[0])["property"] = "skew";
                case "empty": return json => json["presetTransforms"] = new List<object>();
                default: return json => json["presetTransforms"] = "x";
            }
        }

        [TestCase("gaze")]
        [TestCase("neutral")]
        [TestCase("custom face")]
        [TestCase("other case")]
        [TestCase("duplicate")]
        [TestCase("a clock")]
        [TestCase("loop")]
        [TestCase("no tracks")]
        [TestCase("unknown property")]
        [TestCase("empty")]
        [TestCase("not an array")]
        public void PresetTransformsRejectBrokenEntries(string kind)
        {
            var json = PresetsJson();
            BreakPresets(kind)(json);
            // A broken presetTransforms key stops neither the faces nor their bones.
            var document = ExpressionAnimationData.ReadDocument(json);
            Assert.That(document.Animations.Count, Is.EqualTo(2));
            Assert.That(ExpressionAnimationData.ReadTransforms(json, document).Count, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.ReadPresetTransforms(json, document));
        }

        [Test]
        public void WriteDocumentRejectsPresetTransformsItCannotRead()
        {
            var gaze = WithPresets(Document());
            gaze.PresetTransforms[0].Expression = "lookLeft";
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.WriteDocument(gaze));
            var clock = WithPresets(Document());
            clock.PresetTransforms[1].Duration = 1;
            Assert.Throws<InvalidOperationException>(() => ExpressionAnimationData.WriteDocument(clock));
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
        [TestCase("1..0")]
        [TestCase("01.0.0")]
        [TestCase("+1.0.0")]
        [TestCase(" 1.0.0")]
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
