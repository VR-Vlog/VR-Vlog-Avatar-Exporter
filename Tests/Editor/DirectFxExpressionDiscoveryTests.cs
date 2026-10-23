using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The descriptor's FX is the controller the list and the neutral use,
    // even when the root Animator has none. An installed MA build marker in
    // that FX does not change a face read from its clip.
    public sealed class DirectFxExpressionDiscoveryTests
    {
        string folder;
        GameObject avatar;
        Mesh mesh;
        AnimatorController controller;
        AnimatorState neutral;

        [SetUp]
        public void SetUp()
        {
            var name = "__DirectFxDiscovery_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            avatar = new GameObject("Avatar", typeof(Animator));
            var face = new GameObject("Face", typeof(SkinnedMeshRenderer)); face.transform.SetParent(avatar.transform, false);
            mesh = BaseShapeFixture.Create(); face.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            face.GetComponent<SkinnedMeshRenderer>().SetBlendShapeWeight(0, 25);
            neutral = State(controller.layers[0].stateMachine, "Neutral", Clip("Neutral", 0));
            controller.layers[0].stateMachine.defaultState = neutral;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); AssetDatabase.DeleteAsset(folder);
        }

        AnimationClip Clip(string name, float weight)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Face", typeof(SkinnedMeshRenderer), "blendShape.Face size"),
                AnimationCurve.Constant(0, 1, weight));
            AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }
        static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = false; return state;
        }
        static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, string parameter, float value,
            AnimatorConditionMode mode = AnimatorConditionMode.Equals)
        {
            var transition = from.AddTransition(to); transition.duration = 0; transition.hasExitTime = false;
            transition.AddCondition(mode, value, parameter); return transition;
        }
        static float Weight(VrChatExpressionMenu.Entry entry) => entry.Values.Single(value => value.Shape == "Face size").Weight;

        [Test]
        public void DescriptorFxIsAuthoritativeWhenRootAnimatorControllerIsNone()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor"))
                .FirstOrDefault(value => value != null);
            if (type == null) Assert.Ignore("Install the real VRChat SDK to validate descriptor FX discovery.");
            var descriptor = avatar.AddComponent(type);
            using (var data = new SerializedObject(descriptor))
            {
                data.FindProperty("customizeAnimationLayers").boolValue = true;
                data.FindProperty("customExpressions").boolValue = false;
                var layers = data.FindProperty("baseAnimationLayers");
                layers.arraySize = 1;
                var fx = layers.GetArrayElementAtIndex(0);
                var typeProperty = fx.FindPropertyRelative("type");
                var fxIndex = Array.IndexOf(typeProperty.enumNames, "FX");
                Assert.That(fxIndex, Is.GreaterThanOrEqualTo(0), "The installed SDK must expose its FX layer kind.");
                typeProperty.enumValueIndex = fxIndex;
                fx.FindPropertyRelative("isDefault").boolValue = false;
                fx.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            // The face is read from the list; the descriptor still supplies
            // the FX controller that the neutral and the list use.
            var metadata = VrChatExpressionSampler.Analyze(avatar, null, new[] { ("Smile", (Object)Clip("Smile", 85), 0f) });
            Assert.That(metadata.Controller, Is.SameAs(controller));
            Assert.That(metadata.Entries.Single().Error, Is.Null);
            Assert.That(Weight(metadata.Entries.Single()), Is.EqualTo(85).Within(.01));
            Assert.That(avatar.GetComponent<Animator>().runtimeAnimatorController, Is.Null);
        }

        StateMachineBehaviour InstalledMaBuildMarker(bool disableInMmd)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("nadena.dev.modular_avatar.core.ModularAvatarMMDLayerControl")).FirstOrDefault(value => value != null);
            if (type == null) Assert.Ignore("Install the real Modular Avatar package to validate its generated data-only build marker.");
            var marker = (StateMachineBehaviour)ScriptableObject.CreateInstance(type);
            AssetDatabase.AddObjectToAsset(marker, controller);
            using (var data = new SerializedObject(marker))
            {
                data.FindProperty("m_DisableInMMDMode").boolValue = disableInMmd; data.ApplyModifiedPropertiesWithoutUndo();
            }
            Assert.That(ExpressionDependencies.IsInertAuthoringMarker(marker), Is.True,
                "The installed MA marker must match the exact sealed, callback-free field/property contract.");
            return marker;
        }

        // A list face read directly from its clip keeps its weight through the
        // FX permanent overrides when the installed MA marker sits beside it.
        [TestCase("machine", false)]
        [TestCase("machine", true)]
        [TestCase("state", false)]
        [TestCase("state", true)]
        public void InstalledMaDataOnlyMarkerPreservesNativeAndDirectExpressionSampling(string placement, bool disableInMmd)
        {
            controller.AddParameter("FaceChoice", AnimatorControllerParameterType.Int);
            var root = controller.layers[0].stateMachine;
            var selected = State(root, "Smile", Clip("Smile", 75)); Transition(neutral, selected, "FaceChoice", 1);
            var marker = InstalledMaBuildMarker(disableInMmd);
            if (placement == "machine") root.behaviours = new[] { marker }; else selected.behaviours = new[] { marker };
            var markerJson = EditorJsonUtility.ToJson(marker); var controllerJson = EditorJsonUtility.ToJson(controller);
            var direct = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)selected.motion, direct);
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, direct, 0, sourceState: selected);
            Assert.That(Weight(direct), Is.EqualTo(75).Within(.01));
            Assert.That(EditorJsonUtility.ToJson(marker), Is.EqualTo(markerJson));
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(controllerJson));
            Assert.That(avatar.GetComponentInChildren<SkinnedMeshRenderer>().GetBlendShapeWeight(0), Is.EqualTo(25));
        }

        [Test]
        public void InstalledMaMarkerCannotMakeAnUnknownCallbackSafe()
        {
            controller.AddParameter("FaceChoice", AnimatorControllerParameterType.Int);
            var root = controller.layers[0].stateMachine;
            var selected = State(root, "Smile", Clip("Smile", 75)); Transition(neutral, selected, "FaceChoice", 1);
            var marker = InstalledMaBuildMarker(false);
            var callback = selected.AddStateMachineBehaviour<UnknownStateCallbackProbe>();
            Assert.That(ExpressionDependencies.IsInertAuthoringMarker(callback), Is.False);
            root.behaviours = new[] { marker };
            var direct = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)selected.motion, direct);
            Assert.Throws<InvalidOperationException>(() => VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, direct, 0, sourceState: selected));
        }
    }
}
