using System;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // A hidden copy of an avatar in its own preview scene. Its behaviours are
    // off and its Animator has no controller, so only the graphs played on it
    // move the copy. Dispose destroys the copy, then closes the scene; destroy
    // the graphs first.
    internal sealed class AnimatorProbe : IDisposable
    {
        Scene scene;

        internal GameObject Copy { get; private set; }
        internal Animator Animator { get; private set; }

        internal AnimatorProbe(GameObject avatar)
        {
            scene = EditorSceneManager.NewPreviewScene();
            try
            {
                Copy = Object.Instantiate(avatar);
                Copy.name = avatar.name;
                Copy.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(Copy, scene);
                foreach (var behaviour in Copy.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var animator = Copy.GetComponent<Animator>();
                if (animator == null) animator = Copy.AddComponent<Animator>();
                animator.runtimeAnimatorController = null;
                animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
                animator.fireEvents = false;
                Animator = animator;
                Copy.SetActive(true);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // A manually stepped graph that plays the controller on the copy.
        internal PlayableGraph Play(string name, RuntimeAnimatorController controller, string output, out AnimatorControllerPlayable playable)
        {
            var graph = PlayableGraph.Create(name);
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                playable = AnimatorControllerPlayable.Create(graph, controller);
                AnimationPlayableOutput.Create(graph, output, Animator).SetSourcePlayable(playable);
                return graph;
            }
            catch
            {
                graph.Destroy();
                throw;
            }
        }

        // Sets a Bool, Int (rounded) or Float input. False for any other type.
        internal static bool Write(AnimatorControllerPlayable playable, AnimatorControllerParameterType type, string name, float value)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Bool: playable.SetBool(name, value != 0f); return true;
                case AnimatorControllerParameterType.Int: playable.SetInteger(name, Mathf.RoundToInt(value)); return true;
                case AnimatorControllerParameterType.Float: playable.SetFloat(name, value); return true;
                default: return false;
            }
        }

        public void Dispose()
        {
            if (Copy != null) Object.DestroyImmediate(Copy);
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
