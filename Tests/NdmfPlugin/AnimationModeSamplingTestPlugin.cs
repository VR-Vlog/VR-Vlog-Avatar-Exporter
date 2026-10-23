using nadena.dev.ndmf;
using UnityEditor;
using UnityEngine;

[assembly: ExportsPlugin(typeof(VRVlog.LilToonExporter.Tests.AnimationModeSamplingTestPlugin))]

namespace VRVlog.LilToonExporter.Tests
{
    // A test-only Optimizing pass that samples a clip on the avatar root through
    // AnimationMode, as plugins that photograph menu icons do. It does nothing
    // until a test sets Armed, so a project that only installs the package is safe.
    // NdmfAnimationModeSamplingTests resets the counters before each export.
    // It runs on generic-platform avatars too, where AAO needs the lent controller.
#if VRVLOG_TEST_NDMF_PLATFORMS
    [RunsOnAllPlatforms]
#endif
    public sealed class AnimationModeSamplingTestPlugin : Plugin<AnimationModeSamplingTestPlugin>
    {
        public static bool Armed;
        // How many times the pass sampled, and the layer count of the root
        // controller it sampled with (-1 for none).
        public static int Sampled;
        public static int Layers;

        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing).Run("VR-Vlog AnimationMode sampling test", context => Sample(context.AvatarRootObject));
        }

        static void Sample(GameObject root)
        {
            if (!Armed) return;
            var controller = root.GetComponent<Animator>()?.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            Layers = controller == null ? -1 : controller.layers.Length;
            // Unity crashes in Animator::Sample on a controller without layers;
            // the test fails on Layers instead of losing the test runner.
            if (Layers == 0) return;
            var clip = new AnimationClip();
            clip.SetCurve("Front", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0, 1, .01f));
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(root, clip, 0);
                AnimationMode.EndSampling();
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(clip);
            }
            Sampled++;
        }
    }
}
