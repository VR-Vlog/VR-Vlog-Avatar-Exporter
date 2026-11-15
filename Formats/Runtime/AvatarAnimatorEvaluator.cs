using System;
using System.Collections.Generic;

// Plays VRVLOG_avatar_animator (AvatarAnimatorData.cs) the way Unity's Animator
// plays the FX, Gesture and Action controllers in VRChat's playable graph
// (M15). No Unity/Editor dependency: the app drives it from a
// MonoBehaviour and the exporter's EditMode tests compare it with Unity 2022.3's
// Animator. Advance allocates nothing.
namespace VRVlog.AvatarAnimator
{
    // One instance per loaded avatar. The host sets parameters (Set), calls
    // Advance once per frame and writes Values to the targets. Values holds
    // every target's value after the last Advance: a target no layer wrote
    // keeps its previous value, starting from its Rest, except that a
    // parameter target goes back to its Rest (M11). For a morph target
    // that is the Unity BlendShape weight, not the glTF weight. Changed is
    // false after an Advance that skipped a settled frame (see Advance):
    // Values did not change, and the host need not write them.
    //
    // The marks name rules the README explains ("Avatar animator evaluator").
    // M1-M7 and M11-M14 are measured: Tests/AnimatorComparison records Unity's Animator
    // and Tools/Fixtures/AvatarAnimator replays the recordings here.
    // M1 an animated activeSelf is on at any value other than 0; M2 an exit
    // time e is crossed when the state's normalized time passes e (e >= 1
    // once, e < 1 at each loop after the start), and the rest of the frame
    // already advances the transition and its destination; M3 a layer whose
    // transition ended this frame starts none until the next; M4 a trigger
    // used by a transition is reset once every layer has stepped; M5 a
    // running transition whose interruption source is not none is
    // interrupted by Any State's transitions first, then by its source's or
    // destination's; ordered, a list stops before the running transition,
    // otherwise the running transition interrupts itself; an interrupted
    // transition blends from a still copy, and its source's time stops;
    // M6 for a target a state's motion does not write: mid-transition, when
    // the other side's motion writes it, a Write Defaults side gives the
    // rest; otherwise a Write Defaults state leaves it to the layers below
    // (the rest when none wrote it), and a state without Write Defaults
    // gives what the layers below leave (the last frame's value when none
    // wrote it) and applies it with the layer's weight. A state without a motion gives, with Write Defaults,
    // the layers below (the rest when none wrote it); without, the layer's
    // own last value, held from the start of a transition out of it and
    // approached frame by frame in a transition into it. Unity poses a
    // humanoid layer whole: a muscle is the rest where a motion writing
    // other muscles does not write it (with or without Write Defaults), on
    // the side of a transition whose motion does not write it while either
    // side's writes a muscle, and as a layer's own last value of a muscle it never
    // wrote; a motion writing no muscle passes them through. A layer with a
    // state without Write Defaults keeps a value of every target the
    // controller animates; M7 a layer under weight 1 blends over the
    // layers below it, or over the rest when none wrote the target; at
    // weight 0 that leaves the layers below (or the rest), and the layer
    // forgets its own values, as if it had never written them.
    // M11 a float parameter a clip animates is a target that M6 and M7
    // blend as a node, with one difference: in a transition into a state
    // without a motion or Write Defaults, the layer's own value of it starts
    // from the layer's last value and takes the rest where the source side
    // had it, instead of the last frame's value. After each frame the
    // parameter is what the layers wrote, or the controller's default when
    // none did; an int, bool or trigger with a curve stays at that default;
    // neither the host nor a Parameter Driver changes such a parameter, and
    // transitions and trees see the value of the frame before.
    // M12 a 2D Freeform Cartesian tree gives each child the smallest, over
    // the other children, of a weight that falls from 1 at its own position
    // to 0 at the other's along the line between the two, not below 0, and
    // then divides by the sum; a child without a motion is not in the tree,
    // and two children at one position do not limit each other.
    // M13 a 2D Simple Directional tree takes the two neighbouring directions
    // around the point (a child in the direction of an earlier one is left
    // out) and gives them and the centre the point's barycentric weights in
    // their triangle; past the line between the two it gives the two their
    // weights divided by their sum. At (0, 0), between neighbours 180 degrees
    // or more apart, or with one direction, the centre takes all. Without a
    // child at (0, 0) the centre's weight is shared equally by the
    // directions; with more than one, the first takes all at (0, 0) and no
    // child has weight anywhere else.
    // M14 a 2D Freeform Directional tree is M12 in polar terms, each child
    // away from (0, 0) starting from 1 minus its angle to the point divided
    // by 180 degrees instead of 1: the line
    // from child i to child j runs over the difference of their lengths
    // divided by the mean of the two, and over the signed angle from i to j;
    // the point's offset from i is its length minus i's, divided by the same
    // mean, and the signed angle from i to it. Against a child at (0, 0) the
    // angle is the other child's to the point, and the point's angle from the
    // centre is 0; at (0, 0) the point's angle from any child is 0. With no
    // weight left, or at (0, 0) without a child there, the children with a
    // motion share equally.
    // M8-M10 stay assumptions: M8 and M10 are VRChat behaviours that plain
    // Unity cannot record, and no recording covers M9. The frame in which
    // transitions see a driver's change is the one the export's check
    // against Unity with the SDK (AvatarAnimatorCrossCheck) measured; whether
    // VRChat itself agrees is not. M8 the behaviours of a state run when the transition into
    // it starts, the machines' (outer first) before the state's; a Parameter
    // Driver's operations wait until every layer has stepped and then apply
    // in that order, reading the values of that moment (a copy too), so no
    // transition sees them before the next frame, in its own layer or a
    // later one, as Unity does when the SDK drives from OnStateEnter; M9 a
    // state without a motion, or with a clip of length 0, is 1 second long;
    // M10 a layer or playable weight blend moves from the frame after the
    // behaviour that starts it, and a blend of 0 seconds applies at once.
    // M15 and M16 are assumptions too, from VRChat's documentation and its
    // SDK's playable graph; 5-3 measures them. M15 Gesture, Action and FX
    // play in that order, each over the ones before: a controller's layers
    // see only its own (M6 and M7 take the rest where none of its layers
    // wrote a target), and the controller's output then blends over what
    // the ones before it gave by its playable weight (1 at the start, 0 for
    // Action), which a Playable Layer Control sets; where it wrote nothing
    // the ones before show through. A controller whose timing the evaluator
    // cannot play as Unity (an additive layer, a synced layer with Timing or
    // of another shape, a random with preventRepeats, a blend tree of a kind
    // it does not know, or something that drives its transitions the export
    // left out of it) is skipped, and the others play as if it were not
    // there; the FX is not skipped but refused (Supports). M16 a layer's
    // mask keeps it off the muscles of the body parts it leaves out and,
    // when it names transforms, off the translation, rotation and scale of
    // the nodes it leaves out (Unity's mask filters no other property), and
    // the FX applies no muscle when its base layer has no mask. The base
    // layer's mask also keeps the whole controller off the muscles of the
    // parts it leaves out, as VRChat is taken to mask the playable layer
    // with it (an assumption like the FX's; plain Unity does not: its upper
    // layers write there). Measured in Unity (the gestureAction and actionMask cases): a
    // controller whose motions write a muscle writes every muscle it is not
    // kept off, at the rest where none of its layers wrote one, over the
    // controllers below.
    public sealed class AvatarAnimatorEvaluator
    {
        // M9.
        public const double EmptyLength = 1;

        // Unity turns an animated activeSelf on at any value other than 0, negative too.
        public static bool Active(double value) => value != 0;

        // The omitted kinds that change how the controller moves: playing
        // without them would take other transitions than Unity.
        static readonly string[] TimingKinds = AvatarAnimatorData.TransitionOmittedKinds;

        static readonly string[] BlendTypes = { "1d", "direct", "2dSimpleDirectional", "2dFreeformCartesian", "2dFreeformDirectional" };

        // Whether this evaluator plays the data as Unity would. False when the
        // exporter left out something that drives the FX's transitions (the
        // data's Omitted), or the FX (Controllers[0]) uses what is not
        // implemented yet (ControllerReason).
        // A gesture or action controller the evaluator cannot play is skipped
        // instead (SkipReason), and the others play as if it were not there
        // (M15). reason is null when true. A morph target blends as an active
        // one does: its value is the Unity weight, which the host maps to the
        // glTF weight with Baked and End. A parameter target is M11.
        public static bool Supports(AvatarAnimatorData data, out string reason)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            reason = TimingReason(data.Omitted) ?? (data.Controllers.Count > 0 ? ControllerReason(data, 0) : null);
            if (reason != null) return false;
            // A motion only a skipped controller plays does not count.
            var skipped = new HashSet<int>();
            for (var c = 1; c < data.Controllers.Count; c++)
                if (ControllerReason(data, c) != null) skipped.UnionWith(Reached(data, data.Controllers[c]));
            for (var m = 0; m < data.Motions.Count; m++)
                if (!skipped.Contains(m) && UnsupportedTree(data.Motions[m]))
                {
                    reason = "Blend trees of type " + data.Motions[m].BlendType + " are not supported.";
                    return false;
                }
            return true;
        }

        static string TimingReason(List<AvatarAnimatorData.Omission> omitted)
        {
            foreach (var omission in omitted)
                if (Array.IndexOf(TimingKinds, omission.Kind) >= 0)
                    return "The export left out " + omission.Count + " " + omission.Kind + " entries that drive transitions.";
            return null;
        }

        static bool UnsupportedTree(AvatarAnimatorData.Motion motion) => motion.Type == "blendTree" && Array.IndexOf(BlendTypes, motion.BlendType) < 0;

        // The motions the controller's states play, their children too.
        static HashSet<int> Reached(AvatarAnimatorData data, AvatarAnimatorData.Controller controller)
        {
            var seen = new HashSet<int>();
            var pending = new Stack<int>();
            foreach (var layer in controller.Layers)
                foreach (var state in layer.States)
                    if (state.Motion >= 0) pending.Push(state.Motion);
            while (pending.Count > 0)
            {
                var m = pending.Pop();
                if (!seen.Add(m) || data.Motions[m].Type != "blendTree") continue;
                foreach (var child in data.Motions[m].Children)
                    if (child.Motion >= 0) pending.Push(child.Motion);
            }
            return seen;
        }

