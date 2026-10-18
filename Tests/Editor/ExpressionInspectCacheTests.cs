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

        [SetUp]
        public void SetUp()
        {
            var name = "__ExpressionInspectCache_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
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

        private string Run(RuntimeAnimatorController runtime, int index, out ExpressionDependencies.Layer[] layers,
            out Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program> drivers)
        {
            drivers = new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>();
            var unknown = new List<string>();
            layers = ExpressionDependencies.Inspect(runtime, Cases[index].Excluded, drivers, unknown, true, Cases[index].Neutral);
            return Snapshot(layers, drivers, unknown);
        }

        [Test]
        public void ReplayReturnsTheSameOrderedResultAsAFreshWalk()
        {
            var expected = Enumerable.Range(0, Cases.Length).Select(index => Run(fx, index, out _, out _)).ToArray();
            Assert.That(expected.Distinct().Count(), Is.EqualTo(Cases.Length), "Each case must observe a different result.");
            using var cache = ExpressionInspectCache.Begin();
            for (var round = 0; round < 2; round++)
                for (var index = 0; index < Cases.Length; index++)
                    Assert.That(Run(fx, index, out _, out _), Is.EqualTo(expected[index]), "round " + round + " case " + index);
            // Path exclusion is replayed; only the three neutral inputs walk the graph.
            Assert.That(cache.Misses, Is.EqualTo(3));
            Assert.That(cache.Hits, Is.EqualTo(Cases.Length * 2 - 3));
        }

        [Test]
        public void ReplayGivesTheCallerFreshObjects()
        {
            using var cache = ExpressionInspectCache.Begin();
            Run(fx, 0, out var first, out var firstDrivers);
            first[0].Reads.Add("Edited by caller");
            first[0].DriverPrograms[0].Operations.Clear();
            Run(fx, 0, out var second, out var secondDrivers);
            Assert.That(cache.Hits, Is.EqualTo(1));
            Assert.That(second[0].Reads, Does.Not.Contain("Edited by caller"));
            Assert.That(second[0].DriverPrograms[0].Operations.Count, Is.EqualTo(2));
            Assert.That(second[0].DriverPrograms[0], Is.Not.SameAs(first[0].DriverPrograms[0]));
            // The driver map and the layer list share one program, as in a fresh walk.
            Assert.That(secondDrivers[driver], Is.SameAs(second[0].DriverPrograms[0]));
            Assert.That(firstDrivers[driver], Is.SameAs(first[0].DriverPrograms[0]));
        }

        [Test]
        public void UnsavedControllersAreNotCached()
        {
            var probe = new AnimatorController { name = "Probe", hideFlags = HideFlags.HideAndDontSave };
            try
            {
                probe.AddLayer("Base");
                probe.layers[0].stateMachine.AddState("Idle");
                using var cache = ExpressionInspectCache.Begin();
                ExpressionDependencies.Inspect(probe, null, new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>(), new List<string>());
                ExpressionDependencies.Inspect(probe, null, new Dictionary<StateMachineBehaviour, VrChatParameterDriver.Program>(), new List<string>());
                Assert.That(cache.Hits + cache.Misses, Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(probe); }
        }

        [Test]
        public void ClosingTheScopeStopsCaching()
        {
            using (ExpressionInspectCache.Begin()) Assert.That(ExpressionInspectCache.Current, Is.Not.Null);
            Assert.That(ExpressionInspectCache.Current, Is.Null);
        }

        // The cache copies these types member by member. A new member must be
        // added to ExpressionInspectCache.Copy before this list is updated.
        [Test]
        public void CopiedTypesHaveNoUnlistedMembers()
        {
            string Fields(Type type) => string.Join(",", type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal));
            Assert.That(Fields(typeof(ExpressionDependencies.Layer)), Is.EqualTo("Clips,ControlReads,CopyInputs,CurveWrites,DriverPrograms," +
                "DriverWrites,DynamicMorph,EmptyMotion,FxCommands,FxControl,HasBindings,Morphs,NonMorphBindings,Reads,Timed,WeightControls," +
                "WriteDefaults,Writes"));
            Assert.That(Fields(typeof(VrChatParameterDriver.Program)), Is.EqualTo("Error,FxControl,FxWeight,LayerWeight,LocalOnly,Location,Operations"));
            Assert.That(Fields(typeof(VrChatParameterDriver.Operation)), Is.EqualTo("ConvertRange,Destination,DestinationMax,DestinationMin," +
                "Error,Kind,Source,SourceMax,SourceMin,UnresolvedRandom,Value"));
            Assert.That(Fields(typeof(SdkLayerWeightControl)), Is.EqualTo("AnimatorLayer,BlendDuration,FixedBaseLayer,GoalWeight,LayerIndex," +
                "Location,Playable,SourceState"));
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
                "/" + layer.EmptyMotion + "/" + layer.FxControl + "/" + layer.DynamicMorph + "/" + layer.Timed + "/" + layer.NonMorphBindings);
            return string.Join("\n", text) + "\n" + Join(drivers, x => Behaviour(x.Key) + "=" + Program(x.Value)) + Join(unknown, x => x);
        }
    }
}
