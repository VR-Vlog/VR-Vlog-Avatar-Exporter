using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;

namespace VRVlog.AvatarAnimator.Comparison
{
    // The cases AvatarAnimatorEvaluator is compared with Unity's Animator on.
    // This file has no Unity reference: AvatarAnimatorEvaluatorComparisonTests
    // plays every case in Unity and records Unity's values frame by frame, and
    // Tools/run-avatar-animator-evaluator-tests.ps1 replays those recordings
    // (Tools/Fixtures/AvatarAnimator) against the evaluator without Unity.
    public static class AvatarAnimatorComparison
    {
        public const double ValueTolerance = 1e-4;
        // Unity's muscles are read back from the bones (HumanPoseHandler) in floats.
        public const double MuscleTolerance = 1e-3;

        public sealed class Case
        {
            public string Name;
            public double Seconds;
            public readonly AvatarAnimatorData Data = new AvatarAnimatorData();
            // Parameter values set before the first frame that starts at or after Time.
            public readonly List<(double Time, int Parameter, double Value)> Inputs = new List<(double, int, double)>();
            // Layer weights set (Animator.SetLayerWeight) before the first frame that starts at or after Time.
            public readonly List<(double Time, int Layer, double Weight)> LayerWeights = new List<(double, int, double)>();
            // Targets Unity plays as GameObject.activeSelf; the others are a
            // float (Transform.localPosition.x) so blends compare exactly. A
            // morph target is a SkinnedMeshRenderer's blend shape, and a
            // muscle target a muscle of a humanoid rig, which Unity reads
            // back with HumanPoseHandler.
            public readonly HashSet<int> ActiveTargets = new HashSet<int>();
            public bool Humanoid => Data.Targets.Any(t => t.Property == "muscle");
        }

        public static readonly string[] Timings = { "60", "30", "jitter" };

        // The deltaTime of each frame, Seconds long in all.
        public static double[] Steps(Case c, string timing)
        {
            var steps = new List<double>();
            uint seed = 12345;
            var total = 0.0;
            while (total < c.Seconds - 1e-9)
            {
                double dt;
                if (timing == "60") dt = 1.0 / 60;
                else if (timing == "30") dt = 1.0 / 30;
                else
                {
                    seed = seed * 1664525 + 1013904223;
                    dt = 1.0 / 90 + (1.0 / 20 - 1.0 / 90) * (seed >> 8) / (double)(1 << 24);
                }
                // Unity takes deltaTime as a float.
                dt = (float)dt;
                steps.Add(dt);
                total += dt;
            }
            return steps.ToArray();
        }

        // The inputs to set before each frame.
        public static List<(int Parameter, double Value)>[] Inputs(Case c, double[] steps) => PerFrame(c.Inputs, steps);

        // The layer weights to set before each frame.
        public static List<(int Layer, double Weight)>[] LayerWeights(Case c, double[] steps) => PerFrame(c.LayerWeights, steps);

        static List<(int, double)>[] PerFrame(List<(double Time, int Index, double Value)> timed, double[] steps)
        {
            var result = new List<(int, double)>[steps.Length];
            var start = 0.0;
            var next = 0;
            var ordered = timed.OrderBy(input => input.Time).ToList();
            for (var i = 0; i < steps.Length; i++)
            {
                result[i] = new List<(int, double)>();
                while (next < ordered.Count && ordered[next].Time <= start + 1e-9)
                {
                    result[i].Add((ordered[next].Index, ordered[next].Value));
                    next++;
                }
                start += steps[i];
            }
            return result;
        }

        // The evaluator's values and parameters after each frame, in the
        // shape Unity's recording has.
        public static void Play(Case c, string timing, out double[][] values, out double[][] parameters)
        {
            var data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(c.Data));
            var evaluator = new AvatarAnimatorEvaluator(data, () => 0);
            var steps = Steps(c, timing);
            var inputs = Inputs(c, steps);
            var weights = LayerWeights(c, steps);
            values = new double[steps.Length][];
            parameters = new double[steps.Length][];
            for (var i = 0; i < steps.Length; i++)
            {
                foreach (var (parameter, value) in inputs[i]) evaluator.Set(parameter, value);
                foreach (var (layer, weight) in weights[i]) evaluator.SetLayerWeight(layer, weight);
                evaluator.Advance(steps[i]);
                values[i] = new double[data.Targets.Count];
                for (var t = 0; t < values[i].Length; t++)
                    values[i][t] = c.ActiveTargets.Contains(t) ? (AvatarAnimatorEvaluator.Active(evaluator.Values[t]) ? 1 : 0) : evaluator.Values[t];
                parameters[i] = new double[data.Parameters.Count];
                for (var p = 0; p < parameters[i].Length; p++) parameters[i][p] = evaluator.Get(p);
            }
        }

        // Null when the evaluator plays what Unity recorded; otherwise the first
        // difference and how many values differ.
        public static string Mismatch(Case c, string timing, double[][] unityValues, double[][] unityParameters)
        {
            Play(c, timing, out var values, out var parameters);
            if (values.Length != unityValues.Length) return "frame count " + values.Length + " against Unity's " + unityValues.Length;
            string first = null;
            var differences = 0;
            var steps = Steps(c, timing);
            var time = 0.0;
            for (var i = 0; i < values.Length; i++)
            {
                time += steps[i];
                for (var t = 0; t < values[i].Length; t++)
                    if (!(Math.Abs(values[i][t] - unityValues[i][t]) <= (c.Data.Targets[t].Property == "muscle" ? MuscleTolerance : ValueTolerance)))
                    {
                        differences++;
                        first = first ?? string.Format(CultureInfo.InvariantCulture, "frame {0} (t={1:0.####}) target {2}: evaluator {3:R}, Unity {4:R}", i, time, t, values[i][t], unityValues[i][t]);
                    }
                for (var p = 0; p < parameters[i].Length; p++)
                    if (!(Math.Abs(parameters[i][p] - unityParameters[i][p]) <= ValueTolerance))
                    {
                        differences++;
                        first = first ?? string.Format(CultureInfo.InvariantCulture, "frame {0} (t={1:0.####}) parameter {2}: evaluator {3:R}, Unity {4:R}", i, time, c.Data.Parameters[p].Name, parameters[i][p], unityParameters[i][p]);
                    }
            }
            return first == null ? null : c.Name + "/" + timing + ": " + differences + " differences, first at " + first;
        }

        // A recording: the case's data, so a stale recording is found, and
        // Unity's values and parameters after each frame.
        public static string Recording(Case c, double[][] values, double[][] parameters)
        {
            var b = new StringBuilder();
            b.Append("{\"data\":");
            Serialize(AvatarAnimatorData.Write(c.Data), b);
            b.Append(",\"values\":");
            Serialize(values, b);
            b.Append(",\"parameters\":");
            Serialize(parameters, b);
            b.Append("}\n");
            return b.ToString();
        }

        public static string Serialize(object value) { var b = new StringBuilder(); Serialize(value, b); return b.ToString(); }