        // Why the evaluator cannot play the controller, or null when it can:
        // additive layers, synced layers that affect timing or do not match
        // their source, a blend tree of another kind, a random that prevents
        // repeats, an omission of its own that drives transitions.
        static string ControllerReason(AvatarAnimatorData data, int index)
        {
            var controller = data.Controllers[index];
            var omitted = TimingReason(controller.Omitted);
            if (omitted != null) return omitted;
            // How VRChat picks again when a random would repeat is not measured.
            bool PreventsRepeats(List<AvatarAnimatorData.Behaviour> behaviours)
            {
                foreach (var behaviour in behaviours)
                    foreach (var operation in behaviour.Operations)
                        if (operation.PreventRepeats) return true;
                return false;
            }
            foreach (var layer in controller.Layers)
            {
                var prevents = false;
                foreach (var machine in layer.Machines) prevents |= PreventsRepeats(machine.Behaviours);
                foreach (var state in layer.States) prevents |= PreventsRepeats(state.Behaviours);
                if (prevents) return "A random that prevents repeats is not supported.";
            }
            foreach (var layer in controller.Layers)
            {
                if (layer.Blending != "override") return "Additive layers are not supported.";
                if (layer.SyncedLayer >= 0 && layer.SyncedLayerAffectsTiming) return "Synced layers that affect timing are not supported.";
                if (layer.SyncedLayer >= 0 && (layer.SyncedLayer >= controller.Layers.Count || !AvatarAnimatorData.SyncedShape(layer, controller.Layers[layer.SyncedLayer])))
                    return "A synced layer does not match its source layer.";
            }
            foreach (var m in Reached(data, controller))
                if (UnsupportedTree(data.Motions[m])) return "Blend trees of type " + data.Motions[m].BlendType + " are not supported.";
            return null;
        }

        // VRChat's playable layers from the bottom: the gesture, the action,
        // then the FX on top (Controllers holds the FX first).
        static readonly string[] PlayOrder = { "gesture", "action", "fx" };

        // A playable's weight before a Playable Layer Control moves it: the
        // action rests at 0 until an emote raises it, the others at 1.
        static double PlayableRest(string playable) => playable == "action" ? 0 : 1;

        // Unity's AvatarMaskBodyPart of the muscle (HumanTrait.MuscleName's
        // order): Body 1, Head 2 (neck, head, eyes, jaw), LeftLeg 3,
        // RightLeg 4, LeftArm 5 and RightArm 6 (shoulder to hand),
        // LeftFingers 7, RightFingers 8.
        internal static int MuscleBodyPart(int muscle) =>
            muscle < 9 ? 1 : muscle < 21 ? 2 : muscle < 29 ? 3 : muscle < 37 ? 4 : muscle < 46 ? 5 : muscle < 55 ? 6 : muscle < 75 ? 7 : 8;

        const int BoolType = 0, IntType = 1, FloatType = 2, TriggerType = 3;

        sealed class Side
        {
            internal double[] Accumulated, AccumulatedWeight, Value;
            // A stepped target's Accumulated is the value of the motion of
            // the most weight, Best that weight.
            internal double[] Best;
            internal int[] Stamp;
            internal int Current;
            internal double Total;
            // M6: whether a clip with weight writes a muscle, which makes the
            // side a humanoid pose: the muscles it does not write are the rest.
            internal bool Muscles;
        }

        sealed class LayerRun
        {
            internal AvatarAnimatorData.Layer Layer;
            // Index in the evaluator's layers; Controller the index in
            // Data.Controllers; First whether it is its controller's base layer.
            internal int Index, Source = -1, Controller;
            internal bool First;
            // Whether Animated holds every target the controller animates
            // (no mask leaves one out), so the layer may pass them all through.
            internal bool AnimatesAll;
            internal int[] Animated;
            // Out holds this layer's value of Animated[k] after the last Advance.
            internal double[] Out, Snapshot, Held;
            // M11: a parameter's value of its own in an empty state without
            // Write Defaults, which a node takes from Out.
            internal double[] Own;
            internal bool[] OutWritten, SnapshotWritten;
            internal bool Started, Transitioning, Frozen, ActiveFromAny;
            internal int State = -1, Next = -1;
            internal double Time, NextTime, Progress, Duration;
            internal AvatarAnimatorData.Transition Active;
            internal double Weight, WeightFrom, WeightGoal, WeightDuration, WeightElapsed;
            internal bool WeightBlending;
            internal int EnterSerial, EnteredState = -1, SeenEnterSerial, InterruptSerial, SeenInterruptSerial, HeldSerial;
            // Per state: the indexes in Animated of the targets its motion writes.
            internal int[][] StateKeys;
            // Per state, whether its motion has a clip that writes a muscle.
            internal bool[] StateMuscles;
            // The state in which the layer passed the other targets through in
            // the last frame (see Output), or -1. Out does not hold those targets then.
            internal int Passed = -1;
            // The state's and the transition's destination's lengths while the
            // evaluator is settled (see Advance).
            internal double SettledLength, SettledNextLength;
            // Mid-transition: whether a value of the last Output depended on
            // the progress (the two sides gave a target different values).
            internal bool Mixed;
            // While settled: the transitions with an exit time that may be
            // taken once the state's time (WatchTime) or the destination's
            // (WatchNext) crosses it, and the times and progress the frame
            // being skipped reaches.
            internal List<AvatarAnimatorData.Transition> WatchTime, WatchNext;
            internal double PendingTime, PendingNext, PendingProgress;
            // The transition of no length without an exit time by which the
            // layer, resting in a state, entered that state again this frame (or null).
            internal AvatarAnimatorData.Transition Repeated;
        }

        readonly AvatarAnimatorData data;
        readonly Func<double> random;
        readonly int[] types;
        readonly double[] parameters;
        readonly double[] rest, values;
        // The values at the start of this frame and of the last one.
        double[] previous, lastPrevious;
        readonly bool[] written;
        // Whether the motions of the controller being played write a target
        // (one array per controller), and whether a layer passing every such
        // target through (see Output) has marked them all written this frame;
        // Wrote reads both.
        bool[] isAnimated;
        readonly bool[][] controllerAnimated;
        // Per controller: the muscle targets it writes over the controllers
        // below, its layers' or not (M16), or null when it writes no muscle.
        readonly bool[][] controllerPose;
        bool animatedWritten;
        // Per controller: the range of its layers in layers (an empty range
        // when it is skipped), why it is skipped (null when it plays) and its
        // playable weight with the Playable Layer Control blend moving it.
        readonly int[] controllerStart, controllerEnd;
        readonly string[] skipReasons;
        readonly double[] playableWeight, playableFrom, playableGoal, playableDuration, playableElapsed;
        readonly bool[] playableBlending;
        // The controllers that play, from the bottom (PlayOrder).
        readonly int[] played;
        // With more than one controller playing: the values and Written that
        // the controllers below leave, over which the next one blends.
        readonly double[] below;
        readonly bool[] belowWritten;
        // Per target, the share of what lies under every controller (HostShare).
        readonly double[] hostShare;
        // Each value a layer set in values this frame and the last, in layer
        // order: the layer, the value and the same target's entry before it
        // (-1 none). Head holds each target's latest entry (-1 none).
        int[] logLayer, logEarlier, logHead, lastLayer, lastEarlier, lastHead;
        double[] logValue, lastValue;
        int logCount;
        // M11: per parameter, the target of a clip's curves on it, or -1.
        readonly int[] parameterTargets;
        // M11: per target, whether it is a parameter.
        readonly bool[] isParameter;
        // Per target, whether it is stepped (Target.Stepped): wherever two
        // values meet, the one of more weight is taken whole.
        readonly bool[] stepped;
        // M6: per target, whether it is a muscle.
        readonly bool[] isMuscle;
        // M4: triggers a transition used, reset once every layer has stepped.
        readonly bool[] spent;
        // M8: the Parameter Drivers that ran this frame, applied once every layer has stepped.
        readonly List<AvatarAnimatorData.Behaviour> drivers;
        readonly LayerRun[] layers;
        // Per motion: the targets it writes, the order of a 1D tree's children
        // by threshold (a Simple Directional tree's directions by angle) and
        // the children's weights while it is evaluated.
        readonly int[][] motionTargets, childOrder;
        // A Simple Directional tree's first child at (0, 0) (-1 none) and
        // whether it has more than one.
        readonly int[] centreChild;
        readonly bool[] centres;
        readonly double[][] childWeights;
        // Per layer, per state: the targets the state's motion writes.
        readonly int[][][] stateTargets;
        readonly Side sideA, sideB;
        readonly AvatarAnimatorData.Transition[] chain;
        int chainCount;
        // Whether the next Advance may skip the layers (see Advance), whether
        // this frame changed a layer's state or weight, and the parameters at
        // the start of this frame.
        bool settled, moved, skips = true;
        readonly double[] startParameters;
        // Per motion: whether every curve it plays has one value at all times.
        readonly bool[] still;

        public AvatarAnimatorEvaluator(AvatarAnimatorData data, Func<double> random)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            if (!Supports(data, out var reason)) throw new NotSupportedException(reason);

            types = new int[data.Parameters.Count];
            parameters = new double[data.Parameters.Count];
            spent = new bool[data.Parameters.Count];
            startParameters = new double[data.Parameters.Count];
            for (var i = 0; i < types.Length; i++)
            {
                var p = data.Parameters[i];
                types[i] = p.Type == "bool" ? BoolType : p.Type == "int" ? IntType : p.Type == "float" ? FloatType : TriggerType;
                parameters[i] = Convert(types[i], p.Default);
            }

            var targetCount = data.Targets.Count;
            rest = new double[targetCount];
            parameterTargets = new int[data.Parameters.Count];
            isParameter = new bool[targetCount];
            stepped = new bool[targetCount];
            isMuscle = new bool[targetCount];
            hostShare = new double[targetCount];
            for (var i = 0; i < targetCount; i++) hostShare[i] = 1;
            for (var i = 0; i < parameterTargets.Length; i++) parameterTargets[i] = -1;
            for (var i = 0; i < targetCount; i++)
            {
                rest[i] = data.Targets[i].Rest;
                stepped[i] = data.Targets[i].Stepped;
                isMuscle[i] = data.Targets[i].Property == "muscle";
                if (data.Targets[i].Property != "parameter") continue;
                // M11: the controller's default, also when Expression Parameters declares another.
                var parameter = data.Targets[i].Parameter;
                parameterTargets[parameter] = i;
                isParameter[i] = true;
                parameters[parameter] = Convert(types[parameter], rest[i]);
            }
            values = (double[])rest.Clone();
            previous = (double[])rest.Clone();
            lastPrevious = (double[])rest.Clone();
            written = new bool[targetCount];
            sideA = NewSide(targetCount);
            sideB = NewSide(targetCount);

            motionTargets = new int[data.Motions.Count][];
            childOrder = new int[data.Motions.Count][];
            centreChild = new int[data.Motions.Count];
            centres = new bool[data.Motions.Count];
            childWeights = new double[data.Motions.Count][];
            still = new bool[data.Motions.Count];
            for (var m = 0; m < data.Motions.Count; m++) Still(m);
            for (var m = 0; m < data.Motions.Count; m++)
            {
                MotionTargets(m);
                var children = data.Motions[m].Children;
                childWeights[m] = new double[children.Count];
                var order = new int[children.Count];
                for (var c = 0; c < order.Length; c++) order[c] = c;
                // Stable: children with the same threshold keep their order.
                Array.Sort(order, (x, y) => children[x].Threshold != children[y].Threshold ? children[x].Threshold.CompareTo(children[y].Threshold) : x.CompareTo(y));
                childOrder[m] = order;
                if (data.Motions[m].BlendType == "2dSimpleDirectional") Directions(data.Motions[m], out childOrder[m], out centreChild[m], out centres[m]);
            }

