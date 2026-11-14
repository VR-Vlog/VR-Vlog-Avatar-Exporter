using System;
using System.Collections.Generic;

// Plays VRVLOG_avatar_animator (AvatarAnimatorData.cs) the way Unity's Animator
// plays the FX controller. No Unity/Editor dependency: the app drives it from a
// MonoBehaviour and the exporter's EditMode tests compare it with Unity 2022.3's
// Animator. Advance allocates nothing.
namespace VRVlog.AvatarAnimator
{
    // One instance per loaded avatar. The host sets parameters (Set), calls
    // Advance once per frame and writes Values to the targets. Values holds
    // every target's value after the last Advance: a target no layer wrote
    // keeps its previous value, starting from its Rest, except that a
    // parameter target goes back to its Rest (M11). For a morph target
    // that is the Unity BlendShape weight, not the glTF weight.
    //
    // The marks name rules the README explains ("Avatar animator evaluator").
    // M1-M7 and M11 are measured: Tests/AnimatorComparison records Unity's Animator
    // and Tools/Fixtures/AvatarAnimator replays the recordings here.
    // M1 an animated activeSelf is on at any value other than 0; M2 an exit
    // time e is crossed when the state's normalized time passes e (e >= 1
    // once, e < 1 at each loop after the start), and the rest of the frame
    // already advances the transition and its destination; M3 a layer whose
    // transition ended this frame starts none until the next; M4 a trigger
    // used by a transition is reset once every layer has stepped; M5 Any
    // State transitions are checked only while the layer is not
    // transitioning, and an interrupted transition blends from a still copy;
    // M6 for a target a state's motion does not write: mid-transition, when
    // the other side's motion writes it, a Write Defaults side gives the
    // rest; otherwise a Write Defaults state leaves it to the layers below
    // (the rest when none wrote it), and a state without Write Defaults
    // gives what the layers below leave (the last frame's value when none
    // wrote it) and applies it with the layer's weight. A state without a motion gives, with Write Defaults,
    // the layers below (the rest when none wrote it); without, the layer's
    // own last value, held from the start of a transition out of it and
    // approached frame by frame in a transition into it. A layer with a
    // state without Write Defaults keeps a value of every target the
    // controller animates; M7 a layer under weight 1 blends over the
    // layers below it, or over the rest when none wrote the target.
    // M11 a float parameter a clip animates is a target that M6 and M7
    // blend as a node, with one difference: in a transition into a state
    // without a motion or Write Defaults, the layer's own value of it starts
    // from the layer's last value and takes the rest where the source side
    // had it, instead of the last frame's value. After each frame the
    // parameter is what the layers wrote, or the controller's default when
    // none did; an int, bool or trigger with a curve stays at that default;
    // neither the host nor a Parameter Driver changes such a parameter, and
    // transitions and trees see the value of the frame before.
    // M8-M10 stay assumptions: M8 and M10 are VRChat behaviours that plain
    // Unity cannot record, and no recording covers M9. M8 the behaviours of a state run when the transition into
    // it starts, the machines' (outer first) before the state's, and their
    // parameter changes are seen by later layers in the same frame; M9 a
    // state without a motion, or with a clip of length 0, is 1 second long;
    // M10 a layer or FX weight blend moves from the frame after the behaviour
    // that starts it, and a blend of 0 seconds applies at once.
    public sealed class AvatarAnimatorEvaluator
    {
        // M9.
        public const double EmptyLength = 1;

        // Unity turns an animated activeSelf on at any value other than 0, negative too.
        public static bool Active(double value) => value != 0;

        // The omitted kinds that change how the controller moves: playing
        // without them would take other transitions than Unity.
        static readonly string[] TimingKinds = { "parameter", "operation", "unreadParameter" };

