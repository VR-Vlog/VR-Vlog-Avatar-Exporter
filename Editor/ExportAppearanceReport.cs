using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRVlog.LilToonExporter
{
    internal static class ExportAppearanceReport
    {
        // Most notes are written in Japanese whatever the language of the Editor, and these words find them.
        static readonly string[] Words = { "省略", "近似", "縮小", "調整して", "自動除外せず保持" };

        // The four downscale notes (ExportNoteText.*Resized) are written in the language of the Editor,
        // and "縮小" is how they read in every language.
        const string Downscaled = "縮小";

        // The other notes that contain one of the words but are written in the language of the Editor,
        // so in English, Korean or Chinese the words above do not find them. Each starts with one of
        // these sentences, where {0}, {1} stand for the names written into it.
        static readonly string[] TranslatedStarts =
        {
            UniVrmOneClickExporter.StageOmittedWarning, NdmfExportPreparation.LostGeneratedMorphs,
            UnsupportedShaderSubstitution.Warning, UnsupportedShaderSubstitution.Failed,
            UniVrmOneClickExporter.LostMorphOmittedWarning, UniVrmOneClickExporter.LostExpressionOmittedWarning,
            UniVrmOneClickExporter.LostTrackingWarning, VrmExpressionBindings.DuplicateExpressionFormat,
        };

        // Reasons written after " — " at the end of a note whose own sentence holds none of the words.
        static readonly string[] TranslatedReasons = { UniVrmOneClickExporter.ApproximatedLilToonReason };

        internal static string[] Changes(IEnumerable<string> messages)
        {
            var downscaled = ExporterLocalization.T(Downscaled);
            var starts = TranslatedStarts.Select(text => new Regex("^" + string.Join(".*?",
                Regex.Split(ExporterLocalization.T(text), @"\{\d+\}").Select(Regex.Escape)), RegexOptions.Singleline)).ToArray();
            var reasons = TranslatedReasons.Select(text => " — " + ExporterLocalization.T(text)).ToArray();
            return (messages ?? Array.Empty<string>())
                .Where(message => !string.IsNullOrEmpty(message) &&
                    (Words.Any(word => message.Contains(word)) || message.Contains(downscaled) ||
                     starts.Any(start => start.IsMatch(message)) ||
                     reasons.Any(reason => message.EndsWith(reason, StringComparison.Ordinal))))
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