            // The controllers that play, and their layers in one list: the FX's
            // first, so a layer of the FX keeps its index in the controller.
            var controllerCount = data.Controllers.Count;
            controllerStart = new int[controllerCount];
            controllerEnd = new int[controllerCount];
            skipReasons = new string[controllerCount];
            playableWeight = new double[controllerCount];
            playableFrom = new double[controllerCount];
            playableGoal = new double[controllerCount];
            playableDuration = new double[controllerCount];
            playableElapsed = new double[controllerCount];
            playableBlending = new bool[controllerCount];
            controllerAnimated = new bool[controllerCount][];
            var source = new List<AvatarAnimatorData.Layer>();
            var owners = new List<int>();
            for (var c = 0; c < controllerCount; c++)
            {
                skipReasons[c] = ControllerReason(data, c);
                playableWeight[c] = PlayableRest(data.Controllers[c].Playable);
                controllerStart[c] = source.Count;
                if (skipReasons[c] == null)
                    foreach (var layer in data.Controllers[c].Layers) { source.Add(layer); owners.Add(c); }
                controllerEnd[c] = source.Count;
                controllerAnimated[c] = new bool[targetCount];
            }
            var playing = new List<int>();
            foreach (var playable in PlayOrder)
                for (var c = 0; c < controllerCount; c++)
                    if (data.Controllers[c].Playable == playable && skipReasons[c] == null) playing.Add(c);
            played = playing.ToArray();
            if (played.Length > 1)
            {
                below = new double[targetCount];
                belowWritten = new bool[targetCount];
            }
            isAnimated = controllerCount > 0 ? controllerAnimated[0] : new bool[targetCount];

            // M16: whether the layer's mask keeps it off the target. A mask moves
            // only the muscles of its body parts and, when it names transforms
            // (Nodes), only the translation, rotation and scale of its nodes;
            // VRChat applies no muscle of an FX whose base layer has no mask.
            bool Masked(int c, AvatarAnimatorData.Layer layer, int t)
            {
                var target = data.Targets[t];
                if (target.Property != "muscle")
                    return layer.Mask?.Nodes != null && AvatarAnimatorData.IsTransform(target.Property) && layer.Mask.Nodes.BinarySearch(target.Node) < 0;
                var controller = data.Controllers[c];
                if (controller.Playable == "fx" && controller.Layers[0].Mask == null) return true;
                var part = MuscleBodyPart(target.Muscle);
                if (controller.Layers[0].Mask != null && !controller.Layers[0].Mask.Humanoid.Contains(part)) return true;
                return layer.Mask != null && !layer.Mask.Humanoid.Contains(part);
            }

