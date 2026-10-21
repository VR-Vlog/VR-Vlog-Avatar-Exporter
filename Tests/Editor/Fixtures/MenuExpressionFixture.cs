using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Expressions;
using VRVlog.FaceTracking;

namespace VRVlog.LilToonExporter.Tests
{
    internal static class MenuExpressionFixture
    {
        private static Dictionary<string, object> Obj(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (var i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }
        private static List<object> Arr(params object[] values) => values.ToList();
        private static Dictionary<string, object> Mesh(params string[] names) => Obj("extras", Obj("targetNames", names.Cast<object>().ToList()),
            "primitives", Arr(Obj("targets", names.Select(n => (object)Obj()).ToList())));
        private static Dictionary<string, object> Custom(Dictionary<string, object> root) =>
            (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)root["extensions"])["VRMC_vrm"])["expressions"])["custom"];

        internal static void Run(Action<bool, string> check)
        {
            var binary = new byte[] { 1, 2, 3, 4 };
            var root = Obj("asset", Obj("version", "2.0"), "extensions", Obj("VRMC_vrm", Obj("expressions", Obj(
                "preset", Obj("blink", Obj("isBinary", false)), "custom", Obj("笑顔", Obj("isBinary", false))))),
                "nodes", Arr(Obj("mesh", 1L), Obj(), Obj("mesh", 0L)),
                "meshes", Arr(Mesh("raw-eye", "composite-face"), Mesh("raw-body", "composite-brow")));
            var expressions = new List<VrmMenuExpressions.Expression> { new VrmMenuExpressions.Expression { Name = "笑顔" } };
            expressions[0].Targets.AddRange(new[] { "composite-face", "composite-brow" });
            var bytes = GlbDocument.Create(root, binary).Write();
            var result = GlbDocument.Read(VrmMenuExpressions.Add(bytes, expressions));
            check(VrmMenuExpressions.CountRegistered(bytes) == 0, "A blink preset and an unbound custom name are not exported selectable faces.");
            check(VrmMenuExpressions.CountRegistered(result.Write()) == 1, "The export completion count comes from the actual bound VRM expressions.");
            var custom = Custom(result.Json);
            check(custom.Count == 2 && custom.ContainsKey("笑顔 (2)"), "One composed menu entry is added; same-named authored expressions are retained.");
            check(!(bool)((Dictionary<string, object>)custom["笑顔"])["isBinary"], "Existing expression settings remain unchanged.");
            var expression = (Dictionary<string, object>)custom["笑顔 (2)"];
            var binds = (List<object>)expression["morphTargetBinds"];
            check(binds.Count == 2 && (long)((Dictionary<string, object>)binds[0])["node"] == 2 &&
                (long)((Dictionary<string, object>)binds[1])["node"] == 0, "Multi-renderer expressions bind final exported nodes after reordering.");
            check(binds.Cast<Dictionary<string, object>>().All(b => (long)b["index"] == 1 && Convert.ToDouble(b["weight"]) == 1),
                "Each combined residual is applied once at weight one.");
            check((string)expression["overrideBlink"] == "block" && (string)expression["overrideMouth"] == "block" && (string)expression["overrideLookAt"] == "block",
                "Procedural blink, mouth and gaze cannot add onto the selected composed face.");
            check(result.Binary.SequenceEqual(binary), "Registering expressions does not rewrite exported geometry or textures.");
            check(!custom.ContainsKey("raw-eye") && !custom.ContainsKey("raw-body"), "Raw BlendShapes are never catalogued as expressions.");
            check(VrmMenuExpressions.Add(bytes, new List<VrmMenuExpressions.Expression>()).SequenceEqual(bytes), "No menu expressions leaves an existing VRM byte-identical.");
            expressions[0].Targets.Add("absent");
            bool rejected = false;
            try { VrmMenuExpressions.Add(bytes, expressions); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Missing exported composite targets fail before saving a misleading expression.");
            expressions[0].Targets.Remove("absent");
            var animation = new ExpressionAnimationData { Duration = 1, Loop = true };
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = 100, OutTangent = 100 });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1, Value = 100, InTangent = 100, OutTangent = 100 });
            var channel = new ExpressionAnimationData.Channel { Curve = curve };
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 0, Target = null });
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 100, Target = ExpressionAnimationData.TargetPrefix + "test" });
            animation.Channels.Add(channel); expressions[0].Animation = animation;
            ((List<object>)root["meshes"])[0] = Mesh("raw-eye", "composite-face", ExpressionAnimationData.TargetPrefix + "test");
            var animated = GlbDocument.Read(VrmMenuExpressions.Add(GlbDocument.Create(root, binary).Write(), expressions));
            var written = ((Dictionary<string, object>)animated.Json["extensions"])[ExpressionAnimationData.Extension];
            var document = ExpressionAnimationData.ReadDocument(written);
            var stored = document.Animations;
            check(document.SchemaVersion == 2 && document.Expressions.Count == 1 && document.Expressions[0].Expression == "笑顔 (2)" &&
                document.Expressions[0].Source == ExpressionSource.Menu, "Schema 2 lists the registered expression with its source.");
            check(stored[0].Expression == "笑顔 (2)" && stored[0].Channels[0].Points[0].Morph == null &&
                stored[0].Channels[0].Points[1].Morph != null && stored[0].Channels[0].Points[1].Morph.Node == 2 && stored[0].Channels[0].Points[1].Morph.Index == 2,
                "Animated data follows the final disambiguated expression name; points refer to the output node and morph index, rest moves nothing.");
            var animatedBinds = (List<object>)((Dictionary<string, object>)Custom(animated.Json)["笑顔 (2)"])["morphTargetBinds"];
            check(animatedBinds.Count == 3 && animatedBinds.Cast<Dictionary<string, object>>().Any(b => (long)b["node"] == 2 && (long)b["index"] == 2 && Convert.ToDouble(b["weight"]) == 1),
                "The VRM expression holds the animation's last frame.");
            rejected = false;
            try { ExpressionAnimationData.Read(written); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Apps that read only schema 1 reject the file and keep the standard expression.");
            check(Custom(animated.Json).Count == 2 && VrmMenuExpressions.CountRegistered(animated.Write()) == 1,
                "Animation basis morphs do not inflate the exported expression count or selector list.");
            check(((List<object>)animated.Json["extensionsUsed"]).Contains(ExpressionAnimationData.Extension) && !animated.Json.ContainsKey("extensionsRequired"),
                "Animation is an optional extension; other VRM viewers retain the standard first pose.");
            ((List<object>)root["nodes"]).Add(Obj("mesh", 0L));
            rejected = false;
            try { VrmMenuExpressions.Add(GlbDocument.Create(root, binary).Write(), expressions); } catch (InvalidOperationException) { rejected = true; }
            check(rejected, "Ambiguous instanced output targets are rejected rather than bound to an arbitrary node.");

            // Names carry no source prefix. Repeated labels, VRM preset names
            // and face tracking channel names get " (2)", " (3)"...
            var plainRoot = Obj("asset", Obj("version", "2.0"), "extensions", Obj("VRMC_vrm", Obj("expressions", Obj(
                "custom", Obj("Authored", Obj("isBinary", false, "morphTargetBinds", Arr(Obj("node", 0L, "index", 0L, "weight", 1.0))))))),
                "nodes", Arr(Obj("mesh", 0L)), "meshes", Arr(Mesh("composite-a", "composite-b")));
            var plain = new List<VrmMenuExpressions.Expression>();
            foreach (var (name, source) in new[] { ("Smile", ExpressionSource.Menu), ("Smile", ExpressionSource.Gesture), ("Smile", ExpressionSource.FaceEmo),
                ("Authored", ExpressionSource.Menu), ("happy", ExpressionSource.Menu), ("JawOpen", ExpressionSource.Menu) })
            {
                var item = new VrmMenuExpressions.Expression { Name = name, Source = source };
                item.Targets.Add("composite-b");
                plain.Add(item);
            }
            var plainResult = GlbDocument.Read(VrmMenuExpressions.Add(GlbDocument.Create(plainRoot, binary).Write(), plain));
            var plainNames = new[] { "Smile", "Smile (2)", "Smile (3)", "Authored (2)", "happy (2)", "JawOpen (2)" };
            check(plainNames.All(Custom(plainResult.Json).ContainsKey) && Custom(plainResult.Json).ContainsKey("Authored") && Custom(plainResult.Json).Count == 7,
                "Menu names are plain; a repeated, authored, VRM preset or tracking channel name is numbered.");
            var plainDocument = ExpressionAnimationData.ReadDocument(((Dictionary<string, object>)plainResult.Json["extensions"])[ExpressionAnimationData.Extension]);
            check(plainDocument.Expressions.Select(e => e.Expression).SequenceEqual(plainNames) &&
                plainDocument.Expressions.Select(e => e.Source).SequenceEqual(plain.Select(e => e.Source)),
                "The extension lists the final numbered names with their sources.");
            check(VrmMenuExpressions.CountRegistered(plainResult.Write()) == 6 &&
                VrmMenuExpressions.RegisteredNames(plainResult.Write()).SequenceEqual(plainNames),
                "The completion count reads the extension's list; the authored bound expression is not counted.");
            check(VrmMenuExpressions.CountRegistered(GlbDocument.Create(plainRoot, binary).Write()) == 0,
                "Without the extension no expression counts as registered, even a bound authored one.");

            // Emotion inference reads the menu path, not the shortened VRM name.
            var paths = new List<VrmMenuExpressions.Expression> {
                new VrmMenuExpressions.Expression { Name = "1", InferenceName = "Other / 1" },
                new VrmMenuExpressions.Expression { Name = "1", InferenceName = "Smile / 1" },
                new VrmMenuExpressions.Expression { Name = "Wink" } };
            foreach (var item in paths) item.Targets.Add("composite-b");
            var pathOutput = VrmMenuExpressions.Add(GlbDocument.Create(plainRoot, binary).Write(), paths);
            var candidates = VrmMenuExpressions.EmotionCandidates(paths);
            check(candidates.Count == 3 && candidates["1"] == "Other / 1" && candidates["1 (2)"] == "Smile / 1" && candidates["Wink"] == "Wink",
                "Each registered name keeps the text inference reads; without one it reads the name.");
            var filled = new List<(string Preset, string Source, string Format)>();
            VrmExpressionBindings.AddMissing(pathOutput, inferBlink: false, inferEmotions: true, filledPresets: filled, emotionCandidates: candidates);
            check(filled.SequenceEqual(new[] { ("happy", "1 (2)", VrmExpressionBindings.FilledFormat) }), "Item \"1\" of a \"Smile\" submenu still fills happy.");
            check(VrmMenuExpressions.EmotionShapes(paths).Count == 0, "Faces without source shapes give the shape guess nothing.");
            check(VrmMenuExpressions.UniqueName("Smile", name => false) == "Smile" &&
                VrmMenuExpressions.UniqueName("Smile", name => name == "Smile" || name == "Smile (2)") == "Smile (3)" &&
                VrmMenuExpressions.UniqueName("blink", name => false) == "blink (2)" && VrmMenuExpressions.UniqueName("Blink", name => false) == "Blink",
                "Only exact VRM preset names are reserved.");

            // The app drops the whole extension when a listed name is a preset
            // or a tracking channel by its own matching; such names are numbered,
            // and the numbered name is not reserved again.
            var trackingNames = TrackingExpressionNames.PerfectSync
                .SelectMany(name => new[] { name, char.ToLowerInvariant(name[0]) + name.Substring(1), name.ToUpperInvariant(),
                    "Fcl_ALL_" + name, "Face." + name, "Body/" + name, "mesh:" + name, " " + name + " " })
                .Concat(UnifiedExpressionRegistry.AllNames.SelectMany(name => new[] { name, UnifiedExpressionRegistry.Prefix + name }))
                .Concat(new[] { "neutral", "happy", "lookRight", "blinkLeft", "ee", UnifiedExpressionRegistry.RestPrefix,
                    UnifiedExpressionRegistry.RestPrefix + "2" }).ToArray();
            var notNumbered = trackingNames.Where(name => VrmMenuExpressions.UniqueName(name, taken => false) != name + " (2)").ToArray();
            check(notNumbered.Length == 0, "Every preset or tracking name the app rejects is numbered: " + string.Join(", ", notNumbered));
            check(trackingNames.All(name => !VrmMenuExpressions.Reserved(name + " (2)")), "A numbered name is never reserved.");
            var renamed = new[] { "Smile", "Blink", "Happy", "BigSmile", "SmileJawOpen", UnifiedExpressionRegistry.RestPrefix + " (2)" }
                .Where(name => VrmMenuExpressions.UniqueName(name, taken => false) != name).ToArray();
            check(renamed.Length == 0, "Names that only contain a channel name, or differ from a preset in case, keep their name: " + string.Join(", ", renamed));
        }
    }
}
