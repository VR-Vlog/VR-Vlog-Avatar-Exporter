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
    public sealed class FaceEmoDefaultFaceTests
    {
        private string folder;
        private GameObject avatar;
        private AnimatorController controller;
        private Component descriptor;
        private AnimationClip common;
        private readonly List<Mesh> meshes = new List<Mesh>();

        [SetUp]
        public void SetUp()
        {
            var descriptorType = OptionalTools.Require("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", "Install the real VRChat SDK.");
            folder = TempAssetFolder.Create("__FaceEmoDefaultFace_");
            controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            avatar = new GameObject("Avatar", typeof(Animator));
            var merged = new GameObject("Merged"); merged.transform.SetParent(avatar.transform, false);
            Renderer(merged, "Face", "Open", "Smile"); Renderer(merged, "Accessory", "Cheek");
            descriptor = avatar.AddComponent(descriptorType); SetFx(controller);
            common = Clip("Common underlay");
            Curve(common, "Merged/Face", "Open", AnimationCurve.Constant(0, 1, 100));
            Curve(common, "Merged/Face", "Smile", AnimationCurve.Constant(0, 1, 40));
            Curve(common, "Merged/Accessory", "Cheek", AnimationCurve.Constant(0, 1, 10));
            var layers = controller.layers; layers[0].name = "[ USER EDIT ] DEFAULT FACE"; controller.layers = layers;
            var state = layers[0].stateMachine.AddState("DEFAULT"); state.motion = common; state.writeDefaultValues = false;
            layers[0].stateMachine.defaultState = state;
            controller.AddLayer("[ USER EDIT ] FACE EMOTE PLAYER");
            var mode = Clip("Mode default is an alternative");
            Curve(mode, "Merged/Face", "Open", AnimationCurve.Constant(0, 1, 50));
            Curve(mode, "Merged/Face", "Smile", AnimationCurve.Constant(0, 1, 80));
            var modeState = controller.layers[1].stateMachine.AddState("Mode default"); modeState.motion = mode;
            modeState.writeDefaultValues = false; controller.layers[1].stateMachine.defaultState = modeState;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(avatar);
            foreach (var mesh in meshes) Object.DestroyImmediate(mesh); meshes.Clear();
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        private void Renderer(GameObject parent, string name, params string[] shapes)
        {
            var child = new GameObject(name, typeof(SkinnedMeshRenderer)); child.transform.SetParent(parent.transform, false);
            var mesh = new Mesh(); meshes.Add(mesh);
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 };
            foreach (var shape in shapes) mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.up, 3).ToArray(), null, null);
            child.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
        }

        private void SetFx(RuntimeAnimatorController runtime) => VrcAvatar.SetFx(descriptor, runtime);

        private AnimationClip Clip(string name)
        {
            var clip = new AnimationClip { name = name }; AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }

        private static void Curve(AnimationClip clip, string path, string shape, AnimationCurve curve)
            => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), curve);

        // Record the renderers as they stood before NDMF merged them: Face was
        // the source's Body, Accessory sat at the root. Without an original,
        // Face is a renderer the preparation added.
        private PreparedExpressionBindings Bindings(bool faceHasOriginal = true)
        {
            var merged = avatar.transform.Find("Merged"); var face = merged.Find("Face"); var accessory = merged.Find("Accessory");
            face.SetParent(faceHasOriginal ? avatar.transform : null, false); face.name = "Body"; accessory.SetParent(avatar.transform, false);
            var bindings = new PreparedExpressionBindings(avatar, null);
            face.SetParent(merged, false); face.name = "Face"; accessory.SetParent(merged, false);
            return bindings;
        }

        private static VrChatExpressionMenu.Source Menu(float smile)
        {
            var source = new VrChatExpressionMenu.Source();
            var entry = new VrChatExpressionMenu.Entry { Id = "faceemo/0", Name = "Registered branch" };
            entry.Values.Add(new VrChatExpressionMenu.MorphValue { Path = "Body", Shape = "Smile", Weight = smile });
            source.Entries.Add(entry); return source;
        }

        [Test]
        public void UnresolvedOriginalRendererAndAnimatedCommonFaceAreActionableErrors()
        {
            var menu = Menu(100);
            FaceEmoExpressions.ApplyPreparedDefaultFace(avatar, menu, Bindings(faceHasOriginal: false));
            Assert.That(menu.Entries[0].Error, Does.Contain("元Renderer"));
            menu = Menu(100); Curve(common, "Merged/Face", "Open", AnimationCurve.Linear(0, 0, 10, 100));
            FaceEmoExpressions.ApplyPreparedDefaultFace(avatar, menu, Bindings());
            Assert.That(menu.Entries[0].Error, Does.Contain("時間で変化"));
        }

        [Test]
        public void PreparedOverrideAndExclusionApplyToTheCommonUnderlay()
        {
            var replacement = Clip("Prepared common override");
            Curve(replacement, "Merged/Face", "Open", AnimationCurve.Constant(0, 1, 60));
            Curve(replacement, "Merged/Accessory", "Cheek", AnimationCurve.Constant(0, 1, 30));
            var runtime = new AnimatorOverrideController(controller);
            try
            {
                runtime.ApplyOverrides(new[] { new KeyValuePair<AnimationClip, AnimationClip>(common, replacement) }); SetFx(runtime);
                var menu = Menu(100);
                FaceEmoExpressions.ApplyPreparedDefaultFace(avatar, menu, Bindings(), path => path == "Merged/Accessory");
                Assert.That(menu.Entries[0].Error, Is.Null);
                Assert.That(menu.Entries[0].Values.Single(value => value.Shape == "Open").Weight, Is.EqualTo(60));
                Assert.That(menu.Entries[0].Values.Any(value => value.Shape == "Cheek"), Is.False);
            }
            finally { Object.DestroyImmediate(runtime); }
        }
    }
}
