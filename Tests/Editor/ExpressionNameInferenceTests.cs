using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UniVRM10;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionNameInferenceTests
    {
        [TestCase("Smile", ExpressionPreset.happy, 0)]
        [TestCase("happy", ExpressionPreset.happy, 0)]
        [TestCase("faceSmile", ExpressionPreset.happy, 0)]
        [TestCase("joy", ExpressionPreset.happy, 1)]
        [TestCase("Angry", ExpressionPreset.angry, 0)]
        [TestCase("sad", ExpressionPreset.sad, 0)]
        [TestCase("relaxed", ExpressionPreset.relaxed, 0)]
        [TestCase("Surprised", ExpressionPreset.surprised, 0)]
        [TestCase("笑顔", ExpressionPreset.happy, 0)]
        [TestCase("怒り", ExpressionPreset.angry, 0)]
        [TestCase("悲しい", ExpressionPreset.sad, 0)]
        [TestCase("びっくり", ExpressionPreset.surprised, 0)]
        [TestCase("驚き", ExpressionPreset.surprised, 0)]
        public void MenuStyleNamesNameOneEmotion(string name, ExpressionPreset expected, int tier) =>
            Assert.That(ExpressionNameInference.MatchEmotion(name), Is.EqualTo(((ExpressionPreset, int)?)(expected, tier)));

        [TestCase("Fcl_ALL_Joy", ExpressionPreset.happy, 1)]
        [TestCase("Fcl_ALL_Angry", ExpressionPreset.angry, 0)]
        [TestCase("Fcl_ALL_Sorrow", ExpressionPreset.sad, 0)]
        [TestCase("Fcl_ALL_Fun", ExpressionPreset.relaxed, 0)]
        [TestCase("Fcl_ALL_Surprised", ExpressionPreset.surprised, 0)]
        public void WholeFaceBlendShapeNamesNameOneEmotion(string name, ExpressionPreset expected, int tier) =>
            Assert.That(ExpressionNameInference.MatchEmotion(name, latinOnly: true), Is.EqualTo(((ExpressionPreset, int)?)(expected, tier)));

        // MMD-style names move only a part of the face.
        [TestCase("怒り")]
        [TestCase("にっこり")]
        [TestCase("びっくり")]
        [TestCase("笑い")]
        [TestCase("悲しい")]
        public void JapaneseBlendShapeNamesAreNotRawCandidates(string name) =>
            Assert.That(ExpressionNameInference.MatchEmotion(name, latinOnly: true), Is.Null);

        [TestCase("eye_smile_L")]
        [TestCase("Fcl_EYE_Joy")]
        [TestCase("Fcl_MTH_Angry")]
        [TestCase("Fcl_BRW_Sorrow")]
        [TestCase("mouth_smile")]
        [TestCase("smile_R")]
        [TestCase("wink")]
        [TestCase("blink_happy")]
        [TestCase("happy_angry")]
        [TestCase("smileOff")]
        [TestCase("Reset")]
        public void PartialOneSidedOrMixedNamesNameNothing(string name)
        {
            Assert.That(ExpressionNameInference.MatchEmotion(name), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion(name, latinOnly: true), Is.Null);
        }

        // The leaf label decides; a parent only speaks when every level below says nothing.
        [TestCase("喜怒哀楽 / 笑顔", ExpressionPreset.happy, 0)]
        [TestCase("喜怒哀楽 / 怒り", ExpressionPreset.angry, 0)]
        [TestCase("Eye / Smile", ExpressionPreset.happy, 0)]
        [TestCase("Left / Sad", ExpressionPreset.sad, 0)]
        [TestCase("Smile / 1", ExpressionPreset.happy, 0)]
        [TestCase("Emotes / Joy / A", ExpressionPreset.happy, 1)]
        [TestCase("Surprised", ExpressionPreset.surprised, 0)]
        public void MenuPathsAreDecidedByTheLeafFirst(string path, ExpressionPreset expected, int tier) =>
            Assert.That(ExpressionNameInference.MatchMenuPath(path), Is.EqualTo(((ExpressionPreset, int)?)(expected, tier)));

        // A level with a veto word or several emotions stops the walk.
        [TestCase("喜怒哀楽 / 1")]
        [TestCase("Eye / 1")]
        [TestCase("Smile / Wink")]
        [TestCase("Smile / Eye / 1")]
        [TestCase("Happy / smile_angry")]
        [TestCase("Emotes / A")]
        public void MenuPathsWithoutAWholeEmotionNameNothing(string path) =>
            Assert.That(ExpressionNameInference.MatchMenuPath(path), Is.Null);

        static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (var i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        static List<object> Arr(params object[] values) => values.ToList();
        static Dictionary<string, object> Mesh(params string[] names) => Obj("extras", Obj("targetNames", names.Cast<object>().ToList()),
            "primitives", Arr(Obj("targets", names.Select(n => (object)Obj()).ToList())));
        static Dictionary<string, object> Bind(long node, long index) => Obj("node", node, "index", index, "weight", 1.0);
        static Dictionary<string, object> MenuFace(params Dictionary<string, object>[] binds) => Obj("morphTargetBinds", binds.Cast<object>().ToList(),
            "isBinary", true, "overrideBlink", "block", "overrideMouth", "block", "overrideLookAt", "block");

        static byte[] Glb(Dictionary<string, object> presets, Dictionary<string, object> custom, List<object> nodes, List<object> meshes)
        {
            var expressions = new Dictionary<string, object>();
            if (presets != null) expressions["preset"] = presets;
            if (custom != null) expressions["custom"] = custom;
            var root = Obj("asset", Obj("version", "2.0"), "extensions", Obj("VRMC_vrm", Obj("expressions", expressions)),
                "nodes", nodes, "meshes", meshes);
            return GlbDocument.Create(root, new byte[] { 1, 2, 3, 4 }).Write();
        }

        // registered: the custom expressions this export added, each inferred
        // from its own name. Null treats every custom expression in the file so.
        static byte[] Infer(byte[] bytes, List<string> warnings = null, List<(string Preset, string Source)> filled = null, bool inferEmotions = true,
            IEnumerable<string> registered = null) =>
            Infer(bytes, (registered ?? CustomNames(bytes)).ToDictionary(name => name, name => name, StringComparer.Ordinal), warnings, filled, inferEmotions);

        // paths: final VRM name -> the text inference reads (the menu path).
        // shapes/table: the source shapes each face moves and the name table.
        static byte[] Infer(byte[] bytes, IReadOnlyDictionary<string, string> paths, List<string> warnings = null,
            List<(string Preset, string Source)> filled = null, bool inferEmotions = true,
            IReadOnlyDictionary<string, IReadOnlyList<(string Shape, float Weight)>> shapes = null, ExpressionShapeNames table = null,
            List<string> formats = null)
        {
            var all = new List<(string Preset, string Source, string Format)>();
            var output = VrmExpressionBindings.AddMissing(bytes, warnings, inferBlink: false, inferEmotions: inferEmotions, filledEmotions: all,
                emotionCandidates: paths, emotionShapes: shapes, shapeNames: table);
            filled?.AddRange(all.Select(item => (item.Preset, item.Source)));
            formats?.AddRange(all.Select(item => item.Format));
            return output;
        }

        static IEnumerable<string> CustomNames(byte[] bytes) =>
            Expressions(bytes).TryGetValue("custom", out var value) ? ((Dictionary<string, object>)value).Keys.ToArray() : Array.Empty<string>();

        static Dictionary<string, object> Expressions(byte[] bytes) =>
            (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"])["VRMC_vrm"])["expressions"];
        static Dictionary<string, object> Presets(byte[] bytes) =>
            Expressions(bytes).TryGetValue("preset", out var value) ? (Dictionary<string, object>)value : new Dictionary<string, object>();

        static string Canonical(object value)
        {
            switch (value)
            {
                case null: return "null";
                case string text: return "\"" + text + "\"";
                case bool flag: return flag ? "true" : "false";
                case Dictionary<string, object> map:
                    return "{" + string.Join(",", map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Key + ":" + Canonical(pair.Value))) + "}";
                case List<object> list: return "[" + string.Join(",", list.Select(Canonical)) + "]";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        [Test]
        public void MenuFaceFillsEmptyEmotionWithACopyOfItsBinds()
        {
            var warnings = new List<string>();
            var filled = new List<(string Preset, string Source)>();
            var face = MenuFace(Bind(0, 1));
            face["materialColorBinds"] = Arr(Obj("material", 0L, "type", "color", "targetValue", Arr(1.0, 0.0, 0.0, 1.0)));
            var bytes = Glb(null, Obj("笑顔", face), Arr(Obj("mesh", 0L)), Arr(Mesh("base", "composite")));
            var output = Infer(bytes, warnings, filled);
            var happy = (Dictionary<string, object>)Presets(output)["happy"];
            Assert.That(Canonical(happy["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
            Assert.That(Canonical(happy["materialColorBinds"]), Is.EqualTo(Canonical(face["materialColorBinds"])));
            Assert.That(happy["isBinary"], Is.False);
            Assert.That(happy["overrideBlink"], Is.EqualTo("block"));
            Assert.That(happy["overrideMouth"], Is.EqualTo("block"));
            Assert.That(happy["overrideLookAt"], Is.EqualTo("block"));
            Assert.That(Presets(output).Keys, Is.EquivalentTo(new[] { "happy" }), "One menu face fills one emotion.");
            Assert.That(((Dictionary<string, object>)Expressions(output)["custom"]).ContainsKey("笑顔"), Is.True, "The menu face stays selectable.");
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "笑顔") }));
            Assert.That(warnings, Does.Contain("happy: 名前から自動で設定しました（笑顔）。動作を確認してください。"));

            var parsed = GlbDocument.Read(output).Json;
            var expressions = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)parsed["extensions"])["VRMC_vrm"])["expressions"];
            var custom = (Dictionary<string, object>)((Dictionary<string, object>)expressions["custom"])["笑顔"];
            var preset = (Dictionary<string, object>)((Dictionary<string, object>)expressions["preset"])["happy"];
            Assert.That(preset["morphTargetBinds"], Is.Not.SameAs(custom["morphTargetBinds"]));
            Assert.That(custom["isBinary"], Is.True, "The custom expression itself is unchanged.");
        }

        [Test]
        public void RecordedFacesFillByTheirPlainName()
        {
            var filled = new List<(string Preset, string Source)>();
            var bytes = Glb(null, Obj("Surprised", MenuFace(Bind(0, 0))), Arr(Obj("mesh", 0L)), Arr(Mesh("composite")));
            Infer(bytes, filled: filled, registered: new[] { "Surprised" });
            Assert.That(filled, Is.EqualTo(new[] { ("surprised", "Surprised") }));
        }

        // Names carry no source prefix, so the exporter says which custom
        // expressions it registered. An authored custom expression with an
        // emotion name never fills a preset, even when it moves something.
        [Test]
        public void AuthoredCustomExpressionsAreNotCandidates()
        {
            var custom = Obj("笑顔", MenuFace(Bind(0, 0)), "怒り", MenuFace(Bind(0, 1)), "Sad (2)", MenuFace(Bind(0, 2)));
            var bytes = Glb(null, custom, Arr(Obj("mesh", 0L)), Arr(Mesh("s0", "s1", "s2")));
            var filled = new List<(string Preset, string Source)>();
            var presets = Presets(Infer(bytes, filled: filled, registered: new[] { "怒り", "Sad (2)" }));
            Assert.That(filled, Is.EqualTo(new[] { ("angry", "怒り"), ("sad", "Sad (2)") }), "A numbered menu face still names its emotion.");
            Assert.That(presets.ContainsKey("happy"), Is.False, "The authored 笑顔 is not a candidate.");
            Assert.That(Infer(bytes, registered: Array.Empty<string>()), Is.EqualTo(bytes), "Without registered faces nothing is filled from custom names.");
            Assert.That(VrmExpressionBindings.AddMissing(bytes, inferBlink: false, inferEmotions: true), Is.EqualTo(bytes),
                "A caller that names no candidates fills nothing from custom names.");
        }

        [Test]
        public void AuthoredEmotionThatMovesSomethingIsKept()
        {
            var authored = Obj("morphTargetBinds", Arr(Bind(0, 0)), "isBinary", false);
            var bytes = Glb(Obj("happy", authored), Obj("笑顔", MenuFace(Bind(0, 1))),
                Arr(Obj("mesh", 0L)), Arr(Mesh("base", "composite", "Fcl_ALL_Joy")));
            var warnings = new List<string>();
            var output = Infer(bytes, warnings);
            Assert.That(output, Is.EqualTo(bytes), "Nothing else is fillable, so the file is byte-identical.");
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void AuthoredEmotionWithoutBindsIsFilled()
        {
            var bytes = Glb(Obj("happy", Obj("morphTargetBinds", Arr(), "isBinary", false)), Obj("笑顔", MenuFace(Bind(0, 1))),
                Arr(Obj("mesh", 0L)), Arr(Mesh("base", "composite")));
            var happy = (Dictionary<string, object>)Presets(Infer(bytes))["happy"];
            Assert.That(Canonical(happy["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
        }

        [Test]
        public void WholeFaceBlendShapeFillsEveryNodeThatHasIt()
        {
            var filled = new List<(string Preset, string Source)>();
            var warnings = new List<string>();
            var bytes = Glb(null, null, Arr(Obj("mesh", 0L), Obj(), Obj("mesh", 1L)),
                Arr(Mesh("Fcl_ALL_Angry", "Fcl_ALL_Joy"), Mesh("other", "Fcl_ALL_Angry")));
            var presets = Presets(Infer(bytes, warnings, filled));
            Assert.That(Canonical(((Dictionary<string, object>)presets["angry"])["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 0), Bind(2, 1)))));
            Assert.That(((Dictionary<string, object>)presets["angry"])["isBinary"], Is.False);
            // A whole-face shape already shapes the eyes; auto blink must not stack on it.
            Assert.That(((Dictionary<string, object>)presets["angry"])["overrideBlink"], Is.EqualTo("block"));
            Assert.That(((Dictionary<string, object>)presets["angry"]).ContainsKey("overrideMouth"), Is.False);
            Assert.That(((Dictionary<string, object>)presets["angry"]).ContainsKey("overrideLookAt"), Is.False);
            Assert.That(Canonical(((Dictionary<string, object>)presets["happy"])["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
            Assert.That(filled, Is.EquivalentTo(new[] { ("angry", "Fcl_ALL_Angry"), ("happy", "Fcl_ALL_Joy") }));
            Assert.That(warnings, Does.Contain("angry: 名前から自動で設定しました（Fcl_ALL_Angry）。動作を確認してください。"));
        }

        [Test]
        public void MenuFaceWinsOverBlendShapeForTheSameEmotion()
        {
            var filled = new List<(string Preset, string Source)>();
            var bytes = Glb(null, Obj("Smile", MenuFace(Bind(0, 2))), Arr(Obj("mesh", 0L)),
                Arr(Mesh("Fcl_ALL_Joy", "Fcl_ALL_Angry", "composite")));
            var presets = Presets(Infer(bytes, filled: filled));
            Assert.That(Canonical(((Dictionary<string, object>)presets["happy"])["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 2)))));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "Smile"), ("angry", "Fcl_ALL_Angry") }));
        }

        [Test]
        public void PartialAndJapaneseBlendShapesFillNothing()
        {
            var bytes = Glb(null, null, Arr(Obj("mesh", 0L)), Arr(Mesh("eye_smile_L", "怒り", "にっこり", "Fcl_EYE_Joy", "Fcl_MTH_Angry")));
            var warnings = new List<string>();
            Assert.That(Infer(bytes, warnings), Is.EqualTo(bytes));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void DuplicateBlendShapeInOneMeshIsSkippedWithAWarning()
        {
            var warnings = new List<string>();
            var bytes = Glb(null, null, Arr(Obj("mesh", 0L)), Arr(Mesh("Fcl_ALL_Fun", "Fcl_ALL_Fun")));
            Assert.That(Infer(bytes, warnings), Is.EqualTo(bytes));
            Assert.That(warnings, Does.Contain("relaxed: mesh 0 に同名のBlendShape（Fcl_ALL_Fun）が複数あるため、この名前は自動設定に使いませんでした。"));
        }

        [Test]
        public void DuplicateInOneMeshDropsTheNameForEveryMesh()
        {
            var warnings = new List<string>();
            var filled = new List<(string Preset, string Source)>();
            var bytes = Glb(null, null, Arr(Obj("mesh", 0L), Obj("mesh", 1L)),
                Arr(Mesh("Fcl_ALL_Fun", "Fcl_ALL_Fun"), Mesh("Fcl_ALL_Fun", "Calm")));
            var relaxed = (Dictionary<string, object>)Presets(Infer(bytes, warnings, filled))["relaxed"];
            Assert.That(Canonical(relaxed["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(1, 1)))),
                "The single copy in mesh 1 alone would move only part of the face; the next name is used.");
            Assert.That(filled, Is.EqualTo(new[] { ("relaxed", "Calm") }));
            Assert.That(warnings, Does.Contain("relaxed: mesh 0 に同名のBlendShape（Fcl_ALL_Fun）が複数あるため、この名前は自動設定に使いませんでした。"));
        }

        // The VRM name is the item label alone, but inference still reads the
        // menu path: the leaf decides, and a parent speaks only when every
        // level below says nothing.
        [Test]
        public void MenuFacesInsideAnEmotionSubmenuAreDecidedByTheirLeaf()
        {
            var filled = new List<(string Preset, string Source)>();
            var custom = Obj("笑顔", MenuFace(Bind(0, 0)), "怒り", MenuFace(Bind(0, 1)),
                "1", MenuFace(Bind(0, 2)), "Sad", MenuFace(Bind(0, 3)), "Wink", MenuFace(Bind(0, 4)));
            var bytes = Glb(null, custom, Arr(Obj("mesh", 0L)), Arr(Mesh("a", "b", "c", "d", "e")));
            var paths = new Dictionary<string, string> {
                ["笑顔"] = "喜怒哀楽 / 笑顔", ["怒り"] = "喜怒哀楽 / 怒り", ["1"] = "喜怒哀楽 / 1", ["Sad"] = "Eye / Sad", ["Wink"] = "Smile / Wink" };
            var presets = Presets(Infer(bytes, paths, filled: filled));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "笑顔"), ("angry", "怒り"), ("sad", "Sad") }));
            Assert.That(Canonical(((Dictionary<string, object>)presets["sad"])["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 3)))));
            Assert.That(presets.ContainsKey("relaxed") || presets.ContainsKey("surprised"), Is.False);
        }

        // An item "1" inside a "Smile" submenu is named "1" (or "1 (2)") in
        // the VRM and still fills happy, as it did when the name kept its path.
        [Test]
        public void ItemOfAnEmotionSubmenuStillNamesTheEmotion()
        {
            var filled = new List<(string Preset, string Source)>();
            var custom = Obj("1", MenuFace(Bind(0, 0)), "1 (2)", MenuFace(Bind(0, 1)));
            var bytes = Glb(null, custom, Arr(Obj("mesh", 0L)), Arr(Mesh("s0", "s1")));
            var paths = new Dictionary<string, string> { ["1"] = "Other / 1", ["1 (2)"] = "Smile / 1" };
            var happy = (Dictionary<string, object>)Presets(Infer(bytes, paths, filled: filled))["happy"];
            Assert.That(Canonical(happy["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "1 (2)") }), "The result names the face by its VRM name.");
            Assert.That(Infer(bytes, registered: new[] { "1", "1 (2)" }), Is.EqualTo(bytes), "The label alone names no emotion.");
        }

        [Test]
        public void FilledEmotionsAreKeptLinesOfTheResultList()
        {
            var report = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordInferredEmotions(new[] { ("happy", "笑顔", VrmExpressionBindings.FilledFormat),
                ("angry", "Fcl_ALL_Angry", VrmExpressionBindings.FilledFormat), ("sad", "3", VrmExpressionBindings.ShapeFilledFormat) }, report);
            var results = report.Fallbacks.Results;
            Assert.That(results.Select(result => result.Name), Is.EqualTo(new[] { "happy", "angry", "sad" }));
            Assert.That(results.All(result => result.Outcome == ExportOutcome.Kept && result.Stage == null), Is.True);
            Assert.That(results.Select(result => result.Category).Distinct(),
                Is.EqualTo(new[] { ExporterLocalization.T(UniVrmOneClickExporter.InferredEmotionCategory) }));
            Assert.That(results[0].Reason, Is.EqualTo(string.Format(ExporterLocalization.T(VrmExpressionBindings.FilledFormat), "happy", "笑顔")));
            Assert.That(results[2].Reason, Is.EqualTo(string.Format(ExporterLocalization.T(VrmExpressionBindings.ShapeFilledFormat), "sad", "3")),
                "A face filled from its shapes says so in the same row.");
            Assert.That(report.Fallbacks.Summary()[ExportOutcome.Kept], Is.EqualTo(3));
            Assert.That(report.Fallbacks.Count, Is.Zero, "Nothing is asked before saving.");
        }

        [Test]
        public void DisabledInferenceFillsNothing()
        {
            var bytes = Glb(null, Obj("笑顔", MenuFace(Bind(0, 1))), Arr(Obj("mesh", 0L)), Arr(Mesh("Fcl_ALL_Angry", "composite")));
            var filled = new List<(string Preset, string Source)>();
            Assert.That(Infer(bytes, filled: filled, inferEmotions: false), Is.EqualTo(bytes));
            Assert.That(VrmExpressionBindings.AddMissing(bytes, inferBlink: false), Is.EqualTo(bytes), "The default leaves emotions alone.");
            Assert.That(filled, Is.Empty);
        }

        const string SmallTable = @"{""minWeight"":50,""labels"":[
            {""label"":""eye.happy"",""names"":[""笑い"",""eye_smile""]},{""label"":""eye.gimmick"",""names"":[""星目""]},
            {""label"":""eye.wide"",""names"":[""eye_surprised""]},{""label"":""eye.relaxed"",""names"":[""なごみ""]},
            {""label"":""brow.angry"",""names"":[""怒り""]},{""label"":""brow.sad"",""names"":[""困る""]},
            {""label"":""mouth.smile"",""names"":[""にやり""]},{""label"":""mouth.frown"",""names"":[""口角下げ"",""へ""]},
            {""label"":""mouth.o"",""names"":[""mouth_○""]},{""label"":""mouth.other"",""names"":[""ω""]},{""label"":""tear"",""names"":[""涙""]},
            {""label"":""mouth.viseme"",""names"":[""あ""]},{""label"":""eye.close"",""names"":[""まばたき""]},{""label"":""whole.happy"",""names"":[""fcl_all_joy""]}]}";

        static (string, float)[] Moves(params object[] pairs)
        {
            var result = new (string, float)[pairs.Length / 2];
            for (var i = 0; i < result.Length; i++) result[i] = ((string)pairs[i * 2], Convert.ToSingle(pairs[i * 2 + 1], CultureInfo.InvariantCulture));
            return result;
        }

        static Dictionary<string, IReadOnlyList<(string Shape, float Weight)>> Shapes(params (string Name, (string, float)[] Moves)[] faces) =>
            faces.ToDictionary(face => face.Name, face => (IReadOnlyList<(string Shape, float Weight)>)face.Moves.Select(m => (m.Item1, m.Item2)).ToList(), StringComparer.Ordinal);

        [Test]
        public void ShippedTableLoadsWithRepresentativeNames()
        {
            var table = ExpressionShapeNames.Shipped;
            Assert.That(table.Count, Is.GreaterThan(100));
            Assert.That(table.MinWeight, Is.EqualTo(50f));
            Assert.That(table.PartOf("笑い"), Is.EqualTo("eye.happy"));
            Assert.That(table.PartOf("口角下げ"), Is.EqualTo("mouth.frown"));
            Assert.That(table.PartOf("困る"), Is.EqualTo("brow.sad"));
            Assert.That(table.PartOf("Eye_Smile_L"), Is.EqualTo("eye.happy"), "A one-side name reads through its plain form.");
            Assert.That(table.PartOf("Fcl_ALL_Joy"), Is.EqualTo("whole.happy"), "A Latin whole-face name is the whole face.");
            Assert.That(table.PartOf("Body_Height"), Is.Null);
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 100))?.Preset, Is.EqualTo(ExpressionPreset.happy));
            Assert.That(table.Classify(Moves("困る", 100, "口角下げ", 100)), Is.Null, "Sad parts alone do not decide sad.");
        }

        [TestCase("Eye Smile-L", "eye_smile_l")]
        [TestCase("ＥＹＥ＿ＳＭＩＬＥ", "eye_smile")]
        [TestCase(" 眉・怒り ", "眉_怒り")]
        public void ShapeNamesAreNormalized(string name, string expected) =>
            Assert.That(ExpressionShapeNames.Normalize(name), Is.EqualTo(expected));

        [Test]
        public void FaceShapesNameOneEmotionOnlyWhenPartsAgree()
        {
            var table = ExpressionShapeNames.Parse(SmallTable);
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 2)));
            Assert.That(table.Classify(Moves("21_Eye_Smile_R", 100, "にやり", 80))?.Preset, Is.EqualTo(ExpressionPreset.happy));
            Assert.That(table.Classify(Moves("困る", 100, "口角下げ", 100)), Is.Null, "Sad and angry need a whole-face name.");
            Assert.That(table.Classify(Moves("怒り", 100, "へ", 100)), Is.Null);
            Assert.That(table.Classify(Moves("Fcl_ALL_Sorrow", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.sad, 1)));
            Assert.That(table.Classify(Moves("笑い", 100, "困る", 100, "涙", 100)), Is.Null, "A crying smile is not happy.");
            Assert.That(table.Classify(Moves("eye_surprised", 100, "mouth_○", 100))?.Preset, Is.EqualTo(ExpressionPreset.surprised));
            Assert.That(table.Classify(Moves("なごみ", 100, "ω", 100))?.Preset, Is.EqualTo(ExpressionPreset.relaxed));
            Assert.That(table.Classify(Moves("Fcl_ALL_Angry", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.angry, 1)));
            Assert.That(table.Classify(Moves("笑い", 100)), Is.Null, "One part alone is not a whole face.");
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 30)), Is.Null, "A weak shape is not part of the face.");
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 100, "星目", 100)), Is.Null, "Gimmick eyes are not an emotion.");
            Assert.That(table.Classify(Moves("笑い", 100, "口角下げ", 100)), Is.Null, "Parts that disagree say nothing.");
            Assert.That(table.Classify(Moves("Fcl_ALL_Angry", 100, "Fcl_ALL_Joy", 100)), Is.Null);
            Assert.That(table.Classify(Moves("unknown_a", 100, "unknown_b", 100)), Is.Null);
        }

        [Test]
        public void UnlistedWholeFaceNameRanksBelowAListedOne()
        {
            var table = ExpressionShapeNames.Parse(SmallTable);
            Assert.That(table.Classify(Moves("Fcl_ALL_Joy", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 3)));
            Assert.That(table.Classify(Moves("smile2", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 1)),
                "A name the table does not list only reads as an emotion.");
            Assert.That(table.Classify(Moves("smile2", 100, "Fcl_ALL_Joy", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 3)));
            Assert.That(table.Classify(Moves("smile2", 100, "Fcl_ALL_Angry", 100)), Is.Null);
        }

        [Test]
        public void OnlyPartsThatShowTheEmotionSupportIt()
        {
            var table = ExpressionShapeNames.Parse(SmallTable);
            Assert.That(table.Classify(Moves("笑い", 100, "あ", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 1)),
                "A viseme mouth does not support the smile.");
            Assert.That(table.Classify(Moves("にやり", 100, "まばたき", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 1)),
                "A blink does not support the smile.");
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 2)));
            Assert.That(table.Classify(Moves("笑い", 100, "にやり", 100, "あ", 100)), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.happy, 2)));
        }

        [Test]
        public void EmptyTableOrShapesGuessNothing()
        {
            Assert.That(ExpressionShapeNames.Parse("").Count, Is.Zero);
            Assert.That(ExpressionShapeNames.Parse(@"{""labels"":[]}").Classify(Moves("笑い", 100, "にやり", 100)), Is.Null);
            Assert.That(ExpressionShapeNames.Parse(SmallTable).Classify(Moves()), Is.Null);
            var bytes = Glb(null, Obj("1", MenuFace(Bind(0, 1))), Arr(Obj("mesh", 0L)), Arr(Mesh("body", "composite")));
            var paths = new Dictionary<string, string> { { "1", "1" } };
            var face = Shapes(("1", Moves("笑い", 100, "にやり", 100)));
            Assert.That(Infer(bytes, paths, shapes: face, table: ExpressionShapeNames.Parse("")), Is.EqualTo(bytes));
            Assert.That(Infer(bytes, paths, shapes: face), Is.EqualTo(bytes), "Without a table nothing is guessed from shapes.");
            Assert.That(Infer(bytes, paths, shapes: Shapes(), table: ExpressionShapeNames.Parse(SmallTable)), Is.EqualTo(bytes));
        }

        [TestCase("1", true)]
        [TestCase("Face 3", true)]
        [TestCase("喜怒哀楽 / 1", true)]
        [TestCase("Emotes / A", true)]
        [TestCase("Eye / 1", false)]
        [TestCase("Smile / 1", false)]
        [TestCase("笑顔", false)]
        [TestCase("ウィンク", false)]
        [TestCase("Happy / smile_angry", false)]
        public void OnlyLabelsThatSayNothingAreGuessedFromShapes(string path, bool expected) =>
            Assert.That(ExpressionNameInference.SaysNothing(path), Is.EqualTo(expected));

        [Test]
        public void UnnamedMenuFaceFillsTheEmotionItsShapesShow()
        {
            var bytes = Glb(null, Obj("1", MenuFace(Bind(0, 1)), "2", MenuFace(Bind(0, 1)), "3", MenuFace(Bind(0, 1)), "Wink", MenuFace(Bind(0, 1))),
                Arr(Obj("mesh", 0L)), Arr(Mesh("body", "composite")));
            var paths = new Dictionary<string, string> { { "1", "喜怒哀楽 / 1" }, { "2", "喜怒哀楽 / 2" }, { "3", "喜怒哀楽 / 3" }, { "Wink", "ウィンク" } };
            var shapes = Shapes(("1", Moves("笑い", 100, "にやり", 100)), ("2", Moves("笑い", 100)), ("3", Moves("困る", 100, "口角下げ", 100)),
                ("Wink", Moves("怒り", 100, "へ", 100)));
            var warnings = new List<string>();
            var filled = new List<(string Preset, string Source)>();
            var formats = new List<string>();
            var presets = Presets(Infer(bytes, paths, warnings, filled, shapes: shapes, table: ExpressionShapeNames.Parse(SmallTable), formats: formats));
            Assert.That(filled, Is.EquivalentTo(new[] { ("happy", "1") }), "A part-only face, sad parts and a named part are not used.");
            Assert.That(formats.Distinct(), Is.EqualTo(new[] { VrmExpressionBindings.ShapeFilledFormat }));
            Assert.That(warnings, Has.Some.EqualTo(string.Format(ExporterLocalization.T(VrmExpressionBindings.ShapeFilledFormat), "happy", "1")));
            Assert.That(presets.Keys, Is.EquivalentTo(new[] { "happy" }));
            var happy = (Dictionary<string, object>)presets["happy"];
            Assert.That(happy["isBinary"], Is.EqualTo(false));
            Assert.That(Canonical(happy["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
            Assert.That(happy["overrideBlink"], Is.EqualTo("block"), "The menu face's overrides are copied as in the name tier.");
        }

        [Test]
        public void ShapeGuessNeverReplacesAnEarlierFill()
        {
            var authored = Obj("morphTargetBinds", Arr(Bind(0, 0)), "isBinary", false);
            var bytes = Glb(Obj("happy", authored), Obj("笑顔", MenuFace(Bind(0, 1)), "1", MenuFace(Bind(0, 1)), "2", MenuFace(Bind(0, 1))),
                Arr(Obj("mesh", 0L)), Arr(Mesh("body", "composite")));
            var paths = new Dictionary<string, string> { { "笑顔", "笑顔" }, { "1", "1" }, { "2", "2" } };
            var shapes = Shapes(("笑顔", Moves("なごみ", 100, "ω", 100)), ("1", Moves("笑い", 100, "にやり", 100)), ("2", Moves("なごみ", 100, "ω", 100)));
            var table = ExpressionShapeNames.Parse(SmallTable);
            var filled = new List<(string Preset, string Source)>();
            var presets = Presets(Infer(bytes, paths, filled: filled, shapes: shapes, table: table));
            Assert.That(Canonical(presets["happy"]), Is.EqualTo(Canonical(authored)), "An authored preset stays.");
            Assert.That(filled, Is.EqualTo(new[] { ("relaxed", "2") }), "A named face is never guessed again from its shapes.");

            // The name tier fills first; the shape guess only takes what is left.
            var named = Glb(null, Obj("1", MenuFace(Bind(0, 1)), "Relaxed", MenuFace(Bind(0, 1))), Arr(Obj("mesh", 0L)), Arr(Mesh("body", "composite")));
            filled.Clear();
            Infer(named, new Dictionary<string, string> { { "1", "1" }, { "Relaxed", "Relaxed" } }, filled: filled,
                shapes: Shapes(("1", Moves("なごみ", 100, "ω", 100))), table: table);
            Assert.That(filled, Is.EqualTo(new[] { ("relaxed", "Relaxed") }));
        }

        [Test]
        public void BetterSupportedFaceWinsTheShapeGuess()
        {
            var bytes = Glb(null, Obj("1", MenuFace(Bind(0, 1)), "2", MenuFace(Bind(0, 1))), Arr(Obj("mesh", 0L)), Arr(Mesh("body", "composite")));
            var paths = new Dictionary<string, string> { { "1", "1" }, { "2", "2" } };
            var filled = new List<(string Preset, string Source)>();
            Infer(bytes, paths, filled: filled, table: ExpressionShapeNames.Parse(SmallTable),
                shapes: Shapes(("1", Moves("笑い", 100, "にやり", 100)), ("2", Moves("Fcl_ALL_Joy", 100))));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "2") }), "A listed whole-face shape beats two agreeing parts.");

            // A name the table does not list only reads as an emotion and ranks below agreeing parts.
            filled.Clear();
            Infer(bytes, paths, filled: filled, table: ExpressionShapeNames.Parse(SmallTable),
                shapes: Shapes(("1", Moves("smile2", 100)), ("2", Moves("笑い", 100, "にやり", 100))));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "2") }), "Two agreeing parts beat an unlisted whole-face name.");
        }

        // The descriptor's 15 viseme shapes with aa, E, I, O, U in slots 10-14.
        static string[] Visemes(string aa, string e, string i, string o, string u) =>
            new[] { "", "", "", "", "", "", "", "", "", "", aa, e, i, o, u };

        static byte[] Mouth(byte[] bytes, string renderer, string[] visemes, List<string> warnings = null) =>
            VrmExpressionBindings.AddMissing(bytes, warnings, inferBlink: false, visemeRenderer: renderer, visemeShapes: visemes);

        [Test]
        public void DescriptorVisemesFillMouthWhenShapeNamesMatchNoKnownName()
        {
            var bytes = Glb(null, null, Arr(Obj("name", "Body", "mesh", 0L), Obj("name", "Face", "mesh", 1L)),
                Arr(Mesh("body"), Mesh("Mouth.A1", "Mouth.I1", "Mouth.U1", "Mouth.E1", "Mouth.O1")));
            Assert.That(Presets(VrmExpressionBindings.AddMissing(bytes, inferBlink: false)), Is.Empty,
                "These names match no known mouth name, so name inference alone leaves the lip-sync slots empty.");

            var warnings = new List<string>();
            var presets = Presets(Mouth(bytes, "Face", Visemes("Mouth.A1", "Mouth.E1", "Mouth.I1", "Mouth.O1", "Mouth.U1"), warnings));
            foreach (var (preset, index) in new[] { ("aa", 0L), ("ih", 1L), ("ou", 2L), ("ee", 3L), ("oh", 4L) })
                Assert.That(Canonical(presets[preset]), Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(1, index)), "isBinary", false))), preset);
            Assert.That(warnings, Does.Contain(string.Format(VrmExpressionBindings.VisemeFilledFormat, "aa")));
        }

        [Test]
        public void AuthoredMouthPresetsWinOverDescriptorVisemes()
        {
            var authored = Obj("morphTargetBinds", Arr(Bind(0, 1)), "isBinary", false);
            var bytes = Glb(Obj("aa", Obj("morphTargetBinds", Arr(), "isBinary", false), "ee", authored), null,
                Arr(Obj("name", "Face", "mesh", 0L)), Arr(Mesh("v_a", "v_e", "v_i")));
            var presets = Presets(Mouth(bytes, "Face", Visemes("v_a", "v_e", "v_i", "", "")));
            Assert.That(Canonical(presets["aa"]), Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(), "isBinary", false))),
                "An intentionally empty preset stays empty.");
            Assert.That(Canonical(presets["ee"]), Is.EqualTo(Canonical(authored)));
            Assert.That(Canonical(presets["ih"]), Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(0, 2)), "isBinary", false))));
            Assert.That(presets.ContainsKey("oh") || presets.ContainsKey("ou"), Is.False, "Empty viseme slots fill nothing.");
        }

        [Test]
        public void UnusableDescriptorVisemeFallsBackToKnownNames()
        {
            var bytes = Glb(null, null, Arr(Obj("name", "Face", "mesh", 0L)), Arr(Mesh("vrc.v_aa", "vrc.v_ih", "dup", "dup")));
            // aa names a shape the mesh lacks, ih is empty, oh appears twice on the mesh.
            var presets = Presets(Mouth(bytes, "Face", Visemes("missing", "", "", "dup", "")));
            Assert.That(Canonical(presets["aa"]), Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(0, 0)), "isBinary", false))));
            Assert.That(Canonical(presets["ih"]), Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(0, 1)), "isBinary", false))));
            Assert.That(presets.ContainsKey("oh"), Is.False);
        }

        [Test]
        public void DescriptorVisemeBindsTheLipSyncMeshOrTheOneMeshThatHasTheShape()
        {
            var nodes = Arr(Obj("name", "Body", "mesh", 0L), Obj("name", "Face", "mesh", 1L));
            var bytes = Glb(null, null, nodes, Arr(Mesh("x", "Mouth.A1"), Mesh("Mouth.A1")));
            Assert.That(Canonical(Presets(Mouth(bytes, "Face", Visemes("Mouth.A1", "", "", "", "")))["aa"]),
                Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(1, 0)), "isBinary", false))), "Only the Lip Sync mesh is bound.");
            Assert.That(Presets(Mouth(bytes, "Merged", Visemes("Mouth.A1", "", "", "", ""))).ContainsKey("aa"), Is.False,
                "With the Lip Sync mesh gone and two meshes having the shape, the known names decide.");

            var merged = Glb(null, null, Arr(Obj("name", "Merged", "mesh", 0L)), Arr(Mesh("x", "Mouth.A1")));
            Assert.That(Canonical(Presets(Mouth(merged, "Face", Visemes("Mouth.A1", "", "", "", "")))["aa"]),
                Is.EqualTo(Canonical(Obj("morphTargetBinds", Arr(Bind(0, 1)), "isBinary", false))),
                "When an optimizer merged the Lip Sync mesh, the one mesh with the shape is bound.");
        }
    }
}
