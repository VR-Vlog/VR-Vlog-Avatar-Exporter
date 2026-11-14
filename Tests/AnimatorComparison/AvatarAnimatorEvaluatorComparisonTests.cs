using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.AvatarAnimator.Comparison
{
    // Plays each case of AvatarAnimatorComparison in Unity's Animator and in
    // AvatarAnimatorEvaluator, frame by frame. With VRVLOG_ANIMATOR_RECORDINGS
    // set to a folder, Unity's values are also written there as
    // <case>.<timing>.json, which Tools/run-avatar-animator-evaluator-tests.ps1
    // replays without Unity (Tools/Fixtures/AvatarAnimator).
    public sealed class AvatarAnimatorEvaluatorComparisonTests
    {
        const string Folder = "Assets/__VRVlogAnimatorComparison";

        static IEnumerable<TestCaseData> Sources() =>
            from c in AvatarAnimatorComparison.Cases()
            from timing in AvatarAnimatorComparison.Timings
            select new TestCaseData(c.Name, timing).SetName("Compare_" + c.Name + "_" + timing);

        [TearDown]
        public void Clean() => AssetDatabase.DeleteAsset(Folder);

        [TestCaseSource(nameof(Sources))]
        public void Compare(string name, string timing)
        {
            var c = AvatarAnimatorComparison.Find(name);
            Record(c, timing, out var values, out var parameters);
            var folder = Environment.GetEnvironmentVariable("VRVLOG_ANIMATOR_RECORDINGS");
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(System.IO.Path.Combine(folder, name + "." + timing + ".json"), AvatarAnimatorComparison.Recording(c, values, parameters));
            }
            var mismatch = AvatarAnimatorComparison.Mismatch(c, timing, values, parameters);
            Assert.That(mismatch, Is.Null);
        }

        // Unity's values and parameters after each frame.
        static void Record(AvatarAnimatorComparison.Case c, string timing, out double[][] values, out double[][] parameters)
        {
            var data = c.Data;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Folder.Substring("Assets/".Length));
            var controller = Controller(c, Folder + "/" + c.Name + ".controller");
            var root = new GameObject("Comparison");
            try
            {
                var targets = new GameObject[data.Targets.Count];
                for (var t = 0; t < targets.Length; t++)
                {
                    targets[t] = new GameObject(Path(t));
                    targets[t].transform.SetParent(root.transform, false);
                    if (c.ActiveTargets.Contains(t)) targets[t].SetActive(data.Targets[t].Rest != 0);
                    else targets[t].transform.localPosition = new Vector3((float)data.Targets[t].Rest, 0, 0);
                }
                var animator = root.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.Rebind();
                var steps = AvatarAnimatorComparison.Steps(c, timing);
                var inputs = AvatarAnimatorComparison.Inputs(c, steps);
                values = new double[steps.Length][];
                parameters = new double[steps.Length][];
                for (var i = 0; i < steps.Length; i++)
                {
                    foreach (var (parameter, value) in inputs[i])
                    {
                        var p = data.Parameters[parameter];
                        switch (p.Type)
                        {
                            case "bool": animator.SetBool(p.Name, value != 0); break;
                            case "int": animator.SetInteger(p.Name, (int)value); break;
                            case "float": animator.SetFloat(p.Name, (float)value); break;
                            default: if (value != 0) animator.SetTrigger(p.Name); else animator.ResetTrigger(p.Name); break;
                        }
                    }
                    animator.Update((float)steps[i]);
                    values[i] = new double[targets.Length];
                    for (var t = 0; t < targets.Length; t++)
                        values[i][t] = data.Targets[t].Property == "parameter" ? Get(animator, data.Parameters[data.Targets[t].Parameter])
                            : c.ActiveTargets.Contains(t) ? (targets[t].activeSelf ? 1 : 0) : targets[t].transform.localPosition.x;
                    parameters[i] = new double[data.Parameters.Count];
                    for (var p = 0; p < parameters[i].Length; p++) parameters[i][p] = Get(animator, data.Parameters[p]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static double Get(Animator animator, AvatarAnimatorData.Parameter parameter) =>
            parameter.Type == "int" ? animator.GetInteger(parameter.Name)
            : parameter.Type == "float" ? animator.GetFloat(parameter.Name)
            : animator.GetBool(parameter.Name) ? 1 : 0;

        static string Path(int target) => "T" + target;

        // The controller Unity plays for the data: what AvatarAnimatorCapture
        // reads back into the same data.
        static AnimatorController Controller(AvatarAnimatorComparison.Case c, string path)
        {
            var data = c.Data;
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.RemoveLayer(0);
            foreach (var p in data.Parameters)
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = p.Name,
                    type = p.Type == "bool" ? AnimatorControllerParameterType.Bool : p.Type == "int" ? AnimatorControllerParameterType.Int
                        : p.Type == "float" ? AnimatorControllerParameterType.Float : AnimatorControllerParameterType.Trigger,
                    defaultBool = p.Default != 0, defaultInt = (int)p.Default, defaultFloat = (float)p.Default,
                });

            var clips = new List<AnimationClip>();
            foreach (var clip in data.Clips)
            {
                var unity = new AnimationClip { name = clip.Name };
                foreach (var track in clip.Tracks)
                {
                    var target = data.Targets[track.Target];
                    var binding = target.Property == "parameter"
                        ? EditorCurveBinding.FloatCurve("", typeof(Animator), data.Parameters[target.Parameter].Name)
                        : c.ActiveTargets.Contains(track.Target)
                        ? EditorCurveBinding.FloatCurve(Path(track.Target), typeof(GameObject), "m_IsActive")
                        : EditorCurveBinding.FloatCurve(Path(track.Target), typeof(Transform), "m_LocalPosition.x");
                    AnimationUtility.SetEditorCurve(unity, binding, Curve(track.Curves[0]));
                }
                // The data has no track for a curve on an int, bool or trigger
                // (Unity holds it at its default), so the first clip gets one
                // that would move it.
                if (clips.Count == 0)
                    foreach (var target in data.Targets)
                        if (target.Property == "parameter" && data.Parameters[target.Parameter].Type != "float")
                            AnimationUtility.SetEditorCurve(unity, EditorCurveBinding.FloatCurve("", typeof(Animator), data.Parameters[target.Parameter].Name),
                                AnimationCurve.Constant(0, 1, 1 - (float)target.Rest));
                var settings = AnimationUtility.GetAnimationClipSettings(unity);
                settings.loopTime = clip.Loop;
                settings.cycleOffset = (float)clip.CycleOffset;
                AnimationUtility.SetAnimationClipSettings(unity, settings);
                AssetDatabase.AddObjectToAsset(unity, controller);
                clips.Add(unity);
            }

            var motions = new Motion[data.Motions.Count];
            Motion MotionAt(int index)
            {
                if (index < 0) return null;
                if (motions[index] != null) return motions[index];
                var motion = data.Motions[index];
                if (motion.Type == "clip") return motions[index] = clips[motion.Clip];
                var tree = new BlendTree { name = "Tree " + index, useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
                tree.blendType = motion.BlendType == "1d" ? BlendTreeType.Simple1D : BlendTreeType.Direct;
                if (motion.BlendType == "1d") tree.blendParameter = data.Parameters[motion.Parameters[0]].Name;
                AssetDatabase.AddObjectToAsset(tree, controller);
                tree.children = motion.Children.Select(child => new ChildMotion
                {
                    motion = MotionAt(child.Motion),
                    threshold = (float)child.Threshold,
                    position = new Vector2((float)child.X, (float)child.Y),
                    timeScale = (float)child.TimeScale,
                    cycleOffset = (float)child.CycleOffset,
                    mirror = child.Mirror,
                    directBlendParameter = child.DirectParameter.HasValue ? data.Parameters[child.DirectParameter.Value].Name : "",
                }).ToArray();
                var serialized = new SerializedObject(tree);
                serialized.FindProperty("m_NormalizedBlendValues").boolValue = motion.NormalizedBlendValues;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                return motions[index] = tree;
            }

            var layerStates = new List<AnimatorState[]>();
            foreach (var layer in data.Controllers[0].Layers)
            {
                var machines = new AnimatorStateMachine[layer.Machines.Count];
                machines[0] = new AnimatorStateMachine { name = layer.Machines[0].Name, hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(machines[0], controller);
                for (var m = 1; m < machines.Length; m++)
                    machines[m] = machines[layer.Machines[m].Parent].AddStateMachine(layer.Machines[m].Name, new Vector3(300, 100 * m));
                var states = new AnimatorState[layer.States.Count];
                for (var s = 0; s < states.Length; s++)
                {
                    var state = layer.States[s];
                    var unity = states[s] = machines[state.Machine].AddState(state.Name, new Vector3(0, 60 * s));
                    if (layer.SyncedLayer < 0) unity.motion = MotionAt(state.Motion);
                    unity.writeDefaultValues = state.WriteDefaults;
                    unity.mirror = state.Mirror;
                    unity.speed = (float)state.Speed;
                    unity.cycleOffset = (float)state.CycleOffset;
                    if (state.SpeedParameter.HasValue) { unity.speedParameterActive = true; unity.speedParameter = data.Parameters[state.SpeedParameter.Value].Name; }
                    if (state.CycleOffsetParameter.HasValue) { unity.cycleOffsetParameterActive = true; unity.cycleOffsetParameter = data.Parameters[state.CycleOffsetParameter.Value].Name; }
                    if (state.MirrorParameter.HasValue) { unity.mirrorParameterActive = true; unity.mirrorParameter = data.Parameters[state.MirrorParameter.Value].Name; }
                    if (state.TimeParameter.HasValue) { unity.timeParameterActive = true; unity.timeParameter = data.Parameters[state.TimeParameter.Value].Name; }
                }
                for (var m = 0; m < machines.Length; m++)
                    machines[m].defaultState = layer.Machines[m].DefaultState >= 0 ? states[layer.Machines[m].DefaultState] : null;
                layerStates.Add(states);
                if (layer.SyncedLayer >= 0) continue;

                void Timed(AnimatorStateTransition unity, AvatarAnimatorData.Transition t)
                {
                    unity.hasExitTime = t.HasExitTime;
                    unity.exitTime = (float)t.ExitTime;
                    unity.hasFixedDuration = t.HasFixedDuration;
                    unity.duration = (float)t.Duration;
                    unity.offset = (float)t.Offset;
                    unity.orderedInterruption = t.OrderedInterruption;
                    unity.canTransitionToSelf = t.CanTransitionToSelf;
                    unity.interruptionSource = t.InterruptionSource switch
                    {
                        "source" => TransitionInterruptionSource.Source,
                        "destination" => TransitionInterruptionSource.Destination,
                        "sourceThenDestination" => TransitionInterruptionSource.SourceThenDestination,
                        "destinationThenSource" => TransitionInterruptionSource.DestinationThenSource,
                        _ => TransitionInterruptionSource.None,
                    };
                }
                void Conditions(AnimatorTransitionBase unity, AvatarAnimatorData.Transition t)
                {
                    foreach (var condition in t.Conditions)
                        unity.AddCondition(condition.Mode switch
                        {
                            "if" => AnimatorConditionMode.If,
                            "ifNot" => AnimatorConditionMode.IfNot,
                            "greater" => AnimatorConditionMode.Greater,
                            "less" => AnimatorConditionMode.Less,
                            "equals" => AnimatorConditionMode.Equals,
                            _ => AnimatorConditionMode.NotEqual,
                        }, (float)condition.Threshold, data.Parameters[condition.Parameter].Name);
                }

                for (var s = 0; s < states.Length; s++)
                    foreach (var t in layer.States[s].Transitions)
                    {
                        var unity = t.ToState.HasValue ? states[s].AddTransition(states[t.ToState.Value])
                            : t.ToMachine.HasValue ? states[s].AddTransition(machines[t.ToMachine.Value])
                            : states[s].AddExitTransition();
                        Timed(unity, t);
                        Conditions(unity, t);
                    }
                foreach (var t in layer.Machines[0].AnyStateTransitions)
                {
                    var unity = t.ToState.HasValue ? machines[0].AddAnyStateTransition(states[t.ToState.Value]) : machines[0].AddAnyStateTransition(machines[t.ToMachine.Value]);
                    Timed(unity, t);
                    Conditions(unity, t);
                }
                for (var m = 0; m < machines.Length; m++)
                {
                    foreach (var t in layer.Machines[m].EntryTransitions)
                        Conditions(t.ToState.HasValue ? machines[m].AddEntryTransition(states[t.ToState.Value]) : machines[m].AddEntryTransition(machines[t.ToMachine.Value]), t);
                    if (m == 0) continue;
                    var parent = machines[layer.Machines[m].Parent];
                    foreach (var t in layer.Machines[m].ExitTransitions)
                        Conditions(t.ToState.HasValue ? parent.AddStateMachineTransition(machines[m], states[t.ToState.Value])
                            : t.ToMachine.HasValue ? parent.AddStateMachineTransition(machines[m], machines[t.ToMachine.Value])
                            : parent.AddStateMachineExitTransition(machines[m]), t);
                }

                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = layer.Name, defaultWeight = (float)layer.Weight, stateMachine = machines[0],
                    blendingMode = layer.Blending == "additive" ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override,
                });
            }

            // Synced layers take the source's machines and their own motions.
            var layers = controller.layers.ToList();
            var dataLayers = data.Controllers[0].Layers;
            for (var l = 0; l < dataLayers.Count; l++)
            {
                if (dataLayers[l].SyncedLayer < 0) continue;
                var synced = new AnimatorControllerLayer
                {
                    name = dataLayers[l].Name, defaultWeight = (float)dataLayers[l].Weight,
                    syncedLayerIndex = dataLayers[l].SyncedLayer, syncedLayerAffectsTiming = dataLayers[l].SyncedLayerAffectsTiming,
                    stateMachine = new AnimatorStateMachine { name = dataLayers[l].Name, hideFlags = HideFlags.HideInHierarchy },
                };
                AssetDatabase.AddObjectToAsset(synced.stateMachine, controller);
                var source = layerStates[dataLayers[l].SyncedLayer];
                for (var s = 0; s < dataLayers[l].States.Count; s++)
                    synced.SetOverrideMotion(source[s], MotionAt(dataLayers[l].States[s].Motion));
                layers.Insert(l, synced);
            }
            controller.layers = layers.ToArray();
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        static AnimationCurve Curve(ExpressionAnimationData.Curve curve) =>
            new AnimationCurve(curve.Keys.Select(key => new Keyframe((float)key.Time, (float)key.Value,
                key.InTangent.HasValue ? (float)key.InTangent.Value : float.PositiveInfinity,
                key.OutTangent.HasValue ? (float)key.OutTangent.Value : float.PositiveInfinity,
                (float)key.InWeight, (float)key.OutWeight) { weightedMode = WeightedMode.Both }).ToArray());
    }
}
