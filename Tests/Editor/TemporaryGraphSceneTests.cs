using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Sampling expressions builds hidden Animator graphs. Building them must
    // not mark the user's saved scene modified.
    public sealed class TemporaryGraphSceneTests
    {
        string folder;

        [SetUp] public void SetUp()
        {
            var name = "__TemporaryGraphScene_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
        }

        [TearDown] public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(folder);
        }

        static AnimatorStateMachine Layer(AnimatorController controller, string name)
        {
            controller.AddLayer(name); var layers = controller.layers;
            layers[layers.Length - 1].defaultWeight = 1; controller.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        AnimationClip Clip(AnimatorController controller, string name, EditorCurveBinding binding, float value)
        {
            var clip = new AnimationClip { name = name };
            AnimationUtility.SetEditorCurve(clip, binding, AnimationCurve.Constant(0, 1, value));
            AssetDatabase.AddObjectToAsset(clip, controller); return clip;
        }

        [Test]
        public void SamplingAnExpressionKeepsTheSavedSceneUnmodified()
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            controller.AddParameter("Face", AnimatorControllerParameterType.Int);
            var face = Layer(controller, "Face layer");
            var idle = face.AddState("Neutral"); idle.writeDefaultValues = false; face.defaultState = idle;
            idle.motion = Clip(controller, "Neutral", EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), 0);
            var smile = face.AddState("Smile"); smile.writeDefaultValues = false;
            smile.motion = Clip(controller, "Smile", EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Face size"), 75);
            var enter = idle.AddTransition(smile); enter.hasExitTime = false; enter.duration = 0;
            enter.AddCondition(AnimatorConditionMode.Equals, 1, "Face");
            // A layer that never touches the face is replaced by an idle layer
            // in the temporary graph.
            var unrelated = Layer(controller, "Unrelated");
            var still = unrelated.AddState("Unrelated state"); still.writeDefaultValues = false; unrelated.defaultState = still;
            still.motion = Clip(controller, "Unrelated", EditorCurveBinding.FloatCurve("Cheek", typeof(Transform), "m_LocalPosition.x"), .25f);

            var mesh = BaseShapeFixture.Create(); AssetDatabase.CreateAsset(mesh, folder + "/Mesh.asset");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatar = new GameObject("Avatar", typeof(Animator));
            avatar.GetComponent<Animator>().runtimeAnimatorController = controller;
            var body = new GameObject("Body", typeof(SkinnedMeshRenderer)); body.transform.SetParent(avatar.transform, false);
            body.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            new GameObject("Cheek").transform.SetParent(avatar.transform, false);
            AssetDatabase.SaveAssets();
            Assert.That(EditorSceneManager.SaveScene(scene, folder + "/Scene.unity"), Is.True);
            scene = EditorSceneManager.OpenScene(folder + "/Scene.unity", OpenSceneMode.Single);
            avatar = scene.GetRootGameObjects().Single(root => root.name == "Avatar");
            Assert.That(scene.isDirty, Is.False);

            var metadata = new VrChatExpressionMenu.Source { Controller = controller };
            var values = VrChatExpressionSampler.Sample(avatar, controller, metadata.Defaults,
                new Dictionary<string, float> { ["Face"] = 1 }, null, metadata);

            Assert.That(values.Single(value => value.Shape == "Face size").Weight, Is.EqualTo(75).Within(.01));
            Assert.That(scene.isDirty, Is.False);
        }
    }
}
