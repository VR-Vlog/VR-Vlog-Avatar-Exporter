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
    public sealed class DirectProbeTests
    {
        string folder;
        GameObject avatar;
        Mesh mesh;
        AnimatorController controller;
        AnimatorState selected;
        AnimationClip faceClip;
        VrChatExpressionMenu.Source source;
        readonly List<string> events = new List<string>();
        static EditorCurveBinding Binding(string shape) => EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape." + shape);

        [SetUp]
        public void SetUp()
        {
            var name = "__DirectProbe_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            avatar = new GameObject("Direct probe", typeof(Animator));
            var skin = new GameObject("Body", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>(); skin.transform.SetParent(avatar.transform, false);
            mesh = new Mesh { name = "Direct probe test" }; mesh.vertices = new[] { Vector3.zero, Vector3.up, Vector3.right }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (var shape in new[] { "Smile", "Wink", "Rest" }) mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.up * .01f, 3).ToArray(), null, null);
            skin.sharedMesh = mesh;
            skin.SetBlendShapeWeight(2, 20);
            controller.AddLayer("Left hand"); var layers = controller.layers; layers[1].defaultWeight = 1; controller.layers = layers;
            var machine = controller.layers[1].stateMachine;
            var idle = machine.AddState("Idle"); idle.motion = Clip("Idle"); idle.writeDefaultValues = false; machine.defaultState = idle;
            faceClip = Clip("Smile and wink", ("Smile", 80), ("Wink", 35.5f));
            selected = machine.AddState("Smile"); selected.motion = faceClip; selected.writeDefaultValues = false;
            var enter = idle.AddTransition(selected); enter.duration = 0; enter.AddCondition(AnimatorConditionMode.Equals, 3, "GestureLeft");
            var leave = selected.AddTransition(idle); leave.duration = 0; leave.AddCondition(AnimatorConditionMode.NotEqual, 3, "GestureLeft");
            source = new VrChatExpressionMenu.Source { Controller = controller, NeutralInputInventoryComplete = true };
            VrChatExpressionSampler.DirectProbeObserver = events.Add;
        }

        [TearDown]
        public void TearDown()
        {
            VrChatExpressionSampler.DirectProbeObserver = null;
            VrChatExpressionSampler.CompareDirectProbe = false;
            Object.DestroyImmediate(avatar); Object.DestroyImmediate(mesh); if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        AnimationClip Clip(string name, params (string Shape, float Weight)[] weights)
        {
            var clip = new AnimationClip { name = name }; AssetDatabase.AddObjectToAsset(clip, controller);
            foreach (var weight in weights) AnimationUtility.SetEditorCurve(clip, Binding(weight.Shape), AnimationCurve.Constant(0, 1, weight.Weight));
            return clip;
        }

        VrChatExpressionMenu.Entry Apply(bool compare)
        {
            events.Clear();
            VrChatExpressionSampler.CompareDirectProbe = compare;
            var entry = new VrChatExpressionMenu.Entry(); VrChatGestureExpressions.ReadClip(avatar, faceClip, entry);
            VrChatExpressionSampler.ApplyPermanentOverrides(avatar, controller, entry, 1, false, metadata: source, sourceState: selected);
            return entry;
        }

        static string[] Values(VrChatExpressionMenu.Entry entry) => entry.Values.OrderBy(value => value.Path, StringComparer.Ordinal)
            .ThenBy(value => value.Shape, StringComparer.Ordinal).Select(value => value.Path + "/" + value.Shape + "=" + value.Weight.ToString("R")).ToArray();

        [Test]
        public void AClipOnItsOwnFullLayerIsReadWithoutPlaying()
        {
            var played = Apply(compare: true);
            Assert.That(events, Is.EqualTo(new[] { "direct", "match", "match" }));
            var read = Apply(compare: false);
            Assert.That(events, Is.EqualTo(new[] { "direct" }));
            Assert.That(Values(read), Is.EqualTo(Values(played)));
            Assert.That(Values(read), Is.EqualTo(new[] { "Body/Smile=80", "Body/Wink=35.5" }));
            Assert.That(read.Messages, Is.EqualTo(played.Messages));
        }

        [Test]
        public void APartialLayerStillPlays()
        {
            var layers = controller.layers; layers[1].defaultWeight = .5f; controller.layers = layers;
            var entry = Apply(compare: false);
            Assert.That(events, Is.EqualTo(new[] { "sampled: weight" }));
            Assert.That(entry.Values.Single(value => value.Shape == "Smile").Weight, Is.EqualTo(40).Within(.01));
        }

        [Test]
        public void ALowerLayerOnTheSameMorphStillPlays()
        {
            var machine = controller.layers[0].stateMachine;
            var rest = machine.AddState("Rest"); rest.motion = Clip("Lower smile", ("Smile", 30)); rest.writeDefaultValues = false; machine.defaultState = rest;
            var entry = Apply(compare: false);
            Assert.That(events.Single(), Does.StartWith("sampled: "));
            Assert.That(entry.Values.Single(value => value.Shape == "Smile").Weight, Is.EqualTo(80).Within(.01));
        }

        [Test]
        public void AMaskedLayerStillPlays()
        {
            var mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, folder + "/Mask.mask");
            var layers = controller.layers; layers[1].avatarMask = mask; controller.layers = layers;
            try { Apply(compare: false); } catch (InvalidOperationException) { }
            Assert.That(events.FirstOrDefault(), Is.EqualTo("sampled: mask"));
        }

        [Test]
        public void AStateCallbackStillPlays()
        {
            controller.AddParameter("Enable", AnimatorControllerParameterType.Bool);
            ParameterDriverExpressionTests.Driver(selected, ParameterDriverExpressionTests.Op("Set", "Enable", 1));
            Apply(compare: false);
            Assert.That(events, Is.Not.Contains("direct"));
        }
    }
}
