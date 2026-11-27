using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The progress bar of an export. An export holds the editor for a minute or
    // more on a large avatar; without a bar Unity's busy dialog names only the
    // callback the export runs in (for a transfer, AppTransferPanel.Poll), not
    // what it is doing. Inside Scope each stage the export enters shows its
    // name, and the bar's cancel button stops the export at the next stage.
    internal static class ExportProgress
    {
        // Draws the bar and returns true when its cancel button was pressed.
        // Tests replace it to read the steps and to press cancel.
        internal static Func<string, float, bool> Display = Draw;

        // Unity drops an update that comes less than about 0.1 s after the bar
        // was last drawn (measured on 2022.3: 80 ms dropped, 120 ms drawn; a new
        // title is dropped too), and the bar is not drawn again while a stage
        // holds the editor. A stage that starts right after another would then
        // run for minutes under the name of the one before. A bar shown after
        // ClearProgressBar is drawn at once; waiting out the time instead made
        // an export about 8 s longer (about 70 stages at 0.12 s).
        const double UnityRedrawInterval = 0.12;
        static double drawnAt = double.NegativeInfinity;

        static bool Draw(string text, float fraction)
        {
            if (Application.isBatchMode) return false;
            var now = EditorApplication.timeSinceStartup;
            if (now - drawnAt < UnityRedrawInterval) EditorUtility.ClearProgressBar();
            drawnAt = now;
            return EditorUtility.DisplayCancelableProgressBar(ExporterLocalization.T("VRMを書き出しています"), text, fraction);
        }

        const double RedrawInterval = 0.25;
        static int depth;
        static bool canceled;
        static string shown;
        static double shownAt;
        static readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        // The stages of ExportStageGuard and the steps the report names, about
        // as many as an export enters; only for the length of the bar.
        static readonly int expected = Enum.GetValues(typeof(ExportStage)).Length + 10;

        internal static bool Active => depth > 0;

        // One export. A nested export shares the outer bar; the outermost
        // clears it.
        internal static IDisposable Scope()
        {
            if (depth++ == 0) { canceled = false; shown = null; seen.Clear(); }
            return new Exit();
        }

        // Shows label when it changed or the bar was last drawn a moment ago,
        // so the cancel button is read often without redrawing on every call.
        // After a cancel every later step throws again, so a broad catch inside
        // the export cannot keep it going.
        internal static void Step(string label)
        {
            if (depth == 0 || string.IsNullOrEmpty(label)) return;
            if (canceled) throw new OperationCanceledException();
            seen.Add(label);
            var now = EditorApplication.timeSinceStartup;
            if (label == shown && now - shownAt < RedrawInterval) return;
            shown = label;
            shownAt = now;
            var fraction = Mathf.Clamp01((float)seen.Count / expected);
            if (!Display(ExporterLocalization.T(label), fraction)) return;
            canceled = true;
            throw new OperationCanceledException();
        }

        sealed class Exit : IDisposable
        {
            bool done;
            public void Dispose()
            {
                if (done) return;
                done = true;
                if (--depth > 0) return;
                shown = null;
                seen.Clear();
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
