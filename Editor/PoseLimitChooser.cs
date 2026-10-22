using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal interface IPoseLimitDialog
    {
        // Shows the plan, already set to the saved choice or the default. The
        // user's answer is left in the rows' Keep flags; false stops the export.
        bool Ask(PoseLimitPlan plan, GameObject avatar);
    }

    // Fits the bundled poses to the app's limits before they are written.
    // Within the limits nothing is asked and every pose is kept. Over them a
    // normal export asks once (PoseLimitWindow); quick transfer and batch runs
    // use the choice saved for the avatar, or the default (drop from the end).
    internal static class PoseLimitChooser
    {
        sealed class EditorDialog : IPoseLimitDialog
        {
            public bool Ask(PoseLimitPlan plan, GameObject avatar) => PoseLimitWindow.Ask(plan, avatar);
        }

        // Tests replace this; batch runs never reach the window.
        internal static IPoseLimitDialog Dialog = new EditorDialog();

        internal const string Category = "同梱ポーズ";
        internal const string DroppedReason = "アプリの上限に合わせて外しました";
        internal const string TooLargeReason = "1本でデータの上限16MBを超えるため外しました";

        // One export, from the button to the saved file. Retries and the
        // re-export of recorded faces run inside it and reuse the answer, so
        // the window opens at most once.
        internal sealed class Scope : IDisposable
        {
            internal readonly GameObject Avatar;
            internal readonly bool Interactive;
            internal HashSet<string> Answer;
            readonly Scope outer;

            internal Scope(GameObject avatar, bool interactive)
            {
                Avatar = avatar; Interactive = interactive;
                outer = current; current = this;
            }

            public void Dispose() { if (current == this) current = outer; }
        }

        static Scope current;

        // Sets session.LimitExcluded and records what was left out. Throws
        // OperationCanceledException when the user stops the export.
        internal static void Apply(PoseExportSession session, ICollection<string> warnings, ExportRecoveryReport report)
        {
            session.LimitExcluded.Clear();
            var plan = new PoseLimitPlan(session.LimitCandidates());
            if (!plan.NeedsChoice) return;
            var scope = current;
            var avatar = scope?.Avatar != null ? scope.Avatar : session.Source;
            plan.Choose(scope?.Answer ?? ExportSettingsStore.LoadLimitChoice(avatar));
            if (scope != null && scope.Interactive && scope.Answer == null && !ExportFallbackConfirmation.IsBatchMode())
            {
                if (!Dialog.Ask(plan, avatar)) throw new OperationCanceledException();
                // The window only allows an answer within the limits; a test
                // dialog may not, so the export still fits.
                plan.Choose(plan.Dropped);
                scope.Answer = plan.Dropped;
                ExportSettingsStore.SaveLimitChoice(avatar, plan.IsDefault ? Enumerable.Empty<string>() : plan.Dropped);
            }
            foreach (var row in plan.Rows.Where(row => !row.Keep)) session.LimitExcluded.Add(row.Id);
            Record(plan, warnings, report);
        }

        static void Record(PoseLimitPlan plan, ICollection<string> warnings, ExportRecoveryReport report)
        {
            foreach (var row in plan.LeftOut)
            {
                var reason = row.TooLarge ? TooLargeReason : DroppedReason;
                report?.Fallbacks.Record(ExportOutcome.Omitted, Category, row.Name, reason);
                var text = ExporterLocalization.T(reason);
                if (warnings != null && !warnings.Contains(text)) warnings.Add(text);
            }
        }
    }
}
