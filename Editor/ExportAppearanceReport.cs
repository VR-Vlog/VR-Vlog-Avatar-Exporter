using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRVlog.LilToonExporter
{
    internal static class ExportAppearanceReport
    {
        // A note that changes how the avatar looks says one of these words in Japanese.
        static readonly string[] Words = { "省略", "近似", "縮小", "調整して", "自動除外せず保持" };

        static string patternsLocale;
        static Regex[] patterns;

        // A note is listed when it holds one of the words, as the many notes still written in
        // Japanese do, or contains, in the language of the Editor, a sentence of the locale table
        // whose Japanese holds one; in Japanese the table adds nothing. A word alone is not
        // translated: Korean and Chinese render it as a shorter word that other notes use too.
        // {0} or {name} stand for what is written into the sentence.
        static Regex[] Patterns()
        {
            var locale = ExporterLocalization.Locale;
            if (patterns != null && patternsLocale == locale) return patterns;
            var sentences = Words.Concat(ExporterLocalization.Sources()
                    .Where(source => !Words.Contains(source) && Words.Any(source.Contains))
                    .Select(ExporterLocalization.T))
                .Distinct(StringComparer.Ordinal);
            patterns = sentences.Select(text => Regex.Split(text, @"\{[^{}]*\}"))
                .Where(parts => parts.Any(part => part.Trim().Length > 0))
                .Select(parts => new Regex(string.Join(".*?", parts.Select(Regex.Escape)), RegexOptions.Singleline))
                .ToArray();
            patternsLocale = locale;
            return patterns;
        }

        internal static string[] Changes(IEnumerable<string> messages)
        {
            var found = Patterns();
            return (messages ?? Array.Empty<string>())
                .Where(message => !string.IsNullOrEmpty(message) && found.Any(pattern => pattern.IsMatch(message)))
                .Distinct().ToArray();
        }

        // The first ones, each cut to a line or two, are shown until all are asked for.
        internal const int Shown = 3;
        const int Longest = 110;

        // Whether the summary leaves something out until all are shown.
        internal static bool Shortened(IReadOnlyCollection<string> changes) => changes.Count > Shown || changes.Any(item => item.Length > Longest);

        internal static string Summary(IEnumerable<string> changes, bool all = false)
        {
            var items = changes.ToArray();
            var shown = all ? items.Length : Math.Min(Shown, items.Length);
            return string.Join("\n", items.Take(shown).Select(item => "・" + (all || item.Length <= Longest ? item : item.Substring(0, Longest - 3) + "…"))) +
                (items.Length > shown ? "\n" + ExporterLocalization.Format("ほか{0}件", items.Length - shown) : "");
        }
    }
}
