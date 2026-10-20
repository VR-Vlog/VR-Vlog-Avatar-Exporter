using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace VRVlog.LilToonExporter
{
    // A menu expression that is still moving after the short warm-up is
    // decided from the Animator itself: follow its timed transitions to the
    // final state, then hold the clip's last key (or a loop's first frame).
    // Only the native graph is stepped; states are never jumped with Play(),
    // so state callbacks such as Parameter Drivers run exactly once per entry.
    internal static class ExpressionSettlement
    {
        internal const string FollowedTransitions = "表情の最終状態をAnimatorの時間遷移から確定しました";
        internal const string LoopingFinal = "ループする表情のため、最終状態の最初のフレームで保存しました";
        internal const string CyclicTransitions = "遷移が循環するため、最初に戻った状態で保存しました";
        internal const string FinalKeyOfClip = "時間で変わる表情を最後のキーで保存しました";
        internal const string HeldTransitions = "最終状態に時間遷移が残っていますが、今の入力では起きないため現在の状態で保存しました";
        internal const string SelfTransition = "同じ状態へ戻る遷移があるため、その状態で保存しました";
        internal const string OverlappingLoops = "複数のループが重なるため、いずれかのループの最初のフレームで保存しました";
        // Keep the number in this text equal to LongWaitSeconds.
        internal const string LongWait = "60秒を超える時間遷移の後の状態で保存しました";

        internal static readonly string[] Messages = { FollowedTransitions, LoopingFinal, CyclicTransitions, FinalKeyOfClip,
            HeldTransitions, SelfTransition, OverlappingLoops, LongWait };

        // These leave the expression out of the export, as before settlement.
        internal const string UntraceableTransitions = "時間遷移を最後までたどれないため、表情の最終状態を確定できません。";
        internal const string InvalidTransitionTime = "遷移の時間が不正なため、表情の最終状態を確定できません。";

        // A face reached only after this much simulated time is still saved,
        // but is reported: users rarely hold one menu item that long.
        internal const float LongWaitSeconds = 60;

        private const float Frame = 1f / 60f;

        // Returns true when a time-dependent decision was made (a warning was
        // added). When no timed event is pending and alreadyFixed proves the
        // current pose stationary, the graph is not evaluated at all, so
        // expressions accepted by the stationary proof keep their exact values.
        internal static bool Settle(PlayableGraph graph, AnimatorControllerPlayable playable, AnimatorController controller,
            Func<int, bool> activeLayer, Action afterUpdate, ICollection<string> warnings,
            Func<int, AnimationClip, EditorCurveBinding, bool> relevantCurve = null, Func<bool> alreadyFixed = null, int hopLimit = 64)
        {
            var added = false;
            void Warn(string message)
            {
                if (!warnings.Contains(message)) warnings.Add(message);
                added = true;
            }
            var elapsed = 0f;
            void Step(float seconds) { graph.Evaluate(seconds); afterUpdate?.Invoke(); elapsed += seconds; }
            relevantCurve = relevantCurve ?? ((layer, clip, binding) => binding.type == typeof(SkinnedMeshRenderer) &&
                binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal));
            var layers = controller.layers;
            var states = StateIndex(controller);
            (AnimatorState State, List<AnimatorStateMachine> Ancestors) Resolve(int layer, int hash) =>
                states.TryGetValue((layer, hash), out var found) ? found : (null, null);
            int[] Active() => Enumerable.Range(0, layers.Length).Where(activeLayer).ToArray();
            int Entered(int layer) => playable.IsInTransition(layer) ? playable.GetNextAnimatorStateInfo(layer).fullPathHash :
                playable.GetCurrentAnimatorStateInfo(layer).fullPathHash;
            float Clock(int layer) => playable.GetCurrentAnimatorStateInfo(layer).normalizedTime;
            float Rate(int layer)
            {
                var info = playable.GetCurrentAnimatorStateInfo(layer);
                var state = Resolve(layer, info.fullPathHash).State;
                return state != null && !state.timeParameterActive && Advancing(info) ? 1 / info.length : 0;
            }
            string Snapshot() => string.Join(";", Active().Select(layer => layer + ":" + playable.GetCurrentAnimatorStateInfo(layer).fullPathHash + ":" +
                (playable.IsInTransition(layer) ? playable.GetNextAnimatorStateInfo(layer).fullPathHash.ToString() : "-")));

            var visited = new HashSet<(int, int)>();
            var entered = new Dictionary<int, int>();
            var clocks = new Dictionary<int, float>();
            foreach (var layer in Active()) { entered[layer] = Entered(layer); visited.Add((layer, entered[layer])); clocks[layer] = Clock(layer); }
            var stuck = new HashSet<int>();
            var cyclic = new HashSet<int>();
            var reentered = new HashSet<int>();
            // A self transition with a duration is counted when it starts,
            // not on every frame while it runs.
            var selfRunning = new HashSet<int>();
            var followed = false;
            // Hops are counted per layer: independent layers each changing
            // state a few times are not a cycle.
            var hops = new Dictionary<int, int>();
            var snapshot = Snapshot();
            // Record states entered by the last step. A due exit edge that did
            // not leave the state is held by its runtime conditions; do not
            // wait on it again.
            void Track(IDictionary<int, (float Seconds, bool Timed, bool Stepped)> events, float delta)
            {
                foreach (var layer in Active())
                {
                    var current = Entered(layer);
                    if (entered.TryGetValue(layer, out var previous) && previous == current)
                    {
                        var due = events.TryGetValue(layer, out var value) ? value : ((float Seconds, bool Timed, bool Stepped)?)null;
                        // A self transition enters the state it leaves: it is
                        // running toward its own state, or a zero-duration one
                        // restarted the state's clock during the timed wait.
                        var running = playable.IsInTransition(layer) &&
                            playable.GetNextAnimatorStateInfo(layer).fullPathHash == playable.GetCurrentAnimatorStateInfo(layer).fullPathHash;
                        // Compare with the time the clock should have reached,
                        // so a state entered just before restarting is caught.
                        var self = running ? selfRunning.Add(layer) :
                            due?.Timed == true && clocks.TryGetValue(layer, out var before) &&
                            Clock(layer) + .0001f < before + Math.Max(0, delta) * Rate(layer);
                        if (!running) selfRunning.Remove(layer);
                        if (self)
                        {
                            // Do not follow its exit edges again. A self edge
                            // without exit time restarts at once; stop there.
                            Warn(SelfTransition);
                            cyclic.Add(layer);
                            if (!reentered.Add(layer)) stuck.Add(layer);
                        }
                        else if (due?.Timed == true && due.Value.Seconds <= delta + .0001f && !playable.IsInTransition(layer))
                            stuck.Add(layer);
                        continue;
                    }
                    entered[layer] = current;
                    selfRunning.Remove(layer);
                    if (!visited.Add((layer, current))) { cyclic.Add(layer); Warn(CyclicTransitions); }
                    hops[layer] = hops.TryGetValue(layer, out var count) ? count + 1 : 1;
                    if (hops[layer] > hopLimit) throw new InvalidOperationException(UntraceableTransitions);
                }
                foreach (var layer in Active()) clocks[layer] = Clock(layer);
                snapshot = Snapshot();
            }
            var iterations = 0;
            var frames = 0;
            while (true)
            {
                var parameters = Parameters(playable, controller);
                var events = new Dictionary<int, (float Seconds, bool Timed, bool Stepped)>();
                foreach (var layer in Active())
                {
                    if (stuck.Contains(layer) && !playable.IsInTransition(layer)) continue;
                    var next = NextEvent(playable, controller, layer, parameters, !cyclic.Contains(layer), Resolve);
                    if (next.HasValue) events[layer] = next.Value;
                }
                // Frame steps through an interruptible transition are bounded
                // by its length; other steps by the number of state changes, which
                // grows with the number of layers changing at different times.
                if (events.Values.Any(value => value.Stepped) ? ++frames > LongWaitSeconds / Frame :
                    ++iterations > hopLimit * 4 * Math.Max(1, Active().Length))
                    throw new InvalidOperationException(UntraceableTransitions);
                if (events.Count == 0)
                {
                    if (!followed) break;
                    // A state entered by the last step can change parameters
                    // through its callbacks. Confirm one more native frame.
                    var before = snapshot;
                    Step(Frame);
                    Track(events, -1);
                    if (snapshot == before) break;
                    continue;
                }
                followed = true;
                var delta = events.Values.Min(value => value.Seconds);
                Step(delta + Frame);
                Track(events, delta);
            }
            if (followed) Warn(FollowedTransitions);
            else if (alreadyFixed?.Invoke() == true) return false;

            // Terminal clip time. A nonlooping clip holds its last key after
            // normalized time one; a looping clip is kept at its first frame.
            bool Varies(int layer, AnimationClip clip, float from, bool whole)
            {
                foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                {
                    if (!relevantCurve(layer, clip, binding)) continue;
                    var curve = AnimationUtility.GetEditorCurve(clip, binding);
                    if (VrChatExpressionSampler.IsConstant(curve)) continue;
                    if (whole || curve.postWrapMode == WrapMode.Loop || curve.postWrapMode == WrapMode.PingPong) return true;
                    var keys = curve.keys;
                    for (var index = 1; index < keys.Length; index++)
                    {
                        var left = keys[index - 1]; var right = keys[index];
                        if (right.time <= from) continue;
                        if (left.value != right.value || left.outTangent != 0 && !float.IsInfinity(left.outTangent) ||
                            right.inTangent != 0 && !float.IsInfinity(right.inTangent)) return true;
                    }
                }
                return false;
            }
            var remaining = 0f;
            var looping = new List<int>();
            foreach (var layer in Active())
            {
                if (playable.IsInTransition(layer)) continue;
                var info = playable.GetCurrentAnimatorStateInfo(layer);
                var state = Resolve(layer, info.fullPathHash).State;
                if (state == null || state.timeParameterActive || !Advancing(info)) continue;
                var clips = playable.GetCurrentAnimatorClipInfo(layer).Where(value => value.clip != null && value.weight > .00001f).Select(value => value.clip).ToArray();
                if (info.loop) { if (clips.Any(clip => Varies(layer, clip, 0, true))) looping.Add(layer); continue; }
                if (info.normalizedTime >= 1) continue;
                var phase = Math.Max(0, info.normalizedTime);
                if (clips.Any(clip => Varies(layer, clip, phase * clip.length, false)))
                    remaining = Math.Max(remaining, (1 - phase) * info.length);
            }
            // A layer whose exit edges are no longer followed can still fire
            // them natively while the clip runs to its end. That restarts
            // the state (and its callbacks), so the face is not the last key
            // or the loop start; leave the expression out as before.
            // A layer counts when its clips write a relevant curve or its state
            // runs callbacks; a restart elsewhere does not change the face.
            // A clip that animates a controller parameter can change another
            // layer's conditions, and a state we cannot resolve is unknown, so
            // both count as before.
            var parameterNames = new HashSet<string>(controller.parameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            bool Matters(int layer)
            {
                bool Callbacks(int hash) { var state = Resolve(layer, hash).State; return state == null || state.behaviours?.Length > 0; }
                if (Callbacks(playable.GetCurrentAnimatorStateInfo(layer).fullPathHash)) return true;
                var clips = playable.GetCurrentAnimatorClipInfo(layer).AsEnumerable();
                if (playable.IsInTransition(layer))
                {
                    if (Callbacks(playable.GetNextAnimatorStateInfo(layer).fullPathHash)) return true;
                    clips = clips.Concat(playable.GetNextAnimatorClipInfo(layer));
                }
                return clips.Select(value => value.clip).Where(clip => clip != null)
                    .Any(clip => AnimationUtility.GetCurveBindings(clip).Any(binding => relevantCurve(layer, clip, binding) ||
                        binding.type == typeof(Animator) && binding.path == "" && parameterNames.Contains(binding.propertyName)));
            }
            void Final(float seconds)
            {
                // A restart inside one long step can leave the clock past its
                // old value, so compare with the time the clock should reach.
                var before = Active().Select(layer => (Layer: layer, State: Entered(layer), Time: Clock(layer), Rate: Rate(layer), Matters: Matters(layer))).ToArray();
                Step(seconds);
                foreach (var (layer, state, time, rate, matters) in before)
                    if ((playable.IsInTransition(layer) || Entered(layer) != state || Clock(layer) + .0001f < time + (seconds - Frame) * rate) &&
                        (matters || Matters(layer)))
                        throw new InvalidOperationException(UntraceableTransitions);
            }
            if (remaining > 0) { Final(remaining + Frame); Warn(FinalKeyOfClip); }
            if (looping.Count > 0)
            {
                var layer = looping.Min();
                var info = playable.GetCurrentAnimatorStateInfo(layer);
                var fraction = info.normalizedTime - Mathf.Floor(info.normalizedTime);
                if (fraction > .0001f && Advancing(info)) Final((1 - fraction) * info.length + Frame / 10);
                // One shared clock aligns one loop. A loop of another length
                // keeps its own phase, which is not its first frame.
                bool Aligned(int other)
                {
                    var otherInfo = playable.GetCurrentAnimatorStateInfo(other);
                    var otherFraction = otherInfo.normalizedTime - Mathf.Floor(otherInfo.normalizedTime);
                    return !Advancing(otherInfo) || otherFraction * otherInfo.length <= Frame || (1 - otherFraction) * otherInfo.length <= Frame;
                }
                Warn(looping.All(Aligned) ? LoopingFinal : OverlappingLoops);
            }
            if (elapsed > LongWaitSeconds) Warn(LongWait);
            return added;
        }

        // A remaining exit edge on the final state is reported only when the
        // settlement has not already said how that state was reached.
        internal static void Held(ICollection<string> warnings)
        {
            if (new[] { FollowedTransitions, CyclicTransitions, SelfTransition, HeldTransitions }.Any(warnings.Contains)) return;
            warnings.Add(HeldTransitions);
        }

        // A moving clip of the final state. Overlapping loops already say
        // that not every loop is at its first frame.
        internal static void Clip(bool loop, ICollection<string> warnings)
        {
            if (loop && warnings.Contains(OverlappingLoops)) return;
            var message = loop ? LoopingFinal : FinalKeyOfClip;
            if (!warnings.Contains(message)) warnings.Add(message);
        }

        private static bool Advancing(AnimatorStateInfo info) =>
            Finite(info.normalizedTime) && Finite(info.length) && info.length > 0 && Finite(info.speed * info.speedMultiplier) && info.speed * info.speedMultiplier > 0;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        internal static Dictionary<string, float> Parameters(AnimatorControllerPlayable playable, AnimatorController controller)
        {
            var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var parameter in controller.parameters)
                parameters[parameter.name] = parameter.type == AnimatorControllerParameterType.Float ? playable.GetFloat(parameter.name) :
                    parameter.type == AnimatorControllerParameterType.Int ? playable.GetInteger(parameter.name) : playable.GetBool(parameter.name) ? 1 : 0;
            return parameters;
        }

        // Seconds until the layer's next native state change, or null when it
        // has none: the end of a running transition, or the earliest exit time
        // whose conditions currently hold (state and ancestor Any State edges).
        // An interruptible transition is stepped one frame at a time instead.
        private static (float Seconds, bool Timed, bool Stepped)? NextEvent(AnimatorControllerPlayable playable, AnimatorController controller,
            int layer, IDictionary<string, float> parameters, bool followExitTime,
            Func<int, int, (AnimatorState State, List<AnimatorStateMachine> Ancestors)> resolve)
        {
            if (playable.IsInTransition(layer))
            {
                var transition = playable.GetAnimatorTransitionInfo(layer);
                var duration = transition.durationUnit == DurationUnit.Fixed ? transition.duration :
                    transition.duration * playable.GetCurrentAnimatorStateInfo(layer).length;
                if (!Finite(duration) || !Finite(transition.normalizedTime)) throw new InvalidOperationException(InvalidTransitionTime);
                if (Interruptible(playable, layer, resolve)) return (0, false, true);
                return (Math.Max(0, (1 - transition.normalizedTime) * duration), false, false);
            }
            if (!followExitTime) return null;
            var info = playable.GetCurrentAnimatorStateInfo(layer);
            if (info.fullPathHash == 0 || !Advancing(info)) return null;
            var (state, ancestors) = resolve(layer, info.fullPathHash);
            if (state == null || state.timeParameterActive) return null;
            // An Any State edge into the current state fires only when it can
            // transition to self.
            var exits = state.transitions.Concat(ancestors.SelectMany(machine => machine.anyStateTransitions)
                    .Where(value => value.destinationState != state || value.canTransitionToSelf))
                .Where(value => !value.mute && value.hasExitTime && !ExpressionDependencies.IsFalse(value, parameters))
                .Select(value => value.exitTime).Where(Finite).ToArray();
            if (exits.Length == 0) return null;
            var exit = exits.Min();
            var time = info.normalizedTime;
            var wait = info.loop && exit < 1 ? Mathf.Repeat(exit - (time - Mathf.Floor(time)), 1) : Math.Max(0, exit - time);
            return (wait * info.length, true, false);
        }

        // VRChat evaluates every frame, so another edge can cut a running
        // transition whose interruption source is set. The running edge is not
        // identified by Unity; any matching edge with a source counts, and an
        // unresolved state is treated as interruptible.
        private static bool Interruptible(AnimatorControllerPlayable playable, int layer,
            Func<int, int, (AnimatorState State, List<AnimatorStateMachine> Ancestors)> resolve)
        {
            var (source, ancestors) = resolve(layer, playable.GetCurrentAnimatorStateInfo(layer).fullPathHash);
            var target = resolve(layer, playable.GetNextAnimatorStateInfo(layer).fullPathHash).State;
            if (source == null || target == null) return true;
            return source.transitions.Concat(ancestors.SelectMany(machine => machine.anyStateTransitions))
                .Any(value => !value.mute && (value.destinationState == null || value.destinationState == target) &&
                    value.interruptionSource != TransitionInterruptionSource.None);
        }

        // (layer, state hash) -> the state and its machines from the root.
        // Synced layers play their owner's state machine. Unity can retain
        // either the layer name or the root machine name in the state hash.
        // A hash that names two different states is left out.
        private static Dictionary<(int Layer, int Hash), (AnimatorState State, List<AnimatorStateMachine> Ancestors)> StateIndex(AnimatorController controller)
        {
            var index = new Dictionary<(int, int), (AnimatorState, List<AnimatorStateMachine>)>();
            var ambiguous = new HashSet<(int, int)>();
            var layers = controller.layers;
            for (var layer = 0; layer < layers.Length; layer++)
            {
                var owner = layer;
                var seen = new HashSet<int>();
                while (owner >= 0 && owner < layers.Length && layers[owner].syncedLayerIndex >= 0)
                {
                    if (!seen.Add(owner)) { owner = -1; break; }
                    owner = layers[owner].syncedLayerIndex;
                }
                if (owner < 0 || owner >= layers.Length) continue;
                void Visit(AnimatorStateMachine machine, string path, List<AnimatorStateMachine> chain)
                {
                    var current = new List<AnimatorStateMachine>(chain) { machine };
                    foreach (var child in machine.states)
                    {
                        var key = (layer, Animator.StringToHash(path + "." + child.state.name));
                        if (!index.TryGetValue(key, out var found)) index[key] = (child.state, current);
                        else if (found.Item1 != child.state) ambiguous.Add(key);
                    }
                    foreach (var child in machine.stateMachines) Visit(child.stateMachine, path + "." + child.stateMachine.name, current);
                }
                foreach (var root in new[] { layers[layer].name, layers[owner].name, layers[owner].stateMachine.name }.Distinct(StringComparer.Ordinal))
                    Visit(layers[owner].stateMachine, root, new List<AnimatorStateMachine>());
            }
            foreach (var key in ambiguous) index.Remove(key);
            return index;
        }

        // The same (layer, state hash) lookup for callers outside Settle.
        internal static Func<int, int, AnimatorState> StateResolver(AnimatorController controller)
        {
            var index = StateIndex(controller);
            return (layer, hash) => index.TryGetValue((layer, hash), out var found) ? found.State : null;
        }
    }
}
