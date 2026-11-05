using System;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // Plays an FX controller on a copy of the avatar with Unity's own Animator
    // in a manually stepped graph. Tests take their expected values from this
    // playback, independently of the exporter's sampler.
    sealed class NativeFxPlayback : IDisposable
    {
        public const float Frame = 1f / 60f;
        public readonly GameObject Copy;
        public readonly PlayableGraph Graph;
        public readonly AnimatorControllerPlayable Playable;

        // isolate disables the copy's other behaviours so only the Animator
        // moves it; defaultWeights sets each layer above the base to the
        // controller's default weight. A valid scene receives the hidden copy.
        public NativeFxPlayback(GameObject source, RuntimeAnimatorController controller, bool isolate = false, bool defaultWeights = false,
            Scene scene = default)
        {
            Copy = Object.Instantiate(source);
            if (scene.IsValid()) { Copy.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(Copy, scene); }
            if (isolate) foreach (var behaviour in Copy.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
            var animator = Copy.GetComponent<Animator>(); animator.enabled = true;
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false; animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Graph = PlayableGraph.Create("Native FX reference"); Graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            Playable = AnimatorControllerPlayable.Create(Graph, controller);
            if (defaultWeights)
            {
                var layers = ((AnimatorController)controller).layers;
                for (var index = 0; index < layers.Length; index++) Playable.SetLayerWeight(index, index == 0 ? 1 : layers[index].defaultWeight);
            }
            AnimationPlayableOutput.Create(Graph, "Native FX", animator).SetSourcePlayable(Playable);
        }

        public void Start() { Graph.Play(); Graph.Evaluate(0); }

        public void Run(int frames, float step = Frame)
        {
            for (var frame = 0; frame < frames; frame++) Graph.Evaluate(step);
        }

        public SkinnedMeshRenderer Skin(string path) => Copy.transform.Find(path).GetComponent<SkinnedMeshRenderer>();

        public void Dispose()
        {
            if (Graph.IsValid()) Graph.Destroy();
            Object.DestroyImmediate(Copy);
        }
    }
}
