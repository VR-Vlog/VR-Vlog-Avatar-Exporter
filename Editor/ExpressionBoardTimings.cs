using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;

namespace VRVlog.LilToonExporter
{
    // How long each step of opening the expression board takes. Every step is
    // a Profiler marker ("ExpressionBoard/<step>", seen in the Profiler's Edit
    // Mode) and is kept in Last until the next board starts preparing, so a
    // slow avatar can be measured step by step. A step inside another is named
    // "<outer>/<inner>".
    internal static class ExpressionBoardTimings
    {
        internal static readonly List<(string Step, double Milliseconds)> Last = new List<(string, double)>();
        static readonly Dictionary<string, ProfilerMarker> markers = new Dictionary<string, ProfilerMarker>();
        static readonly List<string> open = new List<string>();

        internal static void Clear() => Last.Clear();

        // One step, ended by Dispose.
        internal static Steps Step(string name) => new Steps(name);

        // Consecutive steps of straight-line code: Next ends the current step
        // and starts the following one; Dispose ends the last.
        internal static Steps Sequence() => new Steps(null);

        internal sealed class Steps : IDisposable
        {
            string name;
            ProfilerMarker marker;
            long started;

            internal Steps(string first) { if (first != null) Next(first); }

            internal void Next(string step)
            {
                End();
                name = open.Count > 0 ? open[open.Count - 1] + "/" + step : step;
                if (!markers.TryGetValue(name, out marker)) markers[name] = marker = new ProfilerMarker("ExpressionBoard/" + name);
                open.Add(name);
                marker.Begin();
                started = Stopwatch.GetTimestamp();
            }

            void End()
            {
                if (name == null) return;
                var elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
                marker.End();
                open.Remove(name);
                if (Last.Count >= 2000) Last.RemoveRange(0, 1000); // A long session keeps only recent steps.
                Last.Add((name, elapsed));
                name = null;
            }

            public void Dispose() => End();
        }
    }
}
