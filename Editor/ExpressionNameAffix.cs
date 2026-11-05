using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    // Custom expression names often carry the same prefix or suffix, e.g.
    // "face_smile" and "face_sad" or "smile_L" and "sad_L". When every name
    // has one, it is left out of the written names. Only whole words go:
    // the prefix ends and the suffix begins at a separator (_ - . space).
    internal static class ExpressionNameAffix
    {
        static bool IsSeparator(char c) => c == '_' || c == '-' || c == '.' || c == ' ';

        // The names to write, in order. Two or more distinct names are
        // needed. Of the shared prefixes and suffixes that leave every name
        // valid, the pair that removes the most is taken (the longer prefix
        // on a tie). A name is not valid when it would be empty, digits only
        // (any script's digits, e.g. full-width),
        // separators only, the same as another result whose original was
        // different, or reserved (taken by the caller: a preset, a face
        // tracking name or a name already in the file).
        internal static IReadOnlyList<string> Trim(IReadOnlyList<string> names, Func<string, bool> reserved = null)
        {
            if (names == null || names.Any(name => name == null)) return names;
            var distinct = names.Distinct(StringComparer.Ordinal).Count();
            if (distinct < 2) return names;
            var shortest = names.Min(name => name.Length);
            var best = names;
            var removed = 0;
            foreach (var prefix in Prefixes(names).Append(0))
                foreach (var suffix in Suffixes(names).Append(0))
                {
                    if (prefix + suffix <= removed || prefix + suffix >= shortest) continue;
                    var result = names.Select(name => name.Substring(prefix, name.Length - prefix - suffix)).ToArray();
                    if (result.Any(name => name.All(IsSeparator) || name.All(char.IsDigit))) continue;
                    if (result.Distinct(StringComparer.Ordinal).Count() != distinct) continue;
                    if (reserved != null && result.Any(reserved)) continue;
                    best = result;
                    removed = prefix + suffix;
                }
            return best;
        }

        // Lengths of the shared starts that end with a separator and hold a
        // word, longest first.
        static IEnumerable<int> Prefixes(IReadOnlyList<string> names)
        {
            var first = names[0];
            var common = 0;
            while (common < first.Length && names.All(name => common < name.Length && name[common] == first[common])) common++;
            for (var length = common; length > 0; length--)
                if (IsSeparator(first[length - 1]) && !first.Take(length).All(IsSeparator)) yield return length;
        }

        static IEnumerable<int> Suffixes(IReadOnlyList<string> names)
        {
            var first = names[0];
            var common = 0;
            while (common < first.Length && names.All(name => common < name.Length &&
                name[name.Length - 1 - common] == first[first.Length - 1 - common])) common++;
            for (var length = common; length > 0; length--)
                if (IsSeparator(first[first.Length - length]) && !first.Skip(first.Length - length).All(IsSeparator)) yield return length;
        }
    }
}
