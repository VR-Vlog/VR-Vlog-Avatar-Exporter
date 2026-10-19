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
    // Every expression probe proves the same stationary FX layers. Inside an
    // inspect scope their graph checks come from the cache; the binding checks
    // still use each caller's path exclusion.
    public sealed class StationaryLayerCacheTests
    {
        private string folder;
        private AnimatorController fx;
        private AnimatorController other;
        private AnimatorOverrideController overrides;
        private AnimationClip hair;

        [SetUp]
        public void SetUp()
        {
            var name = "__StationaryLayerCache_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            fx = Controller("FX", 1, out hair);
            other = Controller("Other", 0, out _);
            overrides = new AnimatorOverrideController(fx) { name = "Overrides" };
            AssetDatabase.CreateAsset(overrides, folder + "/Overrides.overrideController");
            overrides[hair] = Clip("Wink", clip => AnimationUtility.SetEditorCurve(clip, Morph("Body", "Wink"), AnimationCurve.Linear(0, 0, 1, 100)));
        }

        [TearDown]
        public void TearDown()
        {
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        // Base: a constant face. Hair: a constant clip on two renderers.
        // Timed: a moving curve, which is never stationary.
        private AnimatorController Controller(string name, float hairWeight, out AnimationClip hairClip)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/" + name + ".controller");
            var body = Clip(name + "Body", clip => AnimationUtility.SetEditorCurve(clip, Morph("Body", "Smile"), AnimationCurve.Constant(0, 1, 100)));
            hairClip = Clip(name + "Hair", clip =>
            {
                AnimationUtility.SetEditorCurve(clip, Morph("Hair", "Bangs"), AnimationCurve.Constant(0, 1, 50));
                AnimationUtility.SetEditorCurve(clip, Morph("Body", "Blink"), AnimationCurve.Constant(0, 1, 0));
            });
            var timed = Clip(name + "Timed", clip => AnimationUtility.SetEditorCurve(clip, Morph("Body", "Open"), AnimationCurve.Linear(0, 0, 1, 100)));
            controller.AddLayer("Hair");
            controller.AddLayer("Timed");
            var motions = new[] { body, hairClip, timed };
            var layers = controller.layers;
            for (var index = 0; index < layers.Length; index++)
            {
                var state = layers[index].stateMachine.AddState("Rest"); state.motion = motions[index];
                layers[index].stateMachine.defaultState = state;
            }
            layers[1].defaultWeight = hairWeight;
            layers[2].defaultWeight = 1;
            controller.layers = layers;
            return controller;
        }

        private AnimationClip Clip(string name, Action<AnimationClip> curves)
        {
            var clip = new AnimationClip { name = name };
            curves(clip);
            AssetDatabase.CreateAsset(clip, folder + "/" + name + ".anim");
            return clip;
        }

        private static EditorCurveBinding Morph(string path, string shape) =>
            EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape);

        private static string Run(RuntimeAnimatorController runtime, Func<string, bool> excludedPath)
        {
            var layers = ExpressionDependencies.StationaryLayers(runtime, excludedPath);
            var bindings = ExpressionDependencies.StationaryMorphBindings(runtime, excludedPath);
            return string.Join(";", layers.Keys.Select(index => index + "=" + layers[index].name)) + "|" +
                string.Join(";", bindings.Select(binding => binding.path + "/" + binding.propertyName));
        }

        private (RuntimeAnimatorController Runtime, Func<string, bool> Excluded)[] Cases() => new (RuntimeAnimatorController, Func<string, bool>)[]
        {
            (fx, null),
            (fx, path => path == "Hair"),
            (fx, path => path == "Body"),
            (overrides, null),
            (other, null),
        };

        [Test]
        public void DifferentControllersAndExclusionsKeepTheirOwnResults()
        {
            var cases = Cases();
            var expected = cases.Select(item => Run(item.Runtime, item.Excluded)).ToArray();
            Assert.That(expected.Distinct().Count(), Is.EqualTo(cases.Length), "Each case must observe a different result.");
            using var cache = ExpressionInspectCache.Begin();
            for (var round = 0; round < 2; round++)
                for (var index = 0; index < cases.Length; index++)
                    Assert.That(Run(cases[index].Runtime, cases[index].Excluded), Is.EqualTo(expected[index]), "round " + round + " case " + index);
        }

        [Test]
        public void EditingAHitDoesNotChangeTheCache()
        {
            var expected = Run(fx, null);
            var candidates = ExpressionDependencies.StationaryCandidates(fx);
            var curves = AnimationUtility.GetCurveBindings(hair);
            using var cache = ExpressionInspectCache.Begin();
            var first = ExpressionDependencies.StationaryLayers(fx, null);
            first.Remove(0);
            first[2] = hair;
            var hit = cache.StationaryCandidates(fx);
            hit[0] = (9, null);
            var bindings = cache.CurveBindings(hair);
            bindings[0] = default;
            Assert.That(Run(fx, null), Is.EqualTo(expected));
            Assert.That(cache.StationaryCandidates(fx), Is.EqualTo(candidates));
            Assert.That(cache.StationaryCandidates(fx), Is.Not.SameAs(cache.StationaryCandidates(fx)));
            Assert.That(cache.CurveBindings(hair), Is.EqualTo(curves));
        }

        [Test]
        public void UnsavedControllersAreReadAgain()
        {
            var clip = new AnimationClip { name = "Probe face", hideFlags = HideFlags.HideAndDontSave };
            var probe = new AnimatorController { name = "Probe", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                AnimationUtility.SetEditorCurve(clip, Morph("Body", "Smile"), AnimationCurve.Constant(0, 1, 100));
                probe.AddLayer("Base");
                var machine = probe.layers[0].stateMachine;
                var state = machine.AddState("Rest"); state.motion = clip; machine.defaultState = state;
                using var cache = ExpressionInspectCache.Begin();
                Assert.That(ExpressionDependencies.StationaryLayers(probe).Keys, Is.EqualTo(new[] { 0 }));
                AnimationUtility.SetEditorCurve(clip, Morph("Body", "Smile"), AnimationCurve.Linear(0, 0, 1, 100));
                Assert.That(ExpressionDependencies.StationaryLayers(probe), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(probe);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
