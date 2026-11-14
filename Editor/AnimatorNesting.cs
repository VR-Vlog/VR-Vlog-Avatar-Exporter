using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEditor.Animations;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The walks over an FX controller recurse once per nested state machine
    // and BlendTree. A deep enough chain would exhaust the stack, which Unity
    // does not recover from, so a controller is checked once, without
    // recursion, before any walk reads it. Loops are left to the walks, which
    // report them in their own words.
    internal static class AnimatorNesting
    {
        // A chain of 256 state machines built with the editor API exported in
        // Unity 2022.3.22f1; building 1024 overflowed in Unity itself.
        internal const int MaximumDepth = 256;
        internal const string TooDeepReason = "FXのStateMachineかBlendTreeの入れ子が上限の256階層を超えています。";

        static readonly ConditionalWeakTable<AnimatorController, object> Checked = new ConditionalWeakTable<AnimatorController, object>();

        // The controller, once no state machine or BlendTree in it is nested
        // deeper than the limit. A controller the export builds itself copies
        // one that was checked, so later edits to it are not checked again.
        internal static AnimatorController Check(AnimatorController controller)
        {
            if (controller == null || Checked.TryGetValue(controller, out _)) return controller;
            var layers = controller.layers;
            var machines = new Dictionary<AnimatorStateMachine, int>();
            var motions = new Dictionary<Motion, int>();
            for (var index = 0; index < layers.Length; index++)
            {
                var source = layers[index].syncedLayerIndex;
                var root = layers[source >= 0 && source < layers.Length ? source : index].stateMachine;
                if (root == null) continue;
                Walk(root, machines, machine => machine.stateMachines.Select(child => child.stateMachine));
                foreach (var state in AnimatorGraph.AllStates(root))
                {
                    Walk(state.motion, motions, Children);
                    if (source >= 0) Walk(controller.GetStateEffectiveMotion(state, index), motions, Children);
                }
            }
            Checked.Add(controller, null);
            return controller;
        }

        // Only BlendTrees nest; a clip ends the chain.
        static IEnumerable<Motion> Children(Motion motion) =>
            motion is BlendTree tree ? tree.children.Select(child => child.motion).OfType<BlendTree>() : Enumerable.Empty<Motion>();

        // Depth first with a stack. `deepest` keeps how deep each node was
        // walked; a node reached again no deeper is not walked again, and one
        // already on the path (a loop) is skipped.
        static void Walk<T>(T root, Dictionary<T, int> deepest, Func<T, IEnumerable<T>> children) where T : UnityEngine.Object
        {
            if (root == null) return;
            var path = new HashSet<T>();
            var pending = new Stack<(T Node, IEnumerator<T> Next)>();
            bool Enter(T node, int depth)
            {
                if (node == null || path.Contains(node) || deepest.TryGetValue(node, out var walked) && walked >= depth) return false;
                if (depth > MaximumDepth) throw new InvalidOperationException(TooDeepReason);
                deepest[node] = depth;
                path.Add(node);
                pending.Push((node, children(node).ToList().GetEnumerator()));
                return true;
            }
            Enter(root, 0);
            while (pending.Count > 0)
            {
                var (node, next) = pending.Peek();
                if (next.MoveNext()) Enter(next.Current, pending.Count);
                else
                {
                    pending.Pop();
                    path.Remove(node);
                }
            }
        }
    }
}
