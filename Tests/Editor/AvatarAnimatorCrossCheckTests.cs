using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // AvatarAnimatorCrossCheck on a small controller built twice, as Unity's
    // AnimatorController and as the data the exporter would write for it:
    //   Hat: the toggle Hat turns the object Hat on (off at rest).
    //   Smile: the left Fist blends the BlendShape Smile of Body to 100 over 0.25 s.
    // Needs no VRChat SDK; the export with the SDK is in AvatarAnimatorCaptureTests.
    public sealed class AvatarAnimatorCrossCheckTests
    {
        const int Hat = 0, Gesture = 1;
        TempAssetFolder folder;
        readonly List<Object> owned = new List<Object>();

        [SetUp]
        public void Create() => folder = new TempAssetFolder("__VRVlogCrossCheckTests_");

        [TearDown]
        public void Clean()
        {
            foreach (var value in owned.Where(value => value != null && !EditorUtility.IsPersistent(value))) Object.DestroyImmediate(value);
            owned.Clear();
            folder.Dispose();
            AvatarAnimatorCrossCheck.BudgetSeconds = 60;
        }

        [Test]
        public void EveryInputMatchesUnity()
        {
            var (root, controller, data, bindings) = Build();
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            Assert.That(result.Unsupported, Is.Null);
            // The start, the toggle and the seven gestures of the left hand.
            Assert.That((result.Inputs, result.Compared, result.Targets, result.Unbound), Is.EqualTo((9, 9, 2, 0)));
            Assert.That(result.Mismatches.Select(m => m.Input), Is.Empty);
            Assert.That(AvatarAnimatorCrossCheck.ResultLine(result), Is.Null);
        }

        [Test]
        public void AnInputThatDiffersIsListedWithItsFirstFrame()
        {
            var (root, controller, data, bindings) = Build();
            // The app would show the hat off and only half the smile.
            data.Clips[1].Tracks[0].Curves[0].Keys.ForEach(key => key.Value = 0);
            data.Clips[3].Tracks[0].Curves[0].Keys.ForEach(key => key.Value = 50);
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            var toggle = ExporterLocalization.Format(AvatarAnimatorCrossCheck.MenuInput, "Hat");
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            Assert.That(result.Mismatches.Select(m => m.Input), Is.EqualTo(new[] { toggle, fist }));
            var hat = result.Mismatches[0];
            // The toggle is set after the settling frames and shows in that frame.
            Assert.That((hat.Frame, hat.Target, hat.Unity, hat.Evaluator), Is.EqualTo((AvatarAnimatorCrossCheck.SettleFrames, 0, 1d, 0d)));
            Assert.That(result.Mismatches[1].Target, Is.EqualTo(1));
            Assert.That(AvatarAnimatorCrossCheck.ResultLine(result),
                Is.EqualTo(ExporterLocalization.Format(AvatarAnimatorCrossCheck.MismatchNote, toggle + ExporterLocalization.T("、") + fist, 2, 9)));
            var lines = AvatarAnimatorCrossCheck.LogLines(result, t => "T" + t).ToList();
            Assert.That(lines[0], Does.StartWith(AvatarAnimatorCrossCheck.LogPrefix + "inputs 9 compared 9 mismatched 2 skipped 0 outOfTime 0 targets 2 unbound 0 "));
            Assert.That(lines[1], Is.EqualTo(AvatarAnimatorCrossCheck.LogPrefix + $"mismatch \"{toggle}\" frame 30 T0 unity 1 evaluator 0"));
        }

        [Test]
        public void TheResultLineListsEightInputsAndCountsTheRest()
        {
            var result = new AvatarAnimatorCrossCheck.Result { Compared = 12 };
            for (var i = 0; i < 10; i++) result.Mismatches.Add(new AvatarAnimatorCrossCheck.Mismatch { Input = "I" + i });
            var listed = string.Join(ExporterLocalization.T("、"), Enumerable.Range(0, 8).Select(i => "I" + i)
                .Concat(new[] { ExporterLocalization.Format(AvatarAnimatorCrossCheck.MoreInputs, 2) }));
            Assert.That(AvatarAnimatorCrossCheck.ResultLine(result), Is.EqualTo(ExporterLocalization.Format(AvatarAnimatorCrossCheck.MismatchNote, listed, 10, 12)));
        }

        [Test]
        public void ATargetTheCopyNoLongerShowsIsCountedAndNotCompared()
        {
            var (root, controller, data, bindings) = Build();
            var lost = bindings.ToList();
            lost[1] = new AvatarAnimatorCrossCheck.Binding { Renderer = bindings[1].Renderer, Shape = "Gone" };
            data.Clips[3].Tracks[0].Curves[0].Keys.ForEach(key => key.Value = 50);
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, lost, null);

            Assert.That((result.Targets, result.Unbound), Is.EqualTo((2, 1)));
            Assert.That(result.Mismatches, Is.Empty, "The smile is not compared.");
        }

        [Test]
        public void DataTheEvaluatorCannotPlayIsNotCompared()
        {
            var (root, controller, data, bindings) = Build();
            data.Controllers[0].Layers[1].Blending = "additive";
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            Assert.That(result.Unsupported, Is.EqualTo("Additive layers are not supported."));
            Assert.That(result.Compared, Is.Zero);
            Assert.That(AvatarAnimatorCrossCheck.ResultLine(result), Is.Null);
            Assert.That(AvatarAnimatorCrossCheck.LogLines(result, t => "").Single(), Is.EqualTo(AvatarAnimatorCrossCheck.LogPrefix + "unsupported Additive layers are not supported."));
        }

        [Test]
        public void InputsPastTheTimeBudgetAreCounted()
        {
            var (root, controller, data, bindings) = Build();
            AvatarAnimatorCrossCheck.BudgetSeconds = 0;
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            Assert.That((result.Compared, result.OutOfTime), Is.EqualTo((1, 8)), "The first input always runs.");
        }

        [Test]
        public void InputsFollowTheMenuAndBothHands()
        {
            var data = new AvatarAnimatorData();
            int Parameter(string name, string type, double value = 0)
            {
                data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = name, Type = type, Origin = "expression", Default = value });
                return data.Parameters.Count - 1;
            }
            var on = Parameter("On", "bool", 1);
            var outfit = Parameter("Outfit", "int");
            var size = Parameter("Size", "float", .8);
            var x = Parameter("X", "float");
            var y = Parameter("Y", "float");
            var open = Parameter("Open", "bool");
            var right = Parameter("GestureRight", "int");
            var rightWeight = Parameter("GestureRightWeight", "float");
            Parameter("GestureLeft", "float");
            data.Menus.Add(new AvatarAnimatorData.Menu());
            data.Menus.Add(new AvatarAnimatorData.Menu());
            data.Menus[0].Controls.Add(new AvatarAnimatorData.Control { Name = "On", Type = "toggle", Parameter = on, Value = 1 });
            data.Menus[0].Controls.Add(new AvatarAnimatorData.Control { Name = "More", Type = "subMenu", Parameter = open, Value = 1, Menu = 1 });
            data.Menus[0].Controls.Add(new AvatarAnimatorData.Control { Name = "Again", Type = "subMenu", Menu = 1 });
            data.Menus[1].Controls.Add(new AvatarAnimatorData.Control { Name = "Outfit 2", Type = "button", Parameter = outfit, Value = 2 });
            data.Menus[1].Controls.Add(new AvatarAnimatorData.Control { Name = "Outfit 2 again", Type = "toggle", Parameter = outfit, Value = 2 });
            var radial = new AvatarAnimatorData.Control { Name = "Size", Type = "radial" };
            radial.SubParameters.Add(size);
            data.Menus[1].Controls.Add(radial);
            var axis = new AvatarAnimatorData.Control { Name = "Move", Type = "twoAxis" };
            axis.SubParameters.AddRange(new[] { x, -1, y });
            data.Menus[1].Controls.Add(axis);

            var inputs = AvatarAnimatorCrossCheck.Inputs(data);
            string Menu(string name) => ExporterLocalization.Format(AvatarAnimatorCrossCheck.MenuInput, name);
            string Right(string name) => ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.RightHand), name);
            Assert.That(inputs.Select(input => input.Name), Is.EqualTo(new[]
            {
                ExporterLocalization.T(AvatarAnimatorCrossCheck.StartInput), Menu("On"), Menu("More"), Menu("More/Outfit 2"), Menu("More/Size"), Menu("More/Move"),
                Right("Fist"), Right("HandOpen"), Right("FingerPoint"), Right("Victory"), Right("RockNRoll"), Right("HandGun"), Right("ThumbsUp"),
            }), "The second way into the submenu and the same values again are listed once; a GestureLeft that is not an int is no gesture.");
            Assert.That(inputs[0].Values, Is.Empty);
            Assert.That(inputs[1].Values, Is.EqualTo(new[] { (on, 0d) }), "A toggle on at the start turns off.");
            Assert.That(inputs[2].Values, Is.EqualTo(new[] { (open, 1d) }));
            Assert.That(inputs[3].Values, Is.EqualTo(new[] { (outfit, 2d) }));
            Assert.That(inputs[4].Values, Is.EqualTo(new[] { (size, 0d) }), "A radial from 0.5 up goes to 0.");
            Assert.That(inputs[5].Values, Is.EqualTo(new[] { (x, 1d), (y, 1d) }));
            Assert.That(inputs[6].Values, Is.EqualTo(new[] { (right, 1d), (rightWeight, 1d) }));
        }

        // The objects, the Unity controller and the data written for them.
        (GameObject Root, AnimatorController Controller, AvatarAnimatorData Data, IReadOnlyList<AvatarAnimatorCrossCheck.Binding> Bindings) Build()
        {
            var root = new GameObject("Cross check");
            owned.Add(root);
            var hat = new GameObject("Hat");
            hat.transform.SetParent(root.transform, false);
            hat.SetActive(false);
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var mesh = new Mesh { name = "Body", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("Smile", 100, new[] { Vector3.up, Vector3.up, Vector3.up }, null, null);
            owned.Add(mesh);
            var renderer = body.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;

            var data = new AvatarAnimatorData();
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "Hat", Type = "bool", Origin = "expression", ExpressionType = "bool" });
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "GestureLeft", Type = "int", Origin = "builtin" });
            data.Menus.Add(new AvatarAnimatorData.Menu());
            data.Menus[0].Controls.Add(new AvatarAnimatorData.Control { Name = "Hat", Type = "toggle", Parameter = Hat, Value = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 1, Property = "active", Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "morph", Morph = 0, Rest = 0, Baked = 0, End = 100 });
            foreach (var (target, value) in new[] { (0, 0d), (0, 1d), (1, 0d), (1, 100d) })
            {
                var clip = new AvatarAnimatorData.Clip { Name = "Clip" + data.Clips.Count, Duration = 1.0 / 60 };
                var track = new AvatarAnimatorData.Track { Target = target };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = value, InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                clip.Tracks.Add(track);
                data.Clips.Add(clip);
                data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = data.Clips.Count - 1 });
            }
            var fx = new AvatarAnimatorData.Controller();
            data.Controllers.Add(fx);
            fx.Layers.Add(Layer("Hat", 0, 1, Hat, "if", "ifNot", 0, 0));
            fx.Layers.Add(Layer("Smile", 2, 3, Gesture, "equals", "notEqual", 1, .25));

            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder.Path + "/FX.controller");
            controller.RemoveLayer(0);
            controller.AddParameter("Hat", AnimatorControllerParameterType.Bool);
            controller.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
            var clips = data.Clips.Select((clip, i) =>
            {
                var unity = new AnimationClip { name = clip.Name };
                var binding = clip.Tracks[0].Target == 0
                    ? EditorCurveBinding.FloatCurve("Hat", typeof(GameObject), "m_IsActive")
                    : EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile");
                var value = (float)clip.Tracks[0].Curves[0].Keys[0].Value;
                AnimationUtility.SetEditorCurve(unity, binding, AnimationCurve.Constant(0, 1f / 60, value));
                AssetDatabase.AddObjectToAsset(unity, controller);
                return unity;
            }).ToArray();
            foreach (var layer in fx.Layers)
            {
                controller.AddLayer(layer.Name);
                var layers = controller.layers;
                layers[layers.Length - 1].defaultWeight = 1;
                controller.layers = layers;
                var machine = layers[layers.Length - 1].stateMachine;
                var states = layer.States.Select(state =>
                {
                    var unity = machine.AddState(state.Name);
                    unity.motion = clips[data.Motions[state.Motion].Clip];
                    unity.writeDefaultValues = state.WriteDefaults;
                    return unity;
                }).ToArray();
                machine.defaultState = states[0];
                for (var s = 0; s < states.Length; s++)
                    foreach (var transition in layer.States[s].Transitions)
                    {
                        var unity = states[s].AddTransition(states[transition.ToState.Value]);
                        unity.hasExitTime = false;
                        unity.hasFixedDuration = true;
                        unity.duration = (float)transition.Duration;
                        foreach (var condition in transition.Conditions)
                            unity.AddCondition(condition.Mode == "if" ? AnimatorConditionMode.If : condition.Mode == "ifNot" ? AnimatorConditionMode.IfNot
                                : condition.Mode == "equals" ? AnimatorConditionMode.Equals : AnimatorConditionMode.NotEqual,
                                (float)condition.Threshold, data.Parameters[condition.Parameter].Name);
                    }
            }
            AssetDatabase.SaveAssets();

            var bindings = new[]
            {
                new AvatarAnimatorCrossCheck.Binding { Object = hat },
                new AvatarAnimatorCrossCheck.Binding { Renderer = renderer, Shape = "Smile" },
            };
            return (root, controller, data, bindings);
        }

        // Two states playing the motions off and on, each going to the other under its condition.
        static AvatarAnimatorData.Layer Layer(string name, int off, int on, int parameter, string enter, string leave, double threshold, double duration)
        {
            var layer = new AvatarAnimatorData.Layer { Name = name, Weight = 1 };
            layer.Machines.Add(new AvatarAnimatorData.Machine { Name = name, DefaultState = 0 });
            foreach (var (state, motion) in new[] { ("Off", off), ("On", on) })
                layer.States.Add(new AvatarAnimatorData.State { Name = state, Machine = 0, Motion = motion, WriteDefaults = true });
            foreach (var (from, to, mode) in new[] { (0, 1, enter), (1, 0, leave) })
            {
                var transition = new AvatarAnimatorData.Transition
                {
                    ToState = to, Timed = true, HasFixedDuration = true, Duration = duration, OrderedInterruption = true,
                };
                transition.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = parameter, Mode = mode, Threshold = threshold });
                layer.States[from].Transitions.Add(transition);
            }
            return layer;
        }
    }
}
