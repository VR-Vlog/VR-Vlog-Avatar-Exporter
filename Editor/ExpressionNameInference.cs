using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VRVlog.LilToonExporter.Vendor.UniVRM10;

namespace VRVlog.LilToonExporter
{
    // The single place that guesses a VRM expression from a name.
    // - Emotions (happy/angry/sad/relaxed/surprised): word vocabulary below.
    // - Mouth (aa/ih/ou/ee/oh): exact aliases in MouthShapes.
    // - Blink: exact names in BlinkShapeNames.
    // - Neutral: NeutralWords, under the same vetoes as the emotions.
    // - Gaze (lookUp/lookDown/lookLeft/lookRight): exact names in
    //   GazeShapes, either one shape or a pair moving both eyes.
    internal static class ExpressionNameInference
    {
        // Latin words must match a whole word (split at symbols, digits and
        // camelCase); Japanese words match inside the name. Tier 0 words name
        // the emotion itself and win over the looser tier 1 words.
        internal static readonly (ExpressionPreset Preset, int Tier, string[] Words)[] Emotions =
        {
            (ExpressionPreset.happy, 0, new[] { "happy", "happiness", "smile", "smiling", "grin", "笑顔", "えがお", "喜び", "嬉し", "うれし", "にこにこ", "ニコニコ", "にっこり", "スマイル", "ハッピー" }),
            (ExpressionPreset.angry, 0, new[] { "angry", "anger", "furious", "mad", "怒", "いかり", "イライラ", "ぷんぷん", "プンプン" }),
            (ExpressionPreset.sad, 0, new[] { "sad", "sadness", "sorrow", "unhappy", "悲", "哀", "かなし", "しょんぼり", "しょぼん" }),
            (ExpressionPreset.relaxed, 0, new[] { "relax", "relaxed", "calm", "fun", "peaceful", "楽", "和み", "なごみ", "穏", "リラックス", "ほっこり", "まったり" }),
            (ExpressionPreset.surprised, 0, new[] { "surprise", "surprised", "suprise", "shock", "shocked", "astonished", "驚", "びっくり", "ビックリ", "おどろ" }),
            (ExpressionPreset.happy, 1, new[] { "joy", "joyful", "laugh", "laughing", "glad", "cheerful", "笑", "喜", "にこ", "ニコ" }),
            (ExpressionPreset.angry, 1, new[] { "rage", "annoyed", "grumpy", "むっ", "ムッ" }),
            (ExpressionPreset.sad, 1, new[] { "cry", "crying", "tear", "tears", "weep", "泣", "涙", "なみだ" }),
            (ExpressionPreset.relaxed, 1, new[] { "chill", "gentle", "serene", "安らぎ", "やすらぎ" }),
            (ExpressionPreset.surprised, 1, new[] { "wow", "gasp", "startled", "ぎょっ", "ギョッ" }),
        };

        // A side, a part of the face, a wink/blink or a toggle is not a whole emotion.
        internal static readonly string[] VetoWords = { "eye", "eyes", "mouth", "brow", "eyebrow", "cheek", "wink", "blink", "left", "right", "l", "r",
            "off", "reset", "none", "default", "brw", "mth", "teeth", "tongue", "nose" };
        internal static readonly string[] VetoParts = { "目", "口", "眉", "頬", "ウィンク", "まばたき", "解除", "左", "右" };

        // Alias order expresses a documented preference (e.g. authored VRChat
        // visemes before a generic mouth shape). Never use substring matching.
        internal static readonly (string Preset, string[] Aliases)[] MouthShapes = {
            ("aa", new[] { "vrc.v.aa", "vrc.v_aa", "mouth_a", "Aa", "A", "Fcl_MTH_A" }),
            ("ih", new[] { "vrc.v.ih", "vrc.v_ih", "mouth_i", "Ih", "I", "Fcl_MTH_I" }),
            ("ou", new[] { "vrc.v.ou", "vrc.v_ou", "vrc.v.u", "vrc.v_u", "mouth_u", "Ou", "U", "Fcl_MTH_U" }),
            ("ee", new[] { "vrc.v.e", "vrc.v_e", "mouth_e", "Ee", "E", "Fcl_MTH_E" }),
            ("oh", new[] { "vrc.v.oh", "vrc.v_oh", "mouth_o", "Oh", "O", "Fcl_MTH_O" }),
        };

        internal static readonly string[] NeutralWords = { "neutral", "真顔", "無表情", "ニュートラル" };
        // A neutral name with one of these is a variant (eyes closed, half
        // open, asleep), not the resting face.
        internal static readonly string[] NeutralVetoWords = { "close", "closed", "half", "open", "sleep", "sleepy" };

