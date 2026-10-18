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

        static byte[] Infer(byte[] bytes, List<string> warnings = null, List<(string Preset, string Source)> filled = null, bool inferEmotions = true) =>
            VrmExpressionBindings.AddMissing(bytes, warnings, inferBlink: false, inferEmotions: inferEmotions, filledEmotions: filled);

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
            var bytes = Glb(null, Obj("VRChat / 笑顔", face), Arr(Obj("mesh", 0L)), Arr(Mesh("base", "composite")));
            var output = Infer(bytes, warnings, filled);
            var happy = (Dictionary<string, object>)Presets(output)["happy"];
            Assert.That(Canonical(happy["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 1)))));
            Assert.That(Canonical(happy["materialColorBinds"]), Is.EqualTo(Canonical(face["materialColorBinds"])));
            Assert.That(happy["isBinary"], Is.False);
            Assert.That(happy["overrideBlink"], Is.EqualTo("block"));
            Assert.That(happy["overrideMouth"], Is.EqualTo("block"));
            Assert.That(happy["overrideLookAt"], Is.EqualTo("block"));
            Assert.That(Presets(output).Keys, Is.EquivalentTo(new[] { "happy" }), "One menu face fills one emotion.");
            Assert.That(((Dictionary<string, object>)Expressions(output)["custom"]).ContainsKey("VRChat / 笑顔"), Is.True, "The menu face stays selectable.");
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "笑顔") }));
            Assert.That(warnings, Does.Contain("happy: 名前から自動で設定しました（笑顔）。動作を確認してください。"));

            var parsed = GlbDocument.Read(output).Json;
            var expressions = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)parsed["extensions"])["VRMC_vrm"])["expressions"];
            var custom = (Dictionary<string, object>)((Dictionary<string, object>)expressions["custom"])["VRChat / 笑顔"];
            var preset = (Dictionary<string, object>)((Dictionary<string, object>)expressions["preset"])["happy"];
            Assert.That(preset["morphTargetBinds"], Is.Not.SameAs(custom["morphTargetBinds"]));
            Assert.That(custom["isBinary"], Is.True, "The custom expression itself is unchanged.");
        }

        [Test]
        public void RecordedMenuFacesStripTheirPrefix()
        {
            var filled = new List<(string Preset, string Source)>();
            var bytes = Glb(null, Obj("VRChat / 記録 / Surprised", MenuFace(Bind(0, 0))), Arr(Obj("mesh", 0L)), Arr(Mesh("composite")));
            Infer(bytes, filled: filled);
            Assert.That(filled, Is.EqualTo(new[] { ("surprised", "Surprised") }));
        }

        [Test]
        public void AuthoredEmotionThatMovesSomethingIsKept()
        {
            var authored = Obj("morphTargetBinds", Arr(Bind(0, 0)), "isBinary", false);
            var bytes = Glb(Obj("happy", authored), Obj("VRChat / 笑顔", MenuFace(Bind(0, 1))),
                Arr(Obj("mesh", 0L)), Arr(Mesh("base", "composite", "Fcl_ALL_Joy")));
            var warnings = new List<string>();
            var output = Infer(bytes, warnings);
            Assert.That(output, Is.EqualTo(bytes), "Nothing else is fillable, so the file is byte-identical.");
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void AuthoredEmotionWithoutBindsIsFilled()
        {
            var bytes = Glb(Obj("happy", Obj("morphTargetBinds", Arr(), "isBinary", false)), Obj("VRChat / 笑顔", MenuFace(Bind(0, 1))),
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
            var bytes = Glb(null, Obj("VRChat / Smile", MenuFace(Bind(0, 2))), Arr(Obj("mesh", 0L)),
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

        [Test]
        public void MenuFacesInsideAnEmotionSubmenuAreDecidedByTheirLeaf()
        {
            var filled = new List<(string Preset, string Source)>();
            var custom = Obj("VRChat / 喜怒哀楽 / 笑顔", MenuFace(Bind(0, 0)), "VRChat / 喜怒哀楽 / 怒り", MenuFace(Bind(0, 1)),
                "VRChat / 喜怒哀楽 / 1", MenuFace(Bind(0, 2)), "VRChat / Eye / Sad", MenuFace(Bind(0, 3)), "VRChat / Smile / Wink", MenuFace(Bind(0, 4)));
            var bytes = Glb(null, custom, Arr(Obj("mesh", 0L)), Arr(Mesh("a", "b", "c", "d", "e")));
            var presets = Presets(Infer(bytes, filled: filled));
            Assert.That(filled, Is.EqualTo(new[] { ("happy", "喜怒哀楽 / 笑顔"), ("angry", "喜怒哀楽 / 怒り"), ("sad", "Eye / Sad") }));
            Assert.That(Canonical(((Dictionary<string, object>)presets["sad"])["morphTargetBinds"]), Is.EqualTo(Canonical(Arr(Bind(0, 3)))));
            Assert.That(presets.ContainsKey("relaxed") || presets.ContainsKey("surprised"), Is.False);
        }

        [Test]
        public void FilledEmotionsAreKeptLinesOfTheResultList()
        {
            var report = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordInferredEmotions(new[] { ("happy", "笑顔"), ("angry", "Fcl_ALL_Angry") }, report);
            var results = report.Fallbacks.Results;
            Assert.That(results.Select(result => result.Name), Is.EqualTo(new[] { "happy", "angry" }));
            Assert.That(results.All(result => result.Outcome == ExportOutcome.Kept && result.Stage == null), Is.True);
            Assert.That(results.Select(result => result.Category).Distinct(),
                Is.EqualTo(new[] { ExporterLocalization.T(UniVrmOneClickExporter.InferredEmotionCategory) }));
            Assert.That(results[0].Reason, Is.EqualTo(string.Format(ExporterLocalization.T(VrmExpressionBindings.FilledFormat), "happy", "笑顔")));
            Assert.That(report.Fallbacks.Summary()[ExportOutcome.Kept], Is.EqualTo(2));
            Assert.That(report.Fallbacks.Count, Is.Zero, "Nothing is asked before saving.");
        }

        [Test]
        public void DisabledInferenceFillsNothing()
        {
            var bytes = Glb(null, Obj("VRChat / 笑顔", MenuFace(Bind(0, 1))), Arr(Obj("mesh", 0L)), Arr(Mesh("Fcl_ALL_Angry", "composite")));
            var filled = new List<(string Preset, string Source)>();
            Assert.That(Infer(bytes, filled: filled, inferEmotions: false), Is.EqualTo(bytes));
            Assert.That(VrmExpressionBindings.AddMissing(bytes, inferBlink: false), Is.EqualTo(bytes), "The default leaves emotions alone.");
            Assert.That(filled, Is.Empty);
        }
    }
}
