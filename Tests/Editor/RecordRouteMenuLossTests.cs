using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using Session = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession;

namespace VRVlog.LilToonExporter.Tests
{
    // The record route records only the board. These menu items have no board
    // item, so the custom list warns about them before saving.
    public sealed class RecordRouteMenuLossTests
    {
        readonly List<Object> created = new List<Object>();

        [TearDown]
        public void DestroyCreated()
        {
            foreach (var item in created) if (item != null) Object.DestroyImmediate(item);
            created.Clear();
        }

        T Keep<T>(T item) where T : Object { created.Add(item); return item; }

        AnimationClip FaceClip(string name)
        {
            var clip = Keep(new AnimationClip { name = name });
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape." + name), AnimationCurve.Constant(0, 1, 80));
            return clip;
        }

        AnimationClip ClothingClip(string name)
        {
            var clip = Keep(new AnimationClip { name = name });
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(name, typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 1, 1));
            return clip;
        }

        // One layer per motion, each entered from Any State while Face equals
        // the value, as menu toggles drive face layers.
        AnimatorController Controller(params (Motion Motion, float Value)[] layers)
        {
            var controller = Keep(new AnimatorController());
            controller.AddParameter("Face", AnimatorControllerParameterType.Int);
            controller.layers = layers.Select((layer, index) =>
            {
                var machine = Keep(new AnimatorStateMachine());
                machine.defaultState = machine.AddState("Idle");
                var state = machine.AddState("State " + index); state.motion = layer.Motion;
                machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.Equals, layer.Value, "Face");
                return new AnimatorControllerLayer { name = "Layer " + index, defaultWeight = 1, stateMachine = machine };
            }).ToArray();
            return controller;
        }

        BlendTree Tree(params Motion[] children)
        {
            var tree = Keep(new BlendTree { name = "Tree", blendParameter = "Blend" });
            for (var index = 0; index < children.Length; index++) tree.AddChild(children[index], index);
            return tree;
        }

        static VrChatExpressionMenu.Entry Item(string path, float value, string type = "Toggle")
        {
            var entry = new VrChatExpressionMenu.Entry { Name = path, ControlType = type, ControlParameter = "Face" };
            entry.Parameters["Face"] = value;
            return entry;
        }

        static readonly Dictionary<string, float> FaceOff = new Dictionary<string, float> { ["Face"] = 0 };

        [Test]
        public void AFaceSplitOverLayersIsListed()
        {
            var controller = Controller((FaceClip("Eyes"), 1), (FaceClip("Mouth"), 1));
            var items = new[] { Item("Face / Smile", 1), Item("Face / Smile", 1) };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.EqualTo(new[] { "Face / Smile" }), "Listed once.");
            Assert.That(Session.MenuClipPaths(items, controller, FaceOff), Is.Empty, "Each clip is only part of the face.");
        }

        [Test]
        public void ABlendTreeFaceIsListed()
        {
            var controller = Controller((Tree(FaceClip("Smile"), FaceClip("Laugh")), 1), (Tree(ClothingClip("Hat")), 2));
            var items = new[] { Item("Face / Smile", 1), Item("Wear / Hat", 2) };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.EqualTo(new[] { "Face / Smile" }),
                "A Blend Tree that moves no BlendShape is not a face.");
            Assert.That(Session.MenuClipPaths(items, controller, FaceOff), Is.Empty);
        }

        [Test]
        public void AClothingToggleIsNotListed()
        {
            var controller = Controller((ClothingClip("Hat"), 1), (ClothingClip("Coat"), 1), (FaceClip("Smile"), 2));
            var items = new[] { Item("Wear / Hat and coat", 1), Item("Face / Smile", 2), Item("Face / Off", 1, "RadialPuppet") };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.Empty, "Two clothing clips, then a linked face; puppets are not items.");
        }

        [Test]
        public void MenuClipPathsIsTheSameAndTheListKeepsMenuOrder()
        {
            var smile = FaceClip("Smile");
            var angry = FaceClip("Angry");
            var controller = Controller((smile, 1), (FaceClip("Eyes"), 3), (FaceClip("Mouth"), 3), (angry, 4), (Tree(FaceClip("Laugh")), 2));
            var items = new[] { Item("Face / Split", 3), Item("Face / Smile", 1), Item("Face / Tree", 2), Item("Face / Angry", 4), Item("Face / Not wired", 5) };
            var paths = Session.MenuClipPaths(items, controller, FaceOff);
            Assert.That(paths.Keys, Is.EquivalentTo(new[] { smile, angry }));
            Assert.That(paths[smile], Is.EqualTo(new[] { "Face / Smile" }));
            Assert.That(paths[angry], Is.EqualTo(new[] { "Face / Angry" }));
            var links = Session.MenuLinks(items, controller, FaceOff);
            Assert.That(links.Paths.Keys, Is.EquivalentTo(paths.Keys), "One walk gives both.");
            Assert.That(links.Outside, Is.EqualTo(new[] { "Face / Split", "Face / Tree" }));
            Assert.That(Session.MenuEntriesOutsideBoard(items, null, FaceOff), Is.Empty);
        }

        [Test]
        public void ASharedResetLayerEnteredByNotEqualIsNotASecondFace()
        {
            var smile = FaceClip("Smile");
            var angry = FaceClip("Angry");
            var controller = Controller((smile, 1), (angry, 2));
            var machine = Keep(new AnimatorStateMachine());
            machine.defaultState = machine.AddState("Idle");
            var reset = machine.AddState("Reset"); reset.motion = FaceClip("Reset");
            machine.AddAnyStateTransition(reset).AddCondition(AnimatorConditionMode.NotEqual, 0, "Face");
            controller.layers = controller.layers.Append(new AnimatorControllerLayer { name = "Reset", defaultWeight = 1, stateMachine = machine }).ToArray();
            var items = new[] { Item("Face / Smile", 1), Item("Face / Angry", 2) };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.Empty,
                "Every item of Face selects the reset face, so it does not split either item.");
            var paths = Session.MenuClipPaths(items, controller, FaceOff);
            Assert.That(paths, Does.Not.ContainKey(smile).And.Not.ContainKey(angry),
                "MenuClipPaths is unchanged: each item still selects two clips there.");
        }

        [Test]
        public void AFloatToggleSplitOverLayersByGreaterIsListed()
        {
            var controller = Keep(new AnimatorController());
            controller.AddParameter("Open", AnimatorControllerParameterType.Float);
            controller.layers = new[] { FaceClip("Eyes"), FaceClip("Mouth") }.Select((clip, index) =>
            {
                var machine = Keep(new AnimatorStateMachine());
                machine.defaultState = machine.AddState("Idle");
                var state = machine.AddState("State " + index); state.motion = clip;
                machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.Greater, 0.5f, "Open");
                return new AnimatorControllerLayer { name = "Layer " + index, defaultWeight = 1, stateMachine = machine };
            }).ToArray();
            var entry = new VrChatExpressionMenu.Entry { Name = "Face / Open", ControlType = "Toggle", ControlParameter = "Open" };
            entry.Parameters["Open"] = 1;
            var items = new[] { entry };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, new Dictionary<string, float> { ["Open"] = 0 }),
                Is.EqualTo(new[] { "Face / Open" }), "A face split over layers by Greater is still split.");
        }

        [Test]
        public void AnItemWithOneClipAndABlendTreeFaceIsNotListed()
        {
            // Spec choice: the item is linked to its clip, which is on the board.
            var smile = FaceClip("Smile");
            var controller = Controller((smile, 1), (Tree(FaceClip("Laugh")), 1));
            var items = new[] { Item("Face / Smile", 1) };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.Empty);
            Assert.That(Session.MenuClipPaths(items, controller, FaceOff)[smile], Is.EqualTo(new[] { "Face / Smile" }));
        }

        [Test]
        public void AMenuThatCannotBeReadIsUnreadableNotEmpty()
        {
            var links = Session.ReadMenuLinks(() => throw new InvalidOperationException("broken"));
            Assert.That(links.Outside, Is.Null);
            Assert.That(links.Paths, Is.Empty);
            Assert.That(ExperimentalExpressionCaptureWindow.MenuLossBand(true, links.Outside).Value.Text,
                Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuUnreadable)));
        }

        [Test]
        public void AnAvatarWithoutAVrChatMenuHasNoItemsRatherThanAFailure()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            Assert.That(session.SourceMenuEntriesOutsideBoard, Is.Not.Null.And.Empty);
        }
    }
}
