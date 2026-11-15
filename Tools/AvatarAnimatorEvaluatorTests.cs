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
        PassThroughThenHold();
        LayerWeights();
        ParameterDrivers();
        ParameterCurves();
        LayerControl();
        LayerAtZero();
        BlendTrees();
        FreeformCartesianTrees();
        DirectionalTrees();
        ValuesPassUnclamped();
        Morphs();
        SyncedLayers();
        Interruption();
        StateOptions();
        Unsupported();
        Controllers();
        ControllerOmitted();
        Arguments();
        Settled();
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

    // A layer that passes the layers below through (WD off, a motion, weight 1) keeps no
    // value of its own while it does; the first frame it stops, its own value is rebuilt
    // as the layers below it left the target in the frame before, not the layers above.
    static void PassThroughThenHold()
    {
        var b = new Builder();
        var hold = b.Parameter("Hold", "bool");
        var node = b.Target();
        var own = b.Target();
        var bottom = b.Layer("Bottom");
        Builder.State(bottom, "Ramp", b.Ramp(1, true, node, 1));
        var middle = b.Layer("Middle");
        var pass = Builder.State(middle, "Pass", b.Clip(0, false, (own, 1)), writeDefaults: false);
        var held = Builder.State(middle, "Held", -1, writeDefaults: false);
        middle.States[pass].Transitions.Add(To(held).When(hold, "if"));
        var top = b.Layer("Top", weight: .5);
        Builder.State(top, "Seven", b.Clip(0, false, (node, 7)));
        var e = b.Build();
        Run(e, .3);
        var before = e.Values[node];
        e.Advance(Frame);
        Check(e.Values[node] != before, "the bottom layer moves the node every frame");
        before = e.Values[node];
        e.Set(hold, 1);
        e.Advance(Frame);
        Check(e.State(1) == held, "the middle layer left the passing state");
        Near(e.Values[node], before, "M6: the empty state applies what the layers below left in the last frame");
        Run(e, .2);
        Near(e.Values[node], before, "M6: and keeps applying it while the bottom layer moves");
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
        Check(e.State(1) == waiting && e.NextState(1) == -1, "M8: a later layer does not see the driver's change in the same frame");
        e.Advance(Frame);
        Check(e.State(1) == reacted, "but in the next");

        // M8: a copy reads its source when the drivers apply, here a trigger
        // the transition into its state used, and the driver's own layer also
        // sees its change from the next frame on.
        var d = new Builder();
        var pull = d.Parameter("Pull", "trigger");
        var pulled = d.Parameter("Pulled", "bool");
        var mark = d.Parameter("Mark", "bool");
        var dn = d.Target();
        var only = d.Layer("Only");
        var idle = Builder.State(only, "Idle", -1);
        var entered = Builder.State(only, "Entered", -1);
        var done = Builder.State(only, "Done", d.Clip(0, false, (dn, 1)));
        only.States[idle].Transitions.Add(To(entered).When(pull, "if"));
        only.States[entered].Transitions.Add(To(done).When(mark, "if"));
        var copy = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        copy.Operations.Add(new AvatarAnimatorData.Operation { Type = "copy", Parameter = pulled, Source = pull });
        copy.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = mark, Value = 1 });
        only.States[entered].Behaviours.Add(copy);
        var e2 = d.Build();
        e2.Advance(Frame);
        e2.Set(pull, 1);
        e2.Advance(Frame);
        Check(e2.State(0) == entered, "the trigger takes the transition");
        Check(e2.Get(pulled) == 0, "the copy reads the trigger after the transition used it");
        Check(e2.Get(mark) == 1, "the set applied after the frame");
        Check(e2.NextState(0) == -1, "its own layer did not start a transition on it");
        e2.Advance(Frame);
        Check(e2.State(0) == done, "its own layer moves on it in the next frame");

        // M8 on the first frame: the default state's driver sets the value its
        // own transition waits for, which moves from the second frame on.
        var f = new Builder();
        var ready = f.Parameter("Ready", "bool");
        var first = f.Layer("First");
        var start = Builder.State(first, "Start", -1);
        var after = Builder.State(first, "After", -1);
        first.States[start].Transitions.Add(To(after).When(ready, "if"));
        var setReady = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        setReady.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = ready, Value = 1 });
        first.States[start].Behaviours.Add(setReady);
        var e3 = f.Build();
        e3.Advance(Frame);
        Check(e3.Get(ready) == 1 && e3.State(0) == start && e3.NextState(0) == -1, "M8: the first frame enters the default state and stays");
        e3.Advance(Frame);
        Check(e3.State(0) == after, "the second frame takes the transition");

        // M8: drivers of two layers on the same parameter apply in layer order.
        var g = new Builder();
        var total = g.Parameter("Total", "int");
        foreach (var (name, type) in new[] { ("Set", "set"), ("Add", "add") })
        {
            var l = g.Layer(name);
            var s0 = Builder.State(l, "S", -1);
            var op = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
            op.Operations.Add(new AvatarAnimatorData.Operation { Type = type, Parameter = total, Value = 1 });
            l.States[s0].Behaviours.Add(op);
        }
        var e4 = g.Build();
        e4.Advance(Frame);
        Check(e4.Get(total) == 2, "M8: set 1 then add 1 in the same frame gives 2");
    }

    // M11: Unity owns a parameter a clip animates.
    static void ParameterCurves()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var amount = b.Parameter("Amount", "float", .5);
        var count = b.Parameter("Count", "int", 2);
        var node = b.Target();
        var animated = b.Data.Targets.Count;
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = amount, Rest = .25 });
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = count, Rest = 3 });
        var layer = b.Layer("Curve");
        var idle = Builder.State(layer, "Idle", b.Clip(1, true, (node, 1)));
        var moving = Builder.State(layer, "Moving", b.Clip(1, true, (animated, .75)));
        layer.States[idle].Transitions.Add(To(moving).When(go, "if"));
        var driver = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "set", Parameter = amount, Value = .9 });
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "add", Parameter = count, Value = 1 });
        layer.States[moving].Behaviours.Add(driver);
        var reader = b.Layer("Reader");
        var low = Builder.State(reader, "Low", b.Clip(0, false, (node, 0)));
        var high = Builder.State(reader, "High", b.Clip(0, false, (node, 0)));
        reader.States[low].Transitions.Add(To(high).When(amount, "greater", .5));
        var e = b.Build();
        Near(e.Get(amount), .25, "the controller's default, not the parameter's");
        Check(e.Get(count) == 3, "an int with a curve starts at the controller's default");
        e.Set(amount, .9);
        e.Set(count, 7);
        Near(e.Get(amount), .25, "the host does not change a parameter a clip animates");
        Check(e.Get(count) == 3, "nor an int with a curve");
        e.Advance(Frame);
        Near(e.Values[animated], .25, "no layer wrote it: the default");
        e.Set(go, 1);
        e.Advance(Frame);
        Near(e.Get(amount), .75, "the layers' value after the frame");
        Near(e.Values[animated], .75, "the target shows the same value");
        Check(e.Get(count) == 3, "a driver does not change an int with a curve");
        Check(e.State(1) == low, "transitions see the value of the frame before");
        e.Advance(Frame);
        Check(e.State(1) == high, "and the new value a frame later");
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
        Check(e.Values[node] == 0, "a layer at weight 0 leaves the rest");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Near(e.LayerWeight(1), .5, "layer control blends the weight over its duration");
        Near(e.Values[node], .5, "the layer's values follow the weight");
        e.Advance(.5);
        Near(e.LayerWeight(1), 1, "and stops at the goal");
        e.SetLayerWeight(1, 0);
        e.Advance(Frame);
        Check(e.Values[node] == 0, "weight 0 after writing: the rest, not the last value (M7)");
    }

    // M7 at weight 0 over a lower layer, and a layer without Write Defaults
    // that held its own values forgetting them at weight 0.
    static void LayerAtZero()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var shared = b.Target();
        var alone = b.Target();
        var bottom = b.Layer("Bottom");
        Builder.State(bottom, "A", b.Clip(1, true, (shared, 2)));
        var hold = b.Layer("Hold");
        var full = Builder.State(hold, "Full", b.Clip(1, true, (shared, 5), (alone, 1)), writeDefaults: false);
        var empty = Builder.State(hold, "Empty", -1, writeDefaults: false);
        hold.States[full].Transitions.Add(To(empty).When(go, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        Check(e.State(1) == empty && e.Values[shared] == 5 && e.Values[alone] == 1, "the empty state holds its own values");
        e.SetLayerWeight(1, 0);
        e.Advance(Frame);
        Check(e.Values[shared] == 2, "weight 0: the layer below");
        Check(e.Values[alone] == 0, "weight 0: the rest where no layer below wrote");
        e.SetLayerWeight(1, 1);
        e.Advance(Frame);
        Near(e.Values[shared], 2, "back at weight 1 the empty state passes the layer below through");
        Near(e.Values[alone], 0, "and keeps the rest it left");

        // Mid-transition at weight 0 the layer keeps its values, so an
        // interruption right after freezes the blend it had.
        Check(InterruptAfter(true) == InterruptAfter(false), "a weight-0 frame mid-transition keeps the still copy");
    }

    static double InterruptAfter(bool zero)
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var stop = b.Parameter("Stop", "bool");
        var node = b.Target();
        var other = b.Target();
        Builder.State(b.Layer("Base"), "Idle", b.Clip(1, true, (other, 0)));
        var layer = b.Layer("L");
        var a = Builder.State(layer, "A", b.Clip(1, true, (node, 1)), writeDefaults: false);
        var to = Builder.State(layer, "B", b.Clip(1, true, (node, 3)), writeDefaults: false);
        var away = Builder.State(layer, "C", b.Clip(1, true, (node, 5)), writeDefaults: false);
        var slow = To(to, duration: 1).When(go, "if");
        slow.InterruptionSource = "source";
        layer.States[a].Transitions.Add(slow);
        layer.States[a].Transitions.Add(To(away, duration: 1).When(stop, "if"));
        var e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        e.Set(go, 0);
        e.Advance(.5);
        if (zero) e.SetLayerWeight(1, 0);
        e.Advance(Frame);
        e.SetLayerWeight(1, 1);
        e.Set(stop, 1);
        e.Advance(Frame);
        e.Advance(.25);
        return e.Values[node];
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

    // A morph target plays as an active one does: its value is the Unity
    // weight, blended by layer weight and Write Defaults over its Rest.
    static void Morphs()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = 0, Property = "morph", Morph = 2, Rest = 30, Baked = 30, End = 100 });
        var morph = b.Data.Targets.Count - 1;
        var layer = b.Layer("Base");
        var idle = Builder.State(layer, "Idle", -1);
        layer.States[idle].WriteDefaults = true;
        var on = Builder.State(layer, "On", b.Clip(0, false, (morph, 80)));
        layer.States[idle].Transitions.Add(To(on, duration: 1).When(go, "if"));
        var top = b.Layer("Top", weight: .5);
        Builder.State(top, "Hold", b.Clip(0, false, (morph, 0)));
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "morph targets are supported");
        var e = b.Build();
        e.Advance(Frame);
        Near(e.Values[morph], 30 * .5, "a layer at weight .5 blends the weight over the rest");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.5);
        Near(e.Values[morph], (30 + (80 - 30) * .5) * .5, "a transition blends the weight from the rest");
        e.Advance(1);
        Near(e.Values[morph], 80 * .5, "and reaches the clip's weight");
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
        // M5: unordered, the running transition starts again while Go holds.
        e.Advance(.25);
        Check(e.NextState(0) == c && e.TransitionProgress(0) == 0, "M5: the running transition interrupts itself");
        e.Set(go, 0);
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

        // Ordered, the running transition does not start again.
        slow.InterruptionSource = "source";
        slow.OrderedInterruption = true;
        e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Near(e.TransitionProgress(0), .25, "M5: ordered, the running transition goes on");

        // M5: starting again does not run the destination's driver again.
        slow.OrderedInterruption = false;
        var count = b.Parameter("Count", "int");
        var addOne = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        addOne.Operations.Add(new AvatarAnimatorData.Operation { Type = "add", Parameter = count, Value = 1 });
        layer.States[c].Behaviours.Add(addOne);
        e = b.Build();
        e.Advance(Frame);
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(.25);
        Check(e.NextState(0) == c && e.Get(count) == 1, "M5: the driver runs once while the transition starts again");
        layer.States[c].Behaviours.Remove(addOne);

        // Any State interrupts unless the interruption source is none, before
        // the destination's transitions.
        var away = Builder.State(layer, "E", b.Clip(1, true, (node, 9)));
        var any = To(away, duration: 1).When(stop, "if");
        layer.Machines[0].AnyStateTransitions.Add(any);
        layer.States[c].Transitions.Add(To(d, duration: 1).When(stop, "if"));
        foreach (var source in new[] { "none", "destination" })
        {
            slow.InterruptionSource = source;
            e = b.Build();
            e.Advance(Frame);
            e.Set(go, 1);
            e.Advance(Frame);
            e.Set(stop, 1);
            e.Advance(Frame);
            Check(e.NextState(0) == (source == "none" ? c : away), "M5: Any State with interruption source " + source);
        }
        layer.Machines[0].AnyStateTransitions.Remove(any);
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
        var animated = b.Parameter("Animated", "float");
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = animated });
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "M11: parameters a clip animates");
        b.Data.Targets.RemoveAt(b.Data.Targets.Count - 1);
        var count = b.Parameter("Count", "int");
        var driver = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
        driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = count, ValueMin = 0, ValueMax = 3, PreventRepeats = true });
        b.Data.Controllers[0].Layers[0].States[0].Behaviours.Add(driver);
        Check(!AvatarAnimatorEvaluator.Supports(b.Data, out _), "a random that prevents repeats");
        driver.Operations[0].PreventRepeats = false;
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "a random that may repeat");
        var blend = b.Parameter("X", "float");
        var tree = b.Tree("1d", blend, (0, 0));
        b.Data.Motions[tree].BlendType = "2dSimpleDirectional";
        b.Data.Motions[tree].Parameters.Add(blend);
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "2D Simple Directional trees are played");
        b.Data.Motions[tree].BlendType = "2dFreeformCartesian";
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "2D Freeform Cartesian trees are played");
        b.Data.Motions[tree].BlendType = "2dFreeformDirectional";
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out _), "2D Freeform Directional trees are played");
        b.Data.Motions[tree].BlendType = "2dSomethingElse";
        Check(!AvatarAnimatorEvaluator.Supports(b.Data, out _), "a blend tree of another kind");

        var s = new Builder();
        var sn = s.Target();
        var sourceLayer = s.Layer("Source");
        Builder.State(sourceLayer, "A", s.Clip(0, false, (sn, 1)));
        Builder.State(sourceLayer, "B", s.Clip(0, false, (sn, 2)));
        var copy = s.Layer("Synced");
        copy.SyncedLayer = 0;
        Builder.State(copy, "A", -1);
        Check(!AvatarAnimatorEvaluator.Supports(s.Data, out _), "a synced layer with fewer states than its source");
        Throws<NotSupportedException>(() => new AvatarAnimatorEvaluator(s.Data, () => 0), "is refused before it can index past its states");
    }

    // Starts a controller of the playable; the layers that follow go in it.
    static void Playable(Builder b, string playable)
    {
        b.Controller = new AvatarAnimatorData.Controller { Playable = playable };
        b.Data.Controllers.Add(b.Controller);
    }

    static int Muscle(Builder b, int muscle)
    {
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Property = "muscle", Muscle = muscle, Rest = 0 });
        return b.Data.Targets.Count - 1;
    }

    // An FX toggle with a timed transition, then whatever gesture and action
    // controllers add writes the same node: the FX plays the same.
    static (Builder b, int hat, int node) FxToggle()
    {
        var b = new Builder();
        var hat = b.Parameter("Hat", "bool");
        var node = b.Target(rest: 1);
        var layer = b.Layer("Hat");
        var off = Builder.State(layer, "Off", b.Clip(0, false, (node, 0)));
        var on = Builder.State(layer, "On", b.Ramp(.5, true, node, 3));
        layer.States[off].Transitions.Add(To(on, .25).When(hat, "if"));
        layer.States[on].Transitions.Add(To(off, .25).When(hat, "ifNot"));
        return (b, hat, node);
    }

    static void SameAsFxAlone(Builder b, int hat, int node, string what)
    {
        var (alone, _, _) = FxToggle();
        var e = b.Build();
        var f = alone.Build();
        for (var i = 0; i < 120; i++)
        {
            var v = i % 40 < 20 ? 1 : 0;
            e.Set(hat, v);
            f.Set(hat, v);
            e.Advance(Frame);
            f.Advance(Frame);
            if (e.Values[node] != f.Values[node] || e.Written[node] != f.Written[node]) { Check(false, what + " at frame " + i); return; }
        }
        Check(true, what);
    }

    // #3666: what the exporter left out while reading a gesture or action
    // controller is its own; omissions there that change its transitions
    // skip only it, and data with the FX alone is written and played as before.
    static void ControllerOmitted()
    {
        // The FX alone writes no omitted of its controller, so a reader of
        // the extension before this sees the same data.
        var (alone, _, _) = FxToggle();
        alone.Data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "unreadParameter", Count = 2 });
        var written = AvatarAnimatorData.Write(alone.Data);
        var fxController = (Dictionary<string, object>)((List<object>)written["controllers"])[0];
        Check(fxController.Count == 2 && fxController.ContainsKey("playable") && fxController.ContainsKey("layers"), "the FX's controller has only its playable and layers");
        Check(((List<object>)written["omitted"]).Count == 1, "the FX's omissions stay in the root");
        Check(!AvatarAnimatorEvaluator.Supports(AvatarAnimatorData.Read(written), out _), "an FX omission that changes its transitions still refuses the data");

        foreach (var playable in new[] { "gesture", "action" })
            foreach (var kind in new[] { "unreadParameter", "operation", "parameter" })
            {
                var (b, hat, node) = FxToggle();
                Playable(b, playable);
                var hand = b.Target();
                Builder.State(b.Layer("Hands"), "Fist", b.Clip(0, false, (hand, 1)));
                b.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = kind, Count = 3 });
                b.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "somethingNewer", Count = 1 });
                var read = AvatarAnimatorData.Read(AvatarAnimatorData.Write(b.Data));
                var c = read.Controllers[1];
                Check(c.Omitted.Count == 2 && c.Omitted[0].Kind == kind && c.Omitted[0].Count == 3 && c.Omitted[1].Kind == "somethingNewer" && read.Omitted.Count == 0 && read.Controllers[0].Omitted.Count == 0,
                    playable + " " + kind + ": its omitted is read back as its own");
                Check(AvatarAnimatorEvaluator.Supports(read, out var reason) && reason == null, playable + " " + kind + ": does not refuse the FX");
                var e = new AvatarAnimatorEvaluator(read, () => 0);
                Check(e.SkipReason(1) != null && e.SkipReason(0) == null && e.LayerCount == 1, playable + " " + kind + ": skips only that controller");
                e.Advance(Frame);
                Check(!e.Written[hand], playable + " " + kind + ": does not write its target");
                SameAsFxAlone(b, hat, node, playable + " " + kind + ": the FX plays as without it");
                b.Controller.Omitted.RemoveAt(0);
                var clean = b.Build();
                clean.Advance(Frame);
                Check(clean.SkipReason(1) == null && clean.LayerCount == 2 && clean.Written[hand], playable + " " + kind + ": plays without the omission");
                // Data as the exporter writes it before this, with the
                // gesture's omission in the root, is still refused.
                b.Controller.Omitted.Clear();
                b.Data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = kind, Count = 3 });
                Check(!AvatarAnimatorEvaluator.Supports(b.Data, out _), playable + " " + kind + ": in the root it refuses the data");
            }

        // A gesture without omitted and an action with one: only the action is skipped.
        var (g, _, _) = FxToggle();
        Playable(g, "gesture");
        var target = g.Target();
        Builder.State(g.Layer("Hands"), "Fist", g.Clip(0, false, (target, 1)));
        Playable(g, "action");
        Builder.State(g.Layer("Act"), "Sit", g.Clip(0, false, (g.Target(), 1)));
        g.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "operation", Count = 1 });
        var played = new AvatarAnimatorEvaluator(AvatarAnimatorData.Read(AvatarAnimatorData.Write(g.Data)), () => 0);
        played.Advance(Frame);
        Check(played.SkipReason(1) == null && played.SkipReason(2) != null && played.Values[target] == 1, "only the controller with the omission is skipped");

        // Newer kinds after the known ones are read; other known kinds, the
        // FX, an empty list, a kind twice or out of order are not.
        g.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "somethingNewer", Count = 1 });
        Check(AvatarAnimatorData.Read(AvatarAnimatorData.Write(g.Data)).Controllers[2].Omitted[1].Kind == "somethingNewer", "a newer kind of a controller's omission is read");
        g.Controller.Omitted.Insert(0, new AvatarAnimatorData.Omission { Kind = "unreadParameter", Count = 1 });
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Write(g.Data), "known kinds out of order are not written");
        g.Controller.Omitted.Clear();
        g.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "operation", Count = 1 });
        g.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "operation", Count = 1 });
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Write(g.Data), "a kind twice is not written");
        g.Controller.Omitted.Clear();
        g.Controller.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "blendShape", Count = 1 });
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Write(g.Data), "a kind that does not change transitions stays in the root");
        var fx = FxToggle().Item1;
        fx.Data.Controllers[0].Omitted.Add(new AvatarAnimatorData.Omission { Kind = "unreadParameter", Count = 1 });
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Write(fx.Data), "the FX's omissions are not written in its controller");
        var raw = AvatarAnimatorData.Write(FxToggle().Item1.Data);
        var omission = new Dictionary<string, object> { ["kind"] = "unreadParameter", ["count"] = 1L };
        var rawFx = (Dictionary<string, object>)((List<object>)raw["controllers"])[0];
        rawFx["omitted"] = new List<object> { omission };
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(raw), "an omitted of the FX controller is not read");
        rawFx.Remove("omitted");
        var gesture = new Dictionary<string, object> { ["playable"] = "gesture", ["layers"] = new List<object>(), ["omitted"] = new List<object>() };
        ((List<object>)raw["controllers"]).Add(gesture);
        Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(raw), "an empty omitted of a controller is not read");
        gesture["omitted"] = new List<object> { omission };
        Check(AvatarAnimatorData.Read(raw).Controllers[1].Omitted[0].Kind == "unreadParameter", "a gesture's omitted is read from JSON");
        gesture.Remove("omitted");
        Check(AvatarAnimatorData.Read(raw).Controllers[1].Omitted.Count == 0, "a controller without omitted is read");
    }

    // #3666: the gesture and action controllers under the FX, each over the
    // ones below with its playable weight; one the evaluator cannot play is
    // skipped and the FX plays as before.
    static void Controllers()
    {
        // A gesture with an additive layer is skipped, not refused.
        var (b, hat, node) = FxToggle();
        Playable(b, "gesture");
        var additive = b.Layer("Hands");
        Builder.State(additive, "Fist", b.Clip(0, false, (node, 9)));
        var extra = b.Layer("Additive");
        extra.Blending = "additive";
        Builder.State(extra, "Add", b.Clip(0, false, (node, 9)));
        Check(AvatarAnimatorEvaluator.Supports(b.Data, out var reason) && reason == null, "a gesture with an additive layer does not refuse the FX");
        var e = b.Build();
        Check(e.SkipReason(1) != null && e.SkipReason(0) == null, "the gesture is skipped with its reason");
        Check(e.LayerIndex(1, 0) == -1 && e.LayerIndex(0, 0) == 0 && e.LayerCount == 1, "a skipped controller has no layers");
        SameAsFxAlone(b, hat, node, "the FX plays as without the skipped gesture");

        // Other things the evaluator cannot play skip an action the same way.
        foreach (var change in new Action<Builder>[]
        {
            x => { var l = x.Layer("Random"); var s = Builder.State(l, "S", -1); var d = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
                   d.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = x.Parameter("Pick", "int"), ValueMax = 3, PreventRepeats = true }); l.States[s].Behaviours.Add(d); },
            x => { Builder.State(x.Layer("Source"), "A", -1); var l = x.Layer("Synced"); l.SyncedLayer = 0; l.SyncedLayerAffectsTiming = true; Builder.State(l, "A", -1); },
        })
        {
            var (c, h, n) = FxToggle();
            Playable(c, "action");
            change(c);
            Check(AvatarAnimatorEvaluator.Supports(c.Data, out reason), "an action the evaluator cannot play does not refuse the FX: " + reason);
            Check(c.Build().SkipReason(1) != null, "and is skipped");
            SameAsFxAlone(c, h, n, "the FX plays as without the skipped action");
        }
        // The FX itself is still refused.
        var (fx, _, _) = FxToggle();
        fx.Data.Controllers[0].Layers[0].Blending = "additive";
        Check(!AvatarAnimatorEvaluator.Supports(fx.Data, out _), "an FX with an additive layer is refused");

        // A gesture writing other targets plays alongside, and the FX keeps playing the same.
        (b, hat, node) = FxToggle();
        Playable(b, "gesture");
        var hand = b.Target();
        var gestureLeft = b.Parameter("GestureLeft", "int");
        var hands = b.Layer("Left");
        var neutral = Builder.State(hands, "Neutral", b.Clip(0, false, (hand, 0)));
        var fist = Builder.State(hands, "Fist", b.Clip(0, false, (hand, 1)));
        hands.States[neutral].Transitions.Add(To(fist).When(gestureLeft, "equals", 1));
        hands.States[fist].Transitions.Add(To(neutral).When(gestureLeft, "notEqual", 1));
        SameAsFxAlone(b, hat, node, "the FX plays the same beside a gesture");
        e = b.Build();
        e.Advance(Frame);
        Check(e.Values[hand] == 0 && e.Written[hand], "the gesture writes its target");
        e.Set(gestureLeft, 1);
        e.Advance(Frame);
        Check(e.Values[hand] == 1, "and follows the parameters");
        Check(e.LayerIndex(1, 0) == 1 && e.State(e.LayerIndex(1, 0)) == fist && e.LayerCount == 2, "its layers follow the FX's");

        // Where both write a target, the FX above takes it; where the FX's
        // state does not write a target the FX animates, it gives the rest
        // over the gesture, as its layers do not see the gesture (assumed).
        b = new Builder();
        var go = b.Parameter("Go", "bool");
        var shared = b.Target();
        var fxLayer = b.Layer("FX");
        var idle = Builder.State(fxLayer, "Idle", b.Clip(0, false, (b.Target(), 1)));
        var set = Builder.State(fxLayer, "Set", b.Clip(0, false, (shared, .8)));
        fxLayer.States[idle].Transitions.Add(To(set).When(go, "if"));
        Playable(b, "gesture");
        Builder.State(b.Layer("Gesture"), "Hold", b.Clip(0, false, (shared, .3)));
        e = b.Build();
        e.Advance(Frame);
        Check(e.Values[shared] == 0, "a Write Defaults FX state gives the rest of a target the FX animates");
        e.Set(go, 1);
        e.Advance(Frame);
        Check(e.Values[shared] == .8, "the FX takes a target both write");
        // Without the FX animating it, the gesture's value shows.
        b.Data.Controllers[0].Layers[0].States[set].Motion = b.Clip(0, false, (b.Target(), 1));
        e = b.Build();
        e.Advance(Frame);
        Check(e.Values[shared] == .3 && e.Written[shared], "a target only the gesture writes keeps its value under the FX");

        // The action rests at playable weight 0 until a Playable Layer Control raises it.
        b = new Builder();
        var emote = b.Parameter("Emote", "bool");
        var arm = b.Target();
        var control = b.Layer("Control");
        var rest = Builder.State(control, "Rest", -1);
        var play = Builder.State(control, "Play", -1);
        control.States[rest].Transitions.Add(To(play).When(emote, "if"));
        control.States[play].Transitions.Add(To(rest).When(emote, "ifNot"));
        control.States[play].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "action", GoalWeight = 1, BlendDuration = .5 });
        control.States[rest].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "action", GoalWeight = 0, BlendDuration = 0 });
        Playable(b, "gesture");
        Builder.State(b.Layer("Gesture"), "Hold", b.Clip(0, false, (arm, 2)));
        Playable(b, "action");
        var dance = b.Layer("Action");
        Builder.State(dance, "Dance", b.Clip(0, false, (arm, 6)));
        foreach (var skips in new[] { true, false })
        {
            e = b.Build();
            e.SkipsSettledFrames = skips;
            Run(e, .5);
            Check(e.PlayableWeight(2) == 0 && e.Values[arm] == 2, "the action plays at weight 0 over the gesture");
            Check(e.State(e.LayerIndex(2, 0)) == 0 && e.NormalizedTime(e.LayerIndex(2, 0)) > 0, "while its layers run");
            e.Set(emote, 1);
            e.Advance(Frame);
            e.Advance(.25);
            Near(e.PlayableWeight(2), .5, "a Playable Layer Control blends the action's weight");
            Near(e.Values[arm], 4, "the action blends over the gesture by it");
            Run(e, 1);
            Check(e.PlayableWeight(2) == 1 && e.Values[arm] == 6, "and at weight 1 takes the target");
            e.Set(emote, 0);
            e.Advance(Frame);
            e.Advance(Frame);
            Check(e.PlayableWeight(2) == 0 && e.Values[arm] == 2, "a blend of 0 s drops it back at once");
        }
        // The FX's playable weight blends toward what the controllers below leave.
        b.Data.Controllers[0].Layers[0].States[play].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "fx", GoalWeight = .5, BlendDuration = 0 });
        Builder.State(b.Data.Controllers[0].Layers[0], "Unused", b.Clip(0, false, (arm, 10)));
        e = b.Build();
        e.Set(emote, 1);
        Run(e, 1);
        Near(e.FxWeight, .5, "the FX's weight");
        Near(e.Values[arm], 3, "an FX state not writing the target gives the rest over the action at half weight");

        // A Layer Control names a layer of its own playable.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        var finger = b.Target();
        var top = b.Layer("Control");
        var wait = Builder.State(top, "Wait", -1);
        var raise = Builder.State(top, "Raise", -1);
        top.States[wait].Transitions.Add(To(raise).When(go, "if"));
        top.States[raise].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "layerControl", Playable = "gesture", Layer = 1, GoalWeight = 1, BlendDuration = 0 });
        Playable(b, "gesture");
        Builder.State(b.Layer("Base"), "Open", b.Clip(0, false, (finger, 1)));
        Builder.State(b.Layer("Point", weight: 0), "Point", b.Clip(0, false, (finger, 5)));
        e = b.Build();
        e.Advance(Frame);
        Check(e.Values[finger] == 1 && e.LayerWeight(e.LayerIndex(1, 1)) == 0, "the gesture's second layer starts at its weight");
        e.Set(go, 1);
        e.Advance(Frame);
        e.Advance(Frame);
        Check(e.LayerWeight(e.LayerIndex(1, 1)) == 1 && e.Values[finger] == 5, "a Layer Control on the gesture moves the gesture's layer");
        e.SetLayerWeight(e.LayerIndex(1, 0), 0);
        e.Advance(Frame);
        Check(e.LayerWeight(e.LayerIndex(1, 0)) == 1, "a controller's base layer stays at 1");

        // A Layer Control on a controller's base layer leaves it at 1 too.
        top.States[raise].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "layerControl", Playable = "gesture", Layer = 0, GoalWeight = 0, BlendDuration = 0 });
        e = b.Build();
        e.Set(go, 1);
        Run(e, .1);
        Check(e.LayerWeight(e.LayerIndex(1, 0)) == 1 && e.Values[finger] == 5, "a Layer Control on a base layer leaves it at 1");

        // An FX layer without Write Defaults resting in a state that does not
        // write a target it animates keeps the last frame's value, at weight 1
        // as below it, not what the gesture under the FX writes this frame.
        b = new Builder();
        var x = b.Target();
        var y = b.Target();
        b.Layer("Base");
        var hold = b.Layer("Hold");
        Builder.State(hold, "Y", b.Clip(0, false, (y, 1)), writeDefaults: false);
        Builder.State(hold, "X", b.Clip(0, false, (x, 7)), writeDefaults: false);
        Playable(b, "gesture");
        var ramp = b.Layer("Ramp");
        Builder.State(ramp, "Ramp", b.Ramp(1, true, x, 4));
        foreach (var weight in new[] { 1, .999999 })
        {
            e = b.Build();
            e.SetLayerWeight(1, weight);
            var last = double.NaN;
            for (var i = 0; i < 30; i++)
            {
                e.Advance(Frame);
                if (i > 0) Check(Math.Abs(e.Values[x] - last) < 1e-4, "an FX layer holding a target keeps it at weight " + weight);
                last = e.Values[x];
            }
        }

        // A float parameter only a skipped controller animates is still set by the host.
        (b, hat, node) = FxToggle();
        var amount = b.Parameter("Amount", "float");
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "parameter", Parameter = amount });
        var amountTarget = b.Data.Targets.Count - 1;
        Playable(b, "gesture");
        var skippedLayer = b.Layer("Additive");
        skippedLayer.Blending = "additive";
        Builder.State(skippedLayer, "Drive", b.Clip(0, false, (amountTarget, .5)));
        e = b.Build();
        Check(e.SkipReason(1) != null, "the gesture animating the parameter is skipped");
        e.Set(amount, .75);
        e.Advance(Frame);
        Check(e.Get(amount) == .75, "the host still sets a parameter only a skipped controller animates");
        SameAsFxAlone(b, hat, node, "the FX plays as without that gesture");

        Masks();
        HostShare();
    }

    // A layer's mask moves the muscles of its body parts, and an FX whose
    // base layer has no mask moves no muscle (VRChat).
    static void Masks()
    {
        Check(AvatarAnimatorEvaluator.MuscleBodyPart(0) == 1 && AvatarAnimatorEvaluator.MuscleBodyPart(20) == 2 && AvatarAnimatorEvaluator.MuscleBodyPart(21) == 3
            && AvatarAnimatorEvaluator.MuscleBodyPart(37) == 5 && AvatarAnimatorEvaluator.MuscleBodyPart(54) == 6 && AvatarAnimatorEvaluator.MuscleBodyPart(55) == 7
            && AvatarAnimatorEvaluator.MuscleBodyPart(94) == 8, "the muscles' body parts");
        var b = new Builder();
        var node = b.Target();
        var thumb = Muscle(b, 55);
        var arm = Muscle(b, 39);
        var fx = b.Layer("FX");
        Builder.State(fx, "Pose", b.Clip(0, false, (node, 1), (thumb, .9), (arm, .9)));
        Playable(b, "gesture");
        var left = b.Layer("Left");
        left.Mask = new AvatarAnimatorData.Mask();
        left.Mask.Humanoid.Add(7);
        Builder.State(left, "Fist", b.Clip(0, false, (thumb, .5), (arm, .5)), writeDefaults: false);
        var e = b.Build();
        e.Advance(Frame);
        Check(e.Values[node] == 1, "the FX moves its node");
        Check(e.Values[thumb] == .5 && e.Written[thumb], "the gesture's mask lets the fingers move, and the FX without a mask on its base layer moves no muscle");
        Check(e.Values[arm] == 0 && !e.Written[arm], "the mask keeps the gesture off the arm");
        b.Data.Controllers[0].Layers[0].Mask = new AvatarAnimatorData.Mask();
        b.Data.Controllers[0].Layers[0].Mask.Humanoid.Add(5);
        e = b.Build();
        e.Advance(Frame);
        Check(e.Values[arm] == .9 && e.Values[thumb] == .5, "an FX with a mask on its base layer moves the muscles of its body parts");
        var none = new Builder();
        var m = Muscle(none, 3);
        Builder.State(none.Layer("FX"), "Pose", none.Clip(0, false, (m, .7)));
        e = none.Build();
        e.Advance(Frame);
        Check(e.Values[m] == 0 && !e.Written[m], "an FX alone without a mask moves no muscle");

        // A mask that names transforms moves only the positions, rotations
        // and scales of its nodes, and other targets on the nodes it leaves
        // out; one that names none (Nodes null) moves every node.
        var nodes = new Builder();
        var kept = nodes.Target();
        var moved = nodes.Target();
        var shown = nodes.Target();
        nodes.Data.Targets[kept].Property = nodes.Data.Targets[moved].Property = "translation";
        nodes.Data.Targets[kept].Channel = nodes.Data.Targets[moved].Channel = "x";
        Builder.State(nodes.Layer("Bottom"), "A", nodes.Clip(0, false, (kept, 1), (moved, 1), (shown, 1)));
        var top = nodes.Layer("Top");
        top.Mask = new AvatarAnimatorData.Mask();
        top.Mask.Nodes = new List<int> { nodes.Data.Targets[moved].Node };
        Builder.State(top, "B", nodes.Clip(0, false, (kept, 2), (moved, 2), (shown, 0)));
        e = nodes.Build();
        e.Advance(Frame);
        Check(e.Values[kept] == 1 && e.Values[moved] == 2, "a transform mask keeps the layer off the positions of the nodes it leaves out");
        Check(e.Values[shown] == 0, "a transform mask lets a GameObject it leaves out move");
        top.Mask.Nodes = null;
        e = nodes.Build();
        e.Advance(Frame);
        Check(e.Values[kept] == 2 && e.Values[moved] == 2, "a mask without transforms moves every node");
    }

    // HostShare: how much of what lies under every controller (the host's
    // pose, VRChat's Base) is left in each target. The action writes every
    // muscle of its mask, the rest where its clip has none, also at weight 0;
    // over a host pose h, Values + (h - rest) * share keeps the host's pose
    // there and moves from it as the action's weight rises.
    static void HostShare()
    {
        var b = new Builder();
        var go = b.Parameter("Go", "bool");
        var gestureWeight = b.Parameter("Half", "bool");
        var spine = Muscle(b, 0);
        var finger = Muscle(b, 55);
        var arm = Muscle(b, 39);
        var node = b.Target();
        var control = b.Layer("Control");
        var wait = Builder.State(control, "Wait", b.Clip(0, false, (node, 1)));
        var play = Builder.State(control, "Play", -1);
        var half = Builder.State(control, "Half", -1);
        control.States[wait].Transitions.Add(To(play).When(go, "if"));
        control.States[wait].Transitions.Add(To(half).When(gestureWeight, "if"));
        control.States[play].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "action", GoalWeight = 1, BlendDuration = .3 });
        control.States[half].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "gesture", GoalWeight = .5, BlendDuration = 0 });
        Playable(b, "gesture");
        var hands = b.Layer("Left");
        hands.Mask = new AvatarAnimatorData.Mask();
        hands.Mask.Humanoid.Add(7);
        Builder.State(hands, "Point", b.Clip(0, false, (finger, .5)));
        Playable(b, "action");
        var body = b.Layer("Action");
        body.Mask = new AvatarAnimatorData.Mask();
        body.Mask.Humanoid.Add(1);
        body.Mask.Humanoid.Add(7);
        Builder.State(body, "Idle", b.Clip(0, false, (spine, .6)));
        const double host = .3;
        foreach (var skips in new[] { true, false })
        {
            var e = b.Build();
            e.SkipsSettledFrames = skips;
            Run(e, .5);
            Check(e.PlayableWeight(2) == 0 && e.Written[spine] && e.Values[spine] == 0, "the action writes the rest of its muscles at weight 0");
            Check(e.HostShare[spine] == 1, "and leaves the host's pose whole there");
            Check(e.HostShare[finger] == 0 && e.Values[finger] == .5, "the gesture at weight 1 covers the host on the fingers it writes");
            Check(!e.Written[arm] && e.HostShare[arm] == 1, "a muscle no controller writes is the host's");
            Check(e.HostShare[node] == 0 && e.Values[node] == 1, "an FX target's share follows the FX");
            e.Set(go, 1);
            var last = e.Values[spine] + (host - 0) * e.HostShare[spine];
            var steps = 0;
            for (var i = 0; i < 30; i++)
            {
                e.Advance(Frame);
                var w = e.PlayableWeight(2);
                Near(e.HostShare[spine], 1 - w, "the host's share is what the action's weight leaves, at " + w);
                var shown = e.Values[spine] + (host - 0) * e.HostShare[spine];
                Near(shown, host + (.6 - host) * w, "the spine moves from the host's pose to the action's by its weight, at " + w);
                if (Math.Abs(shown - last) > (.6 - host) * Frame / .3 + 1e-9) { Check(false, "the spine does not jump in the blend, at " + w); return; }
                if (w > 0 && w < 1) steps++;
                last = shown;
                Check(e.HostShare[finger] == 0 && e.HostShare[arm] == 1, "the other muscles' shares stay through the blend");
            }
            Check(steps > 5 && e.PlayableWeight(2) == 1 && e.HostShare[spine] == 0 && e.Values[spine] == .6, "at weight 1 the action covers the host");
            Run(e, .5);
            Check(e.HostShare[spine] == 0, "and keeps it while settled");
        }

        // Controllers under a weight below 1 multiply what they leave.
        var g = b.Build();
        g.Set(gestureWeight, 1);
        Run(g, .1);
        Near(g.PlayableWeight(1), .5, "the gesture's weight");
        Near(g.HostShare[finger], .5, "a gesture at half weight leaves half the host on its fingers");
        Near(g.Values[finger] + (host - 0) * g.HostShare[finger], .5 * .5 + .5 * host, "and blends them over the host's pose");
        Check(g.HostShare[spine] == 1, "under an action at weight 0");

        // One controller alone: what it writes takes its weight's share.
        var fx = new Builder();
        var m = Muscle(fx, 3);
        var other = Muscle(fx, 55);
        var top = fx.Layer("FX");
        top.Mask = new AvatarAnimatorData.Mask();
        top.Mask.Humanoid.Add(1);
        Builder.State(top, "Pose", fx.Clip(0, false, (m, .7)));
        var alone = fx.Build();
        alone.Advance(Frame);
        Check(alone.HostShare[m] == 0 && alone.Values[m] == .7, "an FX alone at weight 1 covers the muscles it writes");
        Check(alone.HostShare[other] == 1 && !alone.Written[other], "and leaves the host's where its mask keeps it off");
    }

    // M12. The recordings of the freeformCartesian case hold Unity's weights; these are the same rule by hand.
    static void FreeformCartesianTrees()
    {
        var b = new Builder();
        var x = b.Parameter("X", "float");
        var y = b.Parameter("Y", "float");
        var node = b.Target();
        var tree = b.Tree("1d", x, (b.Clip(1, true, (node, 0)), 0), (b.Clip(1, true, (node, 10)), 0), (b.Clip(1, true, (node, 20)), 0), (-1, 0));
        var motion = b.Data.Motions[tree];
        motion.BlendType = "2dFreeformCartesian";
        motion.Parameters.Add(y);
        // Children at (0, 0), (1, 0) and (0, 1), and one without a motion at (.25, .25).
        motion.Children[1].X = 1;
        motion.Children[2].Y = 1;
        motion.Children[3].X = motion.Children[3].Y = .25;
        Builder.State(b.Layer("2D"), "Tree", tree);
        var e = b.Build();
        e.Set(x, 1);
        e.Advance(Frame);
        Near(e.Values[node], 10, "Freeform Cartesian: all of the child at the point");
        e.Set(x, .5);
        e.Advance(Frame);
        Near(e.Values[node], 5, "Freeform Cartesian: between two children, a child off the line has no weight");
        // .75, .25 and .25 before the division by their sum; the child without a motion is not in the tree.
        e.Set(x, .25);
        e.Set(y, .25);
        e.Advance(Frame);
        Near(e.Values[node], 6, "Freeform Cartesian: the smallest weight against each other child, normalized");
        e.Set(x, 3);
        e.Set(y, -2);
        e.Advance(Frame);
        Near(e.Values[node], 10, "Freeform Cartesian: outside the children");

        // Two children at one position share the weight.
        var s = new Builder();
        var sx = s.Parameter("X", "float");
        var sy = s.Parameter("Y", "float");
        var sn = s.Target();
        var same = s.Tree("1d", sx, (s.Clip(1, true, (sn, 2)), 0), (s.Clip(1, true, (sn, 4)), 0), (s.Clip(1, true, (sn, 9)), 0));
        s.Data.Motions[same].BlendType = "2dFreeformCartesian";
        s.Data.Motions[same].Parameters.Add(sy);
        s.Data.Motions[same].Children[2].X = 1;
        Builder.State(s.Layer("2D"), "Tree", same);
        e = s.Build();
        e.Advance(Frame);
        Near(e.Values[sn], 3, "Freeform Cartesian: two children at one position");

        // A position past the range of a Unity float would overflow the weights into NaN.
        s.Data.Motions[same].Children[2].X = 1e39;
        Throws<InvalidOperationException>(() => s.Build(), "a child position outside the float range is not read");
        s.Data.Motions[same].Children[2].X = float.MaxValue;
        e = s.Build();
        e.Set(sx, .5);
        e.Advance(Frame);
        Check(!double.IsNaN(e.Values[sn]), "Freeform Cartesian: the farthest position a float holds gives a number");
    }

    // M13 and M14. The recordings of the simpleDirectional and freeformDirectional cases hold Unity's weights.
    static void DirectionalTrees()
    {
        AvatarAnimatorEvaluator Directional(string type, out int x, out int y, out int node, params (double X, double Y, double Value)[] children)
        {
            var b = new Builder();
            x = b.Parameter("X", "float");
            y = b.Parameter("Y", "float");
            var target = b.Target();
            var tree = b.Tree("1d", x);
            var motion = b.Data.Motions[tree];
            motion.BlendType = type;
            motion.Parameters.Add(y);
            foreach (var child in children)
                motion.Children.Add(new AvatarAnimatorData.Child { Motion = b.Clip(1, true, (target, child.Value)), X = child.X, Y = child.Y, TimeScale = 1 });
            Builder.State(b.Layer("2D"), "Tree", tree);
            node = target;
            return b.Build();
        }

        var e = Directional("2dSimpleDirectional", out var sx, out var sy, out var sn, (0, 0, 0), (1, 0, 10), (0, 1, 20), (-1, 0, 30), (0, -1, 40));
        e.Set(sx, .25);
        e.Advance(Frame);
        Near(e.Values[sn], 2.5, "Simple Directional: between the centre and one direction");
        e.Set(sx, .25);
        e.Set(sy, .5);
        e.Advance(Frame);
        Near(e.Values[sn], 12.5, "Simple Directional: inside the triangle of two directions and the centre");
        e.Set(sx, 1.25);
        e.Set(sy, .75);
        e.Advance(Frame);
        Near(e.Values[sn], 10 * 1.25 / 2 + 20 * .75 / 2, "Simple Directional: past the line between two directions");
        e = Directional("2dSimpleDirectional", out sx, out sy, out sn, (1, 0, 10), (0, 1, 20), (-1, 0, 30), (0, -1, 40));
        e.Set(sx, .5);
        e.Advance(Frame);
        Near(e.Values[sn], 5 + .5 * 25, "Simple Directional: without a centre, the directions share its weight");
        e = Directional("2dSimpleDirectional", out sx, out sy, out sn, (0, 0, 5), (1, 0, 10), (0, 1, 20));
        e.Set(sx, -1);
        e.Advance(Frame);
        Near(e.Values[sn], 5, "Simple Directional: between neighbours more than 180 degrees apart, the centre");
        e = Directional("2dSimpleDirectional", out sx, out sy, out sn, (0, 0, 5), (1, 0, 10), (0, 0, 20));
        e.Set(sx, .5);
        e.Advance(Frame);
        Near(e.Values[sn], 0, "Simple Directional: with two centres no child has weight away from (0, 0)");
        e.Set(sx, 0);
        e.Advance(Frame);
        Near(e.Values[sn], 5, "Simple Directional: with two centres the first takes all at (0, 0)");

        e = Directional("2dFreeformDirectional", out var fx, out var fy, out var fn, (1, 0, 10), (0, 1, 20), (-1, 0, 30), (0, -1, 40));
        e.Set(fx, .5);
        e.Set(fy, .5);
        e.Advance(Frame);
        Near(e.Values[fn], 15, "Freeform Directional: halfway in angle between two directions of one length");
        e = Directional("2dFreeformDirectional", out fx, out fy, out fn, (0, 0, 0), (1, 0, 10), (2, 0, 20), (0, 1, 30), (-1, 0, 40), (0, -1, 50));
        e.Set(fx, 1.5);
        e.Advance(Frame);
        Near(e.Values[fn], 15, "Freeform Directional: halfway in length between two children in one direction");
        e.Set(fx, .5);
        e.Advance(Frame);
        Near(e.Values[fn], 5, "Freeform Directional: halfway between the centre and a child");
        e = Directional("2dFreeformDirectional", out fx, out fy, out fn, (0, 0, 10), (.5, .5, 20));
        e.Set(fx, -2.5);
        e.Set(fy, -2.5);
        e.Advance(Frame);
        Near(e.Values[fn], 15, "Freeform Directional: with no weight left the children share equally");
        e = Directional("2dFreeformDirectional", out fx, out fy, out fn, (1, 0, 10), (0, 2, 20), (-2, 0, 30), (0, -1, 40));
        e.Advance(Frame);
        Near(e.Values[fn], 25, "Freeform Directional: at (0, 0) without a child there the children share equally");
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
        e.Set(f, 1e300);
        Check(e.Get(f) == float.MaxValue, "a value beyond the float range stops at the largest float");
        e.Set(f, .1);
        e.Set(i, -2.7);
        Check(e.Get(i) == -2, "an int parameter is truncated");
        e.Set(t, 5);
        Check(e.Get(t) == 1, "a trigger is set");
        e.Advance(Frame);
        Check(e.LayerCount == 0, "no controller: nothing plays");
    }

    // Plays the data twice side by side, once skipping settled frames and once
    // evaluating every frame, checks that every frame matches and returns, per
    // frame, whether the skipping one skipped it. input(e, frame) sets
    // parameters before each frame.
    static bool[] Lockstep(Builder b, int frames, Action<AvatarAnimatorEvaluator, int> input = null)
    {
        var e = b.Build();
        var full = b.Build();
        full.SkipsSettledFrames = false;
        var skipped = new bool[frames];
        for (var frame = 0; frame < frames; frame++)
        {
            input?.Invoke(e, frame);
            input?.Invoke(full, frame);
            e.Advance(Frame);
            full.Advance(Frame);
            skipped[frame] = !e.Changed;
            Check(full.Changed, "an evaluator that does not skip reports every frame changed");
            var same = true;
            for (var t = 0; t < e.Values.Count; t++) same &= e.Values[t].Equals(full.Values[t]) && e.Written[t] == full.Written[t];
            for (var l = 0; l < e.LayerCount; l++)
                same &= e.State(l) == full.State(l) && e.NextState(l) == full.NextState(l)
                    && e.NormalizedTime(l).Equals(full.NormalizedTime(l)) && e.NextNormalizedTime(l).Equals(full.NextNormalizedTime(l))
                    && e.TransitionProgress(l).Equals(full.TransitionProgress(l)) && e.LayerWeight(l).Equals(full.LayerWeight(l));
            for (var p = 0; p < b.Data.Parameters.Count; p++) same &= e.Get(p).Equals(full.Get(p));
            Check(same, "frame " + frame + " matches the evaluator that does not skip");
        }
        return skipped;
    }

    static int Count(bool[] skipped, int from, int to)
    {
        var n = 0;
        for (var i = from; i < to; i++) if (skipped[i]) n++;
        return n;
    }

    // #3664: while nothing can change the next frame, Advance does not evaluate
    // it, keeps Values and reports Changed false.
    static void Settled()
    {
        // A toggle: a frame to enter, a frame to see it hold, then skipped until
        // a parameter changes; a 0.25 s transition plays every frame.
        var b = new Builder();
        var hat = b.Parameter("Hat", "bool");
        var node = b.Target(rest: 1);
        var layer = b.Layer("Hat");
        var off = Builder.State(layer, "Off", b.Clip(0, false, (node, 0)));
        var on = Builder.State(layer, "On", b.Clip(1, true, (node, 1)));
        layer.States[off].Transitions.Add(To(on, duration: .25).When(hat, "if"));
        layer.States[on].Transitions.Add(To(off).When(hat, "ifNot"));
        var skipped = Lockstep(b, 120, (e, frame) =>
        {
            if (frame == 10) e.Set(hat, 0);
            if (frame == 20) e.Set(hat, 1);
            if (frame == 80) e.Set(hat, 0);
        });
        Check(!skipped[0] && !skipped[1] && Count(skipped, 2, 20) == 18, "a toggle at rest is skipped from its third frame, and setting a parameter to its value keeps it so");
        Check(Count(skipped, 20, 36) == 0, "a transition plays every frame");
        Check(Count(skipped, 40, 80) == 40, "a looping clip that holds its values is skipped");
        Check(!skipped[80] && Count(skipped, 82, 120) == 38, "and a parameter that changes plays the next frame");

        // A clip whose values change plays while it loops; once a clip that
        // does not loop has ended it holds its last values.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Loop");
        Builder.State(layer, "Ramp", b.Ramp(1, true, node, 1));
        Check(Count(Lockstep(b, 180), 0, 180) == 0, "a looping clip whose values change plays every frame");
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Once");
        Builder.State(layer, "Ramp", b.Ramp(1, false, node, 1));
        skipped = Lockstep(b, 180);
        Check(Count(skipped, 0, 60) == 0 && Count(skipped, 70, 180) == 110, "a clip that does not loop is skipped once it has ended");

        // A transition on exit time waits for the state's time: the frames up to
        // it are skipped, the frame that reaches it plays, and so do those
        // of the transition; those after it, in a state without one, are skipped.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Wait");
        var a = Builder.State(layer, "A", b.Clip(1, true, (node, 0)));
        var c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: 2));
        skipped = Lockstep(b, 240);
        Check(Count(skipped, 2, 120) == 118 && !skipped[120] && !skipped[121] && Count(skipped, 122, 240) == 118, "a pending exit time is skipped up to the frame that reaches it");
        // An exit time below 1 fires on each loop, but not while its conditions fail.
        b = new Builder();
        var go = b.Parameter("Go", "bool");
        node = b.Target();
        layer = b.Layer("Late");
        a = Builder.State(layer, "A", b.Clip(1, true, (node, 0)));
        c = Builder.State(layer, "B", b.Clip(0, false, (node, 1)));
        layer.States[a].Transitions.Add(To(c, exitTime: true, at: .5).When(go, "if"));
        skipped = Lockstep(b, 240, (e, frame) => { if (frame == 120) e.Set(go, 1); });
        Check(Count(skipped, 5, 120) == 115 && !skipped[120] && Count(skipped, 121, 150) == 29 && !skipped[150] && !skipped[151] && Count(skipped, 160, 240) == 80,
            "an exit time below 1 is watched while its conditions hold, and the frame that reaches it on the next loop plays");

        // A timer: states whose motions give the same values (or none), joined
        // by transitions of a fixed length on exit time. Mid-transition only
        // the progress moves, so the frames are skipped until the transition
        // ends (#3664). Both sides empty, too, with and without Write Defaults.
        foreach (var form in new[] { "same", "empty", "emptyNoWriteDefaults" })
        {
            b = new Builder();
            node = b.Target();
            layer = b.Layer("Timer");
            var hold = form == "same" ? b.Clip(1, true, (node, 1)) : -1;
            var wd = form != "emptyNoWriteDefaults";
            a = Builder.State(layer, "Wait", hold, wd);
            c = Builder.State(layer, "Again", hold, wd);
            layer.States[a].Transitions.Add(To(c, duration: 1, exitTime: true, at: .5));
            layer.States[c].Transitions.Add(To(a, duration: 2, exitTime: true, at: 0));
            skipped = Lockstep(b, 600);
            Check(Count(skipped, 0, 600) > 520, "a timer of " + form + " states is skipped mid-transition");
            Check(!skipped[29] && !skipped[30] && Count(skipped, 31, 89) == 58, "a timer of " + form + " states plays the frame that reaches the exit time");
        }

        // The same timer whose two states differ plays every frame of each transition.
        b = new Builder();
        node = b.Target();
        layer = b.Layer("Timer");
        a = Builder.State(layer, "Off", b.Clip(1, true, (node, 0)));
        c = Builder.State(layer, "On", b.Clip(1, true, (node, 1)));
        layer.States[a].Transitions.Add(To(c, duration: 1, exitTime: true, at: .5));
        layer.States[c].Transitions.Add(To(a, duration: 1, exitTime: true, at: .5));
        skipped = Lockstep(b, 600);
        Check(Count(skipped, 31, 90) == 0, "a transition between states of different values plays every frame");

        // M5: mid-transition, the source's exit time interrupts the timer, and
        // a parameter set mid-transition interrupts it from Any State: both
        // frames play as in the evaluator that skips nothing.
        foreach (var from in new[] { "source", "destination", "sourceThenDestination", "destinationThenSource", "none" })
        {
            b = new Builder();
            go = b.Parameter("Go", "bool");
            node = b.Target();
            layer = b.Layer("Timer");
            var hold = b.Clip(1, true, (node, 1));
            a = Builder.State(layer, "Wait", hold);
            c = Builder.State(layer, "Again", hold);
            var d = Builder.State(layer, "Away", b.Clip(1, true, (node, 0)));
            var timer = To(c, duration: 4, exitTime: true, at: .25);
            timer.InterruptionSource = from;
            layer.States[a].Transitions.Add(timer);
            layer.States[a].Transitions.Add(To(d, duration: .5, exitTime: true, at: .75));
            layer.States[c].Transitions.Add(To(d, duration: .5, exitTime: true, at: .5));
            layer.Machines[0].AnyStateTransitions.Add(To(d, duration: .5).When(go, "if"));
            skipped = Lockstep(b, 480, (e, frame) => { if (frame == 300) e.Set(go, 1); });
            Check(Count(skipped, 17, 44) > 20, "a timer that may be interrupted from " + from + " is skipped until it is");
        }

        // A material slot is stepped: it takes the value of the state with
        // more weight across a transition, and settles on a whole index.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = 0, Property = "materialSlot", Slot = 1, Rest = 0 });
        var slot = b.Data.Targets.Count - 1;
        layer = b.Layer("Slot");
        a = Builder.State(layer, "A", b.Clip(0, false, (slot, 2)));
        c = Builder.State(layer, "B", b.Clip(0, false, (slot, -1)));
        layer.States[a].Transitions.Add(To(c, duration: .5).When(go, "if"));
        layer.States[c].Transitions.Add(To(a, duration: .5).When(go, "ifNot"));
        var values = new List<double>();
        skipped = Lockstep(b, 200, (e, frame) =>
        {
            if (frame == 20) e.Set(go, 1);
            if (frame == 100) e.Set(go, 0);
            if (frame > 0) values.Add(e.Values[slot]);
        });
        Check(values.TrueForAll(v => v == 2 || v == -1) && values.Contains(-1), "a material slot never takes a value between two indices");
        Check(Count(skipped, 20, 50) == 0 && Count(skipped, 55, 100) == 45 && Count(skipped, 135, 200) == 65, "and settles once the transition ends");
        // A stepped key change inside a clip that does not loop.
        b = new Builder();
        b.Data.Targets.Add(new AvatarAnimatorData.Target { Node = 0, Property = "materialSlot", Slot = 0, Rest = 0 });
        var clip = new AvatarAnimatorData.Clip { Name = "swap", Duration = 1 };
        var curve = new ExpressionAnimationData.Curve();
        curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 1, InTangent = 0, OutTangent = null });
        curve.Keys.Add(new ExpressionAnimationData.Key { Time = .5, Value = 3, InTangent = null, OutTangent = 0 });
        var track = new AvatarAnimatorData.Track { Target = 0 };
        track.Curves.Add(curve);
        clip.Tracks.Add(track);
        b.Data.Clips.Add(clip);
        b.Data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = 0 });
        Builder.State(b.Layer("Swap"), "Swap", 0);
        skipped = Lockstep(b, 120);
        Check(Count(skipped, 0, 60) == 0 && Count(skipped, 65, 120) == 55, "a clip that swaps a material plays until it has ended");

        // An Any State transition that may go to its own state enters it again
        // each frame; that repeats the frame, unless entering draws a random number.
        foreach (var draws in new[] { false, true })
        {
            b = new Builder();
            go = b.Parameter("Go", "bool", 1);
            var rolled = b.Parameter("Rolled", "float");
            node = b.Target();
            layer = b.Layer("Again");
            a = Builder.State(layer, "A", b.Clip(0, false, (node, 1)));
            var again = To(a).When(go, "if");
            again.CanTransitionToSelf = true;
            layer.Machines[0].AnyStateTransitions.Add(again);
            if (draws)
            {
                var roll = new AvatarAnimatorData.Behaviour { Type = "parameterDriver" };
                roll.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = rolled, ValueMin = 0, ValueMax = 1 });
                layer.States[a].Behaviours.Add(roll);
            }
            var r = new Random(1);
            var e = new AvatarAnimatorEvaluator(AvatarAnimatorData.Read(AvatarAnimatorData.Write(b.Data)), () => r.NextDouble());
            var r2 = new Random(1);
            var full = new AvatarAnimatorEvaluator(AvatarAnimatorData.Read(AvatarAnimatorData.Write(b.Data)), () => r2.NextDouble()) { SkipsSettledFrames = false };
            var n = 0;
            for (var frame = 0; frame < 60; frame++)
            {
                e.Advance(Frame);
                full.Advance(Frame);
                if (!e.Changed) n++;
                Check(e.Get(rolled).Equals(full.Get(rolled)) && e.Values[node] == full.Values[node] && e.NormalizedTime(0).Equals(full.NormalizedTime(0)), "a layer entering its state again matches every frame");
            }
            Check(draws ? n == 0 : n >= 55, draws ? "a state that draws a random number on entry plays every frame" : "a state entered again each frame is skipped");
        }

        // A state entered again each frame whose 0 s Layer Control sets the
        // weight of a layer that has already stepped: the frame after plays it.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        node = b.Target();
        Builder.State(b.Layer("Base"), "A", -1);
        Builder.State(b.Layer("Lit"), "A", b.Clip(0, false, (node, 1)));
        layer = b.Layer("Control");
        a = Builder.State(layer, "S", -1);
        layer.States[a].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "layerControl", Playable = "fx", Layer = 1, GoalWeight = 1, BlendDuration = 0 });
        var enter = To(a).When(go, "if");
        enter.CanTransitionToSelf = true;
        layer.Machines[0].AnyStateTransitions.Add(enter);
        skipped = Lockstep(b, 30, (e, frame) => { if (frame == 5) e.SetLayerWeight(1, 0); if (frame == 10) e.Set(go, 1); });
        Check(!skipped[11] && Count(skipped, 13, 30) == 17, "a Layer Control that changes the weight of an earlier layer plays the next frame");

        // A layer at weight 0 forgets what it wrote (M7) after the frame that
        // still wrote it, so the frame after no longer counts its targets
        // written. Its empty state without Write Defaults holds what its clip
        // wrote, the rest value, so the values do not change.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        node = b.Target();
        Builder.State(b.Layer("Base"), "A", -1);
        layer = b.Layer("Over");
        a = Builder.State(layer, "C", b.Clip(0, false, (node, 0)), writeDefaults: false);
        var held = Builder.State(layer, "E", -1, writeDefaults: false);
        layer.States[a].Transitions.Add(To(held).When(go, "if"));
        skipped = Lockstep(b, 60, (e, frame) => { if (frame == 10) e.Set(go, 1); if (frame == 30) e.SetLayerWeight(1, 0); });
        Check(!skipped[30] && !skipped[31] && Count(skipped, 33, 60) == 27, "a layer at weight 0 plays the frame in which it forgets what it wrote");

        // A layer weight set from outside plays the frame.
        b = new Builder();
        node = b.Target();
        var other = b.Target();
        Builder.State(b.Layer("Base"), "A", b.Clip(0, false, (node, 1)));
        Builder.State(b.Layer("Over", .5), "A", b.Clip(0, false, (other, 1)));
        skipped = Lockstep(b, 60, (e, frame) => { if (frame == 30) e.SetLayerWeight(1, .25); if (frame == 40) e.SetLayerWeight(1, .25); });
        Check(Count(skipped, 2, 30) == 28 && !skipped[30] && skipped[32] && !skipped[40] && skipped[42], "a layer weight set from outside plays the frame, even one it already has");

        // A parameter a state's time follows: skipped while the parameter holds.
        b = new Builder();
        var time = b.Parameter("Time", "float", .25);
        node = b.Target();
        layer = b.Layer("Timed");
        a = Builder.State(layer, "A", b.Ramp(1, true, node, 1));
        layer.States[a].TimeParameter = time;
        skipped = Lockstep(b, 90, (e, frame) => { if (frame == 30) e.Set(time, .75); });
        Check(Count(skipped, 2, 30) == 28 && !skipped[30] && Count(skipped, 32, 90) == 58, "a state whose time is a parameter is skipped while it holds");

        // A synced layer follows its source's clock and states.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        node = b.Target();
        other = b.Target();
        var source = b.Layer("Source");
        a = Builder.State(source, "A", b.Clip(1, true, (node, 0)));
        c = Builder.State(source, "B", b.Ramp(1, false, node, 1));
        source.States[a].Transitions.Add(To(c, duration: .25).When(go, "if"));
        source.States[c].Transitions.Add(To(a, duration: .25).When(go, "ifNot"));
        var synced = b.Layer("Synced");
        synced.SyncedLayer = 0;
        Builder.State(synced, "A", b.Clip(0, false, (other, 10)));
        Builder.State(synced, "B", b.Ramp(2, true, other, 20));
        skipped = Lockstep(b, 300, (e, frame) => { if (frame == 30) e.Set(go, 1); if (frame == 200) e.Set(go, 0); });
        Check(Count(skipped, 2, 30) == 28 && Count(skipped, 30, 200) == 0 && Count(skipped, 220, 300) == 80, "a synced layer playing a clip whose values change keeps its source playing");

        // A synced layer before its source takes the source's time a frame
        // late: when the source's time follows a parameter, the frame after
        // the change moves the synced layer though the source shows nothing.
        b = new Builder();
        time = b.Parameter("Time", "float", .25);
        node = b.Target();
        var early = b.Layer("Synced");
        source = b.Layer("Source");
        early.SyncedLayer = 1;
        Builder.State(early, "A", b.Ramp(1, false, node, 1));
        a = Builder.State(source, "A", -1);
        source.States[a].TimeParameter = time;
        skipped = Lockstep(b, 90, (e, frame) => { if (frame == 30) e.Set(time, .75); });
        Check(!skipped[30] && !skipped[31] && Count(skipped, 33, 90) == 57, "a synced layer before a source timed by a parameter moves in the frame after the change");
        // The same mid-transition into a state timed by a parameter: the next
        // state's time is compared with the source's next time.
        b = new Builder();
        go = b.Parameter("Go", "bool");
        time = b.Parameter("Time", "float");
        node = b.Target();
        early = b.Layer("Synced");
        source = b.Layer("Source");
        early.SyncedLayer = 1;
        Builder.State(early, "A", b.Clip(1, false, (node, 0)));
        Builder.State(early, "B", b.Ramp(1, false, node, 1));
        a = Builder.State(source, "A", -1);
        var timed = Builder.State(source, "B", -1);
        source.States[timed].TimeParameter = time;
        source.States[a].Transitions.Add(To(timed, 2).When(go, "if"));
        skipped = Lockstep(b, 80, (e, frame) => { if (frame == 10) e.Set(go, 1); if (frame == 40) e.Set(time, .5); });
        Check(!skipped[41], "a synced layer before its source moves its next state in the frame after the change");

        // Random synced layers on either side of their source, with states
        // timed by a parameter: seeds the settled check failed before.
        foreach (var seed in SyncedSeeds)
        {
            AvatarAnimatorSettledCheck.Play(AvatarAnimatorData.Read(AvatarAnimatorData.Write(SyncedRandom(seed).Data)), seed, "synced random " + seed, 1500);
            Check(true, "synced random " + seed + " skips only frames that repeat");
        }
    }

    static readonly int[] SyncedSeeds = { 4, 10, 18, 53, 67 };

    static Builder SyncedRandom(int seed)
    {
        var r = new Random(seed);
        var b = new Builder();
        b.Parameter("B", "bool");
        b.Parameter("I", "int");
        int[] times = { b.Parameter("F", "float"), b.Parameter("G", "float") };
        var targets = new List<int>();
        for (var i = 0; i < 5; i++) targets.Add(b.Target(r.Next(2)));
        var layerCount = 2 + r.Next(3);
        var stateCount = 1 + r.Next(3);
        var layers = new List<AvatarAnimatorData.Layer>();
        for (var l = 0; l < layerCount; l++) layers.Add(b.Layer("L" + l, r.Next(3) / 2.0));
        var source = r.Next(layerCount);
        var synced = new List<int>();
        for (var l = 0; l < layerCount; l++) if (l != source && r.NextDouble() < .7) synced.Add(l);
        if (synced.Count == 0) synced.Add((source + 1) % layerCount);
        int Motion()
        {
            if (r.NextDouble() < .15) return -1;
            var duration = r.NextDouble() < .3 ? 0 : 1;
            var loop = r.NextDouble() < .4;
            var target = targets[r.Next(targets.Count)];
            var value = Math.Round(r.NextDouble() * 4, 1);
            return duration > 0 && r.NextDouble() < .6 ? b.Ramp(duration, loop, target, value) : b.Clip(duration, loop, (target, value));
        }
        for (var l = 0; l < layerCount; l++)
        {
            var layer = layers[l];
            var count = synced.Contains(l) || l == source ? stateCount : 1 + r.Next(3);
            for (var s = 0; s < count; s++) Builder.State(layer, "S" + s, Motion(), r.NextDouble() < .85);
            if (synced.Contains(l))
            {
                layer.SyncedLayer = source;
                layer.Machines[0].DefaultState = -1;
                continue;
            }
            foreach (var state in layer.States)
            {
                if (r.NextDouble() < .5) state.TimeParameter = times[r.Next(2)];
                else if (r.NextDouble() < .3) state.Speed = new[] { 0, .5, -1, 2 }[r.Next(4)];
                for (var i = r.Next(3); i > 0; i--)
                {
                    var exit = r.NextDouble() < .3;
                    var transition = To(r.Next(count), new[] { 0, .1, .4 }[r.Next(3)], exit, r.NextDouble() < .5 ? .5 : 1);
                    if (!exit || r.NextDouble() < .5)
                    {
                        var p = r.Next(b.Data.Parameters.Count);
                        var type = b.Data.Parameters[p].Type;
                        if (type == "bool") transition.When(p, r.Next(2) == 0 ? "if" : "ifNot");
                        else if (type == "int") transition.When(p, r.Next(2) == 0 ? "equals" : "notEqual", r.Next(3));
                        else transition.When(p, r.Next(2) == 0 ? "greater" : "less", .5);
                    }
                    if (r.NextDouble() < .3) transition.InterruptionSource = new[] { "source", "destination", "sourceThenDestination" }[r.Next(3)];
                    state.Transitions.Add(transition);
                }
            }
        }
        return b;
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
