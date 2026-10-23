using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class FaceEmoPreparedFxIntegrationTests
    {
        [TestCase(false, false)]
        [TestCase(true, true)]
        public void RegisteredBranchUsesOriginalPlayerSlotAndNativeCommonWeightOnce(bool fractionalCommon, bool writeDefaults)
        {
            using var fixture = new Fixture(fractionalCommon, writeDefaults);
            var original = fixture.RegisteredClip;
            var snapshot = FaceEmoExpressions.CaptureRegistered(fixture.Avatar, fixture.Registered,
                deferPermanentOverrides: true);
            var old = fixture.Skin;
            fixture.ReplaceRenderer();
            snapshot.RebindPrepared(renderer => ReferenceEquals(renderer, old) ? fixture.Skin : renderer);
            var source = new VrChatExpressionMenu.Source { Controller = fixture.Controller };
            FaceEmoPreparedFxIntegrationTests.ReadListed(fixture.Avatar, fixture.Registered, source, bindings: snapshot);
            var entry = source.Entries.Single();
            Assert.That(entry.Error, Is.Null);
            Assert.That(entry.Values.All(value => value.Path == "Prepared/Merged"), Is.True);
            Assert.That(entry.Values.Single(value => value.Shape == "Pupil").Weight, Is.Zero, "FX is deferred until the common face is validated.");
            using var bindings = new PreparedExpressionBindings(fixture.Avatar, source);
            var expected = fixture.NativeSelectedWeights();
            FaceEmoExpressions.ApplyPreparedDefaultFace(fixture.Avatar, source, bindings, registeredBindings: snapshot);
            Assert.That(entry.Error, Is.Null);
            foreach (var pair in expected)
                Assert.That(entry.Values.Single(value => value.Shape == pair.Key).Weight,
                    Is.EqualTo(pair.Value).Within(.02f), pair.Key);
            Assert.That(entry.Values.Single(value => value.Shape == "Pupil").Weight, Is.EqualTo(50).Within(.02f));
            var result = entry.Values.Select(value => (value.Path, value.Shape, value.Weight)).ToArray();
            FaceEmoExpressions.ApplyPreparedDefaultFace(fixture.Avatar, source, bindings, registeredBindings: snapshot);
            Assert.That(entry.Values.Select(value => (value.Path, value.Shape, value.Weight)), Is.EqualTo(result),
                "A completed deferred evaluation cannot be applied twice.");
            Assert.That(AnimationUtility.GetCurveBindings(original).All(value => value.path == "Face"), Is.True);
            Assert.That(fixture.Skin.GetBlendShapeWeight(0), Is.EqualTo(20), "Native sampling must leave the prepared base untouched.");
        }

        [TestCase("ambiguous")]
        [TestCase("missing")]
        [TestCase("callback-changed")]
        [TestCase("motion-changed")]
        public void RegisteredPlayerRejectsAmbiguousMissingOrChangedCallbackProvenance(string kind)
        {
            using var fixture = new Fixture(false, false);
            var selected = fixture.Controller.layers[2].stateMachine.states.Single(child => child.state.motion == fixture.PreparedBranch).state;
            var driver = fixture.ConfigureSelectedDriver(selected);
            if (kind == "ambiguous")
            {
                var other = fixture.Controller.layers[2].stateMachine.AddState("Same registered clip with different callbacks");
                other.motion = fixture.PreparedBranch; other.writeDefaultValues = false;
                ParameterDriverExpressionTests.Driver(other, ParameterDriverExpressionTests.Op("Set", "Pattern setting", 0));
            }
            if (kind == "missing") selected.motion = fixture.Controller.layers[2].stateMachine.defaultState.motion;
            var snapshot = FaceEmoExpressions.CaptureRegistered(fixture.Avatar, fixture.Registered, deferPermanentOverrides: true);
            var old = fixture.Skin; fixture.ReplaceRenderer();
            snapshot.RebindPrepared(renderer => ReferenceEquals(renderer, old) ? fixture.Skin : renderer);
            var source = new VrChatExpressionMenu.Source { Controller = fixture.Controller };
            FaceEmoPreparedFxIntegrationTests.ReadListed(fixture.Avatar, fixture.Registered, source, bindings: snapshot);
            var entry = source.Entries.Single();
            if (kind == "callback-changed" || kind == "motion-changed")
            {
                Assert.That(entry.Error, Is.Null);
                if (kind == "motion-changed") selected.motion = fixture.Controller.layers[2].stateMachine.defaultState.motion;
                else
                {
                    using var data = new SerializedObject(driver);
                    data.FindProperty("parameters").GetArrayElementAtIndex(0).FindPropertyRelative("value").floatValue = 0;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                using var bindings = new PreparedExpressionBindings(fixture.Avatar, source);
                FaceEmoExpressions.ApplyPreparedDefaultFace(fixture.Avatar, source, bindings, registeredBindings: snapshot);
                Assert.That(entry.Error, Does.Contain("評価前に変わりました"));
            }
            else Assert.That(entry.Error, Does.Contain("FX状態").And.Contain(kind == "ambiguous" ? "重複" : "ありません"));
            Assert.That(fixture.Skin.GetBlendShapeWeight(1), Is.EqualTo(10));
        }

        [TestCase(true)]
        public void RegisteredPlayerReadsRegistryProvenPreparedAugmentationAndExactCallbacks(bool withDriver)
        {
            using var fixture = new Fixture(false, false);
            var selected = fixture.Controller.layers[2].stateMachine.states.Single(child => child.state.motion == fixture.PreparedBranch).state;
            if (withDriver) fixture.ConfigureSelectedDriver(selected);
            var snapshot = FaceEmoExpressions.CaptureRegistered(fixture.Avatar, fixture.Registered, deferPermanentOverrides: true);
            var old = fixture.Skin; fixture.ReplaceRenderer();
            AddPreparedChannel(fixture.PreparedBranch, 65);
            var registry = Registry(fixture.Avatar, fixture.RegisteredClip, fixture.PreparedBranch);
            snapshot.RebindPrepared(renderer => ReferenceEquals(renderer, old) ? fixture.Skin : renderer, registry);
            var beforeController = EditorJsonUtility.ToJson(fixture.Controller);
            var beforeClip = EditorJsonUtility.ToJson(fixture.RegisteredClip);
            var expected = fixture.NativeSelectedWeights(withDriver ? new Dictionary<string, float> { ["Pattern setting"] = 1 } : null);
            var source = new VrChatExpressionMenu.Source { Controller = fixture.Controller };
            FaceEmoPreparedFxIntegrationTests.ReadListed(fixture.Avatar, fixture.Registered, source, bindings: snapshot);
            var entry = source.Entries.Single(); Assert.That(entry.Error, Is.Null);
            Assert.That(entry.Values.Single(value => value.Shape == "Customization").Weight, Is.EqualTo(65),
                "The NDMF-added channel must enter the expression rather than merely permit state matching.");
            using var bindings = new PreparedExpressionBindings(fixture.Avatar, source);
            FaceEmoExpressions.ApplyPreparedDefaultFace(fixture.Avatar, source, bindings, registeredBindings: snapshot);
            Assert.That(entry.Error, Is.Null);
            foreach (var pair in expected)
                Assert.That(entry.Values.Single(value => value.Shape == pair.Key).Weight, Is.EqualTo(pair.Value).Within(.02), pair.Key);
            Assert.That(expected["Customization"], Is.EqualTo(65).Within(.02));
            Assert.That(entry.Values.All(value => value.Path == "Prepared/Merged"), Is.True);
            Assert.That(EditorJsonUtility.ToJson(fixture.Controller), Is.EqualTo(beforeController));
            Assert.That(EditorJsonUtility.ToJson(fixture.RegisteredClip), Is.EqualTo(beforeClip));
            Assert.That(AnimationUtility.GetCurveBindings(fixture.RegisteredClip).Length, Is.EqualTo(2));
            Assert.That(fixture.Skin.GetBlendShapeWeight(3), Is.EqualTo(30));
        }

        [TestCase("missing")]
        [TestCase("wrong-origin")]
        public void RegisteredPlayerRejectsUnprovedOrAmbiguousPreparedAugmentation(string kind)
        {
            using var fixture = new Fixture(false, false);
            var snapshot = FaceEmoExpressions.CaptureRegistered(fixture.Avatar, fixture.Registered, deferPermanentOverrides: true);
            var old = fixture.Skin; fixture.ReplaceRenderer(); AddPreparedChannel(fixture.PreparedBranch, 65);
            object registry = null;
            if (kind == "unknown") registry = new object();
            if (kind == "wrong-origin") registry = Registry(fixture.Avatar,
                fixture.Controller.layers[2].stateMachine.defaultState.motion, fixture.PreparedBranch);
            if (kind == "ambiguous")
            {
                registry = Registry(fixture.Avatar, fixture.RegisteredClip, fixture.PreparedBranch);
                var duplicate = fixture.Controller.layers[2].stateMachine.AddState("Another registered replacement");
                duplicate.motion = fixture.PreparedBranch; duplicate.writeDefaultValues = false;
            }
            snapshot.RebindPrepared(renderer => ReferenceEquals(renderer, old) ? fixture.Skin : renderer, registry);
            var source = new VrChatExpressionMenu.Source { Controller = fixture.Controller };
            FaceEmoPreparedFxIntegrationTests.ReadListed(fixture.Avatar, fixture.Registered, source, bindings: snapshot);
            Assert.That(source.Entries.Single().Error, Does.Contain("FX状態").And.Contain(kind == "ambiguous" ? "重複" : "ありません"));
            Assert.That(fixture.Skin.GetBlendShapeWeight(3), Is.EqualTo(30));
            Assert.That(AnimationUtility.GetCurveBindings(fixture.RegisteredClip).Length, Is.EqualTo(2));
        }

        [TestCase("clip-data")]
        public void RegisteredPlayerRejectsPreparedMotionChangesBeforeDeferredEvaluation(string kind)
        {
            using var fixture = new Fixture(false, false);
            var snapshot = FaceEmoExpressions.CaptureRegistered(fixture.Avatar, fixture.Registered, deferPermanentOverrides: true);
            var old = fixture.Skin; fixture.ReplaceRenderer(); AddPreparedChannel(fixture.PreparedBranch, 65);
            var registry = Registry(fixture.Avatar, fixture.RegisteredClip, fixture.PreparedBranch);
            snapshot.RebindPrepared(renderer => ReferenceEquals(renderer, old) ? fixture.Skin : renderer, registry);
            var overrides = kind == "effective-override" ? new AnimatorOverrideController(fixture.Controller) : null;
            AnimationClip changed = null;
            try
            {
                var source = new VrChatExpressionMenu.Source { Controller = overrides == null ? (RuntimeAnimatorController)fixture.Controller : overrides };
                FaceEmoPreparedFxIntegrationTests.ReadListed(fixture.Avatar, fixture.Registered, source, bindings: snapshot);
                var entry = source.Entries.Single(); Assert.That(entry.Error, Is.Null);
                if (overrides == null) AddPreparedChannel(fixture.PreparedBranch, 90);
                else
                {
                    changed = Object.Instantiate(fixture.PreparedBranch); AddPreparedChannel(changed, 90);
                    overrides[fixture.PreparedBranch] = changed;
                }
                using var bindings = new PreparedExpressionBindings(fixture.Avatar, source);
                FaceEmoExpressions.ApplyPreparedDefaultFace(fixture.Avatar, source, bindings, registeredBindings: snapshot);
                Assert.That(entry.Error, Does.Contain("評価前に変わりました"));
                Assert.That(fixture.Skin.GetBlendShapeWeight(3), Is.EqualTo(30));
            }
            finally { if (overrides != null) Object.DestroyImmediate(overrides); if (changed != null) Object.DestroyImmediate(changed); }
        }

        private static void AddPreparedChannel(AnimationClip clip, float value) => AnimationUtility.SetEditorCurve(clip,
            EditorCurveBinding.FloatCurve("Prepared/Merged", typeof(SkinnedMeshRenderer), "blendShape.Customization"),
            AnimationCurve.Constant(0, 1, value));

        // Reads each clip a FaceEmo registry lists as the list-only export
        // reads a listed clip FaceEmo registers.
        internal static void ReadListed(GameObject avatar, object registered, VrChatExpressionMenu.Source source,
            Func<string, bool> excludedPath = null, FaceEmoExpressions.BindingSnapshot bindings = null)
        {
            foreach (var clip in FaceEmoExpressions.CaptureRegistered(avatar, registered).RegisteredClips.ToArray())
            {
                var entry = new VrChatExpressionMenu.Entry { Id = "faceemo/list/" + source.Entries.Count, Name = clip.name,
                    ExportName = clip.name, Source = VRVlog.Expressions.ExpressionSource.FaceEmo };
                try { FaceEmoExpressions.ReadClip(avatar, clip, entry, source, excludedPath, bindings); }
                catch (InvalidOperationException error) { entry.Error = error.Message; }
                source.Entries.Add(entry);
            }
        }

        private static object Registry(GameObject avatar, Object original, Object prepared)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("nadena.dev.ndmf.ObjectRegistry"))
                .FirstOrDefault(value => value != null);
            if (type == null) Assert.Ignore("Optional NDMF ObjectRegistry is not installed in this project.");
            var registry = Activator.CreateInstance(type, new object[] { avatar.transform, null });
            var contract = type.GetInterfaces().Single(value => value.FullName == "nadena.dev.ndmf.IObjectRegistry");
            var reference = contract.GetMethod("GetReference", new[] { typeof(Object), typeof(bool) })
                .Invoke(registry, new object[] { original, true });
            contract.GetMethod("RegisterReplacedObject", new[] { reference.GetType(), typeof(Object) })
                .Invoke(registry, new object[] { reference, prepared });
            return registry;
        }

        private sealed class Fixture : IDisposable
        {
            internal readonly GameObject Avatar;
            internal readonly AnimatorController Controller;
            internal readonly AnimationClip RegisteredClip, PreparedBranch;
            internal SkinnedMeshRenderer Skin;
            internal readonly object Registered;
            private readonly Mesh mesh;
            private readonly string folder;

            internal Fixture(bool fractionalCommon, bool writeDefaults)
            {
                var descriptorType = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                    assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor")).FirstOrDefault(type => type != null);
                if (descriptorType == null) Assert.Ignore("Install the real VRChat SDK.");
                var name = "__FaceEmoPreparedFx_" + Guid.NewGuid().ToString("N");
                AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
                Controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
                Avatar = new GameObject("Avatar", typeof(Animator));
                var child = new GameObject("Face", typeof(SkinnedMeshRenderer)); child.transform.SetParent(Avatar.transform, false);
                Skin = child.GetComponent<SkinnedMeshRenderer>();
                mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                foreach (var shape in new[] { "Open", "Smile", "Pupil", "Customization" })
                    mesh.AddBlendShapeFrame(shape, 100, Enumerable.Repeat(Vector3.up * .1f, 3).ToArray(), null, null);
                Skin.sharedMesh = mesh; Weights();
                var descriptor = Avatar.AddComponent(descriptorType);
                using (var data = new SerializedObject(descriptor))
                {
                    data.FindProperty("customizeAnimationLayers").boolValue = true;
                    var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
                    var layer = layers.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                    type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                    layer.FindPropertyRelative("isDefault").boolValue = false;
                    layer.FindPropertyRelative("animatorController").objectReferenceValue = Controller;
                    data.ApplyModifiedPropertiesWithoutUndo();
                }
                var baseClip = Clip("Base", ("Customization", 30));
                SetState(0, "Base", baseClip, false);
                Controller.AddLayer("[ USER EDIT ] DEFAULT FACE");
                SetState(1, "DEFAULT", Clip("Common", ("Open", 80), ("Smile", 40), ("Pupil", 0)), false,
                    fractionalCommon ? .5f : 1);
                Controller.AddLayer("[ USER EDIT ] FACE EMOTE PLAYER");
                SetState(2, "Alternative mode default", Clip("Mode", ("Open", 60), ("Smile", 90), ("Pupil", 80)), writeDefaults);
                PreparedBranch = Clip("Prepared registered branch", ("Smile", 75), ("Pupil", 0));
                var branch = Controller.layers[2].stateMachine.AddState("Registered branch");
                branch.motion = PreparedBranch; branch.writeDefaultValues = writeDefaults;
                Controller.AddLayer("Permanent pupil");
                SetState(3, "Pupil", Clip("Pupil", ("Pupil", 100)), false, .5f);
                RegisteredClip = new AnimationClip { name = "Registered branch" };
                Curve(RegisteredClip, "Face", "Smile", 75); Curve(RegisteredClip, "Face", "Pupil", 0);
                AssetDatabase.CreateAsset(RegisteredClip, folder + "/Registered.anim");
                Registered = new { Modes = new[] { new { ChangeDefaultFace = true, DisplayName = "Registered",
                    Animation = new { GUID = AssetDatabase.AssetPathToGUID(folder + "/Registered.anim") } } } };
                AssetDatabase.SaveAssets();
            }

            private void Weights()
            { Skin.SetBlendShapeWeight(0, 20); Skin.SetBlendShapeWeight(1, 10); Skin.SetBlendShapeWeight(2, 10); Skin.SetBlendShapeWeight(3, 30); }

            internal void ReplaceRenderer()
            {
                var old = Skin;
                var parent = new GameObject("Prepared"); parent.transform.SetParent(Avatar.transform, false);
                var child = new GameObject("Merged", typeof(SkinnedMeshRenderer)); child.transform.SetParent(parent.transform, false);
                Skin = child.GetComponent<SkinnedMeshRenderer>(); Skin.sharedMesh = mesh; Weights();
                Object.DestroyImmediate(old.gameObject);
                // A historical path reused by another renderer cannot become
                // the registered target merely because the string matches.
                var reused = new GameObject("Face", typeof(SkinnedMeshRenderer)); reused.transform.SetParent(Avatar.transform, false);
                reused.GetComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            }

            private AnimationClip Clip(string name, params (string Shape, float Weight)[] values)
            {
                var clip = new AnimationClip { name = name }; AssetDatabase.AddObjectToAsset(clip, Controller);
                foreach (var value in values) Curve(clip, "Prepared/Merged", value.Shape, value.Weight);
                return clip;
            }
            private static void Curve(AnimationClip clip, string path, string shape, float weight) => AnimationUtility.SetEditorCurve(clip,
                EditorCurveBinding.FloatCurve(path, typeof(SkinnedMeshRenderer), "blendShape." + shape), AnimationCurve.Constant(0, 1, weight));
            private void SetState(int index, string name, AnimationClip clip, bool writeDefaults, float weight = 1)
            {
                var layers = Controller.layers; layers[index].defaultWeight = weight; layers[index].stateMachine.name = layers[index].name;
                Controller.layers = layers;
                var state = layers[index].stateMachine.AddState(name); state.motion = clip; state.writeDefaultValues = writeDefaults;
                layers[index].stateMachine.defaultState = state;
            }

            internal StateMachineBehaviour ConfigureSelectedDriver(AnimatorState selected)
            {
                Controller.AddParameter("Pattern setting", AnimatorControllerParameterType.Int);
                var driver = ParameterDriverExpressionTests.Driver(selected, ParameterDriverExpressionTests.Op("Set", "Pattern setting", 1));
                Controller.AddLayer("Driver-controlled face support");
                var layer = Controller.layers.Length - 1;
                SetState(layer, "Unselected setting", Clip("Unselected support", ("Smile", 0)), false);
                var machine = Controller.layers[layer].stateMachine;
                var active = machine.AddState("Selected setting"); active.motion = Clip("Selected support", ("Smile", 100)); active.writeDefaultValues = false;
                var transition = machine.defaultState.AddTransition(active); transition.hasExitTime = false; transition.duration = 0;
                transition.AddCondition(AnimatorConditionMode.Equals, 1, "Pattern setting");
                return driver;
            }

            internal Dictionary<string, float> NativeSelectedWeights(IDictionary<string, float> inputs = null,
                RuntimeAnimatorController runtime = null, AnimatorState selected = null)
            {
                var copy = Object.Instantiate(Avatar);
                var animator = copy.GetComponent<Animator>(); animator.runtimeAnimatorController = null;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.enabled = true;
                var machine = Controller.layers[2].stateMachine;
                var previous = machine.defaultState;
                var graph = PlayableGraph.Create("FaceEmo direct native reference");
                try
                {
                    machine.defaultState = selected ?? machine.states.Single(child => child.state.motion == PreparedBranch).state;
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var playable = AnimatorControllerPlayable.Create(graph, runtime ?? Controller);
                    if (inputs != null)
                        foreach (var input in inputs) playable.SetInteger(input.Key, Mathf.RoundToInt(input.Value));
                    AnimationPlayableOutput.Create(graph, "Reference", animator).SetSourcePlayable(playable);
                    graph.Play(); graph.Evaluate(0);
                    for (var frame = 0; frame < 120; frame++) graph.Evaluate(.01f);
                    var skin = copy.transform.Find("Prepared/Merged").GetComponent<SkinnedMeshRenderer>();
                    return Enumerable.Range(0, mesh.blendShapeCount).ToDictionary(mesh.GetBlendShapeName, skin.GetBlendShapeWeight);
                }
                finally { graph.Destroy(); machine.defaultState = previous; Object.DestroyImmediate(copy); }
            }

            public void Dispose()
            { Object.DestroyImmediate(Avatar); Object.DestroyImmediate(mesh); if (folder != null) AssetDatabase.DeleteAsset(folder); }
        }
    }
}
