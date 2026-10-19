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
    // Menu expressions that are still moving after the warm-up are decided
    // from the Animator's timed transitions and the clip's final key.
    public sealed class ExpressionSettlementTests
    {
        private string folder;
        private GameObject avatar;
        private Mesh mesh;
        private AnimatorController controller;
        private VrChatExpressionMenu.Source metadata;

        [SetUp]
        public void SetUp()
        {
            var name = "__ExpressionSettlement_" + Guid.NewGuid().ToString("N");
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

        private static AnimatorState State(AnimatorStateMachine machine, string name, AnimationClip motion = null)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = false; return state;
        }

        private AnimatorStateMachine Layer(string name)
        {
            controller.AddLayer(name); var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1; controller.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        private static void Transition(AnimatorState from, AnimatorState to, string parameter, float value, float duration = 0)
        {
            var t = from.AddTransition(to); t.hasExitTime = false; t.duration = duration; t.canTransitionToSelf = true;
            t.AddCondition(AnimatorConditionMode.Equals, value, parameter);
        }

        private static AnimatorStateTransition Timed(AnimatorState from, AnimatorState to, float exitTime, float duration = 0, bool fixedDuration = true)
        {
            var t = from.AddTransition(to); t.hasExitTime = true; t.exitTime = exitTime;
            t.hasFixedDuration = fixedDuration; t.duration = duration; return t;
        }

        private AnimationClip Clip(string name, AnimationCurve face, bool loop = false)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), face);
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink"),
                AnimationCurve.Constant(0, face.keys.Last().time, 0));
            if (loop) { var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings); }
            AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }

        private AnimationClip Clip(string name, float value, float length = 1) => Clip(name, AnimationCurve.Constant(0, length, value));

        // Layer 0: Idle -> (Menu == 1) -> returned state.
        private AnimatorState Selected(AnimationClip clip, float duration = 0)
        {
            var machine = controller.layers[0].stateMachine;
            var idle = State(machine, "Idle"); machine.defaultState = idle;
            var selected = State(machine, "Selected", clip); Transition(idle, selected, "Menu", 1, duration); return selected;
        }

        private static Dictionary<string, float> Menu(float value = 1) => new Dictionary<string, float> { ["Menu"] = value };

        private float Sample(List<string> messages, IDictionary<string, float> selected = null) =>
            VrChatExpressionSampler.Sample(avatar, controller, metadata.Defaults, selected ?? Menu(), null, metadata, messages: messages)
                .Single(value => value.Shape == "Face size").Weight;

        private float Play(List<string> warnings) =>
            ExperimentalInstalledExpressionPlayer.Sample(avatar, metadata, Menu(), warnings: warnings)
                .Single(value => value.Shape == "Face size").Weight;

        // Settlement converts native clocks with AnimatorStateInfo.length and
        // AnimatorTransitionInfo.duration/durationUnit. Measure both directly.
        [TestCase(1f, false)] [TestCase(2f, false)] [TestCase(.5f, false)] [TestCase(2f, true)]
        public void NativeClocksMatchTheSettlementConversion(float speed, bool fixedDuration)
        {
            var machine = controller.layers[0].stateMachine;
            var a = State(machine, "A", Clip("A", 0, 2)); a.speed = speed; machine.defaultState = a;
            var b = State(machine, "B", Clip("B", 60));
            Timed(a, b, .5f, fixedDuration ? .25f : .5f, fixedDuration);
            var graph = PlayableGraph.Create("Settlement clock probe");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, "Probe", avatar.GetComponent<Animator>()).SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate(0);
                var length = playable.GetCurrentAnimatorStateInfo(0).length;
                Assert.That(length, Is.EqualTo(2 / speed).Within(.001), "AnimatorStateInfo.length includes the state speed.");
                const float step = 1f / 600;
                var elapsed = 0f;
                while (!playable.IsInTransition(0) && elapsed < 10) { graph.Evaluate(step); elapsed += step; }
                Assert.That(elapsed, Is.EqualTo(.5f * length).Within(.01), "Exit time waits exitTime * length.");
                var transition = playable.GetAnimatorTransitionInfo(0);
                Assert.That(transition.durationUnit, Is.EqualTo(fixedDuration ? DurationUnit.Fixed : DurationUnit.Normalized));
                var seconds = (1 - transition.normalizedTime) * (transition.durationUnit == DurationUnit.Fixed ? transition.duration :
                    transition.duration * playable.GetCurrentAnimatorStateInfo(0).length);
                elapsed = 0;
                while (playable.IsInTransition(0) && elapsed < 10) { graph.Evaluate(step); elapsed += step; }
                Assert.That(elapsed, Is.EqualTo(seconds).Within(.01),
                    "duration=" + transition.duration + " unit=" + transition.durationUnit + " t=" + transition.normalizedTime);
            }
            finally { if (graph.IsValid()) graph.Destroy(); }
        }

        [TestCase(1f)] [TestCase(.5f)] [TestCase(2f)]
        public void ANonLoopingClipLongerThanTheWarmUpIsSavedAtItsLastKey(float speed)
        {
            Selected(Clip("Slow face", AnimationCurve.Linear(0, 0, 10, 100))).speed = speed;
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(100).Within(.01));
            Assert.That(messages, Is.EqualTo(new[] { ExpressionSettlement.FinalKeyOfClip }));
        }

        [TestCase(0f, true)] [TestCase(.5f, true)] [TestCase(.5f, false)]
        public void AStateReachedByALateExitTimeIsFollowed(float duration, bool fixedDuration)
        {
            var a = Selected(Clip("A", 0));
            Timed(a, State(controller.layers[0].stateMachine, "B", Clip("B", 60)), 5, duration, fixedDuration);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(60).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.FollowedTransitions));
            Assert.That(messages, Does.Not.Contain(ExpressionSettlement.CyclicTransitions));
        }

        [Test]
        public void ALoopingClipIsSavedAtTheFirstFrameOfItsFinalState()
        {
            Selected(Clip("Looping face", AnimationCurve.Linear(0, 20, 1, 80), loop: true));
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(20).Within(1.01));
            Assert.That(messages, Is.EqualTo(new[] { ExpressionSettlement.LoopingFinal }));
        }

        [Test]
        public void CyclicExitTimesStopAtTheFirstRevisitedState()
        {
            var a = Selected(Clip("A", 10));
            var b = State(controller.layers[0].stateMachine, "B", Clip("B", 30));
            Timed(a, b, 3); Timed(b, a, 3);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(10).Within(.01).Or.EqualTo(30).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.CyclicTransitions));
        }

        [Test]
        public void AFaceThatSettlesInsideTheWarmUpKeepsItsValueWithoutMessages()
        {
            Selected(Clip("Smile", 75), .25f);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(75).Within(.0001));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void AnAddDriverOnAnExitTimeTargetRunsOnce()
        {
            var wait = Selected(Clip("Wait", 0));
            var done = State(controller.layers[0].stateMachine, "Done", Clip("Done", 0));
            Timed(wait, done, 4);
            ParameterDriverExpressionTests.Driver(done, ParameterDriverExpressionTests.Op("Add", "Face", 1));
            // The face is decided by layer 1; layer 0 writes the same morph
            // only before its rest, so give layer 0 no Face size curve.
            foreach (var clip in new[] { (AnimationClip)wait.motion, (AnimationClip)done.motion })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), null);
            var face = Layer("Face layer");
            var neutral = State(face, "Neutral", Clip("Neutral", 0)); face.defaultState = neutral;
            var smile = State(face, "Smile", Clip("Smile", 75)); var other = State(face, "Other", Clip("Other", 40));
            Transition(neutral, smile, "Face", 1); Transition(neutral, other, "Face", 2); Transition(smile, other, "Face", 2);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(75).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.FollowedTransitions));
        }

        [Test]
        public void AMotionTimeStateIsNotStepped()
        {
            controller.AddParameter("Clock", AnimatorControllerParameterType.Float);
            var selected = Selected(Clip("Clocked face", AnimationCurve.Linear(0, 0, 1, 100)));
            selected.timeParameterActive = true; selected.timeParameter = "Clock";
            var messages = new List<string>();
            Assert.That(Sample(messages, new Dictionary<string, float> { ["Menu"] = 1, ["Clock"] = .5f }), Is.EqualTo(50).Within(.01));
            Assert.That(messages, Is.Empty);
        }

        [Test]
        public void TheExperimentalPlayerKeepsTheLastKeyOfALongClip()
        {
            Selected(Clip("Slow face", AnimationCurve.Linear(0, 0, 10, 100)));
            var warnings = new List<string>();
            Assert.That(Play(warnings), Is.EqualTo(100).Within(.01));
            Assert.That(warnings, Is.EqualTo(new[] { ExpressionSettlement.FinalKeyOfClip }));
        }

        [Test]
        public void TheExperimentalPlayerFollowsALateExitTime()
        {
            var a = Selected(Clip("A", 0));
            Timed(a, State(controller.layers[0].stateMachine, "B", Clip("B", 60)), 5, .5f);
            var warnings = new List<string>();
            Assert.That(Play(warnings), Is.EqualTo(60).Within(.01));
            Assert.That(warnings, Does.Contain(ExpressionSettlement.FollowedTransitions));
        }
    }
}
