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

        public sealed class Case
        {
            public string Name;
            public double Seconds;
            public readonly AvatarAnimatorData Data = new AvatarAnimatorData();
            // Parameter values set before the first frame that starts at or after Time.
            public readonly List<(double Time, int Parameter, double Value)> Inputs = new List<(double, int, double)>();
            // Targets Unity plays as GameObject.activeSelf; the others are a
            // float (Transform.localPosition.x) so blends compare exactly.
            public readonly HashSet<int> ActiveTargets = new HashSet<int>();
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
        public static List<(int Parameter, double Value)>[] Inputs(Case c, double[] steps)
        {
            var result = new List<(int, double)>[steps.Length];
            var start = 0.0;
            var next = 0;
            var ordered = c.Inputs.OrderBy(input => input.Time).ToList();
            for (var i = 0; i < steps.Length; i++)
            {
                result[i] = new List<(int, double)>();
                while (next < ordered.Count && ordered[next].Time <= start + 1e-9)
                {
                    result[i].Add((ordered[next].Parameter, ordered[next].Value));
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
            values = new double[steps.Length][];
            parameters = new double[steps.Length][];
            for (var i = 0; i < steps.Length; i++)
            {
                foreach (var (parameter, value) in inputs[i]) evaluator.Set(parameter, value);
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
                    if (!(Math.Abs(values[i][t] - unityValues[i][t]) <= ValueTolerance))
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
            yield return WriteDefaultsOff();
            yield return Layers();
            yield return Trees();
            yield return Machines();
            yield return StateOptions();
            yield return Chain();
            yield return Synced();
            yield return ActiveThreshold();
            yield return ToggleOverRamp();
            yield return LayersBack();
            yield return MixedWriteDefaults();
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

        // Any State taking a layer that is in a transition.
        static Case AnyStateInterruption()
        {
            var c = new Builder("anyStateInterruption", 2);
            var go = c.Parameter("Go", "bool");
            var stop = c.Parameter("Stop", "bool");
            var node = c.Target(0);
            var layer = c.Layer("Any");
            var a = c.State(layer, "A", c.Ramp(1, true, node, 0, 1));
            var b = c.State(layer, "B", c.Ramp(1, true, node, 10, 11));
            var d = c.State(layer, "C", c.Ramp(1, true, node, 20, 21));
            layer.States[a].Transitions.Add(To(b, duration: 1).When(go, "if"));
            layer.Machines[0].AnyStateTransitions.Add(To(d, duration: .5).When(stop, "if"));
            c.Input(.2, go, 1).Input(.6, stop, 1).Input(.7, stop, 0);
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

            internal AvatarAnimatorData.Layer Layer(string name, double weight = 1)
            {
                if (controller == null) { controller = new AvatarAnimatorData.Controller(); Data.Controllers.Add(controller); }
                var layer = new AvatarAnimatorData.Layer { Name = name, Weight = controller.Layers.Count == 0 ? 1 : weight };
                layer.Machines.Add(new AvatarAnimatorData.Machine { Name = name });
                controller.Layers.Add(layer);
                return layer;
            }

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
