using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter.Tests
{
    // The VRVLOG_avatar_animator contract: what Write produces is what Read
    // accepts, and Read rejects anything outside the tables and limits.
    public sealed class AvatarAnimatorDataTests
    {
        const string Invalid = "Avatar animator data is invalid or exceeds the supported limits.";

        static void Rejects(Action<Dictionary<string, object>> change)
        {
            var json = Json(Sample());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        // Parsed back from text, as the app reads it.
        static Dictionary<string, object> Json(AvatarAnimatorData data) =>
            (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(AvatarAnimatorData.Write(data)));

        static Dictionary<string, object> At(Dictionary<string, object> json, params object[] path)
        {
            object value = json;
            foreach (var step in path) value = step is int index ? ((List<object>)value)[index] : ((Dictionary<string, object>)value)[(string)step];
            return (Dictionary<string, object>)value;
        }

        static AvatarAnimatorData.Transition Transition(int? toState = null, int? toMachine = null, bool exit = false, bool timed = true)
        {
            var transition = new AvatarAnimatorData.Transition
            {
                ToState = toState, ToMachine = toMachine, Exit = exit, Timed = timed,
                HasExitTime = timed, ExitTime = timed ? .75 : 0, Duration = timed ? .25 : 0, InterruptionSource = timed ? "sourceThenDestination" : "none"
            };
            transition.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = 0, Mode = "if", Threshold = 0 });
            return transition;
        }

        static AvatarAnimatorData Sample()
        {
            var data = new AvatarAnimatorData();
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "Hat", Type = "bool", Default = 1, Origin = "expression", ExpressionType = "bool", Saved = true, Synced = true });
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "Blend", Type = "float", Default = .5, Origin = "animator" });
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "GestureLeft", Type = "int", Default = 0, Origin = "builtin" });
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "Size", Type = "float", Default = 0, Origin = "expression", ExpressionType = "float", Saved = false, Synced = true });
            var root = new AvatarAnimatorData.Menu();
            root.Controls.Add(new AvatarAnimatorData.Control { Name = "Hat", Type = "toggle", Parameter = 0, Value = 1 });
            var more = new AvatarAnimatorData.Control { Name = "More", Type = "subMenu", Menu = 1 };
            root.Controls.Add(more);
            var sub = new AvatarAnimatorData.Menu();
            var radial = new AvatarAnimatorData.Control { Name = "", Type = "radial" };
            radial.SubParameters.Add(3); radial.SubParameters.Add(-1); radial.Labels.Add("Size");
            sub.Controls.Add(radial);
            sub.Controls.Add(new AvatarAnimatorData.Control { Name = "Back", Type = "subMenu", Menu = 0 });
            data.Menus.Add(root); data.Menus.Add(sub);

            var layer = new AvatarAnimatorData.Layer { Name = "Hat", Weight = 1, Blending = "override" };
            var top = new AvatarAnimatorData.Machine { Name = "Hat", DefaultState = 0 };
            top.EntryTransitions.Add(Transition(toMachine: 1, timed: false));
            top.AnyStateTransitions.Add(Transition(toState: 1));
            var driver = new AvatarAnimatorData.Behaviour { Type = "parameterDriver", LocalOnly = true };
            driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "copy", Parameter = 1, Source = 2, ConvertRange = true, SourceMax = 1, DestMax = 2 });
            driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = 0, Chance = .5 });
            driver.Operations.Add(new AvatarAnimatorData.Operation { Type = "random", Parameter = 2, ValueMin = 0, ValueMax = 3, PreventRepeats = true });
            top.Behaviours.Add(driver);
            var nested = new AvatarAnimatorData.Machine { Name = "Nested", Parent = 0, DefaultState = 2 };
            nested.ExitTransitions.Add(Transition(exit: true, timed: false));
            layer.Machines.Add(top); layer.Machines.Add(nested);
            var off = new AvatarAnimatorData.State { Name = "Off", Machine = 0, Motion = 0, WriteDefaults = false };
            off.Transitions.Add(Transition(toState: 1));
            var tracking = new AvatarAnimatorData.Behaviour { Type = "trackingControl" };
            tracking.Tracking.Add(new KeyValuePair<string, string>("head", "noChange"));
            tracking.Tracking.Add(new KeyValuePair<string, string>("leftHand", "animation"));
            off.Behaviours.Add(tracking);
            var on = new AvatarAnimatorData.State { Name = "On", Machine = 0, Motion = 1, TimeParameter = 1, Speed = 2 };
            on.Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "layerControl", Playable = "fx", Layer = 1, GoalWeight = 1, BlendDuration = .5 });
            on.Behaviours.Add(new AvatarAnimatorData.Behaviour { Type = "playableLayerControl", Playable = "sitting", GoalWeight = 0 });
            var inner = new AvatarAnimatorData.State { Name = "Inner", Machine = 1, Motion = -1 };
            inner.Transitions.Add(Transition(exit: true));
            layer.States.Add(off); layer.States.Add(on); layer.States.Add(inner);
            var controller = new AvatarAnimatorData.Controller();
            controller.Layers.Add(layer);
            // A synced layer holds a copy of its source's machines.
            var synced = new AvatarAnimatorData.Layer { Name = "Hat synced", Weight = .5, SyncedLayer = 0, SyncedLayerAffectsTiming = true };
            synced.Machines.Add(new AvatarAnimatorData.Machine { Name = "Hat", DefaultState = 0 });
            synced.Machines.Add(new AvatarAnimatorData.Machine { Name = "Nested", Parent = 0, DefaultState = 2 });
            foreach (var state in layer.States) synced.States.Add(new AvatarAnimatorData.State { Name = state.Name, Machine = state.Machine, Motion = -1 });
            controller.Layers.Add(synced);
            data.Controllers.Add(controller);

            data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = 0 });
            var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = "2dFreeformCartesian" };
            tree.Parameters.Add(1); tree.Parameters.Add(1);
            tree.Children.Add(new AvatarAnimatorData.Child { Motion = 0, X = -1, Y = .5 });
            tree.Children.Add(new AvatarAnimatorData.Child { Motion = 2, X = 1, Mirror = true });
            data.Motions.Add(tree);
            var direct = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = "direct", NormalizedBlendValues = true };
            direct.Children.Add(new AvatarAnimatorData.Child { Motion = 0, DirectParameter = 1 });
            data.Motions.Add(direct);

            var clip = new AvatarAnimatorData.Clip { Name = "Hat On", Duration = 1d / 60, Loop = false, CycleOffset = .25 };
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 1, InTangent = null, OutTangent = null });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1d / 60, Value = 0, InTangent = 0, OutTangent = 0 });
            var track = new AvatarAnimatorData.Track { Target = 0 };
            track.Curves.Add(curve);
            clip.Tracks.Add(track);
            // Float parameter Blend, driven by the clip.
            var blend = new AvatarAnimatorData.Track { Target = 1 };
            blend.Curves.Add(curve);
            clip.Tracks.Add(blend);
            data.Clips.Add(clip);
            data.Clips.Add(new AvatarAnimatorData.Clip { Name = "Empty", Duration = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Rest = 0 });
            // Parameter targets: float Blend with its controller default and a
            // track, int GestureLeft, which a clip holds at rest, with none.
            data.Targets.Add(new AvatarAnimatorData.Target { Property = "parameter", Node = -1, Parameter = 1, Rest = .25 });
            data.Targets.Add(new AvatarAnimatorData.Target { Property = "parameter", Node = -1, Parameter = 2, Rest = 2 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "blendShape", Count = 2 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "behaviour", Count = 1 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "operation", Count = 3 });
            return data;
        }

        [Test]
        public void WriteAndReadRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(Sample()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text), "Writing what was read must give the same JSON.");
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(Sample())), Is.EqualTo(text), "The same data must always give the same JSON.");

            Assert.That(read.Parameters[0].Saved && read.Parameters[0].Synced, Is.True);
            Assert.That(read.Parameters[1].ExpressionType, Is.Null);
            Assert.That(read.Menus[0].Controls[0].Parameter, Is.EqualTo(0));
            Assert.That(read.Menus[1].Controls[0].Parameter, Is.Null);
            Assert.That(read.Menus[1].Controls[0].SubParameters, Is.EqualTo(new[] { 3, -1 }));
            Assert.That(read.Menus[1].Controls[1].Menu, Is.EqualTo(0), "A submenu may point back to an earlier menu.");
            var layer = read.Controllers[0].Layers[0];
            Assert.That(layer.Machines[1].Parent, Is.EqualTo(0));
            Assert.That(layer.Machines[0].EntryTransitions[0].ToMachine, Is.EqualTo(1));
            Assert.That(layer.Machines[0].EntryTransitions[0].Timed, Is.False);
            Assert.That(layer.Machines[1].ExitTransitions[0].Exit, Is.True);
            Assert.That(layer.States[0].Transitions[0].InterruptionSource, Is.EqualTo("sourceThenDestination"));
            Assert.That(layer.States[1].TimeParameter, Is.EqualTo(1));
            Assert.That(layer.States[1].SpeedParameter, Is.Null);
            Assert.That(layer.States[2].Motion, Is.EqualTo(-1));
            Assert.That(layer.Machines[0].Behaviours[0].Operations[0].Source, Is.EqualTo(2));
            Assert.That(layer.Machines[0].Behaviours[0].Operations[1].Source, Is.Null);
            Assert.That(layer.States[0].Behaviours[0].Tracking[1], Is.EqualTo(new KeyValuePair<string, string>("leftHand", "animation")));
            Assert.That(read.Motions[2].Children[0].DirectParameter, Is.EqualTo(1));
            Assert.That(read.Clips[0].Tracks[0].Curves[0].Keys[0].OutTangent, Is.Null, "Stepped keys keep their null tangents.");
            Assert.That(read.Targets[0].Node, Is.EqualTo(3));
            Assert.That(read.Targets.Select(t => (t.Property, t.Node, t.Parameter, t.Rest)),
                Is.EqualTo(new[] { ("active", 3, 0, 0d), ("parameter", -1, 1, .25), ("parameter", -1, 2, 2d) }));
            Assert.That(read.Clips[0].Tracks.Select(t => t.Target), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(layer.Machines[0].Behaviours[0].Operations.Select(o => o.PreventRepeats), Is.EqualTo(new[] { false, false, true }));
            Assert.That((layer.SyncedLayer, layer.SyncedLayerAffectsTiming), Is.EqualTo((-1, false)));
            Assert.That((read.Controllers[0].Layers[1].SyncedLayer, read.Controllers[0].Layers[1].SyncedLayerAffectsTiming), Is.EqualTo((0, true)));
            Assert.That((read.Clips[0].CycleOffset, read.Clips[1].CycleOffset), Is.EqualTo((.25, 0d)));

            var json = Json(Sample());
            Assert.That(json["expressions"], Is.Empty);
            Assert.That(At(json, "parameters", 1).ContainsKey("saved"), Is.False, "Only expression parameters carry saved, synced and expressionType.");
            Assert.That(At(json, "controllers", 0, "layers", 0, "machines", 0, "entryTransitions", 0).ContainsKey("hasExitTime"), Is.False,
                "Entry and exit transitions have no timing.");
            Assert.That(At(json, "motions", 1, "children", 0).ContainsKey("directParameter"), Is.False);
            Assert.That(At(json, "targets", 1).Keys, Is.EqualTo(new[] { "property", "parameter", "rest" }), "A parameter target has no node.");
            Assert.That(At(json, "targets", 0).Keys, Is.EqualTo(new[] { "node", "property", "rest" }));
            Assert.That(At(json, "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", 0)["preventRepeats"], Is.EqualTo(false));
        }

        // A face of the sample plays clip 0: state Off directly, the first
        // state of the layer and entered from nothing else, and state On in its
        // trees, entered from Any State and from Off.
        static AvatarAnimatorData WithFace()
        {
            var data = Sample();
            var face = new AvatarAnimatorData.Expression { Name = "Face A" };
            face.Clips.Add(0);
            face.When.Add(new AvatarAnimatorData.Entrance { Layer = 0, State = 0, From = "default" });
            var any = new AvatarAnimatorData.Entrance { Layer = 0, State = 1, From = "any" };
            any.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = 2, Mode = "equals", Threshold = 3 });
            face.When.Add(any);
            var fromOff = new AvatarAnimatorData.Entrance { Layer = 0, State = 1, From = "state", FromState = 0 };
            fromOff.Conditions.Add(new AvatarAnimatorData.Condition { Parameter = 0, Mode = "if", Threshold = 0 });
            face.When.Add(fromOff);
            face.When.Add(new AvatarAnimatorData.Entrance { Layer = 0, State = 0, From = "entry", Machine = 1 });
            data.Expressions.Add(face);
            return data;
        }

        [Test]
        public void AnExpressionRoundTripsWithTheWaysIntoItsStates()
        {
            var json = Json(WithFace());
            var read = AvatarAnimatorData.Read(json).Expressions.Single();
            Assert.That((read.Name, string.Join(",", read.Clips)), Is.EqualTo(("Face A", "0")));
            Assert.That(read.When.Select(w => (w.Layer, w.State, w.From, w.Machine, w.FromState)),
                Is.EqualTo(new[] { (0, 0, "default", -1, -1), (0, 1, "any", -1, -1), (0, 1, "state", -1, 0), (0, 0, "entry", 1, -1) }));
            Assert.That(read.When[1].Conditions.Select(c => (c.Parameter, c.Mode, c.Threshold)), Is.EqualTo(new[] { (2, "equals", 3d) }));
            Assert.That(At(json, "expressions", 0, "when", 0).Keys, Is.EqualTo(new[] { "layer", "state", "from", "conditions" }));
            Assert.That(At(json, "expressions", 0, "when", 2).Keys, Is.EqualTo(new[] { "layer", "state", "from", "fromState", "conditions" }));
            Assert.That(At(json, "expressions", 0, "when", 3).Keys, Is.EqualTo(new[] { "layer", "state", "from", "machine", "conditions" }));
        }

        [Test]
        public void ReadRejectsAnExpressionThatDoesNotFitTheFx()
        {
            void RejectsFace(Action<Dictionary<string, object>> change)
            {
                var json = Json(WithFace());
                change(json);
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
            }
            Dictionary<string, object> Face(Dictionary<string, object> json) => At(json, "expressions", 0);
            Dictionary<string, object> When(Dictionary<string, object> json, int index) => At(json, "expressions", 0, "when", index);
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(WithFace())));
            RejectsFace(json => Face(json)["name"] = "");
            RejectsFace(json => ((List<object>)json["expressions"]).Add(JsonDom.Parse(JsonDom.Serialize(Face(json)))));
            RejectsFace(json => Face(json)["clips"] = new List<object>());
            RejectsFace(json => Face(json)["clips"] = new List<object> { 1L, 0L });
            RejectsFace(json => Face(json)["clips"] = new List<object> { 0L, 0L });
            RejectsFace(json => Face(json)["clips"] = new List<object> { 2L });
            // Inner plays nothing; layer 1 has no motions; layer 2 is not there.
            RejectsFace(json => When(json, 1)["state"] = 2L);
            RejectsFace(json => When(json, 1)["layer"] = 1L);
            RejectsFace(json => When(json, 1)["layer"] = 2L);
            RejectsFace(json => When(json, 1)["from"] = "exit");
            RejectsFace(json => When(json, 1)["machine"] = 0L);
            RejectsFace(json => When(json, 3).Remove("machine"));
            RejectsFace(json => When(json, 3)["machine"] = 2L);
            RejectsFace(json => When(json, 2)["fromState"] = 3L);
            RejectsFace(json => When(json, 0)["conditions"] = ((List<object>)When(json, 1)["conditions"]).ToList());
            // equals on the bool Hat is a mode Unity never takes.
            RejectsFace(json => At(json, "expressions", 0, "when", 1, "conditions", 0)["parameter"] = 0L);
            RejectsFace(json => At(json, "expressions", 0, "when", 1, "conditions", 0)["parameter"] = 4L);
        }

        // Unity moves a float parameter by a curve and holds any other at the
        // controller's default, so a parameter target is a parameter of the
        // table, its rest a value of its type, and only a float has tracks.
        [Test]
        public void ReadRejectsAParameterTargetOutsideItsTypeOrTheTable()
        {
            // Parameters 0, 1 and 2 are a bool, a float and an int; targets 1
            // and 2 are Blend (float) and GestureLeft (int).
            Rejects(json => At(json, "targets", 1)["parameter"] = 4L);
            Rejects(json => At(json, "targets", 1)["parameter"] = -1L);
            Rejects(json => At(json, "targets", 1)["parameter"] = 2L);
            Rejects(json =>
            {
                At(json, "targets", 2)["parameter"] = 1L;
                At(json, "targets", 2)["rest"] = 2L;
            });
            Rejects(json => At(json, "clips", 0, "tracks", 1)["target"] = 2L);
            Rejects(json => At(json, "targets", 2)["rest"] = .5);
            Rejects(json => At(json, "targets", 2)["parameter"] = 0L);
            Rejects(json => At(json, "targets", 1)["node"] = 3L);
            Rejects(json => At(json, "targets", 1).Remove("parameter"));
            Rejects(json => At(json, "targets", 0)["parameter"] = 1L);
            Rejects(json => At(json, "targets", 0)["rest"] = .5);
            Rejects(json => At(json, "targets", 1)["rest"] = "0.25");
            Rejects(json => At(json, "targets", 1)["rest"] = double.NaN);
            Rejects(json => At(json, "targets", 2)["rest"] = 2147483648L);
            Rejects(json => At(json, "targets", 2)["rest"] = -2147483649L);

            // Bool and int parameters may be targets at a value of their type.
            var targets = Json(Sample());
            At(targets, "targets", 2)["parameter"] = 0L;
            At(targets, "targets", 2)["rest"] = 1L;
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(targets));
            targets = Json(Sample());
            At(targets, "targets", 2)["rest"] = -3L;
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(targets));
            foreach (var rest in new[] { 2147483647L, -2147483648L })
            {
                targets = Json(Sample());
                At(targets, "targets", 2)["rest"] = rest;
                Assert.That(AvatarAnimatorData.Read(targets).Targets[2].Rest, Is.EqualTo((double)rest));
            }

            // Write checks the same contract, without rounding an active rest first.
            foreach (var rest in new[] { .9, 1.9, -.5 })
            {
                var data = Sample();
                data.Targets[0].Rest = rest;
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Write(data)).Message, Is.EqualTo(Invalid));
            }
        }

        [Test]
        public void ReadRejectsPreventRepeatsOffARandomIntoAnInt()
        {
            Dictionary<string, object> Op(Dictionary<string, object> json, int index) =>
                At(json, "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", index);
            // A copy, a random into a bool.
            Rejects(json => Op(json, 0)["preventRepeats"] = true);
            Rejects(json => Op(json, 1)["preventRepeats"] = true);
            Rejects(json => { Op(json, 2)["type"] = "set"; });
            Rejects(json => Op(json, 2)["preventRepeats"] = 1L);
        }

        // Data written before Prevent Repeats was read has no preventRepeats.
        [Test]
        public void ReadTakesAMissingPreventRepeatsAsOff()
        {
            var json = Json(Sample());
            foreach (var index in new[] { 0, 1, 2 })
                At(json, "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", index).Remove("preventRepeats");
            var data = AvatarAnimatorData.Read(json);
            Assert.That(data.Controllers[0].Layers[0].Machines[0].Behaviours[0].Operations.Select(o => o.PreventRepeats), Is.EqualTo(new[] { false, false, false }));
            Assert.That(At(Json(data), "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", 0)["preventRepeats"], Is.EqualTo(false), "Written again, it is there.");
        }

        [Test]
        public void ReadRejectsOtherSchemaVersions()
        {
            Rejects(json => json["schemaVersion"] = 2L);
            Rejects(json => json["schemaVersion"] = "1");
            Rejects(json => json["expressions"] = new List<object> { new Dictionary<string, object>() });
        }

        [Test]
        public void ReadRejectsIndicesOutsideTheirTables()
        {
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0)["toState"] = 3L);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 0, "entryTransitions", 0)["toMachine"] = 2L);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0)["motion"] = 3L);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0)["motion"] = -2L);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0, "conditions", 0)["parameter"] = 4L);
            Rejects(json => At(json, "menus", 0, "controls", 1)["menu"] = 2L);
            Rejects(json => At(json, "menus", 0, "controls", 0)["parameter"] = -1L);
            // Menu controls point at Expression Parameters only.
            Rejects(json => At(json, "menus", 0, "controls", 0)["parameter"] = 1L);
            Rejects(json => ((List<object>)At(json, "menus", 1, "controls", 0)["subParameters"])[0] = 2L);
            // A copy has a source and nothing else has.
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", 0).Remove("source"));
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 0, "behaviours", 0, "operations", 1)["source"] = 2L);
            Rejects(json => At(json, "motions", 0)["clip"] = 2L);
            Rejects(json => At(json, "clips", 0, "tracks", 0)["target"] = 3L);
            // The root machine has no parent; every other machine follows its parent.
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 1)["parent"] = 1L);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 0)["parent"] = 0L);
            // A blend tree must not contain itself.
            Rejects(json => At(json, "motions", 1, "children", 1)["motion"] = 1L);
        }

        [Test]
        public void ReadRejectsValuesOutsideTheEnumerations()
        {
            Rejects(json => At(json, "parameters", 0)["type"] = "string");
            Rejects(json => At(json, "parameters", 1)["origin"] = "expression");
            Rejects(json => At(json, "menus", 0, "controls", 0)["type"] = "stick");
            Rejects(json => At(json, "controllers", 0)["playable"] = "gesture");
            Rejects(json => At(json, "controllers", 0, "layers", 0)["blending"] = "multiply");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0, "conditions", 0)["mode"] = "exitTime");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0)["interruptionSource"] = "both");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "behaviours", 0)["head"] = "off");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 1, "behaviours", 0)["playable"] = "sitting");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 1, "behaviours", 0)["type"] = "playAudio");
            Rejects(json => At(json, "motions", 1)["blendType"] = "3d");
            Rejects(json => At(json, "targets", 0)["property"] = "blendShape");
            Rejects(json => At(json, "targets", 0)["rest"] = 2L);
            Rejects(json => At(json, "omitted", 0)["kind"] = "camera");
        }

        [Test]
        public void ReadRejectsTheWrongKeys()
        {
            Rejects(json => json["extra"] = 1L);
            Rejects(json => json.Remove("omitted"));
            Rejects(json => At(json, "parameters", 1)["saved"] = true);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "machines", 0, "entryTransitions", 0)["hasExitTime"] = false);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0).Remove("toState"));
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0)["exit"] = true);
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 2, "transitions", 0)["exit"] = false);
            Rejects(json => At(json, "motions", 1, "children", 0)["directParameter"] = 1L);
            Rejects(json => At(json, "motions", 2, "children", 0).Remove("directParameter"));
            Rejects(json => At(json, "menus", 0, "controls", 0)["menu"] = 1L);
            Rejects(json => At(json, "controllers", 0, "layers", 0).Remove("syncedLayer"));
            Rejects(json => At(json, "controllers", 0, "layers", 1).Remove("syncedLayerAffectsTiming"));
            Rejects(json => At(json, "clips", 0).Remove("cycleOffset"));
            // Tracking parts are ASCII, as the schema's pattern.
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "behaviours", 0)["h\u00e9ad"] = "noChange");
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "behaviours", 0)["\u0127ead"] = "noChange");
        }

        [Test]
        public void ReadRejectsSyncedLayersOutsideTheControllerAndCycleOffsetsThatAreNotFinite()
        {
            Rejects(json => At(json, "controllers", 0, "layers", 1)["syncedLayer"] = 2L);
            Rejects(json => At(json, "controllers", 0, "layers", 1)["syncedLayer"] = 1L);
            // The source of a synced layer is not synced itself.
            Rejects(json => At(json, "controllers", 0, "layers", 0)["syncedLayer"] = 1L);
            Rejects(json => At(json, "controllers", 0, "layers", 0)["syncedLayer"] = -2L);
            Rejects(json => At(json, "controllers", 0, "layers", 1)["syncedLayer"] = .5);
            Rejects(json => At(json, "controllers", 0, "layers", 1)["syncedLayerAffectsTiming"] = 1L);
            // A synced layer holds a copy of its source's machines and states.
            // The machine that held the removed state points at none, so only
            // the shape rejects it.
            Rejects(json =>
            {
                At(json, "controllers", 0, "layers", 1, "machines", 1)["defaultState"] = -1L;
                ((List<object>)At(json, "controllers", 0, "layers", 1)["states"]).RemoveAt(2);
            });
            Rejects(json =>
            {
                var machines = (List<object>)At(json, "controllers", 0, "layers", 1)["machines"];
                var extra = new Dictionary<string, object>(At(json, "controllers", 0, "layers", 1, "machines", 1)) { ["defaultState"] = -1L };
                machines.Add(extra);
            });
            Rejects(json => At(json, "controllers", 0, "layers", 1, "states", 2)["machine"] = 0L);
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = double.NaN);
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = double.PositiveInfinity);
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = "0.25");
            // A blend tree child's position is a Unity float.
            Rejects(json => At(json, "motions", 1, "children", 0)["position"] = new List<object> { 1e39, 0.0 });
            Rejects(json => At(json, "motions", 1, "children", 0)["position"] = new List<object> { 0.0, -1e200 });
        }

        // Adds a chain of blend trees on top of the first clip. Outer first puts
        // the outermost tree at the lowest index, so that the check walks down
        // the whole chain from one root.
        static void Nest(Dictionary<string, object> json, int trees, bool outerFirst)
        {
            var motions = (List<object>)json["motions"];
            var first = motions.Count;
            for (var index = 0; index < trees; index++)
                motions.Add(Tree(outerFirst ? (index == trees - 1 ? 0 : first + index + 1) : (index == 0 ? 0 : first + index - 1)));
        }

        // A direct blend tree that holds the motions given, in that order.
        static Dictionary<string, object> Tree(params int[] children) => new Dictionary<string, object>
        {
            ["type"] = "blendTree", ["blendType"] = "direct", ["parameters"] = new List<object>(), ["normalizedBlendValues"] = false,
            ["children"] = children.Select(child => (object)new Dictionary<string, object>
            {
                ["motion"] = (long)child, ["threshold"] = 0.0, ["position"] = new List<object> { 0.0, 0.0 },
                ["timeScale"] = 1.0, ["cycleOffset"] = 0.0, ["mirror"] = false, ["directParameter"] = 1L
            }).ToList()
        };

        // Adds trees on top of the first clip: each of the doubled ones holds
        // the one before it twice, so the n-th expands to 2^(n+1) - 1 motions,
        // and a last tree holds the last of them twice and the clip as often
        // as given.
        static void Widen(Dictionary<string, object> json, int doubled, int clips)
        {
            var motions = (List<object>)json["motions"];
            var inner = 0;
            for (var level = 0; level < doubled; level++)
            {
                motions.Add(Tree(inner, inner));
                inner = motions.Count - 1;
            }
            motions.Add(Tree(new[] { inner, inner }.Concat(Enumerable.Repeat(0, clips)).ToArray()));
        }

        // Unity takes If and IfNot on a bool or a trigger, Greater and Less on
        // a float or an int, Equals and NotEqual on an int. A condition of
        // another mode never holds in Unity, so it is not data to play.
        [Test]
        public void ReadRejectsAConditionOfAModeThatIsNotItsParametersTypes()
        {
            // Parameters 0, 1 and 2 are a bool, a float and an int.
            void Set(Dictionary<string, object> json, string mode, int parameter)
            {
                var condition = At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0, "conditions", 0);
                condition["mode"] = mode;
                condition["parameter"] = (long)parameter;
            }
            foreach (var (mode, types) in new[] { ("if", "100"), ("ifNot", "100"), ("greater", "011"), ("less", "011"), ("equals", "001"), ("notEqual", "001") })
                for (var parameter = 0; parameter < 3; parameter++)
                {
                    if (types[parameter] == '0') { Rejects(json => Set(json, mode, parameter)); continue; }
                    var json = Json(Sample());
                    Set(json, mode, parameter);
                    Assert.DoesNotThrow(() => AvatarAnimatorData.Read(json), mode + " on parameter " + parameter);
                }
            // A trigger is only waited for: If is the one mode Unity takes on it.
            Assert.That(new[] { "if", "ifNot" }.Select(mode => string.Join(" ", AvatarAnimatorData.ConditionTypes(mode))), Is.EqualTo(new[] { "bool trigger", "bool" }));
        }

        // Unity looks at Any State transitions on a layer's root machine
        // alone: one on a machine inside another is not data to play.
        [Test]
        public void ReadRejectsAnyStateTransitionsOnAMachineInsideAnother()
        {
            List<object> Any(Dictionary<string, object> json, int machine) => (List<object>)At(json, "controllers", 0, "layers", 0, "machines", machine)["anyStateTransitions"];
            Rejects(json => Any(json, 1).Add(Any(json, 0)[0]));
        }

        // A blend tree, a child of a direct tree and a state's Speed, Motion
        // Time and Cycle Offset read a float, and Mirror reads a bool. Unity
        // does not read the value of a parameter of another type there.
        [Test]
        public void ReadRejectsATreeOrAStateOptionOnAParameterOfAnotherType()
        {
            // Parameters 0, 1 and 2 are a bool, a float and an int.
            Dictionary<string, object> State(Dictionary<string, object> json) => At(json, "controllers", 0, "layers", 0, "states", 1);
            Rejects(json => ((List<object>)At(json, "motions", 1)["parameters"])[1] = 2L);
            Rejects(json => At(json, "motions", 2, "children", 0)["directParameter"] = 0L);
            foreach (var (key, type) in new[] { ("speedParameter", 1L), ("cycleOffsetParameter", 1L), ("timeParameter", 1L), ("mirrorParameter", 0L) })
            {
                Rejects(json => State(json)[key] = 2L);
                Rejects(json => State(json)[key] = 1L - type);
                var json = Json(Sample());
                State(json)[key] = type;
                Assert.DoesNotThrow(() => AvatarAnimatorData.Read(json), key);
            }
        }

        // A tree is written once and played under each tree that holds it:
        // the motions one motion expands to are counted with every repeat,
        // without recursion and without counting past the limit.
        [Test]
        public void ReadRejectsBlendTreesThatExpandOverTheLimit()
        {
            Assert.That(AvatarAnimatorData.MaximumExpandedMotions, Is.EqualTo(4096));
            // Ten doubled trees expand to 2047: the last tree to 1 + 2 * 2047 + clips.
            var json = Json(Sample());
            Widen(json, 10, 1);
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(json));
            Rejects(changed => Widen(changed, 10, 2));
            // 2^33 - 1 motions, in trees nested as deep as the format allows.
            Rejects(changed => Widen(changed, AvatarAnimatorData.MaximumBlendTreeDepth - 1, 0));
        }

        // The depth is checked without recursion: a chain as long as the motion
        // table allows is rejected like any other data outside the limits.
        [Test]
        public void ReadRejectsBlendTreesNestedOverTheLimit()
        {
            foreach (var outerFirst in new[] { false, true })
            {
                var json = Json(Sample());
                Nest(json, AvatarAnimatorData.MaximumBlendTreeDepth, outerFirst);
                Assert.DoesNotThrow(() => AvatarAnimatorData.Read(json));
                Rejects(changed => Nest(changed, AvatarAnimatorData.MaximumBlendTreeDepth + 1, outerFirst));
                Rejects(changed => Nest(changed, 60000, outerFirst));
            }
            // A tree without children still counts as one level.
            Rejects(changed =>
            {
                Nest(changed, AvatarAnimatorData.MaximumBlendTreeDepth + 1, true);
                ((List<object>)At(changed, "motions", ((List<object>)changed["motions"]).Count - 1)["children"]).Clear();
            });
            // A tracking part must not take the key that names the behaviour.
            Assert.That(AvatarAnimatorData.IsTrackingPart("type"), Is.False);
            Assert.That(AvatarAnimatorData.IsTrackingPart("head"), Is.True);
        }

        [Test]
        public void ReadRejectsTablesOverTheirLimits()
        {
            Rejects(json =>
            {
                var controls = (List<object>)At(json, "menus", 0)["controls"];
                while (controls.Count < 65) controls.Add(controls[0]);
            });
            Rejects(json =>
            {
                var conditions = (List<object>)At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0)["conditions"];
                while (conditions.Count < 257) conditions.Add(conditions[0]);
            });
            Rejects(json => At(json, "parameters", 0)["name"] = new string('a', 1025));
            Rejects(json => At(json, "omitted", 1)["kind"] = "blendShape");
            Rejects(json => At(json, "omitted", 0)["count"] = 0L);
            Rejects(json => ((List<object>)json["targets"]).Add(At(json, "targets", 0)));
            // The key list keeps the expression animation contract.
            Rejects(json => ((List<object>)At(json, "clips", 0, "tracks", 0, "curves", 0)["keys"]).Add(new List<object> { 0.0, 0.0, null, null, 1d / 3, 1d / 3 }));
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(Sample())));
        }

        // A morph target names a mesh target of its node and the three
        // weights that turn the FX's weight into the target's. It follows the
        // sample's active and parameter targets, and its track follows theirs
        // in the same clip.
        const int MorphTarget = 3;

        static AvatarAnimatorData WithMorph()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "morph", Morph = 2, Rest = 35, Baked = 35, End = 100 });
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 80, InTangent = 0, OutTangent = 0 });
            var track = new AvatarAnimatorData.Track { Target = MorphTarget };
            track.Curves.Add(curve);
            data.Clips[0].Tracks.Add(track);
            return data;
        }

        static void RejectsMorph(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithMorph());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void AMorphTargetRoundTrips()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithMorph()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            var morph = read.Targets[MorphTarget];
            Assert.That((morph.Node, morph.Property, morph.Morph, morph.Rest, morph.Baked, morph.End), Is.EqualTo((3, "morph", 2, 35d, 35d, 100d)));
            var json = Json(WithMorph());
            Assert.That(At(json, "targets", MorphTarget).Keys.OrderBy(key => key, StringComparer.Ordinal),
                Is.EqualTo(new[] { "baked", "end", "morph", "node", "property", "rest" }));
            Assert.That(At(json, "targets", 0).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "node", "property", "rest" }),
                "An activeSelf target keeps its keys.");
            // Rest, baked and end are Unity weights; a target may sit past its last frame.
            var wide = WithMorph();
            (wide.Targets[MorphTarget].Rest, wide.Targets[MorphTarget].Baked, wide.Targets[MorphTarget].End) = (-50, 100, 0);
            Assert.That(AvatarAnimatorData.Read(Json(wide)).Targets[MorphTarget].End, Is.EqualTo(0));
        }

        // Active, parameter and morph targets in one file, with one clip
        // holding a track of each kind, read back as written.
        [Test]
        public void AllTargetKindsRoundTripTogether()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithMorph()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Targets.Select(t => (t.Property, t.Node)),
                Is.EqualTo(new[] { ("active", 3), ("parameter", -1), ("parameter", -1), ("morph", 3) }));
            Assert.That(read.Clips[0].Tracks.Select(t => read.Targets[t.Target].Property), Is.EqualTo(new[] { "active", "parameter", "morph" }));
            Assert.That(read.Clips[0].Tracks[2].Curves[0].Keys[0].Value, Is.EqualTo(80));
            var json = Json(WithMorph());
            Assert.That(At(json, "targets", 1).ContainsKey("node"), Is.False, "A parameter target still has no node.");
            Assert.That(At(json, "targets", MorphTarget).ContainsKey("parameter"), Is.False, "A morph target has no parameter.");
            // Each kind keeps its own keys.
            RejectsMorph(changed => At(changed, "targets", 1)["morph"] = 0L);
            RejectsMorph(changed => At(changed, "targets", 1)["node"] = 3L);
            RejectsMorph(changed => At(changed, "targets", MorphTarget)["parameter"] = 1L);
        }

        [Test]
        public void ReadRejectsABrokenMorphTarget()
        {
            // A target that shows no change cannot carry a weight.
            RejectsMorph(json => At(json, "targets", MorphTarget)["end"] = 35L);
            // One target per node and mesh target.
            RejectsMorph(json => ((List<object>)json["targets"]).Add(At(json, "targets", MorphTarget)));
            RejectsMorph(json => At(json, "targets", MorphTarget)["morph"] = -1L);
            RejectsMorph(json => At(json, "targets", MorphTarget)["morph"] = 1.5);
            RejectsMorph(json => At(json, "targets", MorphTarget).Remove("baked"));
            RejectsMorph(json => At(json, "targets", MorphTarget)["extra"] = 1L);
            RejectsMorph(json => At(json, "targets", 0)["morph"] = 0L);
            RejectsMorph(json => At(json, "targets", MorphTarget)["rest"] = "35");
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(WithMorph())));
            // The same mesh target on another node, and the node's activeSelf, are their own targets.
            var data = WithMorph();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "morph", Morph = 2, Rest = 0, Baked = 0, End = 100 });
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(data)));
        }
    }
}
