using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarAnimator;
using VRVlog.AvatarFormats;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_avatar_animator in a real export: the FX controller, menus and
    // parameters of a VRChat avatar, read from the prepared copy. The tests
    // use the real SDK types through reflection and skip without them.
    public sealed class AvatarAnimatorCaptureTests
    {
        [TearDown]
        public void Reset()
        {
            ExportTestHooks.FaultInjection = null;
            AvatarAnimatorCapture.MaximumBytes = AvatarAnimatorCapture.MaximumMegabytes * 1024 * 1024;
        }

        [Test]
        public void ExportWritesTheToggleWithItsRestStateAndMenu()
        {
            using var avatar = new AnimatorAvatar();
            var warnings = new List<string>();
            var json = GlbDocument.Read(avatar.Export(warnings, null)).Json;

            Assert.That((List<object>)json["extensionsUsed"], Has.Member(AvatarAnimatorData.Extension), string.Join("\n", warnings));
            Assert.That(json.TryGetValue("extensionsRequired", out var required) && ((List<object>)required).Contains(AvatarAnimatorData.Extension), Is.False,
                "Apps that do not read the extension still open the file.");
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            Assert.That(((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey])[AvatarAnimatorData.Extension], Is.EqualTo("1.2.8"));

            var data = Read(json);
            var nodes = Nodes(json);
            var hat = data.Targets.Single(target => nodes[target.Node] == "Hat");
            Assert.That(hat.Property, Is.EqualTo("active"));
            Assert.That(hat.Rest, Is.EqualTo(0), "Hat is off by default, though the export turns its mesh on.");
            Assert.That(data.Targets.Single(target => nodes[target.Node] == "Glasses").Rest, Is.EqualTo(1));

            var layer = data.Controllers.Single().Layers.Single(l => l.Name == "Hat");
            double[] Values(string state)
            {
                var motion = data.Motions[layer.States.Single(s => s.Name == state).Motion];
                var track = data.Clips[motion.Clip].Tracks.Single(t => t.Target == data.Targets.IndexOf(hat));
                return track.Curves.Single().Keys.Select(key => key.Value).ToArray();
            }
            Assert.That(Values("Hat 0"), Is.EqualTo(new[] { 0d, 0d }));
            Assert.That(Values("Hat 1"), Is.EqualTo(new[] { 1d, 1d }));
            Assert.That(layer.States.Single(s => s.Name == "Hat 1").WriteDefaults, Is.False);

            var parameters = data.Parameters.ToDictionary(p => p.Name);
            Assert.That((parameters["Hat"].Origin, parameters["Hat"].Type, parameters["Hat"].Default, parameters["Hat"].Saved), Is.EqualTo(("expression", "bool", 0d, true)));
            Assert.That((parameters["Blend"].Origin, parameters["Blend"].Type, parameters["Blend"].Default), Is.EqualTo(("expression", "float", .5)));
            Assert.That((parameters["BlendX"].Origin, parameters["BlendX"].Type), Is.EqualTo(("animator", "float")));
            Assert.That(parameters["GestureLeft"].Origin, Is.EqualTo("builtin"));

            Assert.That(data.Menus, Has.Count.EqualTo(2));
            var toggle = data.Menus[0].Controls[0];
            Assert.That((toggle.Name, toggle.Type, toggle.Value), Is.EqualTo(("Hat", "toggle", 1d)));
            Assert.That(data.Parameters[toggle.Parameter.Value].Name, Is.EqualTo("Hat"));
            Assert.That((data.Menus[0].Controls[1].Type, data.Menus[0].Controls[1].Menu), Is.EqualTo(("subMenu", (int?)1)));
            var radial = data.Menus[1].Controls.Single();
            Assert.That(radial.Type, Is.EqualTo("radial"));
            Assert.That(radial.SubParameters.Select(index => data.Parameters[index].Name), Is.EqualTo(new[] { "Blend" }));

            Assert.That(avatar.Source.transform.Find("Hat").gameObject.activeSelf, Is.False, "The source is left as it was.");
        }

        [Test]
        public void ExportWritesSubMachinesAnyStateEntryBlendTreesAndDrivers()
        {
            using var avatar = new AnimatorAvatar();
            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);
            var parameters = data.Parameters.Select(p => p.Name).ToList();
            var layers = data.Controllers.Single().Layers;

            var hat = layers.Single(l => l.Name == "Hat");
            var any = hat.Machines.Single().AnyStateTransitions;
            Assert.That(any.Select(t => hat.States[t.ToState.Value].Name), Is.EqualTo(new[] { "Hat 0", "Hat 1" }));
            Assert.That(any.Select(t => t.Conditions.Single().Mode), Is.EqualTo(new[] { "ifNot", "if" }));
            Assert.That(any.All(t => t.Timed && !t.HasExitTime && parameters[t.Conditions[0].Parameter] == "Hat"), Is.True);

            var nested = layers.Single(l => l.Name == "Nested");
            Assert.That(nested.Machines.Select(m => (m.Name, m.Parent)), Is.EqualTo(new[] { ("Nested", -1), ("Inner", 0) }));
            var entry = nested.Machines[0].EntryTransitions.Single();
            Assert.That((entry.ToMachine, entry.Timed), Is.EqualTo(((int?)1, false)));
            var exit = nested.Machines[1].ExitTransitions.Single();
            Assert.That((exit.Exit, exit.Timed), Is.EqualTo((true, false)));
            var inner = nested.States.Single();
            Assert.That((inner.Name, inner.Machine), Is.EqualTo(("Inner", 1)));
            Assert.That(nested.Machines[1].DefaultState, Is.EqualTo(0));
            Assert.That((inner.Transitions.Single().Exit, inner.Transitions.Single().Timed), Is.EqualTo((true, true)));
            var driver = inner.Behaviours.Single();
            Assert.That(driver.Type, Is.EqualTo("parameterDriver"));
            var set = driver.Operations.Single();
            Assert.That((set.Type, parameters[set.Parameter], set.Value, set.Source), Is.EqualTo(("set", "BlendY", 1d, (int?)null)));

            var blend = layers.Single(l => l.Name == "Blend");
            var oneD = data.Motions[blend.States.Single(s => s.Name == "1D").Motion];
            Assert.That((oneD.Type, oneD.BlendType), Is.EqualTo(("blendTree", "1d")));
            Assert.That(oneD.Parameters.Select(p => parameters[p]), Is.EqualTo(new[] { "Blend" }));
            Assert.That(oneD.Children.Select(c => c.Threshold), Is.EqualTo(new[] { 0d, 1d }));
            var twoD = data.Motions[blend.States.Single(s => s.Name == "2D").Motion];
            Assert.That(twoD.BlendType, Is.EqualTo("2dSimpleDirectional"));
            Assert.That(twoD.Parameters.Select(p => parameters[p]), Is.EqualTo(new[] { "BlendX", "BlendY" }));
            Assert.That(twoD.Children.Select(c => (c.X, c.Y)), Is.EqualTo(new[] { (1d, 0d), (-1d, 0d) }));
            Assert.That(twoD.Children.Select(c => data.Motions[c.Motion].Type), Is.EqualTo(new[] { "clip", "clip" }));
            Assert.That(oneD.Children[1].Motion, Is.EqualTo(twoD.Children[0].Motion), "A clip used twice is written once.");
        }

        // Unity plays the first layer at weight 1 and as an override layer,
        // whatever its default weight and blending mode say; a later layer keeps
        // both. A synced layer keeps its source and Timing option with a copy
        // of the source's machines and its own motions; a clip its Cycle Offset.
        [Test]
        public void ExportWritesTheFirstLayerAsUnityPlaysItSyncedLayersAndCycleOffsets()
        {
            using var avatar = new AnimatorAvatar();
            var fx = avatar.Fx;
            var hatIndex = Array.FindIndex(fx.layers, l => l.name == "Hat");
            var synced = avatar.Clip("Synced");
            var settings = AnimationUtility.GetAnimationClipSettings(synced);
            settings.cycleOffset = .25f;
            AnimationUtility.SetAnimationClipSettings(synced, settings);
            fx.AddLayer("Hat synced");
            var layers = fx.layers;
            var syncedIndex = layers.Length - 1;
            layers[0].defaultWeight = 0;
            layers[0].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[hatIndex].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[syncedIndex].defaultWeight = 0;
            layers[syncedIndex].syncedLayerIndex = hatIndex;
            layers[syncedIndex].syncedLayerAffectsTiming = true;
            fx.layers = layers;
            foreach (var child in layers[hatIndex].stateMachine.states) fx.SetStateEffectiveMotion(child.state, synced, syncedIndex);

            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);
            var written = data.Controllers.Single().Layers;
            Assert.That((written[0].Weight, written[0].Blending, written[0].SyncedLayer, written[0].SyncedLayerAffectsTiming), Is.EqualTo((1d, "override", -1, false)));
            var hat = written[hatIndex];
            Assert.That((hat.Blending, hat.SyncedLayer, hat.SyncedLayerAffectsTiming), Is.EqualTo(("additive", -1, false)));
            var copy = written[syncedIndex];
            Assert.That((copy.Name, copy.Weight, copy.Blending, copy.SyncedLayer, copy.SyncedLayerAffectsTiming), Is.EqualTo(("Hat synced", 0d, "override", hatIndex, true)));
            Assert.That(copy.States.Select(s => s.Name), Is.EqualTo(hat.States.Select(s => s.Name)), "The synced layer holds a copy of its source's machines.");
            Assert.That(copy.Machines.Single().AnyStateTransitions, Has.Count.EqualTo(2));
            foreach (var state in copy.States)
            {
                var clip = data.Clips[data.Motions[state.Motion].Clip];
                // Saving the clip as an asset names it after its file.
                Assert.That((clip.Name, clip.CycleOffset), Is.EqualTo((synced.name, .25)), "The synced layer plays its own motions.");
            }
            Assert.That(hat.States.Select(s => data.Clips[data.Motions[s.Motion].Clip].CycleOffset), Is.EqualTo(new[] { 0d, 0d }));
        }

        // Unity runs a layer synced to a synced layer on the machines at the end
        // of the chain, with the layer's own motions.
        [Test]
        public void ALayerSyncedToASyncedLayerIsWrittenWithTheLayerThatOwnsTheMachines()
        {
            using var avatar = new AnimatorAvatar();
            var fx = avatar.Fx;
            var hatIndex = Array.FindIndex(fx.layers, l => l.name == "Hat");
            var once = avatar.Clip("Once");
            var twice = avatar.Clip("Twice");
            fx.AddLayer("Hat synced");
            fx.AddLayer("Hat synced twice");
            var layers = fx.layers;
            var first = layers.Length - 2;
            layers[first].defaultWeight = 0;
            layers[first].syncedLayerIndex = hatIndex;
            layers[first + 1].defaultWeight = 0;
            layers[first + 1].syncedLayerIndex = first;
            fx.layers = layers;
            foreach (var child in layers[hatIndex].stateMachine.states)
            {
                fx.SetStateEffectiveMotion(child.state, once, first);
                fx.SetStateEffectiveMotion(child.state, twice, first + 1);
            }

            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);
            var written = data.Controllers.Single().Layers;
            Assert.That(written.Skip(first).Select(l => l.SyncedLayer), Is.EqualTo(new[] { hatIndex, hatIndex }));
            var copy = written[first + 1];
            Assert.That(copy.States.Select(s => s.Name), Is.EqualTo(written[hatIndex].States.Select(s => s.Name)));
            Assert.That(copy.States, Is.Not.Empty);
            foreach (var state in copy.States)
                Assert.That(data.Clips[data.Motions[state.Motion].Clip].Name, Is.EqualTo(twice.name), "The layer plays its own motions.");
        }

        // -1 is a layer that is not synced. A source outside the layers counts as
        // none, and a chain that comes back to a layer it passed is not synced.
        [Test]
        public void TheOwnerOfASyncedLayersMachinesIsTheEndOfTheChain()
        {
            AnimatorControllerLayer[] Layers(params int[] sources) =>
                sources.Select(source => new AnimatorControllerLayer { syncedLayerIndex = source }).ToArray();
            int[] Owners(params int[] sources) =>
                Enumerable.Range(0, sources.Length).Select(index => AvatarAnimatorCapture.MachineOwner(Layers(sources), index)).ToArray();

            Assert.That(Owners(-1, 0, 1, 2), Is.EqualTo(new[] { -1, 0, 0, 0 }));
            Assert.That(Owners(-1, 1, 9, 2), Is.EqualTo(new[] { -1, -1, -1, 2 }), "Itself, outside the layers, and a source whose own source is outside.");
            Assert.That(Owners(1, 2, 0, 0, -1), Is.EqualTo(new[] { -1, -1, -1, -1, -1 }), "A loop, and a layer whose chain ends in one.");
        }

        // Only copy reads its source: a name left in another type's source
        // adds no parameter.
        [Test]
        public void ADriverSourceIsWrittenForCopyOnly()
        {
            using var avatar = new AnimatorAvatar();
            var nested = avatar.Fx.layers.Single(l => l.name == "Nested").stateMachine;
            var inner = nested.stateMachines.Single().stateMachine.states.Single().state;
            ParameterDriverExpressionTests.Driver(inner, ParameterDriverExpressionTests.Op("Set", "BlendY", 1, "Stale source"));

            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);
            Assert.That(data.Parameters.Select(p => p.Name), Has.No.Member("Stale source"));
            var behaviours = data.Controllers.Single().Layers.Single(l => l.Name == "Nested").States.Single().Behaviours;
            Assert.That(behaviours.Select(b => b.Operations.Single().Source), Is.EqualTo(new int?[] { null, null }));
        }

        // Hat on moves Front's Hair detail to 80. The scene holds it at 35,
        // which the export bakes into the base, and its one frame is at 100.
        // The file keeps the target the extension names, though no face uses it.
        [Test]
        public void BlendShapeCurvesAreWrittenAndMaterialCurvesCounted()
        {
            using var avatar = new AnimatorAvatar();
            var json = GlbDocument.Read(avatar.Export(new List<string>(), null)).Json;
            var data = Read(json);
            var nodes = Nodes(json);

            // "other" is the root's activeSelf curve, whose last key is past the hour the contract allows.
            Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("material", 1), ("other", 1) }));
            Assert.That(data.Targets.Where(t => t.Property == "active").Select(t => nodes[t.Node]).OrderBy(name => name), Is.EqualTo(new[] { "Glasses", "Hat" }));
            var morph = data.Targets.Single(t => t.Property == "morph");
            Assert.That(nodes[morph.Node], Is.EqualTo("Front"));
            Assert.That((morph.Rest, morph.Baked, morph.End), Is.EqualTo((35d, 35d, 100d)));
            Assert.That(TargetName(json, morph), Is.EqualTo("Hair detail"));

            var hatOn = data.Clips.Single(clip => clip.Name == avatar.ClipNamed("Hat on").name);
            var track = hatOn.Tracks.Single(t => t.Target == data.Targets.IndexOf(morph));
            Assert.That(track.Curves.Single().Keys.Select(key => key.Value), Is.EqualTo(new[] { 80d, 80d }));
            Assert.That(data.Clips.Where(clip => clip != hatOn).SelectMany(clip => clip.Tracks).Any(t => t.Target == data.Targets.IndexOf(morph)), Is.False);
        }

        // A clip that holds a shape at its scene weight changes nothing: it is
        // neither written nor counted.
        [Test]
        public void AShapeHeldAtItsSceneWeightIsNeitherWrittenNorCounted()
        {
            using var avatar = new AnimatorAvatar();
            AnimationUtility.SetEditorCurve(avatar.ClipNamed("Hat on"), FrontDetail, AnimationCurve.Constant(0, 1f / 60, 35));
            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);

            Assert.That(data.Targets.Any(t => t.Property == "morph"), Is.False);
            Assert.That(data.Omitted.Select(o => o.Kind), Is.EqualTo(new[] { "material", "other" }));
        }

        // A curve on a shape the mesh lacks is a blendShape the file cannot
        // carry; one on a path with no SkinnedMeshRenderer has no node.
        [Test]
        public void AShapeCurveWithoutItsShapeOrRendererIsCounted()
        {
            using var avatar = new AnimatorAvatar();
            var clip = avatar.ClipNamed("Hat on");
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Gone"), AnimationCurve.Constant(0, 1f / 60, 50));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Nowhere", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1f / 60, 50));
            var json = GlbDocument.Read(avatar.Export(new List<string>(), null)).Json;
            var data = Read(json);

            Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("blendShape", 1), ("material", 1), ("other", 1), ("missingNode", 1) }));
            Assert.That(Nodes(json)[data.Targets.Single(t => t.Property == "morph").Node], Is.EqualTo("Front"));
        }

        // An additive layer adds a shape's curve less its reference pose,
        // which the extension does not carry: a shape that a clip of such a
        // layer moves is counted as blendShape, though an override layer plays
        // the same clip.
        [Test]
        public void AShapeAnAdditiveLayerMovesIsCountedNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            avatar.Fx.AddLayer("Added");
            var layers = avatar.Fx.layers;
            layers[layers.Length - 1].blendingMode = AnimatorLayerBlendingMode.Additive;
            layers[layers.Length - 1].defaultWeight = 1;
            layers[layers.Length - 1].stateMachine.AddState("On").motion = avatar.ClipNamed("Hat on");
            avatar.Fx.layers = layers;
            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);

            Assert.That(data.Controllers.Single().Layers.Last().Blending, Is.EqualTo("additive"));
            Assert.That(data.Targets.Any(t => t.Property == "morph"), Is.False);
            Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("blendShape", 1), ("material", 1), ("other", 1) }));
        }

        // The base geometry takes the other Lip Sync shapes and the default
        // mouth off a Lip Sync shape too, which baked and end cannot carry:
        // a curve on one is counted as blendShape.
        [Test]
        public void ALipSyncShapeIsCountedNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var descriptor = avatar.Source.GetComponent(VrcAvatar.DescriptorType);
            var type = descriptor.GetType();
            var lipSync = type.GetField("lipSync");
            lipSync.SetValue(descriptor, Enum.Parse(lipSync.FieldType, "VisemeBlendShape"));
            type.GetField("VisemeSkinnedMesh").SetValue(descriptor, avatar.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Front"));
            var visemes = new string[15];
            visemes[10] = "Hair detail";
            type.GetField("VisemeBlendShapes").SetValue(descriptor, visemes);
            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);

            Assert.That(data.Targets.Any(t => t.Property == "morph"), Is.False);
            Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("blendShape", 1), ("material", 1), ("other", 1) }));
        }

        // What the export made of the shape decides whether it is written.
        // Sources that agree are one; none, a disagreement or a target that
        // shows no change (baked equals end) count as blendShape.
        [Test]
        public void AShapeIsWrittenOnlyFromSourcesThatAgreeAndShowAChange()
        {
            using var avatar = new AnimatorAvatar();
            var capture = AvatarAnimatorCapture.Capture(avatar.Source, null);
            var front = avatar.Source.transform.Find("Front").gameObject;
            AvatarAnimatorCapture.MorphSource Source(double baked) => new AvatarAnimatorCapture.MorphSource(35, baked, 100);
            foreach (var (sources, written) in new[]
            {
                (new[] { Source(35) }, true),
                (new[] { Source(35), Source(35) }, true),
                (new AvatarAnimatorCapture.MorphSource[0], false),
                (new[] { Source(35), Source(0) }, false),
                (new[] { Source(100) }, false),
            })
            {
                var data = capture.Resolve(o => o == front ? 0 : -1, (renderer, shape) =>
                    renderer.gameObject == front && shape == "Hair detail" ? sources : Array.Empty<AvatarAnimatorCapture.MorphSource>());
                var label = string.Join(",", sources.Select(s => s.Baked));
                var morph = data.Targets.SingleOrDefault(t => t.Property == "morph");
                Assert.That(morph != null, Is.EqualTo(written), label);
                Assert.That(data.Omitted.Any(o => o.Kind == "blendShape"), Is.EqualTo(!written), label);
                Assert.That(capture.MorphBinds.Count, Is.EqualTo(written ? 1 : 0), label);
                if (written) Assert.That((morph.Node, morph.Morph, morph.Rest, morph.Baked, morph.End), Is.EqualTo((0, 0, 35d, 35d, 100d)));
            }
            Assert.That(capture.Resolve(o => o == front ? 0 : -1).Targets.Any(t => t.Property == "morph"), Is.False, "No sources at all.");
        }

        // A curve that holds the scene weight still moves the shape when the
        // base geometry shows another weight (a curve baked in was left out):
        // it is written, and the app puts the shape back with a weight of -0.3.
        [Test]
        public void ACurveAtTheSceneWeightIsWrittenWhenTheBaseShowsAnother()
        {
            using var avatar = new AnimatorAvatar();
            AnimationUtility.SetEditorCurve(avatar.ClipNamed("Hat on"), FrontDetail, AnimationCurve.Constant(0, 1f / 60, 35));
            var capture = AvatarAnimatorCapture.Capture(avatar.Source, null);
            var front = avatar.Source.transform.Find("Front").gameObject;
            foreach (var (baked, written) in new[] { (50d, true), (35d, false) })
            {
                var data = capture.Resolve(o => o == front ? 0 : -1, (renderer, shape) => renderer.gameObject == front && shape == "Hair detail"
                    ? new[] { new AvatarAnimatorCapture.MorphSource(35, baked, 100) } : Array.Empty<AvatarAnimatorCapture.MorphSource>());
                var morph = data.Targets.SingleOrDefault(t => t.Property == "morph");
                Assert.That(morph != null, Is.EqualTo(written), baked.ToString());
                Assert.That(data.Omitted.Any(o => o.Kind == "blendShape"), Is.False, baked.ToString());
                if (written) Assert.That((35 - morph.Baked) / (morph.End - morph.Baked), Is.EqualTo(-0.3).Within(1e-9));
            }
        }

        // Shapes that would take the extension past the limit are all
        // counted, and the extension is written with activeSelf alone.
        [Test]
        public void ShapesOverTheSizeLimitAreCountedAndTheRestWritten()
        {
            using var avatar = new AnimatorAvatar();
            var capture = AvatarAnimatorCapture.Capture(avatar.Source, null);
            var front = avatar.Source.transform.Find("Front").gameObject;
            int Node(GameObject o) => o == front ? 0 : -1;
            IReadOnlyList<AvatarAnimatorCapture.MorphSource> Sources(SkinnedMeshRenderer renderer, string shape) =>
                new[] { new AvatarAnimatorCapture.MorphSource(35, 35, 100) };
            int Size(AvatarAnimatorData data) => System.Text.Encoding.UTF8.GetByteCount(JsonDom.Serialize(AvatarAnimatorData.Write(data)));
            var without = Size(capture.Resolve(Node));
            Assert.That(Size(capture.Resolve(Node, Sources)), Is.GreaterThan(without));

            AvatarAnimatorCapture.MaximumBytes = without;
            var data = capture.Resolve(Node, Sources);
            Assert.That(data.Targets.Any(t => t.Property == "morph"), Is.False);
            Assert.That(data.Omitted.Single(o => o.Kind == "blendShape").Count, Is.EqualTo(1));
            Assert.That(capture.MorphBinds, Is.Empty);
            Assert.That(Size(data), Is.EqualTo(without));
        }

        // A morph target must name one of its node's mesh's targets.
        [Test]
        public void InjectRejectsAMorphTargetItsNodeDoesNotHave()
        {
            using var avatar = new AnimatorAvatar();
            var exported = avatar.Export(new List<string>(), null);
            var json = GlbDocument.Read(exported).Json;
            var nodes = (List<object>)json["nodes"];
            Assert.That(AvatarAnimatorCapture.Inject(exported, Read(json)), Is.Not.Null);

            var data = Read(json);
            data.Targets.Single(t => t.Property == "morph").Morph = 1000;
            Assert.That(() => AvatarAnimatorCapture.Inject(exported, data), Throws.InvalidOperationException.With.Message.Contains("morph target"));
            data = Read(json);
            data.Targets.Single(t => t.Property == "morph").Node = nodes.FindIndex(node => !((Dictionary<string, object>)node).ContainsKey("mesh"));
            Assert.That(() => AvatarAnimatorCapture.Inject(exported, data), Throws.InvalidOperationException.With.Message.Contains("morph target"));
        }

        // BlendShape curves take the keys and tracks activeSelf leaves in
        // their clip, whatever order Unity lists the curves in.
        [Test]
        public void ShapeCurvesTakeWhatActiveSelfLeavesOfTheClip()
        {
            using var avatar = new AnimatorAvatar();
            (int Shape, int Kept) Counts()
            {
                var data = Captured(avatar);
                int Count(string kind) => data.Omitted.Where(o => o.Kind == kind).Sum(o => o.Count);
                return (Count("blendShape"), Count("missingNode"));
            }
            var before = Counts();
            var clip = avatar.Clip("Long");
            AnimationUtility.SetEditorCurve(clip, FrontDetail, AnimationCurve.Constant(0, 1f / 60, 50));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"),
                new AnimationCurve(Enumerable.Range(0, AvatarAnimatorData.MaximumKeysPerClip - 1).Select(index => new Keyframe(index / 60f, index % 2)).ToArray()));
            avatar.Fx.AddLayer("Long");
            avatar.Fx.layers.Last().stateMachine.AddState("Long").motion = clip;
            var after = Counts();
            // No object has a node here: the activeSelf curve kept is counted as missingNode.
            Assert.That((after.Shape - before.Shape, after.Kept - before.Kept), Is.EqualTo((1, 1)));

            // One track over the clip's limit.
            var wide = avatar.Clip("Wide");
            var bindings = Enumerable.Range(0, AvatarAnimatorData.MaximumTracksPerClip + 1)
                .Select(index => EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Shape " + index)).ToArray();
            AnimationUtility.SetEditorCurves(wide, bindings, bindings.Select(_ => AnimationCurve.Constant(0, 1f / 60, 1)).ToArray());
            avatar.Fx.AddLayer("Wide");
            avatar.Fx.layers.Last().stateMachine.AddState("Wide").motion = wide;
            var data = Captured(avatar);
            Assert.That(data.Omitted.Single(o => o.Kind == "blendShape").Count - after.Shape, Is.EqualTo(1));
        }

        [Test]
        public void AnAvatarWithoutADescriptorHasNoExtension()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
            using var avatar = new AttachmentConnectionTests.Fixture();
            var material = new Material(shader) { name = "Plain" };
            try
            {
                foreach (var renderer in avatar.Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                var json = GlbDocument.Read(Export(avatar.Source, new List<string>(), null)).Json;
                Assert.That(json.TryGetValue("extensionsUsed", out var used) && ((List<object>)used).Contains(AvatarAnimatorData.Extension), Is.False);
                Assert.That(json.TryGetValue("extensions", out var extensions) && ((Dictionary<string, object>)extensions).ContainsKey(AvatarAnimatorData.Extension), Is.False);
            }
            finally { Object.DestroyImmediate(material); }
        }

        // Without shapes to keep, the extension only adds: without it, its
        // extensionsUsed entry and its required version, the file is the one
        // written when the stage fails. The stage runs six times (activeSelf
        // and BlendShape snapshots, the shapes the FX moves, read, node
        // mapping, injection); a failure in any of them is left out, in strict
        // mode too.
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        public void TheExtensionLeavesTheRestOfTheFileAsItIs(int failingCall, bool continueOnStageFailure)
        {
            using var avatar = new AnimatorAvatar();
            AnimationUtility.SetEditorCurve(avatar.ClipNamed("Hat on"), FrontDetail, null);
            var calls = 0;
            ExportTestHooks.FaultInjection = stage => { if (stage == ExportStage.AvatarAnimator) calls++; };
            var normal = avatar.Export(new List<string>(), null);
            Assert.That(calls, Is.EqualTo(6), "Each of the six runs once.");

            calls = 0;
            ExportTestHooks.FaultInjection = stage =>
            {
                if (stage == ExportStage.AvatarAnimator && ++calls == failingCall) throw new InvalidOperationException("injected avatar animator");
            };
            var report = new ExportRecoveryReport { ContinueOnStageFailure = continueOnStageFailure };
            var warnings = new List<string>();
            var faulted = avatar.Export(warnings, report);

            Assert.That(report.Succeeded, Is.True);
            Assert.That(calls, Is.EqualTo(failingCall), "An omitted stage does not run again.");
            Assert.That(report.Fallbacks.Results.Single(result => result.Stage != null).Stage, Is.EqualTo(ExportStage.AvatarAnimator));
            Assert.That(warnings, Has.Some.Contains(ExportStageGuard.DisplayName(ExportStage.AvatarAnimator)));
            var withExtension = GlbDocument.Read(normal);
            Assert.That(withExtension.Json.ContainsKey("extensions") && ((Dictionary<string, object>)withExtension.Json["extensions"]).ContainsKey(AvatarAnimatorData.Extension), Is.True);
            var without = GlbDocument.Read(faulted);
            Assert.That(((List<object>)without.Json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension), Is.False);

            Assert.That(Strip(withExtension.Json), Is.EqualTo(JsonDom.Serialize(without.Json)));
            Assert.That(withExtension.Binary, Is.EqualTo(without.Binary));
        }

        [Test]
        public void AnExtensionOverTheLimitIsLeftOutAndListedWithTheReason()
        {
            using var avatar = new AnimatorAvatar();
            AvatarAnimatorCapture.MaximumBytes = 64;
            var warnings = new List<string>();
            var report = new ExportRecoveryReport { ContinueOnStageFailure = false };
            var json = GlbDocument.Read(avatar.Export(warnings, report)).Json;

            Assert.That(((List<object>)json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension), Is.False);
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            Assert.That(((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey]).ContainsKey(AvatarAnimatorData.Extension), Is.False);
            var entry = report.Fallbacks.Results.Single(result => result.Stage != null);
            Assert.That(entry.Stage, Is.EqualTo(ExportStage.AvatarAnimator));
            Assert.That(entry.Reason, Is.EqualTo(AvatarAnimatorCapture.TooLarge));
            Assert.That(entry.Reason, Does.Contain(AvatarAnimatorCapture.MaximumMegabytes + "MB"));
            Assert.That(warnings.Count(warning => warning.Contains(ExportStageGuard.DisplayName(ExportStage.AvatarAnimator))), Is.EqualTo(1));
        }

        // An extension of exactly the limit is written; one byte less of
        // room leaves it out.
        [Test]
        public void AnExtensionOfExactlyTheLimitIsWritten()
        {
            using var avatar = new AnimatorAvatar();
            var exported = avatar.Export(new List<string>(), new ExportRecoveryReport());
            var data = Read(GlbDocument.Read(exported).Json);
            var size = System.Text.Encoding.UTF8.GetByteCount(JsonDom.Serialize(AvatarAnimatorData.Write(data)));

            AvatarAnimatorCapture.MaximumBytes = size;
            var written = Read(GlbDocument.Read(AvatarAnimatorCapture.Inject(exported, data)).Json);
            Assert.That(written.Motions.Count, Is.EqualTo(data.Motions.Count));
            AvatarAnimatorCapture.MaximumBytes = size - 1;
            Assert.That(() => AvatarAnimatorCapture.Inject(exported, data),
                Throws.InvalidOperationException.With.Message.EqualTo(AvatarAnimatorCapture.TooLarge));
        }

        // Trees nested as deep as the format allows are written; one level more
        // leaves the stage out, and the export still succeeds.
        [Test]
        public void BlendTreesNestedOverTheLimitLeaveTheStageOut()
        {
            foreach (var depth in new[] { AvatarAnimatorData.MaximumBlendTreeDepth, AvatarAnimatorData.MaximumBlendTreeDepth + 1 })
            {
                using var avatar = new AnimatorAvatar();
                var fx = avatar.Fx;
                fx.AddLayer("Deep");
                fx.CreateBlendTreeInController("Deep", out var tree, fx.layers.Length - 1);
                for (var level = 1; level < depth; level++)
                {
                    tree.blendType = BlendTreeType.Simple1D;
                    tree.blendParameter = "Blend";
                    tree = tree.CreateBlendTreeChild(0);
                }
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = "Blend";
                tree.AddChild(avatar.Clip("Deep"), 0);

                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = false };
                var json = GlbDocument.Read(avatar.Export(warnings, report)).Json;
                var written = ((List<object>)json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension);
                Assert.That(written, Is.EqualTo(depth <= AvatarAnimatorData.MaximumBlendTreeDepth), "depth " + depth);
                if (written)
                {
                    Assert.That(Read(json).Motions.Count(motion => motion.Type == "blendTree"), Is.GreaterThanOrEqualTo(depth));
                    Assert.That(report.Fallbacks.Results.Any(result => result.Stage != null), Is.False);
                }
                else
                {
                    var entry = report.Fallbacks.Results.Single(result => result.Stage != null);
                    Assert.That(entry.Stage, Is.EqualTo(ExportStage.AvatarAnimator));
                    Assert.That(entry.Reason, Is.EqualTo(AvatarAnimatorCapture.TooDeep));
                    Assert.That(entry.Reason, Does.Contain(AvatarAnimatorData.MaximumBlendTreeDepth.ToString()));
                    Assert.That(warnings.Count(warning => warning.Contains(ExportStageGuard.DisplayName(ExportStage.AvatarAnimator))), Is.EqualTo(1));
                }
            }
        }

        // A tree two parents share is written once and counted at the deeper
        // of its two places: under the top tree it is 2 deep, at the end of
        // the chain it is chain + 2 deep.
        [Test]
        public void ASharedBlendTreeCountsAtItsDeepestPlace()
        {
            foreach (var chain in new[] { AvatarAnimatorData.MaximumBlendTreeDepth - 2, AvatarAnimatorData.MaximumBlendTreeDepth - 1 })
            {
                using var avatar = new AnimatorAvatar();
                var fx = avatar.Fx;
                fx.AddLayer("Shared");
                fx.CreateBlendTreeInController("Shared", out var top, fx.layers.Length - 1);
                top.blendType = BlendTreeType.Simple1D;
                top.blendParameter = "Blend";
                var shared = top.CreateBlendTreeChild(0);
                shared.blendType = BlendTreeType.Simple1D;
                shared.blendParameter = "Blend";
                var clip = avatar.Clip("Shared");
                shared.AddChild(clip, 0);
                var tree = top;
                for (var level = 0; level < chain; level++)
                {
                    tree = tree.CreateBlendTreeChild(1);
                    tree.blendType = BlendTreeType.Simple1D;
                    tree.blendParameter = "Blend";
                }
                tree.AddChild(shared, 0);
                Assert.That(top.children[0].motion, Is.EqualTo(shared), "the shared tree is walked under the top tree first");

                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = false };
                var json = GlbDocument.Read(avatar.Export(warnings, report)).Json;
                var written = ((List<object>)json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension);
                Assert.That(written, Is.EqualTo(chain + 2 <= AvatarAnimatorData.MaximumBlendTreeDepth), "chain " + chain);
                if (written)
                {
                    var data = Read(json);
                    var clipIndex = data.Clips.FindIndex(candidate => candidate.Name == clip.name);
                    var clipMotion = data.Motions.FindIndex(motion => motion.Type == "clip" && motion.Clip == clipIndex);
                    var sharedIndex = data.Motions.FindIndex(motion => motion.Children.Any(child => child.Motion == clipMotion));
                    Assert.That(data.Motions.Sum(motion => motion.Children.Count(child => child.Motion == sharedIndex)), Is.EqualTo(2));
                }
                else Assert.That(report.Fallbacks.Results.Single(result => result.Stage != null).Reason, Is.EqualTo(AvatarAnimatorCapture.TooDeep));
            }
        }

        // Machines are written root first, each before its children and the
        // children in Unity's order. A machine two parents hold is written
        // once, under the parent reached first: here A, which is walked
        // before the root's second child.
        [Test]
        public void MachinesAreWrittenEachBeforeItsChildrenAndASharedOneUnderTheParentReachedFirst()
        {
            using var avatar = new AnimatorAvatar();
            var fx = avatar.Fx;
            fx.AddLayer("Tree");
            var root = fx.layers.Last().stateMachine;
            var a = root.AddStateMachine("A");
            var b = root.AddStateMachine("B");
            var c = a.AddStateMachine("C");
            a.AddStateMachine(b, Vector3.zero);
            // States are made in another order than the machines: the walk decides.
            foreach (var machine in new[] { b, c, a, root }) machine.AddState(machine.name + " state");
            root.AddEntryTransition(b);

            var tree = Captured(avatar).Controllers.Single().Layers.Last();
            Assert.That(tree.Machines.Select(m => (m.Name, m.Parent)), Is.EqualTo(new[] { ("Tree", -1), ("A", 0), ("C", 1), ("B", 1) }));
            Assert.That(tree.States.Select(s => (s.Name, s.Machine)), Is.EqualTo(new[] { ("Tree state", 0), ("A state", 1), ("C state", 2), ("B state", 3) }));
            Assert.That(tree.Machines[0].EntryTransitions.Single().ToMachine, Is.EqualTo(3));
        }

        // A state on a synced layer is written with the behaviours that layer
        // itself sets for it. They are the ones Unity reports as effective for
        // the state on that layer: none without an override, and not those of
        // the layer it is synced to.
        [Test]
        public void ASyncedLayerIsWrittenWithTheBehavioursEffectiveOnIt()
        {
            using var avatar = new AnimatorAvatar();
            var fx = avatar.Fx;
            var nestedIndex = Array.FindIndex(fx.layers, l => l.name == "Nested");
            var inner = fx.layers[nestedIndex].stateMachine.stateMachines.Single().stateMachine.states.Single().state;
            var names = new[] { "No override", "Own driver", "Emptied" };
            foreach (var name in names) fx.AddLayer(name);
            var layers = fx.layers;
            var first = layers.Length - names.Length;
            for (var index = first; index < layers.Length; index++)
            {
                layers[index].defaultWeight = 0;
                layers[index].syncedLayerIndex = nestedIndex;
            }
            fx.layers = layers;
            // The driver is made on the state and moved to the synced layer.
            var source = inner.behaviours;
            var own = ParameterDriverExpressionTests.Driver(inner, ParameterDriverExpressionTests.Op("Set", "BlendX", 1));
            inner.behaviours = source;
            fx.SetStateEffectiveBehaviours(inner, first + 1, new[] { own });
            fx.SetStateEffectiveBehaviours(inner, first + 2, Array.Empty<StateMachineBehaviour>());

            var data = Captured(avatar);
            var written = data.Controllers.Single().Layers;
            string Sets(int layer) => string.Join(" ", written[layer].States.Single().Behaviours.Select(b => data.Parameters[b.Operations.Single().Parameter].Name));
            Assert.That(Sets(nestedIndex), Is.EqualTo("BlendY"), "The layer that owns the machines keeps its own.");
            Assert.That(names.Select((name, offset) => (name, fx.GetStateEffectiveBehaviours(inner, first + offset)?.Length ?? 0, Sets(first + offset))),
                Is.EqualTo(new[] { ("No override", 0, ""), ("Own driver", 1, "BlendX"), ("Emptied", 0, "") }),
                "The layer, how many behaviours Unity reports as effective for the state on it, and the parameters the written ones set.");
        }

        // A Parameter Driver sets only what Expression Parameters or the FX
        // controller declare. The same operations are read without and with
        // the declaration of Gone: without it, those that set Gone or copy from
        // it are counted and not written, Gone is no parameter, and the rest
        // are written as they are with it.
        [Test]
        public void ADriverOperationOnANameNothingDeclaresIsCountedNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var inner = avatar.Fx.layers.Single(l => l.name == "Nested").stateMachine.stateMachines.Single().stateMachine.states.Single().state;
            ParameterDriverExpressionTests.Driver(inner,
                ParameterDriverExpressionTests.Op("Set", "Gone", 1), ParameterDriverExpressionTests.Op("Add", "BlendX", 2),
                ParameterDriverExpressionTests.Op("Copy", "BlendY", 0, "Gone"), ParameterDriverExpressionTests.Op("Copy", "Gone", 0, "BlendX"),
                ParameterDriverExpressionTests.Op("Set", "Hat", 1), ParameterDriverExpressionTests.Op("Copy", "BlendX", 0, ""), ParameterDriverExpressionTests.Op("Copy", "BlendX"));

            (string Operations, int NotWritten, bool Parameter) Written()
            {
                var data = Captured(avatar);
                var driver = data.Controllers.Single().Layers.Single(l => l.Name == "Nested").States.Single().Behaviours[1];
                string Name(int? index) => index == null ? "" : " " + data.Parameters[index.Value].Name;
                return (string.Join(", ", driver.Operations.Select(o => o.Type + Name(o.Parameter) + " " + o.Value + Name(o.Source))),
                    data.Omitted.Where(o => o.Kind == "operation").Sum(o => o.Count), data.Parameters.Any(p => p.Name == "Gone"));
            }

            // A copy from no name has nothing to read: it is counted either way.
            Assert.That(Written(), Is.EqualTo(("add BlendX 2, set Hat 1", 5, false)));
            avatar.Fx.AddParameter("Gone", AnimatorControllerParameterType.Float);
            Assert.That(Written(), Is.EqualTo(("set Gone 1, add BlendX 2, copy BlendY 0 Gone, copy Gone 0 BlendX, set Hat 1", 2, true)));
        }

        // Unity never takes a transition with a condition it cannot read: on a
        // name its controller does not declare, of a mode that is not one of
        // the parameter type's (If and IfNot are a bool's, Greater and Less a
        // float's and an int's, Equals and NotEqual an int's), or of a mode it
        // has no name for. Such a transition is not written, whatever its
        // other conditions, and the name becomes no parameter.
        [Test]
        public void ATransitionWithAConditionUnityCannotReadIsNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var states = avatar.Fx.layers.Single(l => l.name == "Hat").stateMachine.states.Select(s => s.state).ToArray();
            var (off, on) = (states.Single(s => s.name == "Hat 0"), states.Single(s => s.name == "Hat 1"));
            void Add(params (AnimatorConditionMode Mode, string Parameter)[] conditions)
            {
                var transition = off.AddTransition(on);
                foreach (var (mode, parameter) in conditions) transition.AddCondition(mode, 0, parameter);
            }
            Add((AnimatorConditionMode.IfNot, "Hat"));
            Add((AnimatorConditionMode.Less, "Gone"));
            Add((AnimatorConditionMode.If, "Hat"), (AnimatorConditionMode.Greater, "Gone"));
            Add((AnimatorConditionMode.If, "Blend"));
            Add((AnimatorConditionMode.Equals, "Blend"));
            Add((AnimatorConditionMode.Greater, "Hat"));
            Add((AnimatorConditionMode.If, "GestureLeft"));
            Add((AnimatorConditionMode.If, "Hat"), ((AnimatorConditionMode)5, "Hat"));
            Add((AnimatorConditionMode.Less, "Blend"), (AnimatorConditionMode.Greater, "GestureLeft"), (AnimatorConditionMode.NotEqual, "GestureLeft"));
            Assert.That(off.transitions[7].conditions.Select(c => (int)c.mode), Is.EqualTo(new[] { (int)AnimatorConditionMode.If, 5 }), "Unity keeps the mode as it is set.");

            var data = Captured(avatar);
            var written = data.Controllers.Single().Layers.Single(l => l.Name == "Hat").States.Single(s => s.Name == "Hat 0").Transitions;
            Assert.That(written.Select(t => string.Join(" ", t.Conditions.Select(c => c.Mode + ":" + data.Parameters[c.Parameter].Name))),
                Is.EqualTo(new[] { "ifNot:Hat", "less:Blend greater:GestureLeft notEqual:GestureLeft" }));
            Assert.That(data.Parameters.Select(p => p.Name), Has.No.Member("Gone"));
        }

        // Unity never takes a muted transition, nor one that is not Solo
        // beside one that is. Neither is written, so the written transitions
        // are taken in order with nothing else to look at.
        [Test]
        public void MutedTransitionsAndThoseBesideASoloOneAreNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var states = avatar.Fx.layers.Single(l => l.name == "Hat").stateMachine.states.Select(s => s.state).ToArray();
            var (off, on) = (states.Single(s => s.name == "Hat 0"), states.Single(s => s.name == "Hat 1"));
            var first = off.AddTransition(on);
            first.AddCondition(AnimatorConditionMode.IfNot, 0, "Hat");
            var second = off.AddTransition(on);
            second.AddCondition(AnimatorConditionMode.If, 0, "Hat");
            string Written() => string.Join(", ", Captured(avatar).Controllers.Single().Layers.Single(l => l.Name == "Hat").States.Single(s => s.Name == "Hat 0")
                .Transitions.Select(t => string.Join(" ", t.Conditions.Select(c => c.Mode))));
            Assert.That(Written(), Is.EqualTo("ifNot, if"));
            first.mute = true;
            Assert.That(Written(), Is.EqualTo("if"));
            first.mute = false; second.solo = true;
            Assert.That(Written(), Is.EqualTo("if"));
            second.mute = true;
            Assert.That(Written(), Is.EqualTo(""), "A muted Solo transition still silences the others.");
            second.mute = false; second.solo = false;
            var gone = off.AddTransition(on);
            gone.solo = true;
            gone.AddCondition(AnimatorConditionMode.Greater, 0, "Gone");
            Assert.That(Written(), Is.EqualTo(""), "So does a Solo transition with a condition Unity cannot read.");

            var machine = avatar.Fx.layers.Single(l => l.name == "Nested").stateMachine;
            var inner = machine.stateMachines.Single().stateMachine;
            var after = machine.AddState("After");
            machine.AddStateMachineTransition(inner, after).mute = true;
            machine.AddStateMachineTransition(inner, after).solo = true;
            Assert.That(Captured(avatar).Controllers.Single().Layers.Single(l => l.Name == "Nested").Machines[1].ExitTransitions.Count, Is.EqualTo(3),
                "Unity takes a transition out of a machine whatever its Mute and Solo.");

            // The root's Any State and Entry transitions are written as Unity
            // takes them too, and a trigger reads If alone.
            var fx = avatar.Fx;
            fx.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            fx.AddLayer("Solo");
            var root = fx.layers.Last().stateMachine;
            var (a, b) = (root.AddState("A"), root.AddState("B"));
            var anyMuted = root.AddAnyStateTransition(a);
            anyMuted.AddCondition(AnimatorConditionMode.If, 0, "Hat");
            anyMuted.mute = true;
            root.AddAnyStateTransition(b).AddCondition(AnimatorConditionMode.IfNot, 0, "Hat");
            root.AddAnyStateTransition(a).AddCondition(AnimatorConditionMode.If, 0, "Jump");
            root.AddAnyStateTransition(b).AddCondition(AnimatorConditionMode.IfNot, 0, "Jump");
            root.AddEntryTransition(a).AddCondition(AnimatorConditionMode.If, 0, "Hat");
            var entrySolo = root.AddEntryTransition(b);
            entrySolo.AddCondition(AnimatorConditionMode.IfNot, 0, "Hat");
            entrySolo.solo = true;
            var data = Captured(avatar);
            var written = data.Controllers.Single().Layers.Single(l => l.Name == "Solo").Machines[0];
            string Modes(IEnumerable<AvatarAnimatorData.Transition> transitions) =>
                string.Join(", ", transitions.Select(t => string.Join(" ", t.Conditions.Select(c => c.Mode + ":" + data.Parameters[c.Parameter].Name))));
            Assert.That(Modes(written.AnyStateTransitions), Is.EqualTo("ifNot:Hat, if:Jump"));
            Assert.That(Modes(written.EntryTransitions), Is.EqualTo("ifNot:Hat"));
        }

        // Unity looks at the Any State transitions of a layer's root machine
        // alone: it never takes one that a machine inside another holds.
        [Test]
        public void AnAnyStateTransitionOfAMachineInsideAnotherIsNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var machine = avatar.Fx.layers.Single(l => l.name == "Nested").stateMachine;
            var inner = machine.stateMachines.Single().stateMachine;
            var state = inner.states.Single().state;
            machine.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.If, 0, "Hat");
            inner.AddAnyStateTransition(state).AddCondition(AnimatorConditionMode.IfNot, 0, "Hat");
            var machines = Captured(avatar).Controllers.Single().Layers.Single(l => l.Name == "Nested").Machines;
            Assert.That(machines.Select(m => string.Join(" ", m.AnyStateTransitions.Select(t => t.Conditions.Single().Mode))), Is.EqualTo(new[] { "if", "" }));
        }

        // What Unity plays when a motion or a state option names a parameter
        // its controller does not declare as a float: a 1D or 2D tree plays
        // nothing, a child of a direct tree has no weight, and the state
        // option is off.
        [Test]
        public void ATreeOrStateOptionOnAParameterUnityCannotReadIsWrittenAsUnityPlaysIt()
        {
            using var avatar = new AnimatorAvatar();
            var fx = avatar.Fx;
            fx.AddLayer("Unreadable");
            var layer = fx.layers.Length - 1;
            var clip = avatar.Clip("Unreadable");
            BlendTree Tree(string state, BlendTreeType type, string x, string y = null)
            {
                fx.CreateBlendTreeInController(state, out var tree, layer);
                tree.blendType = type;
                tree.blendParameter = x;
                if (y != null) tree.blendParameterY = y;
                tree.AddChild(clip, 0);
                return tree;
            }
            Tree("1D gone", BlendTreeType.Simple1D, "Gone");
            var onInt = Tree("1D int", BlendTreeType.Simple1D, "GestureLeft");
            fx.layers[layer].stateMachine.AddState("1D int again").motion = onInt;
            Tree("2D int", BlendTreeType.SimpleDirectional2D, "GestureLeft", "GestureLeft");
            Tree("2D gone", BlendTreeType.SimpleDirectional2D, "Blend", "Gone");
            var holder = Tree("Holder", BlendTreeType.Simple1D, "Blend");
            holder.AddChild(Tree("1D gone again", BlendTreeType.Simple1D, "Gone"), 1);
            var direct = Tree("Direct", BlendTreeType.Direct, "Blend");
            direct.AddChild(clip);
            var children = direct.children;
            children[0].directBlendParameter = "Gone";
            holder.AddChild(onInt, 2);
            fx.layers[layer].stateMachine.AddState("Direct again").motion = direct;
            children[1].directBlendParameter = "BlendX";
            direct.children = children;
            var machine = fx.layers[layer].stateMachine;
            foreach (var (name, parameter) in new[] { ("Options gone", "Gone"), ("Options float", "Blend"), ("Options int", "GestureLeft") })
            {
                var state = machine.AddState(name);
                state.motion = clip;
                state.speedParameterActive = state.timeParameterActive = state.cycleOffsetParameterActive = true;
                state.speedParameter = state.timeParameter = state.cycleOffsetParameter = parameter;
            }

            var data = Captured(avatar);
            var states = data.Controllers.Single().Layers.Single(l => l.Name == "Unreadable").States.ToDictionary(s => s.Name);
            Assert.That(new[] { "1D gone", "1D int", "1D int again", "2D int", "2D gone", "1D gone again" }.Select(name => states[name].Motion), Is.All.EqualTo(-1));
            Assert.That(data.Motions[states["Holder"].Motion].Children.Select(child => child.Motion < 0 ? "none" : data.Motions[child.Motion].Type), Is.EqualTo(new[] { "clip", "none", "none" }));
            Assert.That(data.Motions[states["Direct"].Motion].Children.Select(child => data.Parameters[child.DirectParameter.Value].Name), Is.EqualTo(new[] { "BlendX" }));
            string Options(string name) => string.Join(" ", new[] { states[name].SpeedParameter, states[name].TimeParameter, states[name].CycleOffsetParameter }
                .Select(index => index == null ? "off" : data.Parameters[index.Value].Name));
            Assert.That((Options("Options gone"), Options("Options float"), Options("Options int")), Is.EqualTo(("off off off", "Blend Blend Blend", "off off off")));
            Assert.That(data.Parameters.Select(p => p.Name), Has.No.Member("Gone"));
            // Unity reads some other float for a parameter of another type, so
            // each use is counted: the 1D tree on an int under two states and in
            // the holder, the 2D tree on two ints once, and three options. A
            // name nothing declares plays as written, but in a tree held by
            // another tree, which Unity plays by what the others leave, and on
            // a direct weight, which was not measured, here under two states:
            // three more.
            Assert.That(data.Omitted.Where(o => o.Kind == "unreadParameter").Sum(o => o.Count), Is.EqualTo(10));
        }

        // A clip keeps up to MaximumKeysPerClip keys. A curve that does not
        // fit in what the curves before it left is counted and not written;
        // one that fills the clip exactly is written.
        [Test]
        public void ACurveOverTheKeysLeftInItsClipIsCountedNotWritten()
        {
            foreach (var (keys, fits) in new[] { (AvatarAnimatorData.MaximumKeysPerClip - 2, true), (AvatarAnimatorData.MaximumKeysPerClip - 1, false) })
            {
                using var avatar = new AnimatorAvatar();
                // No object has a node here: a curve that is kept is counted as missingNode.
                (int Other, int Kept) Counts()
                {
                    var data = Captured(avatar);
                    int Count(string kind) => data.Omitted.Where(o => o.Kind == kind).Sum(o => o.Count);
                    return (Count("other"), Count("missingNode"));
                }
                var before = Counts();
                // Two curves, of 2 keys and of the keys given, whichever Unity lists first.
                var clip = avatar.Clip("Long", ("Glasses", true));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive"),
                    new AnimationCurve(Enumerable.Range(0, keys).Select(index => new Keyframe(index / 60f, index % 2)).ToArray()));
                avatar.Fx.AddLayer("Long");
                avatar.Fx.layers.Last().stateMachine.AddState("Long").motion = clip;
                var after = Counts();
                Assert.That((after.Other - before.Other, after.Kept - before.Kept), Is.EqualTo(fits ? (0, 2) : (1, 1)), keys + " keys");
            }
        }

        // A tree is written once and played under each tree that holds it.
        // Eleven trees, each holding the next twice and the last a clip
        // twice, expand to 4095 motions. With one more clip under the first
        // they are 4096, the most the format allows; a second clip leaves the
        // stage out, and the export still succeeds.
        [Test]
        public void BlendTreesExpandingOverTheLimitLeaveTheStageOut()
        {
            Assert.That(AvatarAnimatorData.MaximumExpandedMotions, Is.EqualTo(4096));
            foreach (var clips in new[] { 1, 2 })
            {
                using var avatar = new AnimatorAvatar();
                var fx = avatar.Fx;
                fx.AddLayer("Wide");
                fx.CreateBlendTreeInController("Wide", out var top, fx.layers.Length - 1);
                var clip = avatar.Clip("Wide");
                var tree = top;
                for (var level = 0; level < 11; level++)
                {
                    tree.blendType = BlendTreeType.Simple1D;
                    tree.blendParameter = "Blend";
                    if (level == 10) { tree.AddChild(clip, 0); tree.AddChild(clip, 1); break; }
                    var child = tree.CreateBlendTreeChild(0);
                    tree.AddChild(child, 1);
                    tree = child;
                }
                for (var extra = 0; extra < clips; extra++) top.AddChild(clip, 2 + extra);

                var warnings = new List<string>();
                var report = new ExportRecoveryReport { ContinueOnStageFailure = false };
                var json = GlbDocument.Read(avatar.Export(warnings, report)).Json;
                var written = ((List<object>)json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension);
                Assert.That(written, Is.EqualTo(clips == 1), clips + " clips");
                if (written)
                {
                    Assert.That(Read(json).Motions.Count, Is.LessThan(64), "A tree that two places hold is written once.");
                    Assert.That(report.Fallbacks.Results.Any(result => result.Stage != null), Is.False);
                }
                else
                {
                    var entry = report.Fallbacks.Results.Single(result => result.Stage != null);
                    Assert.That(entry.Stage, Is.EqualTo(ExportStage.AvatarAnimator));
                    Assert.That(entry.Reason, Is.EqualTo(AvatarAnimatorCapture.TooManyMotions));
                    Assert.That(entry.Reason, Does.Contain(AvatarAnimatorData.MaximumExpandedMotions.ToString()));
                    Assert.That(warnings.Count(warning => warning.Contains(ExportStageGuard.DisplayName(ExportStage.AvatarAnimator))), Is.EqualTo(1));
                }
            }
        }

        // A control sets only what Expression Parameters declare, as VRChat's
        // menu does. One that names a parameter of the FX controller alone,
        // or a name nothing declares, is written without a parameter.
        [Test]
        public void AMenuControlNamesOnlyWhatExpressionParametersDeclare()
        {
            using var avatar = new AnimatorAvatar();
            AnimatorAvatar.Control(avatar.RootMenu, "Animator only", "Toggle", "BlendX", 1);
            AnimatorAvatar.Control(avatar.RootMenu, "Nothing", "Toggle", "Gone", 1);
            AnimatorAvatar.SubParameters(AnimatorAvatar.Control(avatar.RootMenu, "Puppet", "TwoAxisPuppet"), "Blend", "BlendX");

            var data = Captured(avatar);
            Assert.That(data.Parameters.Select(p => p.Name), Has.No.Member("Gone"));
            var controls = data.Menus[0].Controls.ToDictionary(c => c.Name);
            Assert.That(data.Parameters[controls["Hat"].Parameter.Value].Name, Is.EqualTo("Hat"));
            Assert.That(controls["Animator only"].Parameter, Is.Null);
            Assert.That(controls["Nothing"].Parameter, Is.Null);
            Assert.That(controls["Puppet"].SubParameters.Select(index => index < 0 ? "" : data.Parameters[index].Name), Is.EqualTo(new[] { "Blend", "" }));
        }

        // What the stage reads from the avatar as it stands, through the
        // format's own checks. No object has a node here, so no curve is kept.
        static AvatarAnimatorData Captured(AnimatorAvatar avatar) =>
            AvatarAnimatorData.Read(AvatarAnimatorData.Write(AvatarAnimatorCapture.Capture(avatar.Source, null).Resolve(_ => -1)));

        static AvatarAnimatorData Read(Dictionary<string, object> json) =>
            AvatarAnimatorData.Read(((Dictionary<string, object>)json["extensions"])[AvatarAnimatorData.Extension]);

        static readonly EditorCurveBinding FrontDetail = EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail");

        // The name of the mesh target a morph target names.
        static string TargetName(Dictionary<string, object> json, AvatarAnimatorData.Target target)
        {
            var node = (Dictionary<string, object>)((List<object>)json["nodes"])[target.Node];
            var mesh = (Dictionary<string, object>)((List<object>)json["meshes"])[Convert.ToInt32(node["mesh"])];
            return (string)((List<object>)((Dictionary<string, object>)mesh["extras"])["targetNames"])[target.Morph];
        }

        static List<string> Nodes(Dictionary<string, object> json) =>
            ((List<object>)json["nodes"]).Cast<Dictionary<string, object>>().Select(node => node.TryGetValue("name", out var name) ? (string)name : null).ToList();

        static string Strip(Dictionary<string, object> json)
        {
            ((Dictionary<string, object>)json["extensions"]).Remove(AvatarAnimatorData.Extension);
            ((List<object>)json["extensionsUsed"]).Remove(AvatarAnimatorData.Extension);
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            ((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey]).Remove(AvatarAnimatorData.Extension);
            return JsonDom.Serialize(json);
        }

        static byte[] Export(GameObject source, ICollection<string> warnings, ExportRecoveryReport report) =>
            UniVrmOneClickExporter.Export(source, "Avatar animator", "Tests", warnings: warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                gimmickOptions: new ExportGimmickOptions { AutoExclude = false },
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);

        // A humanoid avatar with a VRChat descriptor, an Expression Menu with a
        // submenu, parameters and an FX controller of three layers:
        //   Hat: Hat (off by default) on and off from Any State; on also moves
        //     a blend shape and a material property.
        //   Nested: the entry goes into a sub-state machine whose state sets
        //     BlendY with a Parameter Driver and leaves through the exit.
        //   Blend: Glasses (on by default) in a 1D and a 2D blend tree.
        sealed class AnimatorAvatar : IDisposable
        {
            readonly AttachmentConnectionTests.Fixture avatar;
            internal GameObject Source => avatar.Source;
            internal AnimatorController Fx => fx;
            internal ScriptableObject RootMenu { get; private set; }
            readonly string directory;
            readonly List<Object> owned = new List<Object>();
            readonly Type menuType;
            readonly AnimatorController fx;
            int serial;

            internal AnimatorAvatar()
            {
                var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the real exporter entry point.");
                var descriptorType = VrcAvatar.DescriptorType;
                menuType = OptionalTools.Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu");
                var parameterType = OptionalTools.Find("VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters");
                if (descriptorType == null || menuType == null || parameterType == null || VrcAvatar.ComponentType("VRCAvatarParameterDriver") == null)
                    Assert.Ignore("Install the real VRChat SDK to read the FX controller and menus.");
                avatar = new AttachmentConnectionTests.Fixture();
                try
                {
                    directory = TempAssetFolder.Create("__VRVlogAvatarAnimatorTests_");
                    var material = new Material(shader) { name = "Animator" };
                    owned.Add(material);
                    foreach (var renderer in Source.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterial = material;
                    var mesh = new Mesh { name = "Animated part" };
                    mesh.vertices = new[] { new Vector3(-.1f, 1.6f, .1f), new Vector3(.1f, 1.6f, .1f), new Vector3(0, 1.8f, .1f) };
                    mesh.triangles = new[] { 0, 1, 2 };
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                    mesh.normals = Enumerable.Repeat(Vector3.forward, 3).ToArray();
                    owned.Add(mesh);
                    foreach (var (name, active) in new[] { ("Hat", false), ("Glasses", true) })
                    {
                        var part = new GameObject(name);
                        part.transform.SetParent(Source.transform, false);
                        part.AddComponent<MeshFilter>().sharedMesh = mesh;
                        part.AddComponent<MeshRenderer>().sharedMaterial = material;
                        part.SetActive(active);
                    }

                    var root = Menu();
                    RootMenu = root;
                    var more = Menu();
                    Control(root, "Hat", "Toggle", "Hat", 1);
                    Control(root, "More", "SubMenu", subMenu: more);
                    SubParameters(Control(more, "Size", "RadialPuppet"), "Blend");
                    EditorUtility.SetDirty(more);

                    var parameters = ScriptableObject.CreateInstance(parameterType);
                    owned.Add(parameters);
                    AssetDatabase.CreateAsset(parameters, directory + "/Parameters.asset");
                    var declared = new[] { ("Hat", "Bool", 0f), ("Blend", "Float", .5f) };
                    var parameterField = parameterType.GetField("parameters");
                    var rowType = parameterField.FieldType.GetElementType();
                    var rows = Array.CreateInstance(rowType, declared.Length);
                    for (var i = 0; i < declared.Length; i++)
                    {
                        var row = Activator.CreateInstance(rowType);
                        VrcAvatar.Set(row, "name", declared[i].Item1);
                        VrcAvatar.Set(row, "valueType", declared[i].Item2);
                        VrcAvatar.Set(row, "defaultValue", declared[i].Item3);
                        VrcAvatar.Set(row, "saved", i == 0);
                        rows.SetValue(row, i);
                    }
                    parameterField.SetValue(parameters, rows);

                    fx = AnimatorController.CreateAnimatorControllerAtPath(directory + "/FX.controller");
                    owned.Add(fx);
                    fx.AddParameter("Hat", AnimatorControllerParameterType.Bool);
                    fx.AddParameter(new AnimatorControllerParameter { name = "Blend", type = AnimatorControllerParameterType.Float, defaultFloat = .5f });
                    fx.AddParameter("BlendX", AnimatorControllerParameterType.Float);
                    fx.AddParameter("BlendY", AnimatorControllerParameterType.Float);
                    fx.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);

                    // Hat: one state per value, entered from Any State.
                    fx.AddLayer("Hat");
                    var machine = fx.layers.Last().stateMachine;
                    var hatOff = Clip("Hat off", ("Hat", false));
                    var hatOn = Clip("Hat on", ("Hat", true));
                    AnimationUtility.SetEditorCurve(hatOn, EditorCurveBinding.FloatCurve("Front", typeof(SkinnedMeshRenderer), "blendShape.Hair detail"), AnimationCurve.Constant(0, 1f / 60, 80));
                    AnimationUtility.SetEditorCurve(hatOn, EditorCurveBinding.FloatCurve("Hat", typeof(MeshRenderer), "material._Color.r"), AnimationCurve.Constant(0, 1f / 60, .5f));
                    // Outside the contract (a key past an hour): left out alone.
                    AnimationUtility.SetEditorCurve(hatOn, EditorCurveBinding.FloatCurve("", typeof(GameObject), "m_IsActive"), AnimationCurve.Constant(0, 7200, 1));
                    foreach (var (value, clip) in new[] { (0, hatOff), (1, hatOn) })
                    {
                        var state = machine.AddState("Hat " + value);
                        state.motion = clip;
                        state.writeDefaultValues = false;
                        if (value == 0) machine.defaultState = state;
                        var transition = machine.AddAnyStateTransition(state);
                        transition.canTransitionToSelf = false;
                        transition.hasExitTime = false;
                        transition.duration = 0;
                        transition.AddCondition(value != 0 ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Hat");
                    }

                    // Nested: entry -> Inner (a sub-state machine) -> exit.
                    fx.AddLayer("Nested");
                    machine = fx.layers.Last().stateMachine;
                    var sub = machine.AddStateMachine("Inner");
                    var inner = sub.AddState("Inner");
                    sub.defaultState = inner;
                    machine.AddEntryTransition(sub);
                    machine.AddStateMachineExitTransition(sub);
                    inner.AddExitTransition().hasExitTime = true;
                    ParameterDriverExpressionTests.Driver(inner, ParameterDriverExpressionTests.Op("Set", "BlendY", 1));

                    // Blend: the same two clips in a 1D and a 2D tree.
                    fx.AddLayer("Blend");
                    var blendLayer = fx.layers.Length - 1;
                    var glassesOff = Clip("Glasses off", ("Glasses", false));
                    var glassesOn = Clip("Glasses on", ("Glasses", true));
                    fx.CreateBlendTreeInController("1D", out var oneD, blendLayer);
                    oneD.blendType = BlendTreeType.Simple1D;
                    oneD.blendParameter = "Blend";
                    oneD.AddChild(glassesOff, 0);
                    oneD.AddChild(glassesOn, 1);
                    fx.CreateBlendTreeInController("2D", out var twoD, blendLayer);
                    twoD.blendType = BlendTreeType.SimpleDirectional2D;
                    twoD.blendParameter = "BlendX";
                    twoD.blendParameterY = "BlendY";
                    twoD.AddChild(glassesOn, new Vector2(1, 0));
                    twoD.AddChild(glassesOff, new Vector2(-1, 0));

                    var layers = fx.layers;
                    foreach (var layer in layers) layer.defaultWeight = 1;
                    fx.layers = layers;

                    var descriptor = Source.AddComponent(descriptorType);
                    VrcAvatar.Set(descriptor, "customExpressions", true);
                    VrcAvatar.Set(descriptor, "customizeAnimationLayers", true);
                    VrcAvatar.Set(descriptor, "expressionsMenu", root);
                    VrcAvatar.Set(descriptor, "expressionParameters", parameters);
                    Layers(descriptor, "baseAnimationLayers", new[] { "Base", "Additive", "Gesture", "Action", "FX" });
                    Layers(descriptor, "specialAnimationLayers", new[] { "Sitting", "TPose", "IKPose" });
                    foreach (var asset in owned.Where(EditorUtility.IsPersistent))
                    {
                        EditorUtility.SetDirty(asset);
                        AssetDatabase.SaveAssetIfDirty(asset);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal byte[] Export(ICollection<string> warnings, ExportRecoveryReport report) => AvatarAnimatorCaptureTests.Export(Source, warnings, report);

            ScriptableObject Menu()
            {
                var menu = ScriptableObject.CreateInstance(menuType);
                owned.Add(menu);
                AssetDatabase.CreateAsset(menu, directory + "/Menu" + serial++ + ".asset");
                return menu;
            }

            internal static void SubParameters(object control, params string[] names)
            {
                var field = control.GetType().GetField("subParameters");
                var rows = Array.CreateInstance(field.FieldType.GetElementType(), names.Length);
                for (var i = 0; i < names.Length; i++)
                {
                    var row = Activator.CreateInstance(field.FieldType.GetElementType());
                    VrcAvatar.Set(row, "name", names[i]);
                    rows.SetValue(row, i);
                }
                field.SetValue(control, rows);
            }

            internal static object Control(ScriptableObject menu, string name, string type, string parameterName = null, float value = 0, ScriptableObject subMenu = null)
            {
                var controls = (IList)VrChatExpressionMenu.Member(menu, "controls");
                var control = Activator.CreateInstance(controls.GetType().GetGenericArguments()[0]);
                controls.Add(control);
                VrcAvatar.Set(control, "name", name);
                VrcAvatar.Set(control, "type", type);
                VrcAvatar.Set(control, "value", value);
                VrcAvatar.Set(control, "subMenu", subMenu);
                if (parameterName != null)
                {
                    var parameter = Activator.CreateInstance(control.GetType().GetField("parameter").FieldType);
                    VrcAvatar.Set(parameter, "name", parameterName);
                    VrcAvatar.Set(control, "parameter", parameter);
                }
                EditorUtility.SetDirty(menu);
                return control;
            }

            internal AnimationClip Clip(string name, params (string Path, bool Active)[] objects)
            {
                var clip = new AnimationClip { name = name };
                foreach (var (path, active) in objects)
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive"),
                        AnimationCurve.Constant(0, 1f / 60, active ? 1 : 0));
                owned.Add(clip);
                AssetDatabase.CreateAsset(clip, directory + "/Clip" + serial++ + ".anim");
                clips[name] = clip;
                return clip;
            }

            // CreateAsset renames a clip after its file, so clips are found by the name they were made with.
            readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();

            internal AnimationClip ClipNamed(string name) => clips[name];

            void Layers(Component descriptor, string fieldName, string[] types)
            {
                var field = descriptor.GetType().GetField(fieldName);
                var rowType = field.FieldType.GetElementType();
                var rows = Array.CreateInstance(rowType, types.Length);
                for (var i = 0; i < types.Length; i++)
                {
                    var row = Activator.CreateInstance(rowType);
                    VrcAvatar.Set(row, "type", types[i]);
                    VrcAvatar.Set(row, "isDefault", false);
                    VrcAvatar.Set(row, "animatorController", fx);
                    rows.SetValue(row, i);
                }
                field.SetValue(descriptor, rows);
            }

            public void Dispose()
            {
                avatar?.Dispose();
                if (!string.IsNullOrEmpty(directory)) AssetDatabase.DeleteAsset(directory);
                foreach (var value in owned.Where(value => value != null && !EditorUtility.IsPersistent(value)).Reverse()) Object.DestroyImmediate(value);
                owned.Clear();
            }
        }
    }
}
