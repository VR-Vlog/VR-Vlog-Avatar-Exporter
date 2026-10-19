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

        internal static readonly string[] Messages = { FollowedTransitions, LoopingFinal, CyclicTransitions, FinalKeyOfClip };

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
            void Step(float seconds) { graph.Evaluate(seconds); afterUpdate?.Invoke(); }
            relevantCurve = relevantCurve ?? ((layer, clip, binding) => binding.type == typeof(SkinnedMeshRenderer) &&
                binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal));
            var layers = controller.layers;
            int[] Active() => Enumerable.Range(0, layers.Length).Where(activeLayer).ToArray();
            int Entered(int layer) => playable.IsInTransition(layer) ? playable.GetNextAnimatorStateInfo(layer).fullPathHash :
                playable.GetCurrentAnimatorStateInfo(layer).fullPathHash;
            string Snapshot() => string.Join(";", Active().Select(layer => layer + ":" + playable.GetCurrentAnimatorStateInfo(layer).fullPathHash + ":" +
                (playable.IsInTransition(layer) ? playable.GetNextAnimatorStateInfo(layer).fullPathHash.ToString() : "-")));

            var visited = new HashSet<(int, int)>();
            var entered = new Dictionary<int, int>();
            foreach (var layer in Active()) { entered[layer] = Entered(layer); visited.Add((layer, entered[layer])); }
            var stuck = new HashSet<int>();
            var cyclic = new HashSet<int>();
            var followed = false;
            var hops = 0;
            var snapshot = Snapshot();
            // Record states entered by the last step. A due exit edge that did
            // not leave the state is held by its runtime conditions; do not
            // wait on it again.
            void Track(IDictionary<int, (float Seconds, bool Timed)> events, float delta)
            {
                foreach (var layer in Active())
                {
                    var current = Entered(layer);
                    if (entered.TryGetValue(layer, out var previous) && previous == current)
                    {
                        if (events.TryGetValue(layer, out var due) && due.Timed && due.Seconds <= delta + .0001f && !playable.IsInTransition(layer))
                            stuck.Add(layer);
                        continue;
                    }
                    entered[layer] = current;
                    if (!visited.Add((layer, current))) { cyclic.Add(layer); Warn(CyclicTransitions); }
                    if (++hops > hopLimit) { Warn(CyclicTransitions); stuck.UnionWith(Active()); }
                }
                snapshot = Snapshot();
            }
            for (var iteration = 0; ; iteration++)
            {
                if (iteration > hopLimit * 4) { Warn(CyclicTransitions); break; }
                var parameters = Parameters(playable, controller);
                var events = new Dictionary<int, (float Seconds, bool Timed)>();
                foreach (var layer in Active())
                {
                    if (stuck.Contains(layer)) continue;
                    var next = NextEvent(playable, controller, layer, parameters, !cyclic.Contains(layer));
                    if (next.HasValue) events[layer] = next.Value;
                }
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
                var state = ResolveState(controller, layer, info.fullPathHash, out _);
                if (state == null || state.timeParameterActive || !Advancing(info)) continue;
                var clips = playable.GetCurrentAnimatorClipInfo(layer).Where(value => value.clip != null && value.weight > .00001f).Select(value => value.clip).ToArray();
                if (info.loop) { if (clips.Any(clip => Varies(layer, clip, 0, true))) looping.Add(layer); continue; }
                if (info.normalizedTime >= 1) continue;
                var phase = Math.Max(0, info.normalizedTime);
                if (clips.Any(clip => Varies(layer, clip, phase * clip.length, false)))
                    remaining = Math.Max(remaining, (1 - phase) * info.length);
            }
            if (remaining > 0) { Step(remaining + Frame); Warn(FinalKeyOfClip); }
            if (looping.Count > 0)
            {
                var layer = looping.Min();
                var info = playable.GetCurrentAnimatorStateInfo(layer);
                var fraction = info.normalizedTime - Mathf.Floor(info.normalizedTime);
                if (fraction > .0001f && Advancing(info)) Step((1 - fraction) * info.length + Frame / 10);
                Warn(LoopingFinal);
            }
            return added;
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
        private static (float Seconds, bool Timed)? NextEvent(AnimatorControllerPlayable playable, AnimatorController controller,
            int layer, IDictionary<string, float> parameters, bool followExitTime)
        {
            if (playable.IsInTransition(layer))
            {
                var transition = playable.GetAnimatorTransitionInfo(layer);
                var duration = transition.durationUnit == DurationUnit.Fixed ? transition.duration :
                    transition.duration * playable.GetCurrentAnimatorStateInfo(layer).length;
                if (!Finite(duration) || !Finite(transition.normalizedTime)) return (0, false);
                return (Math.Max(0, (1 - transition.normalizedTime) * duration), false);
            }
            if (!followExitTime) return null;
            var info = playable.GetCurrentAnimatorStateInfo(layer);
            if (info.fullPathHash == 0 || !Advancing(info)) return null;
            var state = ResolveState(controller, layer, info.fullPathHash, out var ancestors);
            if (state == null || state.timeParameterActive) return null;
            var exits = state.transitions.Concat(ancestors.SelectMany(machine => machine.anyStateTransitions))
                .Where(value => !value.mute && value.hasExitTime && !ExpressionDependencies.IsFalse(value, parameters))
                .Select(value => value.exitTime).Where(Finite).ToArray();
            if (exits.Length == 0) return null;
            var exit = exits.Min();
            var time = info.normalizedTime;
            var wait = info.loop && exit < 1 ? Mathf.Repeat(exit - (time - Mathf.Floor(time)), 1) : Math.Max(0, exit - time);
            return (wait * info.length, true);
        }

        // Synced layers play their owner's state machine. Unity can retain
        // either the layer name or the root machine name in the state hash.
        internal static AnimatorState ResolveState(AnimatorController controller, int layer, int hash, out List<AnimatorStateMachine> ancestors)
        {
            ancestors = null;
            var layers = controller.layers;
            var owner = layer;
            var seen = new HashSet<int>();
            while (layers[owner].syncedLayerIndex >= 0)
            {
                if (!seen.Add(owner)) return null;
                owner = layers[owner].syncedLayerIndex;
                if (owner < 0 || owner >= layers.Length) return null;
            }
            var matches = new List<(AnimatorState State, List<AnimatorStateMachine> Path)>();
            void Visit(AnimatorStateMachine machine, string path, List<AnimatorStateMachine> chain)
            {
                var current = new List<AnimatorStateMachine>(chain) { machine };
                foreach (var child in machine.states)
                    if (Animator.StringToHash(path + "." + child.state.name) == hash && matches.All(match => match.State != child.state))
                        matches.Add((child.state, current));
                foreach (var child in machine.stateMachines) Visit(child.stateMachine, path + "." + child.stateMachine.name, current);
            }
            foreach (var root in new[] { layers[layer].name, layers[owner].stateMachine.name }.Distinct(StringComparer.Ordinal))
                Visit(layers[owner].stateMachine, root, new List<AnimatorStateMachine>());
            if (matches.Count != 1) return null;
            ancestors = matches[0].Path;
            return matches[0].State;
        }
    }
}
