using System;
using System.Collections.Generic;
using System.Linq;

namespace VRVlog.LilToonExporter
{
    internal static class ExportAppearanceReport
    {
        internal static string[] Changes(IEnumerable<string> messages) => (messages ?? Array.Empty<string>())
            .Where(message => !string.IsNullOrEmpty(message) &&
                (message.Contains("省略") || message.Contains("近似") || message.Contains("縮小") || message.Contains("調整して") || message.Contains("自動除外せず保持")))
            .Distinct().ToArray();

        internal static string Summary(IEnumerable<string> changes)
        {
            var items = changes.ToArray();
            return string.Join("\n", items.Take(3).Select(item => "・" + (item.Length <= 110 ? item : item.Substring(0, 107) + "…"))) +
                (items.Length > 3 ? ExporterLocalization.T("\nほか ") + (items.Length - 3) + ExporterLocalization.T(" 件。詳細から確認できます。") : "");
        }
    }
}
