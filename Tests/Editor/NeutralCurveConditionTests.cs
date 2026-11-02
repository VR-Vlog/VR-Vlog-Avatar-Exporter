using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class NeutralCurveConditionTests
    {
        string folder;
        GameObject avatar;
        Mesh mesh;
        AnimatorController controller;

        [SetUp]
        public void SetUp()
        {
            var descriptorType = OptionalTools.Require("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", "Install the real VRChat SDK.");
            folder = TempAssetFolder.Create("__NeutralCurveConditions_");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            avatar = new GameObject("Avatar", typeof(Animator));
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Opening", 100, Enumerable.Repeat(Vector3.up, 3).ToArray(), null, null);
            body.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            var open = Clip("Opening", "Body", typeof(SkinnedMeshRenderer), "blendShape.Opening", AnimationCurve.Constant(0, 1, 100));
            FxGraph.StartState(controller.layers[0].stateMachine, "Neutral face", open, false);
            var descriptor = avatar.AddComponent(descriptorType);
            VrcAvatar.SetFx(descriptor, controller);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        AnimationClip Clip(string name, string path, Type type, string property, AnimationCurve curve)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property), curve);
            AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }

        void Relay(AnimationCurve curve)
        {
            controller.AddParameter("Relay value", AnimatorControllerParameterType.Float);
            var clip = Clip("Relay value", "", typeof(Animator), "Relay value", curve);
            var machine = FxGraph.Layer(controller, "Parameter lifecycle");
            var first = FxGraph.StartState(machine, "Startup", clip, true);
            var second = FxGraph.StartState(machine, "Different state", clip, true);
            var transition = first.AddTransition(second);
            transition.hasExitTime = true; transition.exitTime = .75f; transition.duration = 0;
            transition.AddCondition(AnimatorConditionMode.Less, .5f, "Relay value");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeConstantParameterRelayCanProveItsTimedExitFalseWithoutNames(bool staticWriteDefaultsLayer)
        {
            Relay(AnimationCurve.Constant(0, 1, 1));
            if (staticWriteDefaultsLayer)
            {
                var machine = FxGraph.Layer(controller, "Static empty layer");
                FxGraph.StartState(machine, "Held", new AnimationClip(), true);
                AssetDatabase.AddObjectToAsset(machine.defaultState.motion, controller);
            }
            var values = NeutralShapeSampler.Sample(avatar);
            Assert.That(values.Single(value => value.Shape == "Opening").Weight, Is.EqualTo(100).Within(.01));
            Assert.That(controller.parameters.Single(value => value.name == "Relay value").defaultFloat, Is.Zero,
                "The post-native proof must not overwrite the authoring startup default.");
        }

        [Test]
        public void DynamicParameterCurveKeepsPreparedNeutralWhenItsFutureTimedExitIsUnresolved()
        {
            Relay(AnimationCurve.Linear(0, 1, 10, 0));
            AssertPreparedFallback();
        }

        [Test]
        public void CompetingParameterDriverPreventsTheRelayProof()
        {
            Relay(AnimationCurve.Constant(0, 1, 1));
            var machine = FxGraph.Layer(controller, "Competing writer");
            var state = FxGraph.StartState(machine, "Set relay", null, false);
            ParameterDriverExpressionTests.Driver(state, ParameterDriverExpressionTests.Op("Set", "Relay value", 0));
            AssertPreparedFallback();
        }

        [Test]
        public void FractionalRelayWeightNearExitThresholdCannotCountAsAFullConstantWriter()
        {
            Relay(AnimationCurve.Constant(0, 1, 1));
            var layers = controller.layers;
            layers[1].defaultWeight = .999995f; controller.layers = layers;
            var machine = layers[1].stateMachine;
            var first = machine.defaultState;
            var second = machine.states.Select(value => value.state).Single(value => value != first);
            var conditions = first.transitions.Single().conditions;
            conditions[0].threshold = .999997f; first.transitions.Single().conditions = conditions;
            var back = second.AddTransition(first);
            back.hasExitTime = true; back.exitTime = .75f; back.duration = 0;
            back.AddCondition(AnimatorConditionMode.Less, .999997f, "Relay value");
            AssertPreparedFallback();
        }

        void AssertPreparedFallback()
        {
            var skin = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            skin.SetBlendShapeWeight(0, 27);
            var before = EditorJsonUtility.ToJson(controller);
            var metadata = VrChatExpressionMenu.Read(avatar, new VrChatMenuImportPolicy { SkipAll = true });
            var context = FixedExpressionContext.Create(controller, metadata.Defaults, metadata);
            var dependencies = ExpressionDependencies.AnalyzeNeutral(controller, new HashSet<EditorCurveBinding> {
                EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Opening") }, null, metadata,
                fixedContext: context, preserveCommittedMorphs: true);
            Assert.Throws<NeutralShapeSamplingException>(() => VrChatExpressionSampler.SampleNeutral(
                avatar, controller, dependencies, metadata, null, context));
            var warnings = new List<string>();
            var values = NeutralShapeSampler.Sample(avatar, warnings: warnings);
            Assert.That(values, Is.Empty, "An unresolved relay must not invent a stable FX opening value.");
            NeutralShapeSnapshot.Apply(avatar, values);
            Assert.That(skin.GetBlendShapeWeight(0), Is.EqualTo(27));
            Assert.That(warnings.Any(value => value.Contains("Opening") && value.Contains("FX")), Is.True);
            Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(before));
        }
    }
}
