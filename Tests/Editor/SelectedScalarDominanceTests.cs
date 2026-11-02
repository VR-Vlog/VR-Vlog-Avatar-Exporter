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
    public sealed class SelectedScalarDominanceTests
    {
        string folder;
        GameObject avatar;
        Mesh mesh;
        AnimatorController controller;
        AnimatorState lower, selected;
        AnimationClip moving, face;

        [SetUp]
        public void SetUp()
        {
            folder = TempAssetFolder.Create("__ScalarDominance_");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            avatar = new GameObject("Selected scalar dominance", typeof(Animator));
            var skin = new GameObject("Body", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>(); skin.transform.SetParent(avatar.transform, false);
            mesh = new Mesh { name = "Prepared native underlay" }; mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (var shape in new[] { "Face", "Rest" }) mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.up * .01f, 3).ToArray(), null, null);
            skin.sharedMesh = mesh; skin.SetBlendShapeWeight(0, 17); skin.SetBlendShapeWeight(1, 23);
            State(0, "Independent permanent rest", FxGraph.CurveClip(controller, "Rest", ("Rest", AnimationCurve.Constant(0, 1, 12))));
            AddLayer("Moving lower face");
            moving = FxGraph.CurveClip(controller, "Delayed mouth movement", ("Face", new AnimationCurve(new Keyframe(0, 0), new Keyframe(10, 0), new Keyframe(10.5f, 5), new Keyframe(12, 0))));
            lower = State(1, "Moving", moving);
            AddLayer("Selected upper face"); face = FxGraph.CurveClip(controller, "Held face", ("Face", AnimationCurve.Constant(0, 1, 75))); selected = State(2, "Selected", face);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); if (folder != null) AssetDatabase.DeleteAsset(folder);
        }
        void AddLayer(string name) => FxGraph.Layer(controller, name);
        AnimatorState State(int layer, string name, AnimationClip clip) => FxGraph.StartState(controller.layers[layer].stateMachine, name, clip);

        VrChatExpressionMenu.Entry Hold()
        {
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, face, entry);
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, 2, false,
                metadata: new VrChatExpressionMenu.Source { Controller = controller }, sourceState: selected);
            return entry;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExactUpperScalarDominanceRetainsIndependentRestAndSelectedCallbacks(bool callback)
        {
            if (callback)
            {
                controller.AddParameter("Choice", AnimatorControllerParameterType.Bool);
                ParameterDriverExpressionTests.Driver(selected, ParameterDriverExpressionTests.Op("Set", "Choice", 1));
                AddLayer("Selected callback response");
                var normal = State(3, "Normal", FxGraph.CurveClip(controller, "Normal response", ("Face", AnimationCurve.Constant(0, 1, 75))));
                var response = State(3, "Response", FxGraph.CurveClip(controller, "Callback response", ("Face", AnimationCurve.Constant(0, 1, 20))));
                var transition = normal.AddTransition(response); transition.hasExitTime = false; transition.duration = 0; transition.AddCondition(AnimatorConditionMode.If, 0, "Choice");
            }
            var sourceBefore = ExportSourceFingerprint.Compute(avatar); var controllerBefore = EditorJsonUtility.ToJson(controller);
            if (!callback)
            {
                // Independent full native graph: the lower mouth keeps moving
                // while the held upper face and the unrelated rest stay exact.
                using var native = new NativeFxPlayback(avatar, controller);
                var playable = native.Playable;
                native.Start(); native.Run(650);
                var skin = native.Skin("Body");
                Assert.That(playable.GetCurrentAnimatorStateInfo(1).normalizedTime, Is.GreaterThan(.85));
                Assert.That(skin.GetBlendShapeWeight(0), Is.EqualTo(75).Within(.01)); Assert.That(skin.GetBlendShapeWeight(1), Is.EqualTo(12).Within(.01));
            }
            var entry = Hold();
            Assert.That(entry.Values.Single(value => value.Shape == "Face").Weight, Is.EqualTo(callback ? 20 : 75).Within(.01));
            Assert.That(entry.Values.Single(value => value.Shape == "Rest").Weight, Is.EqualTo(12).Within(.01));
            Assert.That(ExportSourceFingerprint.Compute(avatar), Is.EqualTo(sourceBefore)); Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(controllerBefore));
        }

        [TestCase("fractional")]
        [TestCase("masked")]
        [TestCase("additive")]
        [TestCase("undominated moving rest")]
        public void ScalarDominanceNeverPreservesAnUndominatedMovingChannel(string kind)
        {
            var layers = controller.layers;
            if (kind == "fractional") layers[2].defaultWeight = .5f;
            else if (kind == "additive") layers[2].blendingMode = AnimatorLayerBlendingMode.Additive;
            else if (kind == "masked")
            {
                var mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, folder + "/Mask.mask");
                mask.AddTransformPath(avatar.transform); layers[2].avatarMask = mask;
            }
            else AnimationUtility.SetEditorCurve(moving, FxGraph.Binding("Rest"), AnimationCurve.Linear(0, 0, 12, 50));
            controller.layers = layers;
            Assert.Catch<InvalidOperationException>(() => Hold());
        }

        [TestCase("curve")]
        [TestCase("event")]
        [TestCase("unknown callback")]
        [TestCase("malformed needed callback")]
        public void ExactDominanceCannotConcealInvalidPrograms(string kind)
        {
            if (kind == "curve")
            {
                AnimationUtility.SetEditorCurve(moving, FxGraph.Binding("Face"), new AnimationCurve(new Keyframe(0, 0, 0, float.NaN), new Keyframe(12, 5, 0, 0)));
                AnimationUtility.SetEditorCurve(moving, FxGraph.Binding("Rest"), AnimationCurve.Constant(0, 12, 0));
                Assert.That(float.IsNaN(AnimationUtility.GetEditorCurve(moving, FxGraph.Binding("Face")).keys[0].outTangent), Is.True,
                    "The native fixture must retain an invalid active interpolation tangent before testing dominance.");
                Assert.That(VrChatExpressionSampler.IsConstant(AnimationUtility.GetEditorCurve(moving, FxGraph.Binding("Face"))), Is.False);
            }
            else if (kind == "event") AnimationUtility.SetAnimationEvents(moving, new[] { new AnimationEvent { time = 10.5f, functionName = "UnknownEvent" } });
            else if (kind == "unknown callback") Assert.That(lower.AddStateMachineBehaviour<UnknownStateCallbackProbe>(), Is.Not.Null);
            else
            {
                controller.AddParameter("Clock", AnimatorControllerParameterType.Float); lower.timeParameterActive = true; lower.timeParameter = "Clock";
                ParameterDriverExpressionTests.Driver(lower, ParameterDriverExpressionTests.Op("Set", "Clock", float.NaN));
            }
            var before = EditorJsonUtility.ToJson(controller); Assert.Catch<InvalidOperationException>(() => Hold()); Assert.That(EditorJsonUtility.ToJson(controller), Is.EqualTo(before));
        }
    }
}
