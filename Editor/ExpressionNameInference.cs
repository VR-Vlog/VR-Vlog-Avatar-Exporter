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
        // Words are read in every name, raw BlendShape names included.
        // MenuWords (Japanese: kana, kanji and romaji such as "nagomi") only
        // in names an author gave a whole expression (menu items, clips),
        // because a raw Japanese shape name is usually an MMD-style part
        // morph (びっくり = eyes, にっこり = mouth). ASCII words must match a
        // whole word (split at symbols, digits and camelCase); other words
        // match inside the name, so a kana word that ends other words
        // (わらい in あざわらい) is not listed. Tier 0 words name the emotion
        // itself and win over the looser tier 1 words.
        internal static readonly (ExpressionPreset Preset, int Tier, string[] Words, string[] MenuWords)[] Emotions =
        {
            (ExpressionPreset.happy, 0, new[] { "happy", "happiness", "smile", "smiling", "grin", "grinning" },
                new[] { "笑顔", "えがお", "喜び", "嬉し", "うれし", "にこにこ", "ニコニコ", "にっこり", "ニッコリ", "スマイル", "ハッピー", "egao", "nikoniko", "nikkori", "ureshi", "ureshii" }),
            (ExpressionPreset.angry, 0, new[] { "angry", "anger", "furious", "mad" },
                new[] { "怒", "いかり", "イライラ", "ぷんぷん", "プンプン", "ikari", "okori", "punpun", "iraira" }),
            (ExpressionPreset.sad, 0, new[] { "sad", "sadness", "sorrow", "sorrowful", "unhappy" },
                new[] { "悲", "哀", "かなし", "しょんぼり", "しょぼん", "kanashii", "kanashimi", "shonbori", "shobon" }),
            (ExpressionPreset.relaxed, 0, new[] { "relax", "relaxed", "calm", "fun", "peaceful" },
                new[] { "楽", "和み", "なごみ", "ナゴミ", "穏", "リラックス", "ほっこり", "まったり", "nagomi", "hokkori", "mattari" }),
            (ExpressionPreset.surprised, 0, new[] { "surprise", "surprised", "suprise", "suprised", "surprize", "surprized", "shock", "shocked", "astonished", "amazed" },
                new[] { "驚", "びっくり", "ビックリ", "おどろ", "bikkuri", "odoroki" }),
            (ExpressionPreset.happy, 1, new[] { "joy", "joyful", "laugh", "laughing", "glad", "cheerful", "delighted", "pleased" },
                new[] { "笑", "喜", "にこ", "ニコ", "warai" }),
            (ExpressionPreset.angry, 1, new[] { "rage", "annoyed", "grumpy", "irritated", "irate" },
                new[] { "むっ", "ムッ" }),
            (ExpressionPreset.sad, 1, new[] { "cry", "crying", "weep", "sob", "sobbing", "gloomy" },
                new[] { "泣" }),
            (ExpressionPreset.relaxed, 1, new[] { "chill", "gentle", "serene", "relieved", "comfy", "cozy" },
                new[] { "安らぎ", "やすらぎ", "yasuragi" }),
            (ExpressionPreset.surprised, 1, new[] { "wow", "gasp", "startled", "stunned", "omg" },
                new[] { "ぎょっ", "ギョッ" }),
        };

        // A side, a part of the face or an effect on it (tears are a toggle
        // beside Cheek and Sweat), a wink/blink, a toggle, a hand sign or a
        // face that is none of the five emotions is not a whole emotion. Such
        // a name decides "no emotion" itself; a menu parent is never read for it.
        internal static readonly string[] VetoWords = { "eye", "eyes", "mouth", "brow", "eyebrow", "cheek", "wink", "blink", "left", "right", "l", "r",
            "off", "reset", "none", "default", "brw", "mth", "teeth", "tongue", "nose", "tear", "tears", "teary", "namida",
            "peace", "thumbup", "jitome", "doya", "doyagao", "kyoton", "gunu" };
        internal static readonly string[] VetoParts = { "目", "口", "眉", "頬", "涙", "なみだ", "ウィンク", "まばたき", "解除", "左", "右",
            "ジト", "ドヤ", "きょとん", "キョトン", "ぐぬ" };

        // Alias order expresses a documented preference (e.g. authored VRChat
        // visemes before a generic mouth shape). Never use substring matching.
        internal static readonly (string Preset, string[] Aliases)[] MouthShapes = {
            ("aa", new[] { "vrc.v.aa", "vrc.v_aa", "mouth_a", "Aa", "A", "Fcl_MTH_A" }),
            ("ih", new[] { "vrc.v.ih", "vrc.v_ih", "mouth_i", "Ih", "I", "Fcl_MTH_I" }),
            ("ou", new[] { "vrc.v.ou", "vrc.v_ou", "vrc.v.u", "vrc.v_u", "mouth_u", "Ou", "U", "Fcl_MTH_U" }),
            ("ee", new[] { "vrc.v.e", "vrc.v_e", "mouth_e", "Ee", "E", "Fcl_MTH_E" }),
            ("oh", new[] { "vrc.v.oh", "vrc.v_oh", "mouth_o", "Oh", "O", "Fcl_MTH_O" }),
        };

        // Split by source as in Emotions.
        internal static readonly (string[] Words, string[] MenuWords) NeutralWords = (new[] { "neutral" }, new[] { "真顔", "無表情", "ニュートラル" });
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

        // An ASCII word matches a whole word of the name, another word any part of it.
        static bool Says(string name, string[] nameWords, string word) => word.All(character => character < 128)
            ? nameWords.Contains(word) : name.IndexOf(word, StringComparison.Ordinal) >= 0;

        // rawShape reads only Words, not MenuWords: for raw BlendShape names.
        internal static (ExpressionPreset Preset, int Tier)? MatchEmotion(string name, bool rawShape = false) =>
            Evaluate(name, rawShape, out _);

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
        // face part, side or toggle. rawShape as in MatchEmotion.
        internal static bool MatchNeutral(string name, bool rawShape = false)
        {
            if (string.IsNullOrWhiteSpace(name) || Vetoed(name) || Evaluate(name, rawShape, out _) != null) return false;
            var words = LatinWords(name);
            if (words.Any(NeutralVetoWords.Contains)) return false;
            return NeutralWords.Words.Concat(rawShape ? Array.Empty<string>() : NeutralWords.MenuWords).Any(word => Says(name, words, word));
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
        static (ExpressionPreset Preset, int Tier)? Evaluate(string name, bool rawShape, out bool decided)
        {
            decided = false;
            if (string.IsNullOrWhiteSpace(name)) return null;
            decided = true;
            if (Vetoed(name)) return null;
            var words = LatinWords(name);
            for (var tier = 0; tier <= 1; tier++)
            {
                var found = Emotions.Where(entry => entry.Tier == tier && entry.Words.Concat(rawShape ? Array.Empty<string>() : entry.MenuWords)
                    .Any(word => Says(name, words, word))).Select(entry => entry.Preset).Distinct().ToArray();
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
