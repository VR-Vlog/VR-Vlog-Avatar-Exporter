using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // The time at which a moving face is read as one still face: the board's
    // presets, previews and checks, and the VRM expression of a moving face
    // (VrChatExpressionBaker). Curves whose value never changes read the same
    // at every time; a face without a changing curve keeps its last frame.
    // A moving face (a loop that starts and ends at rest, with a blink or a
    // sparkle on top) is read where it holds its face:
    // - The holding curves are the changing curves that stand still for at
    //   least half of the duration. A curve that keeps moving, such as a
    //   sparkle, is not one of them and takes its value at the chosen time.
    // - A hold is a stretch in which every holding curve stands still with
    //   the same values. The hold whose holding curves sum to the largest
    //   weight wins; then the longer one, then the earlier one.
    // - The face is the middle of that hold. Without a hold, the last frame.
    internal static class ClipFaceTime
    {
        internal static float Of(AnimationClip clip)
        {
            if (clip == null) return 0;
            var length = clip.length;
            if (!(length > 0) || float.IsInfinity(length)) return length;
            var curves = AnimationUtility.GetCurveBindings(clip).Where(PoseSampling.IsBlendShape)
                .Select(binding => AnimationUtility.GetEditorCurve(clip, binding))
                .Where(curve => curve != null && curve.length > 0)
                .Select(VrChatGestureExpressions.ConvertCurve);
            var time = Of(curves, length);
            return time == null ? length : (float)time.Value;
        }

        // Null when no curve changes or no hold exists; the caller keeps the last frame.
        internal static double? Of(IEnumerable<ExpressionAnimationData.Curve> curves, double duration)
        {
            if (!(duration > 0) || double.IsInfinity(duration)) return null;
            var changing = curves.Where(Changes).ToArray();
            var still = changing.Select(curve => Still(curve, duration)).ToArray();
            var holding = Enumerable.Range(0, changing.Length)
                .Where(index => still[index].Sum(span => span.End - span.Start) >= duration / 2).ToArray();
            if (holding.Length == 0) return null;
            var cuts = new SortedSet<double> { 0, duration };
            foreach (var index in holding)
                foreach (var span in still[index]) { cuts.Add(span.Start); cuts.Add(span.End); }
            var times = cuts.ToArray();
            (double Start, double End, double Weight)? best = null;
            double runStart = 0, runEnd = 0;
            double[] runValues = null;
            void Close()
            {
                if (runValues == null) return;
                var weight = runValues.Sum();
                if (best == null || weight > best.Value.Weight ||
                    weight == best.Value.Weight && runEnd - runStart > best.Value.End - best.Value.Start)
                    best = (runStart, runEnd, weight);
                runValues = null;
            }
            for (var index = 0; index < times.Length - 1; index++)
            {
                var from = times[index]; var to = times[index + 1];
                if (!holding.All(curve => still[curve].Any(span => span.Start <= from && to <= span.End))) { Close(); continue; }
                var middle = (from + to) / 2;
                var values = holding.Select(curve => changing[curve].Evaluate(middle)).ToArray();
                // A held stretch that changes value (a stepped key) starts a new hold.
                if (runValues != null && !values.SequenceEqual(runValues)) Close();
                if (runValues == null) { runStart = from; runValues = values; }
                runEnd = to;
            }
            Close();
            return best == null ? (double?)null : (best.Value.Start + best.Value.End) / 2;
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
        // the same value with flat tangents, after a stepped key, and before
        // the first or after the last key when the curve clamps there.
        static List<(double Start, double End)> Still(ExpressionAnimationData.Curve curve, double duration)
        {
            var keys = curve.Keys;
            var spans = new List<(double Start, double End)>();
            void Add(double from, double to)
            {
                from = System.Math.Max(0, System.Math.Min(duration, from)); to = System.Math.Max(0, System.Math.Min(duration, to));
                if (to > from) spans.Add((from, to));
            }
            if (curve.PreWrap == "clamp") Add(0, keys[0].Time);
            for (var index = 0; index < keys.Count - 1; index++)
            {
                var left = keys[index]; var right = keys[index + 1];
                var stepped = !left.OutTangent.HasValue || !right.InTangent.HasValue;
                if (stepped || left.Value == right.Value && left.OutTangent == 0 && right.InTangent == 0) Add(left.Time, right.Time);
            }
            if (curve.PostWrap == "clamp") Add(keys[keys.Count - 1].Time, duration);
            return spans;
        }
    }
}