        static void Serialize(object value, StringBuilder b)
        {
            switch (value)
            {
                case null: b.Append("null"); break;
                case string s:
                    b.Append('"');
                    foreach (var ch in s)
                    {
                        if (ch == '"' || ch == '\\') b.Append('\\').Append(ch);
                        else if (ch < ' ') b.Append("\\u").Append(((int)ch).ToString("x4"));
                        else b.Append(ch);
                    }
                    b.Append('"');
                    break;
                case bool flag: b.Append(flag ? "true" : "false"); break;
                case IDictionary dictionary:
                    b.Append('{');
                    var firstEntry = true;
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        if (!firstEntry) b.Append(',');
                        firstEntry = false;
                        Serialize((string)entry.Key, b);
                        b.Append(':');
                        Serialize(entry.Value, b);
                    }
                    b.Append('}');
                    break;
                case IEnumerable list:
                    b.Append('[');
                    var firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) b.Append(',');
                        firstItem = false;
                        Serialize(item, b);
                    }
                    b.Append(']');
                    break;
                default: b.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture)); break;
            }
        }

        public static IEnumerable<Case> Cases()
        {
            yield return Toggle();
            yield return Fade();
            yield return ExitTime();
            yield return ExitTimeLoop();
            yield return Triggers();
            yield return AnyState();
            yield return Interruption();
            yield return AnyStateInterruption();
            yield return AnyStateInterruption("source");
            yield return AnyStateInterruption("source", ordered: true);
            yield return AnyStateInterruption("destination");
            yield return AnyStateRunning("anyStateRestart");
            yield return AnyStateRunning("anyStateOrdered");
            yield return AnyStateRunning("anyStateUnordered");
            yield return AnyStateRunning("anyStateBeforeDestination");
            yield return ExitTimeRestart(false);
            yield return ExitTimeRestart(true);
            yield return WriteDefaultsOff();
            yield return Layers();
            yield return Trees();
            yield return FreeformCartesian();
            yield return SimpleDirectional();
            yield return FreeformDirectional();
            yield return Machines();
            yield return StateOptions();
            yield return Chain();
            yield return Synced();
            yield return ActiveThreshold();
            yield return ToggleOverRamp();
            yield return LayersBack();
            yield return MixedWriteDefaults();
            yield return ParameterCurves();
            yield return ParameterHold();
            yield return ParameterPass();
            yield return LayerZero();
            yield return Hands();
            yield return MaskParts();
            yield return NodeMask();
            yield return GestureAction();
            yield return ActionMask();
            yield return HumanoidNodeMask();
            yield return HandsOverEmpty("handsOverEmpty", true);
            yield return HandsOverEmpty("handsOverNoMotion", false);
            yield return HandsUnderThumb();
        }

        public static Case Find(string name) => Cases().Single(c => c.Name == name);

        // The toggle an exported avatar has: Any State into Off or On, 0 s,
        // Write Defaults off, clips of one frame.
        static Case Toggle()
        {
            var c = new Builder("toggle", 1.2);
            var hat = c.Parameter("Hat", "bool");
            var node = c.Target(1, active: true);
            var layer = c.Layer("Hat");
            var off = c.State(layer, "Off", c.Constant(1.0 / 60, false, (node, 0)), writeDefaults: false);
            var on = c.State(layer, "On", c.Constant(1.0 / 60, false, (node, 1)), writeDefaults: false);
            layer.Machines[0].AnyStateTransitions.Add(To(on).When(hat, "if"));
            layer.Machines[0].AnyStateTransitions.Add(To(off).When(hat, "ifNot"));
            c.Input(.2, hat, 1).Input(.5, hat, 0).Input(.8, hat, 1);
            return c.Case;
        }

        // Timed transitions: fixed and normalized durations and an offset.
        static Case Fade()
        {
            var c = new Builder("fade", 2.5);
            var go = c.Parameter("Go", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Fade");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(.8, true, node, 2, 3));
            layer.States[a].Transitions.Add(To(b, duration: .3).When(go, "if"));
            var back = To(a, duration: .5, fixedDuration: false).When(go, "ifNot");
            back.Offset = .25;
            layer.States[b].Transitions.Add(back);
            c.Input(.4, go, 1).Input(1.3, go, 0);
            return c.Case;
        }

        // Exit times without conditions: under 1 on a clip that does not loop,
        // and over 1 on one that does.
        static Case ExitTime()
        {
            var c = new Builder("exitTime", 3);
            var node = c.Target(0);
            var layer = c.Layer("Exit");
            var a = c.State(layer, "A", c.Ramp(1, false, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(.5, true, node, 5, 6));
            layer.States[a].Transitions.Add(To(b, duration: .2, exitTime: true, at: .75));
            layer.States[b].Transitions.Add(To(a, duration: .1, exitTime: true, at: 2.5));
            return c.Case;
        }

        // An exit time under 1 with a condition on a looping clip: the
        // condition is set after this loop's exit time.
        static Case ExitTimeLoop()
        {
            var c = new Builder("exitTimeLoop", 2.5);
            var go = c.Parameter("Go", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Loop");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Constant(1, true, (node, 4)));
            layer.States[a].Transitions.Add(To(b, duration: .25, exitTime: true, at: .5).When(go, "if"));
            c.Input(.7, go, 1);
            return c.Case;
        }

        // A trigger used by the first layer, looked at by the second, and one
        // no transition uses.
        static Case Triggers()
        {
            var c = new Builder("triggers", 1);
            var fire = c.Parameter("Fire", "trigger");
            var idle = c.Parameter("Idle", "trigger");
            var node = c.Target(0);
            var other = c.Target(0);
            var first = c.Layer("First");
            var a = c.State(first, "A", c.Constant(1, true, (node, 0)));
            var b = c.State(first, "B", c.Constant(1, true, (node, 1)));
            first.States[a].Transitions.Add(To(b).When(fire, "if"));
            first.States[b].Transitions.Add(To(a).When(fire, "if"));
            var second = c.Layer("Second");
            var a2 = c.State(second, "A", c.Constant(1, true, (other, 0)));
            var b2 = c.State(second, "B", c.Constant(1, true, (other, 1)));
            second.States[a2].Transitions.Add(To(b2).When(fire, "if"));
            second.States[b2].Transitions.Add(To(a2).When(fire, "if"));
            c.Input(.2, fire, 1).Input(.5, idle, 1).Input(.6, fire, 1);
            return c.Case;
        }

        // Any State before the state's own transitions, with and without
        // Can Transition To Self.
        static Case AnyState()
        {
            var c = new Builder("anyState", 2);
            var mode = c.Parameter("Mode", "int");
            var node = c.Target(0);
            var layer = c.Layer("Any");
            var idle = c.State(layer, "Idle", c.Ramp(1, true, node, 0, 1));
            var one = c.State(layer, "One", c.Ramp(1, true, node, 10, 11));
            var two = c.State(layer, "Two", c.Ramp(1, true, node, 20, 21));
            layer.States[idle].Transitions.Add(To(two).When(mode, "equals", 1));
            var toOne = To(one, duration: .1).When(mode, "equals", 1);
            toOne.CanTransitionToSelf = true;
            layer.Machines[0].AnyStateTransitions.Add(toOne);
            layer.Machines[0].AnyStateTransitions.Add(To(two, duration: .1).When(mode, "equals", 2));
            layer.Machines[0].AnyStateTransitions.Add(To(idle, duration: .2).When(mode, "equals", 0));
            c.Input(.2, mode, 1).Input(.6, mode, 2).Input(1.2, mode, 0);
            return c.Case;
        }

        // Interruption by the source's and the destination's transitions,
        // ordered and not.
        static Case Interruption()
        {
            var c = new Builder("interruption", 3);
            var go = c.Parameter("Go", "int");
            var node = c.Target(0);
            var layer = c.Layer("Interrupt");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(1, true, node, 10, 11));
            var d = c.State(layer, "C", c.Ramp(1, true, node, 20, 21));
            var e = c.State(layer, "D", c.Ramp(1, true, node, 30, 31));
            var slow = To(b, duration: 1).When(go, "equals", 1);
            slow.InterruptionSource = "sourceThenDestination";
            slow.OrderedInterruption = true;
            layer.States[a].Transitions.Add(To(e, duration: .5).When(go, "equals", 3));
            layer.States[a].Transitions.Add(slow);
            layer.States[a].Transitions.Add(To(d, duration: .5).When(go, "equals", 2));
            layer.States[b].Transitions.Add(To(e, duration: .4).When(go, "equals", 4));
            c.Input(.1, go, 1).Input(.4, go, 2).Input(.6, go, 3).Input(1.8, go, 0)
             .Input(2, go, 1).Input(2.3, go, 4);
            return c.Case;
        }

        // Any State and a layer that is in a transition from a state: with an
        // interruption source other than none, Any State interrupts it, and
        // unordered, the running transition starts again while Go holds.
        static Case AnyStateInterruption(string source = "none", bool ordered = false)
        {
            var name = "anyStateInterruption" + (source == "none" ? "" : char.ToUpperInvariant(source[0]) + source.Substring(1)) + (ordered ? "Ordered" : "");
            var c = new Builder(name, 2);
            var go = c.Parameter("Go", "bool");
            var stop = c.Parameter("Stop", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Any");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(1, true, node, 10, 11));
            var d = c.State(layer, "C", c.Ramp(1, true, node, 20, 21));
            var slow = To(b, duration: 1).When(go, "if");
            slow.InterruptionSource = source;
            slow.OrderedInterruption = ordered;
            layer.States[a].Transitions.Add(slow);
            layer.Machines[0].AnyStateTransitions.Add(To(d, duration: .5).When(stop, "if"));
            c.Input(.2, go, 1).Input(.6, stop, 1).Input(.7, stop, 0);
            return c.Case;
        }

        // A transition interrupted by Any State: anyStateRestart, one from
        // Any State that starts again while Go holds, although it may not
        // transition to itself (the layer is still in A); anyStateOrdered and
        // anyStateUnordered, one from Any State that only the earlier ones
        // interrupt, or every one; anyStateBeforeDestination, Any State before
        // the destination's transition.
        static Case AnyStateRunning(string name)
        {
            var c = new Builder(name, 2.5);
            var go = c.Parameter("Go", "bool");
            var stop = c.Parameter("Stop", "bool");
            var stop2 = c.Parameter("Stop2", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Any");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(1, true, node, 10, 11));
            var d = c.State(layer, "C", c.Ramp(1, true, node, 20, 21));
            var e = c.State(layer, "D", c.Ramp(1, true, node, 30, 31));
            var anys = layer.Machines[0].AnyStateTransitions;
            if (name == "anyStateBeforeDestination")
            {
                var slow = To(b, duration: 1).When(go, "if");
                slow.InterruptionSource = "destination";
                layer.States[a].Transitions.Add(slow);
                layer.States[a].Transitions.Add(To(e, duration: .5).When(stop, "if"));
                layer.States[b].Transitions.Add(To(e, duration: .5).When(stop, "if"));
                anys.Add(To(d, duration: .5).When(stop, "if"));
                c.Input(.2, go, 1).Input(.3, go, 0).Input(.6, stop, 1).Input(.7, stop, 0);
                return c.Case;
            }
            var fromAny = To(b, duration: 1).When(go, "if");
            fromAny.InterruptionSource = "source";
            fromAny.OrderedInterruption = name == "anyStateOrdered";
            if (name == "anyStateRestart")
            {
                anys.Add(fromAny);
                c.Input(.2, go, 1).Input(.5, go, 0);
                return c.Case;
            }
            anys.Add(To(d, duration: .5).When(stop, "if"));
            anys.Add(fromAny);
            anys.Add(To(e, duration: .5).When(stop2, "if"));
            c.Input(.2, go, 1).Input(.3, go, 0).Input(.5, stop2, 1).Input(.6, stop2, 0)
             .Input(1.6, go, 1).Input(1.7, go, 0).Input(1.9, stop, 1).Input(2, stop, 0);
            return c.Case;
        }

        // A transition with an exit time and no ordered interruption, longer
        // than a loop of its source: it starts itself again when the source
        // crosses the exit time once more, then the source holds still and
        // it is not crossed again.
        static Case ExitTimeRestart(bool condition)
        {
            var c = new Builder(condition ? "exitTimeRestartGo" : "exitTimeRestart", 5);
            var go = c.Parameter("Go", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Exit");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(1, true, node, 10, 11));
            var slow = To(b, duration: 2, exitTime: true, at: .9);
            if (condition) slow.When(go, "if");
            slow.InterruptionSource = "source";
            layer.States[a].Transitions.Add(slow);
            if (condition) c.Input(.5, go, 1);
            return c.Case;
        }

        // Write Defaults off, with a state that writes one target only and a
        // transition between states that write different targets.
        static Case WriteDefaultsOff()
        {
            var c = new Builder("writeDefaultsOff", 2.5);
            var step = c.Parameter("Step", "int");
            var first = c.Target(0);
            var second = c.Target(1);
            var layer = c.Layer("Hold");
            var a = c.State(layer, "A", c.Constant(1, true, (first, 3)), writeDefaults: false);
            var b = c.State(layer, "B", c.Constant(1, true, (second, 5)), writeDefaults: false);
            var empty = c.State(layer, "Empty", -1, writeDefaults: false);
            var both = c.State(layer, "Both", c.Constant(1, true, (first, -2), (second, 7)), writeDefaults: false);
            layer.States[a].Transitions.Add(To(b, duration: .5).When(step, "equals", 1));
            layer.States[b].Transitions.Add(To(empty, duration: .4).When(step, "equals", 2));
            layer.States[empty].Transitions.Add(To(both, duration: .4).When(step, "equals", 3));
            layer.States[both].Transitions.Add(To(a, duration: .4).When(step, "equals", 4));
            c.Input(.2, step, 1).Input(.9, step, 2).Input(1.4, step, 3).Input(1.9, step, 4);
            return c.Case;
        }

        // Layer weights: an override layer over a lower one, and a layer under
        // weight 1 over a target no lower layer writes, with Write Defaults on
        // and off.
        static Case Layers()
        {
            var c = new Builder("layers", 1.5);
            var go = c.Parameter("Go", "bool");
            var shared = c.Target(0);
            var alone = c.Target(1);
            var held = c.Target(0);
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Ramp(1, true, shared, 1, 2));
            var middle = c.Layer("Middle", weight: .3);
            var m1 = c.State(middle, "B", c.Constant(1, true, (shared, 5), (alone, 9)));
            var m2 = c.State(middle, "Empty", -1);
            middle.States[m1].Transitions.Add(To(m2, duration: .2).When(go, "if"));
            var top = c.Layer("Top", weight: .6);
            var t1 = c.State(top, "C", c.Constant(1, true, (held, 4)), writeDefaults: false);
            var t2 = c.State(top, "Empty", -1, writeDefaults: false);
            top.States[t1].Transitions.Add(To(t2, duration: .2).When(go, "if"));
            c.Input(.7, go, 1);
            return c.Case;
        }

        // A 1D tree with thresholds out of order and children of different
        // lengths and time scales, and direct trees, normalized and not.
        static Case Trees()
        {
            var c = new Builder("trees", 3);
            var blend = c.Parameter("Blend", "float");
            var w0 = c.Parameter("W0", "float");
            var w1 = c.Parameter("W1", "float");
            var oneD = c.Target(0);
            var direct = c.Target(1);
            var normalized = c.Target(0);
            var low = c.Ramp(1, true, oneD, 0, 1);
            var mid = c.Ramp(2, true, oneD, 10, 12);
            var high = c.Ramp(.5, true, oneD, 20, 21);
            var tree = c.Tree("1d", blend, (high, 1, 1), (low, -1, 1), (mid, 0, 2));
            var first = c.Layer("1D");
            c.State(first, "Tree", tree);
            var d0 = c.Constant(1, true, (direct, 4), (normalized, 4));
            var d1 = c.Ramp(1, true, direct, 0, 10);
            var d2 = c.Ramp(1, true, normalized, 0, 10);
            var second = c.Layer("Direct");
            c.State(second, "Tree", c.Tree("direct", -1, (d0, w0, 1), (d1, w1, 1)));
            var third = c.Layer("Normalized");
            var n = c.Tree("direct", -1, (d0, w0, 1), (d2, w1, 1));
            c.Case.Data.Motions[n].NormalizedBlendValues = true;
            c.State(third, "Tree", n);
            for (var i = 0; i <= 12; i++) c.Input(i * .2, blend, -1.5 + i * .25);
            c.Input(0, w0, .5).Input(0, w1, 1).Input(.8, w0, 2).Input(1.6, w0, 0).Input(1.6, w1, 0).Input(2.2, w1, -.5);
            return c.Case;
        }

        // 2D Freeform Cartesian trees. Each child writes 1 to its own target
        // and 0 to the others, so a target holds its child's weight.
        static Case FreeformCartesian()
        {
            var c = new Builder("freeformCartesian", 4);
            var x = c.Parameter("X", "float");
            var y = c.Parameter("Y", "float");
            var w = c.Parameter("W", "float", 1);
            // Three children on one line and one off it.
            c.State(c.Layer("Line"), "Tree", c.Weights(x, y, (0, 0), (.8, 0), (1, 0), (0, 1)));
            // A child inside the others.
            c.State(c.Layer("Centre"), "Tree", c.Weights(x, y, (-1, -1), (1, -1), (-1, 1), (1, 1), (0, 0)));
            // Uneven distances, and a child far from the rest.
            c.State(c.Layer("Uneven"), "Tree", c.Weights(x, y, (0, 0), (.3, .1), (1, .2), (.4, .9), (-.6, .5), (3, 3)));
            c.State(c.Layer("One"), "Tree", c.Weights(x, y, (.5, .5)));
            // Two children at one position.
            c.State(c.Layer("Same"), "Tree", c.Weights(x, y, (0, 0), (0, 0), (1, 0), (0, 1)));
            // Children of other lengths and time scales, a 1D tree as a child,
            // a child without a motion, and the tree inside a direct tree.
            for (var kind = 0; kind < 5; kind++)
            {
                var length = c.Target(0);
                var a = c.Ramp(1, true, length, 0, 1);
                var b = c.Ramp(2, true, length, 10, 12);
                var d = c.Ramp(.5, true, length, 20, 21);
                int tree;
                if (kind == 1) tree = c.Tree2D("2dFreeformCartesian", x, y, (a, 0, 0, 1), (b, 1, 0, 2), (d, 0, 1, .5));
                else if (kind == 2) tree = c.Tree2D("2dFreeformCartesian", x, y, (a, 0, 0, 1), (b, 1, 0, 1), (c.Tree("1d", x, (a, 0, 1), (d, 1, 1)), 0, 1, 1));
                else if (kind == 3) tree = c.Tree2D("2dFreeformCartesian", x, y, (a, 0, 0, 1), (b, 1, 0, 1), (-1, 0, 1, 1));
                else tree = c.Tree2D("2dFreeformCartesian", x, y, (a, 0, 0, 1), (b, 1, 0, 1), (d, 0, 1, 1));
                c.State(c.Layer(new[] { "Lengths", "Scales", "Inner", "Missing", "Direct" }[kind]), "Tree", kind == 4 ? c.Tree("direct", -1, (tree, w, 1)) : tree);
            }
            // Inside and outside the children's positions.
            for (var i = 0; i <= 80; i++)
            {
                double u = i * .137, v = i * .291;
                c.Input(i * .05, x, -1.5 + 4 * (u - Math.Floor(u))).Input(i * .05, y, -1.5 + 4 * (v - Math.Floor(v)));
            }
            c.Input(1.2, w, .5).Input(2.2, w, 0).Input(2.6, w, 1);
            return c.Case;
        }

        // 2D Simple Directional trees, read as FreeformCartesian reads its trees.
        static Case SimpleDirectional()
        {
            var c = new Builder("simpleDirectional", 7);
            var x = c.Parameter("X", "float");
            var y = c.Parameter("Y", "float");
            var w = c.Parameter("W", "float", 1);
            const string type = "2dSimpleDirectional";
            // Four directions and the centre.
            c.State(c.Layer("Four"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1)));
            // The same without the centre.
            c.State(c.Layer("NoCentre"), "Tree", c.Weights(type, x, y, (1, 0), (0, 1), (-1, 0), (0, -1)));
            // Uneven angles and distances, the children not in angle order.
            c.State(c.Layer("Uneven"), "Tree", c.Weights(type, x, y, (0, 2), (.5, -.3), (0, 0), (-1.5, .4), (1.2, .9), (-.2, -1)));
            // A gap of more than 180 degrees between two neighbours.
            c.State(c.Layer("Gap"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1)));
            c.State(c.Layer("GapNoCentre"), "Tree", c.Weights(type, x, y, (1, 0), (-.5, 1)));
            // One direction, with and without the centre.
            c.State(c.Layer("One"), "Tree", c.Weights(type, x, y, (0, 0), (.5, .5)));
            c.State(c.Layer("OneNoCentre"), "Tree", c.Weights(type, x, y, (.5, .5)));
            c.State(c.Layer("Centre"), "Tree", c.Weights(type, x, y, (0, 0)));
            // Two children in one direction.
            c.State(c.Layer("Same"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (2, 0), (0, 1), (-1, -1)));
            // Two children at the centre.
            c.State(c.Layer("Centres"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1), (0, 0), (-1, -1)));
            Timing(c, type, x, y, w);
            Sweep(c, x, y, (0, 0), (1, 0), (.5, 0), (2, 0), (0, 1), (-1, 0), (0, -1), (.5, .5), (-.75, -.75), (0, 2), (1.2, .9), (.25, 0), (0, .001));
            return c.Case;
        }

        // 2D Freeform Directional trees, read as FreeformCartesian reads its trees.
        static Case FreeformDirectional()
        {
            var c = new Builder("freeformDirectional", 7);
            var x = c.Parameter("X", "float");
            var y = c.Parameter("Y", "float");
            var w = c.Parameter("W", "float", 1);
            const string type = "2dFreeformDirectional";
            // Eight directions around the centre.
            c.State(c.Layer("Ring"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (.7, .7), (0, 1), (-.7, .7), (-1, 0), (-.7, -.7), (0, -1), (.7, -.7)));
            // Two rings: several children in one direction.
            c.State(c.Layer("Rings"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1), (2, 0), (0, 2), (-2, 0), (0, -2)));
            // No centre.
            c.State(c.Layer("NoCentre"), "Tree", c.Weights(type, x, y, (1, 0), (0, 1), (-1, 0), (0, -1)));
            // Uneven angles and distances.
            c.State(c.Layer("Uneven"), "Tree", c.Weights(type, x, y, (0, 0), (.5, -.3), (0, 2), (-1.5, .4), (1.2, .9), (-.5, -1), (3, -2)));
            // Directions of other lengths without the centre.
            c.State(c.Layer("Radii"), "Tree", c.Weights(type, x, y, (1, 0), (0, 10), (-10, 0), (0, -1)));
            // Gaps of 135 and 270 degrees between two neighbours.
            c.State(c.Layer("Wide"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1), (-1, -1)));
            c.State(c.Layer("Gap"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1)));
            // One direction, with and without the centre.
            c.State(c.Layer("One"), "Tree", c.Weights(type, x, y, (0, 0), (.5, .5)));
            c.State(c.Layer("OneNoCentre"), "Tree", c.Weights(type, x, y, (.5, .5)));
            // Two children at one position.
            c.State(c.Layer("Same"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (1, 0), (0, 1), (-1, 0), (0, -1)));
            // Two children at the centre.
            c.State(c.Layer("Centres"), "Tree", c.Weights(type, x, y, (0, 0), (1, 0), (0, 1), (0, 0), (-1, 0), (0, -1)));
            Timing(c, type, x, y, w);
            Sweep(c, x, y, (0, 0), (-.7071, .7071), (1, 0), (.5, 0), (2, 0), (1.5, 0), (0, 1), (-1, 0), (0, -1), (.7, .7), (.35, .35), (-.75, -.75), (0, 2), (1.2, .9), (3, -2), (0, .001));
            return c.Case;
        }

        // Children of other lengths and time scales, a 1D tree as a child, a
        // child without a motion, and the tree inside a direct tree.
        static void Timing(Builder c, string type, int x, int y, int w)
        {
            for (var kind = 0; kind < 5; kind++)
            {
                var length = c.Target(0);
                var a = c.Ramp(1, true, length, 0, 1);
                var b = c.Ramp(2, true, length, 10, 12);
                var d = c.Ramp(.5, true, length, 20, 21);
                var e = c.Ramp(1.5, true, length, 30, 33);
                var f = c.Ramp(.75, true, length, 40, 41.5);
                int tree;
                if (kind == 1) tree = c.Tree2D(type, x, y, (a, 0, 0, 1), (b, 1, 0, 2), (d, 0, 1, .5), (e, -1, 0, 1), (f, 0, -1, 1.5));
                else if (kind == 2) tree = c.Tree2D(type, x, y, (a, 0, 0, 1), (b, 1, 0, 1), (c.Tree("1d", x, (a, 0, 1), (d, 1, 1)), 0, 1, 1), (e, -1, 0, 1), (f, 0, -1, 1));
                else if (kind == 3) tree = c.Tree2D(type, x, y, (a, 0, 0, 1), (b, 1, 0, 1), (d, 0, 1, 1), (-1, .5, .5, 1), (e, -1, 0, 1), (f, 0, -1, 1));
                else tree = c.Tree2D(type, x, y, (a, 0, 0, 1), (b, 1, 0, 1), (d, 0, 1, 1), (e, -1, 0, 1), (f, 0, -1, 1));
                c.State(c.Layer(new[] { "Lengths", "Scales", "Inner", "Missing", "Direct" }[kind]), "Tree", kind == 4 ? c.Tree("direct", -1, (tree, w, 1)) : tree);
            }
            c.Input(1.2, w, .5).Input(2.2, w, 0).Input(2.6, w, 1);
        }

        // The given points first, then points around the origin, inside and
        // outside the children, each held for 0.05 seconds.
        static void Sweep(Builder c, int x, int y, params (double X, double Y)[] points)
        {
            var time = 0.0;
            foreach (var (px, py) in points)
            {
                c.Input(time, x, px).Input(time, y, py);
                time += .05;
            }
            for (var i = 0; time < 7 - 1e-9; i++, time += .05)
            {
                double u = i * .137, v = i * .291;
                c.Input(time, x, -2.5 + 5 * (u - Math.Floor(u))).Input(time, y, -2.5 + 5 * (v - Math.Floor(v)));
            }
        }

        // A sub-state machine: Entry, Exit and the parent's transitions out
        // of it, and Exit from the root.
        static Case Machines()
        {
            var c = new Builder("machines", 2.2);
            var pick = c.Parameter("Pick", "int");
            var enter = c.Parameter("Enter", "bool");
            var leave = c.Parameter("Leave", "bool");
            var back = c.Parameter("Back", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Machines");
            layer.Machines.Add(new AvatarAnimatorData.Machine { Name = "Sub", Parent = 0 });
            var idle = c.State(layer, "Idle", c.Constant(1, true, (node, 0)));
            var first = c.State(layer, "First", c.Constant(1, true, (node, 1)), machine: 1);
            var second = c.State(layer, "Second", c.Constant(1, true, (node, 2)), machine: 1);
            var after = c.State(layer, "After", c.Constant(1, true, (node, 3)));
            layer.States[idle].Transitions.Add(new AvatarAnimatorData.Transition { ToMachine = 1, Timed = true, HasFixedDuration = true, Duration = .1 }.When(enter, "if"));
            layer.Machines[1].EntryTransitions.Add(new AvatarAnimatorData.Transition { ToState = second }.When(pick, "equals", 2));
            layer.States[first].Transitions.Add(new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true, Duration = .1 }.When(leave, "if"));
            layer.States[second].Transitions.Add(new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true, Duration = .1 }.When(leave, "if"));
            layer.Machines[1].ExitTransitions.Add(new AvatarAnimatorData.Transition { ToState = after }.When(back, "ifNot"));
            layer.States[after].Transitions.Add(new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true, Duration = .1 }.When(back, "if"));
            c.Input(.1, enter, 1).Input(.3, enter, 0).Input(.3, leave, 1).Input(.6, leave, 0).Input(.6, back, 1)
             .Input(.9, back, 0).Input(.9, pick, 2).Input(.9, enter, 1).Input(1.2, enter, 0).Input(1.2, back, 1).Input(1.2, leave, 1);
            return c.Case;
        }

        // A state without a motion and one with a clip of length 0 (their
        // length), and a state's speed, cycle offset and time parameters.
        static Case StateOptions()
        {
            var c = new Builder("stateOptions", 3);
            var speed = c.Parameter("Speed", "float", 2);
            var time = c.Parameter("Time", "float", .1);
            var offset = c.Parameter("Offset", "float", .25);
            var node = c.Target(0);
            var fast = c.Target(0);
            var scrub = c.Target(0);
            var layer = c.Layer("Empty");
            var empty = c.State(layer, "Empty", -1);
            var zero = c.State(layer, "Zero", c.Constant(0, false, (node, 1)));
            var end = c.State(layer, "End", c.Constant(1, true, (node, 2)));
            layer.States[empty].Transitions.Add(To(zero, exitTime: true, at: 1));
            layer.States[zero].Transitions.Add(To(end, exitTime: true, at: 1));
            var second = c.Layer("Speed");
            var s = c.State(second, "Fast", c.Ramp(2, true, fast, 0, 2));
            second.States[s].Speed = .75;
            second.States[s].SpeedParameter = speed;
            second.States[s].CycleOffset = .5;
            second.States[s].CycleOffsetParameter = offset;
            var third = c.Layer("Time");
            var t = c.State(third, "Scrub", c.Ramp(1, false, scrub, 0, 1));
            third.States[t].TimeParameter = time;
            c.Input(1, speed, -1).Input(.5, time, .6).Input(1, time, 1.4).Input(1.5, time, -.3);
            return c.Case;
        }

        // Transitions of 0 s whose conditions hold at once: how many a layer
        // takes in one frame.
        static Case Chain()
        {
            var c = new Builder("chain", .6);
            var go = c.Parameter("Go", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Chain");
            var a = c.State(layer, "A", c.Constant(1, true, (node, 0)));
            var b = c.State(layer, "B", c.Constant(1, true, (node, 1)));
            var d = c.State(layer, "C", c.Constant(1, true, (node, 2)));
            var e = c.State(layer, "D", c.Constant(1, true, (node, 3)));
            layer.States[a].Transitions.Add(To(b).When(go, "if"));
            layer.States[b].Transitions.Add(To(d).When(go, "if"));
            layer.States[d].Transitions.Add(To(e, exitTime: true, at: 0));
            c.Input(.2, go, 1);
            return c.Case;
        }

        // A synced layer playing its own motions of the source's states.
        static Case Synced()
        {
            var c = new Builder("synced", 2);
            var go = c.Parameter("Go", "bool");
            var node = c.Target(0);
            var other = c.Target(0);
            var source = c.Layer("Source");
            var a = c.State(source, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(source, "B", c.Ramp(1, true, node, 5, 6));
            source.States[a].Transitions.Add(To(b, duration: .4).When(go, "if"));
            source.States[b].Transitions.Add(To(a, duration: .4, exitTime: true, at: .5).When(go, "ifNot"));
            var synced = c.Layer("Synced", weight: .5);
            synced.SyncedLayer = 0;
            synced.Machines[0].Name = source.Machines[0].Name;
            c.State(synced, "A", c.Ramp(.5, true, other, 10, 11));
            c.State(synced, "B", -1);
            synced.Machines[0].DefaultState = source.Machines[0].DefaultState;
            c.Input(.3, go, 1).Input(1.1, go, 0);
            return c.Case;
        }

        // The common FX shape: a moving lower layer and a toggle layer of
        // weight 1, Write Defaults off, between an empty state and a clip,
        // switched at once and over 0.25 s.
        static Case ToggleOverRamp()
        {
            var c = new Builder("toggleOverRamp", 3);
            var mode = c.Parameter("Mode", "int");
            var shared = c.Target(0);
            var alone = c.Target(1);
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Ramp(1, true, shared, 0, 2));
            var top = c.Layer("Toggle");
            var off = c.State(top, "Off", -1, writeDefaults: false);
            var on = c.State(top, "On", c.Constant(1, true, (shared, 5), (alone, 7)), writeDefaults: false);
            var slow = c.State(top, "Slow", c.Constant(1, true, (shared, 5), (alone, 7)), writeDefaults: false);
            top.States[off].Transitions.Add(To(on).When(mode, "equals", 1));
            top.States[off].Transitions.Add(To(slow, duration: .25).When(mode, "equals", 2));
            top.States[on].Transitions.Add(To(off).When(mode, "equals", 0));
            top.States[slow].Transitions.Add(To(off, duration: .25).When(mode, "equals", 0));
            c.Input(.3, mode, 1).Input(.7, mode, 0).Input(1.1, mode, 2).Input(1.6, mode, 0).Input(2.1, mode, 2).Input(2.25, mode, 0);
            return c.Case;
        }

        // The layers case and back: empty states moving to their clips again
        // in a layer of weight 0.3 (Write Defaults on) and 0.6 (off), over
        // a ramp.
        static Case LayersBack()
        {
            var c = new Builder("layersBack", 2.5);
            var go = c.Parameter("Go", "bool");
            var shared = c.Target(0);
            var alone = c.Target(1);
            var held = c.Target(0);
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Ramp(1, true, shared, 1, 2));
            var middle = c.Layer("Middle", weight: .3);
            var m1 = c.State(middle, "B", c.Ramp(1, true, shared, 5, 6));
            var m2 = c.State(middle, "Empty", -1);
            middle.States[m1].Transitions.Add(To(m2, duration: .2).When(go, "if"));
            middle.States[m2].Transitions.Add(To(m1, duration: .3).When(go, "ifNot"));
            c.State(middle, "Alone", c.Constant(1, true, (alone, 9)));
            var top = c.Layer("Top", weight: .6);
            var t1 = c.State(top, "C", c.Ramp(1, true, held, 4, 5), writeDefaults: false);
            var t2 = c.State(top, "Empty", -1, writeDefaults: false);
            top.States[t1].Transitions.Add(To(t2, duration: .2).When(go, "if"));
            top.States[t2].Transitions.Add(To(t1, duration: .3).When(go, "ifNot"));
            c.Input(.5, go, 1).Input(1.2, go, 0).Input(1.9, go, 1);
            return c.Case;
        }

        // One layer of weight 0.7 with Write Defaults on and off states, with
        // and without motions, over a ramp.
        static Case MixedWriteDefaults()
        {
            var c = new Builder("mixedWriteDefaults", 3);
            var mode = c.Parameter("Mode", "int");
            var shared = c.Target(0);
            var other = c.Target(1);
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Ramp(1, true, shared, 0, 2));
            var top = c.Layer("Mixed", weight: .7);
            var a = c.State(top, "OtherOn", c.Constant(1, true, (other, 3)));
            var b = c.State(top, "SharedOff", c.Constant(1, true, (shared, 6)), writeDefaults: false);
            var e = c.State(top, "EmptyOff", -1, writeDefaults: false);
            var f = c.State(top, "EmptyOn", -1);
            top.States[a].Transitions.Add(To(b, duration: .2).When(mode, "equals", 1));
            top.States[b].Transitions.Add(To(e, duration: .2).When(mode, "equals", 2));
            top.States[e].Transitions.Add(To(f, duration: .2).When(mode, "equals", 3));
            top.States[f].Transitions.Add(To(a, duration: .2).When(mode, "equals", 0));
            c.Input(.4, mode, 1).Input(1, mode, 2).Input(1.6, mode, 3).Input(2.2, mode, 0);
            return c.Case;
        }

        // An activeSelf target blended in and out, then held at -1 and at 0.3.
        static Case ActiveThreshold()
        {
            var c = new Builder("activeThreshold", 3);
            var mode = c.Parameter("Mode", "int");
            var node = c.Target(0, active: true);
            var layer = c.Layer("Active");
            var off = c.State(layer, "Off", c.Constant(1, true, (node, 0)));
            var on = c.State(layer, "On", c.Constant(1, true, (node, 1)));
            var negative = c.State(layer, "Negative", c.Constant(1, true, (node, -1)));
            var low = c.State(layer, "Low", c.Constant(1, true, (node, .3)));
            layer.States[off].Transitions.Add(To(on, duration: 1).When(mode, "equals", 1));
            layer.States[off].Transitions.Add(To(negative).When(mode, "equals", 2));
            layer.States[off].Transitions.Add(To(low).When(mode, "equals", 3));
            foreach (var state in new[] { on, negative, low })
                layer.States[state].Transitions.Add(To(off, duration: state == on ? .6 : 0).When(mode, "equals", 0));
            c.Input(.1, mode, 1).Input(1.3, mode, 0).Input(2.1, mode, 2).Input(2.4, mode, 0).Input(2.6, mode, 3).Input(2.8, mode, 0);
            return c.Case;
        }

        // Parameters a clip animates (M11), every state with Write Defaults:
        // Drive follows a ramp and is read by transitions, Count has a curve
        // and so stays at its default. The inputs try to change both while a
        // curve writes them and while none does.
        static Case ParameterCurves()
        {
            var c = new Builder("parameterCurves", 3);
            var go = c.Parameter("Go", "bool");
            var drive = c.Parameter("Drive", "float", .25);
            var count = c.Parameter("Count", "int", 3);
            var node = c.Target(0);
            var reader = c.Target(0);
            var driven = c.ParameterTarget(drive);
            c.ParameterTarget(count);
            var curve = c.Layer("Curve");
            var rest = c.State(curve, "Rest", c.Constant(1, true, (node, 2)));
            var ramp = c.State(curve, "Ramp", c.Ramp(1, true, driven, 0, 1));
            curve.States[rest].Transitions.Add(To(ramp, duration: .3).When(go, "if"));
            curve.States[ramp].Transitions.Add(To(rest).When(go, "ifNot"));
            var read = c.Layer("Reader");
            var below = c.State(read, "Below", c.Constant(1, true, (reader, 0)));
            var above = c.State(read, "Above", c.Constant(1, true, (reader, 1)));
            read.States[below].Transitions.Add(To(above).When(drive, "greater", .5));
            read.States[below].Transitions.Add(To(above).When(count, "notEqual", 3));
            read.States[above].Transitions.Add(To(below).When(drive, "less", .5));
            c.Input(.2, drive, .9).Input(.5, go, 1).Input(1.2, count, 5).Input(1.6, drive, .1).Input(2.2, go, 0).Input(2.5, drive, .9);
            return c.Case;
        }

        // M11 with M6: a layer without Write Defaults keeps a parameter a clip
        // animates in an empty state, and the host cannot change it there.
        static Case ParameterHold()
        {
            var c = new Builder("parameterHold", 2);
            var go = c.Parameter("Go", "bool");
            var held = c.Parameter("Held", "float", .1);
            var kept = c.ParameterTarget(held);
            var hold = c.Layer("Hold");
            var holding = c.State(hold, "Holding", c.Ramp(1, true, kept, .2, .8), writeDefaults: false);
            var empty = c.State(hold, "Empty", -1, writeDefaults: false);
            hold.States[holding].Transitions.Add(To(empty, duration: .2).When(go, "if"));
            hold.States[empty].Transitions.Add(To(holding, duration: .2).When(go, "ifNot"));
            c.Input(.5, go, 1).Input(.9, held, .3).Input(1.3, go, 0).Input(1.6, held, .9);
            return c.Case;
        }

        // M11 with M6: a state without Write Defaults whose motion does not
        // write a parameter the layer animates, over a layer that does not
        // write it either, and a tree that writes it with part of its weight.
        static Case ParameterPass()
        {
            var c = new Builder("parameterPass", 2.5);
            var go = c.Parameter("Go", "bool");
            var mix = c.Parameter("Mix", "float", .5);
            var held = c.Parameter("Held", "float", .1);
            var node = c.Target(0);
            var kept = c.ParameterTarget(held);
            var layer = c.Layer("Pass");
            var writing = c.State(layer, "Writing", c.Ramp(1, true, kept, .2, .8), writeDefaults: false);
            var other = c.State(layer, "Other", c.Constant(1, true, (node, 3)), writeDefaults: false);
            var part = c.State(layer, "Part", c.Tree("1d", mix, (c.Constant(1, true, (kept, .9)), 0, 1), (c.Constant(1, true, (node, 1)), 1, 1)), writeDefaults: false);
            layer.States[writing].Transitions.Add(To(other, duration: .2).When(go, "if"));
            layer.States[other].Transitions.Add(To(part, duration: .2).When(go, "ifNot"));
            c.Input(.5, go, 1).Input(1.2, go, 0);
            return c.Case;
        }

        // Layers whose weight drops to 0 at once after they wrote (as a 0 s
        // Layer Control does), one at a time and then together, over a ramp:
        // a Write Defaults motion, a motion without Write Defaults that passes
        // the others through, and an empty state without Write Defaults
        // holding its own values. Targets no other layer writes rest at 1.
        static Case LayerZero()
        {
            var c = new Builder("layerZero", 3);
            var go = c.Parameter("Go", "bool");
            var shared = c.Target(0);
            var aloneOn = c.Target(1);
            var aloneOff = c.Target(1);
            var held = c.Target(1);
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Ramp(1, true, shared, 0, 2));
            var on = c.Layer("On");
            c.State(on, "A", c.Constant(1, true, (shared, 5), (aloneOn, 7)));
            var off = c.Layer("Off");
            c.State(off, "B", c.Constant(1, true, (aloneOff, 4)), writeDefaults: false);
            var hold = c.Layer("Hold");
            var full = c.State(hold, "C", c.Constant(1, true, (held, 6), (shared, -3)), writeDefaults: false);
            var empty = c.State(hold, "Empty", -1, writeDefaults: false);
            hold.States[full].Transitions.Add(To(empty).When(go, "if"));
            c.Input(.2, go, 1);
            c.Weight(.5, 1, 0).Weight(.9, 2, 0).Weight(1.3, 3, 0);
            foreach (var layer in new[] { 1, 2, 3 }) c.Weight(1.7, layer, 1).Weight(2.1, layer, .5).Weight(2.5, layer, 0);
            return c.Case;
        }

        // VRChat's hand gestures in the gesture controller (the FX has no
        // layer): a base layer without a mask moving the spine and an arm, a
        // left hand layer whose mask lets only the left fingers move, and a
        // right hand layer without Write Defaults whose mask lets only the
        // right fingers move. Each hand's gesture also writes an arm muscle
        // its mask keeps off. Muscles are HumanTrait.MuscleName's indexes.
        static Case Hands()
        {
            var c = new Builder("hands", 2.4);
            var left = c.Parameter("GestureLeft", "int");
            var right = c.Parameter("GestureRight", "int");
            var sway = c.Parameter("Sway", "float");
            var spine = c.Muscle(0);
            var leftArm = c.Muscle(39);
            var rightArm = c.Muscle(48);
            var leftThumb = c.Muscle(55);
            var leftIndex = c.Muscle(59);
            var rightIndex = c.Muscle(79);
            c.Controller("fx");
            c.Controller("gesture");
            var body = c.Layer("AllParts");
            var swing = c.Ramp(1.5, true, leftArm, -.5, .5);
            var still = c.Constant(1, true, (spine, .3), (leftArm, .2));
            c.State(body, "Idle", c.Tree("1d", sway, (swing, 0, 1), (still, 1, 1)));
            var leftHand = c.Layer("Left Hand", mask: Mask(new[] { 7 }));
            var open = c.State(leftHand, "Open", c.Constant(1, true, (leftThumb, 0), (leftIndex, 0)));
            var fist = c.State(leftHand, "Fist", c.Constant(1, true, (leftThumb, .8), (leftIndex, -.6), (leftArm, 1)));
            leftHand.States[open].Transitions.Add(To(fist, duration: .2).When(left, "equals", 1));
            leftHand.States[fist].Transitions.Add(To(open, duration: .15).When(left, "notEqual", 1));
            var rightHand = c.Layer("Right Hand", mask: Mask(new[] { 8 }));
            var idle = c.State(rightHand, "Idle", c.Constant(1, true, (rightIndex, 0)), writeDefaults: false);
            var point = c.State(rightHand, "Point", c.Constant(1, true, (rightIndex, .7), (rightArm, -1)), writeDefaults: false);
            rightHand.States[idle].Transitions.Add(To(point, duration: .25).When(right, "equals", 2));
            rightHand.States[point].Transitions.Add(To(idle, duration: .1).When(right, "notEqual", 2));
            c.Input(.3, left, 1).Input(.7, right, 2).Input(1, sway, .5).Input(1.2, left, 0).Input(1.6, right, 0).Input(1.9, left, 1);
            return c.Case;
        }

        // Which body part each muscle belongs to: a base layer sets every
        // muscle, and then a layer for each body part with muscles (Body to
        // RightFingers), whose mask lets only that part move, sets every
        // muscle again to a value of its own.
        static Case MaskParts()
        {
            var c = new Builder("maskParts", .1);
            var muscles = Enumerable.Range(0, AvatarAnimatorData.MuscleCount).Select(c.Muscle).ToArray();
            c.Controller("fx");
            c.Controller("gesture");
            // Small values: with every muscle moved, Unity reads the limbs
            // back up to 2e-3 off at .5.
            c.State(c.Layer("Base"), "All", c.Constant(1, true, muscles.Select(m => (m, -.01)).ToArray()));
            for (var part = 1; part <= 8; part++)
            {
                var value = .02 * part;
                c.State(c.Layer("Part " + part, mask: Mask(new[] { part })), "All", c.Constant(1, true, muscles.Select(m => (m, value)).ToArray()));
            }
            return c.Case;
        }

        // Transform masks: an upper layer whose mask names only the first
        // position's transform, and one whose mask names none. A mask keeps
        // a layer off the positions of the transforms it leaves out, but
        // not off a GameObject, a blend shape or a parameter a clip animates
        // there. Unity's masks also name the root, left out.
        static Case NodeMask()
        {
            var c = new Builder("nodeMask", 1.6);
            var go = c.Parameter("Go", "bool");
            var blend = c.Parameter("Blend", "float");
            var first = c.Position(0);
            var second = c.Position(0);
            var shown = c.Target(1, active: true);
            var face = c.Morph(0);
            var value = c.ParameterTarget(blend);
            var allParts = Enumerable.Range(0, AvatarAnimatorData.BodyPartCount).ToArray();
            var bottom = c.Layer("Bottom");
            c.State(bottom, "A", c.Constant(1, true, (first, 1), (second, 1), (shown, 1), (face, 10), (value, .25)));
            var top = c.Layer("Top", mask: Mask(allParts, new[] { c.Case.Data.Targets[first].Node }));
            var a = c.State(top, "A", c.Constant(1, true, (first, 3), (second, 4), (shown, 0), (face, 30), (value, .5)));
            var b = c.State(top, "B", c.Constant(1, true, (first, 5), (second, 6), (shown, 1), (face, 60), (value, .75)));
            top.States[a].Transitions.Add(To(b, duration: .3).When(go, "if"));
            top.States[b].Transitions.Add(To(a, duration: .2).When(go, "ifNot"));
            var none = c.Layer("None", mask: Mask(allParts, new int[0]));
            c.State(none, "C", c.Constant(1, true, (second, 9), (value, .1)), writeDefaults: false);
            c.Input(.4, go, 1).Input(1, go, 0);
            return c.Case;
        }

        // VRChat's three playable layers together (M15): the Gesture's left
        // hand drives Emote from its Fist, which the Action and the FX read.
        // The Action plays at weight 0 until its Wave's Playable Layer
        // Control blends it to 1, and its Wait's back to 0. The FX's base
        // layer has no mask, so the arm muscle its clip writes does not move
        // (M16). The Gesture's body layer and the Action's Wave both move the
        // spine and the left arm.
        static Case GestureAction()
        {
            var c = new Builder("gestureAction", 2.8);
            var left = c.Parameter("GestureLeft", "int");
            var emote = c.Parameter("Emote", "int");
            var spine = c.Muscle(0);
            var leftArm = c.Muscle(39);
            var leftIndex = c.Muscle(59);
            var lamp = c.Position(0);
            c.Controller("fx");
            var fx = c.Layer("Lamp");
            var dark = c.State(fx, "Dark", c.Constant(1, true, (lamp, 0), (leftArm, 0)));
            var lit = c.State(fx, "Lit", c.Constant(1, true, (lamp, 2), (leftArm, -1)));
            fx.States[dark].Transitions.Add(To(lit, duration: .1).When(emote, "equals", 1));
            fx.States[lit].Transitions.Add(To(dark, duration: .1).When(emote, "notEqual", 1));
            c.Controller("gesture");
            c.State(c.Layer("Body"), "Idle", c.Constant(1, true, (spine, .2), (leftArm, .1)));
            var hand = c.Layer("Left Hand", mask: Mask(new[] { 7 }));
            var open = c.State(hand, "Open", c.Constant(1, true, (leftIndex, 0)));
            var fist = c.State(hand, "Fist", c.Constant(1, true, (leftIndex, -.6)));
            c.Driver(hand, open, (emote, 0));
            c.Driver(hand, fist, (emote, 1));
            hand.States[open].Transitions.Add(To(fist, duration: .2).When(left, "equals", 1));
            hand.States[fist].Transitions.Add(To(open, duration: .15).When(left, "notEqual", 1));
            c.Controller("action");
            var action = c.Layer("Action");
            var wait = c.State(action, "Wait", c.Constant(1, true, (spine, 0), (leftArm, 0)));
            var wave = c.State(action, "Wave", c.Ramp(1, true, leftArm, .3, .9));
            c.PlayableControl(action, wait, "action", 0, .25);
            c.PlayableControl(action, wave, "action", 1, .3);
            action.States[wait].Transitions.Add(To(wave, duration: .2).When(emote, "equals", 1));
            action.States[wave].Transitions.Add(To(wait, duration: .2).When(emote, "notEqual", 1));
            c.Input(.3, left, 1).Input(1.3, left, 0).Input(1.9, left, 1);
            return c.Case;
        }

        // An Action over the Gesture with masks: its base layer's mask lets
        // only the left arm move, and its second layer at half weight only
        // the body. Pose's Playable Layer Control blends the Action to 1 over
        // 0.2 s when Go enters it.
        static Case ActionMask()
        {
            var c = new Builder("actionMask", 1.4);
            var go = c.Parameter("Go", "bool");
            var spine = c.Muscle(0);
            var leftArm = c.Muscle(39);
            var rightArm = c.Muscle(48);
            c.Controller("fx");
            c.Controller("gesture");
            c.State(c.Layer("Body"), "Idle", c.Constant(1, true, (spine, .2), (leftArm, .1), (rightArm, .3)));
            c.Controller("action");
            var arm = c.Layer("Arm", mask: Mask(new[] { 5 }));
            var wait = c.State(arm, "Wait", c.Constant(1, true, (leftArm, 0)));
            var pose = c.State(arm, "Pose", c.Constant(1, true, (spine, .6), (leftArm, .9), (rightArm, -.5)));
            c.PlayableControl(arm, pose, "action", 1, .2);
            arm.States[wait].Transitions.Add(To(pose, duration: .25).When(go, "if"));
            c.State(c.Layer("Lean", .5, Mask(new[] { 1 })), "Lean", c.Constant(1, true, (spine, -.4), (rightArm, .8)));
            c.Input(.4, go, 1);
            return c.Case;
        }

        // Transform masks on a humanoid Animator: an upper layer whose mask
        // names only the first position's transform, over a base layer
        // moving both positions, a GameObject, a blend shape and a muscle.
        // The mask keeps the second position, and lets the muscle of its
        // body part through.
        static Case HumanoidNodeMask()
        {
            var c = new Builder("humanoidNodeMask", 1.2);
            var go = c.Parameter("Go", "bool");
            var first = c.Position(0);
            var second = c.Position(0);
            var shown = c.Target(1, active: true);
            var face = c.Morph(0);
            var spine = c.Muscle(0);
            var leftArm = c.Muscle(39);
            c.Controller("fx");
            c.Controller("gesture");
            c.State(c.Layer("Bottom"), "A", c.Constant(1, true, (first, 1), (second, 1), (shown, 1), (face, 10), (spine, .1), (leftArm, .1)));
            var top = c.Layer("Top", mask: Mask(new[] { 1 }, new[] { c.Case.Data.Targets[first].Node }));
            var a = c.State(top, "A", c.Constant(1, true, (first, 3), (second, 4), (shown, 0), (face, 30), (spine, .4), (leftArm, .5)));
            var b = c.State(top, "B", c.Constant(1, true, (first, 5), (second, 6), (shown, 1), (face, 60), (spine, -.2), (leftArm, -.5)));
            top.States[a].Transitions.Add(To(b, duration: .3).When(go, "if"));
            c.Input(.4, go, 1);
            return c.Case;
        }

        // Hand muscles under a humanoid layer without Write Defaults whose
        // resting state has an empty clip (clip) or no motion: the hand layers
        // below move the fingers, and the top layer moves to a state writing
        // them and back. Unity passes the hands through while the empty clip
        // plays, but gives the rest on the empty side of each transition; the
        // state without a motion gives the rest until the layer has written
        // the hands, and then holds what it last wrote.
        static Case HandsOverEmpty(string name, bool clip)
        {
            var c = new Builder(name, 1.6);
            var left = c.Parameter("GestureLeft", "int");
            var go = c.Parameter("Go", "bool");
            var leftThumb = c.Muscle(55);
            var leftIndex = c.Muscle(59);
            var rightIndex = c.Muscle(79);
            c.Controller("fx");
            c.Controller("gesture");
            c.Layer("AllParts", mask: Mask(new[] { 7, 8 }));
            var hand = c.Layer("Left Hand", mask: Mask(new[] { 7 }));
            var idle = c.State(hand, "Idle", c.Constant(1, true, (leftThumb, -1.5), (leftIndex, .4)), writeDefaults: false);
            var fist = c.State(hand, "Fist", c.Constant(1, true, (leftThumb, .8), (leftIndex, -.6)), writeDefaults: false);
            hand.States[idle].Transitions.Add(To(fist, duration: .2).When(left, "equals", 1));
            hand.States[fist].Transitions.Add(To(idle, duration: .15).When(left, "notEqual", 1));
            c.State(c.Layer("Right Hand", mask: Mask(new[] { 8 })), "Idle", c.Constant(1, true, (rightIndex, .5)), writeDefaults: false);
            var top = c.Layer("Control", mask: Mask(new[] { 7, 8 }));
            var empty = c.State(top, "Empty", clip ? c.Constant(1, true) : -1, writeDefaults: false);
            var crouch = c.State(top, "Crouch", c.Constant(1, true, (leftThumb, .3), (leftIndex, -.9), (rightIndex, -.9)), writeDefaults: false);
            top.States[empty].Transitions.Add(To(crouch, duration: .2).When(go, "if"));
            // Not a whole number of frames: a transition ending exactly on a frame
            // can stay just under its end in Unity's float time that frame.
            top.States[crouch].Transitions.Add(To(empty, duration: .18).When(go, "ifNot"));
            c.Input(.3, left, 1).Input(.7, go, 1).Input(1.1, go, 0);
            return c.Case;
        }

        // A humanoid layer whose clips write one finger's muscle, over a hand
        // layer moving two: Unity gives the other finger the rest while the
        // upper clip plays, with or without Write Defaults, as it poses the
        // layer whole, and the lower layer's again once the upper layer moves
        // to an empty clip.
        static Case HandsUnderThumb()
        {
            var c = new Builder("handsUnderThumb", 1.4);
            var go = c.Parameter("Go", "bool");
            var leftThumb = c.Muscle(55);
            var leftIndex = c.Muscle(59);
            c.Controller("fx");
            c.Controller("gesture");
            c.Layer("AllParts", mask: Mask(new[] { 7 }));
            c.State(c.Layer("Left Hand", mask: Mask(new[] { 7 })), "Idle", c.Constant(1, true, (leftThumb, -1.5), (leftIndex, .4)), writeDefaults: false);
            var top = c.Layer("Thumb", mask: Mask(new[] { 7 }));
            var thumb = c.State(top, "Thumb", c.Constant(1, true, (leftThumb, .6)), writeDefaults: false);
            var defaults = c.State(top, "Defaults", c.Constant(1, true, (leftThumb, -.3)));
            var empty = c.State(top, "Empty", c.Constant(1, true), writeDefaults: false);
            top.States[thumb].Transitions.Add(To(defaults, duration: .17).When(go, "if"));
            top.States[defaults].Transitions.Add(To(empty, duration: .17).When(go, "ifNot"));
            c.Input(.4, go, 1).Input(.85, go, 0);
            return c.Case;
        }

        // The body parts a mask lets move and, unless null, the nodes of the
        // transforms it lets move (it names the others too).
        static AvatarAnimatorData.Mask Mask(int[] humanoid, int[] nodes = null)
        {
            var mask = new AvatarAnimatorData.Mask();
            mask.Humanoid.AddRange(humanoid);
            if (nodes != null) mask.Nodes = new List<int>(nodes);
            return mask;
        }

        static AvatarAnimatorData.Transition To(int state, double duration = 0, bool exitTime = false, double at = 0, bool fixedDuration = true) =>
            new AvatarAnimatorData.Transition { ToState = state, Timed = true, Duration = duration, HasFixedDuration = fixedDuration, HasExitTime = exitTime, ExitTime = at };

        static AvatarAnimatorData.Transition When(this AvatarAnimatorData.Transition transition, int parameter, string mode, double threshold = 0)
        {
            transition.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = parameter, Mode = mode, Threshold = threshold });
            return transition;
        }

        sealed class Builder
        {
            internal readonly Case Case;
            AvatarAnimatorData Data => Case.Data;
            AvatarAnimatorData.Controller controller;

            internal Builder(string name, double seconds) { Case = new Case { Name = name, Seconds = seconds }; }

            internal Builder Input(double time, int parameter, double value) { Case.Inputs.Add((time, parameter, value)); return this; }

            internal Builder Weight(double time, int layer, double weight) { Case.LayerWeights.Add((time, layer, weight)); return this; }

            internal int Parameter(string name, string type, double value = 0)
            {
                Data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = name, Type = type, Default = value, Origin = "animator" });
                return Data.Parameters.Count - 1;
            }

            internal int Target(int rest, bool active = false)
            {
                Data.Targets.Add(new AvatarAnimatorData.Target { Node = Data.Targets.Count, Rest = rest });
                if (active) Case.ActiveTargets.Add(Data.Targets.Count - 1);
                return Data.Targets.Count - 1;
            }

            // The x of a node's position, which a transform mask filters.
            internal int Position(int rest)
            {
                Data.Targets.Add(new AvatarAnimatorData.Target { Node = Data.Targets.Count, Property = "translation", Channel = "x", Rest = rest });
                return Data.Targets.Count - 1;
            }

            // A node's first blend shape, which Unity plays on a SkinnedMeshRenderer.
            internal int Morph(int rest)
            {
                Data.Targets.Add(new AvatarAnimatorData.Target { Node = Data.Targets.Count, Property = "morph", Rest = rest, End = 100 });
                return Data.Targets.Count - 1;
            }

            // A Humanoid muscle (HumanTrait.MuscleName's index) at the T-pose's 0.
            internal int Muscle(int muscle)
            {
                Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "muscle", Muscle = muscle, Rest = 0 });
                return Data.Targets.Count - 1;
            }

            // The layers after this go to a new controller of the playable.
            internal Builder Controller(string playable)
            {
                controller = new AvatarAnimatorData.Controller { Playable = playable };
                Data.Controllers.Add(controller);
                return this;
            }

            // A parameter a clip animates, at the controller's default.
            internal int ParameterTarget(int parameter)
            {
                Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = parameter, Rest = Data.Parameters[parameter].Default });
                return Data.Targets.Count - 1;
            }

            internal int Constant(double duration, bool loop, params (int Target, double Value)[] tracks)
            {
                var clip = Clip(duration, loop);
                foreach (var (target, value) in tracks)
                {
                    var curve = new ExpressionAnimationData.Curve();
                    curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                    if (duration > 0) curve.Keys.Add(new ExpressionAnimationData.Key { Time = duration, Value = value, InTangent = 0, OutTangent = 0 });
                    Track(clip, target, curve);
                }
                return Motion(clip);
            }

            // A straight line from start to end over the clip.
            internal int Ramp(double duration, bool loop, int target, double start, double end)
            {
                var clip = Clip(duration, loop);
                var slope = (end - start) / duration;
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = start, InTangent = slope, OutTangent = slope });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = duration, Value = end, InTangent = slope, OutTangent = slope });
                Track(clip, target, curve);
                return Motion(clip);
            }

            AvatarAnimatorData.Clip Clip(double duration, bool loop)
            {
                var clip = new AvatarAnimatorData.Clip { Name = "Clip " + Data.Clips.Count, Duration = duration, Loop = loop };
                Data.Clips.Add(clip);
                return clip;
            }

            static void Track(AvatarAnimatorData.Clip clip, int target, ExpressionAnimationData.Curve curve)
            {
                var track = new AvatarAnimatorData.Track { Target = target };
                track.Curves.Add(curve);
                clip.Tracks.Add(track);
            }

            int Motion(AvatarAnimatorData.Clip clip)
            {
                Data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = Data.Clips.IndexOf(clip) });
                return Data.Motions.Count - 1;
            }

            // Children are (motion, threshold or direct parameter, time scale).
            internal int Tree(string blendType, int parameter, params (int Motion, double Threshold, double TimeScale)[] children)
            {
                var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = blendType };
                if (blendType == "1d") tree.Parameters.Add(parameter);
                foreach (var (motion, threshold, timeScale) in children)
                {
                    var child = new AvatarAnimatorData.Child { Motion = motion, TimeScale = timeScale };
                    if (blendType == "direct") child.DirectParameter = (int)threshold;
                    else child.Threshold = threshold;
                    tree.Children.Add(child);
                }
                Data.Motions.Add(tree);
                return Data.Motions.Count - 1;
            }

            // Children are (motion, x, y, time scale).
            internal int Tree2D(string blendType, int x, int y, params (int Motion, double X, double Y, double TimeScale)[] children)
            {
                var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = blendType };
                tree.Parameters.Add(x);
                tree.Parameters.Add(y);
                foreach (var child in children)
                    tree.Children.Add(new AvatarAnimatorData.Child { Motion = child.Motion, X = child.X, Y = child.Y, TimeScale = child.TimeScale });
                Data.Motions.Add(tree);
                return Data.Motions.Count - 1;
            }

            // A Freeform Cartesian tree with a child at each position, and a target for each child's weight.
            internal int Weights(int x, int y, params (double X, double Y)[] positions) => Weights("2dFreeformCartesian", x, y, positions);

            internal int Weights(string blendType, int x, int y, params (double X, double Y)[] positions)
            {
                var targets = positions.Select(_ => Target(0)).ToArray();
                return Tree2D(blendType, x, y, positions.Select((position, i) =>
                    (Constant(1, true, targets.Select((target, t) => (target, t == i ? 1.0 : 0.0)).ToArray()), position.X, position.Y, 1.0)).ToArray());
            }

            internal AvatarAnimatorData.Layer Layer(string name, double weight = 1, AvatarAnimatorData.Mask mask = null)
            {
                if (controller == null) { controller = new AvatarAnimatorData.Controller(); Data.Controllers.Add(controller); }
                var layer = new AvatarAnimatorData.Layer { Name = name, Weight = controller.Layers.Count == 0 ? 1 : weight, Mask = mask };
                layer.Machines.Add(new AvatarAnimatorData.Machine { Name = name });
                controller.Layers.Add(layer);
                return layer;
            }

            // A Parameter Driver on the state that sets each parameter.
            internal void Driver(AvatarAnimatorData.Layer layer, int state, params (int Parameter, double Value)[] sets)
            {
                var behaviour = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
                foreach (var (parameter, value) in sets) behaviour.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = parameter, Value = value });
                layer.States[state].Behaviours.Add(behaviour);
            }

            // A Playable Layer Control on the state.
            internal void PlayableControl(AvatarAnimatorData.Layer layer, int state, string playable, double goal, double duration) =>
                layer.States[state].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = playable, GoalWeight = goal, BlendDuration = duration });

            internal int State(AvatarAnimatorData.Layer layer, string name, int motion, bool writeDefaults = true, int machine = 0)
            {
                layer.States.Add(new AvatarAnimatorData.State { Name = name, Machine = machine, Motion = motion, WriteDefaults = writeDefaults });
                var index = layer.States.Count - 1;
                if (layer.Machines[machine].DefaultState < 0) layer.Machines[machine].DefaultState = index;
                return index;
            }
        }
    }
}
