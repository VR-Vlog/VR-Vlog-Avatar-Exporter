using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UniGLTF;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Session = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionPresetTests
    {
        static readonly Vector3 HairDelta = new Vector3(.02f, .01f, 0);
        static readonly Vector3 SmileDelta = new Vector3(0, .03f, 0);

        static AttachmentConnectionTests.Fixture Fixture(bool smile = true)
        {
            var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            if (smile) fixture.Mesh.AddBlendShapeFrame("Smile", 100, Enumerable.Repeat(SmileDelta, 3).ToArray(), new Vector3[3], new Vector3[3]);
            return fixture;
        }

        static readonly Vector3 OpenDelta = new Vector3(0, -.04f, 0);
        static readonly Vector3 RaiseDelta = new Vector3(0, .05f, 0);
        static readonly Vector3 BlinkDelta = new Vector3(0, -.01f, .01f);
        static readonly Vector3 LeftDelta = new Vector3(0, -.02f, .01f);
        static readonly Vector3 RightDelta = new Vector3(0, -.03f, .01f);
        static readonly Vector3 ShutDelta = new Vector3(.01f, -.05f, 0);

        // Front gets a mesh of its own with these BlendShapes; Back keeps the
        // shared one. The caller destroys the returned mesh.
        static Mesh FrontMesh(AttachmentConnectionTests.Fixture fixture, params (string Name, Vector3[] Deltas)[] shapes)
        {
            var mesh = Object.Instantiate(fixture.Mesh); mesh.name = "Face";
            foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape.Name, 100, shape.Deltas, new Vector3[3], new Vector3[3]);
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            front.sharedMesh = mesh; front.SetBlendShapeWeight(0, 35);
            return mesh;
        }

        // A BlendShape moving the given vertices (all when none are given).
        static (string Name, Vector3[] Deltas) Shape(string name, Vector3 delta, params int[] vertices) =>
            (name, Enumerable.Range(0, 3).Select(index => vertices.Length == 0 || vertices.Contains(index) ? delta : Vector3.zero).ToArray());

        static Type DescriptorType() => AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(type => type != null);

        static AnimationClip FaceClip(float weight)
        {
            var clip = new AnimationClip { name = "Sample face" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, weight));
            return clip;
        }

        static Dictionary<string, object> Expressions(byte[] bytes)
        {
            var extensions = (Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
            var vrm = (Dictionary<string, object>)extensions["VRMC_vrm"];
            return vrm.TryGetValue("expressions", out var value) ? (Dictionary<string, object>)value : new Dictionary<string, object>();
        }

        static Dictionary<string, object> Presets(byte[] bytes) =>
            Expressions(bytes).TryGetValue("preset", out var value) ? (Dictionary<string, object>)value : new Dictionary<string, object>();

        // "node/shape@weight" for each morph a preset binds, independent of how
        // the export route numbered the morph targets.
        static string[] BoundShapes(byte[] bytes, string preset)
        {
            var json = GlbDocument.Read(bytes).Json;
            var nodes = (List<object>)json["nodes"]; var meshes = (List<object>)json["meshes"];
            var binds = (List<object>)((Dictionary<string, object>)Presets(bytes)[preset])["morphTargetBinds"];
            return binds.Cast<Dictionary<string, object>>().Select(bind =>
            {
                var node = (Dictionary<string, object>)nodes[Convert.ToInt32(bind["node"], CultureInfo.InvariantCulture)];
                var mesh = (Dictionary<string, object>)meshes[Convert.ToInt32(node["mesh"], CultureInfo.InvariantCulture)];
                var names = (List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"];
                return node["name"] + "/" + names[Convert.ToInt32(bind["index"], CultureInfo.InvariantCulture)] + "@" + Convert.ToString(bind["weight"], CultureInfo.InvariantCulture);
            }).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

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

        // The sum of every exported morph on the first vertex, as the
        // existing capture tests measure an imported face.
        static Vector3 FirstVertex(SkinnedMeshRenderer skin) => Vertex(skin, 0);

        static Vector3 Vertex(SkinnedMeshRenderer skin, int vertex)
        {
            var delta = new Vector3[skin.sharedMesh.vertexCount]; var value = skin.sharedMesh.vertices[vertex];
            for (var shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++)
            {
                skin.sharedMesh.GetBlendShapeFrameVertices(shape, skin.sharedMesh.GetBlendShapeFrameCount(shape) - 1, delta, null, null);
                value += delta[vertex] * skin.GetBlendShapeWeight(shape) / 100;
            }
            return value;
        }

        static async Task<Vrm10Instance> Load(byte[] bytes) => await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());

        static Session.PresetAssignment[] NoneForEveryEmotion() => Session.EmotionPresets.Select(Session.PresetAssignment.None).ToArray();

        [TestCase(false)]
        [TestCase(true)]
        public void WithoutAssignmentsTheExportedExpressionsAreUnchanged(bool recordFace)
        {
            using var fixture = Fixture(smile: false);
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            if (recordFace) { session.SetWeight(0, 0, 90); session.Capture("Sample"); }
            var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
            var before = session.Export("Test", "Tests", blinkOptions: blink);
            var explicitNone = session.Export("Test", "Tests", blinkOptions: blink, presets: NoneForEveryEmotion());
            Assert.That(Session.EmotionPresets.Any(preset => Presets(before).ContainsKey(preset.ToString())), Is.False);
            Assert.That(Canonical(Expressions(explicitNone)), Is.EqualTo(Canonical(Expressions(before))),
                "Choosing nothing must keep the expression data of the existing export.");
        }

        [Test]
        public async Task OrdinaryRouteAddsAssignedClipAndBlendShapeAsPresets()
        {
            using var fixture = Fixture();
            var clip = FaceClip(80);
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                session.SetPreset(ExpressionPreset.happy, Session.PresetAssignment.ForClip(ExpressionPreset.happy, clip));
                session.SetPreset(ExpressionPreset.sad, Session.PresetAssignment.ForShape(ExpressionPreset.sad, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.angry, Session.PresetAssignment.None(ExpressionPreset.angry));
                var bytes = session.Export("Test", "Tests", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(Presets(bytes).Keys.Where(key => Session.EmotionPresets.Any(preset => preset.ToString() == key)), Is.EquivalentTo(new[] { "happy", "sad" }));
                imported = await Load(bytes);
                Assert.That(imported.Vrm.Expression.Happy, Is.Not.Null);
                // UniVRM creates empty clips for absent presets on import.
                Assert.That(imported.Vrm.Expression.Angry?.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>(), Is.Empty);
                Assert.That(imported.Vrm.Expression.Happy.IsBinary, Is.False);
                Assert.That(imported.Vrm.Expression.Happy.OverrideBlink.ToString(), Is.EqualTo("blend"));
                var front = imported.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var back = imported.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
                var origin = fixture.Mesh.vertices[0];
                imported.Runtime.Expression.SetWeight(ExpressionKey.Happy, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + HairDelta * .8f), Is.LessThan(.0001));
                Assert.That(Vector3.Distance(FirstVertex(back), origin + HairDelta * .35f), Is.LessThan(.0001));
                imported.Runtime.Expression.SetWeight(ExpressionKey.Happy, 0);
                imported.Runtime.Expression.SetWeight(ExpressionKey.Sad, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + HairDelta * .35f), Is.LessThan(.0001));
                Assert.That(Vector3.Distance(FirstVertex(back), origin + HairDelta * .35f + SmileDelta), Is.LessThan(.0001));
                Assert.That(fixture.Source.GetComponent<Vrm10Instance>(), Is.Null, "The source avatar is never edited.");
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public async Task CaptureRouteKeepsRecordedCustomFacesBesideThePreset()
        {
            using var fixture = Fixture();
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                session.SetWeight(0, 0, 90); session.Capture("Sample"); session.RestoreBaseline();
                session.SetPreset(ExpressionPreset.surprised, Session.PresetAssignment.ForShape(ExpressionPreset.surprised, "Front", "Smile"));
                var bytes = session.Export("Test", "Tests", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                imported = await Load(bytes);
                Assert.That(imported.Vrm.Expression.CustomClips.Select(clip => clip.name), Does.Contain("Sample"),
                    "Recorded faces keep their custom names for the app's animation references.");
                Assert.That(imported.Vrm.Expression.Surprised, Is.Not.Null);
                var front = imported.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var origin = fixture.Mesh.vertices[0];
                imported.Runtime.Expression.SetWeight(ExpressionKey.Surprised, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + HairDelta * .35f + SmileDelta), Is.LessThan(.0001));
            }
            finally { if (imported != null) Object.DestroyImmediate(imported.gameObject); }
        }

        // The authored custom clip stands in for the avatar's menu faces, which
        // the ordinary route keeps and which need the VRChat SDK to build here.
        [TestCase(false)]
        [TestCase(true)]
        public async Task ReplacingCustomFacesWithNothingLeavesNoKeptFace(bool withPreset)
        {
            using var fixture = Fixture();
            var authored = ScriptableObject.CreateInstance<VRM10Object>();
            var kept = ScriptableObject.CreateInstance<VRM10Expression>();
            kept.name = "Excluded face"; kept.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .8f) };
            authored.Expression.AddClip(ExpressionPreset.custom, kept);
            Vrm10Instance ordinary = null, replaced = null;
            try
            {
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = authored;
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                if (withPreset) session.SetPreset(ExpressionPreset.happy, Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile"));
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
                ordinary = await Load(session.Export("Test", "Tests", blinkOptions: blink));
                Assert.That(ordinary.Vrm.Expression.CustomClips.Select(clip => clip.name), Does.Contain("Excluded face"), "The shortcut routes keep the avatar's faces.");
                replaced = await Load(session.Export("Test", "Tests", blinkOptions: blink, recordedExpressions: Array.Empty<Session.Pose>(), replaceCustomFaces: true));
                Assert.That(replaced.Vrm.Expression.CustomClips, Is.Empty, "Replacing with an empty list exports no custom face.");
                Assert.That(replaced.Vrm.Expression.Happy?.MorphTargetBindings ?? Array.Empty<MorphTargetBinding>(), withPreset ? Is.Not.Empty : Is.Empty);
            }
            finally
            {
                foreach (var instance in new[] { ordinary, replaced }) if (instance != null) Object.DestroyImmediate(instance.gameObject);
                Object.DestroyImmediate(kept); Object.DestroyImmediate(authored);
            }
        }

        [Test]
        public void DuplicateOrUnassignableAssignmentsAreRejected()
        {
            using var fixture = Fixture();
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            var first = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile");
            var second = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Back", "Smile");
            Assert.Throws<InvalidOperationException>(() => session.Export("Test", "Tests", presets: new[] { first, second }));
            Assert.Throws<InvalidOperationException>(() => session.SetPreset(ExpressionPreset.custom, Session.PresetAssignment.ForShape(ExpressionPreset.custom, "Front", "Smile")));
            Assert.Throws<InvalidOperationException>(() => session.SetPreset(ExpressionPreset.custom, Session.PresetAssignment.None(ExpressionPreset.custom)));
            session.SetPreset(ExpressionPreset.happy, first); session.SetPreset(ExpressionPreset.happy, second);
            Assert.That(session.Presets.Count, Is.EqualTo(1), "One emotion holds one face; choosing again replaces it.");
            Assert.That(session.Presets[ExpressionPreset.happy].Path, Is.EqualTo("Back"));
            Assert.That(session.PresetError(ExpressionPreset.angry, Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Front", "Missing")), Is.Not.Null);
            Assert.That(session.PresetError(ExpressionPreset.angry, Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Nowhere", "Smile")), Is.Not.Null);
        }

        [Test]
        public void SettingsWithoutPresetsStillLoadAndNewChoicesRoundTrip()
        {
            using var fixture = Fixture();
            string json;
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                session.SetWeight(0, 0, 75); session.Capture("Sample");
                session.SetPreset(ExpressionPreset.sad, Session.PresetAssignment.ForShape(ExpressionPreset.sad, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.angry, Session.PresetAssignment.None(ExpressionPreset.angry));
                var automatic = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile"); automatic.Automatic = true;
                session.SetPreset(ExpressionPreset.happy, automatic);
                json = session.SaveSettings();
            }
            // A record written before this feature has no Presets member.
            var legacy = Regex.Replace(json, ",\\s*\"Presets\"\\s*:\\s*\\[[\\s\\S]*\\]\\s*}\\s*$", "}");
            Assert.That(legacy, Does.Not.Contain("Presets"));
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                session.LoadSettings(legacy);
                Assert.That(session.Expressions.Single().Name, Is.EqualTo("Sample"));
                Assert.That(session.Presets, Is.Empty);
            }
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                session.LoadSettings(json);
                Assert.That(session.Presets.Keys, Is.EquivalentTo(new[] { ExpressionPreset.sad, ExpressionPreset.angry }), "Automatic suggestions are not saved.");
                Assert.That(session.Presets[ExpressionPreset.sad].Shape, Is.EqualTo("Smile"));
                Assert.That(session.Presets[ExpressionPreset.angry].IsNone, Is.True);
            }
        }

        [TestCase("duplicate")]
        [TestCase("custom")]
        [TestCase("both")]
        public void InvalidPresetSettingsLeaveTheRecordUnchanged(string kind)
        {
            using var fixture = Fixture();
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            session.SetWeight(0, 0, 75); session.Capture("Sample");
            var settings = JsonUtility.FromJson<Session.Settings>(session.SaveSettings());
            var entry = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile");
            switch (kind)
            {
                case "duplicate": settings.Presets = new List<Session.PresetAssignment> { entry, entry.Copy() }; break;
                case "custom": entry.Preset = "custom"; settings.Presets = new List<Session.PresetAssignment> { entry }; break;
                case "both": entry.ClipGuid = "0123456789abcdef0123456789abcdef"; settings.Presets = new List<Session.PresetAssignment> { entry }; break;
            }
            Assert.Throws<InvalidOperationException>(() => session.LoadSettings(JsonUtility.ToJson(settings)));
            Assert.That(session.Presets, Is.Empty);
            Assert.That(session.Expressions.Single().Name, Is.EqualTo("Sample"));
        }

        [TestCase("Smile", ExpressionPreset.happy, 0)]
        [TestCase("FaceSmile01", ExpressionPreset.happy, 0)]
        [TestCase("joy", ExpressionPreset.happy, 1)]
        [TestCase("笑顔", ExpressionPreset.happy, 0)]
        [TestCase("ちょっと笑い", ExpressionPreset.happy, 1)]
        [TestCase("怒り", ExpressionPreset.angry, 0)]
        [TestCase("Sorrow", ExpressionPreset.sad, 0)]
        [TestCase("cry_face", ExpressionPreset.sad, 1)]
        [TestCase("Fun", ExpressionPreset.relaxed, 0)]
        [TestCase("びっくり", ExpressionPreset.surprised, 0)]
        [TestCase("Smile Tears", ExpressionPreset.happy, 0)]
        public void NamesSuggestOneEmotion(string name, ExpressionPreset expected, int tier) =>
            Assert.That(ExpressionNameInference.MatchEmotion(name), Is.EqualTo(((ExpressionPreset, int)?)(expected, tier)));

        [TestCase("eye_smile_L")]
        [TestCase("Smile.R")]
        [TestCase("mouth_smile")]
        [TestCase("smile_angry")]
        [TestCase("泣き笑い")]
        [TestCase("笑い目")]
        [TestCase("Blink")]
        [TestCase("Hair detail")]
        [TestCase("function")]
        [TestCase("made")]
        [TestCase("smiles")]
        [TestCase("Smile Off")]
        [TestCase("")]
        public void PartialAmbiguousOrUnrelatedNamesSuggestNothing(string name) =>
            Assert.That(Session.SuggestPreset(name), Is.Null);

        // Every listed word, alone and in a typical clip name, names its own emotion and tier.
        [Test]
        public void EveryVocabularyWordSuggestsItsEmotionAndTier()
        {
            var checkedWords = 0;
            foreach (var entry in ExpressionNameInference.Emotions)
                foreach (var word in entry.Words)
                {
                    var latin = word.All(character => character < 128);
                    var names = latin
                        ? new[] { word, word.ToUpperInvariant(), char.ToUpperInvariant(word[0]) + word.Substring(1), "Face_" + word + "_01", "face" + char.ToUpperInvariant(word[0]) + word.Substring(1) }
                        : new[] { word, "表情_" + word, word + "02" };
                    foreach (var name in names)
                        Assert.That(ExpressionNameInference.MatchEmotion(name), Is.EqualTo(((ExpressionPreset, int)?)(entry.Preset, entry.Tier)), name);
                    if (latin) Assert.That(Session.SuggestPreset(word + "x"), Is.Not.EqualTo(entry.Preset).Or.Null, "Latin words match whole words only: " + word);
                    checkedWords++;
                }
            Assert.That(checkedWords, Is.GreaterThan(50));
            var words = ExpressionNameInference.Emotions.SelectMany(entry => entry.Words).ToArray();
            Assert.That(words.Distinct().Count(), Is.EqualTo(words.Length), "A word belongs to one emotion and tier.");
            foreach (var veto in ExpressionNameInference.VetoWords.Concat(ExpressionNameInference.VetoParts))
                Assert.That(Session.SuggestPreset("Smile " + veto), Is.Null, veto);
        }

        [Test]
        public void SuggestionsAreMarkedAutomaticAndSkipFacesThatDoNotChange()
        {
            using var fixture = Fixture();
            var neutral = FaceClip(35); neutral.name = "Angry neutral";
            var happy = FaceClip(80); happy.name = "Happy face";
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { neutral, happy });
                Assert.That(suggested.Keys, Is.EquivalentTo(new[] { ExpressionPreset.happy }),
                    "A clip equal to the base face is not offered; the BlendShape 'Smile' loses to the named clip.");
                Assert.That(suggested[ExpressionPreset.happy].Clip, Is.SameAs(happy));
                Assert.That(suggested[ExpressionPreset.happy].Automatic, Is.True);
                Assert.That(session.Presets, Is.Empty, "Suggestions are not applied by the session itself.");
                // A strong word on a BlendShape wins over a loose word on a clip.
                var loose = FaceClip(80); loose.name = "Joy face";
                try { Assert.That(session.SuggestPresets(new[] { loose })[ExpressionPreset.happy].Shape, Is.EqualTo("Smile")); }
                finally { Object.DestroyImmediate(loose); }
            }
            finally { Object.DestroyImmediate(neutral); Object.DestroyImmediate(happy); }
        }

        [Test]
        public void AuthoredVrmPresetsAreNotReplacedBySuggestions()
        {
            using var fixture = Fixture();
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var authored = ScriptableObject.CreateInstance<VRM10Expression>();
            var happy = FaceClip(80); happy.name = "Happy face";
            try
            {
                authored.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .5f) };
                settings.Expression.Happy = authored;
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.happy), Is.True);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.sad), Is.False);
                Assert.That(session.SuggestPresets(new[] { happy }).ContainsKey(ExpressionPreset.happy), Is.False,
                    "The avatar's own happy face is kept; a name match never replaces it.");
                Assert.That(Session.PresetDisplayName(null, authored: true), Is.Not.EqualTo(Session.PresetDisplayName(null)));
            }
            finally { Object.DestroyImmediate(happy); Object.DestroyImmediate(authored); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void JapaneseBlendShapeNamesAreOfferedOnlyByHand()
        {
            using var fixture = Fixture();
            fixture.Mesh.AddBlendShapeFrame("怒り", 100, Enumerable.Repeat(new Vector3(.01f, 0, 0), 3).ToArray(), new Vector3[3], new Vector3[3]);
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            var suggested = session.SuggestPresets(Array.Empty<AnimationClip>());
            Assert.That(suggested.Keys, Is.EquivalentTo(new[] { ExpressionPreset.happy }), "'Smile' is suggested; the MMD-style '怒り' is not.");
            Assert.That(session.PresetShapeOptions().Any(option => option.Shape == "怒り"), Is.True, "It can still be chosen by hand.");
            Assert.That(session.PresetError(ExpressionPreset.angry, Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Front", "怒り")), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion("怒り", latinOnly: true), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion("Fcl_BRW_Angry", latinOnly: true), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion("Fcl_ALL_Angry", latinOnly: true), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.angry, 0)));
        }

        [Test]
        public void EveryStandardPresetHasOneGroupAndLabel()
        {
            Assert.That(Session.AssignablePresets, Is.EqualTo(new[] {
                ExpressionPreset.happy, ExpressionPreset.angry, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised,
                ExpressionPreset.blink, ExpressionPreset.blinkLeft, ExpressionPreset.blinkRight,
                ExpressionPreset.aa, ExpressionPreset.ih, ExpressionPreset.ou, ExpressionPreset.ee, ExpressionPreset.oh,
                ExpressionPreset.lookUp, ExpressionPreset.lookDown, ExpressionPreset.lookLeft, ExpressionPreset.lookRight, ExpressionPreset.neutral }));
            Assert.That(Session.PresetGroups.SelectMany(group => group.Presets), Is.EqualTo(Session.AssignablePresets));
            Assert.That(Session.EmotionPresets, Is.EqualTo(Session.PresetGroups[0].Presets));
            Assert.That(Session.AssignablePresets.Select(Session.PresetFullLabel).Distinct().Count(), Is.EqualTo(18), "Labels with their group never collide.");
            Assert.That(Session.PresetFullLabel(ExpressionPreset.blinkLeft), Is.EqualTo(ExporterLocalization.T("まばたき") + " " + ExporterLocalization.T("左目")));
            Assert.That(Session.PresetFullLabel(ExpressionPreset.happy), Is.EqualTo(ExporterLocalization.T(Session.PresetLabel(ExpressionPreset.happy))));
            Assert.That(Session.PresetFullLabel(ExpressionPreset.neutral), Is.EqualTo(ExporterLocalization.T("基準の顔")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MouthGazeAndNeutralAssignmentsExportWithoutOverrides(bool recordFace)
        {
            using var fixture = Fixture();
            var mesh = FrontMesh(fixture, Shape("Open", OpenDelta), Shape("Raise", RaiseDelta));
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                if (recordFace) { session.SetWeight(0, 0, 90); session.Capture("Sample"); session.RestoreBaseline(); }
                session.SetPreset(ExpressionPreset.aa, Session.PresetAssignment.ForShape(ExpressionPreset.aa, "Front", "Open"));
                session.SetPreset(ExpressionPreset.lookUp, Session.PresetAssignment.ForShape(ExpressionPreset.lookUp, "Front", "Raise"));
                session.SetPreset(ExpressionPreset.neutral, Session.PresetAssignment.ForShape(ExpressionPreset.neutral, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.happy, Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Back", "Smile"));
                var bytes = session.Export("Test", "Tests", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(Presets(bytes).Keys, Is.SupersetOf(new[] { "aa", "lookUp", "neutral", "happy" }));
                imported = await Load(bytes);
                var expression = imported.Vrm.Expression;
                foreach (var clip in new[] { expression.Aa, expression.LookUp, expression.Neutral })
                {
                    Assert.That(clip.MorphTargetBindings, Is.Not.Empty, clip.name);
                    Assert.That(clip.IsBinary, Is.False, clip.name);
                    Assert.That(new[] { clip.OverrideBlink, clip.OverrideMouth, clip.OverrideLookAt }.Select(value => value.ToString()), Is.All.EqualTo("none"),
                        "Mouth, gaze and neutral are channels themselves and suppress nothing: " + clip.name);
                }
                Assert.That(expression.Happy.OverrideMouth.ToString(), Is.EqualTo("blend"), "Emotions keep their blend overrides.");
                var front = imported.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var back = imported.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
                var origin = fixture.Mesh.vertices[0];
                imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + HairDelta * .35f + OpenDelta), Is.LessThan(.0001));
                Assert.That(Vector3.Distance(FirstVertex(back), origin + HairDelta * .35f), Is.LessThan(.0001));
                imported.Runtime.Expression.SetWeight(ExpressionKey.Aa, 0);
                imported.Runtime.Expression.SetWeight(ExpressionKey.Neutral, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + HairDelta * .35f), Is.LessThan(.0001));
                Assert.That(Vector3.Distance(FirstVertex(back), origin + HairDelta * .35f + SmileDelta), Is.LessThan(.0001));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task AssignedBlinkReplacesTheAutomaticBlink(bool recordFace)
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Shut", ShutDelta));
            Vrm10Instance assigned = null, automatic = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                if (recordFace) { session.SetWeight(0, 0, 90); session.Capture("Sample"); session.RestoreBaseline(); }
                var origin = fixture.Mesh.vertices[0] + HairDelta * .35f;
                automatic = await Load(session.Export("Test", "Tests"));
                var front = automatic.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                automatic.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); automatic.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + BlinkDelta), Is.LessThan(.0001), "Without an assignment the automatic blink picks 'Blink'.");

                session.SetPreset(ExpressionPreset.blink, Session.PresetAssignment.ForShape(ExpressionPreset.blink, "Front", "Shut"));
                assigned = await Load(session.Export("Test", "Tests"));
                front = assigned.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                assigned.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); assigned.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + ShutDelta), Is.LessThan(.0001), "The assignment wins over the automatic blink.");
                assigned.Runtime.Expression.SetWeight(ExpressionKey.Blink, 0);
                foreach (var key in new[] { ExpressionKey.BlinkLeft, ExpressionKey.BlinkRight })
                {
                    assigned.Runtime.Expression.SetWeight(key, 1); assigned.Runtime.Process();
                    Assert.That(Vector3.Distance(FirstVertex(front), origin), Is.LessThan(.0001), "Unassigned eyes are not written: " + key);
                    assigned.Runtime.Expression.SetWeight(key, 0);
                }
            }
            finally
            {
                foreach (var instance in new[] { assigned, automatic }) if (instance != null) Object.DestroyImmediate(instance.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public async Task AssignedBlinkWinsOverTheVrChatEyelidSetting()
        {
            var descriptorType = DescriptorType();
            if (descriptorType == null) Assert.Ignore("VRChat SDK is required for the descriptor integration test.");
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Lid", BlinkDelta), Shape("Shut", ShutDelta));
            Vrm10Instance imported = null;
            try
            {
                var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var descriptor = fixture.Source.AddComponent(descriptorType);
                descriptorType.GetField("enableEyeLook").SetValue(descriptor, true);
                var settingsField = descriptorType.GetField("customEyeLookSettings"); var settings = settingsField.GetValue(descriptor);
                var settingsType = settings.GetType(); var lidType = settingsType.GetField("eyelidType");
                lidType.SetValue(settings, Enum.Parse(lidType.FieldType, "Blendshapes"));
                settingsType.GetField("eyelidsSkinnedMesh").SetValue(settings, front);
                settingsType.GetField("eyelidsBlendshapes").SetValue(settings, new[] { mesh.GetBlendShapeIndex("Lid"), -1, -1 });
                settingsField.SetValue(descriptor, settings);
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.SuggestPresets(Array.Empty<AnimationClip>())[ExpressionPreset.blink].Shape, Is.EqualTo("Lid"),
                    "The candidate is the eyelid setting the automatic blink uses.");
                Assert.That(session.BlinkFallbackDescription(null), Is.EqualTo(ExporterLocalization.T("VRChatの閉眼設定")));
                session.SetPreset(ExpressionPreset.blink, Session.PresetAssignment.ForShape(ExpressionPreset.blink, "Front", "Shut"));
                imported = await Load(session.Export("Test", "Tests"));
                var exported = imported.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                imported.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(exported), fixture.Mesh.vertices[0] + HairDelta * .35f + ShutDelta), Is.LessThan(.0001));
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BlinkCandidatesRepeatTheAutomaticBlinkSetting()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1));
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>());
                Assert.That(suggested.Keys, Is.EquivalentTo(Session.BlinkPresets));
                Assert.That(Session.BlinkPresets.Select(preset => suggested[preset].Shape), Is.EqualTo(new[] { "Blink", "Blink_L", "Blink_R" }));
                Assert.That(suggested.Values.All(value => value.Automatic && value.Path == "Front"), Is.True);
                Assert.That(session.BlinkFallbackDescription(null), Is.EqualTo(ExporterLocalization.T("自動設定")));

                var none = new BlinkExportOptions { Mode = BlinkExportMode.None };
                Assert.That(session.SuggestPresets(Array.Empty<AnimationClip>(), none), Is.Empty, "No blink, no candidates.");
                Assert.That(session.BlinkFallbackDescription(none), Is.EqualTo(ExporterLocalization.T("瞬きなし")));
                var manual = new BlinkExportOptions { Mode = BlinkExportMode.Manual };
                manual.Both.Add(new BlinkShapeBinding { Renderer = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>(), Shape = "Blink_L" });
                Assert.That(session.SuggestPresets(Array.Empty<AnimationClip>(), manual), Is.Empty, "A manual blink setting is not a candidate.");
                Assert.That(session.BlinkFallbackDescription(manual), Is.EqualTo(ExporterLocalization.T("手動設定")));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task ConfirmingOneBlinkPresetKeepsTheOtherCandidates(bool recordFace)
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1), Shape("Shut_L", ShutDelta, 0));
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                if (recordFace) { session.SetWeight(0, 0, 90); session.Capture("Sample"); session.RestoreBaseline(); }
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>());
                Assert.That(suggested.Keys, Is.EquivalentTo(Session.BlinkPresets));
                var before = session.Export("Test", "Tests");
                var unconfirmed = session.Export("Test", "Tests", presets: suggested.Values);
                Assert.That(Canonical(Expressions(unconfirmed)), Is.EqualTo(Canonical(Expressions(before))),
                    "Automatic blink candidates export exactly what the automatic blink exports.");

                var changed = suggested.Values.Select(value => value.Preset == "blinkLeft"
                    ? Session.PresetAssignment.ForShape(ExpressionPreset.blinkLeft, "Front", "Shut_L") : value).ToArray();
                imported = await Load(session.Export("Test", "Tests", presets: changed));
                var front = imported.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var expression = imported.Runtime.Expression;
                var baseFace = new[] { 0, 1 }.Select(vertex => fixture.Mesh.vertices[vertex] + HairDelta * .35f).ToArray();
                expression.SetWeight(ExpressionKey.BlinkLeft, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(Vertex(front, 0), baseFace[0] + ShutDelta), Is.LessThan(.0001), "The replaced eye uses the choice.");
                expression.SetWeight(ExpressionKey.BlinkLeft, 0);
                expression.SetWeight(ExpressionKey.BlinkRight, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(Vertex(front, 1), baseFace[1] + RightDelta), Is.LessThan(.0001), "The other eye keeps its candidate.");
                Assert.That(Vector3.Distance(Vertex(front, 0), baseFace[0]), Is.LessThan(.0001));
                expression.SetWeight(ExpressionKey.BlinkRight, 0);
                expression.SetWeight(ExpressionKey.Blink, 1); imported.Runtime.Process();
                Assert.That(Vector3.Distance(Vertex(front, 0), baseFace[0] + BlinkDelta), Is.LessThan(.0001), "Both eyes keep their candidate.");
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void MouthCandidatesFollowLipSyncThenKnownNames()
        {
            using var fixture = Fixture(smile: false);
            // On both meshes: the ordinary export binds it on each, so no single candidate.
            fixture.Mesh.AddBlendShapeFrame("vrc.v_ih", 100, Enumerable.Repeat(RaiseDelta, 3).ToArray(), new Vector3[3], new Vector3[3]);
            var mesh = FrontMesh(fixture, Shape("Open", OpenDelta), Shape("Wide", RaiseDelta), Shape("Narrow", BlinkDelta), Shape("Round", LeftDelta),
                Shape("vrc.v_aa", RightDelta), Shape("vrc.v_ou", ShutDelta), Shape("Fcl_MTH_O", OpenDelta, 0));
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var byName = session.SuggestMouthPresets(null, null);
                Assert.That(byName.Keys, Is.EquivalentTo(new[] { ExpressionPreset.aa, ExpressionPreset.ou, ExpressionPreset.oh }));
                Assert.That(byName[ExpressionPreset.aa].Shape, Is.EqualTo("vrc.v_aa"));
                Assert.That(byName[ExpressionPreset.oh].Shape, Is.EqualTo("Fcl_MTH_O"));
                Assert.That(byName.Values.All(value => value.Automatic && value.Path == "Front"), Is.True);
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>(), new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(suggested.Keys, Is.EquivalentTo(byName.Keys), "Without a VRChat Lip Sync setting the known names are the candidates.");

                var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var visemes = new string[15]; visemes[10] = "Open"; visemes[11] = "Wide"; visemes[12] = "Narrow"; visemes[13] = "Round"; visemes[14] = "Missing";
                var lipSync = session.SuggestMouthPresets(front, visemes);
                Assert.That(lipSync[ExpressionPreset.aa].Shape, Is.EqualTo("Open"));
                Assert.That(lipSync[ExpressionPreset.ee].Shape, Is.EqualTo("Wide"));
                Assert.That(lipSync[ExpressionPreset.ih].Shape, Is.EqualTo("Narrow"));
                Assert.That(lipSync[ExpressionPreset.oh].Shape, Is.EqualTo("Round"));
                Assert.That(lipSync[ExpressionPreset.ou].Shape, Is.EqualTo("vrc.v_ou"), "A Lip Sync shape that is not exported falls back to the known names.");
                var outside = fixture.Copy.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                Assert.That(session.SuggestMouthPresets(outside, visemes).Keys, Is.EquivalentTo(byName.Keys), "A renderer of another object is ignored.");

                // Unconfirmed candidates found by name are left to the ordinary
                // export, which binds the same shapes by itself.
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
                var before = session.Export("Test", "Tests", blinkOptions: blink);
                var unconfirmed = session.Export("Test", "Tests", blinkOptions: blink, presets: suggested.Values);
                Assert.That(Canonical(Expressions(unconfirmed)), Is.EqualTo(Canonical(Expressions(before))));
                Assert.That(BoundShapes(unconfirmed, "aa"), Is.EqualTo(new[] { "Front/vrc.v_aa@1" }));
                Assert.That(BoundShapes(unconfirmed, "ih"), Is.EqualTo(new[] { "Back/vrc.v_ih@1", "Front/vrc.v_ih@1" }));
                Assert.That(Presets(unconfirmed).ContainsKey("ee"), Is.False);
                // Lip Sync candidates are not known names and are written as assignments.
                var withLipSync = session.Export("Test", "Tests", blinkOptions: blink, presets: lipSync.Values);
                Assert.That(Presets(withLipSync).ContainsKey("ee"), Is.True);
                Assert.That(BoundShapes(withLipSync, "ih"), Has.Length.EqualTo(1), "The Lip Sync shape replaces the name binding on both meshes.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void VrChatLipSyncShapesAreTheMouthCandidates()
        {
            var descriptorType = DescriptorType();
            if (descriptorType == null) Assert.Ignore("VRChat SDK is required for the descriptor integration test.");
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Open", OpenDelta), Shape("Wide", RaiseDelta), Shape("vrc.v_aa", RightDelta));
            try
            {
                var descriptor = fixture.Source.AddComponent(descriptorType);
                var lipSync = descriptorType.GetField("lipSync");
                lipSync.SetValue(descriptor, Enum.Parse(lipSync.FieldType, "VisemeBlendShape"));
                descriptorType.GetField("VisemeSkinnedMesh").SetValue(descriptor, fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>());
                var visemes = new string[15]; visemes[10] = "Open"; visemes[11] = "Wide";
                descriptorType.GetField("VisemeBlendShapes").SetValue(descriptor, visemes);
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>(), new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(suggested[ExpressionPreset.aa].Shape, Is.EqualTo("Open"));
                Assert.That(suggested[ExpressionPreset.ee].Shape, Is.EqualTo("Wide"));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void MouthBlinkAndNeutralChoicesAreSavedLikeEmotions()
        {
            using var fixture = Fixture();
            string json;
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                session.SetWeight(0, 0, 75); session.Capture("Sample");
                session.SetPreset(ExpressionPreset.aa, Session.PresetAssignment.ForShape(ExpressionPreset.aa, "Front", "Smile"));
                session.SetPreset(ExpressionPreset.blinkLeft, Session.PresetAssignment.ForShape(ExpressionPreset.blinkLeft, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.neutral, Session.PresetAssignment.None(ExpressionPreset.neutral));
                session.SetPreset(ExpressionPreset.sad, Session.PresetAssignment.ForShape(ExpressionPreset.sad, "Back", "Smile"));
                json = session.SaveSettings();
            }
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                session.LoadSettings(json);
                Assert.That(session.Presets.Keys, Is.EquivalentTo(new[] { ExpressionPreset.aa, ExpressionPreset.blinkLeft, ExpressionPreset.neutral, ExpressionPreset.sad }));
                Assert.That(session.Presets[ExpressionPreset.aa].Path, Is.EqualTo("Front"));
                Assert.That(session.Presets[ExpressionPreset.blinkLeft].Shape, Is.EqualTo("Smile"));
                Assert.That(session.Presets[ExpressionPreset.neutral].IsNone, Is.True);
            }

            var path = Path.Combine(Path.GetTempPath(), "vrvlog-presets-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = ExpressionPresetStore.PathOverride;
            ExpressionPresetStore.PathOverride = path;
            try
            {
                ExpressionPresetStore.Save("avatar-a", new[] { Session.PresetAssignment.ForShape(ExpressionPreset.aa, "Front", "Smile"),
                    Session.PresetAssignment.ForShape(ExpressionPreset.blink, "Back", "Smile"), Session.PresetAssignment.None(ExpressionPreset.lookUp) });
                Assert.That(ExpressionPresetStore.Load("avatar-a", out var skipped).Select(value => value.Preset), Is.EquivalentTo(new[] { "aa", "blink", "lookUp" }));
                Assert.That(skipped, Is.Zero);
                // A file written when only emotions could be chosen.
                const string older = "{\"Version\":1,\"Avatars\":[{\"Avatar\":\"avatar-b\",\"Presets\":[{\"Preset\":\"happy\",\"ClipGuid\":\"\",\"ClipLocalId\":0,\"Path\":\"Front\",\"Shape\":\"Smile\"},{\"Preset\":\"angry\",\"ClipGuid\":\"\",\"ClipLocalId\":0,\"Path\":\"\",\"Shape\":\"\"}]}]}";
                File.WriteAllText(path, older, Encoding.UTF8);
                Assert.That(ExpressionPresetStore.Load("avatar-b", out skipped).Select(value => value.Preset), Is.EqualTo(new[] { "happy", "angry" }));
                Assert.That(skipped, Is.Zero);
            }
            finally
            {
                ExpressionPresetStore.PathOverride = previous;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void StoreKeepsUserChoicesPerAvatarAndIgnoresDamagedData()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-presets-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = ExpressionPresetStore.PathOverride;
            ExpressionPresetStore.PathOverride = path;
            try
            {
                var automatic = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile"); automatic.Automatic = true;
                ExpressionPresetStore.Save("avatar-a", new[] { Session.PresetAssignment.ForShape(ExpressionPreset.sad, "Back", "Smile"),
                    Session.PresetAssignment.None(ExpressionPreset.angry), automatic });
                ExpressionPresetStore.Save("avatar-b", new[] { Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile") });
                var loaded = ExpressionPresetStore.Load("avatar-a", out var skipped);
                Assert.That(skipped, Is.Zero);
                Assert.That(loaded.Select(value => value.Preset), Is.EquivalentTo(new[] { "sad", "angry" }));
                Assert.That(ExpressionPresetStore.Load("avatar-b", out _).Single().Preset, Is.EqualTo("happy"));
                Assert.That(ExpressionPresetStore.Load("avatar-c", out _), Is.Empty);
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"sad\"", "\"custom\""), Encoding.UTF8);
                Assert.That(ExpressionPresetStore.Load("avatar-a", out skipped).Select(value => value.Preset), Is.EqualTo(new[] { "angry" }));
                Assert.That(skipped, Is.EqualTo(1));
                File.WriteAllText(path, "{ not json", Encoding.UTF8);
                Assert.That(ExpressionPresetStore.Load("avatar-a", out _), Is.Empty);
                const string future = "{\"Version\":2,\"Avatars\":[]}";
                File.WriteAllText(path, future, Encoding.UTF8);
                ExpressionPresetStore.Save("avatar-a", new[] { Session.PresetAssignment.None(ExpressionPreset.angry) });
                Assert.That(File.ReadAllText(path), Is.EqualTo(future), "A file from another format version is never overwritten.");
                using var fixture = Fixture(smile: false);
                if (string.IsNullOrEmpty(fixture.Source.scene.path))
                    Assert.That(ExpressionPresetStore.KeyFor(fixture.Source), Is.Null, "An object in an unsaved scene has no stable key.");
            }
            finally
            {
                ExpressionPresetStore.PathOverride = previous;
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void StoreKeepsExcludedFacesByAssetAndReadsFilesWithoutThem()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-presets-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = ExpressionPresetStore.PathOverride;
            var folder = "Assets/VRVlogExcludedStore_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            ExpressionPresetStore.PathOverride = path;
            try
            {
                var kept = FaceClip(40); kept.name = "Kept"; AssetDatabase.CreateAsset(kept, folder + "/Kept.anim");
                var removed = FaceClip(50); removed.name = "Removed"; AssetDatabase.CreateAsset(removed, folder + "/Removed.anim");
                var memory = FaceClip(60);
                ExpressionPresetStore.Save("avatar-a", Array.Empty<Session.PresetAssignment>(), new[] { kept, removed, memory, kept });
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-a", out var skipped), Is.EqualTo(new[] { kept, removed }), "Only assets are stored, once each.");
                Assert.That(skipped, Is.Zero);
                Assert.That(ExpressionPresetStore.Load("avatar-a", out _), Is.Empty);
                ExpressionPresetStore.Save("avatar-a", new[] { Session.PresetAssignment.None(ExpressionPreset.angry) });
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-a", out _), Is.EqualTo(new[] { kept, removed }), "Saving presets alone keeps the exclusions.");
                Assert.That(ExpressionPresetStore.Load("avatar-a", out _).Single().Preset, Is.EqualTo("angry"));
                AssetDatabase.DeleteAsset(folder + "/Removed.anim");
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-a", out skipped), Is.EqualTo(new[] { kept }));
                Assert.That(skipped, Is.EqualTo(1));
                ExpressionPresetStore.Save("avatar-a", Array.Empty<Session.PresetAssignment>(), Array.Empty<AnimationClip>());
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-a", out _), Is.Empty);
                Assert.That(ExpressionPresetStore.Load("avatar-a", out _), Is.Empty);
                Object.DestroyImmediate(memory);

                // A file written before exclusions existed.
                const string older = "{\"Version\":1,\"Avatars\":[{\"Avatar\":\"avatar-b\",\"Presets\":[{\"Preset\":\"angry\",\"ClipGuid\":\"\",\"ClipLocalId\":0,\"Path\":\"\",\"Shape\":\"\"}]}]}";
                File.WriteAllText(path, older, Encoding.UTF8);
                Assert.That(ExpressionPresetStore.Load("avatar-b", out skipped).Single().Preset, Is.EqualTo("angry"));
                Assert.That(skipped, Is.Zero);
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-b", out skipped), Is.Empty);
                Assert.That(skipped, Is.Zero);
                ExpressionPresetStore.Save("avatar-b", ExpressionPresetStore.Load("avatar-b", out _), new[] { kept });
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-b", out _), Is.EqualTo(new[] { kept }));
                Assert.That(ExpressionPresetStore.Load("avatar-b", out _).Single().Preset, Is.EqualTo("angry"));
            }
            finally
            {
                ExpressionPresetStore.PathOverride = previous;
                if (File.Exists(path)) File.Delete(path);
                AssetDatabase.DeleteAsset(folder);
            }
        }
    }
}
