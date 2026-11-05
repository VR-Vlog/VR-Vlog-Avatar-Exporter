using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    internal static class ExportAppearanceReport
    {
        // Most notes are written in Japanese whatever the language of the Editor, and these words find them.
        static readonly string[] Words = { "省略", "近似", "縮小", "調整して", "自動除外せず保持" };

        // The notes that contain one of the words but are written in the language of the Editor, so in
        // English, Korean or Chinese the words above do not find them. "縮小" is how the four downscale
        // notes (ExportNoteText.*Resized) read in every language; the others are the fixed sentence the
        // note starts with.
        static readonly string[] Translated =
        {
            "縮小", "書き出しの一部を省略しました", NdmfExportPreparation.LostGeneratedMorphs,
            UnsupportedShaderSubstitution.Warning, UnsupportedShaderSubstitution.Failed,
        };

        internal static string[] Changes(IEnumerable<string> messages) => (messages ?? Array.Empty<string>())
            .Where(message => !string.IsNullOrEmpty(message) &&
                (Words.Any(word => message.Contains(word)) || Translated.Any(text => message.Contains(ExporterLocalization.T(text)))))
            .Distinct().ToArray();

        internal static string Summary(IEnumerable<string> changes)
        {
            var items = changes.ToArray();
            return string.Join("\n", items.Take(3).Select(item => "・" + (item.Length <= 110 ? item : item.Substring(0, 107) + "…"))) +
                (items.Length > 3 ? ExporterLocalization.T("\nほか ") + (items.Length - 3) + ExporterLocalization.T(" 件。詳細から確認できます。") : "");
        }
    }
}
