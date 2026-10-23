using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // A gesture's authored AnimationClip is a composed face in its own
    // right. Read that clip, not every mesh morph and not an arbitrary frame of
    // the live FX controller (whose independent blink can keep running forever).
    // Retain the exact authored state so its deterministic callbacks and native
    // layer contributions are evaluated together with the selected face.
    internal static class VrChatGestureExpressions
    {
        internal static bool HasChangingMorph(AnimationClip clip, Func<string, bool> excludedPath) =>
            AnimationUtility.GetCurveBindings(clip).Where(binding => PoseSampling.IsBlendShape(binding) && excludedPath?.Invoke(binding.path) != true)
                .Any(binding => !VrChatExpressionSampler.IsConstant(AnimationUtility.GetEditorCurve(clip, binding)));

        private sealed class Target
        {
            internal AnimatorState State;
            internal int Layer;
            internal string Path;
        }

        // The clips that Add reads as gesture faces, whatever they hold.
        internal static HashSet<AnimationClip> TargetClips(RuntimeAnimatorController runtime)
        {
            var result = new HashSet<AnimationClip>();
            var controller = Resolve(runtime, out var replacements);
            if (controller == null) return result;
            foreach (var target in Discover(controller))
                if (EffectiveMotion(controller, target.State, target.Layer) is AnimationClip clip)
                    result.Add(replacements.TryGetValue(clip, out var replacement) ? replacement : clip);
            return result;
        }

        private static AnimatorController Resolve(RuntimeAnimatorController runtime, out Dictionary<AnimationClip, AnimationClip> replacements)
        {
            replacements = new Dictionary<AnimationClip, AnimationClip>();
            if (!(runtime is AnimatorOverrideController overrides)) return runtime as AnimatorController;
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrides.GetOverrides(pairs);
            foreach (var pair in pairs) if (pair.Value != null) replacements[pair.Key] = pair.Value;
            return overrides.runtimeAnimatorController as AnimatorController;
        }

        private static IEnumerable<Target> Discover(AnimatorController controller)
        {
            var layers = controller.layers;
            for (var layerIndex = 0; layerIndex < layers.Length; layerIndex++)
            {
                // Synced layers share their source state machine but can replace
                // each state's motion. Keep the evaluated layer with the target.
                var sourceIndex = layerIndex;
                var chain = new HashSet<int>();
                while (true)
                {
                    if (sourceIndex < 0 || sourceIndex >= layers.Length || !chain.Add(sourceIndex))
                        throw new InvalidOperationException("FXの同期レイヤー参照が不正です。");
                    var parent = layers[sourceIndex].syncedLayerIndex;
                    if (parent < 0) break;
                    sourceIndex = parent;
                }
                var paths = new Dictionary<AnimatorState, string>();
                var targets = new HashSet<AnimatorState>();
                var machines = new List<AnimatorStateMachine>();
                var parents = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
                void Index(AnimatorStateMachine machine, string path, AnimatorStateMachine parent)
                {
                    machines.Add(machine);
                    parents[machine] = parent;
                    foreach (var child in machine.states) paths[child.state] = path + "/" + child.state.name;
                    foreach (var child in machine.stateMachines) Index(child.stateMachine, path + "/" + child.stateMachine.name, machine);
                }
                Index(layers[sourceIndex].stateMachine, layers[layerIndex].name, null);
                var reachable = ExpressionDependencies.StructurallyReachableStates(layers[sourceIndex].stateMachine);
                var entering = new HashSet<AnimatorStateMachine>();
                var exiting = new HashSet<AnimatorStateMachine>();
                void Enter(AnimatorStateMachine machine)
                {
                    if (!entering.Add(machine)) return;
                    try
                    {
                        foreach (var entry in EnabledTransitions(machine.entryTransitions))
                        {
                            Destination(entry, machine);
                            // An unconditional entry takes precedence over later
                            // entries and over the default state.
                            if (entry.conditions.Length == 0) return;
                        }
                        if (machine.defaultState != null) targets.Add(machine.defaultState);
                    }
                    finally { entering.Remove(machine); }
                }
                void Exit(AnimatorStateMachine machine)
                {
                    if (!parents.TryGetValue(machine, out var parent) || parent == null || !exiting.Add(machine)) return;
                    try
                    {
                        foreach (var transition in EnabledTransitions(parent.GetStateMachineTransitions(machine)))
                        {
                            Destination(transition, parent);
                            if (transition.conditions.Length == 0) break;
                        }
                    }
                    finally { exiting.Remove(machine); }
                }
                void Destination(AnimatorTransitionBase transition, AnimatorStateMachine owner)
                {
                    if (transition.isExit) { Exit(owner); return; }
                    if (transition.destinationState != null) targets.Add(transition.destinationState);
                    if (transition.destinationStateMachine != null) Enter(transition.destinationStateMachine);
                }
                void Inspect(AnimatorTransitionBase transition, AnimatorStateMachine owner)
                {
                    if (transition.conditions.Any(c => IsGesture(c.parameter))) Destination(transition, owner);
                }
                foreach (var machine in machines)
                {
                    foreach (var transition in EnabledTransitions(machine.anyStateTransitions)) Inspect(transition, machine);
                    foreach (var transition in EnabledTransitions(machine.entryTransitions)) Inspect(transition, machine);
                    foreach (var child in machine.states)
                        foreach (var transition in EnabledTransitions(child.state.transitions)) Inspect(transition, machine);
                    foreach (var child in machine.stateMachines)
                        foreach (var transition in EnabledTransitions(machine.GetStateMachineTransitions(child.stateMachine))) Inspect(transition, machine);
                }
                // A hand-weight tree can be entered through a custom selector
                // or a driver relay. Its own controls still define authored
                // hand expressions, even when no transition names a hand.
                bool HandMotion(Motion motion, HashSet<Motion> visited)
                {
                    if (!(motion is BlendTree tree) || !visited.Add(motion)) return false;
                    if (tree.blendType == BlendTreeType.Direct && tree.children.Any(child => IsGesture(child.directBlendParameter)) ||
                        tree.blendType != BlendTreeType.Direct && (IsGesture(tree.blendParameter) ||
                            tree.blendType != BlendTreeType.Simple1D && IsGesture(tree.blendParameterY))) return true;
                    return tree.children.Any(child => HandMotion(child.motion, visited));
                }
                foreach (var state in paths.Keys)
                    if (reachable?.Contains(state) == true &&
                        (HandMotion(EffectiveMotion(controller, state, layerIndex), new HashSet<Motion>()) ||
                        state.timeParameterActive && IsGesture(state.timeParameter))) targets.Add(state);
                foreach (var state in targets.Where(paths.ContainsKey).OrderBy(s => paths[s], StringComparer.Ordinal))
                    yield return new Target { State = state, Layer = layerIndex, Path = layerIndex + "/" + paths[state] };
            }
        }

        private static IEnumerable<T> EnabledTransitions<T>(IEnumerable<T> transitions) where T : AnimatorTransitionBase
        {
            var siblings = transitions.ToArray();
            var solo = siblings.Any(t => t.solo);
            return siblings.Where(t => !t.mute && (!solo || t.solo));
        }

        private static Motion EffectiveMotion(AnimatorController controller, AnimatorState state, int layer)
        {
            var motion = controller.GetStateEffectiveMotion(state, layer);
            var parent = controller.layers[layer].syncedLayerIndex;
            return motion != null || parent < 0 ? motion : EffectiveMotion(controller, state, parent);
        }

        internal static List<VrChatExpressionMenu.MorphValue> ReadPose(GameObject avatar, AnimationClip clip, Func<string, bool> excludedPath = null,
            VrChatExpressionMenu.Entry entry = null, VrChatExpressionMenu.Source metadata = null)
        {
            SelectedExpressionAppearance.ValidateClipData(clip, excludedPath);
            var projection = FacialProjectionScope.NeedsProjection(avatar, new[] { clip }, excludedPath) ? FacialProjectionScope.For(avatar, metadata) : null;
            var plan = projection == null ? null : NeutralShapePlan.CreateProjection(avatar, projection.Morphs, new[] { clip }, excludedPath);
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Any(b => excludedPath?.Invoke(b.path) != true && plan?.AllowsEvaluationBinding(clip, b) != true))
                throw new InvalidOperationException("マテリアル・オブジェクトの差し替えを含む表情アニメーションは未対応です。");
            if (projection != null)
            {
                projection.WarnOmitted(AnimationUtility.GetCurveBindings(clip).Where(PoseSampling.IsBlendShape), entry?.Messages);
                if (entry != null) entry.UsesFacialProjection = true;
            }
            var result = new List<VrChatExpressionMenu.MorphValue>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (excludedPath?.Invoke(binding.path) == true) continue;
                if (!PoseSampling.IsBlendShape(binding))
                {
                    if (SelectedExpressionAppearance.IsUnchanged(avatar, clip, binding) || NeutralShapePlan.IsInertDynamicsBinding(binding)) continue;
                    // A hand pose beside the face cannot change a BlendShape;
                    // the FX sampler already settles it. Animator parameter
                    // curves can drive other layers and stay rejected.
                    if (PoseSampling.IsHumanoidChannel(binding)) continue;
                    if (plan?.AllowsEvaluationBinding(clip, binding) == true) continue;
                    throw new InvalidOperationException("BlendShape以外の変化を含む表情アニメーションです: " + binding.propertyName);
                }
                if (projection != null && !projection.Morphs.Contains(binding)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) throw new InvalidOperationException("表情アニメーションの曲線を読み取れません: " + clip.name);
                // The static value is the first pose. The animated channels
                // are exported separately and replayed by VR Vlog; the VRM
                // expression takes their last frame.
                var weight = (float)ReadCurve(curve).Evaluate(0);
                var renderer = VrChatExpressionSampler.FindRenderer(avatar, binding.path);
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("非表示のRendererを使う表情アニメーションです: " + binding.path);
                var shape = binding.propertyName.Substring("blendShape.".Length);
                // Keep authored values, even for unresolved morphs, until the
                // prepared mesh is available. Preparation may create that morph.
                result.Add(new VrChatExpressionMenu.MorphValue { Path = binding.path, Shape = shape, Weight = weight });
            }
            if (result.Count == 0) throw new InvalidOperationException("顔のBlendShapeを含まないアニメーションです。");
            return result;
        }

        // Both gesture and FaceEmo clips use this path. Values and animation
        // channels are later resolved/pruned together by PreparedExpressionBindings.
        internal static void ReadClip(GameObject avatar, AnimationClip clip, VrChatExpressionMenu.Entry entry,
            Func<string, bool> excludedPath = null, VrChatExpressionMenu.Source metadata = null)
        {
            var values = ReadPose(avatar, clip, excludedPath, entry, metadata);
            var animated = new List<VrChatExpressionMenu.AnimatedMorph>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (excludedPath?.Invoke(binding.path) == true || !PoseSampling.IsBlendShape(binding)) continue;
                if (!values.Any(value => value.Path == binding.path && "blendShape." + value.Shape == binding.propertyName)) continue;
                var curve = ReadCurve(AnimationUtility.GetEditorCurve(clip, binding));
                curve.Range(out var minimum, out var maximum);
                if (minimum == maximum) continue;
                animated.Add(new VrChatExpressionMenu.AnimatedMorph
                {
                    Path = binding.path, Shape = binding.propertyName.Substring("blendShape.".Length), Curve = curve
                });
            }
            if (animated.Count > 0 && (clip.length <= 0 || clip.length > 600))
                throw new InvalidOperationException("表情アニメーションの長さは0秒より長く600秒以下である必要があります: " + clip.name);
            entry.Values.AddRange(values);
            entry.Animation.AddRange(animated);
            entry.Duration = animated.Count > 0 ? clip.length : 0;
            entry.Loop = animated.Count > 0 && clip.isLooping;
        }

        internal static ExpressionAnimationData.Curve ReadCurve(AnimationCurve curve)
        {
            if (curve == null) throw new InvalidOperationException("表情アニメーションの曲線を読み取れません。");
            string Wrap(WrapMode mode) => mode == WrapMode.Loop ? "loop" : mode == WrapMode.PingPong ? "pingPong" : "clamp";
            var result = new ExpressionAnimationData.Curve { PreWrap = Wrap(curve.preWrapMode), PostWrap = Wrap(curve.postWrapMode) };
            foreach (var k in curve.keys)
                result.Keys.Add(new ExpressionAnimationData.Key
                {
                    Time = k.time, Value = k.value,
                    InTangent = float.IsInfinity(k.inTangent) ? (double?)null : k.inTangent,
                    OutTangent = float.IsInfinity(k.outTangent) ? (double?)null : k.outTangent,
                    InWeight = (k.weightedMode & WeightedMode.In) != 0 ? k.inWeight : 1.0 / 3,
                    OutWeight = (k.weightedMode & WeightedMode.Out) != 0 ? k.outWeight : 1.0 / 3
                });
            result.Validate();
            return result;
        }

        private static bool IsGesture(string name) => name == "GestureLeft" || name == "GestureRight" ||
            name == "GestureLeftWeight" || name == "GestureRightWeight";
    }
}