        // Whether this evaluator plays the controller as Unity would. False
        // when the exporter left out something that drives transitions, or the
        // controller uses what is not implemented yet (additive layers, synced
        // layers that affect timing, 2D blend trees, a random that prevents
        // repeats). reason is null when true.
        public static bool Supports(AvatarAnimatorData data, out string reason)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            foreach (var omission in data.Omitted)
                if (Array.IndexOf(TimingKinds, omission.Kind) >= 0)
                {
                    reason = "The export left out " + omission.Count + " " + omission.Kind + " entries that drive transitions.";
                    return false;
                }
            // A morph target blends as an active one does: its value is the
            // Unity weight, which the host maps to the glTF weight with Baked
            // and End. A parameter target is M11.
            // How VRChat picks again when a random would repeat is not measured.
            bool PreventsRepeats(List<AvatarAnimatorData.Behaviour> behaviours)
            {
                foreach (var behaviour in behaviours)
                    foreach (var operation in behaviour.Operations)
                        if (operation.PreventRepeats) return true;
                return false;
            }
            foreach (var controller in data.Controllers)
                foreach (var layer in controller.Layers)
                {
                    var prevents = false;
                    foreach (var machine in layer.Machines) prevents |= PreventsRepeats(machine.Behaviours);
                    foreach (var state in layer.States) prevents |= PreventsRepeats(state.Behaviours);
                    if (prevents) { reason = "A random that prevents repeats is not supported."; return false; }
                }
            foreach (var controller in data.Controllers)
                foreach (var layer in controller.Layers)
                {
                    if (layer.Blending != "override") { reason = "Additive layers are not supported."; return false; }
                    if (layer.SyncedLayer >= 0 && layer.SyncedLayerAffectsTiming) { reason = "Synced layers that affect timing are not supported."; return false; }
                    if (layer.SyncedLayer >= 0 && (layer.SyncedLayer >= controller.Layers.Count || !AvatarAnimatorData.SyncedShape(layer, controller.Layers[layer.SyncedLayer])))
                    { reason = "A synced layer does not match its source layer."; return false; }
                }
            foreach (var motion in data.Motions)
                if (motion.Type == "blendTree" && motion.BlendType != "1d" && motion.BlendType != "direct")
                {
                    reason = "2D blend trees are not supported.";
                    return false;
                }
            reason = null;
            return true;
        }

        const int BoolType = 0, IntType = 1, FloatType = 2, TriggerType = 3;

        sealed class Side
        {
            internal double[] Accumulated, AccumulatedWeight, Value;
            internal int[] Stamp;
            internal int Current;
            internal double Total;
        }

        sealed class LayerRun
        {
            internal AvatarAnimatorData.Layer Layer;
            internal int Index, Source = -1;
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
        }

        readonly AvatarAnimatorData data;
        readonly Func<double> random;
        readonly int[] types;
        readonly double[] parameters;
        readonly double[] rest, values, previous;
        readonly bool[] written;
        // M11: per parameter, the target of a clip's curves on it, or -1.
        readonly int[] parameterTargets;
        // M11: per target, whether it is a parameter.
        readonly bool[] isParameter;
        // M4: triggers a transition used, reset once every layer has stepped.
        readonly bool[] spent;
        readonly LayerRun[] layers;
        // Per motion: the targets it writes, the order of a 1D tree's children
        // by threshold and the children's weights while it is evaluated.
        readonly int[][] motionTargets, childOrder;
        readonly double[][] childWeights;
        // Per layer, per state: the targets the state's motion writes.
        readonly int[][][] stateTargets;
        readonly Side sideA, sideB;
        readonly AvatarAnimatorData.Transition[] chain;
        int chainCount;
        double fxWeight = 1, fxFrom, fxGoal, fxDuration, fxElapsed;
        bool fxBlending;

        public AvatarAnimatorEvaluator(AvatarAnimatorData data, Func<double> random)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            if (!Supports(data, out var reason)) throw new NotSupportedException(reason);

            types = new int[data.Parameters.Count];
            parameters = new double[data.Parameters.Count];
            spent = new bool[data.Parameters.Count];
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
            for (var i = 0; i < parameterTargets.Length; i++) parameterTargets[i] = -1;
            for (var i = 0; i < targetCount; i++)
            {
                rest[i] = data.Targets[i].Rest;
                if (data.Targets[i].Property != "parameter") continue;
                // M11: the controller's default, also when Expression Parameters declares another.
                var parameter = data.Targets[i].Parameter;
                parameterTargets[parameter] = i;
                isParameter[i] = true;
                parameters[parameter] = Convert(types[parameter], rest[i]);
            }
            values = (double[])rest.Clone();
            previous = (double[])rest.Clone();
            written = new bool[targetCount];
            sideA = NewSide(targetCount);
            sideB = NewSide(targetCount);

