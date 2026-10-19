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
            Assert.That(messages, Does.Not.Contain(ExpressionSettlement.LongWait));
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
            // A is left at 3 s and B is left at 6 s, back into A. A is the
            // first revisited state, so its exit edge is not followed again.
            Assert.That(Sample(messages), Is.EqualTo(10).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.CyclicTransitions));
            Assert.That(messages, Does.Not.Contain(ExpressionSettlement.SelfTransition));
        }

        // An exit edge whose conditions are false now cannot be followed.
        // The face is kept, reported as held, and still checked over time.
        [Test]
        public void AHeldExitTimeIsReportedSeparatelyAndStillChecked()
        {
            var smile = Selected(Clip("Smile", 75));
            var edge = Timed(smile, State(controller.layers[0].stateMachine, "Later", Clip("Later", 30)), 5);
            edge.AddCondition(AnimatorConditionMode.Equals, 1, "Face");
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(75).Within(.01));
            Assert.That(messages, Is.EqualTo(new[] { ExpressionSettlement.HeldTransitions }));
        }

        // A stopped or reversed clock never reaches the clip's last key.
        [TestCase(0f)] [TestCase(-1f)]
        public void AMovingClipOnAStoppedOrReversedClockIsNotSaved(float speed)
        {
            Selected(Clip("Slow face", AnimationCurve.Linear(0, 0, 10, 100))).speed = speed;
            Assert.Catch<InvalidOperationException>(() => Sample(new List<string>()));
            Assert.Catch<InvalidOperationException>(() => Play(new List<string>()));
        }

        // A -> B starts at 5 s and lasts 1 s. A -> C is due at 5.3 s and
        // interrupts it natively; one long step would skip the interruption.
        [Test]
        public void AnInterruptibleTransitionIsSteppedFrameByFrame()
        {
            var a = Selected(Clip("A", 0));
            var machine = controller.layers[0].stateMachine;
            var toB = Timed(a, State(machine, "B", Clip("B", 60)), 5, 1);
            toB.interruptionSource = TransitionInterruptionSource.Source; toB.orderedInterruption = false;
            Timed(a, State(machine, "C", Clip("C", 30)), 5.3f);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(30).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.FollowedTransitions));
        }

        // A self transition re-enters its state and runs its Add driver once
        // per entry. Settlement follows it once and stops there.
        [TestCase(false)] [TestCase(true)]
        public void ASelfTransitionIsFollowedOnceAndReported(bool anyState)
        {
            var machine = controller.layers[0].stateMachine;
            var wait = Selected(Clip("Wait", 0, 10));
            AnimationUtility.SetEditorCurve((AnimationClip)wait.motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), null);
            if (anyState)
            {
                var edge = machine.AddAnyStateTransition(wait); edge.hasExitTime = true; edge.exitTime = .5f; edge.duration = 0;
                edge.canTransitionToSelf = true; edge.AddCondition(AnimatorConditionMode.Equals, 1, "Menu");
            }
            else Timed(wait, wait, .5f);
            ParameterDriverExpressionTests.Driver(wait, ParameterDriverExpressionTests.Op("Add", "Face", 1));
            var face = Layer("Face layer");
            var neutral = State(face, "Neutral", Clip("Neutral", 0)); face.defaultState = neutral;
            var smile = State(face, "Smile", Clip("Smile", 75)); var other = State(face, "Other", Clip("Other", 40));
            var third = State(face, "Third", Clip("Third", 10));
            Transition(neutral, smile, "Face", 1); Transition(smile, other, "Face", 2); Transition(other, third, "Face", 3);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(40).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.SelfTransition));
            Assert.That(messages, Does.Not.Contain(ExpressionSettlement.CyclicTransitions));
            Assert.That(messages, Does.Not.Contain(ExpressionSettlement.HeldTransitions));
        }

        // Loops of 1 s and 0.7 s share one clock; only one starts its cycle.
        [Test]
        public void LoopsOfDifferentLengthsAreReportedAsOverlapping()
        {
            Selected(Clip("Looping face", AnimationCurve.Linear(0, 20, 1, 80), loop: true));
            var blink = new AnimationClip { name = "Blinking" };
            AnimationUtility.SetEditorCurve(blink, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink"),
                AnimationCurve.Linear(0, 0, .7f, 100));
            var settings = AnimationUtility.GetAnimationClipSettings(blink); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(blink, settings);
            AssetDatabase.AddObjectToAsset(blink, controller);
            var eyes = Layer("Eyes");
            var open = State(eyes, "Open", Clip("Open", 0)); eyes.defaultState = open;
            AnimationUtility.SetEditorCurve((AnimationClip)open.motion, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), null);
            Transition(open, State(eyes, "Blinking", blink), "Menu", 1);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(20).Within(1.01));
            Assert.That(messages, Is.EqualTo(new[] { ExpressionSettlement.OverlappingLoops }));
        }

        // More timed state changes than the hop limit leave the expression
        // out, as before settlement, instead of saving a cyclic face.
        [Test]
        public void AChainLongerThanTheHopLimitIsNotSaved()
        {
            var machine = controller.layers[0].stateMachine;
            var previous = Selected(Clip("Step 0", 0));
            for (var index = 1; index <= 70; index++)
            {
                var next = State(machine, "Step " + index, Clip("Step " + index, index));
                Timed(previous, next, 1); previous = next;
            }
            var error = Assert.Catch<InvalidOperationException>(() => Sample(new List<string>()));
            Assert.That(error.Message, Does.Contain(ExpressionSettlement.UntraceableTransitions));
        }

        // A face reached only after a long wait is saved and reported.
        [TestCase(false)] [TestCase(true)]
        public void AFaceAfterMoreThanAMinuteIsReported(bool clip)
        {
            if (clip) Selected(Clip("Very slow face", AnimationCurve.Linear(0, 0, 90, 100)));
            else Timed(Selected(Clip("A", 0)), State(controller.layers[0].stateMachine, "B", Clip("B", 60)), 70);
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(clip ? 100 : 60).Within(.01));
            Assert.That(messages, Does.Contain(clip ? ExpressionSettlement.FinalKeyOfClip : ExpressionSettlement.FollowedTransitions));
            Assert.That(messages, Does.Contain(ExpressionSettlement.LongWait));
        }

        // A face the stationary proof accepts is never stepped, so its
        // values stay exactly those of the warm-up.
        [Test]
        public void SettleDoesNotStepAStationaryFace()
        {
            Selected(Clip("Smile", 75));
            var graph = PlayableGraph.Create("Settlement stationary probe");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, "Probe", avatar.GetComponent<Animator>()).SetSourcePlayable(playable);
                graph.Play(); graph.Evaluate(0);
                playable.SetInteger("Menu", 1);
                for (var frame = 0; frame < 120; frame++) graph.Evaluate(1f / 60);
                var updates = 0;
                var warnings = new List<string>();
                Assert.That(ExpressionSettlement.Settle(graph, playable, controller, layer => true, () => updates++, warnings,
                    alreadyFixed: () => true), Is.False);
                Assert.That(updates, Is.Zero);
                Assert.That(warnings, Is.Empty);
            }
            finally { if (graph.IsValid()) graph.Destroy(); }
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

        // A hand idle animates humanoid muscles, not controller parameters.
        private AnimationClip HandIdle(string name, AnimationClip clip = null)
        {
            clip = clip ?? new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "LeftHand.Index.1 Stretched"),
                new AnimationCurve(new Keyframe(0, 0), new Keyframe(.5f, 1), new Keyframe(1, 0)));
            var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = true; AnimationUtility.SetAnimationClipSettings(clip, settings);
            if (!AssetDatabase.Contains(clip)) AssetDatabase.AddObjectToAsset(clip, controller);
            return clip;
        }

        [TestCase(false)] [TestCase(true)]
        public void ALoopingHandMuscleCurveBesideTheFaceIsSettled(bool separateLayer)
        {
            if (separateLayer)
            {
                Selected(Clip("Smile", 75));
                // The hand layer also writes the face in another state, so it
                // takes part in the expression while it stays in its idle.
                var hand = Layer("Left Hand");
                var idle = State(hand, "Idle", HandIdle("Hands idle")); hand.defaultState = idle;
                Transition(idle, State(hand, "Left smile", Clip("Left smile", 40)), "Face", 1);
            }
            else Selected(HandIdle(null, Clip("Smile with hand", 75)));
            var messages = new List<string>();
            Assert.That(Sample(messages), Is.EqualTo(75).Within(.01));
            Assert.That(messages, Does.Contain(ExpressionSettlement.LoopingFinal));
        }

        // A Right Hand gesture keeps the Left Hand layer below it. Its idle
        // holds the rest face while looping hand muscles, so the rest face
        // sampled under the gesture is still a fixed pose. The Eyes layer
        // keeps the gesture from being the top layer that alone fixes the
        // rest face, so the hand layer's curves are checked.
        [Test]
        public void AGestureAboveALoopingHandIdleLayerIsExported()
        {
            foreach (var side in new[] { "Left", "Right" })
            {
                controller.AddParameter("Gesture" + side, AnimatorControllerParameterType.Int);
                var hand = Layer(side + " Hand");
                var idle = State(hand, "Idle", HandIdle(null, Clip(side + " hand idle", 25))); hand.defaultState = idle;
                Transition(idle, State(hand, side + " smile", Clip(side + " smile", side == "Left" ? 40 : 75)), "Gesture" + side, 2);
            }
            var open = new AnimationClip { name = "Eyes open" };
            AnimationUtility.SetEditorCurve(open, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Blink"),
                AnimationCurve.Constant(0, 1, 0));
            AssetDatabase.AddObjectToAsset(open, controller);
            var eyes = Layer("Eyes"); eyes.defaultState = State(eyes, "Open", open);
            VrChatGestureExpressions.Add(avatar, metadata);
            var entry = metadata.Entries.Single(item => item.Name == "ジェスチャー / Right smile");
            Assert.That(entry.Error, Is.Null, entry.Error);
            Assert.That(entry.Values.Single(value => value.Shape == "Face size").Weight, Is.EqualTo(75).Within(.01));
        }

        // Analyze reads the menu for the one-click export. Its settlement
        // decision must reach the entry and the export result list.
        [Test]
        public void AMenuItemFollowedThroughAnExitTimeIsListedAsApproximated()
        {
            var a = Selected(Clip("A", 0));
            Timed(a, State(controller.layers[0].stateMachine, "B", Clip("B", 60)), 5);
            Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(name)).FirstOrDefault(type => type != null);
            var menuType = Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
            var descriptorType = Find("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (menuType == null || descriptorType == null) Assert.Ignore("Install the real VRChat SDK to run menu-analysis integration tests.");
            var menu = ScriptableObject.CreateInstance(menuType);
            AssetDatabase.CreateAsset(menu, folder + "/Menu.asset");
            using (var data = new SerializedObject(menu))
            {
                var controls = data.FindProperty("controls"); controls.arraySize = 1;
                var item = controls.GetArrayElementAtIndex(0);
                item.FindPropertyRelative("name").stringValue = "Late smile";
                var type = item.FindPropertyRelative("type"); type.enumValueIndex = Array.IndexOf(type.enumNames, "Toggle");
                item.FindPropertyRelative("parameter").FindPropertyRelative("name").stringValue = "Menu";
                item.FindPropertyRelative("value").floatValue = 1;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var descriptor = avatar.AddComponent(descriptorType);
            using (var data = new SerializedObject(descriptor))
            {
                data.FindProperty("customExpressions").boolValue = true;
                data.FindProperty("expressionsMenu").objectReferenceValue = menu;
                data.FindProperty("customizeAnimationLayers").boolValue = true;
                var list = data.FindProperty("baseAnimationLayers"); list.arraySize = 1;
                var layer = list.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                layer.FindPropertyRelative("isDefault").boolValue = false;
                layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                data.FindProperty("specialAnimationLayers").arraySize = 0;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var source = VrChatExpressionSampler.Analyze(avatar);
            var entry = source.Entries.Single(e => e.Name == "Late smile");
            Assert.That(entry.Error, Is.Null, string.Join("\n", source.Messages));
            Assert.That(entry.Values.Single(v => v.Shape == "Face size").Weight, Is.EqualTo(60).Within(.01));
            Assert.That(entry.Messages, Does.Contain(ExpressionSettlement.FollowedTransitions));
            var report = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordSettledMenuExpressions(source, report);
            Assert.That(report.Fallbacks.Results.Where(row => row.Outcome == ExportOutcome.Approximated && row.Category == "表情" && row.Name == "Late smile")
                .Select(row => row.Reason), Does.Contain(ExpressionSettlement.FollowedTransitions));
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
