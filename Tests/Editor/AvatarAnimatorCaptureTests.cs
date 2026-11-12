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
            AvatarAnimatorCapture.MaximumBytes = 4 * 1024 * 1024;
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

        // Unity plays the first layer at weight 1 whatever its default weight
        // says. A synced layer keeps its source and Timing option with a copy
        // of the source's machines and its own motions; a clip its Cycle Offset.
        [Test]
        public void ExportWritesTheFirstLayerAtFullWeightSyncedLayersAndCycleOffsets()
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
            layers[syncedIndex].defaultWeight = 0;
            layers[syncedIndex].syncedLayerIndex = hatIndex;
            layers[syncedIndex].syncedLayerAffectsTiming = true;
            fx.layers = layers;
            foreach (var child in layers[hatIndex].stateMachine.states) fx.SetStateEffectiveMotion(child.state, synced, syncedIndex);

            var data = Read(GlbDocument.Read(avatar.Export(new List<string>(), null)).Json);
            var written = data.Controllers.Single().Layers;
            Assert.That((written[0].Weight, written[0].SyncedLayer, written[0].SyncedLayerAffectsTiming), Is.EqualTo((1d, -1, false)));
            var hat = written[hatIndex];
            Assert.That((hat.SyncedLayer, hat.SyncedLayerAffectsTiming), Is.EqualTo((-1, false)));
            var copy = written[syncedIndex];
            Assert.That((copy.Name, copy.Weight, copy.SyncedLayer, copy.SyncedLayerAffectsTiming), Is.EqualTo(("Hat synced", 0d, hatIndex, true)));
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

        [Test]
        public void BlendShapeAndMaterialCurvesAreCountedNotWritten()
        {
            using var avatar = new AnimatorAvatar();
            var json = GlbDocument.Read(avatar.Export(new List<string>(), null)).Json;
            var data = Read(json);
            var nodes = Nodes(json);

            // "other" is the root's activeSelf curve, whose last key is past the hour the contract allows.
            Assert.That(data.Omitted.Select(o => (o.Kind, o.Count)), Is.EqualTo(new[] { ("blendShape", 1), ("material", 1), ("other", 1) }));
            Assert.That(data.Targets.Select(t => nodes[t.Node]).OrderBy(name => name), Is.EqualTo(new[] { "Glasses", "Hat" }));
            Assert.That(data.Targets.All(t => t.Property == "active"), Is.True);
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

        // The extension only adds: without it, its extensionsUsed entry and its
        // required version, the file is the one written when the stage fails.
        // The stage runs four times (snapshot, read, node mapping, injection);
        // a failure in any of them is left out, in strict mode too.
        [TestCase(1, true)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        public void TheExtensionLeavesTheRestOfTheFileAsItIs(int failingCall, bool continueOnStageFailure)
        {
            using var avatar = new AnimatorAvatar();
            var calls = 0;
            ExportTestHooks.FaultInjection = stage => { if (stage == ExportStage.AvatarAnimator) calls++; };
            var normal = avatar.Export(new List<string>(), null);
            Assert.That(calls, Is.EqualTo(4), "Snapshot, Capture, Resolve and Inject each run once.");

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
        public void AnExtensionOverTheLimitIsLeftOutWithAWarning()
        {
            using var avatar = new AnimatorAvatar();
            AvatarAnimatorCapture.MaximumBytes = 64;
            var warnings = new List<string>();
            var json = GlbDocument.Read(avatar.Export(warnings, null)).Json;

            Assert.That(((List<object>)json["extensionsUsed"]).Contains(AvatarAnimatorData.Extension), Is.False);
            var extras = (Dictionary<string, object>)((Dictionary<string, object>)json["asset"])["extras"];
            Assert.That(((Dictionary<string, object>)extras[AvatarFormatVersions.RequiredAppVersionsKey]).ContainsKey(AvatarAnimatorData.Extension), Is.False);
            Assert.That(warnings, Has.Member(ExporterLocalization.T(AvatarAnimatorCapture.TooLargeWarning)));
        }

        static AvatarAnimatorData Read(Dictionary<string, object> json) =>
            AvatarAnimatorData.Read(((Dictionary<string, object>)json["extensions"])[AvatarAnimatorData.Extension]);

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
                    var more = Menu();
                    Control(root, "Hat", "Toggle", "Hat", 1);
                    Control(root, "More", "SubMenu", subMenu: more);
                    var radial = Control(more, "Size", "RadialPuppet");
                    var subParameters = radial.GetType().GetField("subParameters");
                    var subParameter = Activator.CreateInstance(subParameters.FieldType.GetElementType());
                    VrcAvatar.Set(subParameter, "name", "Blend");
                    var array = Array.CreateInstance(subParameters.FieldType.GetElementType(), 1);
                    array.SetValue(subParameter, 0);
                    subParameters.SetValue(radial, array);
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

            static object Control(ScriptableObject menu, string name, string type, string parameterName = null, float value = 0, ScriptableObject subMenu = null)
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
                return clip;
            }

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
