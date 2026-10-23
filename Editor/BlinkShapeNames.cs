using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRVlog.LilToonExporter
{
    // Exact semantic names only. "eye_close" can mean eye spacing, as in Plum.
    internal static class BlinkShapeNames
    {
        internal const int PartialPair = -3;
        internal static readonly string[] Presets = { "blink", "blinkLeft", "blinkRight" };
        static readonly string[] Both = { "vrc.Blink", "Blink", "Fcl_EYE_Close", "まばたき", "eye_blink_1", "eye_blink_2" };
        static readonly (string Left, string Right)[] Pairs = {
            ("Blink_L", "Blink_R"), ("BlinkLeft", "BlinkRight"),
            ("EyeBlinkLeft", "EyeBlinkRight"), ("Fcl_EYE_Close_L", "Fcl_EYE_Close_R"),
            ("eye_blink_1_L", "eye_blink_1_R"), ("eye_blink_2_L", "eye_blink_2_R")
        };
        // Consulted only when no name above and no Unified Expressions closure
        // matched, so avatars that already resolved keep their exact choice.
        static readonly string[] LateBoth = { "eyelid_Blink" };
        static readonly (string Left, string Right)[] LatePairs = {
            ("eyelid_Blink.L", "eyelid_Blink.R"), ("vrc.blink_left", "vrc.blink_right")
        };

        // Name fallbacks below are consulted only after every rule above left
        // the eye slots empty. Each accepts one unambiguous reading or none.
        static readonly (string Left, string Right, bool Joined)[] SideMarkers = {
            ("L", "R", false), ("Left", "Right", true), ("左", "右", true)
        };
        static readonly string[] SideSeparators = { "_", ".", " ", "-", "" };

        // The left and right forms of a bilateral closing name, such as
        // X_L / X_R, Left_X / Right_X or X左 / X右, when exactly one exists.
        internal static (int Left, int Right) DerivedSides(IReadOnlyList<string> names, string both)
        {
            var found = (Left: -1, Right: -1);
            if (string.IsNullOrEmpty(both)) return found;
            foreach (var marker in SideMarkers)
                foreach (var separator in SideSeparators)
                {
                    // A bare L or R would read as part of a word.
                    if (separator.Length == 0 && !marker.Joined) continue;
                    foreach (var form in new[] { (both + separator + marker.Left, both + separator + marker.Right),
                        (marker.Left + separator + both, marker.Right + separator + both) })
                    {
                        var left = Unique(names, form.Item1); var right = Unique(names, form.Item2);
                        if (left == -2 || right == -2) return (-1, -1);
                        if (left < 0 || right < 0) continue;
                        if (found.Left >= 0 && (found.Left != left || found.Right != right)) return (-1, -1);
                        found = (left, right);
                    }
                }
            return found;
        }

        internal const int NotClosing = 0, ClosingBoth = 1, ClosingLeft = 2, ClosingRight = 3;
        static readonly Regex SideSuffix = new Regex(@"^(?<root>.+?)(?:[ ._-]?(?<side>left|right|左|右)目?|[ ._-](?<side>l|r))$", RegexOptions.IgnoreCase);
        static readonly Regex SidePrefix = new Regex(@"^(?:(?<side>left|right|左|右)(?<eye>目)?[ ._-]?|(?<side>l|r)[ ._-])(?<root>.+)$", RegexOptions.IgnoreCase);
        static readonly Regex Words = new Regex(@"[ ._-]+|(?<=[a-z])(?=[A-Z])");
        static readonly HashSet<string> EyeWords = new HashSet<string> { "eye", "eyes", "eyelid", "eyelids", "both", "face", "fcl", "vrc" };
        static readonly HashSet<string> JapaneseClosings = new HashSet<string> { "まばたき", "瞬き", "目閉じ", "目とじ", "閉じ目", "閉眼", "両目閉じ" };

        // A name made only of a blink word, eye words and at most one side
        // marker. Anything else (smile, wink, half, preview, a number) is not
        // read as a blink. Root is the name without its side marker.
        internal static int NamedClosure(string name, out string root)
        {
            root = name ?? "";
            var side = NotClosing;
            var match = SideSuffix.Match(root);
            if (!match.Success) match = SidePrefix.Match(root);
            if (match.Success)
            {
                var marker = match.Groups["side"].Value.ToLowerInvariant();
                side = marker == "l" || marker == "left" || marker == "左" ? ClosingLeft : ClosingRight;
                // 左目閉じ reads as 左 + 目閉じ.
                root = match.Groups["eye"].Value + match.Groups["root"].Value;
            }
            if (JapaneseClosings.Contains(Regex.Replace(root, @"[ ._-]", "")))
                return side == NotClosing ? ClosingBoth : side;
            var words = Words.Split(root).Where(word => word.Length > 0).Select(word => word.ToLowerInvariant()).ToArray();
            if (words.Any(word => !Regex.IsMatch(word, "^[a-z]+$"))) return NotClosing;
            var rest = words.Where(word => !EyeWords.Contains(word)).ToArray();
            if (rest.Length != 1 || rest[0] != "blink") return NotClosing;
            return side == NotClosing ? ClosingBoth : side;
        }

        internal static int PartialFamily(string shape, bool left)
        {
            for (var index = 0; index < Pairs.Length; index++)
                if (string.Equals(shape, left ? Pairs[index].Left : Pairs[index].Right, StringComparison.OrdinalIgnoreCase)) return index;
            if (VRVlog.FaceTracking.UnifiedExpressionRegistry.TryCanonicalize(shape, out var canonical) &&
                canonical == (left ? "EyeClosedLeft" : "EyeClosedRight")) return Pairs.Length;
            return -1;
        }

        internal static bool CompatiblePartials(string left, string right) =>
            PartialFamily(left, true) >= 0 && PartialFamily(left, true) == PartialFamily(right, false);

        // Automatic ownership includes coexisting exact semantic families.
        // Resolve still chooses one family with its established preset priority.
        internal static IEnumerable<int> AutomaticCandidates(IReadOnlyList<string> names)
        {
            var seen = new HashSet<int>();
            foreach (var name in Both)
            {
                var index = Unique(names, name);
                if (index >= 0 && seen.Add(index)) yield return index;
            }
            foreach (var pair in Pairs)
                foreach (var name in new[] { pair.Left, pair.Right })
                {
                    var index = Unique(names, name);
                    if (index >= 0 && seen.Add(index)) yield return index;
                }
            var unified = UnifiedCandidates(names, null);
            foreach (var canonical in new[] { "EyeClosed", "EyeClosedLeft", "EyeClosedRight" })
                if (unified.TryGetValue(canonical, out var index) && seen.Add(index)) yield return index;
            foreach (var name in LateBoth)
            {
                var index = Unique(names, name);
                if (index >= 0 && seen.Add(index)) yield return index;
            }
            foreach (var pair in LatePairs)
                foreach (var name in new[] { pair.Left, pair.Right })
                {
                    var index = Unique(names, name);
                    if (index >= 0 && seen.Add(index)) yield return index;
                }
        }

        internal static int Unique(IReadOnlyList<string> names, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            var result = -1;
            for (var i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, comparison))
                {
                    if (result >= 0) return -2;
                    result = i;
                }
            return result;
        }

        static Dictionary<string, int> UnifiedCandidates(IReadOnlyList<string> names, Func<int, bool> usable)
        {
            var unified = VrmUnifiedExpressions.Resolve(names);
            // Resolve aliases first: an inert preferred route must not
            // select a lower-priority alias that detailed tracking omits.
            if (usable != null)
                foreach (var canonical in new[] { "EyeClosed", "EyeClosedLeft", "EyeClosedRight" })
                    if (unified.TryGetValue(canonical, out var index) && !usable(index)) unified.Remove(canonical);
            return unified;
        }

        internal static IEnumerable<(int Index, int Slot)> PartialCandidates(IReadOnlyList<string> names, Func<int, bool> unifiedUsable = null)
        {
            var unified = UnifiedCandidates(names, unifiedUsable);
            var hasLeft = unified.TryGetValue("EyeClosedLeft", out var unifiedLeft);
            var hasRight = unified.TryGetValue("EyeClosedRight", out var unifiedRight);
            // A UE side retains its priority over alternate legacy closures on
            // this renderer. Complete pairs are handled by Resolve first.
            if (hasLeft || hasRight)
            {
                if (hasLeft) yield return (unifiedLeft, 1);
                if (hasRight) yield return (unifiedRight, 2);
                yield break;
            }
            foreach (var pair in Pairs)
            {
                var left = Unique(names, pair.Left); var right = Unique(names, pair.Right);
                if (left >= 0 && right >= 0) continue;
                if (left >= 0) yield return (left, 1);
                if (right >= 0) yield return (right, 2);
            }
        }

        internal static int[] Resolve(IReadOnlyList<string> names, bool allowPartial = false, Func<int, bool> unifiedUsable = null)
        {
            var result = new[] { -1, -1, -1 };
            foreach (var name in Both)
            {
                var index = Unique(names, name);
                if (index == -2) return new[] { -2, -2, -2 };
                if (index < 0) continue;
                result[0] = index;
                break;
            }
            var partial = false;
            var partialLeft = -1;
            var partialRight = -1;
            foreach (var pair in Pairs)
            {
                var left = Unique(names, pair.Left);
                var right = Unique(names, pair.Right);
                if (left == -2 || right == -2) return new[] { -2, -2, -2 };
                if (left < 0 || right < 0)
                {
                    partial |= left >= 0 || right >= 0;
                    if (allowPartial && partialLeft < 0 && partialRight < 0 && (left >= 0 || right >= 0))
                    { partialLeft = left; partialRight = right; }
                    continue;
                }
                result[1] = left;
                result[2] = right;
                break;
            }
            if (result[1] < 0 && result[2] < 0)
            {
                // Use the same bounded normalization and collision rules as
                // UE export, including prefixed names and the bilateral form.
                var unified = UnifiedCandidates(names, unifiedUsable);
                if (result[0] < 0 && unified.TryGetValue("EyeClosed", out var both)) result[0] = both;
                var hasLeft = unified.TryGetValue("EyeClosedLeft", out var left);
                var hasRight = unified.TryGetValue("EyeClosedRight", out var right);
                if (hasLeft && hasRight) { result[1] = left; result[2] = right; }
                else
                {
                    partial |= hasLeft || hasRight;
                    // A usable UE partial must agree with the channel exported
                    // for detailed tracking before another renderer supplies
                    // the other eye. Complete legacy pairs still win above.
                    if (allowPartial && (hasLeft || hasRight))
                    { partialLeft = hasLeft ? left : -1; partialRight = hasRight ? right : -1; }
                }
            }
            if (!partial && result[0] < 0 && result[1] < 0 && result[2] < 0)
            {
                foreach (var name in LateBoth)
                {
                    var index = Unique(names, name);
                    if (index == -2) return new[] { -2, -2, -2 };
                    if (index >= 0) { result[0] = index; break; }
                }
                foreach (var pair in LatePairs)
                {
                    var left = Unique(names, pair.Left); var right = Unique(names, pair.Right);
                    if (left == -2 || right == -2) return new[] { -2, -2, -2 };
                    if (left >= 0 && right >= 0) { result[1] = left; result[2] = right; break; }
                }
            }
            if (allowPartial && result[1] < 0 && result[2] < 0) { result[1] = partialLeft; result[2] = partialRight; }
            if (!allowPartial && partial && result[1] < 0) result[1] = result[2] = PartialPair;
            return result;
        }
    }
}
