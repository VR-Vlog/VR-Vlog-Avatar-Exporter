using System;
using System.Collections.Generic;

namespace VRVlog.AvatarAnimator
{
    // The FX parameter values that show a custom expression of VRVLOG_avatar_animator's expressions.
    // The exporter writes how the states that play the face are entered and the conditions of each
    // way in; this picks one way in and solves its conditions into values. Pure C#, no scene.
    // The app picks a face's values this way, and the exporter checks with the same values that
    // the evaluator shows the face (AvatarAnimatorCrossCheck.FaceMismatch). Taken unchanged from
    // the app's AvatarAnimatorExpressions (epic #3658, #3663), which can use this one instead.
    public static class AvatarAnimatorFaces
    {
        // The parameter's value as the controller holds it before the face (the menu's, or the default).
        public delegate double Current(int parameter);

        // The values that show `name`, in parameter order, or null when the avatar's FX has no such
        // face or no way into it can be met. Ways in are tried in this order: an Any State
        // transition (from any state), a transition from the layer's first state (where a layer
        // waits while nothing is picked), an Entry transition, the first state itself (no
        // condition), then a transition from any other state.
        public static List<KeyValuePair<int, double>> Solve(AvatarAnimatorData data, string name, Current current)
        {
            var expression = Find(data, name);
            if (expression == null || data.Controllers.Count == 0) return null;
            var layers = data.Controllers[0].Layers;
            var fixedParameters = ClipParameters(data);
            for (var rank = 0; rank < 5; rank++)
                foreach (var entrance in expression.When)
                {
                    if (Rank(layers[entrance.Layer], entrance) != rank) continue;
                    var values = Solve(data, entrance, fixedParameters, current);
                    if (values == null) continue;
                    Tree(data, layers[entrance.Layer].States[entrance.State].Motion, new HashSet<int>(expression.Clips), values, fixedParameters);
                    var result = new List<KeyValuePair<int, double>>(values);
                    result.Sort((a, b) => a.Key.CompareTo(b.Key));
                    return result;
                }
            return null;
        }

        public static AvatarAnimatorData.Expression Find(AvatarAnimatorData data, string name)
        {
            if (data == null || name == null) return null;
            foreach (var expression in data.Expressions)
                if (expression.Name == name) return expression;
            return null;
        }

        static int Rank(AvatarAnimatorData.Layer layer, AvatarAnimatorData.Entrance entrance)
        {
            switch (entrance.From)
            {
                case "any": return 0;
                case "state": return layer.Machines.Count > 0 && entrance.FromState == layer.Machines[0].DefaultState ? 1 : 4;
                case "entry": return 2;
                case "default": return 3;
                default: return -1;
            }
        }

        // Parameters a clip animates: the evaluator does not take a value for them.
        static HashSet<int> ClipParameters(AvatarAnimatorData data)
        {
            var result = new HashSet<int>();
            foreach (var target in data.Targets)
                if (target.Property == "parameter") result.Add(target.Parameter);
            return result;
        }

        // One range per parameter the conditions name. Integers and bools are whole numbers; a
        // float bound is strict.
        sealed class Range
        {
            internal double? Equal;
            internal double Low = double.NegativeInfinity, High = double.PositiveInfinity;
            internal readonly List<double> Not = new List<double>();
        }

        static Dictionary<int, double> Solve(AvatarAnimatorData data, AvatarAnimatorData.Entrance entrance,
            HashSet<int> fixedParameters, Current current)
        {
            var ranges = new Dictionary<int, Range>();
            foreach (var condition in entrance.Conditions)
            {
                if (!ranges.TryGetValue(condition.Parameter, out var range)) ranges[condition.Parameter] = range = new Range();
                var type = data.Parameters[condition.Parameter].Type;
                var whole = type != "float";
                var t = condition.Threshold;
                switch (condition.Mode)
                {
                    case "if": if (!Meet(range, 1)) return null; break;
                    case "ifNot": if (!Meet(range, 0)) return null; break;
                    case "equals": if (!Meet(range, Math.Truncate(t))) return null; break;
                    case "notEqual": range.Not.Add(Math.Truncate(t)); break;
                    // Unity compares an int with the threshold as a float.
                    case "greater": range.Low = Math.Max(range.Low, whole ? Math.Floor(t) + 1 : t); break;
                    case "less": range.High = Math.Min(range.High, whole ? Math.Ceiling(t) - 1 : t); break;
                    default: return null;
                }
            }
            var values = new Dictionary<int, double>();
            foreach (var pair in ranges)
            {
                var parameter = pair.Key;
                var type = data.Parameters[parameter].Type;
                var now = current(parameter);
                if (fixedParameters.Contains(parameter))
                {
                    // The clips own it; the way in is open only if it already holds.
                    if (!Holds(pair.Value, type, now)) return null;
                    continue;
                }
                if (!Pick(pair.Value, type, now, out var value)) return null;
                values[parameter] = value;
            }
            return values;
        }