        // Each entry is a set of shapes that together turn both eyes; the
        // first set found exactly (case-insensitive) fills the preset. Left
        // and right are the avatar's own, as in VRM and ARKit: looking left
        // turns the left eye out and the right eye in. A shape that moves one
        // eye alone never fills a preset.
        internal static readonly (string Preset, string[][] Sets)[] GazeShapes = {
            ("lookUp", new[] { new[] { "lookUp" }, new[] { "look_up" }, new[] { "eyeLookUp" }, new[] { "eye_look_up" }, new[] { "eyeLookUpLeft", "eyeLookUpRight" } }),
            ("lookDown", new[] { new[] { "lookDown" }, new[] { "look_down" }, new[] { "eyeLookDown" }, new[] { "eye_look_down" }, new[] { "eyeLookDownLeft", "eyeLookDownRight" } }),
            ("lookLeft", new[] { new[] { "lookLeft" }, new[] { "look_left" }, new[] { "eyeLookLeft" }, new[] { "eye_look_left" }, new[] { "eyeLookOutLeft", "eyeLookInRight" } }),
            ("lookRight", new[] { new[] { "lookRight" }, new[] { "look_right" }, new[] { "eyeLookRight" }, new[] { "eye_look_right" }, new[] { "eyeLookInLeft", "eyeLookOutRight" } }),
        };

        static string[] LatinWords(string name) => Regex.Matches(name ?? "", "[A-Z]+(?![a-z])|[A-Z]?[a-z]+|[0-9]+")
            .Cast<Match>().Select(match => match.Value.ToLowerInvariant()).ToArray();

        static bool IsLatin(string word) => word.All(character => character < 128);

        internal static (ExpressionPreset Preset, int Tier)? MatchEmotion(string name, bool latinOnly = false) =>
            Evaluate(name, latinOnly, out _);

        // A VRChat menu path ("Sub / Leaf"). The leaf label decides; parent
        // levels are read, nearest first, only while every level below says
        // nothing. A level with a veto word or several emotions stops the walk
        // with no match, so a "喜怒哀楽" or "Eye" submenu never decides alone.
        internal static (ExpressionPreset Preset, int Tier)? MatchMenuPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var levels = path.Split(new[] { " / " }, StringSplitOptions.None);
            for (var i = levels.Length - 1; i >= 0; i--)
            {
                var match = Evaluate(levels[i], false, out var decided);
                if (decided) return match;
            }
            return null;
        }

        // True for a menu path no level of which names one emotion, a face
        // part, a side or a toggle, and whose leaf names no emotion at all: an
        // item like "1" or "Face 3", maybe under a "喜怒哀楽" submenu. Only such
        // items may be guessed from the shapes they move.
        internal static bool SaysNothing(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var levels = path.Split(new[] { " / " }, StringSplitOptions.None);
            for (var i = 0; i < levels.Length; i++)
            {
                if (Evaluate(levels[i], false, out var decided) != null || Vetoed(levels[i])) return false;
                if (decided && i == levels.Length - 1) return false;
            }
            return true;
        }

        // True for a name that says neutral and nothing else: no emotion, no
        // face part, side or toggle. latinOnly as in MatchEmotion.
        internal static bool MatchNeutral(string name, bool latinOnly = false)
        {
            if (string.IsNullOrWhiteSpace(name) || Vetoed(name) || Evaluate(name, latinOnly, out _) != null) return false;
            var words = LatinWords(name);
            if (words.Any(NeutralVetoWords.Contains)) return false;
            return NeutralWords.Any(word => IsLatin(word) ? words.Contains(word) : !latinOnly && name.IndexOf(word, StringComparison.Ordinal) >= 0);
        }

        // A menu path names neutral only by its leaf, and only when no level
        // above it names an emotion, a face part, a side or a toggle.
        internal static bool MatchNeutralMenuPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var levels = path.Split(new[] { " / " }, StringSplitOptions.None);
            for (var i = 0; i < levels.Length - 1; i++)
                if (Vetoed(levels[i]) || Evaluate(levels[i], false, out _) != null) return false;
            return MatchNeutral(levels[levels.Length - 1]);
        }

        static bool Vetoed(string name) => !string.IsNullOrWhiteSpace(name) &&
            (LatinWords(name).Any(VetoWords.Contains) || VetoParts.Any(part => name.IndexOf(part, StringComparison.Ordinal) >= 0));

        // decided is false only when the name has neither a veto word nor any
        // emotion word, i.e. it says nothing about an emotion.
        static (ExpressionPreset Preset, int Tier)? Evaluate(string name, bool latinOnly, out bool decided)
        {
            decided = false;
            if (string.IsNullOrWhiteSpace(name)) return null;
            decided = true;
            if (Vetoed(name)) return null;
            var words = LatinWords(name);
            for (var tier = 0; tier <= 1; tier++)
            {
                var found = Emotions.Where(entry => entry.Tier == tier && entry.Words.Any(word => IsLatin(word)
                    ? words.Contains(word) : !latinOnly && name.IndexOf(word, StringComparison.Ordinal) >= 0)).Select(entry => entry.Preset).Distinct().ToArray();
                // Two emotions in the same tier say neither.
                if (found.Length == 1) return (found[0], tier);
                if (found.Length > 1) return null;
            }
            decided = false;
            return null;
        }

        // Index of the first alias found exactly (case-insensitive) in names,
        // -1 when none is present, -2 when the matching alias appears twice.
        internal static int FindExact(IList<object> names, string[] aliases)
        {
            foreach (var alias in aliases)
            {
                var found = -1;
                for (var i = 0; i < names.Count; i++)
                    if (names[i] is string name && string.Equals(name, alias, StringComparison.OrdinalIgnoreCase))
                    {
                        if (found >= 0) return -2;
                        found = i;
                    }
                if (found >= 0) return found;
            }
            return -1;
        }
    }
}
