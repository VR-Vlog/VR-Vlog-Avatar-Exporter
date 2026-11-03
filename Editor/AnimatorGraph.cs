using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // Animator condition evaluation shared by the FX walks. A result is true,
    // false, or null when it is not decided.
    internal static class AnimatorConditions
    {
        internal static bool Known(AnimatorConditionMode mode) =>
            mode == AnimatorConditionMode.If || mode == AnimatorConditionMode.IfNot ||
            mode == AnimatorConditionMode.Equals || mode == AnimatorConditionMode.NotEqual ||
            mode == AnimatorConditionMode.Greater || mode == AnimatorConditionMode.Less;

        // Raw comparison. An unknown mode and an ordered comparison with NaN
        // are undecided.
        internal static bool? Evaluate(AnimatorCondition condition, float value)
        {
            switch (condition.mode)
            {
                case AnimatorConditionMode.If: return value != 0;
                case AnimatorConditionMode.IfNot: return value == 0;
                case AnimatorConditionMode.Equals: return value == condition.threshold;
                case AnimatorConditionMode.NotEqual: return value != condition.threshold;
                case AnimatorConditionMode.Greater: return value > condition.threshold ? true : value <= condition.threshold ? false : (bool?)null;
                case AnimatorConditionMode.Less: return value < condition.threshold ? true : value >= condition.threshold ? false : (bool?)null;
                default: return null;
            }
        }

        // Comparison by the parameter's type. Bool reads only If/IfNot;
        // Float and Int need finite values, and Int equality whole numbers.
        // Fractional Int thresholds have no portable authored semantics.
        internal static bool? Evaluate(AnimatorCondition condition, float value, AnimatorControllerParameterType type)
        {
            if (!Finite(value)) return null;
            if (type == AnimatorControllerParameterType.Bool)
                return condition.mode == AnimatorConditionMode.If || condition.mode == AnimatorConditionMode.IfNot ? Evaluate(condition, value) : null;
            if (type != AnimatorControllerParameterType.Float && type != AnimatorControllerParameterType.Int || !Finite(condition.threshold) ||
                type == AnimatorControllerParameterType.Int && (value != Math.Truncate(value) || condition.threshold != Math.Truncate(condition.threshold))) return null;
            switch (condition.mode)
            {
                case AnimatorConditionMode.Equals:
                case AnimatorConditionMode.NotEqual: return type == AnimatorControllerParameterType.Int ? Evaluate(condition, value) : null;
                case AnimatorConditionMode.Greater:
                case AnimatorConditionMode.Less: return Evaluate(condition, value);
                default: return null;
            }
        }

        // Unity's mute/solo: a solo sibling suppresses every non-solo sibling,
        // even a muted or false one. Null entries are kept for the caller.
        internal static IEnumerable<T> Enabled<T>(IEnumerable<T> transitions) where T : AnimatorTransitionBase
        {
            var siblings = transitions.ToArray();
            var solo = siblings.Any(transition => transition != null && transition.solo);
            return siblings.Where(transition => transition == null || !transition.mute && (!solo || transition.solo));
        }
    }

    // The machines and states of one layer graph, each machine with its parent
    // and each state with its owner machine.
    internal sealed class AnimatorGraph
    {
        internal readonly Dictionary<AnimatorStateMachine, AnimatorStateMachine> Parents = new Dictionary<AnimatorStateMachine, AnimatorStateMachine>();
        internal readonly Dictionary<AnimatorState, AnimatorStateMachine> Owners = new Dictionary<AnimatorState, AnimatorStateMachine>();

        // Null when a machine or state is missing or appears twice.
        internal static AnimatorGraph Build(AnimatorStateMachine root)
        {
            var graph = new AnimatorGraph();
            bool Index(AnimatorStateMachine machine, AnimatorStateMachine parent)
            {
                if (machine == null || graph.Parents.ContainsKey(machine)) return false;
                graph.Parents.Add(machine, parent);
                foreach (var child in machine.states)
                {
                    if (child.state == null || graph.Owners.ContainsKey(child.state)) return false;
                    graph.Owners.Add(child.state, machine);
                }
                return machine.stateMachines.All(child => Index(child.stateMachine, machine));
            }
            return Index(root, null) ? graph : null;
        }

        // An Exit has no destination; any other edge has exactly one, inside
        // this graph.
        internal bool Valid(AnimatorTransitionBase transition) => transition != null && (transition.isExit ?
            transition.destinationState == null && transition.destinationStateMachine == null :
            transition.destinationState != null ? transition.destinationStateMachine == null && Owners.ContainsKey(transition.destinationState) :
            transition.destinationStateMachine != null && Parents.ContainsKey(transition.destinationStateMachine));

        internal static IEnumerable<AnimatorState> AllStates(AnimatorStateMachine root)
        {
            if (root == null) yield break;
            var seen = new HashSet<AnimatorStateMachine>();
            var pending = new Stack<AnimatorStateMachine>(); pending.Push(root);
            while (pending.Count > 0)
            {
                var machine = pending.Pop();
                if (machine == null || !seen.Add(machine)) continue;
                foreach (var state in machine.states) if (state.state != null) yield return state.state;
                foreach (var child in machine.stateMachines) pending.Push(child.stateMachine);
            }
        }

        // A motion's clips, including every BlendTree child.
        internal static IEnumerable<AnimationClip> Clips(Motion root)
        {
            var seen = new HashSet<Motion>();
            var pending = new Stack<Motion>(); pending.Push(root);
            while (pending.Count > 0)
            {
                var motion = pending.Pop();
                if (motion == null || !seen.Add(motion)) continue;
                if (motion is AnimationClip clip) yield return clip;
                else if (motion is BlendTree tree) foreach (var child in tree.children) pending.Push(child.motion);
            }
        }
    }
}
