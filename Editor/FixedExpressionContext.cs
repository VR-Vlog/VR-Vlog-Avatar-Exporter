using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // External producers are absent from the export preview. Define their initial
    // input explicitly, while allowing the authored menu and drivers to run once.
    internal sealed class FixedExpressionContext
    {
        internal readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.Ordinal);
        internal readonly HashSet<string> UsedParameters = new HashSet<string>(StringComparer.Ordinal);

        private static readonly Dictionary<string, float> Normal = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            ["IsLocal"] = 1, ["IsAnimatorEnabled"] = 1, ["Grounded"] = 1, ["Upright"] = 1,
            ["AvatarVersion"] = 3, ["VRMode"] = 1, ["ScaleFactor"] = 1, ["ScaleFactorInverse"] = 1,
            ["InStation"] = 0, ["Seated"] = 0, ["AFK"] = 0, ["Voice"] = 0, ["Viseme"] = 0,
            ["GestureLeft"] = 0, ["GestureRight"] = 0, ["GestureLeftWeight"] = 0, ["GestureRightWeight"] = 0,
            ["AngularY"] = 0, ["VelocityX"] = 0, ["VelocityY"] = 0, ["VelocityZ"] = 0, ["VelocityMagnitude"] = 0,
            ["MuteSelf"] = 0, ["Earmuffs"] = 0, ["IsOnFriendsList"] = 0, ["ScaleModified"] = 0, ["PreviewMode"] = 0
        };

        internal static FixedExpressionContext Create(RuntimeAnimatorController runtime, IDictionary<string, float> defaults,
            VrChatExpressionMenu.Source source = null)
        {
            var result = new FixedExpressionContext();
            foreach (var parameter in ExpressionDependencies.UniqueParameters(runtime))
            {
                var builtIn = VrChatParameterDriver.BuiltIn.Contains(parameter.name);
                if ((!builtIn && source?.ExternalParameters.Contains(parameter.name) != true) ||
                    parameter.type == AnimatorControllerParameterType.Trigger) continue;
                // Avatar-dependent height inputs cannot be inferred from Animator
                // defaults. Leave unmodelled built-ins for the dependency check.
                if (builtIn && !Normal.ContainsKey(parameter.name) && parameter.name != "TrackingType") continue;
                var value = parameter.type == AnimatorControllerParameterType.Bool ? (parameter.defaultBool ? 1f : 0f) :
                    parameter.type == AnimatorControllerParameterType.Int ? parameter.defaultInt : parameter.defaultFloat;
                if (source != null && source.Defaults.TryGetValue(parameter.name, out var supplied)) value = supplied;
                if (defaults != null && defaults.TryGetValue(parameter.name, out supplied)) value = supplied;
                if (Normal.TryGetValue(parameter.name, out var normal)) value = normal;
                if (parameter.name == "TrackingType" &&
                    (defaults == null || !defaults.ContainsKey(parameter.name)) &&
                    (source == null || !source.Defaults.ContainsKey(parameter.name))) value = 6;
                if (float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidOperationException("表情を書き出す外部入力の初期値が不正です: " + parameter.name);
                if (parameter.type == AnimatorControllerParameterType.Bool) value = value == 0 ? 0 : 1;
                if (parameter.type == AnimatorControllerParameterType.Int) value = Mathf.RoundToInt(value);
                result.Values[parameter.name] = value;
            }
            return result;
        }

        // Over-approximate every possible path; only a proven false AND condition
        // removes an edge. Invalid/shared graph topology returns false so callers
        // retain their conservative command check instead of accepting a sample.
        internal static bool TryReachableFxCommands(AnimatorStateMachine machine, IDictionary<string, float> fixedValues,
            AnimatorControllerParameter[] parameters, out HashSet<StateMachineBehaviour> commands)
        {
            commands = new HashSet<StateMachineBehaviour>();
            if (machine == null || fixedValues == null || parameters == null) return false;
            var reachedCommands = commands;
            var types = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
            {
                if (parameter == null || string.IsNullOrEmpty(parameter.name) || types.ContainsKey(parameter.name)) return false;
                types.Add(parameter.name, parameter.type);
            }
            var graph = AnimatorGraph.Build(machine);
            if (graph == null) return false;
            var parents = graph.Parents; var owners = graph.Owners;
            foreach (var current in parents.Keys)
            {
                if (current.defaultState != null && (!owners.TryGetValue(current.defaultState, out var owner) || owner != current)) return false;
                if (current.entryTransitions.Any(t => !graph.Valid(t)) || current.anyStateTransitions.Any(t => !graph.Valid(t))) return false;
                foreach (var child in current.states) if (child.state.transitions.Any(t => !graph.Valid(t))) return false;
                foreach (var child in current.stateMachines)
                    if (current.GetStateMachineTransitions(child.stateMachine).Any(t => !graph.Valid(t))) return false;
            }

            bool? Condition(AnimatorCondition condition) =>
                string.IsNullOrEmpty(condition.parameter) || !fixedValues.TryGetValue(condition.parameter, out var value) ||
                !types.TryGetValue(condition.parameter, out var type) ? null : AnimatorConditions.Evaluate(condition, value, type);
            bool CanTake(AnimatorTransitionBase transition) => !transition.conditions.Any(c => Condition(c) == false);
            bool AlwaysTakes(AnimatorTransitionBase transition) => transition.conditions.All(c => Condition(c) == true);

            var active = new HashSet<AnimatorStateMachine>();
            var entered = new HashSet<AnimatorStateMachine>();
            var exited = new HashSet<AnimatorStateMachine>();
            var visited = new HashSet<AnimatorState>();
            var pending = new Queue<AnimatorState>();
            var valid = true;
            void Activate(AnimatorStateMachine current)
            {
                if (!active.Add(current)) return;
                var parent = parents[current];
                if (parent != null) Activate(parent);
                reachedCommands.UnionWith(current.behaviours);
                foreach (var transition in AnimatorConditions.Enabled(current.anyStateTransitions))
                    if (CanTake(transition)) Destination(transition, current);
            }
            void Enter(AnimatorStateMachine current)
            {
                if (!entered.Add(current)) return;
                Activate(current);
                var fallsThrough = true;
                foreach (var transition in AnimatorConditions.Enabled(current.entryTransitions))
                {
                    if (!CanTake(transition)) continue;
                    Destination(transition, current);
                    if (AlwaysTakes(transition)) { fallsThrough = false; break; }
                }
                if (fallsThrough)
                {
                    if (current.defaultState == null) valid = false;
                    else pending.Enqueue(current.defaultState);
                }
            }
            void Exit(AnimatorStateMachine current)
            {
                if (!exited.Add(current)) return;
                var parent = parents[current];
                if (parent == null) return;
                foreach (var transition in AnimatorConditions.Enabled(parent.GetStateMachineTransitions(current)))
                    if (CanTake(transition)) Destination(transition, parent);
            }
            void Destination(AnimatorTransitionBase transition, AnimatorStateMachine current)
            {
                if (transition.isExit) Exit(current);
                else if (transition.destinationState != null) pending.Enqueue(transition.destinationState);
                else Enter(transition.destinationStateMachine);
            }

            Enter(machine);
            while (valid && pending.Count > 0)
            {
                var state = pending.Dequeue();
                if (!visited.Add(state)) continue;
                var owner = owners[state];
                Activate(owner);
                reachedCommands.UnionWith(state.behaviours);
                foreach (var transition in AnimatorConditions.Enabled(state.transitions))
                    if (CanTake(transition)) Destination(transition, owner);
            }
            return valid;
        }
    }
}
