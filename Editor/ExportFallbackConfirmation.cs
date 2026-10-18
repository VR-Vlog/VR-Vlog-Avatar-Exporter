using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal enum ExportFallbackKind
    {
        // Automatic blink found no closing shape and exported without blink.
        Blink,
        // A plane collider lost its Inside Bounds flag.
        PlaneInsideBounds,
        // A collider of an unsupported type or shape was left out.
        ColliderSkipped,
        // No lilToon material; other materials became color-and-image MToon.
        NonLilToonMaterials,
        // A stage failed and was left out; the export continued without it.
        // Names are the stages (ExportStageGuard.Label).
        StageOmitted,
        // More items than the app accepts; the rest were left out.
        LimitExceeded,
        // A material could not be converted and became color-and-image MToon.
        MaterialApproximated
    }

    // One line of the export result: what happened to which item, and why.
    internal sealed class ExportResultEntry
    {
        internal ExportOutcome Outcome;
        // Short feature name shown in the result list (for example "揺れ物").
        internal string Category;
        // The object, material or stage concerned. Empty for whole features.
        internal string Name;
        internal string Reason;
        // Set for entries made by the stage guard.
        internal ExportStage? Stage;
        // Recovery diagnostics for a left-out stage (remedies the user can apply).
        internal readonly List<ExportRecoveryDiagnostic> Diagnostics = new List<ExportRecoveryDiagnostic>();
        internal int Count = 1;
    }

    internal sealed class ExportFallback
    {
        internal ExportFallbackKind Kind;
        // Names shown under the sentence (collider names). Empty for kinds without one.
        internal readonly List<string> Names = new List<string>();
        // Transform paths relative to the avatar root. The export copy is destroyed
        // before the dialog, so the avatar's own objects are found again by path.
        internal readonly List<string> Paths = new List<string>();
    }

    // Filled once while exporting. The confirmation reads it; nothing is judged twice.
    internal sealed class ExportFallbackReport
    {
        readonly List<ExportFallback> items = new List<ExportFallback>();
        readonly List<ExportResultEntry> results = new List<ExportResultEntry>();
        internal IReadOnlyList<ExportFallback> Items => items;
        // Every kept/baked/approximated/omitted item, for the result list.
        internal IReadOnlyList<ExportResultEntry> Results => results;
        internal int Count => items.Count;
        internal void Clear() { items.Clear(); results.Clear(); }

        // Number of affected items per outcome. Outcomes without items are 0.
        internal Dictionary<ExportOutcome, int> Summary()
        {
            var summary = ((ExportOutcome[])Enum.GetValues(typeof(ExportOutcome))).ToDictionary(outcome => outcome, outcome => 0);
            foreach (var entry in results) summary[entry.Outcome] += entry.Count;
            return summary;
        }

        internal ExportResultEntry Record(ExportOutcome outcome, string category, string name = null, string reason = null, int count = 1)
        {
            var entry = new ExportResultEntry { Outcome = outcome, Category = category, Name = name ?? "", Reason = reason ?? "", Count = count };
            results.Add(entry);
            return entry;
        }

        internal static ExportOutcome Outcome(ExportFallbackKind kind)
        {
            switch (kind)
            {
                case ExportFallbackKind.Blink:
                case ExportFallbackKind.ColliderSkipped:
                case ExportFallbackKind.LimitExceeded:
                    return ExportOutcome.Omitted;
                default:
                    return ExportOutcome.Approximated;
            }
        }

        internal void Add(ExportFallbackKind kind, string name = null, Transform root = null, Transform target = null, string reason = null)
        {
            // StageOmitted entries come with their own result line (AddStage).
            if (kind != ExportFallbackKind.StageOmitted)
            {
                var category = ExportFallbackConfirmation.Feature(kind);
                var label = name ?? RelativePath(root, target) ?? "";
                if (!results.Any(entry => entry.Stage == null && entry.Category == category && entry.Name == label))
                    Record(Outcome(kind), category, label, reason ?? ExportFallbackConfirmation.Failure(kind));
            }
            var item = items.FirstOrDefault(existing => existing.Kind == kind);
            if (item == null) items.Add(item = new ExportFallback { Kind = kind });
            if (!string.IsNullOrEmpty(name) && !item.Names.Contains(name)) item.Names.Add(name);
            var path = RelativePath(root, target);
            if (path != null && !item.Paths.Contains(path)) item.Paths.Add(path);
        }

        // A stage the guard left out: one dialog line plus one result line that
        // keeps the cause and the recovery remedies found for it.
        internal ExportResultEntry AddStage(ExportStage stage, string reason, IEnumerable<ExportRecoveryDiagnostic> diagnostics = null)
        {
            var label = ExportStageGuard.Label(stage);
            Add(ExportFallbackKind.StageOmitted, ExporterLocalization.T(label));
            var entry = Record(ExportStageGuard.Outcome(stage), label, "", reason);
            entry.Stage = stage;
            if (diagnostics != null) entry.Diagnostics.AddRange(diagnostics);
            return entry;
        }

        internal static string RelativePath(Transform root, Transform target)
        {
            if (root == null || target == null) return null;
            var names = new List<string>();
            for (var current = target; current != root; current = current.parent)
            {
                if (current == null) return null;
                names.Add(current.name);
            }
            names.Reverse();
            return string.Join("/", names);
        }
    }

    internal interface IExportFallbackDialog
    {
        // True continues the export, false stops it to change settings.
        bool Confirm(string title, string message, string export, string changeSettings);
    }

    internal static class ExportFallbackConfirmation
    {
        internal const string Title = "自動設定できなかった項目があります";
        internal const string ExportButton = "書き出す";
        internal const string ChangeSettingsButton = "設定を変更する";
        internal const string Question = "{0}が使用できない状態でも書き出しますか？";
        internal const string QuestionForSeveral = "これらの機能が使用できない状態でも書き出しますか？";
        internal const string TargetFormat = "対象: {0}";
        internal const string MoreFormat = "{0} ほか{1}件";
        // The native dialog wraps at about 26 full-width characters. Every line
        // stays below that so no sentence breaks in the middle.
        const int ShownNames = 2;

        sealed class EditorDialog : IExportFallbackDialog
        {
            public bool Confirm(string title, string message, string export, string changeSettings) =>
                EditorUtility.DisplayDialog(title, message, export, changeSettings);
        }

        // Tests replace these two; batch runs and CI never reach the dialog.
        internal static IExportFallbackDialog Dialog = new EditorDialog();
        internal static Func<bool> IsBatchMode = () => Application.isBatchMode;

        internal static string Failure(ExportFallbackKind kind)
        {
            switch (kind)
            {
                case ExportFallbackKind.Blink: return "瞬きの表情の自動設定ができませんでした";
                case ExportFallbackKind.PlaneInsideBounds: return "平面のInside Boundsを変換できませんでした";
                case ExportFallbackKind.ColliderSkipped: return "PhysBoneコライダーを変換できませんでした";
                case ExportFallbackKind.StageOmitted: return "書き出しの一部を省略しました";
                case ExportFallbackKind.LimitExceeded: return "上限を超えた分を省略しました";
                case ExportFallbackKind.MaterialApproximated: return "一部の材質を簡易表示にしました";
                default: return "lilToonのマテリアルが見つかりませんでした";
            }
        }

        internal static string Feature(ExportFallbackKind kind)
        {
            switch (kind)
            {
                case ExportFallbackKind.Blink: return "瞬き";
                case ExportFallbackKind.PlaneInsideBounds: return "Inside Bounds";
                case ExportFallbackKind.ColliderSkipped: return "この衝突";
                case ExportFallbackKind.StageOmitted: return "省略した機能";
                case ExportFallbackKind.LimitExceeded: return "上限を超えた項目";
                case ExportFallbackKind.MaterialApproximated: return "その材質の表現";
                default: return "lilToonの表現";
            }
        }

        // The sentence, then the object names on their own line.
        static string Lines(ExportFallback item, string indent)
        {
            var line = ExporterLocalization.T(Failure(item.Kind));
            if (item.Names.Count == 0) return line;
            var names = string.Join(", ", item.Names.Take(ShownNames));
            if (item.Names.Count > ShownNames)
                names = string.Format(ExporterLocalization.T(MoreFormat), names, item.Names.Count - ShownNames);
            return line + "\n" + indent + string.Format(ExporterLocalization.T(TargetFormat), names);
        }

        internal static string Message(ExportFallbackReport report)
        {
            var items = report.Items.OrderBy(item => item.Kind).ToArray();
            if (items.Length == 1)
                return Lines(items[0], "") + "\n" + string.Format(ExporterLocalization.T(Question), ExporterLocalization.T(Feature(items[0].Kind)));
            return string.Join("\n", items.Select(item => "・" + Lines(item, "　"))) + "\n\n" + ExporterLocalization.T(QuestionForSeveral);
        }

        // True when the export should be saved. Without fallbacks, without an
        // interactive caller or in batch mode it never asks and continues.
        internal static bool Confirm(ExportFallbackReport report, GameObject avatar, bool interactive, Action openBlinkSettings = null)
        {
            if (report == null || report.Count == 0 || !interactive || IsBatchMode()) return true;
            if (Dialog.Confirm(ExporterLocalization.T(Title), Message(report),
                    ExporterLocalization.T(ExportButton), ExporterLocalization.T(ChangeSettingsButton))) return true;
            ShowTargets(report, avatar, openBlinkSettings);
            return false;
        }

        internal static GameObject[] Targets(ExportFallbackReport report, GameObject avatar)
        {
            if (avatar == null) return Array.Empty<GameObject>();
            var targets = new List<GameObject>();
            foreach (var path in report.Items.SelectMany(item => item.Paths))
            {
                var found = path.Length == 0 ? avatar.transform : avatar.transform.Find(path);
                if (found != null && !targets.Contains(found.gameObject)) targets.Add(found.gameObject);
            }
            // Build tools can rename or move objects on the copy. Fall back to the avatar.
            if (targets.Count == 0) targets.Add(avatar);
            return targets.ToArray();
        }

        static void ShowTargets(ExportFallbackReport report, GameObject avatar, Action openBlinkSettings)
        {
            var targets = Targets(report, avatar);
            if (targets.Length > 0)
            {
                Selection.objects = targets;
                EditorGUIUtility.PingObject(targets[0]);
            }
            if (report.Items.Any(item => item.Kind == ExportFallbackKind.Blink)) openBlinkSettings?.Invoke();
        }
    }
}
