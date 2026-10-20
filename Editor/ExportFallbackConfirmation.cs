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
        MaterialApproximated,
        // A material on an unsupported shader was replaced by an official lilToon approximation.
        ShaderSubstituted,
        // An animated expression whose loop ends on the neutral face. Apps that
        // only read the VRM expression show no change. Names are the expressions.
        ExpressionEndsNeutral,
        // A material whose shader is missing from the project. Names are the materials.
        MissingShader
    }

    // One line of the export result: what happened to which item, and why.
    internal sealed class ExportResultEntry
    {
        internal ExportOutcome Outcome;
        // Short feature name shown in the result list (for example "揺れ物").
        internal string Category;
        // The object, material or stage concerned. Empty for whole features.
        internal string Name;
        // Transform path relative to the avatar root, when the item is an object.
        internal string Path;
        internal string Reason;
        // Set for entries made by the stage guard.
        internal ExportStage? Stage;
        // Set for entries made by a gimmick rule (ExportGimmickRules).
        internal string RuleId;
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
                    Record(Outcome(kind), category, label, reason ?? ExportFallbackConfirmation.Failure(kind)).Path = RelativePath(root, target);
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
            Add(ExportFallbackKind.StageOmitted, ExportStageGuard.DisplayName(stage));
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
        // Shows the finished export before it is written. Calls save to write
        // it or changeSettings to go back; closing without either saves nothing.
        void Show(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings);
    }

    internal static class ExportFallbackConfirmation
    {
        sealed class EditorDialog : IExportFallbackDialog
        {
            public void Show(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings) =>
                ExportResultWindow.ShowPending(result, avatar, save, changeSettings, openBlinkSettings);
        }

        // Tests replace these two; batch runs and CI never reach the window.
        internal static IExportFallbackDialog Dialog = new EditorDialog();
        internal static Func<bool> IsBatchMode = () => Application.isBatchMode;

        internal const string ExpressionEndsNeutralText = "ループの最後が元の顔に戻る表情です。VR Vlogでは動きますが、ほかのアプリでは選んでも顔が変わりません";
        internal const string MissingShaderReason = "シェーダーが見つからないため、元の見た目を再現できませんでした（Unity上でピンク色に表示されることがあります）";

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
                case ExportFallbackKind.ShaderSubstituted: return "非対応のシェーダーを公式lilToonで近似しました";
                case ExportFallbackKind.ExpressionEndsNeutral: return ExpressionEndsNeutralText;
                case ExportFallbackKind.MissingShader: return MissingShaderReason;
                default: return "lilToonのマテリアルが見つかりませんでした";
            }
        }

        internal static string Feature(ExportFallbackKind kind)
        {
            switch (kind)
            {
                case ExportFallbackKind.Blink: return "瞬き";
                case ExportFallbackKind.PlaneInsideBounds: return "揺れ物のコライダー";
                case ExportFallbackKind.ColliderSkipped: return "揺れ物のコライダー";
                case ExportFallbackKind.StageOmitted: return "省略した機能";
                case ExportFallbackKind.LimitExceeded: return "上限を超えた項目";
                case ExportFallbackKind.MaterialApproximated: return "マテリアル";
                case ExportFallbackKind.ShaderSubstituted: return "マテリアル";
                case ExportFallbackKind.ExpressionEndsNeutral: return "表情";
                case ExportFallbackKind.MissingShader: return "マテリアル";
                default: return "マテリアル";
            }
        }

        // True when the user has to decide before the file is written: the
        // export worked around something, and someone is there to answer.
        internal static bool NeedsAnswer(ExportFallbackReport report, bool interactive) =>
            report != null && report.Count != 0 && interactive && !IsBatchMode();

        // Saves at once unless NeedsAnswer; then the result list decides. Going
        // back selects the affected objects and opens the blink settings if needed.
        internal static void Confirm(ExportFallbackReport report, IEnumerable<string> warnings, string destination,
            GameObject avatar, bool interactive, Action save, Action openBlinkSettings = null)
        {
            if (!NeedsAnswer(report, interactive)) { save(); return; }
            var result = ExportResultList.Build(report, warnings, destination);
            Dialog.Show(result, avatar, save, () => ShowTargets(report, avatar, openBlinkSettings), openBlinkSettings);
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
