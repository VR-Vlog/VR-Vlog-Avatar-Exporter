using System;
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
    public sealed class SelectedLayerControlTests
    {
        private string folder;
        private GameObject avatar;
        private Mesh mesh;
        private SkinnedMeshRenderer skin;
        private Component descriptor;
        private AnimatorController controller;
        private AnimatorState command, neutral, selected;
        private const int TargetLayer = 4;

        private static Type Sdk(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);

        [SetUp]
        public void SetUp()
        {
            if (Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") == null || Sdk("VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl") == null)
                Assert.Ignore("Install the real VRChat SDK.");
            var name = "__SelectedLayerControl_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            avatar = new GameObject("Avatar", typeof(Animator));
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            skin = body.GetComponent<SkinnedMeshRenderer>();
            mesh = new Mesh { name = "Unchanged authored body" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame("Body size", 100, Enumerable.Repeat(Vector3.right * .02f, 3).ToArray(), null, null);
            skin.sharedMesh = mesh; skin.SetBlendShapeWeight(0, 17);
            controller = Graph("FX", new[] { "Body" }, "Body size", 1, out command, out neutral, out selected);
            descriptor = avatar.AddComponent(Sdk("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")); SetFx(descriptor, controller);
        }

        [TearDown]
        public void TearDown()
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            if (mesh != null) Object.DestroyImmediate(mesh);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            var state = machine.AddState(name); state.motion = clip; state.writeDefaultValues = false;
            if (machine.states.Length == 1) machine.defaultState = state; return state;
        }

        private static AnimatorStateMachine Layer(AnimatorController fx, string name)
        {
            fx.AddLayer(name); var layers = fx.layers; layers[layers.Length - 1].defaultWeight = 1; fx.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        private static void To(AnimatorState from, AnimatorState to, float value)
        {
            var transition = from.AddTransition(to); transition.hasExitTime = false; transition.duration = 0;
            transition.AddCondition(AnimatorConditionMode.Equals, value, "Menu");
        }

        private static AnimationClip Clip(AnimatorController fx, string name, IEnumerable<string> paths = null, string shape = "Body size", float value = 0)
        {
            var clip = new AnimationClip { name = name };
            foreach (var path in paths ?? Enumerable.Empty<string>())
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0, 1, value));
            AssetDatabase.AddObjectToAsset(clip, fx); return clip;
        }

        private AnimatorController Graph(string name, string[] paths, string shape, float weight,
            out AnimatorState control, out AnimatorState rest, out AnimatorState endpoint, float baseWeight = 30)
        {
            var fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/" + name + ".controller");
            fx.AddParameter("Menu", AnimatorControllerParameterType.Int);
            State(fx.layers[0].stateMachine, "Base rest", Clip(fx, "Base rest", paths, shape, baseWeight));
            control = State(Layer(fx, "SDK command source"), "Command", Clip(fx, "Empty command motion"));
            State(Layer(fx, "Unrelated slot 2"), "Empty", Clip(fx, "Empty 2"));
            State(Layer(fx, "Unrelated slot 3"), "Empty", Clip(fx, "Empty 3"));
            var target = Layer(fx, "Controlled face at physical index 4");
            rest = State(target, "Neutral", Clip(fx, "Neutral", paths, shape, 60));
            endpoint = State(target, "Selected endpoint", Clip(fx, "Selected endpoint", paths, shape, 80));
            To(rest, endpoint, 1); To(endpoint, rest, 0);
            var layers = fx.layers; layers[TargetLayer].defaultWeight = weight; fx.layers = layers; return fx;
        }

        private static StateMachineBehaviour Control(AnimatorState state, float goal, int target = TargetLayer, float duration = 0, string playable = "FX")
            => Configure(state.AddStateMachineBehaviour(Sdk("VRC.SDK3.Avatars.Components.VRCAnimatorLayerControl")), goal, target, duration, playable);

        private static StateMachineBehaviour Configure(StateMachineBehaviour value, float goal, int target, float duration, string playable)
        {
            Assert.That(value, Is.Not.Null);
            using var data = new SerializedObject(value);
            var property = data.FindProperty("playable"); property.enumValueIndex = Array.IndexOf(property.enumNames, playable);
            data.FindProperty("layer").intValue = target; data.FindProperty("goalWeight").floatValue = goal;
            data.FindProperty("blendDuration").floatValue = duration; data.ApplyModifiedPropertiesWithoutUndo(); return value;
        }

        private static void SetFx(Component target, AnimatorController fx)
        {
            using var data = new SerializedObject(target);
            data.FindProperty("customizeAnimationLayers").boolValue = true;
            var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
            var layer = list.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
            type.enumValueIndex = Array.IndexOf(type.enumNames, "FX"); layer.FindPropertyRelative("isDefault").boolValue = false;
            layer.FindPropertyRelative("animatorController").objectReferenceValue = fx; data.ApplyModifiedPropertiesWithoutUndo();
        }

        private Dictionary<Object, string> Before() => AssetDatabase.LoadAllAssetsAtPath(folder + "/FX.controller")
            .Concat(new Object[] { mesh, descriptor }).ToDictionary(value => value, value => EditorJsonUtility.ToJson(value));

        private void Unchanged(Dictionary<Object, string> before)
        {
            foreach (var pair in before) Assert.That(EditorJsonUtility.ToJson(pair.Key), Is.EqualTo(pair.Value), pair.Key.name);
            Assert.That(skin.sharedMesh, Is.SameAs(mesh)); Assert.That(skin.GetBlendShapeWeight(0), Is.EqualTo(17));
        }

        // This graph contains no SDK callback or exporter adapter. Apply the
        // documented final layer weight through Unity's native API instead.
        private float Native(AnimatorController reference, float goal, int target = TargetLayer, float menu = 1, int shapeIndex = 0)
        {
            var copy = Object.Instantiate(avatar); var graph = PlayableGraph.Create("SDK layer weight native reference");
            try
            {
                foreach (var behaviour in copy.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var animator = copy.GetComponent<Animator>(); animator.enabled = true; animator.runtimeAnimatorController = null;
                animator.applyRootMotion = false; animator.fireEvents = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, reference);
                for (var index = 0; index < reference.layers.Length; index++) playable.SetLayerWeight(index, index == 0 ? 1 : reference.layers[index].defaultWeight);
                playable.SetLayerWeight(target, target == 0 ? 1 : goal); playable.SetInteger("Menu", Mathf.RoundToInt(menu));
                AnimationPlayableOutput.Create(graph, "Native reference", animator).SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate(0);
                for (var frame = 0; frame < 240; frame++) graph.Evaluate(1f / 60);
                return copy.transform.Find("Body").GetComponent<SkinnedMeshRenderer>().GetBlendShapeWeight(shapeIndex);
            }
            finally { if (graph.IsValid()) graph.Destroy(); Object.DestroyImmediate(copy); }
        }

        [TestCase("native", 0f)] [TestCase("native", .5f)]
        [TestCase("inactive", .5f)]
        [TestCase("standalone", .5f)] [TestCase("standalone", 1f)]
        public void DirectClipControlsPreserveNativeSlotAndStandaloneIndexMapping(string mode, float goal)
        {
            Control(command, goal);
            var endpoint = (AnimationClip)selected.motion;
            var reference = Graph("Direct reference", new[] { "Body" }, "Body size", 1, out _, out var referenceRest, out var referenceSelected,
                baseWeight: mode == "standalone" ? 80 : 30);
            if (mode == "inactive")
            { var layers = controller.layers; layers[TargetLayer].defaultWeight = 0; controller.layers = layers; }
            if (mode == "standalone")
            {
                controller.layers[0].stateMachine.defaultState.motion = Clip(controller, "Empty original base");
                neutral.transitions = Array.Empty<AnimatorStateTransition>(); controller.layers[TargetLayer].stateMachine.RemoveState(selected);
                referenceRest.transitions = Array.Empty<AnimatorStateTransition>(); reference.layers[TargetLayer].stateMachine.RemoveState(referenceSelected);
            }
            var expected = Native(reference, goal, menu: mode == "standalone" ? 0 : 1);
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, endpoint, entry);
            var before = Before(); var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true });
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry,
                mode == "standalone" ? (int?)null : TargetLayer, metadata: metadata, sourceState: mode == "standalone" ? null : selected);
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(expected).Within(.01));
            Unchanged(before);
        }

        [TestCase(false)] [TestCase(true)]
        public void InactiveDirectClipDistinguishesDormantEnableFromReachedExplicitDisable(bool dormantEnable)
        {
            var layers = controller.layers; layers[TargetLayer].defaultWeight = 0; controller.layers = layers;
            if (dormantEnable)
            {
                controller.AddParameter(new AnimatorControllerParameter { name = "AFK", type = AnimatorControllerParameterType.Bool, defaultBool = true });
                var dormant = State(controller.layers[1].stateMachine, "Dormant enable", Clip(controller, "Dormant enable"));
                Control(dormant, 1);
                var transition = command.AddTransition(dormant); transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.If, 0, "AFK");
            }
            else Control(command, 0);
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)selected.motion, entry);
            var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true }); var before = Before();
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, TargetLayer, metadata: metadata, sourceState: selected);
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(dormantEnable ? 80 : 30).Within(.01),
                dormantEnable ? "A normally unreachable enable command cannot erase the legacy inactive authored clip." :
                    "A reached SDK disable is an explicit effect: preserve the native underlay instead of exposing the inactive candidate.");
            Unchanged(before);
        }

        [Test]
        public void StandaloneClipKeepsOriginalSdkBaseLayerAtUnitWeightAfterIndexRemap()
        {
            foreach (var state in controller.layers[TargetLayer].stateMachine.states) state.state.motion = Clip(controller, "Unwritten target");
            Control(command, 0, target: 0);
            var endpoint = Clip(controller, "Standalone candidate", new[] { "Body" }, value: 80);
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, endpoint, entry);
            var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true }); var before = Before();
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, metadata: metadata);
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(30).Within(.01),
                "The original SDK base is moved to private slot 1 but must remain fully weighted above the standalone 80 clip.");
            Unchanged(before);
        }

        [Test]
        public void DirectBaseClipWithoutStateProvenanceCannotDiscardItsLayerControl()
        {
            var candidate = State(controller.layers[0].stateMachine, "Base callback candidate",
                Clip(controller, "Base callback candidate", new[] { "Body" }, value: 80));
            Control(candidate, 1);
            var layers = controller.layers; layers[TargetLayer].defaultWeight = 0; controller.layers = layers;
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)candidate.motion, entry);
            var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true }); var before = Before();
            var error = Assert.Throws<InvalidOperationException>(() => VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, 0, metadata: metadata));
            Assert.That(error.Message, Does.Contain(candidate.name));
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(80)); Unchanged(before);
        }

        [Test]
        public void InactiveSelectedStateKeepsItsOwnWeightButExecutesAControlOfAnotherLayer()
        {
            mesh.AddBlendShapeFrame("Auxiliary", 100, Enumerable.Repeat(Vector3.up * .03f, mesh.vertexCount).ToArray(), null, null);
            skin.SetBlendShapeWeight(1, 7);
            var auxiliary = EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Auxiliary");
            AnimationUtility.SetEditorCurve((AnimationClip)selected.motion, auxiliary, AnimationCurve.Constant(0, 1, 50));
            controller.layers[2].stateMachine.defaultState.motion = Clip(controller, "Controlled auxiliary", new[] { "Body" }, "Auxiliary", 90);
            var layers = controller.layers; layers[2].defaultWeight = 0; layers[TargetLayer].defaultWeight = 0; controller.layers = layers;
            var sdkControl = Control(selected, 1, target: 2);
            var reference = Graph("Inactive other-target reference", new[] { "Body" }, "Body size", 0, out _, out _, out var referenceSelected);
            AnimationUtility.SetEditorCurve((AnimationClip)referenceSelected.motion, auxiliary, AnimationCurve.Constant(0, 1, 50));
            reference.layers[2].stateMachine.defaultState.motion = Clip(reference, "Native auxiliary", new[] { "Body" }, "Auxiliary", 90);
            var nativeBody = Native(reference, 1, target: 2);
            var nativeAuxiliary = Native(reference, 1, target: 2, shapeIndex: 1);
            Assert.That(nativeBody, Is.EqualTo(30).Within(.01)); Assert.That(nativeAuxiliary, Is.EqualTo(90).Within(.01));
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)selected.motion, entry);
            var before = Before(); var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true });
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, TargetLayer, metadata: metadata, sourceState: selected);
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(nativeBody).Within(.01),
                "Entering an inactive source state must not apply its authored Body=80 pose.");
            Assert.That(entry.Values.Single(value => value.Shape == "Auxiliary").Weight, Is.EqualTo(nativeAuxiliary).Within(.01),
                "The same zero-weight state's SDK callback still enables the other native layer.");
            Assert.That(controller.layers[TargetLayer].defaultWeight, Is.Zero); Assert.That(selected.behaviours, Does.Contain(sdkControl));
            Assert.That(skin.GetBlendShapeWeight(1), Is.EqualTo(7)); Unchanged(before);
        }

        [TestCase(false)] [TestCase(true)]
        public void SelectedCallbackRetainsItsUpperNonEquivalentSelectorTarget(bool alternate)
        {
            command.motion = Clip(controller, "Lower selected candidate", new[] { "Body" }, value: 90);
            var layers = controller.layers; layers[TargetLayer].defaultWeight = 0; controller.layers = layers;
            var sdkControl = Control(command, 1);
            var reference = Graph("Upper selector native reference", new[] { "Body" }, "Body size", 0,
                out var referenceCommand, out _, out _);
            referenceCommand.motion = Clip(reference, "Native lower selected candidate", new[] { "Body" }, value: 90);
            var expected = Native(reference, 1, menu: alternate ? 1 : 0);
            Assert.That(expected, Is.EqualTo(alternate ? 80 : 60).Within(.01));

            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, (AnimationClip)command.motion, entry);
            var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true });
            if (alternate) metadata.Defaults["Menu"] = 1;
            var before = Before();
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, 1, metadata: metadata, sourceState: command);
            Assert.That(entry.Values.Single(value => value.Shape == "Body size").Weight, Is.EqualTo(expected).Within(.01),
                "An explicitly controlled upper selector must retain its state graph instead of being erased as a competing direct expression.");
            Assert.That(controller.layers[TargetLayer].defaultWeight, Is.Zero);
            Assert.That(controller.layers[TargetLayer].stateMachine.states.Length, Is.EqualTo(2));
            Assert.That(command.behaviours, Does.Contain(sdkControl)); Unchanged(before);
        }
    }
}
