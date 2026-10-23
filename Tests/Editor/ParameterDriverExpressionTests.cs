using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ParameterDriverExpressionTests
    {
        private string folder;
        private GameObject avatar;
        private Mesh mesh;
        private AnimatorController controller;
        private VrChatExpressionMenu.Source metadata;

        [SetUp]
        public void SetUp()
        {
            var name = "__ExpressionDrivers_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            controller.AddParameter("Menu", AnimatorControllerParameterType.Int);
            controller.AddParameter("Face", AnimatorControllerParameterType.Int);
            metadata = new VrChatExpressionMenu.Source { Controller = controller };
            metadata.Defaults["IsLocal"] = 1;
            avatar = new GameObject("Avatar", typeof(Animator));
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            mesh = BaseShapeFixture.Create(); body.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            body.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 25);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); AssetDatabase.DeleteAsset(folder);
        }

        private AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip motion = null)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = false; return state;
        }

        private AnimatorStateMachine Layer(string name)
        {
            controller.AddLayer(name); var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1; controller.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        private static void Transition(AnimatorState from, AnimatorState to, string parameter, float value, AnimatorConditionMode mode = AnimatorConditionMode.Equals)
        {
            var t = from.AddTransition(to); t.hasExitTime = false; t.duration = 0; t.canTransitionToSelf = true;
            t.AddCondition(mode, value, parameter);
        }

        private AnimationClip Clip(string name, float value, bool missing = false)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), AnimationCurve.Constant(0, 1, value));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink"), AnimationCurve.Constant(0, 1, 0));
            if (missing) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.extra_cheek5"), AnimationCurve.Constant(0, 1, 100));
            AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }

        private AnimatorState Gate()
        {
            var machine = controller.layers[0].stateMachine;
            var idle = State(machine, "Idle"); machine.defaultState = idle;
            var gate = State(machine, "Select face"); Transition(idle, gate, "Menu", 1); return gate;
        }

        internal static VrChatParameterDriver.Operation Op(string kind, string destination, float value = 0, string source = null) =>
            new VrChatParameterDriver.Operation { Kind = kind, Destination = destination, Value = value, Source = source };

        internal static StateMachineBehaviour Driver(AnimatorState state, params VrChatParameterDriver.Operation[] operations)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Avatars.Components.VRCAvatarParameterDriver")).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("Install the real VRChat SDK to run state-entry integration tests.");
            var driver = state.AddStateMachineBehaviour(type);
            using (var data = new SerializedObject(driver))
            {
                var parameters = data.FindProperty("parameters"); parameters.arraySize = operations.Length;
                for (var i = 0; i < operations.Length; i++)
                {
                    var item = parameters.GetArrayElementAtIndex(i); var op = operations[i];
                    item.FindPropertyRelative("name").stringValue = op.Destination;
                    var kind = item.FindPropertyRelative("type"); kind.enumValueIndex = Array.IndexOf(kind.enumNames, op.Kind);
                    item.FindPropertyRelative("value").floatValue = op.Value;
                    item.FindPropertyRelative("source").stringValue = op.Source ?? "";
                    item.FindPropertyRelative("convertRange").boolValue = op.ConvertRange;
                    item.FindPropertyRelative("sourceMin").floatValue = op.SourceMin;
                    item.FindPropertyRelative("sourceMax").floatValue = op.SourceMax;
                    item.FindPropertyRelative("destMin").floatValue = op.DestinationMin;
                    item.FindPropertyRelative("destMax").floatValue = op.DestinationMax;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            return driver;
        }

        internal static StateMachineBehaviour SdkBehaviour(AnimatorState state, string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("Install the real VRChat SDK to run playable-control integration tests.");
            Assert.That(type.IsAbstract, Is.False, "Only concrete SDK behaviours can be attached: " + name);
            var behaviour = state.AddStateMachineBehaviour(type);
            Assert.That(behaviour, Is.Not.Null, "The SDK behaviour could not be attached: " + name);
            return behaviour;
        }

        private static StateMachineBehaviour PlayableControl(AnimatorState state, string target, float weight = .5f, float duration = 2,
            string type = "VRC.SDK3.Avatars.Components.VRCPlayableLayerControl")
        {
            var control = SdkBehaviour(state, type);
            using (var data = new SerializedObject(control))
            {
                var layer = data.FindProperty("layer");
                layer.enumValueIndex = Array.IndexOf(layer.enumNames, target);
                data.FindProperty("goalWeight").floatValue = weight;
                data.FindProperty("blendDuration").floatValue = duration;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            return control;
        }

        [TestCase("Action", "VRC.SDK3.Avatars.Components.VRCPlayableLayerControl")]
        [TestCase("Gesture", "VRC.SDK3.Avatars.Components.VRCPlayableLayerControl")]
        [TestCase("Additive", "VRC.SDK3.Avatars.Components.VRCPlayableLayerControl")]
        public void BodyPlayableControlAndTrackedHandDoNotBlockFixedFacialSampler(string target, string type)
        {
            var face = Gate(); face.motion = Clip("Menu face", 75);
            var dance = Layer("Dance"); var idle = State(dance, "0"); dance.defaultState = idle;
            var selected = State(dance, "1"); Transition(idle, selected, "Menu", 1);
            var off = PlayableControl(idle, target, 0, type: type);
            var on = PlayableControl(selected, target, 1, type: type);
            Assert.That(VrChatParameterDriver.IsNonFxPlayableControl(off), Is.True);
            Assert.That(VrChatParameterDriver.IsNonFxPlayableControl(on), Is.True);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void LocomotionControlDoesNotBlockFixedFacialSampling(bool disabled, bool otherPlayable)
        {
            var face = Gate(); face.motion = Clip("Menu face", 75);
            var state = face;
            if (otherPlayable)
            {
                var other = AnimatorController.CreateAnimatorControllerAtPath(folder + "/Other.controller");
                state = State(other.layers[0].stateMachine, "Locomotion control"); other.layers[0].stateMachine.defaultState = state;
                metadata.OtherControllers.Add(other);
            }
            var control = SdkBehaviour(state, "VRC.SDK3.Avatars.Components.VRCAnimatorLocomotionControl");
            using (var data = new SerializedObject(control))
            {
                data.FindProperty("disableLocomotion").boolValue = disabled;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(VrChatParameterDriver.IsLocomotionControl(control), Is.True);
        }
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void TemporaryPoseSpaceDoesNotBlockFixedFacialSampling(bool enter, bool fixedDelay)
        {
            var face = Gate(); face.motion = Clip("Menu face", 75);
            var control = SdkBehaviour(face, "VRC.SDK3.Avatars.Components.VRCAnimatorTemporaryPoseSpace");
            using (var data = new SerializedObject(control))
            {
                data.FindProperty("enterPoseSpace").boolValue = enter;
                data.FindProperty("fixedDelay").boolValue = fixedDelay;
                data.FindProperty("delayTime").floatValue = .5f;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(VrChatParameterDriver.IsTemporaryPoseSpace(control), Is.True);
        }

        [TestCase(-1)]
        [TestCase(float.NaN)]
        public void MalformedTemporaryPoseSpaceIsNotIgnored(float delay)
        {
            var face = Gate(); face.motion = Clip("Menu face", 75);
            var control = SdkBehaviour(face, "VRC.SDK3.Avatars.Components.VRCAnimatorTemporaryPoseSpace");
            using (var data = new SerializedObject(control))
            {
                data.FindProperty("delayTime").floatValue = delay;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(VrChatParameterDriver.IsTemporaryPoseSpace(control), Is.False);
        }
        [TestCase(-.1f, 0)]
        [TestCase(1.1f, 0)]
        [TestCase(float.NaN, 0)]
        [TestCase(float.PositiveInfinity, 0)]
        [TestCase(.5f, -1)]
        [TestCase(.5f, float.NaN)]
        [TestCase(.5f, float.PositiveInfinity)]
        public void MalformedBodyPlayableControlIsNotIgnored(float weight, float duration)
        {
            var gate = Gate(); gate.motion = Clip("Menu face", 75);
            var control = PlayableControl(gate, "Action", weight, duration);
            Assert.That(VrChatParameterDriver.IsNonFxPlayableControl(control), Is.False);
        }

        [Test]
        public void UnknownPlayableTargetIsNotIgnored()
        {
            var gate = Gate(); gate.motion = Clip("Menu face", 75);
            var control = PlayableControl(gate, "Action");
            using (var data = new SerializedObject(control))
            {
                data.FindProperty("layer").intValue = int.MaxValue;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(VrChatParameterDriver.IsNonFxPlayableControl(control), Is.False);
        }

        [Test]
        public void BodyControlDoesNotMakeAnExcludedOnlyLayerLookLikeAFace()
        {
            var gate = Gate(); gate.motion = Clip("Menu face", 75);
            var machine = Layer("Excluded decoration");
            var clip = new AnimationClip { name = "Decoration" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Decoration", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            AssetDatabase.AddObjectToAsset(clip, controller);
            var state = State(machine, "Decoration", clip); machine.defaultState = state;
            PlayableControl(state, "Action");
            var excluded = VrChatExpressionSampler.FindExcludedLayers(controller, controller, path => path == "Decoration");
            Assert.That(excluded, Does.Contain(controller.layers.Length - 1));
        }

        [Test]
        public void SupportedNumbersClampOnlyExpressionParametersAndFloorCopiedIntegers()
        {
            var types = new Dictionary<string, AnimatorControllerParameterType>
            {
                ["Synced"] = AnimatorControllerParameterType.Float, ["Local"] = AnimatorControllerParameterType.Float,
                ["Int"] = AnimatorControllerParameterType.Int, ["Bool"] = AnimatorControllerParameterType.Bool
            };
            var values = types.Keys.ToDictionary(n => n, _ => 0.0);
            var program = new VrChatParameterDriver.Program { Location = "Test" };
            program.Operations.AddRange(new[] { Op("Set", "Synced", 4), Op("Set", "Local", -1.2f), Op("Copy", "Int", source: "Local"), Op("Copy", "Bool", source: "Local") });
            VrChatParameterDriver.Execute(program, types, new HashSet<string> { "Synced" }, new HashSet<string>(types.Keys), true, n => values[n], (n, v) => values[n] = v);
            Assert.That(values["Synced"], Is.EqualTo(1)); Assert.That(values["Local"], Is.EqualTo(-1.2).Within(.0001));
            Assert.That(values["Int"], Is.EqualTo(-2)); Assert.That(values["Bool"], Is.EqualTo(1));
        }
    }
}
