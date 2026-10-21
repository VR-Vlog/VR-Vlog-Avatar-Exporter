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
        public void AnItemLinkedToOneClipIsNotListed()
        {
            var smile = FaceClip("Smile");
            var controller = Controller((smile, 1));
            var items = new[] { Item("Face / Smile", 1), Item("Face / Smile 2", 1, "Button") };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.Empty, "The clip is a board item.");
            Assert.That(Session.MenuClipPaths(items, controller, FaceOff)[smile], Is.EqualTo(new[] { "Face / Smile", "Face / Smile 2" }));
        }

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
        public void ASubMachineFaceIsListed()
        {
            var controller = Keep(new AnimatorController());
            controller.AddParameter("Face", AnimatorControllerParameterType.Int);
            var machine = Keep(new AnimatorStateMachine());
            machine.defaultState = machine.AddState("Idle");
            var face = machine.AddStateMachine("Face");
            var inner = face.AddStateMachine("Inner");
            inner.AddState("Smile").motion = Tree(FaceClip("Smile"));
            machine.AddAnyStateTransition(face).AddCondition(AnimatorConditionMode.Equals, 1, "Face");
            var hat = machine.AddStateMachine("Hat");
            hat.AddState("Hat").motion = ClothingClip("Hat");
            machine.AddAnyStateTransition(hat).AddCondition(AnimatorConditionMode.Equals, 2, "Face");
            controller.layers = new[] { new AnimatorControllerLayer { name = "Face", defaultWeight = 1, stateMachine = machine } };
            var items = new[] { Item("Face / Smile", 1), Item("Wear / Hat", 2) };
            Assert.That(Session.MenuEntriesOutsideBoard(items, controller, FaceOff), Is.EqualTo(new[] { "Face / Smile" }),
                "The face is found in a nested machine; a machine that moves no BlendShape is not a face.");
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
        public void AnAvatarWithoutAVrChatMenuHasNoItemsRatherThanAFailure()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            using var session = new Session(fixture.Source, replayInstalledDefaults: false);
            Assert.That(session.SourceMenuEntriesOutsideBoard, Is.Not.Null.And.Empty);
        }

        [Test]
        public void TheBandShowsOnlyInTheRecordRouteAndCapsTheListedPaths()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.MenuLossBand(false, new[] { "Face / Split" }), Is.Null);
            Assert.That(ExperimentalExpressionCaptureWindow.MenuLossBand(false, null), Is.Null, "The menu route records the menu itself.");
            Assert.That(ExperimentalExpressionCaptureWindow.MenuLossBand(true, Array.Empty<string>()), Is.Null);
            var unreadable = ExperimentalExpressionCaptureWindow.MenuLossBand(true, null).Value;
            Assert.That(unreadable.Text, Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuUnreadable)));
            var paths = Enumerable.Range(1, 23).Select(index => "Face / " + index).ToArray();
            var band = ExperimentalExpressionCaptureWindow.MenuLossBand(true, paths).Value;
            Assert.That(band.Text, Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuLossFormat), 23)));
            var lines = band.Tooltip.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(2 + 20 + 1));
            Assert.That(lines[0], Is.EqualTo(band.Text));
            Assert.That(lines[1], Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuLossListHeader)));
            Assert.That(lines.Skip(2).Take(20), Is.EqualTo(paths.Take(20)), "Paths are the user's own names, in menu order.");
            Assert.That(lines.Last(), Is.EqualTo(string.Format(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.MenuLossMoreFormat), 3)));
        }

        [Test]
        public void EveryShippedLocaleTranslatesTheBand()
        {
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var file = JsonUtility.FromJson<LocaleFile>(File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json")));
                var table = file.entries.ToDictionary(entry => entry.source, entry => entry.translated);
                foreach (var source in new[] {
                    ExperimentalExpressionCaptureWindow.MenuLossFormat, ExperimentalExpressionCaptureWindow.MenuLossListHeader,
                    ExperimentalExpressionCaptureWindow.MenuLossMoreFormat, ExperimentalExpressionCaptureWindow.MenuUnreadable })
                {
                    Assert.That(table.TryGetValue(source, out var translated), Is.True, locale + ": " + source);
                    Assert.That(translated, Is.Not.Empty.And.Not.EqualTo(source), locale + ": " + source);
                    Assert.That(translated.Contains("{0}"), Is.EqualTo(source.Contains("{0}")), locale + ": " + source);
                    Assert.That(string.Format(translated, 7), Does.Contain(source.Contains("{0}") ? "7" : ""), locale + ": " + source);
                }
            }
        }

        [Serializable] class LocaleFile { public LocaleEntry[] entries; }
        [Serializable] class LocaleEntry { public string source, translated; }
    }
}