            motionTargets = new int[data.Motions.Count][];
            childOrder = new int[data.Motions.Count][];
            childWeights = new double[data.Motions.Count][];
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
            }

            var source = data.Controllers.Count > 0 ? data.Controllers[0].Layers : new List<AvatarAnimatorData.Layer>();
            layers = new LayerRun[source.Count];
            stateTargets = new int[source.Count][][];
            var machines = 1;
            var controllerTargets = new HashSet<int>();
            for (var l = 0; l < source.Count; l++)
            {
                stateTargets[l] = new int[source[l].States.Count][];
                for (var s = 0; s < source[l].States.Count; s++)
                {
                    var motion = source[l].States[s].Motion;
                    stateTargets[l][s] = motion < 0 ? Array.Empty<int>() : motionTargets[motion];
                    controllerTargets.UnionWith(stateTargets[l][s]);
                }
            }
            for (var l = 0; l < source.Count; l++)
            {
                var layer = source[l];
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
                if (keepsAll) animated.UnionWith(controllerTargets);
                var list = new List<int>(animated);
                list.Sort();
                layers[l] = new LayerRun
                {
                    Layer = layer, Index = l, Source = layer.SyncedLayer, Animated = list.ToArray(),
                    Out = new double[list.Count], Snapshot = new double[list.Count], Held = new double[list.Count], Own = new double[list.Count],
                    OutWritten = new bool[list.Count], SnapshotWritten = new bool[list.Count],
                    Weight = l == 0 ? 1 : layer.Weight
                };
            }
            // A resolution passes each machine at most twice (in through Entry, out through Exit).
            chain = new AvatarAnimatorData.Transition[2 * machines + 2];
        }

        static Side NewSide(int count) => new Side
        {
            Accumulated = new double[count], AccumulatedWeight = new double[count], Value = new double[count], Stamp = new int[count]
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

        public AvatarAnimatorData Data => data;
        // Each target's value, by the index of Data.Targets.
        public IReadOnlyList<double> Values => values;
        // Whether a layer wrote the target in the last Advance.
        public IReadOnlyList<bool> Written => written;

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
            parameters[parameter] = Convert(types[parameter], value);
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
        public double FxWeight => fxWeight;

        // One frame of deltaTime seconds (0 or more). The first call enters
        // every layer's default state, then advances.
        public void Advance(double deltaTime)
        {
            if (!(deltaTime >= 0) || double.IsInfinity(deltaTime)) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            Array.Copy(values, previous, values.Length);
            Array.Clear(written, 0, written.Length);
            // M10: a weight blend started by a behaviour moves from the next frame on.
            foreach (var layer in layers) BlendWeight(layer, deltaTime);
            if (fxBlending)
            {
                fxElapsed += deltaTime;
                var t = fxDuration > 0 ? Math.Min(1, fxElapsed / fxDuration) : 1;
                fxWeight = fxFrom + (fxGoal - fxFrom) * t;
                if (t >= 1) fxBlending = false;
            }
            foreach (var layer in layers)
            {
                if (layer.Source >= 0) Sync(layer);
                else Step(layer, deltaTime);
                Output(layer);
            }
            if (fxWeight < 1)
                for (var t = 0; t < values.Length; t++)
                    if (written[t]) values[t] = rest[t] + (values[t] - rest[t]) * Math.Max(0, fxWeight);
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
        }

        void Step(LayerRun layer, double dt)
        {
            var states = layer.Layer.States;
            if (!layer.Started)
            {
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
            layer.Time = AdvanceTime(states[layer.State], n0, dt, length);
            double next0 = 0;
            if (layer.Transitioning)
            {
                next0 = layer.NextTime;
                layer.NextTime = AdvanceTime(states[layer.Next], next0, dt, StateLength(states[layer.Next]));
                layer.Progress += layer.Duration > 0 ? dt / layer.Duration : 1;
                if (layer.Progress >= 1)
                {
                    // M3: a layer whose transition ended this frame starts none until the next.
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
            // M5: Any State interrupts only a transition from Any State whose source allows it.
            // Interruptions of the running transition by its source or destination.
            var sourceList = layer.ActiveFromAny ? any : states[layer.State].Transitions;
            var until = -1;
            if (layer.Active.OrderedInterruption)
                until = IndexOf(sourceList, layer.Active);
            var nextLength = StateLength(states[layer.Next]);
            switch (layer.Active.InterruptionSource)
            {
                case "source":
                    TryFire(layer, sourceList, layer.ActiveFromAny, until, n0, layer.Time, dt, length);
                    break;
                case "destination":
                    TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength);
                    break;
                case "sourceThenDestination":
                    if (!TryFire(layer, sourceList, layer.ActiveFromAny, until, n0, layer.Time, dt, length))
                        TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength);
                    break;
                case "destinationThenSource":
                    if (!TryFire(layer, states[layer.Next].Transitions, false, -1, next0, layer.NextTime, dt, nextLength))
                        TryFire(layer, sourceList, layer.ActiveFromAny, until, n0, layer.Time, dt, length);
                    break;
            }
        }

        static int IndexOf(List<AvatarAnimatorData.Transition> list, AvatarAnimatorData.Transition transition)
        {
            for (var i = 0; i < list.Count; i++)
                if (ReferenceEquals(list[i], transition)) return i;
            return -1;
        }

        // The first transition of the list that may be taken now. until >= 0
        // limits the list to the entries before it (ordered interruption); the
        // running transition itself never interrupts.
        bool TryFire(LayerRun layer, List<AvatarAnimatorData.Transition> list, bool fromAny, int until,
            double n0, double n1, double dt, double sourceLength)
        {
            var count = until >= 0 ? until : list.Count;
            for (var i = 0; i < count; i++)
            {
                var transition = list[i];
                if (layer.Transitioning && ReferenceEquals(transition, layer.Active)) continue;
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
                if (fromAny && !transition.CanTransitionToSelf && destination == (layer.Transitioning ? layer.Next : layer.State)) continue;
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
            // M4: the triggers the transitions passed are used up at the end of the frame.
            for (var c = 0; c < chainCount; c++)
                foreach (var condition in chain[c].Conditions)
                    if (types[condition.Parameter] == TriggerType) spent[condition.Parameter] = true;
            if (layer.Transitioning)
            {
                // M5: the interrupted blend continues as a still copy.
                Array.Copy(layer.Out, layer.Snapshot, layer.Out.Length);
                Array.Copy(layer.OutWritten, layer.SnapshotWritten, layer.OutWritten.Length);
                layer.Frozen = true;
                layer.InterruptSerial++;
            }
            layer.Transitioning = true;
            layer.Active = transition;
            layer.ActiveFromAny = fromAny;
            layer.Next = destination;
            layer.NextTime = transition.Offset;
            layer.Progress = 0;
            layer.Duration = transition.HasFixedDuration ? transition.Duration : transition.Duration * sourceLength;
            Enter(layer, destination);
            if (overshoot > 0)
            {
                var states = layer.Layer.States;
                layer.NextTime = AdvanceTime(states[destination], layer.NextTime, overshoot, StateLength(states[destination]));
                if (layer.Duration > 0) layer.Progress = overshoot / layer.Duration;
            }
            // M3: a transition of no length ends now; the next waits a frame.
            if (layer.Duration <= 0 || layer.Progress >= 1) Complete(layer);
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
            if (source.InterruptSerial != layer.SeenInterruptSerial)
            {
                layer.SeenInterruptSerial = source.InterruptSerial;
                Array.Copy(layer.Out, layer.Snapshot, layer.Out.Length);
                Array.Copy(layer.OutWritten, layer.SnapshotWritten, layer.OutWritten.Length);
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
                    foreach (var operation in behaviour.Operations) Drive(operation);
                    break;
                case "layerControl":
                    if (behaviour.Playable == "fx" && behaviour.Layer < layers.Length)
                    {
                        var target = layers[behaviour.Layer];
                        // M10: a 0 s blend takes the goal at once.
                        target.WeightBlending = behaviour.BlendDuration > 0;
                        target.WeightFrom = target.Weight;
                        target.WeightGoal = behaviour.GoalWeight;
                        target.WeightDuration = behaviour.BlendDuration;
                        target.WeightElapsed = 0;
                        if (!target.WeightBlending) target.Weight = behaviour.GoalWeight;
                    }
                    break;
                case "playableLayerControl":
                    if (behaviour.Playable == "fx")
                    {
                        fxBlending = behaviour.BlendDuration > 0;
                        fxFrom = fxWeight;
                        fxGoal = behaviour.GoalWeight;
                        fxDuration = behaviour.BlendDuration;
                        fxElapsed = 0;
                        if (!fxBlending) fxWeight = behaviour.GoalWeight;
                    }
                    break;
                // trackingControl moves nothing the targets show.
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
            // 1D: between the two neighbouring thresholds, clamped at the ends.
            var p = parameters[m.Parameters[0]];
            var order = childOrder[motion];
            var children = m.Children;
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

        // The state's value of each target its motion writes: the children's
        // values weighted, a child that does not write a target giving the
        // default (Rest with Write Defaults, else the previous value).
        void Evaluate(Side side, LayerRun layer, int state, double time)
        {
            var s = layer.Layer.States[state];
            var targets = stateTargets[layer.Index][state];
            side.Current++;
            side.Total = 0;
            foreach (var t in targets)
            {
                side.Accumulated[t] = 0;
                side.AccumulatedWeight[t] = 0;
                side.Stamp[t] = side.Current;
            }
            if (s.Motion < 0) return;
            var normalized = time + ParameterOr(s.CycleOffsetParameter, s.CycleOffset);
            Accumulate(side, s.Motion, 1, normalized);
            // A motion with no weight at all writes nothing, as a state without a motion.
            if (side.Total <= 0)
            {
                side.Current++;
                return;
            }
            foreach (var t in targets)
            {
                var fallback = s.WriteDefaults ? rest[t] : previous[t];
                side.Value[t] = side.Accumulated[t] + (side.Total - side.AccumulatedWeight[t]) * fallback;
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
                    side.Accumulated[track.Target] += weight * track.Curves[0].Evaluate(time);
                    side.AccumulatedWeight[track.Target] += weight;
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
            if (layer.State < 0)
            {
                Array.Clear(layer.OutWritten, 0, layer.OutWritten.Length);
                return;
            }
            if (!layer.Frozen) Evaluate(sideA, layer, layer.State, layer.Time);
            if (layer.Transitioning) Evaluate(sideB, layer, layer.Next, layer.NextTime);
            var weight = layer.Index == 0 ? 1 : layer.Weight;
            var states = layer.Layer.States;
            var serial = layer.Source >= 0 ? layer.SeenEnterSerial : layer.EnterSerial;
            if (layer.Transitioning && layer.HeldSerial != serial)
            {
                layer.HeldSerial = serial;
                for (var k = 0; k < animated.Length; k++) layer.Held[k] = layer.OutWritten[k] ? layer.Out[k] : previous[animated[k]];
            }
            for (var k = 0; k < animated.Length; k++)
            {
                var t = animated[k];
                // What the layers below leave: this frame's value, else the last frame's.
                var through = written[t] ? values[t] : previous[t];
                // Mid-transition the layer holds a target either side's motion writes.
                var bound = layer.Transitioning && layer.Progress > 0 && (sideB.Stamp[t] == sideB.Current
                    || (layer.Frozen ? layer.SnapshotWritten[k] : sideA.Stamp[t] == sideA.Current));
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
                    value += (b - value) * Math.Min(1, layer.Progress);
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
                if (!has || weight <= 0) continue;
                // M7: over the layers below, or over the rest when none of them wrote it.
                var below = written[t] ? values[t] : rest[t];
                values[t] = weight >= 1 ? value : below + (value - below) * weight;
                written[t] = true;
            }
        }

        // M6: one side's value of a target its motion does not write. With
        // Write Defaults: what the layers below leave, or the rest when none
        // wrote it. Without, a motion passes the layers below through, and a
        // state without a motion gives the layer's own last value: a layer
        // resting there keeps applying it, one moving there blends toward it
        // frame by frame.
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
                // transition whose other side writes the target, gives the rest;
                // otherwise the layers below keep the target, and with Write
                // Defaults it is the rest when none of them wrote it.
                if (s.WriteDefaults && (side.Total <= 0 || bound))
                {
                    value = rest[t];
                    return true;
                }
                value = s.WriteDefaults && !written[t] ? rest[t] : through;
                return true;
            }
            if (!s.WriteDefaults)
            {
                // A layer that never wrote the target has no value of its own.
                value = layer.OutWritten[k] ? (isParameter[t] ? layer.Own[k] : layer.Out[k]) : through;
                return layer.OutWritten[k];
            }
            // Write Defaults: the layers below, or the rest when none wrote it.
            value = written[t] ? values[t] : rest[t];
            return true;
        }
    }
}
