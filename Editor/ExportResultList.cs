using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VRVlog.LilToonExporter
{
    // One object or material a result line is about.
    internal sealed class ExportResultTarget
    {
        internal string Name;
        // Path from the avatar root; null when the item is not an object.
        internal string Path;
    }

    // One line of the result list. Entries with the same feature, outcome and
    // reason become one line; the objects concerned are listed when it opens.
    internal sealed class ExportResultRow
    {
        // Null for a notice that only exists as a warning sentence.
        internal ExportOutcome? Outcome;
        internal string Group;
        internal string Content;
        // Shown as "理由" when the line opens. Empty when Content already says it.
        internal string Reason = "";
        internal ExportStage? Stage;
        internal int Count;
        internal bool Blink;
        internal readonly List<ExportResultTarget> Targets = new List<ExportResultTarget>();
        internal readonly List<ExportRecoveryDiagnostic> Diagnostics = new List<ExportRecoveryDiagnostic>();

        // The target column: one name, or the number of items.
        internal string TargetLabel
        {
            get
            {
                if (Count > 1) return string.Format(ExporterLocalization.T(ExportResultList.CountFormat), Count);
                return Targets.Count == 1 ? Targets[0].Name : "";
            }
        }

        internal bool Matches(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return true;
            bool Has(string text) => !string.IsNullOrEmpty(text) && text.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
            return Has(Group) || Has(Content) || Has(Reason) || Targets.Any(target => Has(target.Name) || Has(target.Path));
        }
    }

    internal sealed class ExportResultGroup
    {
        internal string Name;
        internal readonly List<ExportResultRow> Rows = new List<ExportResultRow>();
    }

    // What the result window and the copied text show, built once from the
    // fallback report. Warnings that the report does not cover are kept as notices.
    internal sealed class ExportResultList
    {
        internal const string CountFormat = "{0}件";
        internal const string NoticeGroup = "そのほかの知らせ";
        internal const string StageOmittedContent = "処理に失敗したため省きました";
        internal const string StageApproximatedContent = "処理に失敗したため簡易な形にしました";

        // Most severe first. The tabs and each group follow this order.
        internal static readonly ExportOutcome[] Order =
            { ExportOutcome.Omitted, ExportOutcome.Approximated, ExportOutcome.Baked, ExportOutcome.Kept };

        internal readonly List<ExportResultGroup> Groups = new List<ExportResultGroup>();
        internal Dictionary<ExportOutcome, int> Summary = Order.ToDictionary(outcome => outcome, outcome => 0);
        internal int NoticeCount;
        internal string Destination = "";
        internal string FileName => string.IsNullOrEmpty(Destination) ? "" : System.IO.Path.GetFileName(Destination);

        internal IEnumerable<ExportResultRow> Rows => Groups.SelectMany(group => group.Rows);
        internal bool IsEmpty => Groups.Count == 0;

        internal static string Label(ExportOutcome outcome)
        {
            switch (outcome)
            {
                case ExportOutcome.Omitted: return "省略";
                case ExportOutcome.Approximated: return "近似";
                case ExportOutcome.Baked: return "焼き込み";
                default: return "保持";
            }
        }

        internal static string Label(ExportOutcome? outcome) => outcome.HasValue ? Label(outcome.Value) : "参考";

        static int Rank(ExportOutcome? outcome) => outcome.HasValue ? Array.IndexOf(Order, outcome.Value) : Order.Length;

        // The sentence UniVrmOneClickExporter adds to the warnings for a left-out
        // stage. A notice with this exact text is the same item as the stage line.
        static string StageWarning(ExportResultEntry entry) =>
            ExporterLocalization.T(ExportFallbackConfirmation.Failure(ExportFallbackKind.StageOmitted)) + ": " +
            ExporterLocalization.T(entry.Category) + " — " + entry.Reason;

        // "happy: ..." already shows "happy" in the target column.
        static string WithoutName(string reason, string name) =>
            !string.IsNullOrEmpty(name) && reason.StartsWith(name + ": ", StringComparison.Ordinal) ? reason.Substring(name.Length + 2) : reason;

        internal static ExportResultList Build(ExportFallbackReport report, IEnumerable<string> warnings, string destination = null)
        {
            var list = new ExportResultList { Destination = destination ?? "" };
            var entries = report?.Results ?? (IReadOnlyList<ExportResultEntry>)Array.Empty<ExportResultEntry>();
            if (report != null) list.Summary = report.Summary();
            var known = new HashSet<string>();
            var rows = new List<ExportResultRow>();
            foreach (var entry in entries)
            {
                // Default reasons are recorded as Japanese source text; T returns other text unchanged.
                var reason = ExporterLocalization.T(entry.Reason);
                known.Add(entry.Reason);
                known.Add(reason);
                if (entry.Stage != null) known.Add(StageWarning(entry));
                var group = ExporterLocalization.T(entry.Category);
                var content = entry.Stage == null ? WithoutName(reason, entry.Name)
                    : ExporterLocalization.T(entry.Outcome == ExportOutcome.Omitted ? StageOmittedContent : StageApproximatedContent);
                var row = rows.FirstOrDefault(existing => existing.Stage == entry.Stage && existing.Outcome == entry.Outcome &&
                    existing.Group == group && existing.Content == content);
                if (row == null)
                {
                    rows.Add(row = new ExportResultRow { Outcome = entry.Outcome, Group = group, Content = content, Stage = entry.Stage });
                    if (entry.Stage != null) row.Reason = entry.Reason;
                }
                row.Count += entry.Count;
                // Entries recorded without a name use the path as the name; show only its last part there.
                var targetName = entry.Name == entry.Path && entry.Path != null ? entry.Path.Substring(entry.Path.LastIndexOf('/') + 1) : entry.Name;
                if (!string.IsNullOrEmpty(targetName) && !row.Targets.Any(target => target.Name == targetName && target.Path == entry.Path))
                    row.Targets.Add(new ExportResultTarget { Name = targetName, Path = entry.Path });
                row.Diagnostics.AddRange(entry.Diagnostics);
                if (entry.Stage == ExportStage.BlinkCapture || entry.Stage == ExportStage.BlinkOutput ||
                    entry.Stage == null && entry.Category == ExportFallbackConfirmation.Feature(ExportFallbackKind.Blink))
                    row.Blink = true;
            }
            // Several targets of one line count once per target, not per entry.
            foreach (var row in rows) row.Count = Math.Max(row.Count, row.Targets.Count);
            foreach (var warning in warnings ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(warning) || known.Contains(warning) || rows.Any(row => row.Outcome == null && row.Content == warning)) continue;
                rows.Add(new ExportResultRow { Outcome = null, Group = ExporterLocalization.T(NoticeGroup), Content = warning, Count = 1 });
                list.NoticeCount++;
            }
            // Groups appear by their most severe line, then in the order the
            // export met them. Within a group the most severe lines come first.
            var order = rows.Select(row => row.Group).Distinct().ToList();
            foreach (var name in order.OrderBy(name => rows.Where(row => row.Group == name).Min(row => Rank(row.Outcome))).ThenBy(order.IndexOf))
            {
                var group = new ExportResultGroup { Name = name };
                group.Rows.AddRange(rows.Where(row => row.Group == name).OrderBy(row => Rank(row.Outcome)));
                list.Groups.Add(group);
            }
            return list;
        }

        // "省略2・近似1" for a group header, most severe first.
        internal static string Counts(IEnumerable<ExportResultRow> rows)
        {
            var items = rows.ToArray();
            return string.Join("・", Order.Select(outcome => (outcome, count: items.Where(row => row.Outcome == outcome).Sum(row => row.Count)))
                .Where(pair => pair.count > 0).Select(pair => ExporterLocalization.T(Label(pair.outcome)) + pair.count)
                .Concat(items.Any(row => row.Outcome == null)
                    ? new[] { ExporterLocalization.T(Label((ExportOutcome?)null)) + items.Count(row => row.Outcome == null) }
                    : Array.Empty<string>()));
        }

        // Plain text for support requests. Every line, target and reason is kept.
        internal string Text(string version = null)
        {
            var text = new StringBuilder();
            text.Append(ExporterLocalization.T("VR Vlog 書き出し結果"));
            if (!string.IsNullOrEmpty(FileName)) text.Append(' ').Append(FileName);
            if (!string.IsNullOrEmpty(version)) text.Append('（').Append(version).Append('）');
            text.AppendLine();
            text.AppendLine(string.Join("・", Order.Select(outcome => ExporterLocalization.T(Label(outcome)) + Summary[outcome])
                .Concat(NoticeCount > 0 ? new[] { ExporterLocalization.T(Label((ExportOutcome?)null)) + NoticeCount } : Array.Empty<string>())));
            foreach (var group in Groups)
            {
                text.Append('[').Append(group.Name).AppendLine("]");
                foreach (var row in group.Rows)
                {
                    text.Append(ExporterLocalization.T(Label(row.Outcome))).Append(' ').Append(row.Content);
                    if (row.Targets.Count > 0)
                        text.Append(": ").Append(string.Join(", ", row.Targets.Select(target => target.Name)));
                    if (row.Count > Math.Max(1, row.Targets.Count))
                        text.Append(" (").Append(string.Format(ExporterLocalization.T(CountFormat), row.Count)).Append(')');
                    if (!string.IsNullOrEmpty(row.Reason)) text.Append(" — ").Append(row.Reason);
                    text.AppendLine();
                    foreach (var issue in row.Diagnostics)
                    {
                        if (issue == null) continue;
                        text.Append("  ").Append(ExporterLocalization.T("次にすること: ")).AppendLine(ExporterLocalization.T(issue.Remedy));
                    }
                }
            }
            return text.ToString();
        }
    }
}
