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

        internal static string Summary(IEnumerable<string> changes)
        {
            var items = changes.ToArray();
            return string.Join("\n", items.Take(3).Select(item => "・" + (item.Length <= 110 ? item : item.Substring(0, 107) + "…"))) +
                (items.Length > 3 ? ExporterLocalization.T("\nほか ") + (items.Length - 3) + ExporterLocalization.T(" 件。詳細から確認できます。") : "");
        }
    }
}
