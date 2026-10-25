using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarFormats;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_lip_sync: the data contract, the transition length read from the
    // FX, the capture from a VRChat avatar and the extension in a file. The
    // capture tests use the real SDK types through reflection and skip without them.
    public sealed class LipSyncCaptureTests
    {
        private string folder;
        private GameObject avatar;
        private AnimatorController fx;

        [SetUp]
        public void SetUp()
        {
            var name = "__LipSyncCapture_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", name); folder = "Assets/" + name;
            fx = AnimatorController.CreateAnimatorControllerAtPath(folder + "/FX.controller");
            fx.AddParameter(LipSyncCapture.VisemeParameter, AnimatorControllerParameterType.Int);
            fx.AddParameter("Other", AnimatorControllerParameterType.Int);
        }

        [TearDown]
        public void TearDown()
        {
            if (avatar != null) Object.DestroyImmediate(avatar);
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }

        private static Dictionary<string, object> Json(bool dominant = true, float seconds = .05f) =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(LipSyncData.Write(new LipSyncData { DominantViseme = dominant, TransitionSeconds = seconds })));

        private static void Rejects(Action<Dictionary<string, object>> change)
        {
            var json = Json();
            change(json);
            Assert.Throws<InvalidOperationException>(() => LipSyncData.Read(json));
        }

        [Test]
        public void DataRoundTripsThroughJson()
        {
            Assert.That(JsonDom.Serialize(LipSyncData.Write(new LipSyncData { DominantViseme = true, TransitionSeconds = .05f })),
                Is.EqualTo("{\"schemaVersion\":1,\"dominantViseme\":true,\"transitionSeconds\":0.05}"));
            var read = LipSyncData.Read(Json());
            Assert.That(read.DominantViseme, Is.True);
            Assert.That(read.TransitionSeconds, Is.EqualTo(.05f).Within(1e-6f));
            // 0 and the limit are in range, and an integer number reads too.
            Assert.That(LipSyncData.Read(Json(seconds: 0)).TransitionSeconds, Is.EqualTo(0f));
            Assert.That(LipSyncData.Read(Json(false, 10)).DominantViseme, Is.False);
            var integral = Json();
            integral["transitionSeconds"] = 1L;
            Assert.That(LipSyncData.Read(integral).TransitionSeconds, Is.EqualTo(1f));
        }

        [Test]
        public void DataOutsideTheContractIsRejected()
        {
            Rejects(json => json.Remove("transitionSeconds"));
            Rejects(json => json.Remove("dominantViseme"));
            Rejects(json => json["extra"] = true);
            Rejects(json => json["schemaVersion"] = 2L);
            Rejects(json => json["schemaVersion"] = "1");
            Rejects(json => json["dominantViseme"] = 1L);
            Rejects(json => json["dominantViseme"] = "true");
            Rejects(json => json["transitionSeconds"] = -.001);
            Rejects(json => json["transitionSeconds"] = 10.001);
            Rejects(json => json["transitionSeconds"] = double.NaN);
            Rejects(json => json["transitionSeconds"] = double.PositiveInfinity);
            Rejects(json => json["transitionSeconds"] = "0.05");
            Assert.Throws<InvalidOperationException>(() => LipSyncData.Read(new List<object>()));
            Assert.Throws<InvalidOperationException>(() => LipSyncData.Read(null));
            Assert.Throws<InvalidOperationException>(() => LipSyncData.Write(null));
            Assert.Throws<InvalidOperationException>(() => LipSyncData.Write(new LipSyncData { DominantViseme = true, TransitionSeconds = 11 }));
        }

        [Test]
        public void FixedVisemeTransitionsGiveTheirLength()
        {
            var machine = Layer("Mouth");
            var idle = State(machine, "Idle", null);
            for (var value = 0; value < 15; value++) Any(machine, State(machine, "v" + value, null), true, .05f, value);
            // Transitions on other parameters are not lip sync.
            Any(machine, idle, true, .3f, 1, "Other");
            Any(machine, idle, true, .3f, 2, "Other");
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.05f).Within(1e-6f));
        }

        [Test]
        public void MostCommonLengthWinsAndTiesTakeTheShorter()
        {
            var machine = Layer("Mouth");
            var mouth = State(machine, "Mouth", null);
            Any(machine, mouth, true, .1f, 0); Any(machine, mouth, true, .1f, 1); Any(machine, mouth, true, .1f, 2);
            Any(machine, mouth, true, .05f, 3); Any(machine, mouth, true, .05f, 4);
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.1f).Within(1e-6f));
            Any(machine, mouth, true, .05f, 5);
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.05f).Within(1e-6f));
        }

        [Test]
        public void NormalizedLengthsUseTheClipOfTheStateLeft()
        {
            var machine = Layer("Mouth");
            var clip = new AnimationClip { name = "Two seconds" };
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.vrc.v_aa"), AnimationCurve.Linear(0, 0, 2, 100));
            AssetDatabase.AddObjectToAsset(clip, fx);
            var timed = State(machine, "Timed", clip);
            var empty = State(machine, "Empty", null);
            // .25 of a 2 second clip is .5; without a clip .2 is .2 seconds.
            Any(machine, timed, false, .25f, 0); Any(machine, timed, false, .25f, 1);
            Any(machine, empty, false, .2f, 2);
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.5f).Within(1e-6f));
            Any(machine, empty, false, .2f, 3); Any(machine, empty, false, .2f, 4);
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.2f).Within(1e-6f));
            // A state's own transitions scale by the state they leave: .25 out of the
            // 2 second clip into the empty state is .5, the other way .25 seconds.
            foreach (var (source, destination, value) in new[] { (timed, empty, 5), (timed, empty, 6), (timed, empty, 7), (empty, timed, 8) })
            {
                var transition = source.AddTransition(destination);
                transition.hasExitTime = false; transition.hasFixedDuration = false; transition.duration = .25f;
                transition.AddCondition(AnimatorConditionMode.Equals, value, LipSyncCapture.VisemeParameter);
            }
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.5f).Within(1e-6f));
        }

        [Test]
        public void NoVisemeTransitionGivesZero()
        {
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(0f));
            var machine = Layer("Other");
            Any(machine, State(machine, "Other", null), true, .3f, 1, "Other");
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(0f));
        }

        [Test]
        public void SubStateMachineTransitionsCount()
        {
            var machine = Layer("Mouth");
            Any(machine, State(machine, "Root", null), true, .1f, 0);
            var sub = machine.AddStateMachine("Visemes");
            var from = State(sub, "From", null);
            var to = State(sub, "To", null);
            foreach (var (source, destination, value) in new[] { (from, to, 1), (to, from, 2) })
            {
                var transition = source.AddTransition(destination);
                transition.hasExitTime = false; transition.hasFixedDuration = true; transition.duration = .07f;
                transition.AddCondition(AnimatorConditionMode.Equals, value, LipSyncCapture.VisemeParameter);
            }
            Assert.That(LipSyncCapture.TransitionSeconds(fx), Is.EqualTo(.07f).Within(1e-6f));
        }

        [Test]
        public void AvatarWithoutTheSdkHasNoLipSync()
        {
            avatar = new GameObject("Avatar", typeof(Animator));
            var warnings = new List<string>();
            Assert.That(LipSyncCapture.Capture(avatar, warnings), Is.Null);
            Assert.That(warnings, Is.Empty);
            var bytes = new byte[] { 1, 2, 3 };
            Assert.That(LipSyncCapture.Inject(bytes, null), Is.SameAs(bytes));
        }

        [Test]
        public void VisemeParameterOnlyAvatarIsCaptured()
        {
            var machine = Layer("Mouth");
            for (var value = 0; value < 15; value++) Any(machine, State(machine, "v" + value, null), true, .05f, value);
            if (!BuildAvatar("VisemeParameterOnly", fx)) return;
            var warnings = new List<string>();
            var data = LipSyncCapture.Capture(avatar, warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(data, Is.Not.Null);
            Assert.That(data.DominantViseme, Is.True);
            Assert.That(data.TransitionSeconds, Is.EqualTo(.05f).Within(1e-6f));
        }

        [Test]
        public void VisemeParameterOnlyWithoutVisemeTransitionsWritesZero()
        {
            if (!BuildAvatar("VisemeParameterOnly", fx)) return;
            var data = LipSyncCapture.Capture(avatar, new List<string>());
            Assert.That(data, Is.Not.Null);
            Assert.That(data.DominantViseme, Is.True);
            Assert.That(data.TransitionSeconds, Is.EqualTo(0f));
        }

        [TestCase("VisemeBlendShape")]
        [TestCase("JawFlapBlendShape")]
        [TestCase("Default")]
        public void OtherLipSyncModesWriteNothing(string lipSync)
        {
            var machine = Layer("Mouth");
            Any(machine, State(machine, "Mouth", null), true, .05f, 1);
            if (!BuildAvatar(lipSync, fx)) return;
            var warnings = new List<string>();
            Assert.That(LipSyncCapture.Capture(avatar, warnings), Is.Null);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void UnreadableFxWarnsAndWritesNothing()
        {
            // An override without a base controller cannot be read.
            var overrides = new AnimatorOverrideController { name = "No base" };
            AssetDatabase.CreateAsset(overrides, folder + "/Override.overrideController");
            if (!BuildAvatar("VisemeParameterOnly", overrides)) return;
            var warnings = new List<string>();
            Assert.That(LipSyncCapture.Capture(avatar, warnings), Is.Null);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.StartWith(LipSyncCapture.FailureWarning));
        }

        [Test]
        public void InjectedExtensionIsDeclaredWithItsAppVersion()
        {
            var bytes = GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["extensionsUsed"] = new List<object> { "VRMC_vrm" },
                ["extensions"] = new Dictionary<string, object> { ["VRMC_vrm"] = new Dictionary<string, object>() }
            }, Array.Empty<byte>()).Write();
            var injected = LipSyncCapture.Inject(bytes, new LipSyncData { DominantViseme = true, TransitionSeconds = .05f });
            var json = GlbDocument.Read(AvatarFormatRequirements.Write(injected)).Json;
            Assert.That((List<object>)json["extensionsUsed"], Is.EqualTo(new object[] { "VRMC_vrm", LipSyncData.Extension }));
            var extensions = (Dictionary<string, object>)json["extensions"];
            Assert.That(extensions.ContainsKey("VRMC_vrm"), Is.True);
            var written = LipSyncData.Read(extensions[LipSyncData.Extension]);
            Assert.That(written.DominantViseme, Is.True);
            Assert.That(written.TransitionSeconds, Is.EqualTo(.05f).Within(1e-6f));
            Assert.That(json.ContainsKey("extensionsRequired"), Is.False);
            var asset = (Dictionary<string, object>)json["asset"];
            var required = (Dictionary<string, object>)((Dictionary<string, object>)asset["extras"])[AvatarFormatVersions.RequiredAppVersionsKey];
            Assert.That(required.Keys, Is.EqualTo(new[] { LipSyncData.Extension }));
            Assert.That(required[LipSyncData.Extension], Is.EqualTo("1.0.9"));
        }

        private AnimatorStateMachine Layer(string name)
        {
            fx.AddLayer(name); var layers = fx.layers; layers[layers.Length - 1].defaultWeight = 1; fx.layers = layers;
            return layers[layers.Length - 1].stateMachine;
        }

        private static AnimatorState State(AnimatorStateMachine machine, string name, Motion motion)
        {
            var state = machine.AddState(name); state.motion = motion; state.writeDefaultValues = true;
            if (machine.states.Length == 1) machine.defaultState = state;
            return state;
        }

        private static void Any(AnimatorStateMachine machine, AnimatorState state, bool fixedDuration, float duration, int value,
            string parameter = LipSyncCapture.VisemeParameter)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false; transition.canTransitionToSelf = false;
            transition.hasFixedDuration = fixedDuration; transition.duration = duration;
            transition.AddCondition(AnimatorConditionMode.Equals, value, parameter);
        }

        // An avatar whose descriptor has the Lip Sync mode and the FX layer.
        private bool BuildAvatar(string lipSync, RuntimeAnimatorController controller)
        {
            var descriptorType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("VRC.SDK3.Avatars.Components.VRCAvatarDescriptor", false)).FirstOrDefault(type => type != null);
            if (descriptorType == null) { Assert.Ignore("Install the real VRChat SDK."); return false; }
            avatar = new GameObject("Avatar", typeof(Animator));
            var descriptor = avatar.AddComponent(descriptorType);
            using (var data = new SerializedObject(descriptor))
            {
                data.FindProperty("customizeAnimationLayers").boolValue = true;
                var layers = data.FindProperty("baseAnimationLayers"); layers.arraySize = 1;
                var layer = layers.GetArrayElementAtIndex(0); var type = layer.FindPropertyRelative("type");
                type.enumValueIndex = Array.IndexOf(type.enumNames, "FX");
                layer.FindPropertyRelative("isDefault").boolValue = false;
                layer.FindPropertyRelative("animatorController").objectReferenceValue = controller;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
            var field = descriptorType.GetField("lipSync");
            field.SetValue(descriptor, Enum.Parse(field.FieldType, lipSync));
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
