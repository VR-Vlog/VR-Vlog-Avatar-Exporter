using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UniVRM10;
#if !EXPORTER_BEHAVIOR_TESTS
using UnityEditor;
using UnityEngine;
#endif

namespace VRVlog.LilToonExporter
{
    // Guesses an emotion from the BlendShapes a menu face moves, for menu
    // items whose label says nothing ("1", "Face 3"). ExpressionShapeNames.json
    // maps a normalized BlendShape name to the part of the face it moves
    // (eye.happy, mouth.frown, brow.sad, ...). It was built from rendered faces
    // judged by eye and lists only names seen in two or more unrelated avatars.
    internal sealed class ExpressionShapeNames
    {
        internal const string AssetName = "ExpressionShapeNames";
        static readonly string[] Emotions = { "happy", "angry", "sad", "relaxed", "surprised" };

        readonly Dictionary<string, string> parts = new Dictionary<string, string>(StringComparer.Ordinal);
        // Shapes below this weight (0-100) are a hint, not part of the face.
        internal float MinWeight { get; private set; } = 50f;
        internal int Count => parts.Count;

        internal static ExpressionShapeNames Parse(string json)
        {
            var table = new ExpressionShapeNames();
            if (string.IsNullOrWhiteSpace(json) || !(JsonDom.Parse(json) is Dictionary<string, object> root)) return table;
            if (root.TryGetValue("minWeight", out var minimum) && minimum != null)
                table.MinWeight = Convert.ToSingle(minimum, CultureInfo.InvariantCulture);
            if (!(root.TryGetValue("labels", out var labels) && labels is List<object> list)) return table;
            foreach (var item in list.OfType<Dictionary<string, object>>())
            {
                if (!(item.TryGetValue("label", out var label) && label is string part) || !(item.TryGetValue("names", out var names) && names is List<object> values)) continue;
                foreach (var name in values.OfType<string>())
                {
                    var key = Normalize(name);
                    // A name listed under two parts says neither.
                    if (key.Length == 0) continue;
                    if (table.parts.TryGetValue(key, out var existing) && existing != part) table.parts[key] = null;
                    else table.parts[key] = part;
                }
            }
            return table;
        }

#if !EXPORTER_BEHAVIOR_TESTS
        static ExpressionShapeNames shipped;

        // The table shipped next to this script; empty when it cannot be found.
        internal static ExpressionShapeNames Shipped
        {
            get
            {
                if (shipped != null) return shipped;
                foreach (var guid in AssetDatabase.FindAssets(AssetName + " t:TextAsset"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith("/Editor/" + AssetName + ".json", StringComparison.Ordinal)) continue;
                    var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                    if (asset != null) return shipped = Parse(asset.text);
                }
                return shipped = new ExpressionShapeNames();
            }
        }
#endif

        // NFKC, lower case, and runs of spaces, "_", "-", "." or "・" as one "_".
        internal static string Normalize(string name)
        {
            var text = (name ?? "").Normalize(NormalizationForm.FormKC).ToLowerInvariant().Trim();
            return Regex.Replace(text, @"[\s_\-\.・]+", "_").Trim('_');
        }

        // A one-side or numbered name ("21_eye_smile_l") reads through its
        // plain form when the exact name is not listed.
        static string Stripped(string normalized) =>
            Regex.Replace(Regex.Replace(normalized, @"^\d+_", ""), "_(l|r|left|right)$", "");

        internal string PartOf(string name)
        {
            var key = Normalize(name);
            if (parts.TryGetValue(key, out var part)) return part;
            if (parts.TryGetValue(Stripped(key), out part)) return part;
            // A Latin name that names one whole emotion (Fcl_ALL_Joy) is the whole face.
            var match = ExpressionNameInference.MatchEmotion(name, latinOnly: true);
            return match != null ? "whole." + match.Value.Preset : null;
        }

        // The emotion of a face from the shapes it moves and their weights
        // (0-100), with its support: 3 for a whole-face name, otherwise the
        // number of face parts that agree. Null when the parts do not name
        // exactly one emotion, when only one part moves, when the parts say
        // sad or angry without a whole-face name, or when the eyes are a
        // gimmick (hearts, stars, spirals).
        internal (ExpressionPreset Preset, int Support)? Classify(IEnumerable<(string Shape, float Weight)> shapes)
        {
            if (parts.Count == 0 || shapes == null) return null;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (shape, weight) in shapes)
            {
                if (weight < MinWeight) continue;
                var part = PartOf(shape);
                if (part != null) found.Add(part);
            }
            if (found.Contains("eye.gimmick")) return null;
            var whole = found.Where(part => part.StartsWith("whole.", StringComparison.Ordinal)).Select(part => part.Substring(6)).Distinct().ToArray();
            if (whole.Length > 1) return null;
            if (whole.Length == 1) return Preset(whole[0], 3);

            bool Has(string part) => found.Contains(part);
            var smile = Has("mouth.smile") || Has("mouth.open_smile");
            var negativeMouth = Has("mouth.frown") || Has("mouth.angry");
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (Has("eye.happy") && !negativeMouth && !Has("brow.angry")) result.Add("happy");
            if (smile && !negativeMouth && !Has("eye.angry") && !Has("eye.sad") && !Has("eye.wide") && !Has("eye.jito") && !Has("brow.angry") && !Has("brow.sad"))
                result.Add("happy");
            if (Has("eye.relaxed") && !negativeMouth && !Has("eye.happy") && !Has("mouth.open_smile") && !Has("mouth.o")) result.Add("relaxed");
            if (Has("brow.angry") && (Has("eye.angry") || negativeMouth)) result.Add("angry");
            if (Has("eye.angry") && negativeMouth) result.Add("angry");
            if (Has("brow.sad") && (Has("mouth.frown") || Has("eye.sad") || Has("tear"))) result.Add("sad");
            if (Has("eye.sad") && Has("mouth.frown")) result.Add("sad");
            if (Has("eye.wide")) result.Remove("sad");
            if (Has("eye.wide") && (Has("mouth.o") || Has("brow.up")) && !smile) result.Add("surprised");
            if (result.Contains("happy")) result.Remove("relaxed");
            // One part alone (a mouth toggle, an eye variant) is not a whole face.
            var kinds = found.Select(part => part.Split('.')[0]).Where(kind => kind == "eye" || kind == "mouth" || kind == "brow" || kind == "tear").Distinct().Count();
            if (kinds < 2 || result.Count != 1) return null;
            // Sad and angry part combinations never looked sad or angry in the
            // rendered faces the table was built from, so only a whole-face
            // name decides them. Their rules stay above to make mixed faces
            // ambiguous instead of happy.
            var emotion = result.First();
            if (emotion == "sad" || emotion == "angry") return null;
            return Preset(emotion, kinds);
        }

        static (ExpressionPreset Preset, int Support)? Preset(string name, int support) =>
            Emotions.Contains(name) && Enum.TryParse<ExpressionPreset>(name, out var preset) ? (preset, support) : ((ExpressionPreset, int)?)null;
    }
}
