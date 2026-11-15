using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.AvatarAnimator.Comparison
{
    // Plays each case of AvatarAnimatorComparison in Unity's Animator and in
    // AvatarAnimatorEvaluator, frame by frame. With VRVLOG_ANIMATOR_RECORDINGS
    // set to a folder, Unity's values are also written there as
    // <case>.<timing>.json, which Tools/run-avatar-animator-evaluator-tests.ps1
    // replays without Unity (Tools/Fixtures/AvatarAnimator).
    //
    // A case with layers in one controller plays it as the Animator's
    // controller. A case with layers in more than one plays them as VRChat
    // stacks its playable layers (M15): a PlayableGraph whose
    // AnimationLayerMixerPlayable has the Gesture, the Action and the FX in
    // that order, the Action at weight 0, each masked with its base layer's
    // body parts, as VRChat does, and the FX without muscles when its base
    // layer has no mask (M16). The data's behaviours are played by the
    // test, as plain Unity has no VRChat SDK (a StateMachineBehaviour of
    // this editor-only assembly cannot go on a state): after each frame, a
    // state that became a layer's current or next state is entered, and its
    // Parameter Drivers set their values on every controller (M8, after
    // every layer) and its Playable Layer Controls blend their input's weight
    // from the next frame (M10). This checks the stacking, not M8 or M10.
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
            var played = Played(c);
            var controllers = new AnimatorController[data.Controllers.Count];
            foreach (var p in played) controllers[p] = Controller(c, p, Folder + "/" + c.Name + "." + p + ".controller");
            var root = new GameObject("Comparison");
            Avatar avatar = null;
            HumanPoseHandler poses = null;
            var graph = default(PlayableGraph);
            var masks = new List<AvatarMask>();
            try
            {
                var targets = new GameObject[data.Targets.Count];
                for (var t = 0; t < targets.Length; t++)
                {
                    targets[t] = new GameObject(Path(t));
                    targets[t].transform.SetParent(root.transform, false);
                    if (c.ActiveTargets.Contains(t)) targets[t].SetActive(data.Targets[t].Rest != 0);
                    else if (data.Targets[t].Property == "morph") Morph(targets[t]).SetBlendShapeWeight(0, (float)data.Targets[t].Rest);
                    else targets[t].transform.localPosition = new Vector3((float)data.Targets[t].Rest, 0, 0);
                }
                var animator = root.AddComponent<Animator>();
                if (c.Humanoid)
                {
                    avatar = Humanoid(root);
                    animator.avatar = avatar;
                    poses = new HumanPoseHandler(avatar, root.transform);
                }

                // Each played controller as (data index, its parameters' setters
                // and getters, its layer weights); the Animator's alone, or the
                // graph's playables.
                Action<int, double> set;
                Func<int, double> get;
                Action<int, int, double> setLayerWeight;
                Action<double> update;
                Action afterFrame = () => { };
                var layerOwner = new List<(int Controller, int Layer)>();
                for (var ci = 0; ci < data.Controllers.Count; ci++)
                    for (var l = 0; l < data.Controllers[ci].Layers.Count; l++) layerOwner.Add((ci, l));
                if (played.Count == 1)
                {
                    animator.runtimeAnimatorController = controllers[played[0]];
                    animator.Rebind();
                    set = (p, value) => Set(animator, data.Parameters[p], value);
                    get = p => Get(animator, data.Parameters[p]);
                    setLayerWeight = (ci, l, weight) => animator.SetLayerWeight(l, (float)weight);
                    update = dt => animator.Update((float)dt);
                }
                else
                {
                    animator.Rebind();
                    graph = PlayableGraph.Create("Comparison");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var order = new[] { "gesture", "action", "fx" }.SelectMany(playable => played.Where(p => data.Controllers[p].Playable == playable)).ToList();
                    var mixer = AnimationLayerMixerPlayable.Create(graph, order.Count);
                    var playables = new AnimatorControllerPlayable[data.Controllers.Count];
                    var inputs = new int[data.Controllers.Count];
                    for (var i = 0; i < order.Count; i++)
                    {
                        var ci = order[i];
                        playables[ci] = AnimatorControllerPlayable.Create(graph, controllers[ci]);
                        inputs[ci] = i;
                        graph.Connect(playables[ci], 0, mixer, i);
                        mixer.SetInputWeight(i, data.Controllers[ci].Playable == "action" ? 0 : 1);
                        var baseMask = data.Controllers[ci].Layers[0].Mask;
                        if (baseMask != null || data.Controllers[ci].Playable == "fx")
                        {
                            var parts = new AvatarMask { name = "Playable " + i };
                            for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                                parts.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, baseMask != null && baseMask.Humanoid.Contains(part));
                            masks.Add(parts);
                            mixer.SetLayerMaskFromAvatarMask((uint)i, parts);
                        }
                    }
                    AnimationPlayableOutput.Create(graph, "Animator", animator).SetSourcePlayable(mixer);
                    var top = order[order.Count - 1];
                    set = (p, value) => { foreach (var ci in order) Set(playables[ci], data.Parameters[p], value); };
                    get = p => Get(playables[top], data.Parameters[p]);
                    setLayerWeight = (ci, l, weight) => playables[ci].SetLayerWeight(l, (float)weight);
                    var blends = new List<(int Input, double From, double Goal, double Duration, double Elapsed)>();
                    update = dt =>
                    {
                        // M10: a blend started in a frame moves from the next.
                        for (var b = 0; b < blends.Count; b++)
                        {
                            var (input, from, goal, duration, elapsed) = blends[b];
                            elapsed += dt;
                            mixer.SetInputWeight(input, (float)(from + (goal - from) * Math.Min(1, elapsed / duration)));
                            blends[b] = (input, from, goal, duration, elapsed);
                        }
                        blends.RemoveAll(blend => blend.Elapsed >= blend.Duration);
                        graph.Evaluate((float)dt);
                    };
                    // The states each layer was in or going to after the last frame.
                    var was = order.ToDictionary(ci => ci, ci => data.Controllers[ci].Layers.Select(_ => new HashSet<int>()).ToArray());
                    afterFrame = () =>
                    {
                        foreach (var ci in order)
                            for (var l = 0; l < data.Controllers[ci].Layers.Count; l++)
                            {
                                var states = data.Controllers[ci].Layers[l].States;
                                var now = new HashSet<int> { playables[ci].GetCurrentAnimatorStateInfo(l).shortNameHash };
                                if (playables[ci].IsInTransition(l)) now.Add(playables[ci].GetNextAnimatorStateInfo(l).shortNameHash);
                                foreach (var hash in now.Where(hash => !was[ci][l].Contains(hash)))
                                {
                                    var entered = states.FindIndex(state => Animator.StringToHash(state.Name) == hash);
                                    if (entered < 0) continue;
                                    foreach (var behaviour in states[entered].Behaviours) Play(behaviour);
                                }
                                was[ci][l] = now;
                            }
                    };
                    void Play(AvatarAnimatorData.Behaviour behaviour)
                    {
                        if (behaviour.Type == "parameterDriver")
                            foreach (var operation in behaviour.Operations)
                            {
                                Assert.That(operation.Type, Is.EqualTo("set"), "the comparison drives only set");
                                set(operation.Parameter, operation.Value);
                            }
                        else if (behaviour.Type == "playableLayerControl")
                        {
                            var owner = order.Where(ci => data.Controllers[ci].Playable == behaviour.Playable).DefaultIfEmpty(-1).First();
                            if (owner < 0) return;
                            var input = inputs[owner];
                            blends.RemoveAll(blend => blend.Input == input);
                            if (behaviour.BlendDuration > 0) blends.Add((input, mixer.GetInputWeight(input), behaviour.GoalWeight, behaviour.BlendDuration, 0));
                            else mixer.SetInputWeight(input, (float)behaviour.GoalWeight);
                        }
                        else Assert.Fail("the comparison plays no " + behaviour.Type);
                    }
                }
                var pose = new HumanPose();
                var steps = AvatarAnimatorComparison.Steps(c, timing);
                var inputs2 = AvatarAnimatorComparison.Inputs(c, steps);
                var weights = AvatarAnimatorComparison.LayerWeights(c, steps);
                values = new double[steps.Length][];
                parameters = new double[steps.Length][];
                for (var i = 0; i < steps.Length; i++)
                {
                    foreach (var (parameter, value) in inputs2[i]) set(parameter, value);
                    foreach (var (layer, weight) in weights[i]) setLayerWeight(layerOwner[layer].Controller, layerOwner[layer].Layer, weight);
                    update(steps[i]);
                    afterFrame();
                    poses?.GetHumanPose(ref pose);
                    values[i] = new double[targets.Length];
                    for (var t = 0; t < targets.Length; t++)
                        values[i][t] = data.Targets[t].Property == "parameter" ? get(data.Targets[t].Parameter)
                            : data.Targets[t].Property == "muscle" ? pose.muscles[data.Targets[t].Muscle]
                            : data.Targets[t].Property == "morph" ? targets[t].GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(0)
                            : c.ActiveTargets.Contains(t) ? (targets[t].activeSelf ? 1 : 0) : targets[t].transform.localPosition.x;
                    parameters[i] = new double[data.Parameters.Count];
                    for (var p = 0; p < parameters[i].Length; p++) parameters[i][p] = get(p);
                }
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                masks.ForEach(Object.DestroyImmediate);
                poses?.Dispose();
                foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Object.DestroyImmediate(renderer.sharedMesh);
                Object.DestroyImmediate(root);
                if (avatar != null) Object.DestroyImmediate(avatar);
            }
        }

        static void Set(Animator animator, AvatarAnimatorData.Parameter p, double value)
        {
            switch (p.Type)
            {
                case "bool": animator.SetBool(p.Name, value != 0); break;
                case "int": animator.SetInteger(p.Name, (int)value); break;
                case "float": animator.SetFloat(p.Name, (float)value); break;
                default: if (value != 0) animator.SetTrigger(p.Name); else animator.ResetTrigger(p.Name); break;
            }
        }

        static void Set(AnimatorControllerPlayable playable, AvatarAnimatorData.Parameter p, double value)
        {
            switch (p.Type)
            {
                case "bool": playable.SetBool(p.Name, value != 0); break;
                case "int": playable.SetInteger(p.Name, (int)value); break;
                case "float": playable.SetFloat(p.Name, (float)value); break;
                default: if (value != 0) playable.SetTrigger(p.Name); else playable.ResetTrigger(p.Name); break;
            }
        }

        static double Get(AnimatorControllerPlayable playable, AvatarAnimatorData.Parameter parameter) =>
            parameter.Type == "int" ? playable.GetInteger(parameter.Name)
            : parameter.Type == "float" ? playable.GetFloat(parameter.Name)
            : playable.GetBool(parameter.Name) ? 1 : 0;

        // A SkinnedMeshRenderer with a mesh of one triangle and one blend shape.
        static SkinnedMeshRenderer Morph(GameObject target)
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Shape", 100, new[] { Vector3.forward, Vector3.forward, Vector3.forward }, null, null);
            var renderer = target.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            return renderer;
        }

        // The controllers Unity plays: the data's with layers.
        static List<int> Played(AvatarAnimatorComparison.Case c)
        {
            var controllers = c.Data.Controllers;
            var played = Enumerable.Range(0, controllers.Count).Where(i => controllers[i].Layers.Count > 0).ToList();
            Assert.That(played, Is.Not.Empty, c.Name + " has no layers");
            return played;
        }

        // A humanoid rig in a T-pose with every bone of HumanTrait, Hips
        // under root, and its Avatar.
        static Avatar Humanoid(GameObject root)
        {
            var bones = new Transform[HumanTrait.BoneCount];
            Transform Bone(int i)
            {
                if (bones[i] != null) return bones[i];
                var parent = HumanTrait.GetParentBone(i);
                var bone = new GameObject("Bone " + i).transform;
                bone.SetParent(parent < 0 ? root.transform : Bone(parent), false);
                bone.localPosition = Offset(HumanTrait.BoneName[i]);
                return bones[i] = bone;
            }
            var description = new HumanDescription
            {
                // Without twist distribution Unity reads the twist muscles back
                // within 1e-3 when every muscle of a limb moves.
                upperArmTwist = 0, lowerArmTwist = 0, upperLegTwist = 0, lowerLegTwist = 0,
                armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false,
            };
            var skeleton = new List<SkeletonBone> { new SkeletonBone { name = root.name, rotation = Quaternion.identity, scale = Vector3.one } };
            var human = new List<HumanBone>();
            for (var i = 0; i < bones.Length; i++)
            {
                var bone = Bone(i);
                human.Add(new HumanBone { humanName = HumanTrait.BoneName[i], boneName = bone.name, limit = new HumanLimit { useDefaultValues = true } });
            }
            foreach (var bone in bones)
                skeleton.Add(new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale });
            description.human = human.ToArray();
            description.skeleton = skeleton.ToArray();
            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            Assert.That(avatar.isValid && avatar.isHuman, "the comparison rig's Avatar");
            return avatar;
        }

        // A bone's place from its parent, facing +z with the left side at -x.
        static Vector3 Offset(string bone)
        {
            var side = bone.StartsWith("Left", StringComparison.Ordinal) ? -1f : bone.StartsWith("Right", StringComparison.Ordinal) ? 1f : 0f;
            var name = bone.Replace("Left", "").Replace("Right", "").Replace(" ", "");
            switch (name)
            {
                case "Hips": return new Vector3(0, 1, 0);
                case "Spine": case "Chest": case "UpperChest": return new Vector3(0, .12f, 0);
                case "Neck": return new Vector3(0, .1f, 0);
                case "Head": return new Vector3(0, .08f, 0);
                case "Eye": return new Vector3(side * .03f, .06f, .08f);
                case "Jaw": return new Vector3(0, -.01f, .03f);
                case "UpperLeg": return new Vector3(side * .1f, -.05f, 0);
                case "LowerLeg": case "Foot": return new Vector3(0, -.42f, 0);
                case "Toes": return new Vector3(0, -.06f, .12f);
                case "Shoulder": return new Vector3(side * .04f, .1f, 0);
                case "UpperArm": return new Vector3(side * .1f, 0, 0);
                case "LowerArm": case "Hand": return new Vector3(side * .26f, 0, 0);
                case "ThumbProximal": return new Vector3(side * .02f, -.01f, .03f);
                case "IndexProximal": return new Vector3(side * .08f, 0, .025f);
                case "MiddleProximal": return new Vector3(side * .085f, 0, .008f);
                case "RingProximal": return new Vector3(side * .08f, 0, -.01f);
                case "LittleProximal": return new Vector3(side * .07f, 0, -.028f);
                default: return new Vector3(side * .025f, 0, 0);
            }
        }

        // The curve name of a muscle in a clip (an Animator curve on the
        // root): a finger muscle Left Thumb 1 Stretched as LeftHand.Thumb.1 Stretched.
        static string MuscleCurve(int muscle)
        {
            var name = HumanTrait.MuscleName[muscle];
            foreach (var side in new[] { "Left", "Right" })
                foreach (var finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                    if (name.StartsWith(side + " " + finger + " ", StringComparison.Ordinal))
                        return side + "Hand." + finger + "." + name.Substring(side.Length + finger.Length + 2);
            return name;
        }

        // A layer's AvatarMask: the body parts and, when the data names
        // transforms, the root, inactive, and every target's transform,
        // active when the data's Nodes has it. A mask that lets none move
        // names only the root, so a transform it does not name is checked
        // to be kept off as well.
        static AvatarMask Mask(AvatarAnimatorComparison.Case c, AvatarAnimatorData.Mask mask, string name)
        {
            var unity = new AvatarMask { name = name };
            for (var part = 0; part < AvatarAnimatorData.BodyPartCount; part++)
                unity.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, mask.Humanoid.Contains(part));
            if (mask.Nodes != null)
            {
                var targets = Enumerable.Range(0, c.Data.Targets.Count).Where(t => c.Data.Targets[t].Node >= 0 && mask.Nodes.Count > 0).ToList();
                unity.transformCount = targets.Count + 1;
                unity.SetTransformPath(0, "");
                unity.SetTransformActive(0, false);
                for (var k = 0; k < targets.Count; k++)
                {
                    unity.SetTransformPath(k + 1, Path(targets[k]));
                    unity.SetTransformActive(k + 1, mask.Nodes.Contains(c.Data.Targets[targets[k]].Node));
                }
            }
            return unity;
        }

        static double Get(Animator animator, AvatarAnimatorData.Parameter parameter) =>
            parameter.Type == "int" ? animator.GetInteger(parameter.Name)
            : parameter.Type == "float" ? animator.GetFloat(parameter.Name)
            : animator.GetBool(parameter.Name) ? 1 : 0;

        static string Path(int target) => "T" + target;

        static BlendTreeType TreeType(string blendType)
        {
            switch (blendType)
            {
                case "1d": return BlendTreeType.Simple1D;
                case "2dSimpleDirectional": return BlendTreeType.SimpleDirectional2D;
                case "2dFreeformDirectional": return BlendTreeType.FreeformDirectional2D;
                case "2dFreeformCartesian": return BlendTreeType.FreeformCartesian2D;
                default: return BlendTreeType.Direct;
            }
        }

        // The controller Unity plays for the data: what AvatarAnimatorCapture
        // reads back into the same data.
        static AnimatorController Controller(AvatarAnimatorComparison.Case c, int played, string path)
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
                        : target.Property == "muscle" ? EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleCurve(target.Muscle))
                        : c.ActiveTargets.Contains(track.Target)
                        ? EditorCurveBinding.FloatCurve(Path(track.Target), typeof(GameObject), "m_IsActive")
                        : target.Property == "morph" ? EditorCurveBinding.FloatCurve(Path(track.Target), typeof(SkinnedMeshRenderer), "blendShape.Shape")
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
                tree.blendType = TreeType(motion.BlendType);
                if (motion.Parameters.Count > 0) tree.blendParameter = data.Parameters[motion.Parameters[0]].Name;
                if (motion.Parameters.Count > 1) tree.blendParameterY = data.Parameters[motion.Parameters[1]].Name;
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
            foreach (var layer in data.Controllers[played].Layers)
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

                AvatarMask mask = null;
                if (layer.Mask != null)
                {
                    mask = Mask(c, layer.Mask, layer.Name);
                    AssetDatabase.AddObjectToAsset(mask, controller);
                }
                controller.AddLayer(new AnimatorControllerLayer
                {
                    name = layer.Name, defaultWeight = (float)layer.Weight, stateMachine = machines[0], avatarMask = mask,
                    blendingMode = layer.Blending == "additive" ? AnimatorLayerBlendingMode.Additive : AnimatorLayerBlendingMode.Override,
                });
            }

            // Synced layers take the source's machines and their own motions.
            var layers = controller.layers.ToList();
            var dataLayers = data.Controllers[played].Layers;
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