        static bool Meet(Range range, double value)
        {
            if (range.Equal.HasValue && range.Equal.Value != value) return false;
            range.Equal = value;
            return true;
        }

        static bool Holds(Range range, string type, double value)
        {
            var whole = type != "float";
            if (range.Equal.HasValue) return value == range.Equal.Value;
            if (whole ? value < range.Low || value > range.High : value <= range.Low || value >= range.High) return false;
            return !range.Not.Contains(value);
        }

        // A value in the range, as the evaluator would store it: the current value when it already
        // holds, otherwise a value near the 0 to 1 that bools, gesture weights and toggles use.
        static bool Pick(Range range, string type, double now, out double value)
        {
            if (Holds(range, type, Store(type, now))) { value = Store(type, now); return true; }
            if (range.Equal.HasValue) { value = range.Equal.Value; return Holds(range, type, Store(type, value)); }
            if (type == "float")
            {
                value = !double.IsInfinity(range.Low) && !double.IsInfinity(range.High) ? (range.Low + range.High) / 2
                    : !double.IsInfinity(range.Low) ? (range.Low < 1 ? 1 : range.Low + 1)
                    : !double.IsInfinity(range.High) ? (range.High > 0 ? 0 : range.High - 1) : 0;
                value = Store(type, value);
                return Holds(range, type, value);
            }
            // Whole numbers: the smallest in the range from 0 up, else the largest below 0, skipping notEqual.
            var start = Math.Max(range.Low, Math.Min(range.High, 0));
            for (var step = 0; step <= range.Not.Count; step++)
            {
                foreach (var candidate in new[] { start + step, start - step })
                    if (Holds(range, type, candidate)) { value = candidate; return true; }
            }
            value = 0;
            return false;
        }

        static double Store(string type, double value) =>
            type == "float" ? (float)value : type == "int" ? Math.Truncate(value) : value != 0 ? 1 : 0;

        // When the state plays the face through blend trees, the tree parameters that pick the
        // face's child: a 1D threshold, a 2D position, or a direct child's weight of 1. Parameters
        // the conditions already set keep their values.
        static void Tree(AvatarAnimatorData data, int motion, HashSet<int> clips, Dictionary<int, double> values, HashSet<int> fixedParameters)
        {
            for (var depth = 0; motion >= 0 && depth <= AvatarAnimatorData.MaximumBlendTreeDepth; depth++)
            {
                var m = data.Motions[motion];
                if (m.Type == "clip") return;
                AvatarAnimatorData.Child pick = null;
                foreach (var child in m.Children)
                    if (child.Motion >= 0 && AvatarAnimatorData.Plays(data, child.Motion, clips)) { pick = child; break; }
                if (pick == null) return;
                if (m.BlendType == "1d" && m.Parameters.Count > 0) Put(data, m.Parameters[0], pick.Threshold, values, fixedParameters);
                else if (m.BlendType == "direct") { if (pick.DirectParameter.HasValue) Put(data, pick.DirectParameter.Value, 1, values, fixedParameters); }
                else if (m.Parameters.Count > 1)
                {
                    Put(data, m.Parameters[0], pick.X, values, fixedParameters);
                    Put(data, m.Parameters[1], pick.Y, values, fixedParameters);
                }
                motion = pick.Motion;
            }
        }

        static void Put(AvatarAnimatorData data, int parameter, double value, Dictionary<int, double> values, HashSet<int> fixedParameters)
        {
            if (parameter < 0 || parameter >= data.Parameters.Count || values.ContainsKey(parameter) || fixedParameters.Contains(parameter)) return;
            values[parameter] = Store(data.Parameters[parameter].Type, value);
        }
    }
}