            layers = new LayerRun[source.Count];
            stateTargets = new int[source.Count][][];
            var machines = 1;
            var controllerTargets = new HashSet<int>[controllerCount];
            for (var c = 0; c < controllerCount; c++) controllerTargets[c] = new HashSet<int>();
            for (var l = 0; l < source.Count; l++)
            {
                stateTargets[l] = new int[source[l].States.Count][];
                for (var s = 0; s < source[l].States.Count; s++)
                {
                    var motion = source[l].States[s].Motion;
                    stateTargets[l][s] = motion < 0 ? Array.Empty<int>() : motionTargets[motion];
                    foreach (var t in stateTargets[l][s])
                        if (!Masked(owners[l], source[l], t)) controllerTargets[owners[l]].Add(t);
                }
            }
            for (var c = 0; c < controllerCount; c++)
                foreach (var t in controllerTargets[c]) controllerAnimated[c][t] = true;
            controllerPose = new bool[controllerCount][];
            for (var c = 0; c < controllerCount; c++)
            {
                var controller = data.Controllers[c];
                var writesMuscle = false;
                foreach (var t in controllerTargets[c]) writesMuscle |= data.Targets[t].Property == "muscle";
                if (!writesMuscle) continue;
                controllerPose[c] = new bool[targetCount];
                for (var t = 0; t < targetCount; t++)
                    controllerPose[c][t] = data.Targets[t].Property == "muscle" && !Masked(c, controller.Layers[0], t);
            }
            // M11 holds for a parameter a playing controller animates. One only a
            // skipped controller animates stays a parameter the host and the
            // drivers set, with its declared default, as without that controller.
            for (var c = 0; c < controllerCount; c++)
            {
                if (skipReasons[c] == null) continue;
                foreach (var m in Reached(data, data.Controllers[c]))
                    foreach (var t in motionTargets[m])
                    {
                        var plays = false;
                        foreach (var targets in controllerTargets) plays |= targets.Contains(t);
                        if (!isParameter[t] || plays) continue;
                        var parameter = data.Targets[t].Parameter;
                        isParameter[t] = false;
                        parameterTargets[parameter] = -1;
                        parameters[parameter] = Convert(types[parameter], data.Parameters[parameter].Default);
                    }
            }
            var entries = 0;
            for (var l = 0; l < source.Count; l++)
            {
                var layer = source[l];
                var c = owners[l];
                machines = Math.Max(machines, layer.Machines.Count);
                // M6: a layer with a Write Defaults off state keeps its own value of
                // every target the controller animates, its motions or not.
                var animated = new HashSet<int>();
                var keepsAll = false;
                for (var s = 0; s < layer.States.Count; s++)
                {
                    animated.UnionWith(stateTargets[l][s]);
                    keepsAll |= !layer.States[s].WriteDefaults;
                }
                if (keepsAll) animated.UnionWith(controllerTargets[c]);
                animated.RemoveWhere(t => Masked(c, layer, t));
                var list = new List<int>(animated);
                list.Sort();
                entries += list.Count;
                var keys = new int[layer.States.Count][];
                var found = new List<int>();
                for (var s = 0; s < keys.Length; s++)
                {
                    // A target the mask keeps the layer off is not among Animated.
                    found.Clear();
                    foreach (var t in stateTargets[l][s])
                    {
                        var k = list.BinarySearch(t);
                        if (k >= 0) found.Add(k);
                    }
                    keys[s] = found.ToArray();
                }
                var first = l == controllerStart[c];
                layers[l] = new LayerRun
                {
                    Layer = layer, Index = l, Source = layer.SyncedLayer >= 0 ? controllerStart[c] + layer.SyncedLayer : -1,
                    Controller = c, First = first, AnimatesAll = list.Count == controllerTargets[c].Count, Animated = list.ToArray(),
                    Out = new double[list.Count], Snapshot = new double[list.Count], Held = new double[list.Count], Own = new double[list.Count],
                    OutWritten = new bool[list.Count], SnapshotWritten = new bool[list.Count],
                    Weight = first ? 1 : layer.Weight, StateKeys = keys,
                    StateMuscles = Array.ConvertAll(stateTargets[l], targets => Array.Exists(targets, t => isMuscle[t]))
                };
                // Sized for every transition of the layer, so Settles never grows them.
                var transitions = layer.Machines.Count > 0 ? layer.Machines[0].AnyStateTransitions.Count : 0;
                foreach (var state in layer.States) transitions += state.Transitions.Count;
                layers[l].WatchTime = new List<AvatarAnimatorData.Transition>(transitions);
                layers[l].WatchNext = new List<AvatarAnimatorData.Transition>(transitions);
            }
            // A layer sets each target it keeps at most once a frame.
            logLayer = new int[entries]; logEarlier = new int[entries]; logValue = new double[entries];
            lastLayer = new int[entries]; lastEarlier = new int[entries]; lastValue = new double[entries];
            logHead = new int[targetCount]; lastHead = new int[targetCount];
            for (var t = 0; t < targetCount; t++) logHead[t] = lastHead[t] = -1;
            // A resolution passes each machine at most twice (in through Entry, out through Exit).
            chain = new AvatarAnimatorData.Transition[2 * machines + 2];
            // A layer enters at most two states in a frame (its default state and
            // a transition of 0 seconds on the first one), each running a driver
            // at most once, so a frame does not grow the list.
            var driverCount = 0;
            foreach (var layer in source)
            {
                foreach (var machine in layer.Machines)
                    foreach (var behaviour in machine.Behaviours)
                        if (behaviour.Type == "parameterDriver") driverCount++;
                foreach (var state in layer.States)
                    foreach (var behaviour in state.Behaviours)
                        if (behaviour.Type == "parameterDriver") driverCount++;
            }
            drivers = new List<AvatarAnimatorData.Behaviour>(2 * driverCount);
        }

        static Side NewSide(int count) => new Side
        {
            Accumulated = new double[count], AccumulatedWeight = new double[count], Value = new double[count], Best = new double[count], Stamp = new int[count]
        };

        int[] MotionTargets(int motion)
        {
            if (motionTargets[motion] != null) return motionTargets[motion];
            var m = data.Motions[motion];
            var set = new HashSet<int>();
            if (m.Type == "clip")
                foreach (var track in data.Clips[m.Clip].Tracks) set.Add(track.Target);
            else
                foreach (var child in m.Children)
                    if (child.Motion >= 0) set.UnionWith(MotionTargets(child.Motion));
            var list = new List<int>(set);
            list.Sort();
            return motionTargets[motion] = list.ToArray();
        }

        // A clip whose curves each hold one value (keys of one value, flat
        // between them), or a tree of such clips.
        void Still(int motion)
        {
            var m = data.Motions[motion];
            var flat = true;
            if (m.Type == "clip")
                foreach (var track in data.Clips[m.Clip].Tracks) flat &= Flat(track.Curves[0]);
            else
                foreach (var child in m.Children)
                    if (child.Motion >= 0)
                    {
                        // Children come in any order, so a child may not be judged yet.
                        Still(child.Motion);
                        flat &= still[child.Motion];
                    }
            still[motion] = flat;
        }

        static bool Flat(Expressions.ExpressionAnimationData.Curve curve)
        {
            var keys = curve.Keys;
            for (var i = 1; i < keys.Count; i++)
            {
                if (keys[i].Value != keys[0].Value) return false;
                var a = keys[i - 1].OutTangent;
                var b = keys[i].InTangent;
                if (a.HasValue && b.HasValue && (a.Value != 0 || b.Value != 0)) return false;
            }
            return true;
        }

        public AvatarAnimatorData Data => data;
        // False when the last Advance skipped the layers because the evaluator
        // was settled (see Advance): Values and Written are those of the
        // Advance before, so the host need not write them again. True after
        // any other Advance.
        public bool Changed { get; private set; } = true;
        // Whether Advance skips the layers while the evaluator is settled. On
        // by default; off evaluates every frame, which gives the same values.
        public bool SkipsSettledFrames
        {
            get => skips;
            set { skips = value; settled = false; }
        }
        // Each target's value, by the index of Data.Targets.
        public IReadOnlyList<double> Values => values;
        // Whether a layer wrote the target in the last Advance.
        public IReadOnlyList<bool> Written => written;
        // Per target, how much of what lies under every controller (VRChat's
        // Base, which is the host's own pose) is left in Values after the last
        // Advance: the product of (1 - playable weight) over the controllers
        // that wrote it, and 1 when none did. Values blend from the rest
        // there, so a host whose value is h shows Values + (h - rest) * share.
        public IReadOnlyList<double> HostShare => hostShare;

        // The first parameter of that name, or -1.
        public int ParameterIndex(string name)
        {
            for (var i = 0; i < data.Parameters.Count; i++)
                if (data.Parameters[i].Name == name) return i;
            return -1;
        }

        public double Get(int parameter) => parameters[parameter];

        // As Unity stores it: a bool or trigger is 0 or 1, an int is truncated
        // and a float is single precision. A trigger set to 1 stays set until a
        // transition uses it. M11: a parameter a clip animates does not change.
        public void Set(int parameter, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (parameterTargets[parameter] >= 0) return;
            var converted = Convert(types[parameter], value);
            if (converted != parameters[parameter]) settled = false;
            parameters[parameter] = converted;
        }

        static double Convert(int type, double value)
        {
            switch (type)
            {
                case BoolType:
                case TriggerType: return value != 0 ? 1 : 0;
                case IntType: return Math.Truncate(Math.Max(int.MinValue, Math.Min(int.MaxValue, value)));
                // A double beyond the float range would become Infinity and then NaN in the blends.
                default: return (float)Math.Max(-float.MaxValue, Math.Min(float.MaxValue, value));
            }
        }

        // For tests and diagnostics: the layer's state, the destination of its
        // running transition (-1 when none), its progress and normalized times.
        public int LayerCount => layers.Length;
        public int State(int layer) => layers[layer].State;
        public int NextState(int layer) => layers[layer].Transitioning ? layers[layer].Next : -1;
        public double TransitionProgress(int layer) => layers[layer].Transitioning ? layers[layer].Progress : 0;
        public double NormalizedTime(int layer) => layers[layer].Time;
        public double NextNormalizedTime(int layer) => layers[layer].Transitioning ? layers[layer].NextTime : 0;
        public double LayerWeight(int layer) => layers[layer].Weight;
        // The FX's playable weight (Playable Layer Control); 1 without an FX.
        public double FxWeight => playableWeight.Length > 0 ? playableWeight[0] : 1;
        // Per controller of Data.Controllers: its playable weight, why the
        // evaluator skips it (null when it plays), and the index above
        // (State, LayerWeight...) of one of its layers, or -1 when it is
        // skipped. The FX's layers keep their own indexes.
        public double PlayableWeight(int controller) => playableWeight[controller];
        public string SkipReason(int controller) => skipReasons[controller];
        public int LayerIndex(int controller, int layer)
        {
            if (layer < 0 || layer >= data.Controllers[controller].Layers.Count) throw new ArgumentOutOfRangeException(nameof(layer));
            return skipReasons[controller] != null ? -1 : controllerStart[controller] + layer;
        }

        // As Unity's Animator.SetLayerWeight: from the next Advance, ending a
        // Layer Control blend. A controller's base layer stays at 1. The comparison with
        // Unity drives weights through this.
        public void SetLayerWeight(int layer, double weight)
        {
            if (double.IsNaN(weight) || double.IsInfinity(weight)) throw new ArgumentOutOfRangeException(nameof(weight));
            if (layers[layer].First) return;
            settled = false;
            layers[layer].WeightBlending = false;
            layers[layer].Weight = weight;
        }

        // One frame of deltaTime seconds (0 or more). The first call enters
        // every layer's default state, then advances.
        //
        // Settled (#3664): when a frame changed no layer's state or weight, no
        // value and no parameter, no layer is in a weight blend or in a
        // transition whose two sides give different values, every state's
        // motion gives the same values at every time to come, and no
        // transition can start while the parameters stay but at a watched exit
        // time, the next frame would repeat it. Until Set or SetLayerWeight
        // changes something, or a transition ends or a watched exit time comes,
        // Advance then only moves the layers' normalized times and the
        // transitions' progress and leaves Values, Written and the parameters
        // as they are (Changed false).
        public void Advance(double deltaTime)
        {
            if (!(deltaTime >= 0) || double.IsInfinity(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (settled && AdvanceSettled(deltaTime)) return;
            settled = false;
            Changed = true;
            moved = false;
            Array.Copy(parameters, startParameters, parameters.Length);
            var swap = lastPrevious; lastPrevious = previous; previous = swap;
            Array.Copy(values, previous, values.Length);
            Array.Clear(written, 0, written.Length);
            var swapLog = lastLayer; lastLayer = logLayer; logLayer = swapLog;
            swapLog = lastEarlier; lastEarlier = logEarlier; logEarlier = swapLog;
            swapLog = lastHead; lastHead = logHead; logHead = swapLog;
            var swapValue = lastValue; lastValue = logValue; logValue = swapValue;
            for (var t = 0; t < logHead.Length; t++) logHead[t] = -1;
            logCount = 0;
            // M10: a weight blend started by a behaviour moves from the next frame on.
            foreach (var layer in layers) BlendWeight(layer, deltaTime);
            for (var c = 0; c < playableBlending.Length; c++)
            {
                if (!playableBlending[c]) continue;
                moved = true;
                playableElapsed[c] += deltaTime;
                var t = playableDuration[c] > 0 ? Math.Min(1, playableElapsed[c] / playableDuration[c]) : 1;
                playableWeight[c] = playableFrom[c] + (playableGoal[c] - playableFrom[c]) * t;
                if (t >= 1) playableBlending[c] = false;
            }
            for (var t = 0; t < hostShare.Length; t++) hostShare[t] = 1;
            foreach (var c in played) Play(c, deltaTime);
            for (var p = 0; p < spent.Length; p++)
                if (spent[p])
                {
                    spent[p] = false;
                    parameters[p] = 0;
                }
            // M11: the next frame's transitions and trees read what the layers wrote.
            for (var p = 0; p < parameterTargets.Length; p++)
            {
                var t = parameterTargets[p];
                if (t < 0) continue;
                values[t] = types[p] == FloatType && written[t] ? Convert(FloatType, values[t]) : rest[t];
                parameters[p] = values[t];
            }
            // M8: after every layer, so the transitions see the drivers' values from the next frame on.
            foreach (var driver in drivers)
                foreach (var operation in driver.Operations) Drive(operation);
            drivers.Clear();
            settled = skips && Settles();
        }

        // A settled frame: only the normalized times and the transitions'
        // progress move, as Step and Sync move them. False, moving nothing,
        // when in this frame a transition would end or a watched exit time
        // would be crossed: then the frame is evaluated. The check takes the
        // times and progress Step would reach, by the same arithmetic, so it
        // finds the frame in which the evaluator that skips nothing gets there.
        bool AdvanceSettled(double dt)
        {
            foreach (var layer in layers)
            {
                // A layer that enters its state again each frame stays at the transition's offset.
                if (layer.Source >= 0 || layer.State < 0 || layer.Repeated != null) continue;
                var states = layer.Layer.States;
                layer.PendingTime = AdvanceTime(states[layer.State], layer.Time, dt, layer.SettledLength);
                if (Crosses(layer.WatchTime, layer.Time, layer.PendingTime)) return false;
                if (!layer.Transitioning) continue;
                layer.PendingNext = AdvanceTime(states[layer.Next], layer.NextTime, dt, layer.SettledNextLength);
                layer.PendingProgress = layer.Progress + (layer.Duration > 0 ? dt / layer.Duration : 1);
                if (layer.PendingProgress >= 1 || Crosses(layer.WatchNext, layer.NextTime, layer.PendingNext)) return false;
            }
            Changed = false;
            foreach (var layer in layers)
            {
                if (layer.Source >= 0)
                {
                    var source = layers[layer.Source];
                    layer.Time = source.Time;
                    layer.NextTime = source.NextTime;
                    layer.Progress = source.Progress;
                    continue;
                }
                if (layer.State < 0 || layer.Repeated != null) continue;
                layer.Time = layer.PendingTime;
                if (!layer.Transitioning) continue;
                layer.NextTime = layer.PendingNext;
                layer.Progress = layer.PendingProgress;
            }
            return true;
        }

        // Whether TryFire would find one of the transitions' exit times
        // crossed from n0 to n1 (their conditions hold while settled).
        static bool Crosses(List<AvatarAnimatorData.Transition> watched, double n0, double n1)
        {
            foreach (var transition in watched)
                if (Crossing(transition.ExitTime, n0, n1) <= n1) return true;
            return false;
        }

        // Whether the next frame would repeat this one (see Advance).
        bool Settles()
        {
            if (moved || Array.IndexOf(playableBlending, true) >= 0) return false;
            for (var p = 0; p < parameters.Length; p++)
                if (parameters[p] != startParameters[p]) return false;
            for (var t = 0; t < values.Length; t++)
                if (values[t] != previous[t]) return false;
            // A transition whose two sides give every target one value moves
            // only its progress, until it ends; not one interrupted (M5), and
            // not in its first frame, where it may have had no progress yet.
            foreach (var layer in layers)
                if (layer.WeightBlending || (layer.Transitioning && (layer.Frozen || !(layer.Progress > 0) || layer.Mixed))) return false;
            foreach (var layer in layers)
            {
                if (layer.State < 0) continue;
                // A synced layer plays its own motions on its source's time.
                var clock = layer.Source >= 0 ? layers[layer.Source] : layer;
                if (clock.State < 0) return false;
                var timing = clock.Layer.States[clock.State];
                var rate = Rate(timing);
                if (timing.TimeParameter.HasValue && clock.Time != parameters[timing.TimeParameter.Value]) return false;
                var state = layer.Layer.States[layer.State];
                // A layer entering its state again each frame plays it at the same time each frame.
                var repeated = clock.Repeated != null;
                if (repeated && (layer.Source >= 0 || Random(layer.Layer, layer.State))) return false;
                if (!repeated && !Holds(state, layer.Time, clock.Time, rate)) return false;
                double nextRate = 0;
                if (layer.Transitioning)
                {
                    if (!clock.Transitioning) return false;
                    var nextTiming = clock.Layer.States[clock.Next];
                    nextRate = Rate(nextTiming);
                    if (nextTiming.TimeParameter.HasValue && clock.NextTime != parameters[nextTiming.TimeParameter.Value]) return false;
                    if (!Holds(layer.Layer.States[layer.Next], layer.NextTime, clock.NextTime, nextRate)) return false;
                }
                if (layer.Source >= 0) continue;
                var any = layer.Layer.Machines[0].AnyStateTransitions;
                layer.WatchTime.Clear();
                layer.WatchNext.Clear();
                if (repeated)
                {
                    if (!Dormant(layer, any, rate) || !Dormant(layer, state.Transitions, rate)) return false;
                }
                else if (!layer.Transitioning)
                {
                    Watch(layer.WatchTime, any, layer.Time, rate);
                    Watch(layer.WatchTime, state.Transitions, layer.Time, rate);
                }
                else
                {
                    // The transitions Step tries mid-transition (M5); more is only an extra evaluated frame.
                    var source = layer.Active.InterruptionSource;
                    if (source != "none")
                    {
                        Watch(layer.WatchTime, any, layer.Time, rate);
                        if (source != "destination") Watch(layer.WatchTime, state.Transitions, layer.Time, rate);
                        if (source != "source") Watch(layer.WatchNext, layer.Layer.States[layer.Next].Transitions, layer.NextTime, nextRate);
                    }
                    layer.SettledNextLength = StateLength(layer.Layer.States[layer.Next]);
                }
                layer.SettledLength = StateLength(state);
            }
            return true;
        }

        double Rate(AvatarAnimatorData.State timing) => timing.TimeParameter.HasValue ? 0 : timing.Speed * ParameterOr(timing.SpeedParameter, 1);

        // Whether the state's motion gives the same values at every time to
        // come: from time, which moves to the clock's time first (a synced
        // layer before its source takes the source's time a frame late), then
        // at the rate. A clock that moves reaches the clock's time on the way.
        bool Holds(AvatarAnimatorData.State state, double time, double clockTime, double rate) =>
            state.Motion < 0 || still[state.Motion]
            || (rate == 0 ? time == clockTime : Clamped(state.Motion, time + ParameterOr(state.CycleOffsetParameter, state.CycleOffset), rate > 0));

        // The transitions with an exit time Dormant would not let rest: their
        // conditions hold and their time moves toward an exit time not passed.
        void Watch(List<AvatarAnimatorData.Transition> watched, List<AvatarAnimatorData.Transition> transitions, double time, double rate)
        {
            foreach (var transition in transitions)
            {
                if (!transition.HasExitTime || rate <= 0 || !Pass(transition.Conditions)) continue;
                if (transition.ExitTime >= 1 && time >= transition.ExitTime) continue;
                watched.Add(transition);
            }
        }

        // Whether entering the state runs a Parameter Driver that draws a
        // random number: skipping the frame would not draw it.
        static bool Random(AvatarAnimatorData.Layer layer, int state)
        {
            bool Draws(List<AvatarAnimatorData.Behaviour> behaviours)
            {
                foreach (var behaviour in behaviours)
                    foreach (var operation in behaviour.Operations)
                        if (operation.Type == "random") return true;
                return false;
            }
            var s = layer.States[state];
            if (Draws(s.Behaviours)) return true;
            for (var machine = s.Machine; machine >= 0; machine = machine > 0 ? layer.Machines[machine].Parent : -1)
                if (Draws(layer.Machines[machine].Behaviours)) return true;
            return false;
        }

        // A motion whose clips do not loop and whose time has run past their
        // end (forward) or before their start, so it stays at their last or
        // first keys, or whose curves hold one value.
        bool Clamped(int motion, double normalized, bool forward)
        {
            if (still[motion]) return true;
            var m = data.Motions[motion];
            if (m.Type == "clip")
            {
                var clip = data.Clips[m.Clip];
                if (clip.Duration <= 0) return true;
                return !clip.Loop && (forward ? normalized >= 1 : normalized <= 0);
            }
            foreach (var child in m.Children)
                if (child.Motion >= 0 && !Clamped(child.Motion, normalized + child.CycleOffset, forward)) return false;
            return true;
        }

        // Whether none of the transitions can start while the parameters
        // stay. This frame looked at all of them and took none, so one without
        // an exit time stays as it is; one with an exit time waits for a
        // crossing only while its conditions hold and its time moves forward
        // toward an exit time not yet passed (M2).
        bool Dormant(LayerRun layer, List<AvatarAnimatorData.Transition> transitions, double rate)
        {
            foreach (var transition in transitions)
            {
                if (ReferenceEquals(transition, layer.Repeated)) continue;
                if (!transition.HasExitTime || rate <= 0 || !Pass(transition.Conditions)) continue;
                if (transition.ExitTime >= 1 && layer.Time >= transition.ExitTime) continue;
                return false;
            }
            return true;
        }

        void Step(LayerRun layer, double dt)
        {
            var states = layer.Layer.States;
            layer.Repeated = null;
            if (!layer.Started)
            {
                moved = true;
                layer.Started = true;
                var initial = layer.Layer.Machines[0].DefaultState;
                if (initial >= 0)
                {
                    layer.State = initial;
                    layer.Time = 0;
                    Enter(layer, initial);
                }
            }
            if (layer.State < 0) return;

            var n0 = layer.Time;
            var length = StateLength(states[layer.State]);
            // M5: after an interruption the source is a still copy, so its time
            // stops too and its exit times are not crossed again.
            if (!layer.Frozen) layer.Time = AdvanceTime(states[layer.State], n0, dt, length);
            double next0 = 0;
            if (layer.Transitioning)
            {
                next0 = layer.NextTime;
                layer.NextTime = AdvanceTime(states[layer.Next], next0, dt, StateLength(states[layer.Next]));
                layer.Progress += layer.Duration > 0 ? dt / layer.Duration : 1;
                if (layer.Progress >= 1)
                {
                    // M3: a layer whose transition ended this frame starts none until the next.
                    moved = true;
                    Complete(layer);
                    return;
                }
            }
            var any = layer.Layer.Machines[0].AnyStateTransitions;
            if (!layer.Transitioning)
            {
                if (!TryFire(layer, any, true, -1, n0, layer.Time, dt, length))
                    TryFire(layer, states[layer.State].Transitions, false, -1, n0, layer.Time, dt, length);
                return;
            }
            // M5: Any State's transitions interrupt first, unless nothing may.
            var source = layer.Active.InterruptionSource;
            if (source == "none") return;
            var ordered = layer.Active.OrderedInterruption;
            if (TryFire(layer, any, true, ordered && layer.ActiveFromAny ? IndexOf(any, layer.Active) : -1, n0, layer.Time, dt, length))
                return;
            var nextLength = StateLength(states[layer.Next]);
            switch (source)
            {
                case "source":
                    TrySource(layer, ordered, n0, dt, length);
                    break;
                case "destination":
                    TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength);
                    break;
                case "sourceThenDestination":
                    if (!TrySource(layer, ordered, n0, dt, length))
                        TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength);
                    break;
                case "destinationThenSource":
                    if (!TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength))
                        TrySource(layer, ordered, n0, dt, length);
                    break;
            }
        }

        // The source state's own transitions; a transition from Any State
        // has had its source checked already.
        bool TrySource(LayerRun layer, bool ordered, double n0, double dt, double length)
        {
            if (layer.ActiveFromAny) return false;
            var own = layer.Layer.States[layer.State].Transitions;
            return TryFire(layer, own, false, ordered ? IndexOf(own, layer.Active) : -1, n0, layer.Time, dt, length);
        }

        static int IndexOf(List<AvatarAnimatorData.Transition> list, AvatarAnimatorData.Transition transition)
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], transition)) return i;
            return -1;
        }

        // The first transition of the list that may be taken now. until >= 0
        // limits the list to the entries before it (ordered interruption);
        // otherwise the running transition may start again.
        bool TryFire(LayerRun layer, List<AvatarAnimatorData.Transition> list, bool fromAny, int until,
            double n0, double n1, double dt, double sourceLength)
        {
            var count = until >= 0 ? until : list.Count;
            for (var i = 0; i < count; i++)
            {
                var transition = list[i];
                // Unity never takes a transition with neither an exit time nor a condition.
                if (!transition.HasExitTime && transition.Conditions.Count == 0) continue;
                double overshoot = 0;
                if (transition.HasExitTime)
                {
                    var at = Crossing(transition.ExitTime, n0, n1);
                    if (!(at <= n1)) continue;
                    // M2: the part of the frame after the crossing already runs the transition.
                    overshoot = n1 > n0 ? (n1 - at) / (n1 - n0) * dt : 0;
                }
                if (!Pass(transition.Conditions)) continue;
                chainCount = 0;
                chain[chainCount++] = transition;
                var machine = layer.Layer.States[layer.State].Machine;
                var destination = Resolve(layer.Layer, transition, fromAny ? 0 : machine, 0);
                if (destination < 0) continue;
                // Mid-transition, "self" is the source of the running transition.
                if (fromAny && !transition.CanTransitionToSelf && destination == layer.State) continue;
                Fire(layer, transition, destination, fromAny, sourceLength, overshoot);
                return true;
            }
            return false;
        }

        // M2: the first normalized time after n0 at which exit time e is
        // reached: e itself for e >= 1, the next k + e for e < 1 (every loop).
        // The starting time does not count, so e = 0 waits for the end of the
        // first loop. NaN when e >= 1 is already behind.
        static double Crossing(double exitTime, double n0, double n1)
        {
            if (exitTime >= 1) return n0 < exitTime ? exitTime : double.NaN;
            return Math.Max(Math.Floor(n0 - exitTime) + 1, 0) + exitTime;
        }

        // The state a transition ends in, following Entry and Exit, or -1 when
        // it leads nowhere. owner is the machine the transition leaves from.
        int Resolve(AvatarAnimatorData.Layer layer, AvatarAnimatorData.Transition transition, int owner, int depth)
        {
            if (transition.ToState.HasValue) return transition.ToState.Value;
            if (transition.ToMachine.HasValue) return ResolveEntry(layer, transition.ToMachine.Value, depth + 1);
            return ResolveExit(layer, owner, depth + 1);
        }

        int ResolveEntry(AvatarAnimatorData.Layer layer, int machine, int depth)
        {
            if (depth >= chain.Length - 1) return -1;
            var m = layer.Machines[machine];
            foreach (var entry in m.EntryTransitions)
                if (Pass(entry.Conditions))
                {
                    chain[chainCount++] = entry;
                    return Resolve(layer, entry, machine, depth);
                }
            return m.DefaultState;
        }

        // Leaving machine: the root goes back through its Entry; another
        // machine takes its parent's first transition out of it that passes,
        // else its parent's Entry.
        int ResolveExit(AvatarAnimatorData.Layer layer, int machine, int depth)
        {
            if (depth >= chain.Length - 1) return -1;
            if (machine == 0) return ResolveEntry(layer, 0, depth);
            var m = layer.Machines[machine];
            foreach (var exit in m.ExitTransitions)
                if (Pass(exit.Conditions))
                {
                    chain[chainCount++] = exit;
                    return Resolve(layer, exit, m.Parent, depth);
                }
            return ResolveEntry(layer, m.Parent, depth);
        }

        void Fire(LayerRun layer, AvatarAnimatorData.Transition transition, int destination, bool fromAny,
            double sourceLength, double overshoot)
        {
            var duration = transition.HasFixedDuration ? transition.Duration : transition.Duration * sourceLength;
            // A layer that enters its own state again at once, each frame (an Any
            // State transition that may go to itself, say), repeats the frame
            // exactly; Settles decides whether the frames to come are the same.
            if (!layer.Transitioning && destination == layer.State && !transition.HasExitTime && duration <= 0 && layer.Repeated == null)
                layer.Repeated = transition;
            else moved = true;
            // M4: the triggers the transitions passed are used up at the end of the frame.
            for (var c = 0; c < chainCount; c++)
                foreach (var condition in chain[c].Conditions)
                    if (types[condition.Parameter] == TriggerType) spent[condition.Parameter] = true;
            // M5: a transition that starts itself again does not run its destination's
            // behaviours again, so they run once as before (not measured in Unity).
            var restart = layer.Transitioning && ReferenceEquals(transition, layer.Active) && destination == layer.Next;
            if (layer.Transitioning)
            {
                // M5: the interrupted blend continues as a still copy.
                Snap(layer);
                layer.Frozen = true;
                layer.InterruptSerial++;
            }
            layer.Transitioning = true;
            layer.Active = transition;
            layer.ActiveFromAny = fromAny;
            layer.Next = destination;
            layer.NextTime = transition.Offset;
            layer.Progress = 0;
            layer.Duration = duration;
            if (restart)
            {
                layer.EnterSerial++;
                layer.EnteredState = -1;
            }
            else Enter(layer, destination);
            if (overshoot > 0)
            {
                var states = layer.Layer.States;
                layer.NextTime = AdvanceTime(states[destination], layer.NextTime, overshoot, StateLength(states[destination]));
                if (layer.Duration > 0) layer.Progress = overshoot / layer.Duration;
            }
            // M3: a transition of no length ends now; the next waits a frame.
            if (layer.Duration <= 0 || layer.Progress >= 1) Complete(layer);
        }

        // A layer that passed keeps no Out, so rebuild it before copying.
        void Snap(LayerRun layer)
        {
            if (layer.Passed >= 0) Rebuild(layer);
            Array.Copy(layer.Out, layer.Snapshot, layer.Out.Length);
            Array.Copy(layer.OutWritten, layer.SnapshotWritten, layer.OutWritten.Length);
        }

        static void Complete(LayerRun layer)
        {
            layer.State = layer.Next;
            layer.Time = layer.NextTime;
            layer.Next = -1;
            layer.Transitioning = false;
            layer.Frozen = false;
            layer.Active = null;
        }

        // A synced layer moves with its source; the behaviours of its copied
        // machines and of its own states run when the source enters them. A
        // source later in the list is a frame ahead of it.
        void Sync(LayerRun layer)
        {
            var source = layers[layer.Source];
            // A source later in the list moved in the last frame.
            if (source.InterruptSerial != layer.SeenInterruptSerial || source.EnterSerial != layer.SeenEnterSerial || source.State != layer.State
                || source.Next != layer.Next || source.Transitioning != layer.Transitioning || source.Frozen != layer.Frozen)
                moved = true;
            if (source.InterruptSerial != layer.SeenInterruptSerial)
            {
                layer.SeenInterruptSerial = source.InterruptSerial;
                Snap(layer);
            }
            layer.State = source.State;
            layer.Next = source.Next;
            layer.Time = source.Time;
            layer.NextTime = source.NextTime;
            layer.Progress = source.Progress;
            layer.Transitioning = source.Transitioning;
            layer.Frozen = source.Frozen;
            if (source.EnterSerial != layer.SeenEnterSerial)
            {
                layer.SeenEnterSerial = source.EnterSerial;
                if (source.EnteredState >= 0) RunBehaviours(layer.Layer, source.EnteredState);
            }
        }

        void Enter(LayerRun layer, int state)
        {
            layer.EnterSerial++;
            layer.EnteredState = state;
            RunBehaviours(layer.Layer, state);
        }

        // M8: the behaviours of the machines holding the state, outer first,
        // then the state's own.
        void RunBehaviours(AvatarAnimatorData.Layer layer, int state)
        {
            var s = layer.States[state];
            RunMachineBehaviours(layer, s.Machine);
            foreach (var behaviour in s.Behaviours) Run(behaviour);
        }

        void RunMachineBehaviours(AvatarAnimatorData.Layer layer, int machine)
        {
            if (machine < 0) return;
            var m = layer.Machines[machine];
            // Parents come before their children, so this ends.
            if (m.Parent < machine) RunMachineBehaviours(layer, m.Parent);
            foreach (var behaviour in m.Behaviours) Run(behaviour);
        }

        void Run(AvatarAnimatorData.Behaviour behaviour)
        {
            switch (behaviour.Type)
            {
                case "parameterDriver":
                    drivers.Add(behaviour);
                    break;
                case "layerControl":
                    var controller = Controller(behaviour.Playable);
                    // A controller's base layer stays at 1, as with SetLayerWeight.
                    if (controller >= 0 && behaviour.Layer > 0 && behaviour.Layer < controllerEnd[controller] - controllerStart[controller])
                    {
                        var target = layers[controllerStart[controller] + behaviour.Layer];
                        // M10: a 0 s blend takes the goal at once.
                        target.WeightBlending = behaviour.BlendDuration > 0;
                        target.WeightFrom = target.Weight;
                        target.WeightGoal = behaviour.GoalWeight;
                        target.WeightDuration = behaviour.BlendDuration;
                        target.WeightElapsed = 0;
                        if (!target.WeightBlending)
                        {
                            // A layer that has already stepped this frame plays the new weight from the next.
                            if (target.Weight != behaviour.GoalWeight) moved = true;
                            target.Weight = behaviour.GoalWeight;
                        }
                    }
                    break;
                case "playableLayerControl":
                    // The weight of a skipped controller moves too, unseen.
                    var playable = -1;
                    for (var c = 0; c < data.Controllers.Count; c++)
                        if (data.Controllers[c].Playable == behaviour.Playable) playable = c;
                    if (playable >= 0)
                    {
                        playableBlending[playable] = behaviour.BlendDuration > 0;
                        playableFrom[playable] = playableWeight[playable];
                        playableGoal[playable] = behaviour.GoalWeight;
                        playableDuration[playable] = behaviour.BlendDuration;
                        playableElapsed[playable] = 0;
                        if (!playableBlending[playable])
                        {
                            // A controller that has already played this frame plays the new weight from the next.
                            if (playableWeight[playable] != behaviour.GoalWeight) moved = true;
                            playableWeight[playable] = behaviour.GoalWeight;
                        }
                    }
                    break;
                // trackingControl moves nothing the targets show.
            }
        }

        // The index in Data.Controllers of the playing controller of the playable, or -1.
        int Controller(string playable)
        {
            foreach (var c in played)
                if (data.Controllers[c].Playable == playable) return c;
            return -1;
        }

        // M15: one controller's layers, each over the ones below it in the
        // controller (M6, M7), and then the controller over what the
        // controllers below it left, by its playable weight: a target it
        // writes takes its value, blended at a weight under 1 from what
        // the controllers below left, or from the rest when none wrote it;
        // a target it does not write keeps theirs. Its own layers do not see
        // the controllers below: there, a target none of its layers wrote
        // is the rest, or the last frame's value (M6).
        void Play(int c, double dt)
        {
            // A single controller has nothing below: it plays over the rest as before.
            var stacked = below != null;
            isAnimated = controllerAnimated[c];
            if (stacked)
            {
                // Its layers start from the last frame's values, as a single
                // controller does, not from what the controllers below left.
                Array.Copy(values, below, values.Length);
                Array.Copy(previous, values, values.Length);
                Array.Copy(written, belowWritten, written.Length);
                Array.Clear(written, 0, written.Length);
            }
            for (var l = controllerStart[c]; l < controllerEnd[c]; l++)
            {
                var layer = layers[l];
                if (layer.Source >= 0) Sync(layer);
                else Step(layer, dt);
                Output(layer);
            }
            if (animatedWritten)
            {
                animatedWritten = false;
                for (var t = 0; t < written.Length; t++) written[t] |= isAnimated[t];
            }
            var weight = Math.Max(0, playableWeight[c]);
            // What lies under the controller is left by 1 - weight where it writes.
            var left = weight < 1 ? 1 - weight : 0;
            var pose = controllerPose[c];
            if (!stacked)
            {
                for (var t = 0; t < values.Length; t++)
                    if (written[t])
                    {
                        if (weight < 1) values[t] = Blend(t, rest[t], values[t], weight);
                        hostShare[t] = left;
                    }
                    // Alone it plays over the rest as before, but over the host's pose its muscles
                    // cover the host's even where no clip moves them (M16): the rest under its weight.
                    else if (pose != null && pose[t]) hostShare[t] = left;
                return;
            }
            for (var t = 0; t < values.Length; t++)
            {
                if (!written[t] && pose != null && pose[t])
                {
                    values[t] = rest[t];
                    written[t] = true;
                }
                if (written[t])
                {
                    if (weight < 1) values[t] = Blend(t, belowWritten[t] ? below[t] : rest[t], values[t], weight);
                    hostShare[t] *= left;
                }
                else
                {
                    values[t] = below[t];
                    written[t] = belowWritten[t];
                }
            }
        }

        // VRChat's Avatar Parameter Driver.
        void Drive(AvatarAnimatorData.Operation operation)
        {
            var parameter = operation.Parameter;
            var type = types[parameter];
            double value;
            switch (operation.Type)
            {
                case "set":
                    value = operation.Value;
                    break;
                case "add":
                    value = parameters[parameter] + operation.Value;
                    break;
                case "random":
                    if (type == BoolType || type == TriggerType)
                        value = random() < operation.Chance ? 1 : 0;
                    else if (type == IntType)
                    {
                        var low = (int)operation.ValueMin;
                        var high = (int)operation.ValueMax;
                        value = high < low ? low : low + Math.Min(high - low, (int)Math.Floor(random() * (high - low + 1.0)));
                    }
                    else
                        value = operation.ValueMin + random() * (operation.ValueMax - operation.ValueMin);
                    break;
                default: // copy
                    value = parameters[operation.Source.Value];
                    if (operation.ConvertRange)
                    {
                        var span = operation.SourceMax - operation.SourceMin;
                        var t = span != 0 ? Math.Max(0, Math.Min(1, (value - operation.SourceMin) / span)) : 0;
                        value = operation.DestMin + (operation.DestMax - operation.DestMin) * t;
                    }
                    break;
            }
            // M11: a parameter a clip animates does not change.
            if (!double.IsNaN(value) && !double.IsInfinity(value) && parameterTargets[parameter] < 0) parameters[parameter] = Convert(type, value);
        }

        void BlendWeight(LayerRun layer, double dt)
        {
            if (!layer.WeightBlending) return;
            moved = true;
            layer.WeightElapsed += dt;
            var t = layer.WeightDuration > 0 ? Math.Min(1, layer.WeightElapsed / layer.WeightDuration) : 1;
            layer.Weight = layer.WeightFrom + (layer.WeightGoal - layer.WeightFrom) * t;
            if (t >= 1) layer.WeightBlending = false;
        }

        bool Pass(List<AvatarAnimatorData.Condition> conditions)
        {
            foreach (var condition in conditions)
            {
                var value = parameters[condition.Parameter];
                var threshold = (double)(float)condition.Threshold;
                bool pass;
                switch (condition.Mode)
                {
                    case "if": pass = value != 0; break;
                    case "ifNot": pass = value == 0; break;
                    case "greater": pass = value > threshold; break;
                    case "less": pass = value < threshold; break;
                    case "equals": pass = value == Math.Truncate(threshold); break;
                    default: pass = value != Math.Truncate(threshold); break;
                }
                if (!pass) return false;
            }
            return true;
        }

        double ParameterOr(int? parameter, double fallback) => parameter.HasValue ? parameters[parameter.Value] : fallback;

        double AdvanceTime(AvatarAnimatorData.State state, double time, double dt, double length)
        {
            if (state.TimeParameter.HasValue) return parameters[state.TimeParameter.Value];
            var speed = state.Speed * ParameterOr(state.SpeedParameter, 1);
            return (float)((float)time + (float)(dt * speed / length));
        }

        // Seconds per normalized time unit at the parameters' current values.
        double StateLength(AvatarAnimatorData.State state) => state.Motion < 0 ? EmptyLength : MotionLength(state.Motion);

        double MotionLength(int motion)
        {
            var m = data.Motions[motion];
            if (m.Type == "clip")
            {
                var duration = data.Clips[m.Clip].Duration;
                return duration > 0 ? duration : EmptyLength;
            }
            var weights = Weights(motion);
            double sum = 0, total = 0;
            for (var c = 0; c < weights.Length; c++)
            {
                var w = weights[c];
                if (w == 0) continue;
                var child = m.Children[c].Motion;
                var length = child < 0 ? EmptyLength : MotionLength(child);
                var scale = m.Children[c].TimeScale;
                sum += Math.Abs(w) * (scale != 0 ? length / Math.Abs(scale) : length);
                total += Math.Abs(w);
            }
            // Weights of motion are rewritten by the recursion above only for
            // other motions, so they still hold this tree's.
            return total > 0 ? sum / total : EmptyLength;
        }

        // A tree's children's weights at the current parameter values.
        double[] Weights(int motion)
        {
            var m = data.Motions[motion];
            var weights = childWeights[motion];
            Array.Clear(weights, 0, weights.Length);
            if (weights.Length == 0) return weights;
            if (m.BlendType == "direct")
            {
                double sum = 0;
                // Unity reads a negative direct weight as 0.
                for (var c = 0; c < weights.Length; c++) sum += weights[c] = Math.Max(0, parameters[m.Children[c].DirectParameter.Value]);
                if (m.NormalizedBlendValues && sum > 0)
                    for (var c = 0; c < weights.Length; c++) weights[c] /= sum;
                return weights;
            }
            var children = m.Children;
            if (m.BlendType == "2dFreeformCartesian")
            {
                // M12.
                double x = parameters[m.Parameters[0]], y = parameters[m.Parameters[1]], sum = 0;
                for (var i = 0; i < weights.Length; i++)
                {
                    if (children[i].Motion < 0) continue;
                    double w = 1, fromX = x - children[i].X, fromY = y - children[i].Y;
                    for (var j = 0; j < weights.Length && w > 0; j++)
                    {
                        if (j == i || children[j].Motion < 0) continue;
                        double toX = children[j].X - children[i].X, toY = children[j].Y - children[i].Y, distance = toX * toX + toY * toY;
                        if (distance > 0) w = Math.Min(w, 1 - (fromX * toX + fromY * toY) / distance);
                    }
                    sum += weights[i] = Math.Max(0, w);
                }
                if (sum > 0)
                    for (var c = 0; c < weights.Length; c++) weights[c] /= sum;
                return weights;
            }
            if (m.BlendType == "2dSimpleDirectional")
            {
                // M13.
                double x = parameters[m.Parameters[0]], y = parameters[m.Parameters[1]], centre = 1;
                var directions = childOrder[motion];
                if (centres[motion])
                {
                    if (x == 0 && y == 0) weights[centreChild[motion]] = 1;
                    return weights;
                }
                if ((x != 0 || y != 0) && directions.Length >= 2)
                    for (var k = 0; k < directions.Length; k++)
                    {
                        int a = directions[k], b = directions[(k + 1) % directions.Length];
                        double ax = children[a].X, ay = children[a].Y, bx = children[b].X, by = children[b].Y;
                        double det = ax * by - ay * bx, fromA = ax * y - ay * x, toB = x * by - y * bx;
                        // Neighbours 180 degrees or more apart leave the point to the centre.
                        if (det <= 0 || fromA <= 0 || toB < 0) continue;
                        double wa = toB / det, wb = fromA / det, sum = wa + wb;
                        // Past the line between the two, the centre has no weight.
                        if (sum > 1) { wa /= sum; wb /= sum; sum = 1; }
                        weights[a] = wa;
                        weights[b] = wb;
                        centre = 1 - sum;
                        break;
                    }
                if (centreChild[motion] >= 0) weights[centreChild[motion]] += centre;
                else
                    foreach (var d in directions) weights[d] += centre / directions.Length;
                return weights;
            }
            if (m.BlendType == "2dFreeformDirectional")
            {
                // M14.
                double x = parameters[m.Parameters[0]], y = parameters[m.Parameters[1]], sum = 0;
                var length = Math.Sqrt(x * x + y * y);
                int playing = 0;
                var centre = false;
                for (var i = 0; i < weights.Length; i++)
                {
                    if (children[i].Motion < 0) continue;
                    playing++;
                    double xi = children[i].X, yi = children[i].Y, li = Math.Sqrt(xi * xi + yi * yi);
                    centre |= li == 0;
                    // A child away from the centre starts from its angle to the point: 1 in its direction, 0 opposite.
                    var w = li > 0 && length > 0 ? 1 - Math.Abs(Angle(xi, yi, x, y)) / Math.PI : 1;
                    for (var j = 0; j < weights.Length && w > 0; j++)
                    {
                        if (j == i || children[j].Motion < 0 || (children[j].X == xi && children[j].Y == yi)) continue;
                        double xj = children[j].X, yj = children[j].Y, lj = Math.Sqrt(xj * xj + yj * yj), mean = (li + lj) / 2;
                        double between, toPoint;
                        if (li == 0 || lj == 0)
                        {
                            // A child at the centre has no direction: the angle is the other child's to the point.
                            var away = li == 0 ? j : i;
                            between = length > 0 ? Math.Abs(Angle(children[away].X, children[away].Y, x, y)) : 0;
                            toPoint = li == 0 ? 0 : between;
                        }
                        else
                        {
                            between = Angle(xi, yi, xj, yj);
                            toPoint = length > 0 ? Angle(xi, yi, x, y) : 0;
                        }
                        double radial = (lj - li) / mean, distance = radial * radial + between * between;
                        w = Math.Min(w, 1 - ((length - li) / mean * radial + toPoint * between) / distance);
                    }
                    sum += weights[i] = Math.Max(0, w);
                }
                // With no weight anywhere, or at (0, 0) without a child there, the children share equally.
                if (sum == 0 || (length == 0 && !centre))
                    for (var c = 0; c < weights.Length; c++) weights[c] = children[c].Motion < 0 ? 0 : 1.0 / playing;
                else
                    for (var c = 0; c < weights.Length; c++) weights[c] /= sum;
                return weights;
            }
            // 1D: between the two neighbouring thresholds, clamped at the ends.
            var p = parameters[m.Parameters[0]];
            var order = childOrder[motion];
            if (p <= children[order[0]].Threshold) { weights[order[0]] = 1; return weights; }
            var last = order[order.Length - 1];
            if (p >= children[last].Threshold) { weights[last] = 1; return weights; }
            for (var i = 0; i + 1 < order.Length; i++)
            {
                double low = children[order[i]].Threshold, high = children[order[i + 1]].Threshold;
                if (p >= low && p < high)
                {
                    var t = (p - low) / (high - low);
                    weights[order[i]] = 1 - t;
                    weights[order[i + 1]] = t;
                    break;
                }
            }
            return weights;
        }

        // The signed angle from a to b, in radians.
        static double Angle(double ax, double ay, double bx, double by) => Math.Atan2(ax * by - ay * bx, ax * bx + ay * by);

        // A 2D tree's children with a motion away from (0, 0), by angle, a
        // child in the direction of an earlier one left out; and its child
        // with a motion at (0, 0) (-1 none) and whether it has more than one.
        static void Directions(AvatarAnimatorData.Motion m, out int[] directions, out int centre, out bool centres)
        {
            var children = m.Children;
            var kept = new List<int>();
            centre = -1;
            centres = false;
            for (var c = 0; c < children.Count; c++)
            {
                if (children[c].Motion < 0) continue;
                double x = children[c].X, y = children[c].Y;
                if (x == 0 && y == 0)
                {
                    if (centre == -1) centre = c;
                    else centres = true;
                    continue;
                }
                var same = false;
                foreach (var k in kept) same |= children[k].X * y - children[k].Y * x == 0 && children[k].X * x + children[k].Y * y > 0;
                if (!same) kept.Add(c);
            }
            // Stable: children at one angle cannot both be kept.
            kept.Sort((a, b) => Math.Atan2(children[a].Y, children[a].X).CompareTo(Math.Atan2(children[b].Y, children[b].X)));
            directions = kept.ToArray();
        }

        // The state's value of each target its motion writes: the children's
        // values weighted, a child that does not write a target giving the
        // default (Rest with Write Defaults, else the previous value).
        void Evaluate(Side side, LayerRun layer, int state, double time)
        {
            var s = layer.Layer.States[state];
            var targets = stateTargets[layer.Index][state];
            side.Current++;
            side.Total = 0;
            side.Muscles = false;
            foreach (var t in targets)
            {
                side.Accumulated[t] = 0;
                side.AccumulatedWeight[t] = 0;
                side.Best[t] = 0;
                side.Stamp[t] = side.Current;
            }
            if (s.Motion < 0) return;
            var normalized = time + ParameterOr(s.CycleOffsetParameter, s.CycleOffset);
            Accumulate(side, s.Motion, 1, normalized);
            // A motion with no weight at all writes nothing, as a state without a motion.
            if (side.Total <= 0)
            {
                side.Current++;
                side.Muscles = false;
                return;
            }
            foreach (var t in targets)
            {
                var fallback = s.WriteDefaults || (isMuscle[t] && side.Muscles) ? rest[t] : previous[t];
                // The motions that do not write a stepped target give the
                // fallback with their weight together.
                side.Value[t] = stepped[t] ? (side.Best[t] >= side.Total - side.AccumulatedWeight[t] ? side.Accumulated[t] : fallback)
                    : side.Accumulated[t] + (side.Total - side.AccumulatedWeight[t]) * fallback;
            }
        }

        void Accumulate(Side side, int motion, double weight, double normalized)
        {
            var m = data.Motions[motion];
            if (m.Type == "clip")
            {
                side.Total += weight;
                var clip = data.Clips[m.Clip];
                var local = normalized + (clip.Loop ? clip.CycleOffset : 0);
                var time = clip.Duration <= 0 ? 0 : (clip.Loop ? local - Math.Floor(local) : Math.Max(0, Math.Min(1, local))) * clip.Duration;
                foreach (var track in clip.Tracks)
                {
                    var t = track.Target;
                    side.AccumulatedWeight[t] += weight;
                    side.Muscles |= isMuscle[t];
                    if (!stepped[t]) side.Accumulated[t] += weight * track.Curves[0].Evaluate(time);
                    // The first of equal weights stays.
                    else if (weight > side.Best[t])
                    {
                        side.Best[t] = weight;
                        side.Accumulated[t] = track.Curves[0].Evaluate(time);
                    }
                }
                return;
            }
            var weights = Weights(motion);
            for (var c = 0; c < weights.Length; c++)
            {
                var w = weights[c];
                if (w == 0) continue;
                var child = m.Children[c];
                // Recursion rewrites only other trees' weights (no tree holds itself).
                // Children share the tree's normalized time; a time scale only
                // changes the tree's length (MotionLength).
                if (child.Motion < 0) side.Total += weight * w;
                else Accumulate(side, child.Motion, weight * w, normalized + child.CycleOffset);
            }
        }

        // M6 and M7 decide what a side or a layer that does not write a target leaves.
        void Output(LayerRun layer)
        {
            var animated = layer.Animated;
            layer.Mixed = false;
            if (layer.State < 0)
            {
                Array.Clear(layer.OutWritten, 0, layer.OutWritten.Length);
                return;
            }
            if (!layer.Frozen) Evaluate(sideA, layer, layer.State, layer.Time);
            if (layer.Transitioning) Evaluate(sideB, layer, layer.Next, layer.NextTime);
            var weight = layer.First ? 1 : layer.Weight;
            var states = layer.Layer.States;
            // A layer resting in a motion state without Write Defaults at full weight passes
            // every target its motion does not write through unchanged: the value stays what
            // the layers below leave, the target counts as written and the layer's own value
            // of it is that value. A controller of many such layers (one per menu toggle) would
            // visit layers × targets every frame (#3664), so such a layer only visits its
            // motion's targets, marks the rest written at once, and rebuilds its own values
            // from the last frame's log in the first frame it stops passing.
            var passes = !layer.Transitioning && !layer.Frozen && weight >= 1 && layer.AnimatesAll
                && states[layer.State].Motion >= 0 && !states[layer.State].WriteDefaults && !layer.StateMuscles[layer.State];
            if (layer.Passed >= 0 && !passes) Rebuild(layer);
            if (passes)
            {
                // Such a state is without Write Defaults and no mask leaves a target out,
                // so Animated holds every target the controller animates.
                animatedWritten = true;
                if (layer.Passed != layer.State) moved = true;
                layer.Passed = layer.State;
                foreach (var k in layer.StateKeys[layer.State]) OutputTarget(layer, k, weight);
                return;
            }
            var serial = layer.Source >= 0 ? layer.SeenEnterSerial : layer.EnterSerial;
            if (layer.Transitioning && layer.HeldSerial != serial)
            {
                layer.HeldSerial = serial;
                for (var k = 0; k < animated.Length; k++) layer.Held[k] = layer.OutWritten[k] ? layer.Out[k] : previous[animated[k]];
            }
            // M7: a layer resting at weight 0 forgets its own values, as one that never
            // wrote. Mid-transition they stay, as an interruption copies them (not measured
            // in Unity). Forgetting values kept from an earlier frame changes what the next
            // frame writes, so that frame is evaluated (#3664).
            var forgets = weight <= 0 && !layer.Transitioning;
            if (forgets && Array.IndexOf(layer.OutWritten, true) >= 0) moved = true;
            for (var k = 0; k < animated.Length; k++) OutputTarget(layer, k, weight);
            if (forgets) Array.Clear(layer.OutWritten, 0, layer.OutWritten.Length);
        }

        bool Wrote(int t) => written[t] || (animatedWritten && isAnimated[t]);

        // A passing layer's own value of a target its motion did not write, as
        // of the last frame: what the layers below it in its controller left
        // then (the value at the start of that frame when none wrote it).
        void Rebuild(LayerRun layer)
        {
            var animated = layer.Animated;
            var own = layer.StateKeys[layer.Passed];
            layer.Passed = -1;
            moved = true;
            for (int k = 0, j = 0; k < animated.Length; k++)
            {
                // Keys are in ascending order, as Animated is.
                if (j < own.Length && own[j] == k) { j++; continue; }
                var t = animated[k];
                var e = lastHead[t];
                while (e >= 0 && (lastLayer[e] >= layer.Index || layers[lastLayer[e]].Controller != layer.Controller)) e = lastEarlier[e];
                var kept = e >= 0 ? lastValue[e] : lastPrevious[t];
                layer.Out[k] = kept;
                layer.OutWritten[k] = true;
                if (isParameter[t]) layer.Own[k] = kept;
            }
        }

        void OutputTarget(LayerRun layer, int k, double weight)
        {
            var states = layer.Layer.States;
            var t = layer.Animated[k];
            // What the layers below leave: this frame's value, else the last frame's.
            var through = Wrote(t) ? values[t] : previous[t];
            // Mid-transition the layer holds a target either side's motion writes,
            // and every muscle when either side's motion writes a muscle (M6: the
            // layer is then a pose; a frozen source's muscles are not looked at).
            var bound = layer.Transitioning && layer.Progress > 0 && (sideB.Stamp[t] == sideB.Current
                || (layer.Frozen ? layer.SnapshotWritten[k] : sideA.Stamp[t] == sideA.Current)
                || (isMuscle[t] && (sideB.Muscles || (!layer.Frozen && sideA.Muscles))));
            double value, b = 0;
            bool has;
            if (layer.Frozen)
            {
                value = layer.Snapshot[k];
                has = layer.SnapshotWritten[k];
                if (!has) value = through;
            }
            else has = SideValue(sideA, layer, layer.State, t, k, through, bound, out value);
            if (layer.Transitioning)
            {
                var hasB = SideValue(sideB, layer, layer.Next, t, k, through, bound, out b);
                // M6: an empty source without Write Defaults holds the layer's
                // values from when the transition started.
                if (!layer.Frozen && states[layer.State].Motion < 0 && !states[layer.State].WriteDefaults) value = layer.Held[k];
                has |= hasB;
                // M11: a parameter's own value nears the rest by the progress, below.
                if (value != b || (isParameter[t] && b != rest[t] && states[layer.Next].Motion < 0 && !states[layer.Next].WriteDefaults))
                    layer.Mixed = true;
                var progress = Math.Min(1, layer.Progress);
                // Unity takes an object reference only from the motions that
                // write it, the rest of Write Defaults not counting: when one
                // side's motion alone writes a stepped target and the other
                // side is a Write Defaults motion, the writing side shows while
                // it has any weight (measured in
                // AvatarAnimatorCrossCheckTests.TransformAndMaterialSlotMatchUnity;
                // an empty state or one without Write Defaults is not measured).
                var writesA = Writes(sideA, layer, layer.State, t);
                var writesB = Writes(sideB, layer, layer.Next, t);
                if (stepped[t] && !layer.Frozen && writesA != writesB && RestsByDefault(layer, writesA ? layer.Next : layer.State))
                    value = writesB ? (progress > 0 ? b : value) : (progress < 1 ? value : b);
                else value = Blend(t, value, b, progress);
            }
            if (has || layer.Transitioning)
            {
                layer.Out[k] = value;
                layer.OutWritten[k] = has;
            }
            // M11: moving into an empty state without Write Defaults, a
            // parameter's own value takes the rest where the source had it:
            // it starts from the layer's last value and nears the rest.
            if (isParameter[t])
                layer.Own[k] = layer.Transitioning && layer.Progress > 0 && states[layer.Next].Motion < 0 && !states[layer.Next].WriteDefaults
                    ? rest[t] + (b - rest[t]) * Math.Min(1, layer.Progress) : layer.Out[k];
            if (!has) return;
            // M7: over the layers below, or over the rest when none of them wrote it.
            var below = Wrote(t) ? values[t] : rest[t];
            values[t] = weight >= 1 ? value : Blend(t, below, value, Math.Max(0, weight));
            written[t] = true;
            logLayer[logCount] = layer.Index;
            logValue[logCount] = values[t];
            logEarlier[logCount] = logHead[t];
            logHead[t] = logCount++;
        }

        static bool Writes(Side side, LayerRun layer, int state, int t) => layer.Layer.States[state].Motion >= 0 && side.Stamp[t] == side.Current;

        static bool RestsByDefault(LayerRun layer, int state) => layer.Layer.States[state].Motion >= 0 && layer.Layer.States[state].WriteDefaults;

        // From a toward b by weight w; a stepped target takes b only past
        // half, where b has more weight than a (measured in Unity for a
        // transition whose two sides both write it, not for a layer weight).
        double Blend(int t, double a, double b, double w) => stepped[t] ? (w > 0.5 ? b : a) : a + (b - a) * w;

        // M6: one side's value of a target its motion does not write. With
        // Write Defaults: what the layers below leave, or the rest when none
        // wrote it. Without, a motion passes the layers below through, and a
        // state without a motion gives the layer's own last value: a layer
        // resting there keeps applying it, one moving there blends toward it
        // frame by frame. A muscle differs (measured in the hands cases): a
        // motion that writes other muscles gives the rest, one side of a
        // transition where either side writes a muscle gives the rest, and a layer
        // that never wrote it has the rest as its own last value.
        bool SideValue(Side side, LayerRun layer, int state, int t, int k, double through, bool bound, out double value)
        {
            var s = layer.Layer.States[state];
            if (s.Motion >= 0 && side.Stamp[t] == side.Current)
            {
                value = side.Value[t];
                return true;
            }
            if (s.Motion >= 0)
            {
                // A Write Defaults motion with no weight at all, or one side of a
                // transition whose other side writes the target (a muscle with or
                // without Write Defaults, and when either side writes any muscle),
                // gives the rest, and so does a motion
                // writing other muscles for a muscle; otherwise the layers
                // below keep the target, and with Write Defaults it is the rest
                // when none of them wrote it.
                if ((s.WriteDefaults && side.Total <= 0) || (isMuscle[t] && side.Muscles) || ((s.WriteDefaults || isMuscle[t]) && bound))
                {
                    value = rest[t];
                    return true;
                }
                value = s.WriteDefaults && !Wrote(t) ? rest[t] : through;
                return true;
            }
            if (!s.WriteDefaults)
            {
                // A layer that never wrote the target has no value of its own,
                // except a muscle: its own value starts at the rest.
                if (!layer.OutWritten[k] && isMuscle[t])
                {
                    value = rest[t];
                    return true;
                }
                value = layer.OutWritten[k] ? (isParameter[t] ? layer.Own[k] : layer.Out[k]) : through;
                return layer.OutWritten[k];
            }
            // Write Defaults: the layers below, or the rest when none wrote it.
            value = Wrote(t) ? values[t] : rest[t];
            return true;
        }
    }
}
