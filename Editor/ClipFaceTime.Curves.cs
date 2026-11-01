using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // The part of ClipFaceTime that reads ExpressionAnimationData curves
    // only, without UnityEditor, so the behavior tests compile it outside Unity.
    internal static partial class ClipFaceTime
    {
        // Null when no curve changes or no hold is away from rest; the caller keeps the last frame.
        internal static double? Of(IEnumerable<(ExpressionAnimationData.Curve Curve, double RestWeight)> curves, double duration)
        {
            if (!(duration > 0) || double.IsInfinity(duration)) return null;
            var changing = curves.Where(curve => Changes(curve.Curve)).ToArray();
            var still = changing.Select(curve => Still(curve.Curve, duration)).ToArray();
            var holding = Enumerable.Range(0, changing.Length)
                .Where(index => still[index].Sum(span => span.End - span.Start) >= duration / 2).ToArray();
            if (holding.Length == 0) return null;
            var cuts = new SortedSet<double> { 0, duration };
            foreach (var index in holding)
                foreach (var span in still[index]) { cuts.Add(span.Start); cuts.Add(span.End); }
            var times = cuts.ToArray();
            (double Start, double End, double Score)? best = null;
            double runStart = 0, runEnd = 0;
            double[] runValues = null;
            void Close()
            {
                if (runValues == null) return;
                var score = (runEnd - runStart) * holding.Select((curve, index) => Math.Abs(runValues[index] - changing[curve].RestWeight)).Sum();
                if (score > 0 && (best == null || score > best.Value.Score ||
                    score == best.Value.Score && runEnd - runStart > best.Value.End - best.Value.Start))
                    best = (runStart, runEnd, score);
                runValues = null;
            }
            for (var index = 0; index < times.Length - 1; index++)
            {
                var from = times[index]; var to = times[index + 1];
                var values = new double[holding.Length];
                var held = true;
                for (var curve = 0; curve < holding.Length && held; curve++)
                {
                    var span = still[holding[curve]].FirstOrDefault(candidate => candidate.Start <= from && to <= candidate.End);
                    held = span.End > span.Start;
                    values[curve] = span.Value;
                }
                if (!held) { Close(); continue; }
                // A held stretch that changes value (a stepped key) starts a new hold.
                if (runValues != null && !values.SequenceEqual(runValues)) Close();
                if (runValues == null) { runStart = from; runValues = values; }
                runEnd = to;
            }
            Close();
            return best == null ? (double?)null : (best.Value.Start + best.Value.End) / 2;
        }

        // The value of a curve at a time ClipFaceTime chose: the key value
        // where the curve stands still, which Curve.Evaluate may miss by a
        // rounding error between two flat keys; elsewhere Curve.Evaluate.
        internal static double Value(ExpressionAnimationData.Curve curve, double time, double duration)
        {
            if (curve.Keys.Count > 0 && time >= 0 && time <= duration)
                foreach (var span in Still(curve, duration))
                    if (span.Start < time && time < span.End) return span.Value;
            return curve.Evaluate(time);
        }

        // The gesture reader animates the same curves (VrChatGestureExpressions.ReadClip).
        static bool Changes(ExpressionAnimationData.Curve curve)
        {
            if (curve.Keys.Count < 2) return false;
            curve.Range(out var minimum, out var maximum);
            return minimum < maximum;
        }

        // The stretches of [0, duration] in which the curve keeps one value, as
        // ExpressionAnimationData.Curve.Evaluate reads it: between two keys of
        // the same value with flat handles (a zero tangent or a zero weight),
        // after a stepped key, before the first and after the last key when
        // the curve clamps there, and the repeats of the keyed stretches when
        // it loops or ping-pongs there.
        static List<(double Start, double End, double Value)> Still(ExpressionAnimationData.Curve curve, double duration)
        {
            var keys = curve.Keys;
            var spans = new List<(double Start, double End, double Value)>();
            var first = keys[0].Time; var last = keys[keys.Count - 1].Time;
            var keyed = new List<(double Start, double End, double Value)>();
            for (var index = 0; index < keys.Count - 1; index++)
            {
                var left = keys[index]; var right = keys[index + 1];
                var stepped = !left.OutTangent.HasValue || !right.InTangent.HasValue;
                if (right.Time > left.Time && (stepped || left.Value == right.Value && left.OutWeight * left.OutTangent == 0 && right.InWeight * right.InTangent == 0))
                    keyed.Add((left.Time, right.Time, left.Value));
            }
            void Add(double from, double to, double value, double lower, double upper)
            {
                from = Math.Max(lower, from); to = Math.Min(upper, to);
                if (to > from) spans.Add((from, to, value));
            }
            void Outside(string wrap, double lower, double upper, double value)
            {
                if (!(upper > lower)) return;
                if (wrap == "clamp") { Add(lower, upper, value, lower, upper); return; }
                // Curve.Evaluate maps a time outside the keys to
                // first + phase, with phase folded back for ping-pong.
                var length = last - first;
                var period = wrap == "pingPong" ? length * 2 : length;
                if (!(length > 0) || (upper - lower) / period > 10000) return;
                var cycle = new List<(double Start, double End, double Value)>(keyed);
                if (wrap == "pingPong")
                    cycle.AddRange(keyed.Select(span => (first + period - (span.End - first), first + period - (span.Start - first), span.Value)));
                for (var turn = Math.Floor((lower - first) / period); first + turn * period < upper; turn++)
                    foreach (var span in cycle) Add(span.Start + turn * period, span.End + turn * period, span.Value, lower, upper);
            }
            Outside(curve.PreWrap, 0, Math.Min(first, duration), keys[0].Value);
            foreach (var span in keyed) Add(span.Start, span.End, span.Value, 0, duration);
            Outside(curve.PostWrap, Math.Max(last, 0), duration, keys[keys.Count - 1].Value);
            return spans;
        }
    }
}
