using System;
using System.Collections.Generic;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;

// AvatarAnimatorEvaluator without Unity: the rules it plays by, M1..M10. The
// comparison with Unity's Animator is the recordings of Tests/AnimatorComparison
// that AvatarAnimatorComparisonReplay plays back.
public static class AvatarAnimatorEvaluatorTests
{
    static int count;
    static void Check(bool value, string what) { count++; if (!value) throw new Exception("Avatar animator evaluator assertion " + count + ": " + what); }
    static void Near(double actual, double expected, string what) => Check(Math.Abs(actual - expected) < 1e-9, what + " = " + actual + ", expected " + expected);
    static void Throws<T>(Action action, string what) where T : Exception
    { var thrown = false; try { action(); } catch (T) { thrown = true; } Check(thrown, what); }

    const double Frame = 1.0 / 60;

    // A small builder over AvatarAnimatorData. Every data set goes through
    // Write and Read, so the tests only play what the reader accepts.
    sealed class Builder
    {
        internal readonly AvatarAnimatorData Data = new AvatarAnimatorData();
        internal AvatarAnimatorData.Controller Controller;

        internal int Parameter(string name, string type, double value = 0)
        {
            Data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = name, Type = type, Default = value, Origin = "animator" });
            return Data.Parameters.Count - 1;
        }

        internal int Target(int rest = 0)
        {
            Data.Targets.Add(new AvatarAnimatorData.Target { Node = Data.Targets.Count, Rest = rest });
            return Data.Targets.Count - 1;
        }

        // A clip motion holding target = value for the whole clip.
        internal int Clip(double duration, bool loop, params (int target, double value)[] tracks)
        {
            var clip = new AvatarAnimatorData.Clip { Name = "clip " + Data.Clips.Count, Duration = duration, Loop = loop };
            foreach (var (target, value) in tracks)
            {
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                if (duration > 0) curve.Keys.Add(new ExpressionAnimationData.Key { Time = duration, Value = value, InTangent = 0, OutTangent = 0 });
                var track = new AvatarAnimatorData.Track { Target = target };
                track.Curves.Add(curve);
                clip.Tracks.Add(track);
            }
            Data.Clips.Add(clip);
            Data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = Data.Clips.Count - 1 });
            return Data.Motions.Count - 1;
        }

        // A clip whose target ramps linearly from 0 at the start to `end` at its end.
        internal int Ramp(double duration, bool loop, int target, double end)
        {
            var clip = new AvatarAnimatorData.Clip { Name = "ramp " + Data.Clips.Count, Duration = duration, Loop = loop };
            var curve = new ExpressionAnimationData.Curve();
            var slope = end / duration;
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = slope, OutTangent = slope, InWeight = 1.0 / 3, OutWeight = 1.0 / 3 });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = duration, Value = end, InTangent = slope, OutTangent = slope, InWeight = 1.0 / 3, OutWeight = 1.0 / 3 });
            var track = new AvatarAnimatorData.Track { Target = target };
            track.Curves.Add(curve);
            clip.Tracks.Add(track);
            Data.Clips.Add(clip);
            Data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = Data.Clips.Count - 1 });
            return Data.Motions.Count - 1;
        }

        internal int Tree(string blendType, int parameter, params (int motion, double threshold)[] children)
        {
            var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = blendType };
            if (blendType == "1d") tree.Parameters.Add(parameter);
            foreach (var (motion, threshold) in children)
            {
                var child = new AvatarAnimatorData.Child { Motion = motion, Threshold = threshold };
                if (blendType == "direct") child.DirectParameter = (int)threshold;
                tree.Children.Add(child);
            }
            Data.Motions.Add(tree);
            return Data.Motions.Count - 1;
        }

        internal AvatarAnimatorData.Layer Layer(string name, double weight = 1)
        {
            if (Controller == null) { Controller = new AvatarAnimatorData.Controller(); Data.Controllers.Add(Controller); }
            var layer = new AvatarAnimatorData.Layer { Name = name, Weight = Controller.Layers.Count == 0 ? 1 : weight };
            layer.Machines.Add(new AvatarAnimatorData.Machine { Name = name });
            Controller.Layers.Add(layer);
            return layer;
        }

        internal static int State(AvatarAnimatorData.Layer layer, string name, int motion, bool writeDefaults = true, int machine = 0)
        {
            layer.States.Add(new AvatarAnimatorData.State { Name = name, Machine = machine, Motion = motion, WriteDefaults = writeDefaults });
            var index = layer.States.Count - 1;
            if (layer.Machines[machine].DefaultState < 0) layer.Machines[machine].DefaultState = index;
            return index;
        }

        internal AvatarAnimatorEvaluator Build(Func<double> random = null) =>
            new AvatarAnimatorEvaluator(AvatarAnimatorData.Read(AvatarAnimatorData.Write(Data)), random ?? (() => 0));
    }

    static AvatarAnimatorData.Transition To(int state, double duration = 0, bool exitTime = false, double at = 0, bool fixedDuration = true)
        => new AvatarAnimatorData.Transition { ToState = state, Timed = true, Duration = duration, HasFixedDuration = fixedDuration, HasExitTime = exitTime, ExitTime = at };

    static AvatarAnimatorData.Transition When(this AvatarAnimatorData.Transition transition, int parameter, string mode, double threshold = 0)
    {
        transition.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = parameter, Mode = mode, Threshold = threshold });
        return transition;
    }

    static void Run(AvatarAnimatorEvaluator evaluator, double seconds, double dt = Frame)
    {
        var frames = (int)Math.Round(seconds / dt);
        for (var i = 0; i < frames; i++) evaluator.Advance(dt);
    }

    public static void Run()
    {
        Toggle();
        TransitionBlends();
        ExitTimes();
        Triggers();
        AnyState();
        SubMachines();
        WriteDefaults();
        LayerWeights();
        ParameterDrivers();
        LayerControl();
        BlendTrees();
        ValuesPassUnclamped();
        SyncedLayers();
        Interruption();
        StateOptions();
        Unsupported();
        Arguments();
        NoAllocation();
        Console.WriteLine("Avatar animator evaluator: " + count + " assertions passed.");
    }

    // The toggle every exported avatar has: a bool switches a node between two states.
    static void Toggle()
    {
        var b = new Builder();
        var hat = b.Parameter("Hat", "bool");
        var node = b.Target(rest: 1);
        var layer = b.Layer("Hat");
        var off = Builder.State(layer, "Off", b.Clip(0, false, (node, 0)));
        var on = Builder.State(layer, "On", b.Clip(0, false, (node, 1)));
        layer.States[off].Transitions.Add(To(on).When(hat, "if"));
        layer.States[on].Transitions.Add(To(off).When(hat, "ifNot"));
        var e = b.Build();
        Near(e.Values[node], 1, "before the first frame each target is at its rest");
        e.Advance(Frame);
        Check(e.State(0) == off && e.Values[node] == 0 && e.Written[node], "the layer starts in its default state");
        e.Set(hat, 1);
        e.Advance(Frame);
        Check(e.State(0) == on && e.Values[node] == 1 && AvatarAnimatorEvaluator.Active(e.Values[node]), "a 0 s transition switches in the frame its condition holds");
        e.Set(hat, 0);
        e.Advance(Frame);
        Check(e.State(0) == off && !AvatarAnimatorEvaluator.Active(e.Values[node]), "and back");
        Check(e.ParameterIndex("Hat") == hat && e.ParameterIndex("missing") == -1, "parameters are found by name");
    }

    static void TransitionBlends()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var node = b.Target();
        var layer = b.Layer("Fade");
        var a = Builder.State(layer, "A", b.Clip(1, true, (node, 0)));
        var c = Builder.State(layer, "B", b.Clip(1, true, (node, 1)));
        layer.States[a].Transitions.Add(To(c, duration: .5).When(go, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(.1);
        Check(e.NextState(0) == c && e.TransitionProgress(0) == 0, "M2: the transition starts at the end of the frame its condition is seen");
        e.Advance(.25);
        Near(e.TransitionProgress(0), .5, "progress");
        Near(e.Values[node], .5, "a target blends linearly between the two states");
        Check(AvatarAnimatorEvaluator.Active(.3) && AvatarAnimatorEvaluator.Active(-1) && !AvatarAnimatorEvaluator.Active(0), "M1: active at any value but 0");
        e.Advance(.25);
        Check(e.State(0) == c && e.NextState(0) == -1 && e.Values[node] == 1, "the transition ends after its duration");

        // A duration in normalized time scales with the source state's length.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        node = b.Target();
        layer = b.Layer("Fade");
        a = Builder.State(layer, "A", b.Clip(2, true, (node, 0)));
        c = Builder.State(layer, "B", b.Clip(1, true, (node, 1)));
        layer.States[a].Transitions.Add(To(c, duration: .25, fixedDuration: false).When(go, "if"));
        e = b.Build();
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Near(e.TransitionProgress(0), .5, "0.25 of a 2 s state is 0.5 s");
    }

    static void ExitTimes()
    {
        // Exit time 1 on a 1 s clip without conditions: once, at its end.
        var b = new Builder();
        var node = b.Target();
        var layer = b.Layer("Once");
        var a = Builder.State(layer, "A", b.Clip(1, false, (node, 0)));
        var c = Builder.State(layer, "B", b.Clip(1, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: 1));
        var e = b.Build();
        Run(e, .5, .1);
        Check(e.State(0) == a, "before the exit time");
        Run(e, .6, .1);
        Check(e.State(0) == c, "after the exit time");

        // Exit time under 1 with a condition: checked on each loop.
        b = new Builder();
        var go = b.Parameter("Go", "bool");
        node = b.Target();
        layer = b.Layer("Loop");
        a = Builder.State(layer, "A", b.Clip(1, true, (node, 0)));
        c = Builder.State(layer, "B", b.Clip(1, true, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: .5).When(go, "if"));
        e = b.Build();
        Run(e, .7, .1);
        e.Set(go, 1);
        Run(e, .7, .1);
        Check(e.State(0) == a, "the exit time of this loop has passed");
        Run(e, .2, .1);
        Check(e.State(0) == c, "the next loop's exit time takes it");

        // M2: exit time 0 is not met at the start, only when the first loop ends.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Zero");
        a = Builder.State(layer, "A", -1);
        c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: 0));
        e = b.Build();
        e.Advance(Frame);
        Check(e.State(0) == a, "exit time 0 waits");
        Run(e, 1);
        Check(e.State(0) == c, "exit time 0 at the end of the first loop");

        // M9: a state without a motion is 1 s long.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Empty");
        a = Builder.State(layer, "A", -1);
        c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: 1));
        e = b.Build();
        Run(e, .9, .1);
        Check(e.State(0) == a, "M9: empty state before 1 s");
        Run(e, .2, .1);
        Check(e.State(0) == c, "M9: empty state after 1 s");

        // A transition with neither an exit time nor a condition is never taken.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Never");
        a = Builder.State(layer, "A", b.Clip(0, false, (node, 0)));
        c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c));
        e = b.Build();
        Run(e, 1);
        Check(e.State(0) == a, "a transition without exit time or condition");
    }

    static void Triggers()
    {
        var b = new Builder();
        var fire = b.Parameter("Fire", "trigger");
        var node = b.Target();
        var other = b.Target();
        var first = b.Layer("First");
        var a = Builder.State(first, "A", b.Clip(0, false, (node, 0)));
        var c = Builder.State(first, "B", b.Clip(0, false, (node, 1)));
        first.States[a].Transitions.Add(To(c).When(fire, "if"));
        var second = b.Layer("Second");
        var a2 = Builder.State(second, "A", b.Clip(0, false, (other, 0)));
        var c2 = Builder.State(second, "B", b.Clip(0, false, (other, 1)));
        second.States[a2].Transitions.Add(To(c2).When(fire, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(fire, 1);
        Check(e.Get(fire) == 1, "a trigger stays set");
        e.Advance(Frame);
        Check(e.State(0) == c && e.Get(fire) == 0, "the transition that uses a trigger resets it");
        Check(e.State(1) == c2, "M4: a later layer sees the trigger an earlier one used in the same frame");
    }

    static void AnyState()
    {
        var b = new Builder();
        var mode = b.Parameter("Mode", "int");
        var node = b.Target();
        var layer = b.Layer("Any");
        var idle = Builder.State(layer, "Idle", b.Clip(1, true, (node, 0)));
        var one = Builder.State(layer, "One", b.Clip(1, true, (node, 1)));
        var two = Builder.State(layer, "Two", b.Clip(1, true, (node, 2)));
        layer.States[idle].Transitions.Add(To(two).When(mode, "equals", 1));
        layer.Machines[0].AnyStateTransitions.Add(To(one).When(mode, "equals", 1));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(mode, 1);
        e.Advance(Frame);
        Check(e.State(0) == one, "Any State is looked at before the state's own transitions");
        Run(e, .5);
        Check(e.State(0) == one && e.NormalizedTime(0) > .4, "without Can Transition To Self it does not restart");

        layer.Machines[0].AnyStateTransitions[0].CanTransitionToSelf = true;
        e = b.Build();
        e.Advance(Frame);
        e.Set(mode, 1);
        Run(e, .5);
        Check(e.State(0) == one && e.NormalizedTime(0) < .05, "with it, it restarts every frame its condition holds");
    }

    static void SubMachines()
    {
        // Root: Idle -> machine Sub; Sub's Entry picks by an int; Sub's states
        // leave through Exit, and the parent's transitions out of Sub decide.
        var b = new Builder();
        var pick = b.Parameter("Pick", "int");
        var enter = b.Parameter("Enter", "bool");
        var leave = b.Parameter("Leave", "bool");
        var back = b.Parameter("Back", "bool");
        var node = b.Target();
        var layer = b.Layer("Machines");
        layer.Machines.Add(new AvatarAnimatorData.Machine { Name = "Sub", Parent = 0 });
        var idle = Builder.State(layer, "Idle", b.Clip(0, false, (node, 0)));
        var first = Builder.State(layer, "First", b.Clip(0, false, (node, 1)), machine: 1);
        var second = Builder.State(layer, "Second", b.Clip(0, false, (node, 2)), machine: 1);
        var after = Builder.State(layer, "After", b.Clip(0, false, (node, 3)));
        layer.States[idle].Transitions.Add(new AvatarAnimatorData.Transition { ToMachine = 1, Timed = true, HasFixedDuration = true }.When(enter, "if"));
        layer.Machines[1].EntryTransitions.Add(new AvatarAnimatorData.Transition { ToState = second }.When(pick, "equals", 2));
        var exit = new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true }.When(leave, "if");
        layer.States[first].Transitions.Add(exit);
        layer.States[second].Transitions.Add(new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true }.When(leave, "if"));
        layer.Machines[1].ExitTransitions.Add(new AvatarAnimatorData.Transition { ToState = after }.When(back, "ifNot"));
        layer.States[after].Transitions.Add(new AvatarAnimatorData.Transition { Exit = true, Timed = true, HasFixedDuration = true }.When(back, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(enter, 1);
        e.Advance(Frame);
        Check(e.State(0) == first, "no entry transition passes: the machine's default state");
        e.Set(enter, 0);
        e.Set(leave, 1);
        e.Advance(Frame);
        Check(e.State(0) == after, "Exit takes the parent's transition out of the machine");
        e.Set(leave, 0);
        e.Set(back, 1);
        e.Advance(Frame);
        Check(e.State(0) == idle, "Exit from the root goes back through Entry to the default state");
        e.Set(back, 0);
        e.Set(pick, 2);
        e.Set(enter, 1);
        e.Advance(Frame);
        Check(e.State(0) == second, "an entry transition that passes picks the state");
        e.Set(enter, 0);
        e.Set(back, 1);
        e.Set(leave, 1);
        e.Advance(Frame);
        Check(e.State(0) == idle, "no transition out of the machine passes: the parent's Entry");
    }

    static void WriteDefaults()
    {
        // WD off: a state that does not write the node leaves it as it was.
        var b = new Builder();
        var on = b.Parameter("On", "bool");
        var node = b.Target();
        var layer = b.Layer("Hold");
        var empty = Builder.State(layer, "Empty", -1, writeDefaults: false);
        var lit = Builder.State(layer, "Lit", b.Clip(0, false, (node, 1)), writeDefaults: false);
        layer.States[empty].Transitions.Add(To(lit).When(on, "if"));
        layer.States[lit].Transitions.Add(To(empty).When(on, "ifNot"));
        var e = b.Build();
        e.Advance(Frame);
        Check(e.Values[node] == 0 && !e.Written[node], "WD off, never written: rest");
        e.Set(on, 1);
        e.Advance(Frame);
        e.Set(on, 0);
        e.Advance(Frame);
        Check(e.State(0) == empty && e.Values[node] == 1 && e.Written[node], "M6: WD off keeps applying the last value");

        // WD on: the same state writes the node's rest.
        foreach (var state in layer.States) state.WriteDefaults = true;
        e = b.Build();
        e.Advance(Frame);
        e.Set(on, 1);
        e.Advance(Frame);
        e.Set(on, 0);
        e.Advance(Frame);
        Check(e.Values[node] == 0 && e.Written[node], "WD on writes the rest");
    }

    static void LayerWeights()
    {
        var b = new Builder();
        var node = b.Target();
        var bottom = b.Layer("Bottom");
        Builder.State(bottom, "A", b.Clip(0, false, (node, 1)));
        var top = b.Layer("Top", weight: .25);
        Builder.State(top, "B", b.Clip(0, false, (node, 5)));
        var e = b.Build();
        e.Advance(Frame);
        Near(e.Values[node], 1 + (5 - 1) * .25, "an override layer blends over the layers below by its weight");
        Near(e.LayerWeight(0), 1, "the first layer is always at weight 1");
    }

    static void ParameterDrivers()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var number = b.Parameter("Number", "int", 3);
        var amount = b.Parameter("Amount", "float", .5);
        var flag = b.Parameter("Flag", "bool");
        var copied = b.Parameter("Copied", "float");
        var rolled = b.Parameter("Rolled", "int");
        var seen = b.Parameter("Seen", "bool");
        var node = b.Target();
        var other = b.Target();
        var layer = b.Layer("Driver");
        var a = Builder.State(layer, "A", -1);
        var c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c).When(go, "if"));
        var driver = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "add", Parameter = number, Value = 2.9 });
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = flag, Value = .2 });
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "copy", Parameter = copied, Source = amount, ConvertRange = true, SourceMin = 0, SourceMax = 1, DestMin = 10, DestMax = 20 });
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = rolled, ValueMin = 2, ValueMax = 4 });
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = seen, Value = 1 });
        layer.States[c].Behaviours.Add(driver);
        var later = b.Layer("Later");
        var waiting = Builder.State(later, "Waiting", b.Clip(0, false, (other, 0)));
        var reacted = Builder.State(later, "Reacted", b.Clip(0, false, (other, 1)));
        later.States[waiting].Transitions.Add(To(reacted).When(seen, "if"));
        var e = b.Build(() => .999);
        e.Advance(Frame);
        Check(e.Get(number) == 3, "drivers run on entering their state only");
        e.Set(go, 1);
        e.Advance(Frame);
        Check(e.Get(number) == 5, "add, truncated to an int");
        Check(e.Get(flag) == 1, "set, any value but 0 is true");
        Near(e.Get(copied), 15, "copy with a range");
        Check(e.Get(rolled) == 4, "random int includes the maximum");
        Check(e.State(1) == reacted, "M8: a later layer sees the driver's change in the same frame");
    }

    static void LayerControl()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var node = b.Target();
        var first = b.Layer("Control");
        var a = Builder.State(first, "A", -1);
        var c = Builder.State(first, "B", -1);
        first.States[a].Transitions.Add(To(c).When(go, "if"));
        first.States[c].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "layerControl", Playable = "fx", Layer = 1, GoalWeight = 1, BlendDuration = .5 });
        var second = b.Layer("Faded", weight: 0);
        Builder.State(second, "On", b.Clip(0, false, (node, 1)));
        var e = b.Build();
        e.Advance(Frame);
        Check(e.Values[node] == 0 && !e.Written[node], "a layer at weight 0 writes nothing");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Near(e.LayerWeight(1), .5, "layer control blends the weight over its duration");
        Near(e.Values[node], .5, "the layer's values follow the weight");
        e.Advance(.5);
        Near(e.LayerWeight(1), 1, "and stops at the goal");
    }

    static void BlendTrees()
    {
        var b = new Builder();
        var blend = b.Parameter("Blend", "float");
        var node = b.Target();
        var low = b.Clip(1, true, (node, 2));
        var high = b.Clip(1, true, (node, 6));
        var tree = b.Tree("1d", blend, (high, 1), (low, 0));
        var layer = b.Layer("1D");
        Builder.State(layer, "Tree", tree);
        var e = b.Build();
        e.Set(blend, .25);
        e.Advance(Frame);
        Near(e.Values[node], 3, "1D: between the neighbouring thresholds, in threshold order");
        e.Set(blend, -3);
        e.Advance(Frame);
        Near(e.Values[node], 2, "1D: clamped below");
        e.Set(blend, 9);
        e.Advance(Frame);
        Near(e.Values[node], 6, "1D: clamped above");

        // Direct: each child at its parameter's value, not normalized; a child
        // that does not write a target gives the default (WD on: rest).
        b = new Builder();
        var w0 = b.Parameter("W0", "float");
        var w1 = b.Parameter("W1", "float");
        node = b.Target(rest: 1);
        var second = b.Target();
        var first = b.Clip(1, true, (node, 4));
        var other = b.Clip(1, true, (second, 10));
        tree = b.Tree("direct", -1, (first, w0), (other, w1));
        layer = b.Layer("Direct");
        Builder.State(layer, "Tree", tree);
        e = b.Build();
        e.Set(w0, .5);
        e.Set(w1, 1);
        e.Advance(Frame);
        Near(e.Values[node], .5 * 4 + 1 * 1, "direct: the other child gives the rest");
        Near(e.Values[second], 1 * 10 + .5 * 0, "direct: weights add up");
        e.Set(w0, 0);
        e.Set(w1, 0);
        e.Advance(Frame);
        Near(e.Values[node], 1, "direct: all weights 0 write what an empty state writes (WD on: rest)");
    }

    // Values are not clamped: negative values and values over 100 pass as the
    // clips hold them (a blendShape target, once written, is in 0..100 units).
    static void ValuesPassUnclamped()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var node = b.Target();
        var layer = b.Layer("Range");
        var a = Builder.State(layer, "Below", b.Clip(0, false, (node, -20)));
        var c = Builder.State(layer, "Above", b.Clip(0, false, (node, 150)));
        layer.States[a].Transitions.Add(To(c, duration: 1).When(go, "if"));
        var e = b.Build();
        e.Advance(Frame);
        Near(e.Values[node], -20, "a negative value passes");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.5);
        Near(e.Values[node], 65, "the blend between them is not clamped");
        e.Advance(1);
        Near(e.Values[node], 150, "a value over 100 passes");
    }

    static void SyncedLayers()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var mark = b.Parameter("Mark", "bool");
        var node = b.Target();
        var other = b.Target();
        var source = b.Layer("Source");
        var a = Builder.State(source, "A", b.Clip(0, false, (node, 0)));
        var c = Builder.State(source, "B", b.Clip(0, false, (node, 1)));
        source.States[a].Transitions.Add(To(c, duration: .5).When(go, "if"));
        var synced = b.Layer("Synced");
        synced.SyncedLayer = 0;
        Builder.State(synced, "A", b.Clip(0, false, (other, 10)));
        var c2 = Builder.State(synced, "B", b.Clip(0, false, (other, 20)));
        var driver = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = mark, Value = 1 });
        synced.States[c2].Behaviours.Add(driver);
        // The synced layer's machine copy has no transitions of its own.
        var e = b.Build();
        e.Advance(Frame);
        Near(e.Values[other], 10, "a synced layer plays its own motion of the source's state");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Near(e.Values[other], 15, "and blends with the source's transition");
        Check(e.Get(mark) == 1, "its own behaviours run when the source enters the state");
    }

    static void Interruption()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var stop = b.Parameter("Stop", "bool");
        var node = b.Target();
        var layer = b.Layer("Interrupt");
        var a = Builder.State(layer, "A", b.Clip(1, true, (node, 0)));
        var c = Builder.State(layer, "B", b.Clip(1, true, (node, 1)));
        var d = Builder.State(layer, "C", b.Clip(1, true, (node, 4)));
        var slow = To(c, duration: 1).When(go, "if");
        slow.InterruptionSource = "source";
        layer.States[a].Transitions.Add(slow);
        layer.States[a].Transitions.Add(To(d, duration: 1).When(stop, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.5);
        Near(e.Values[node], .5, "halfway to B");
        e.Set(stop, 1);
        e.Advance(Frame);
        Check(e.NextState(0) == d && e.TransitionProgress(0) == 0, "the source's other transition interrupts");
        e.Advance(.5);
        Near(e.Values[node], .5 + (4 - .5) * .5, "M5: from a still copy of the interrupted blend");

        // Interruption source none: the same transition is not interrupted.
        slow.InterruptionSource = "none";
        e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        e.Set(stop, 1);
        e.Advance(.5);
        Check(e.NextState(0) == c, "no interruption");
    }

    static void StateOptions()
    {
        // Speed parameter and time parameter.
        var b = new Builder();
        var speed = b.Parameter("Speed", "float", 2);
        var time = b.Parameter("Time", "float", .25);
        var node = b.Target();
        var other = b.Target();
        var layer = b.Layer("Speed");
        var s = Builder.State(layer, "Fast", b.Ramp(1, false, node, 1));
        layer.States[s].SpeedParameter = speed;
        layer.States[s].Speed = .5;
        var second = b.Layer("Time");
        var t = Builder.State(second, "Scrub", b.Ramp(1, false, other, 1));
        second.States[t].TimeParameter = time;
        var e = b.Build();
        e.Advance(.25);
        Near(e.NormalizedTime(0), .25, "speed times the speed parameter");
        Near(e.Values[node], .25, "the clip is sampled at the normalized time");
        Near(e.Values[other], .25, "a time parameter sets the normalized time");
        e.Set(time, .75);
        e.Advance(.25);
        Near(e.Values[other], .75, "and follows the parameter");
    }

    static void Unsupported()
    {
        var b = new Builder();
        var node = b.Target();
        var layer = b.Layer("Base");
        Builder.State(layer, "A", b.Clip(0, false, (node, 1)));
        var additive = b.Layer("Additive");
        additive.Blending = "additive";
        Builder.State(additive, "B", -1);
        Check(!AvatarAnimatorEvaluator.Supports(b.Data, out var reason) && reason != null, "additive layers");
        Throws<NotSupportedException>(() => new AvatarAnimatorEvaluator(b.Data, () => 0), "the evaluator refuses what it does not support");
        additive.Blending = "override";
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out reason) && reason == null, "supported");
        b.Data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "unreadParameter", Count = 1 });
        Check(!AvatarAnimatorEvaluator.Supports(b.Data, out _), "omitted conditions change the transitions");
        b.Data.Omitted.Clear();
        b.Data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "blendShape", Count = 3 });
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "omitted curves do not");
        var blend = b.Parameter("X", "float");
        var tree = b.Tree("1d", blend, (0, 0));
        b.Data.Motions[tree].BlendType = "2dSimpleDirectional";
        b.Data.Motions[tree].Parameters.Add(blend);
        Check(!AvatarAnimatorEvaluator.Supports(b.Data, out _), "2D trees");
    }

    static void Arguments()
    {
        var b = new Builder();
        var f = b.Parameter("F", "float");
        var i = b.Parameter("I", "int");
        var t = b.Parameter("T", "trigger");
        var e = b.Build();
        Throws<ArgumentOutOfRangeException>(() => e.Set(f, double.NaN), "NaN");
        Throws<ArgumentOutOfRangeException>(() => e.Advance(-1), "negative time");
        Throws<ArgumentOutOfRangeException>(() => e.Advance(double.PositiveInfinity), "infinite time");
        e.Set(f, .1);
        Check(e.Get(f) == (double)(float).1, "a float parameter is single precision");
        e.Set(i, -2.7);
        Check(e.Get(i) == -2, "an int parameter is truncated");
        e.Set(t, 5);
        Check(e.Get(t) == 1, "a trigger is set");
        e.Advance(Frame);
        Check(e.LayerCount == 0, "no controller: nothing plays");
    }

    static void NoAllocation()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var blend = b.Parameter("Blend", "float", .5);
        var node = b.Target();
        var layer = b.Layer("Busy");
        var a = Builder.State(layer, "A", b.Tree("1d", blend, (b.Clip(1, true, (node, 0)), 0), (b.Clip(1, true, (node, 1)), 1)));
        var c = Builder.State(layer, "B", b.Clip(1, true, (node, 2)));
        layer.States[a].Transitions.Add(To(c, duration: .1).When(go, "if"));
        layer.States[c].Transitions.Add(To(a, duration: .1).When(go, "ifNot"));
        layer.Machines[0].AnyStateTransitions.Add(To(c, duration: .1).When(go, "if"));
        var e = b.Build();
        e.Advance(Frame);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 2000; frame++)
        {
            if (frame % 30 == 0) e.Set(go, frame / 30 % 2);
            e.Advance(Frame);
        }
        Check(GC.GetAllocatedBytesForCurrentThread() == before, "Advance allocates nothing");
    }
}
