using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.Animations;
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

        // The board passes its list of faces even when it lists none.
        static readonly (string Name, AnimationClip Clip, float Time)[] NoFaces = Array.Empty<(string, AnimationClip, float)>();

        // A saved face record, as the board keeps one from its list.
        static void Record(Session session, string name, float weight)
        {
            var pose = Session.ClonePose(session.Baseline); pose.Name = name; pose.Rows[0].Weights[0] = weight;
            session.Expressions.Add(pose);
        }

        static Session.PresetAssignment[] NoneForEveryEmotion() => Session.EmotionPresets.Select(Session.PresetAssignment.None).ToArray();

        [Test]
        public void WithoutAssignmentsTheExportedExpressionsAreUnchanged()
        {
            using var fixture = Fixture(smile: false);
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
            var before = session.Export("Test", "Tests", NoFaces, blinkOptions: blink);
            var explicitNone = session.Export("Test", "Tests", NoFaces, blinkOptions: blink, presets: NoneForEveryEmotion());
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
                var bytes = session.Export("Test", "Tests", NoFaces, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
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

        // The board takes a clip that also names a shape the avatar never had.
        // In a standard slot it must save too, binding only the shapes the
        // mesh has. ShapesLostByPreparationStayUnsupportedOnTheBoard covers
        // a shape lost by preparation, which never reaches the slot.
        [Test]
        public void PresetFromAClipWithShapesTheAvatarNeverHadBindsOnlyTheRest()
        {
            using var fixture = Fixture();
            var clip = FaceClip(100);
            var absent = new AnimationClip { name = "Only missing" };
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.MissingShape"), AnimationCurve.Constant(0, 1, 100));
                AnimationUtility.SetEditorCurve(absent, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.MissingShape"), AnimationCurve.Constant(0, 1, 100));
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var assignment = Session.PresetAssignment.ForClip(ExpressionPreset.happy, clip);
                Assert.That(session.PresetError(ExpressionPreset.happy, assignment), Is.Null);
                session.SetPreset(ExpressionPreset.happy, assignment);
                var bytes = session.Export("Test", "Tests", NoFaces, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                // A clip preset is baked into one generated target on the mesh it moves.
                var bound = BoundShapes(bytes, "happy");
                Assert.That(bound, Has.Length.EqualTo(1));
                Assert.That(bound[0], Does.StartWith("Front/__VRVlog_Endpoint_").And.EndWith("@1"));
                Assert.That(session.PresetError(ExpressionPreset.sad, Session.PresetAssignment.ForClip(ExpressionPreset.sad, absent)),
                    Does.Contain(ExporterLocalization.T("参照を解決した結果、有効な顔のBlendShapeがないため、この表情を省略しました。")));
            }
            finally { Object.DestroyImmediate(clip); Object.DestroyImmediate(absent); }
        }

        [Test]
        public void DuplicateOrUnassignableAssignmentsAreRejected()
        {
            using var fixture = Fixture();
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            var first = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Smile");
            var second = Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Back", "Smile");
            Assert.Throws<InvalidOperationException>(() => session.Export("Test", "Tests", NoFaces, presets: new[] { first, second }));
            Assert.Throws<InvalidOperationException>(() => session.SetPreset(ExpressionPreset.custom, Session.PresetAssignment.ForShape(ExpressionPreset.custom, "Front", "Smile")));
            Assert.Throws<InvalidOperationException>(() => session.SetPreset(ExpressionPreset.custom, Session.PresetAssignment.None(ExpressionPreset.custom)));
            session.SetPreset(ExpressionPreset.happy, first); session.SetPreset(ExpressionPreset.happy, second);
            Assert.That(session.Presets.Count, Is.EqualTo(1), "One emotion holds one face; choosing again replaces it.");
            Assert.That(session.Presets[ExpressionPreset.happy].Path, Is.EqualTo("Back"));
            Assert.That(session.PresetError(ExpressionPreset.angry, Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Front", "Missing")), Is.Not.Null);
            Assert.That(session.PresetError(ExpressionPreset.angry, Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Nowhere", "Smile")), Is.Not.Null);
        }

        [Test]
        public void APresetErrorReadsTheSameWhenAskedAgain()
        {
            using var fixture = Fixture();
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            var missing = Session.PresetAssignment.ForShape(ExpressionPreset.angry, "Front", "Missing");
            var first = session.PresetError(ExpressionPreset.angry, missing);
            var label = Session.PresetFullLabel(ExpressionPreset.angry) + ": ";
            Assert.That(first, Does.StartWith(label).And.Not.StartWith(label + label));
            // The second answer comes from the faces already built, with the label once.
            Assert.That(session.PresetError(ExpressionPreset.angry, missing), Is.EqualTo(first));
            Assert.That(Assert.Throws<InvalidOperationException>(() => session.BuildPresetPose(ExpressionPreset.angry, missing)).Message, Is.EqualTo(first));
        }

        [Test]
        public void SettingsWithoutPresetsStillLoadAndNewChoicesRoundTrip()
        {
            using var fixture = Fixture();
            string json;
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                Record(session, "Sample", 75);
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
            Record(session, "Sample", 75);
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
        [TestCase("ちょっと笑い", ExpressionPreset.happy, 1)]
        [TestCase("Sorrow", ExpressionPreset.sad, 0)]
        [TestCase("cry_face", ExpressionPreset.sad, 1)]
        [TestCase("Fun", ExpressionPreset.relaxed, 0)]
        [TestCase("Smile Tears", ExpressionPreset.happy, 0)]
        public void NamesSuggestOneEmotion(string name, ExpressionPreset expected, int tier) =>
            Assert.That(ExpressionNameInference.MatchEmotion(name), Is.EqualTo(((ExpressionPreset, int)?)(expected, tier)));

        [TestCase("eye_smile_L")]
        [TestCase("Smile.R")]
        [TestCase("mouth_smile")]
        [TestCase("smile_angry")]
        [TestCase("泣き笑い")]
        [TestCase("Blink")]
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
                foreach (var word in entry.English.Concat(entry.Japanese))
                {
                    var latin = word.All(character => character < 128);
                    var japanese = entry.Japanese.Contains(word);
                    var names = latin
                        ? new[] { word, word.ToUpperInvariant(), char.ToUpperInvariant(word[0]) + word.Substring(1), "Face_" + word + "_01", "face" + char.ToUpperInvariant(word[0]) + word.Substring(1) }
                        : new[] { word, "表情_" + word, word + "02" };
                    foreach (var name in names)
                        Assert.That(ExpressionNameInference.MatchEmotion(name), Is.EqualTo(((ExpressionPreset, int)?)(entry.Preset, entry.Tier)), name);
                    if (latin) Assert.That(Session.SuggestPreset(word + "x"), Is.Not.EqualTo(entry.Preset).Or.Null, "Latin words match whole words only: " + word);
                    // Japanese words, romaji too, are read only in menu and clip names, never in raw BlendShape names.
                    Assert.That(ExpressionNameInference.MatchEmotion(word, englishOnly: true),
                        japanese ? Is.Null : Is.EqualTo(((ExpressionPreset, int)?)(entry.Preset, entry.Tier)), word);
                    checkedWords++;
                }
            Assert.That(checkedWords, Is.GreaterThan(50));
            var words = ExpressionNameInference.Emotions.SelectMany(entry => entry.English.Concat(entry.Japanese)).ToArray();
            Assert.That(ExpressionNameInference.Emotions.SelectMany(entry => entry.English).All(word => word.All(character => character < 128)), "English words are ASCII.");
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

        // A clip named "1" that moves a happy eye and a smiling mouth, names
        // listed in the shipped ExpressionShapeNames.json.
        static AnimationClip UnnamedSmileClip()
        {
            var clip = new AnimationClip { name = "1" };
            foreach (var shape in new[] { "eye_smile", "mouth_smile" })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0, 1, 100));
            return clip;
        }

        static Mesh SmileShapesMesh(AttachmentConnectionTests.Fixture fixture) =>
            FrontMesh(fixture, Shape("eye_smile", BlinkDelta, 0), Shape("mouth_smile", OpenDelta, 1));

        [Test]
        public void UnnamedClipIsSuggestedByTheShapesItMoves()
        {
            using var fixture = Fixture(smile: false);
            var mesh = SmileShapesMesh(fixture);
            var unnamed = UnnamedSmileClip();
            try
            {
                Assert.That(ExpressionShapeNames.Shipped.Count, Is.GreaterThan(0));
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { unnamed });
                Assert.That(suggested.TryGetValue(ExpressionPreset.happy, out var happy), Is.True, "A happy eye and a smiling mouth say happy.");
                Assert.That(happy.Clip, Is.SameAs(unnamed));
                Assert.That(happy.Automatic, Is.True);
                Assert.That(suggested.Keys.Intersect(new[] { ExpressionPreset.angry, ExpressionPreset.sad, ExpressionPreset.relaxed, ExpressionPreset.surprised }), Is.Empty);
            }
            finally { Object.DestroyImmediate(unnamed); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void EmotionNameWinsOverTheShapesAnUnnamedClipMoves()
        {
            using var fixture = Fixture(smile: false);
            var mesh = SmileShapesMesh(fixture);
            var unnamed = UnnamedSmileClip();
            var named = FaceClip(80); named.name = "Happy face";
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.SuggestPresets(new[] { unnamed, named })[ExpressionPreset.happy].Clip, Is.SameAs(named),
                    "A clip whose name says happy fills happy before any guess from shapes.");
            }
            finally { Object.DestroyImmediate(unnamed); Object.DestroyImmediate(named); Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void AuthoredVrmPresetIsNotReplacedByAnUnnamedClip()
        {
            using var fixture = Fixture(smile: false);
            var mesh = SmileShapesMesh(fixture);
            var unnamed = UnnamedSmileClip();
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var authored = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                authored.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .5f) };
                settings.Expression.Happy = authored;
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.happy), Is.True);
                Assert.That(session.SuggestPresets(new[] { unnamed }).ContainsKey(ExpressionPreset.happy), Is.False,
                    "The avatar's own happy face is kept.");
            }
            finally { Object.DestroyImmediate(unnamed); Object.DestroyImmediate(authored); Object.DestroyImmediate(settings); Object.DestroyImmediate(mesh); }
        }

        // An FX controller with an Int "Face" whose Any State transitions play
        // each clip under the given condition, as menu toggles drive a face layer.
        static UnityEditor.Animations.AnimatorController FaceController(params (AnimationClip Clip, AnimatorConditionMode Mode, float Threshold)[] faces)
        {
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddParameter("Face", AnimatorControllerParameterType.Int);
            var machine = new UnityEditor.Animations.AnimatorStateMachine();
            machine.defaultState = machine.AddState("Idle");
            foreach (var face in faces)
            {
                var state = machine.AddState(face.Clip.name); state.motion = face.Clip;
                machine.AddAnyStateTransition(state).AddCondition(face.Mode, face.Threshold, "Face");
            }
            controller.layers = new[] { new UnityEditor.Animations.AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
            return controller;
        }

        // One layer per clip, each entering its clip from Any State when all
        // the given conditions hold. Parameters are added as Int, or Bool when
        // a condition on them is If/IfNot.
        static UnityEditor.Animations.AnimatorController LayeredController(params (AnimationClip Clip, (AnimatorConditionMode Mode, float Threshold, string Parameter)[] Conditions)[] faces)
        {
            var controller = new UnityEditor.Animations.AnimatorController();
            foreach (var parameter in faces.SelectMany(face => face.Conditions).GroupBy(condition => condition.Parameter))
                controller.AddParameter(parameter.Key, parameter.Any(condition => condition.Mode == AnimatorConditionMode.If || condition.Mode == AnimatorConditionMode.IfNot)
                    ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Int);
            var layers = new List<UnityEditor.Animations.AnimatorControllerLayer>();
            foreach (var face in faces)
            {
                var machine = new UnityEditor.Animations.AnimatorStateMachine();
                machine.defaultState = machine.AddState("Idle");
                var state = machine.AddState(face.Clip.name); state.motion = face.Clip;
                var transition = machine.AddAnyStateTransition(state);
                foreach (var condition in face.Conditions) transition.AddCondition(condition.Mode, condition.Threshold, condition.Parameter);
                layers.Add(new UnityEditor.Animations.AnimatorControllerLayer { name = face.Clip.name, defaultWeight = 1, stateMachine = machine });
            }
            controller.layers = layers.ToArray();
            return controller;
        }

        static void DestroyController(UnityEditor.Animations.AnimatorController controller)
        {
            foreach (var layer in controller.layers)
            {
                foreach (var transition in layer.stateMachine.anyStateTransitions) Object.DestroyImmediate(transition);
                foreach (var child in layer.stateMachine.states)
                {
                    foreach (var transition in child.state.transitions) Object.DestroyImmediate(transition);
                    Object.DestroyImmediate(child.state);
                }
                Object.DestroyImmediate(layer.stateMachine);
            }
            Object.DestroyImmediate(controller);
        }

        static VrChatExpressionMenu.Entry MenuItem(string path, float value, string type = "Toggle", string parameter = "Face")
        {
            var entry = new VrChatExpressionMenu.Entry { Name = path, ControlType = type, ControlParameter = parameter };
            entry.Parameters[parameter] = value;
            return entry;
        }

        static readonly Dictionary<string, float> FaceOff = new Dictionary<string, float> { ["Face"] = 0 };

        [Test]
        public void GenericallyNamedClipIsSuggestedByTheMenuItemThatSelectsIt()
        {
            using var fixture = Fixture(smile: false);
            var clip = FaceClip(80); clip.name = "Face 1";
            var controller = FaceController((clip, AnimatorConditionMode.Equals, 1));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller, FaceOff);
                Assert.That(paths.TryGetValue(clip, out var found), Is.True);
                Assert.That(found, Is.EqualTo(new[] { "Face / Smile" }));
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { clip }, menuPaths: paths);
                Assert.That(suggested.TryGetValue(ExpressionPreset.happy, out var happy), Is.True, "The toggle labelled Smile says the clip is happy.");
                Assert.That(happy.Clip, Is.SameAs(clip));
                Assert.That(happy.Automatic, Is.True);
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
        }

        [Test]
        public void ClipNameWinsOverMenuPathAndTheBoardKeepsRemovalsAndAuthoredPresets()
        {
            using var fixture = Fixture(smile: false);
            var smile = FaceClip(80); smile.name = "Face 1";
            var named = FaceClip(70); named.name = "Sad face";
            var angry = FaceClip(60); angry.name = "Face 3";
            var controller = FaceController((smile, AnimatorConditionMode.Equals, 1), (named, AnimatorConditionMode.Equals, 2), (angry, AnimatorConditionMode.Equals, 3));
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var authored = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                authored.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .5f) };
                settings.Expression.Happy = authored;
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1), MenuItem("Face / Angry", 2), MenuItem("Face / Angry 2", 3) }, controller, FaceOff);
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { smile, named, angry }, menuPaths: paths);
                Assert.That(suggested.ContainsKey(ExpressionPreset.happy), Is.False, "The avatar's own happy face is kept.");
                Assert.That(suggested[ExpressionPreset.sad].Clip, Is.SameAs(named), "The clip's own name decides before its menu label.");
                Assert.That(suggested[ExpressionPreset.angry].Clip, Is.SameAs(angry));
                var board = new ExpressionBoard(session.Presets, session.SetPreset, clips => session.SuggestPresets(clips, menuPaths: paths), session.HasAuthoredPreset);
                board.SetItems(null, new[] { smile, named, angry }.Select(clip => new ExpressionBoard.Item { Clip = clip, Source = "Fixture" }));
                board.ApplyAutomatic();
                Assert.That(session.Presets[ExpressionPreset.angry].Automatic, Is.True);
                Assert.That(session.Presets[ExpressionPreset.angry].Clip, Is.SameAs(angry));
                session.SetPreset(ExpressionPreset.angry, Session.PresetAssignment.None(ExpressionPreset.angry));
                board.ApplyAutomatic();
                Assert.That(session.Presets[ExpressionPreset.angry].IsNone, Is.True, "A removed suggestion is not filled again.");
                Assert.That(session.Presets.ContainsKey(ExpressionPreset.happy), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(smile); Object.DestroyImmediate(named); Object.DestroyImmediate(angry);
                Object.DestroyImmediate(authored); Object.DestroyImmediate(settings); DestroyController(controller);
            }
        }

        [Test]
        public void ClipSelectedByNoMenuItemIsSuggestedAsBefore()
        {
            using var fixture = Fixture(smile: false);
            var mesh = SmileShapesMesh(fixture);
            var unnamed = UnnamedSmileClip();
            var generic = FaceClip(80); generic.name = "Face 1";
            var other = FaceClip(70); other.name = "Face 2";
            var controller = FaceController((other, AnimatorConditionMode.Equals, 2));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Sad", 2) }, controller, FaceOff);
                Assert.That(paths.Keys, Is.EquivalentTo(new[] { other }));
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { generic, unnamed }, menuPaths: paths);
                Assert.That(suggested[ExpressionPreset.happy].Clip, Is.SameAs(unnamed), "Without a menu item the shapes still decide.");
                Assert.That(suggested.ContainsKey(ExpressionPreset.sad), Is.False, "Only the clip the item selects takes its label.");
                Assert.That(session.SuggestPresets(new[] { generic, unnamed }).Keys, Is.EquivalentTo(suggested.Keys),
                    "An avatar without a VRChat menu gets the same suggestions.");
                // A label that names a part, not an emotion, also stops the guess from shapes.
                var vetoed = new Dictionary<AnimationClip, List<string>> { [unnamed] = new List<string> { "Face / Eye" } };
                Assert.That(session.SuggestPresets(new[] { unnamed }, menuPaths: vetoed).ContainsKey(ExpressionPreset.happy), Is.False);
            }
            finally { Object.DestroyImmediate(unnamed); Object.DestroyImmediate(generic); Object.DestroyImmediate(other); Object.DestroyImmediate(mesh); DestroyController(controller); }
        }

        [Test]
        public void TransitionFalseUnderTheItemValueDoesNotLink()
        {
            var clip = new AnimationClip { name = "Face 1" };
            var controller = FaceController((clip, AnimatorConditionMode.Equals, 1));
            try
            {
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 2) }, controller, FaceOff), Is.Empty,
                    "The item sets Face=2; the face needs Face==1.");
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1, "RadialPuppet") }, controller, FaceOff), Is.Empty,
                    "Only toggles and buttons select one face.");
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
        }

        [Test]
        public void TransitionAlsoTrueAtTheOffValueDoesNotLink()
        {
            var always = new AnimationClip { name = "Face 1" };
            var pressed = new AnimationClip { name = "Face 2" };
            var controller = FaceController((always, AnimatorConditionMode.Less, 2), (pressed, AnimatorConditionMode.Greater, .5f));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1, "Button") }, controller, FaceOff);
                Assert.That(paths.Keys, Is.EquivalentTo(new[] { pressed }), "Face<2 also plays when the button is released, so the item does not decide it.");
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller, new Dictionary<string, float>()).Keys,
                    Is.EquivalentTo(new[] { pressed }), "A parameter without a declared default is off at 0.");
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller, new Dictionary<string, float> { ["Face"] = 1 }), Is.Empty,
                    "An item that only restates the default value decides nothing.");
            }
            finally { Object.DestroyImmediate(always); Object.DestroyImmediate(pressed); DestroyController(controller); }
        }

        [Test]
        public void MenuPathsThatDisagreeGiveNoSuggestion()
        {
            using var fixture = Fixture(smile: false);
            var clip = FaceClip(80); clip.name = "Face 1";
            var controller = FaceController((clip, AnimatorConditionMode.Equals, 1));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1), MenuItem("Favorite / Sad", 1) }, controller, FaceOff);
                Assert.That(paths[clip], Is.EqualTo(new[] { "Face / Smile", "Favorite / Sad" }), "Every item that selects the clip is kept, in menu order.");
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(new[] { clip }, menuPaths: paths);
                Assert.That(suggested.Keys.Intersect(Session.EmotionPresets), Is.Empty, "Two items naming different emotions say neither.");
                var agreeing = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1), MenuItem("Favorite / Happy", 1) }, controller, FaceOff);
                Assert.That(session.SuggestPresets(new[] { clip }, menuPaths: agreeing)[ExpressionPreset.happy].Clip, Is.SameAs(clip),
                    "Items naming the same emotion agree.");
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
        }

        [Test]
        public void FaceSplitOverLayersGivesNoMenuSuggestion()
        {
            using var fixture = Fixture(smile: false);
            var eyes = FaceClip(80); eyes.name = "Layer 1";
            var mouth = FaceClip(70); mouth.name = "Layer 2";
            var controller = LayeredController((eyes, new[] { (AnimatorConditionMode.Equals, 1f, "Face") }), (mouth, new[] { (AnimatorConditionMode.Equals, 1f, "Face") }));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller, FaceOff);
                Assert.That(paths, Is.Empty, "Each layer's clip is only part of the smile, so the item is linked to neither.");
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.SuggestPresets(new[] { eyes, mouth }, menuPaths: paths).Keys.Intersect(Session.EmotionPresets), Is.Empty);
            }
            finally { Object.DestroyImmediate(eyes); Object.DestroyImmediate(mouth); DestroyController(controller); }
        }

        [Test]
        public void ClipNamingAPartTakesNoEmotionFromItsMenuPath()
        {
            using var fixture = Fixture(smile: false);
            var clip = FaceClip(80); clip.name = "Mouth_Smile";
            var controller = FaceController((clip, AnimatorConditionMode.Equals, 1));
            try
            {
                Assert.That(ExpressionNameInference.MatchEmotion(clip.name), Is.Null);
                Assert.That(ExpressionNameInference.SaysNothing(clip.name), Is.False);
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller, FaceOff);
                Assert.That(paths.Keys, Is.EquivalentTo(new[] { clip }));
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.SuggestPresets(new[] { clip }, menuPaths: paths).Keys.Intersect(Session.EmotionPresets), Is.Empty,
                    "A clip named after a part of the face is not a whole emotion, whatever the item says.");
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
        }

        [Test]
        public void OtherParameterOffItsDefaultStopsTheLink()
        {
            var clip = new AnimationClip { name = "Face 1" };
            var controller = LayeredController((clip, new[] { (AnimatorConditionMode.Equals, 1f, "Face"), (AnimatorConditionMode.Equals, 1f, "Variant") }));
            try
            {
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller,
                    new Dictionary<string, float> { ["Face"] = 0, ["Variant"] = 0 }), Is.Empty, "Variant is 0 by default, so Face==1 alone does not play the face.");
                Assert.That(Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1) }, controller,
                    new Dictionary<string, float> { ["Face"] = 0, ["Variant"] = 1 }).Keys, Is.EquivalentTo(new[] { clip }), "Variant is 1 by default.");
                var underSubmenu = MenuItem("Variant / Smile", 1); underSubmenu.Parameters["Variant"] = 1;
                Assert.That(Session.MenuClipPaths(new[] { underSubmenu }, controller,
                    new Dictionary<string, float> { ["Face"] = 0, ["Variant"] = 0 }).Keys, Is.EquivalentTo(new[] { clip }), "The submenu sets Variant to 1.");
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
        }

        [Test]
        public void BoolToggleLinksItsClip()
        {
            var clip = new AnimationClip { name = "Face 1" };
            var controller = LayeredController((clip, new[] { (AnimatorConditionMode.If, 0f, "Smile") }));
            try
            {
                var paths = Session.MenuClipPaths(new[] { MenuItem("Face / Smile", 1, parameter: "Smile") }, controller,
                    new Dictionary<string, float> { ["Smile"] = 0 });
                Assert.That(paths.TryGetValue(clip, out var found), Is.True);
                Assert.That(found, Is.EqualTo(new[] { "Face / Smile" }));
            }
            finally { Object.DestroyImmediate(clip); DestroyController(controller); }
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
            Assert.That(ExpressionNameInference.MatchEmotion("怒り", englishOnly: true), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion("Fcl_BRW_Angry", englishOnly: true), Is.Null);
            Assert.That(ExpressionNameInference.MatchEmotion("Fcl_ALL_Angry", englishOnly: true), Is.EqualTo(((ExpressionPreset, int)?)(ExpressionPreset.angry, 0)));
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

        [Test]
        public async Task MouthGazeAndNeutralAssignmentsExportWithoutOverrides()
        {
            using var fixture = Fixture();
            var mesh = FrontMesh(fixture, Shape("Open", OpenDelta), Shape("Raise", RaiseDelta));
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                session.SetPreset(ExpressionPreset.aa, Session.PresetAssignment.ForShape(ExpressionPreset.aa, "Front", "Open"));
                session.SetPreset(ExpressionPreset.lookUp, Session.PresetAssignment.ForShape(ExpressionPreset.lookUp, "Front", "Raise"));
                session.SetPreset(ExpressionPreset.neutral, Session.PresetAssignment.ForShape(ExpressionPreset.neutral, "Back", "Smile"));
                session.SetPreset(ExpressionPreset.happy, Session.PresetAssignment.ForShape(ExpressionPreset.happy, "Back", "Smile"));
                var bytes = session.Export("Test", "Tests", NoFaces, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
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

        [Test]
        public async Task AssignedBlinkReplacesTheAutomaticBlink()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Shut", ShutDelta));
            Vrm10Instance assigned = null, automatic = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var origin = fixture.Mesh.vertices[0] + HairDelta * .35f;
                automatic = await Load(session.Export("Test", "Tests", NoFaces));
                var front = automatic.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                automatic.Runtime.Expression.SetWeight(ExpressionKey.Blink, 1); automatic.Runtime.Process();
                Assert.That(Vector3.Distance(FirstVertex(front), origin + BlinkDelta), Is.LessThan(.0001), "Without an assignment the automatic blink picks 'Blink'.");

                session.SetPreset(ExpressionPreset.blink, Session.PresetAssignment.ForShape(ExpressionPreset.blink, "Front", "Shut"));
                assigned = await Load(session.Export("Test", "Tests", NoFaces));
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
                imported = await Load(session.Export("Test", "Tests", NoFaces));
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

        [Test]
        public async Task ConfirmingOneBlinkPresetKeepsTheOtherCandidates()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1), Shape("Shut_L", ShutDelta, 0));
            Vrm10Instance imported = null;
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>());
                Assert.That(suggested.Keys, Is.EquivalentTo(Session.BlinkPresets));
                var before = session.Export("Test", "Tests", NoFaces);
                var unconfirmed = session.Export("Test", "Tests", NoFaces, presets: suggested.Values);
                Assert.That(Canonical(Expressions(unconfirmed)), Is.EqualTo(Canonical(Expressions(before))),
                    "Automatic blink candidates export exactly what the automatic blink exports.");

                var changed = suggested.Values.Select(value => value.Preset == "blinkLeft"
                    ? Session.PresetAssignment.ForShape(ExpressionPreset.blinkLeft, "Front", "Shut_L") : value).ToArray();
                imported = await Load(session.Export("Test", "Tests", NoFaces, presets: changed));
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
                var before = session.Export("Test", "Tests", NoFaces, blinkOptions: blink);
                var unconfirmed = session.Export("Test", "Tests", NoFaces, blinkOptions: blink, presets: suggested.Values);
                Assert.That(Canonical(Expressions(unconfirmed)), Is.EqualTo(Canonical(Expressions(before))));
                Assert.That(BoundShapes(unconfirmed, "aa"), Is.EqualTo(new[] { "Front/vrc.v_aa@1" }));
                Assert.That(BoundShapes(unconfirmed, "ih"), Is.EqualTo(new[] { "Back/vrc.v_ih@1", "Front/vrc.v_ih@1" }));
                Assert.That(Presets(unconfirmed).ContainsKey("ee"), Is.False);
                // Lip Sync candidates are not known names and are written as assignments.
                var withLipSync = session.Export("Test", "Tests", NoFaces, blinkOptions: blink, presets: lipSync.Values);
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

        static int BindCount(byte[] bytes, string preset) =>
            Presets(bytes).TryGetValue(preset, out var value) && ((Dictionary<string, object>)value).TryGetValue("morphTargetBinds", out var binds)
                ? ((List<object>)binds).Count : 0;

        [Test]
        public void LipSyncCandidateOnAKnownNameKeepsTheNameBindingOnEveryMesh()
        {
            using var fixture = Fixture(smile: false);
            // Back keeps the shared mesh and Front copies it: both have vrc.v_aa.
            fixture.Mesh.AddBlendShapeFrame("vrc.v_aa", 100, Enumerable.Repeat(OpenDelta, 3).ToArray(), new Vector3[3], new Vector3[3]);
            var mesh = FrontMesh(fixture);
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
                var visemes = new string[15]; visemes[10] = "vrc.v_aa";
                var lipSync = session.SuggestMouthPresets(front, visemes);
                Assert.That(lipSync.Keys, Is.EquivalentTo(new[] { ExpressionPreset.aa }));
                Assert.That(lipSync[ExpressionPreset.aa].Automatic && lipSync[ExpressionPreset.aa].Path == "Front", Is.True);
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
                var before = session.Export("Test", "Tests", NoFaces, blinkOptions: blink);
                var unconfirmed = session.Export("Test", "Tests", NoFaces, blinkOptions: blink, presets: lipSync.Values);
                Assert.That(Canonical(Expressions(unconfirmed)), Is.EqualTo(Canonical(Expressions(before))),
                    "An unconfirmed Lip Sync candidate on a known name exports what the name binding exports.");
                Assert.That(BoundShapes(unconfirmed, "aa"), Is.EqualTo(new[] { "Back/vrc.v_aa@1", "Front/vrc.v_aa@1" }), "The other mesh keeps its mouth shape.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void NoneOnAMouthPresetStopsTheNameBinding()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("vrc.v_aa", OpenDelta), Shape("vrc.v_ih", RaiseDelta));
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var blink = new BlinkExportOptions { Mode = BlinkExportMode.None };
                var before = session.Export("Test", "Tests", NoFaces, blinkOptions: blink);
                Assert.That(BindCount(before, "aa"), Is.EqualTo(1), "Without a choice the known name binds aa.");
                var none = session.Export("Test", "Tests", NoFaces, blinkOptions: blink, presets: new[] { Session.PresetAssignment.None(ExpressionPreset.aa) });
                Assert.That(BindCount(none, "aa"), Is.Zero, "\"None\" on aa writes no aa binding.");
                Assert.That(BoundShapes(none, "ih"), Is.EqualTo(BoundShapes(before, "ih")), "The other mouth presets keep the name binding.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void NoneOnEveryBlinkPresetWritesNoBlink()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink", BlinkDelta), Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1));
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var automatic = session.Export("Test", "Tests", NoFaces);
                Assert.That(BindCount(automatic, "blink"), Is.EqualTo(1), "Without a choice the automatic blink binds 'Blink'.");
                var none = session.Export("Test", "Tests", NoFaces, presets: Session.BlinkPresets.Select(Session.PresetAssignment.None).ToArray());
                var noBlink = session.Export("Test", "Tests", NoFaces, blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                // No blink still writes a no-op binding so viewers do not fall back to raw names.
                Assert.That(Session.BlinkPresets.SelectMany(preset => BoundShapes(none, preset.ToString())), Has.All.Contains("/__VRVlog_BlinkNone_"));
                Assert.That(Canonical(Expressions(none)), Is.EqualTo(Canonical(Expressions(noBlink))), "\"None\" on every blink preset is no blink.");
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        [Test]
        public void ChoosingOneEyeKeepsTheAutomaticBlinkOnEveryMesh()
        {
            using var fixture = Fixture(smile: false);
            // Back keeps the shared mesh and Front copies it: the automatic
            // blink closes both eyes on two meshes, so it offers no candidate.
            foreach (var shape in new[] { Shape("Blink", BlinkDelta), Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1) })
                fixture.Mesh.AddBlendShapeFrame(shape.Name, 100, shape.Deltas, new Vector3[3], new Vector3[3]);
            var mesh = FrontMesh(fixture);
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.SuggestPresets(Array.Empty<AnimationClip>()).Keys.Intersect(Session.BlinkPresets), Is.Empty);
                // The blink export bakes each binding into a generated endpoint
                // shape named from its mesh, source shape and slot.
                static string[] Meshes(IEnumerable<string> bound) => bound.Select(value => value.Substring(0, value.IndexOf('/'))).ToArray();
                var automatic = session.Export("Test", "Tests", NoFaces);
                Assert.That(Meshes(BoundShapes(automatic, "blink")), Is.EqualTo(new[] { "Back", "Front" }));
                var chosen = session.Export("Test", "Tests", NoFaces, presets: new[] {
                    Session.PresetAssignment.ForShape(ExpressionPreset.blinkLeft, "Front", "Blink_L"),
                    Session.PresetAssignment.ForShape(ExpressionPreset.blinkRight, "Front", "Blink_R") });
                Assert.That(BoundShapes(chosen, "blink"), Is.EqualTo(BoundShapes(automatic, "blink")), "The unchosen both-eyes slot keeps the automatic two meshes.");
                Assert.That(Meshes(BoundShapes(chosen, "blinkLeft")), Is.EqualTo(new[] { "Front" }));
                Assert.That(Meshes(BoundShapes(chosen, "blinkRight")), Is.EqualTo(new[] { "Front" }));
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        // A face file that also records "Hair detail" (35 is the baseline on Front).
        static AnimationClip EyeClip(string shape, float detail)
        {
            var clip = new AnimationClip { name = shape };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1, detail));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0, 1, 100));
            return clip;
        }

        [Test]
        public void BlinkClipsUseOnlyWhatDiffersFromTheBaseline()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1));
            var left = EyeClip("Blink_L", 35); var right = EyeClip("Blink_R", 35);
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var chosen = new[] { Session.PresetAssignment.ForClip(ExpressionPreset.blinkLeft, left), Session.PresetAssignment.ForClip(ExpressionPreset.blinkRight, right) };
                foreach (var assignment in chosen) session.SetPreset(Session.ParsePreset(assignment.Preset), assignment);
                Assert.That(session.PresetError(ExpressionPreset.blinkLeft, chosen[0]), Is.Null);
                Assert.That(session.PresetError(ExpressionPreset.blinkRight, chosen[1]), Is.Null);
                Assert.That(session.BlinkSidesOverlap(ExpressionPreset.blinkLeft, chosen[0]), Is.False);
                var bytes = session.Export("Test", "Tests", NoFaces, presets: chosen);
                Assert.That(BoundShapes(bytes, "blinkLeft").Length, Is.EqualTo(1), "The unchanged 'Hair detail' is not part of the blink.");
                Assert.That(BoundShapes(bytes, "blinkRight").Length, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
        }

        [Test]
        public void BlinkClipsSharingAShapeAreAnEyeSlotError()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("Blink_L", LeftDelta, 0), Shape("Blink_R", RightDelta, 1));
            var left = EyeClip("Blink_L", 60); var right = EyeClip("Blink_R", 60);
            try
            {
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                var chosen = new[] { Session.PresetAssignment.ForClip(ExpressionPreset.blinkLeft, left), Session.PresetAssignment.ForClip(ExpressionPreset.blinkRight, right) };
                foreach (var assignment in chosen) session.SetPreset(Session.ParsePreset(assignment.Preset), assignment);
                var error = session.PresetError(ExpressionPreset.blinkLeft, chosen[0]);
                Assert.That(error, Is.Not.Null, "Both eyes move 'Hair detail' away from the baseline.");
                Assert.That(session.PresetError(ExpressionPreset.blinkRight, chosen[1]), Is.Not.Null);
                Assert.That(session.BlinkSidesOverlap(ExpressionPreset.blinkLeft, chosen[0]), Is.True, "The slot shows the short overlap text.");
                Assert.That(session.BlinkSidesOverlap(ExpressionPreset.blinkRight, chosen[1]), Is.True);
                var thrown = Assert.Throws<InvalidOperationException>(() => session.Export("Test", "Tests", NoFaces, presets: chosen));
                Assert.That(thrown.Message, Is.EqualTo(error), "The export stops with the eye slot's own error.");
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(left); Object.DestroyImmediate(right); }
        }

        [Test]
        public void NoneOnAnEmptySlotKeepsTheSourceVrmBlink()
        {
            using var fixture = Fixture(smile: false);
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var blink = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                blink.IsBinary = true;
                blink.MorphTargetBindings = new[] { new MorphTargetBinding("Front", 0, .6f) };
                settings.Expression.Blink = blink;
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.blinkLeft), Is.False);
                var automatic = session.Export("Test", "Tests", NoFaces);
                var none = session.Export("Test", "Tests", NoFaces, presets: new[] { Session.PresetAssignment.None(ExpressionPreset.blinkLeft) });
                Assert.That(((Dictionary<string, object>)Presets(none)["blink"])["isBinary"], Is.EqualTo(true), "The source VRM's own blink is kept.");
                Assert.That(Canonical(Expressions(none)), Is.EqualTo(Canonical(Expressions(automatic))), "\"None\" on a slot the VRM leaves empty changes nothing.");
            }
            finally { Object.DestroyImmediate(blink); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void EmptyVrmMouthAndBlinkPresetsAreAuthored()
        {
            using var fixture = Fixture(smile: false);
            var mesh = FrontMesh(fixture, Shape("vrc.v_aa", OpenDelta), Shape("Blink", BlinkDelta));
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var aa = ScriptableObject.CreateInstance<VRM10Expression>();
            var blink = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                settings.Expression.Aa = aa; settings.Expression.Blink = blink;
                fixture.Source.AddComponent<Vrm10Instance>().Vrm = settings;
                using var session = new Session(fixture.Source, replayInstalledDefaults: false);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.aa), Is.True, "An intentionally empty aa is the avatar's own.");
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.blink), Is.True);
                Assert.That(session.HasAuthoredPreset(ExpressionPreset.ih), Is.False);
                var suggested = session.SuggestPresets(Array.Empty<AnimationClip>());
                Assert.That(suggested.ContainsKey(ExpressionPreset.aa), Is.False, "No candidate replaces the empty aa.");
                Assert.That(suggested.Keys.Intersect(Session.BlinkPresets), Is.Empty);
            }
            finally { Object.DestroyImmediate(mesh); Object.DestroyImmediate(aa); Object.DestroyImmediate(blink); Object.DestroyImmediate(settings); }
        }

        [Test]
        public void MouthBlinkAndNeutralChoicesAreSavedLikeEmotions()
        {
            using var fixture = Fixture();
            string json;
            using (var session = new Session(fixture.Source, replayInstalledDefaults: false))
            {
                Record(session, "Sample", 75);
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

        [Test]
        public void StoreKeepsFaceNamesByAssetAndReadsFilesWithoutThem()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-presets-" + Guid.NewGuid().ToString("N") + ".json");
            var previous = ExpressionPresetStore.PathOverride;
            var folder = "Assets/VRVlogNameStore_" + Guid.NewGuid().ToString("N"); AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            ExpressionPresetStore.PathOverride = path;
            try
            {
                var kept = FaceClip(40); kept.name = "Kept"; AssetDatabase.CreateAsset(kept, folder + "/Kept.anim");
                var removed = FaceClip(50); removed.name = "Removed"; AssetDatabase.CreateAsset(removed, folder + "/Removed.anim");
                var memory = FaceClip(60);
                ExpressionPresetStore.Save("avatar-a", Array.Empty<Session.PresetAssignment>(), null, new Dictionary<AnimationClip, string>
                    { [kept] = " Grin ", [removed] = "Gone", [memory] = "Memory" });
                var names = ExpressionPresetStore.LoadNames("avatar-a", out var skipped);
                Assert.That(skipped, Is.Zero);
                Assert.That(names.Count, Is.EqualTo(2), "Only assets are stored.");
                Assert.That(names[kept], Is.EqualTo("Grin"));
                Assert.That(names[removed], Is.EqualTo("Gone"));

                ExpressionPresetStore.Save("avatar-a", new[] { Session.PresetAssignment.None(ExpressionPreset.angry) }, new[] { removed });
                Assert.That(ExpressionPresetStore.LoadNames("avatar-a", out _)[kept], Is.EqualTo("Grin"), "Saving without names keeps the stored names.");
                Assert.That(ExpressionPresetStore.LoadExcluded("avatar-a", out _), Is.EqualTo(new[] { removed }));

                AssetDatabase.DeleteAsset(folder + "/Removed.anim");
                names = ExpressionPresetStore.LoadNames("avatar-a", out skipped);
                Assert.That(names.Keys, Is.EqualTo(new[] { kept }));
                Assert.That(skipped, Is.EqualTo(1));

                // Damaged names are skipped instead of reaching the board.
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"Grin\"", "\"two\\nlines\""), Encoding.UTF8);
                Assert.That(ExpressionPresetStore.LoadNames("avatar-a", out skipped), Is.Empty);
                Assert.That(skipped, Is.EqualTo(2));

                // Names alone keep the avatar's entry; no names and no choices remove it.
                ExpressionPresetStore.Save("avatar-a", Array.Empty<Session.PresetAssignment>(), Array.Empty<AnimationClip>(), new Dictionary<AnimationClip, string> { [kept] = "Grin" });
                Assert.That(ExpressionPresetStore.LoadNames("avatar-a", out _)[kept], Is.EqualTo("Grin"));
                ExpressionPresetStore.Save("avatar-a", Array.Empty<Session.PresetAssignment>(), Array.Empty<AnimationClip>(), new Dictionary<AnimationClip, string>());
                Assert.That(ExpressionPresetStore.LoadNames("avatar-a", out _), Is.Empty);
                Assert.That(File.ReadAllText(path), Does.Not.Contain("avatar-a"));
                Object.DestroyImmediate(memory);

                // A file written before names existed.
                const string older = "{\"Version\":1,\"Avatars\":[{\"Avatar\":\"avatar-b\",\"Presets\":[{\"Preset\":\"angry\",\"ClipGuid\":\"\",\"ClipLocalId\":0,\"Path\":\"\",\"Shape\":\"\"}],\"Excluded\":[]}]}";
                File.WriteAllText(path, older, Encoding.UTF8);
                Assert.That(ExpressionPresetStore.LoadNames("avatar-b", out skipped), Is.Empty);
                Assert.That(skipped, Is.Zero);
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
