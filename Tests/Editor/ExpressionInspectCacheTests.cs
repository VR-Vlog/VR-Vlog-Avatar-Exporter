using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionInspectCacheTests
    {
        private string folder;
        private AnimatorController fx;
        private StateMachineBehaviour driver;
        private AnimatorOverrideController overrides;

        [SetUp]
        public void SetUp()
        {
            folder = TempAssetFolder.Create("__ExpressionInspectCache_");
            fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            fx.AddParameter("Menu", AnimatorControllerParameterType.Int);
            fx.AddParameter("Axis", AnimatorControllerParameterType.Float);
            fx.AddParameter("Source", AnimatorControllerParameterType.Float);
            fx.AddParameter("Target", AnimatorControllerParameterType.Float);
            var face = Clip("Face", clip =>
            {
                AnimationUtility.SetEditorCurve(clip, Morph("Body", "Open"), AnimationCurve.Linear(0, 0, 1, 100));
                AnimationUtility.SetEditorCurve(clip, Morph("Hair", "Bangs"), AnimationCurve.Constant(0, 1, 50));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "Axis"), AnimationCurve.Constant(0, 1, 1));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Prop", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1, 1));
            });
            var smile = Clip("Smile", clip => AnimationUtility.SetEditorCurve(clip, Morph("Body", "Smile"), AnimationCurve.Constant(0, 1, 100)));
            var machine = fx.layers[0].stateMachine;
            var neutral = machine.AddState("Neutral"); neutral.motion = face; neutral.writeDefaultValues = false;
            var selected = machine.AddState("Selected"); selected.motion = smile;
            machine.defaultState = neutral;
            var transition = neutral.AddTransition(selected); transition.hasExitTime = false;
            transition.AddCondition(AnimatorConditionMode.Equals, 1, "Menu");
            driver = ParameterDriverExpressionTests.Driver(selected, ParameterDriverExpressionTests.Op("Set", "Axis", 1),
                ParameterDriverExpressionTests.Op("Copy", "Target", source: "Source"));
            fx.AddLayer("Second");
            var second = fx.layers[1].stateMachine;
            var again = second.AddState("Again"); again.motion = face;
            var empty = second.AddState("Empty"); empty.motion = Clip("Empty", _ => { });
            again.AddTransition(empty).AddCondition(AnimatorConditionMode.Greater, .5f, "Axis");
            // Layer weight commands, an unknown callback and an empty state
            // exercise the copied control objects and the replayed messages.
            Control(empty, "VRCAnimatorLayerControl", "playable", .1f);
            Control(empty, "VRCPlayableLayerControl", "layer", 0);
            again.AddStateMachineBehaviour<UnknownStateCallbackProbe>();
            var idle = second.AddState("Idle");
            empty.AddTransition(idle).AddCondition(AnimatorConditionMode.Less, .5f, "Axis");
            overrides = new AnimatorOverrideController(fx) { name = "Overrides" };
            AssetDatabase.CreateAsset(overrides, folder + "/Overrides.overrideController");
            overrides[smile] = Clip("Wink", clip => AnimationUtility.SetEditorCurve(clip, Morph("Body", "Wink"), AnimationCurve.Constant(0, 1, 100)));
        }

        private static void Control(AnimatorState state, string type, string target, float duration)
        {
            var sdk = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Avatars.Components." + type)).First(t => t != null);
            using var data = new SerializedObject(state.AddStateMachineBehaviour(sdk));
            var property = data.FindProperty(target);
            property.enumValueIndex = Array.IndexOf(property.enumNames, "FX");
            if (target == "playable") data.FindProperty("layer").intValue = 0;
            data.FindProperty("goalWeight").floatValue = duration > 0 ? 0 : 1;
            data.FindProperty("blendDuration").floatValue = duration;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (folder != null) AssetDatabase.DeleteAsset(folder);
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

        private static readonly (Func<string, bool> Excluded, Dictionary<string, float> Neutral)[] Cases =
        {
            (null, null),
            (path => path == "Hair", null),
            (null, new Dictionary<string, float>(StringComparer.Ordinal) { ["Menu"] = 0 }),
            (path => path == "Body", new Dictionary<string, float>(StringComparer.Ordinal) { ["Menu"] = 1 }),
            (path => path == "Prop", new Dictionary<string, float>(StringComparer.Ordinal) { ["Menu"] = 1 }),
        };

        private static string Run(RuntimeAnimatorController runtime, int index, out ExpressionDependencies.Layer[] layers,
            out Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program> drivers, FxAnalysis analysis = null)
        {
            drivers = new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>();
            var unknown = new List<string>();
            layers = ExpressionDependencies.Inspect(runtime, Cases[index].Excluded, drivers, unknown, true, Cases[index].Neutral, analysis: analysis);
            return Snapshot(layers, drivers, unknown);
        }

        [Test]
        public void ReplayReturnsTheSameOrderedResultAsAFreshWalk()
        {
            var expected = Enumerable.Range(0, Cases.Length).Select(index => Run(fx, index, out _, out _)).ToArray();
            Assert.That(expected.Distinct().Count(), Is.EqualTo(Cases.Length), "Each case must observe a different result.");
            // The fixture reaches the copied control objects and the replayed messages.
            Run(fx, 0, out var layers, out _);
            Assert.That(layers[1].WeightControls.Count + layers[1].FxCommands.Count, Is.GreaterThan(0));
            Assert.That(expected[0], Does.Contain(nameof(UnknownStateCallbackProbe)));
            var cache = new FxAnalysis();
            for (var round = 0; round < 2; round++)
                for (var index = 0; index < Cases.Length; index++)
                    Assert.That(Run(fx, index, out _, out _, cache), Is.EqualTo(expected[index]), "round " + round + " case " + index);
            // A kept walk already left its excluded paths out, so the path
            // exclusion is part of the key: each case walks the graph once.
            Assert.That(cache.Misses, Is.EqualTo(Cases.Length));
            Assert.That(cache.Hits, Is.EqualTo(Cases.Length));
        }

        private string Run(IDictionary<string, ISet<float>> domains, Dictionary<string, float> neutral = null, bool typed = false,
            Func<string, bool> excluded = null, FxAnalysis analysis = null)
        {
            var drivers = new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>();
            var unknown = new List<string>();
            var layers = ExpressionDependencies.Inspect(fx, excluded ?? (path => path == "Hair"), drivers, unknown, true,
                neutral ?? new Dictionary<string, float>(StringComparer.Ordinal), typed, domains: domains, analysis: analysis);
            return Snapshot(layers, drivers, unknown);
        }

        private static Dictionary<string, ISet<float>> Domains(params float[] menu) =>
            new Dictionary<string, ISet<float>>(StringComparer.Ordinal) { ["Menu"] = new HashSet<float>(menu) };

        private static Dictionary<string, float> Fixed(string name, float value) =>
            new Dictionary<string, float>(StringComparer.Ordinal) { [name] = value };

        [Test]
        public void DomainsAreKeyedByContentAndReplayTheFreshWalk()
        {
            // Menu = 0 prunes the transition to Selected; adding 1 keeps it.
            // Every call builds new dictionaries and sets with the same content.
            Func<FxAnalysis, string>[] cases =
            {
                analysis => Run(Domains(0), analysis: analysis),
                analysis => Run(Domains(1, 0), analysis: analysis),
                analysis => Run(null, analysis: analysis),
                analysis => Run(new Dictionary<string, ISet<float>>(StringComparer.Ordinal), analysis: analysis),
                analysis => Run(Domains(), analysis: analysis),
                // Path exclusion is in the key: these walk again.
                analysis => Run(Domains(1, 0), excluded: path => false, analysis: analysis),
                analysis => Run(Domains(1, 0), excluded: path => path == "Body", analysis: analysis),
                // A fixed value of the same input wins over its domain.
                analysis => Run(Domains(1, 0), Fixed("Menu", 0), analysis: analysis),
                analysis => Run(Domains(0), Fixed("Axis", 0), analysis: analysis),
                analysis => Run(Domains(1, 0), typed: true, analysis: analysis),
                analysis => Run(Domains(0), typed: true, analysis: analysis),
            };
            var expected = cases.Select(run => run(null)).ToArray();
            Assert.That(expected[0], Does.Not.Contain("Smile"));
            Assert.That(expected[1], Does.Contain("Smile"));
            Assert.That(expected[7], Does.Not.Contain("Smile"));
            Assert.That(expected[9], Does.Contain("Smile"));
            Assert.That(expected[10], Does.Not.Contain("Smile"));
            Assert.That(new[] { expected[1], expected[5], expected[6] }.Distinct().Count(), Is.EqualTo(3));
            var cache = new FxAnalysis();
            for (var round = 0; round < 2; round++)
                for (var index = 0; index < cases.Length; index++)
                    Assert.That(cases[index](cache), Is.EqualTo(expected[index]), "round " + round + " case " + index);
            // Null domains, an empty dictionary and an empty set are separate entries.
            Assert.That(cache.Misses, Is.EqualTo(cases.Length));
            Assert.That(cache.Hits, Is.EqualTo(cases.Length));
            Assert.That(Run(Domains(0, 1), analysis: cache), Is.EqualTo(expected[1]));
            Assert.That(cache.Hits, Is.EqualTo(cases.Length + 1));
        }

        private sealed class SameFloat : IEqualityComparer<float>
        {
            public bool Equals(float x, float y) => x.Equals(y);
            public int GetHashCode(float value) => value.GetHashCode();
        }

        [Test]
        public void DomainsWithOtherComparersAreNotCached()
        {
            var ignoreCase = new Dictionary<string, ISet<float>>(StringComparer.OrdinalIgnoreCase) { ["Menu"] = new HashSet<float> { 0 } };
            var sorted = new Dictionary<string, ISet<float>>(StringComparer.Ordinal) { ["Menu"] = new SortedSet<float> { 0 } };
            var compared = new Dictionary<string, ISet<float>>(StringComparer.Ordinal) { ["Menu"] = new HashSet<float>(new SameFloat()) { 0 } };
            var cache = new FxAnalysis();
            foreach (var domains in new[] { ignoreCase, sorted, compared })
            {
                Run(domains, analysis: cache);
                Run(domains, analysis: cache);
            }
            Assert.That(cache.Hits + cache.Misses, Is.EqualTo(0));
        }

        [Test]
        public void OverrideControllersAreCachedSeparatelyFromTheirBase()
        {
            var expected = Run(overrides, 0, out var fresh, out _);
            Assert.That(fresh[0].Clips.Select(clip => clip.name), Does.Contain("Wink").And.Not.Contain("Smile"));
            var original = Run(fx, 0, out _, out _);
            var cache = new FxAnalysis();
            Assert.That(Run(fx, 0, out _, out _, cache), Is.EqualTo(original));
            Assert.That(Run(overrides, 0, out _, out _, cache), Is.EqualTo(expected));
            Assert.That(Run(overrides, 0, out _, out _, cache), Is.EqualTo(expected));
            Assert.That(Run(fx, 0, out _, out _, cache), Is.EqualTo(original));
            Assert.That(cache.Misses, Is.EqualTo(2));
            Assert.That(cache.Hits, Is.EqualTo(2));
        }

        [Test]
        public void ReplayGivesTheCallerFreshObjects()
        {
            // The caller gets its own array and driver map; the walked layers
            // are shared and read-only, and a caller that narrows one copies it.
            var cache = new FxAnalysis();
            Run(fx, 0, out var first, out var firstDrivers, cache);
            var original = first[0];
            first[0] = original.Copy();
            first[0].Reads.Add("Edited by caller");
            firstDrivers.Remove(driver);
            Run(fx, 0, out var second, out var secondDrivers, cache);
            Assert.That(cache.Hits, Is.EqualTo(1));
            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second[0], Is.SameAs(original));
            Assert.That(second[0].Reads, Does.Not.Contain("Edited by caller"));
            Assert.That(second[0].DriverPrograms[0].Operations.Count, Is.EqualTo(2));
            // The driver map and the layer list share one program, as in a fresh walk.
            Assert.That(secondDrivers[driver], Is.SameAs(second[0].DriverPrograms[0]));
            Assert.That(first[0].DriverPrograms[0], Is.SameAs(second[0].DriverPrograms[0]));
        }

        [Test]
        public void UnsavedControllersAreNotCached()
        {
            var probe = new AnimatorController { name = "Probe", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                probe.AddLayer("Base");
                probe.layers[0].stateMachine.AddState("Idle");
                var cache = new FxAnalysis();
                ExpressionDependencies.Inspect(probe, null, new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>(), new List<string>(), analysis: cache);
                ExpressionDependencies.Inspect(probe, null, new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>(), new List<string>(), analysis: cache);
                Assert.That(cache.Hits + cache.Misses, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(probe); }
        }

        [Test]
        public void ClosingTheScopeStopsCaching()
        {
            // There is no ambient scope: a call that is not handed the
            // analysis walks afresh and leaves the analysis untouched.
            var cache = new FxAnalysis();
            Run(fx, 0, out var kept, out _, cache);
            Run(fx, 0, out var fresh, out _);
            Assert.That(cache.Hits, Is.EqualTo(0));
            Assert.That(cache.Misses, Is.EqualTo(1));
            Assert.That(fresh[0], Is.Not.SameAs(kept[0]));
        }

        private static string Snapshot(ExpressionDependencies.Layer[] layers,
            Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program> drivers, List<string> unknown)
        {
            string Join<T>(IEnumerable<T> values, Func<T, string> text) => "[" + string.Join(";", values.Select(text)) + "]";
            string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);
            string Weight(SdkLayerWeightControl value) => value == null ? "-" : value.Location + "/" + value.AnimatorLayer + "/" + value.Playable +
                "/" + value.LayerIndex + "/" + Number(value.GoalWeight) + "/" + Number(value.BlendDuration) + "/" + value.SourceState?.name + "/" + value.FixedBaseLayer;
            string Program(VrChatParameterDriver.Program value) => value.Location + "/" + value.Error + "/" + value.LocalOnly + "/" + value.FxControl +
                "/" + Number(value.FxWeight) + "/" + Weight(value.LayerWeight) + Join(value.Operations, op => op.Kind + "/" + op.Destination + "/" +
                op.Source + "/" + Number(op.Value) + "/" + Number(op.SourceMin) + "/" + Number(op.SourceMax) + "/" + Number(op.DestinationMin) + "/" +
                Number(op.DestinationMax) + "/" + op.ConvertRange + "/" + op.UnresolvedRandom + "/" + op.Error);
            string Behaviour(StateMachineBehaviour value) => value == null ? "-" : value.GetInstanceID().ToString(CultureInfo.InvariantCulture);
            var text = layers.Select(layer => Join(layer.Reads, x => x) + Join(layer.ControlReads, x => x) +
                Join(layer.CopyInputs, x => x.Source + ">" + x.Destination) + Join(layer.Writes, x => x) + Join(layer.DriverWrites, x => x) +
                Join(layer.CurveWrites, x => x) + Join(layer.Morphs, x => x.path + "/" + x.propertyName) + Join(layer.Clips, x => x.name) +
                Join(layer.DriverPrograms, Program) + Join(layer.FxCommands, x => Behaviour(x.Key) + "=" + Program(x.Value)) +
                Join(layer.WeightControls, x => Behaviour(x.Key) + "=" + Weight(x.Value)) + "/" + layer.WriteDefaults + "/" + layer.HasBindings +
                "/" + layer.EmptyMotion + "/" + layer.FxControl + "/" + string.Join(",", layer.DynamicMorphs.Select(binding => binding.path + ":" + binding.propertyName).OrderBy(name => name, StringComparer.Ordinal)) + "/" + layer.Timed + "/" + layer.NonMorphBindings);
            return string.Join("\n", text) + "\n" + Join(drivers, x => Behaviour(x.Key) + "=" + Program(x.Value)) + Join(unknown, x => x);
        }
    }
}
