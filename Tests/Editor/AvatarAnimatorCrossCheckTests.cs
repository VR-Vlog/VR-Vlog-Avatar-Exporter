using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;
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
        public void ATargetTheExportRenamedIsCountedAndNotCompared()
        {
            // Unity binds the Hat curve by path: renamed, the hat no longer
            // moves in Unity while the evaluator still shows it.
            var (root, controller, data, bindings) = Build();
            bindings[0].Object.name = "Hat (renamed)";
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            Assert.That((result.Targets, result.Unbound, result.Moved), Is.EqualTo((2, 0, 1)));
            Assert.That(result.Mismatches, Is.Empty, "The hat is not compared.");
        }

        [Test]
        public void UnityWritesTheRestWhereAStateDoesNotAnimate()
        {
            // Smile's Off state leaves Smile to Write Defaults, which brings back
            // the rest. The export left the copy's weight elsewhere (it bakes
            // the scene weight); Unity takes its defaults when the controller
            // binds, so the check must put the rest in before that.
            var (root, controller, data, bindings) = Build();
            var off = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Single(clip => clip.name == "Clip2");
            AnimationUtility.SetEditorCurve(off, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile"), null);
            data.Clips[2].Tracks.Clear();
            data.Targets[1].Rest = data.Targets[1].Baked = 40;
            bindings[1].Renderer.SetBlendShapeWeight(0, 0);
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, bindings, null);

            Assert.That(result.Compared, Is.EqualTo(9));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} {m.Unity} {m.Evaluator}"), Is.Empty);
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

        // Smile on also turns the Body renderer off and fades the float
        // _Cutoff of its material from 0.5 to 0.25 over the transition; Unity
        // keeps the animated float in the renderer's property block.
        [Test]
        public void RendererEnabledAndMaterialFloatMatchUnity()
        {
            var (root, controller, data, bindings) = Build();
            var renderer = bindings[1].Renderer;
            var material = new Material(Shader.Find("Standard")) { name = "Body" };
            owned.Add(material);
            material.SetFloat("_Cutoff", .5f);
            renderer.sharedMaterial = material;
            const int Enabled = 2, Cutoff = 3;
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "enabled", Component = "renderer", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialFloat", Material = 0, Name = "_Cutoff", Rest = .5 });
            var on = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Single(clip => clip.name == "Clip3");
            foreach (var (target, property, value) in new[] { (Enabled, "m_Enabled", 0f), (Cutoff, "material._Cutoff", .25f) })
            {
                AnimationUtility.SetEditorCurve(on, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), property), AnimationCurve.Constant(0, 1f / 60, value));
                var track = new AvatarAnimatorData.Track { Target = target };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = value, InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                data.Clips[3].Tracks.Add(track);
            }
            var all = bindings.Concat(new[]
            {
                new AvatarAnimatorCrossCheck.Binding { Component = renderer, Path = "Body" },
                new AvatarAnimatorCrossCheck.Binding { Component = renderer, Material = material, Name = "_Cutoff", Path = "Body" },
            }).ToList();
            data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, all, null);

            Assert.That(result.Unsupported, Is.Null);
            Assert.That((result.Inputs, result.Compared, result.Targets, result.Unbound), Is.EqualTo((9, 9, 4, 0)));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} T{m.Target} {m.Unity} {m.Evaluator}"), Is.Empty);

            // Data that keeps the renderer on, or holds another float, differs on the fist.
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            foreach (var (target, value) in new[] { (Enabled, 1d), (Cutoff, .9) })
            {
                var changed = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
                changed.Clips[3].Tracks.Single(t => t.Target == target).Curves[0].Keys.ForEach(key => key.Value = value);
                var differs = AvatarAnimatorCrossCheck.Run(root, controller, changed, all, null);
                Assert.That(differs.Mismatches.Select(m => (m.Input, m.Target)), Is.EqualTo(new[] { (fist, target) }));
            }
        }

        // Smile on also sets the green of the color _Color of the Body
        // renderer's material to 0.25 and the x offset of the texture _MainTex
        // to 0.5; Unity keeps the animated color and the texture's scale and
        // offset (_MainTex_ST) whole in the renderer's property block.
        [Test]
        public void MaterialColorAndTextureSTMatchUnity()
        {
            var (root, controller, data, bindings) = Build();
            var renderer = bindings[1].Renderer;
            var material = new Material(Shader.Find("Standard")) { name = "Body" };
            owned.Add(material);
            material.SetColor("_Color", new Color(1, .75f, .5f, 1));
            material.SetTextureScale("_MainTex", new Vector2(2, 1));
            renderer.sharedMaterial = material;
            const int Green = 2, Offset = 3;
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialColor", Material = 0, Name = "_Color", Channel = "g", Rest = .75 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialTextureST", Material = 0, Name = "_MainTex", Channel = "z", Rest = 0 });
            var on = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Single(clip => clip.name == "Clip3");
            foreach (var (target, property, value) in new[] { (Green, "material._Color.g", .25f), (Offset, "material._MainTex_ST.z", .5f) })
            {
                AnimationUtility.SetEditorCurve(on, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), property), AnimationCurve.Constant(0, 1f / 60, value));
                var track = new AvatarAnimatorData.Track { Target = target };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = value, InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                data.Clips[3].Tracks.Add(track);
            }
            var all = bindings.Concat(new[]
            {
                new AvatarAnimatorCrossCheck.Binding { Component = renderer, Material = material, Name = "_Color", Path = "Body" },
                new AvatarAnimatorCrossCheck.Binding { Component = renderer, Material = material, Name = "_MainTex", Path = "Body" },
            }).ToList();
            data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, all, null);

            Assert.That(result.Unsupported, Is.Null);
            Assert.That((result.Inputs, result.Compared, result.Targets, result.Unbound), Is.EqualTo((9, 9, 4, 0)));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} T{m.Target} {m.Unity} {m.Evaluator}"), Is.Empty);

            // Data that holds another value differs on the fist, and names the channel.
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            foreach (var (target, value, shown) in new[] { (Green, .5, "Body:_Color.g"), (Offset, .25, "Body:_MainTex_ST.z") })
            {
                var changed = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
                changed.Clips[3].Tracks.Single(t => t.Target == target).Curves[0].Keys.ForEach(key => key.Value = value);
                var differs = AvatarAnimatorCrossCheck.Run(root, controller, changed, all, null);
                Assert.That(differs.Mismatches.Select(m => (m.Input, m.Target)), Is.EqualTo(new[] { (fist, target) }));
                Assert.That(AvatarAnimatorCrossCheck.Describe(root, changed, all, target), Does.Contain(shown));
            }
        }

        // Smile on also moves Body 0.5 m along x, turns it 30 degrees about y
        // (an Euler curve), doubles its y scale and puts the material Swapped
        // in its first slot; the write defaults put them back when it goes off.
        [Test]
        public void TransformAndMaterialSlotMatchUnity()
        {
            var (root, controller, data, bindings) = Build();
            var renderer = bindings[1].Renderer;
            var held = new Material(Shader.Find("Standard")) { name = "Held" };
            var swapped = new Material(Shader.Find("Standard")) { name = "Swapped" };
            owned.Add(held);
            owned.Add(swapped);
            renderer.sharedMaterial = held;
            const int X = 2, Turn = 3, Tall = 4, Slot = 5;
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "translation", Channel = "x", Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "rotation", Channel = "y", Euler = true, Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "scale", Channel = "y", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "materialSlot", Slot = 0, Rest = 0 });
            var on = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Single(clip => clip.name == "Clip3");
            foreach (var (target, property, value) in new[] { (X, "m_LocalPosition.x", .5f), (Turn, "localEulerAnglesRaw.y", 30f), (Tall, "m_LocalScale.y", 2f), (Slot, "", 1f) })
            {
                if (target == Slot)
                    AnimationUtility.SetObjectReferenceCurve(on, EditorCurveBinding.PPtrCurve("Body", typeof(SkinnedMeshRenderer), "m_Materials.Array.data[0]"),
                        new[] { new ObjectReferenceKeyframe { time = 0, value = swapped }, new ObjectReferenceKeyframe { time = 1f / 60, value = swapped } });
                else AnimationUtility.SetEditorCurve(on, EditorCurveBinding.FloatCurve("Body", typeof(Transform), property), AnimationCurve.Constant(0, 1f / 60, value));
                var track = new AvatarAnimatorData.Track { Target = target };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = value, InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                data.Clips[3].Tracks.Add(track);
            }
            var body = renderer.gameObject;
            var all = bindings.Concat(new[]
            {
                new AvatarAnimatorCrossCheck.Binding { Object = body, Path = "Body" },
                new AvatarAnimatorCrossCheck.Binding { Object = body, Path = "Body" },
                new AvatarAnimatorCrossCheck.Binding { Object = body, Path = "Body" },
                new AvatarAnimatorCrossCheck.Binding { Component = renderer, Slot = 0, Slots = new Dictionary<Material, int> { [held] = 0, [swapped] = 1 }, Path = "Body" },
            }).ToList();
            data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, all, null);

            Assert.That(result.Unsupported, Is.Null);
            Assert.That((result.Inputs, result.Compared, result.Targets, result.Unbound), Is.EqualTo((9, 9, 6, 0)));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} T{m.Target} {m.Unity} {m.Evaluator}"), Is.Empty);

            // Data that holds another value differs on the fist, and names the channel or slot.
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            foreach (var (target, value, shown) in new[] { (X, .25, "Body:translation.x"), (Turn, 10, "Body:rotationEuler.y"), (Tall, 3, "Body:scale.y"), (Slot, 0, "Body:slot0") })
            {
                var changed = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
                changed.Clips[3].Tracks.Single(t => t.Target == target).Curves[0].Keys.ForEach(key => key.Value = value);
                var differs = AvatarAnimatorCrossCheck.Run(root, controller, changed, all, null);
                Assert.That(differs.Mismatches.Select(m => (m.Input, m.Target)), Is.EqualTo(new[] { (fist, target) }), shown);
                Assert.That(AvatarAnimatorCrossCheck.Describe(root, changed, all, target), Does.Contain(shown));
            }
        }

        // A Gesture whose Fist moves Body to x 0.5 and an Action that moves it
        // to 2 play under the FX on the graph: the Gesture shows, the Action
        // stays at weight 0 until the Hat's On blends it in, and data with
        // another Fist value differs.
        [Test]
        public void GestureAndActionPlayUnderTheFx()
        {
            var (root, fx, data, bindings) = Build();
            const int X = 2;
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "translation", Channel = "x", Rest = 0 });
            var clips = new List<AnimationClip>();
            foreach (var value in new[] { 0d, .5, 2 })
            {
                var clip = new AvatarAnimatorData.Clip { Name = "Clip" + data.Clips.Count, Duration = 1.0 / 60 };
                var track = new AvatarAnimatorData.Track { Target = X };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value, InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = value, InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                clip.Tracks.Add(track);
                data.Clips.Add(clip);
                data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = data.Clips.Count - 1 });
                var unity = new AnimationClip { name = clip.Name };
                AnimationUtility.SetEditorCurve(unity, EditorCurveBinding.FloatCurve("Body", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0, 1f / 60, (float)value));
                clips.Add(unity);
            }
            var gesture = new AvatarAnimatorData.Controller { Playable = "gesture" };
            gesture.Layers.Add(Layer("Pose", 4, 5, Gesture, "equals", "notEqual", 1, .2));
            var action = new AvatarAnimatorData.Controller { Playable = "action" };
            action.Layers.Add(Layer("Wave", 6, 6, Hat, "if", "ifNot", 0, 0));
            // The Hat's On blends the Action in, from the data: the playables'
            // copies of the SDK's Layer Controls are out of the hooks' reach.
            action.Layers[0].States[1].Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "action", GoalWeight = 1, BlendDuration = .1 });
            data.Controllers.Add(gesture);
            data.Controllers.Add(action);
            var controllers = new List<RuntimeAnimatorController> { fx };
            foreach (var (c, name) in new[] { (gesture, "Gesture"), (action, "Action") })
            {
                var unity = AnimatorController.CreateAnimatorControllerAtPath(folder.Path + "/" + name + ".controller");
                unity.RemoveLayer(0);
                unity.AddParameter("Hat", AnimatorControllerParameterType.Bool);
                unity.AddParameter("GestureLeft", AnimatorControllerParameterType.Int);
                var all = new AnimationClip[data.Clips.Count];
                for (var i = 0; i < clips.Count; i++)
                {
                    all[4 + i] = Object.Instantiate(clips[i]);
                    all[4 + i].name = clips[i].name;
                    AssetDatabase.AddObjectToAsset(all[4 + i], unity);
                }
                AddLayers(unity, data, c, all);
                controllers.Add(unity);
            }
            clips.ForEach(Object.DestroyImmediate);
            AssetDatabase.SaveAssets();
            var body = bindings[1].Renderer.gameObject;
            var withX = bindings.Concat(new[] { new AvatarAnimatorCrossCheck.Binding { Object = body, Path = "Body" } }).ToList();
            data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));

            var result = AvatarAnimatorCrossCheck.Run(root, controllers, data, withX, null);
            Assert.That(result.Unsupported, Is.Null);
            Assert.That((result.Inputs, result.Compared, result.Targets, result.Unbound, result.Muscles), Is.EqualTo((9, 9, 3, 0, 0)));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} T{m.Target} {m.Unity} {m.Evaluator} {m.Layers}"), Is.Empty);

            var changed = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            changed.Clips[5].Tracks[0].Curves[0].Keys.ForEach(key => key.Value = .25);
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            Assert.That(AvatarAnimatorCrossCheck.Run(root, controllers, changed, withX, null).Mismatches.Select(m => (m.Input, m.Target)), Is.EqualTo(new[] { (fist, X) }));
        }

        // Smile on also turns the object Head 90 degrees about y with the
        // four quaternion curves; over the 0.25 s transition the evaluator's
        // blend, normalized, follows Unity's.
        [Test]
        public void AQuaternionRotationMatchesUnityThroughTheTransition()
        {
            var (root, controller, data, bindings) = Build();
            var head = new GameObject("Head");
            head.transform.SetParent(root.transform, false);
            var on = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller)).OfType<AnimationClip>().Single(clip => clip.name == "Clip3");
            var turned = Quaternion.Euler(0, 90, 0);
            var all = bindings.ToList();
            for (var c = 0; c < 4; c++)
            {
                var channel = AvatarAnimatorData.QuaternionChannels[c];
                data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "rotation", Channel = channel, Rest = c == 3 ? 1 : 0 });
                AnimationUtility.SetEditorCurve(on, EditorCurveBinding.FloatCurve("Head", typeof(Transform), "m_LocalRotation." + channel), AnimationCurve.Constant(0, 1f / 60, turned[c]));
                var track = new AvatarAnimatorData.Track { Target = data.Targets.Count - 1 };
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = turned[c], InTangent = 0, OutTangent = 0 });
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1.0 / 60, Value = turned[c], InTangent = 0, OutTangent = 0 });
                track.Curves.Add(curve);
                data.Clips[3].Tracks.Add(track);
                all.Add(new AvatarAnimatorCrossCheck.Binding { Object = head, Path = "Head" });
            }
            data = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            var result = AvatarAnimatorCrossCheck.Run(root, controller, data, all, null);

            Assert.That(result.Unsupported, Is.Null);
            Assert.That((result.Compared, result.Targets, result.Unbound), Is.EqualTo((9, 6, 0)));
            Assert.That(result.Mismatches.Select(m => $"{m.Input} {m.Frame} T{m.Target} {m.Unity} {m.Evaluator}"), Is.Empty);

            // The turn the other way about y differs on the fist.
            var changed = AvatarAnimatorData.Read(AvatarAnimatorData.Write(data));
            changed.Clips[3].Tracks.Single(t => t.Target == 3).Curves[0].Keys.ForEach(key => key.Value = -key.Value);
            var fist = ExporterLocalization.Format(AvatarAnimatorCrossCheck.GestureInput, ExporterLocalization.T(AvatarAnimatorCrossCheck.LeftHand), "Fist");
            Assert.That(AvatarAnimatorCrossCheck.Run(root, controller, changed, all, null).Mismatches.Select(m => (m.Input, m.Target)), Is.EqualTo(new[] { (fist, 3) }));
        }

        // An Action-like input writing every muscle of the body at a partial
        // weight over a Gesture-like one moving a finger, on a rig with Unity's
        // default twist distribution. Unity mixes the muscles linearly but reads
        // them back off by up to about 1.5e-3, by as much as the linear pose put
        // on the bones with SetHumanPose: the cross check puts the evaluator's
        // muscles through the same read back (MuscleReader) before comparing.
        [Test]
        public void MusclesReadBackAsAPosePutOnTheBones()
        {
            var root = new GameObject("Rig");
            owned.Add(root);
            var avatar = HumanoidRig(root);
            owned.Add(avatar);
            var animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            const int body = 55;
            var data = new AvatarAnimatorData();
            for (var m = 0; m < body; m++) data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "muscle", Muscle = m, Rest = 0 });
            var muscles = Enumerable.Range(0, body).ToList();
            var pose = Enumerable.Range(0, body).Select(m => .3f * Mathf.Sin(m * 1.7f + .3f)).ToArray();
            var action = MuscleClip("Action", Enumerable.Range(0, body).Select(m => (HumanTrait.MuscleName[m], pose[m])));
            var gesture = MuscleClip("Gesture", new[] { ("LeftHand.Index.1 Stretched", .4f) });
            var graph = PlayableGraph.Create("Read back");
            var hands = new AvatarMask();
            for (var part = 0; part < (int)AvatarMaskBodyPart.LastBodyPart; part++)
                hands.SetHumanoidBodyPartActive((AvatarMaskBodyPart)part, part == (int)AvatarMaskBodyPart.LeftFingers || part == (int)AvatarMaskBodyPart.RightFingers);
            owned.Add(hands);
            using (var reader = new AvatarAnimatorCrossCheck.MuscleReader(animator, root.transform))
            {
                try
                {
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
                    graph.Connect(AnimatorControllerPlayable.Create(graph, gesture), 0, mixer, 0);
                    graph.Connect(AnimatorControllerPlayable.Create(graph, action), 0, mixer, 1);
                    mixer.SetLayerMaskFromAvatarMask(0, hands);
                    mixer.SetInputWeight(0, 1);
                    AnimationPlayableOutput.Create(graph, "Animator", animator).SetSourcePlayable(mixer);
                    var unity = new HumanPose();
                    var linear = new double[body];
                    double direct = 0, through = 0;
                    foreach (var weight in new[] { 1f / 30, 2f / 30, 4f / 30, .5f, 1 })
                    {
                        mixer.SetInputWeight(1, weight);
                        graph.Evaluate(1f / 60);
                        reader.Read(ref unity);
                        for (var m = 0; m < body; m++) linear[m] = weight * pose[m];
                        var bones = root.GetComponentsInChildren<Transform>();
                        var before = bones.Select(b => (b.localPosition, b.localRotation)).ToArray();
                        var read = reader.ReadBack(unity, data, muscles, linear);
                        // Setting a rotation back may round its last bit.
                        for (var b = 0; b < bones.Length; b++)
                        {
                            Assert.That(Vector3.Distance(bones[b].localPosition, before[b].localPosition), Is.LessThan(1e-6f), $"{bones[b].name} is put back where Unity left it");
                            Assert.That(Quaternion.Angle(bones[b].localRotation, before[b].localRotation), Is.LessThan(1e-3f), $"{bones[b].name} is turned back as Unity left it");
                        }
                        for (var m = 0; m < body; m++)
                        {
                            direct = Math.Max(direct, Math.Abs(unity.muscles[m] - linear[m]));
                            through = Math.Max(through, Math.Abs(unity.muscles[m] - read[m]));
                        }
                    }
                    Assert.That(direct, Is.GreaterThan(AvatarAnimatorCrossCheck.MuscleTolerance), "read back as is, the linear muscles would differ");
                    Assert.That(through, Is.LessThan(1e-4));
                }
                finally
                {
                    graph.Destroy();
                }
            }
        }

        RuntimeAnimatorController MuscleClip(string name, IEnumerable<(string Curve, float Value)> curves)
        {
            var clip = new AnimationClip { name = name };
            foreach (var (curve, value) in curves)
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), curve), AnimationCurve.Constant(0, 1, value));
            AssetDatabase.CreateAsset(clip, folder.Path + "/" + name + ".anim");
            return AnimatorController.CreateAnimatorControllerAtPathWithClip(folder.Path + "/" + name + ".controller", clip);
        }

        // A humanoid rig in a T-pose with every bone of HumanTrait and Unity's
        // default HumanDescription (twist distribution 0.5), and its Avatar.
        static Avatar HumanoidRig(GameObject root)
        {
            var bones = new Transform[HumanTrait.BoneCount];
            Transform Bone(int i)
            {
                if (bones[i] != null) return bones[i];
                var parent = HumanTrait.GetParentBone(i);
                var bone = new GameObject("Bone " + i).transform;
                bone.SetParent(parent < 0 ? root.transform : Bone(parent), false);
                bone.localPosition = Offset(HumanTrait.BoneName[i]);
                return bones[i] = bone;
            }
            var human = new List<HumanBone>();
            for (var i = 0; i < bones.Length; i++)
                human.Add(new HumanBone { humanName = HumanTrait.BoneName[i], boneName = Bone(i).name, limit = new HumanLimit { useDefaultValues = true } });
            var skeleton = new List<SkeletonBone> { new SkeletonBone { name = root.name, rotation = Quaternion.identity, scale = Vector3.one } };
            skeleton.AddRange(bones.Select(bone => new SkeletonBone { name = bone.name, position = bone.localPosition, rotation = bone.localRotation, scale = bone.localScale }));
            var description = new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton.ToArray(),
                upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f,
                armStretch = .05f, legStretch = .05f, feetSpacing = 0, hasTranslationDoF = false,
            };
            var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            Assert.That(avatar.isValid && avatar.isHuman, "the rig's Avatar");
            return avatar;
        }

        // A bone's place from its parent, facing +z with the left side at -x.
        static Vector3 Offset(string bone)
        {
            var side = bone.StartsWith("Left", StringComparison.Ordinal) ? -1f : bone.StartsWith("Right", StringComparison.Ordinal) ? 1f : 0f;
            switch (bone.Replace("Left", "").Replace("Right", "").Replace(" ", ""))
            {
                case "Hips": return new Vector3(0, 1, 0);
                case "Spine": case "Chest": case "UpperChest": return new Vector3(0, .12f, 0);
                case "Neck": return new Vector3(0, .1f, 0);
                case "Head": return new Vector3(0, .08f, 0);
                case "Eye": return new Vector3(side * .03f, .06f, .08f);
                case "Jaw": return new Vector3(0, -.01f, .03f);
                case "UpperLeg": return new Vector3(side * .1f, -.05f, 0);
                case "LowerLeg": case "Foot": return new Vector3(0, -.42f, 0);
                case "Toes": return new Vector3(0, -.06f, .12f);
                case "Shoulder": return new Vector3(side * .04f, .1f, 0);
                case "UpperArm": return new Vector3(side * .1f, 0, 0);
                case "LowerArm": case "Hand": return new Vector3(side * .26f, 0, 0);
                case "ThumbProximal": return new Vector3(side * .02f, -.01f, .03f);
                case "IndexProximal": return new Vector3(side * .08f, 0, .025f);
                case "MiddleProximal": return new Vector3(side * .085f, 0, .008f);
                case "RingProximal": return new Vector3(side * .08f, 0, -.01f);
                case "LittleProximal": return new Vector3(side * .07f, 0, -.028f);
                default: return new Vector3(side * .025f, 0, 0);
            }
        }

        // The face Smile is the fist's state: the evaluator shows its weight
        // of 100 with the values the shared solver picks, and no other weight.
        [Test]
        public void FaceMismatchTellsWhetherTheEvaluatorShowsTheFace()
        {
            var (_, _, data, _) = Build();
            AddFace(data, "Smile", 3);

            Assert.That(AvatarAnimatorCrossCheck.FaceMismatch(data, "Smile", new[] { (1, 100d) }), Is.Null);
            Assert.That(AvatarAnimatorCrossCheck.FaceMismatch(data, "Smile", new[] { (1, 99.6) }), Is.Null, "within half a weight");
            Assert.That(AvatarAnimatorCrossCheck.FaceMismatch(data, "Smile", new[] { (1, 50d) }), Does.StartWith("T1 face 50 evaluator 100"));
            Assert.That(AvatarAnimatorCrossCheck.FaceMismatch(data, "Frown", new[] { (1, 100d) }), Is.EqualTo("no way in"));
        }

        // A face's values come from its morph binds (Baked + weight x
        // (End - Baked)) and the tracks it holds still, from glTF's space to
        // Unity's; a material bind, a track that moves in time, a BlendShape
        // animation or a bind with no target leaves it out.
        [Test]
        public void FaceValuesFollowTheBindsAndTheHeldTracks()
        {
            var (_, _, data, _) = Build();
            data.Targets[1].Baked = 10;
            foreach (var channel in AvatarAnimatorData.VectorChannels)
                data.Targets.Add(new AvatarAnimatorData.Target { Node = 2, Property = "translation", Channel = channel, Rest = 0 });
            var document = GlbDocument.Read(Glb(new Dictionary<string, object>
            {
                ["Half"] = Face((2, 0, .5)),
                ["Moved"] = Face(),
                ["HatOn"] = Face(),
                ["Waving"] = Face(),
                ["Blinking"] = Face(),
                ["Colored"] = new Dictionary<string, object> { ["materialColorBinds"] = new List<object> { new Dictionary<string, object>() } },
                ["Elsewhere"] = Face((1, 0, 1)),
            }, Animations()));

            string Values(string name) => AvatarAnimatorCapture.FaceValues(document, data, name, out var reason) is { } values
                ? string.Join(" ", values.Select(v => $"T{v.Target}={v.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}")) : reason;
            Assert.That(Values("Half"), Is.EqualTo("T1=55"));
            Assert.That(Values("Moved"), Is.EqualTo("T2=-0.25 T3=0.5 T4=0"));
            Assert.That(Values("HatOn"), Is.EqualTo("T0=1"));
            Assert.That(Values("Waving"), Is.EqualTo("transform animation"));
            Assert.That(Values("Blinking"), Is.EqualTo("blendShape animation"));
            Assert.That(Values("Colored"), Is.EqualTo("materialColorBinds"));
            Assert.That(Values("Elsewhere"), Is.EqualTo("morph 1:0 is no target"));
            Assert.That(Values("Missing"), Is.EqualTo("no expression"));
        }

        // Inject writes the faces the evaluator shows and leaves out the one
        // whose bind it cannot show (a weight of 50 where the fist plays 100).
        [Test]
        public void InjectWritesOnlyTheFacesTheEvaluatorShows()
        {
            var (_, _, data, _) = Build();
            var bytes = Glb(new Dictionary<string, object> { ["Smile"] = Face((2, 0, 1)), ["Half smile"] = Face((2, 0, .5)), ["Hat"] = Face() }, null);
            var faces = new (string, IReadOnlyCollection<int>)[] { ("Smile", new[] { 3 }), ("Half smile", new[] { 3 }), ("Hat", new[] { 1 }) };

            LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("face \"Half smile\" left out: T1 face 50 evaluator 100"));
            var written = AvatarAnimatorData.Read(AvatarAnimatorPacking.Document(GlbDocument.Read(AvatarAnimatorCapture.Inject(bytes, data, faces))));

            Assert.That(written.Expressions.Select(e => e.Name), Is.EqualTo(new[] { "Smile", "Hat" }));
        }

        static void AddFace(AvatarAnimatorData data, string name, int clip)
        {
            var expression = new AvatarAnimatorData.Expression { Name = name };
            expression.Clips.Add(clip);
            expression.When.AddRange(AvatarAnimatorCapture.Entrances(data, new[] { clip }));
            data.Expressions.Add(expression);
        }

        static Dictionary<string, object> Face(params (int Node, int Index, double Weight)[] binds) => new Dictionary<string, object>
        {
            ["morphTargetBinds"] = binds.Select(b => (object)new Dictionary<string, object> { ["node"] = (long)b.Node, ["index"] = (long)b.Index, ["weight"] = b.Weight }).ToList(),
        };

        // Moved holds a glTF translation of (0.25, 0.5, 0); HatOn holds the
        // hat shown; Waving turns over a second; Blinking has a BlendShape animation.
        static Dictionary<string, object> Animations()
        {
            var document = new ExpressionAnimationDocument();
            foreach (var name in new[] { "Moved", "HatOn", "Waving", "Blinking" })
                document.Expressions.Add(new ExpressionSource { Expression = name, Source = ExpressionSource.Menu });
            ExpressionAnimationData.Curve Curve(params (double Time, double Value)[] keys)
            {
                var curve = new ExpressionAnimationData.Curve();
                foreach (var (time, value) in keys) curve.Keys.Add(new ExpressionAnimationData.Key { Time = time, Value = value, InTangent = 0, OutTangent = 0 });
                return curve;
            }
            var moved = new ExpressionTransformAnimation { Expression = "Moved" };
            var position = new ExpressionTransformAnimation.Track { Node = 2, Property = ExpressionTransformAnimation.Translation };
            position.Curves.AddRange(new[] { Curve((0, .25)), Curve((0, .5)), Curve((0, 0)) });
            moved.Tracks.Add(position);
            var hat = new ExpressionTransformAnimation { Expression = "HatOn" };
            var shown = new ExpressionTransformAnimation.Track { Node = 1, Property = ExpressionTransformAnimation.Active };
            shown.Curves.Add(Curve((0, 1)));
            hat.Tracks.Add(shown);
            var waving = new ExpressionTransformAnimation { Expression = "Waving", Duration = 1 };
            var turn = new ExpressionTransformAnimation.Track { Node = 2, Property = ExpressionTransformAnimation.Translation };
            turn.Curves.AddRange(new[] { Curve((0, 0), (1, .5)), Curve((0, 0)), Curve((0, 0)) });
            waving.Tracks.Add(turn);
            document.Transforms.AddRange(new[] { moved, hat, waving });
            var blinking = new ExpressionAnimationData { Expression = "Blinking", Duration = 1 };
            var channel = new ExpressionAnimationData.Channel { Curve = Curve((0, 0), (1, 1)) };
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 0 });
            channel.Points.Add(new ExpressionAnimationData.Point { Value = 1, Morph = new ExpressionAnimationData.MorphReference { Node = 2, Index = 0 } });
            blinking.Channels.Add(channel);
            document.Animations.Add(blinking);
            return ExpressionAnimationData.WriteDocument(document);
        }

        // A file with the custom expressions and, when given, the expression
        // animations; nodes 0 to 2, node 2 drawing a mesh with one morph target.
        static byte[] Glb(Dictionary<string, object> custom, Dictionary<string, object> animations)
        {
            var extensions = new Dictionary<string, object>
            {
                ["VRMC_vrm"] = new Dictionary<string, object> { ["expressions"] = new Dictionary<string, object> { ["custom"] = custom } },
            };
            if (animations != null) extensions[ExpressionAnimationData.Extension] = animations;
            return GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["nodes"] = new List<object> { new Dictionary<string, object>(), new Dictionary<string, object>(), new Dictionary<string, object> { ["mesh"] = 0L } },
                ["meshes"] = new List<object>
                {
                    new Dictionary<string, object> { ["primitives"] = new List<object> { new Dictionary<string, object> { ["targets"] = new List<object> { new Dictionary<string, object>() } } } },
                },
                // Inject packs the table into the one buffer of the GLB.
                ["buffers"] = new List<object> { new Dictionary<string, object> { ["byteLength"] = 0L } },
                ["extensions"] = extensions,
            }, System.Array.Empty<byte>()).Write();
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
            AddLayers(controller, data, fx, clips);
            AssetDatabase.SaveAssets();

            var bindings = new[]
            {
                new AvatarAnimatorCrossCheck.Binding { Object = hat, Path = "Hat" },
                new AvatarAnimatorCrossCheck.Binding { Renderer = renderer, Shape = "Smile", Path = "Body" },
            };
            return (root, controller, data, bindings);
        }

        // Unity's layers of a data controller, whose motions play clips[clip].
        static void AddLayers(AnimatorController controller, AvatarAnimatorData data, AvatarAnimatorData.Controller c, IReadOnlyList<AnimationClip> clips)
        {
            foreach (var layer in c.Layers)
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
