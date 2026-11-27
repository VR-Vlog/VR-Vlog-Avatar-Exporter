using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            Rejects(json => json["schemaVersion"] = 3L);
            Rejects(json => json["schemaVersion"] = 0L);
            Rejects(json => json["schemaVersion"] = "1");
            Rejects(json => json["expressions"] = new List<object> { new Dictionary<string, object>() });
        }

        // The sample with a clip of clip 0's tracks under another name and
        // timing (2), a second clip without tracks (3), and one whose tracks
        // differ from clip 0's in one value (4).
        static AvatarAnimatorData WithSharedTracks()
        {
            var data = Sample();
            var again = new AvatarAnimatorData.Clip { Name = "Hat On again", Duration = 2, Loop = true, CycleOffset = .5 };
            again.Tracks.AddRange(AvatarAnimatorData.Read(Json(Sample())).Clips[0].Tracks);
            data.Clips.Add(again);
            data.Clips.Add(new AvatarAnimatorData.Clip { Name = "Empty again", Duration = 0 });
            var other = AvatarAnimatorData.Read(Json(Sample())).Clips[0];
            other.Name = "Hat On other";
            other.Tracks[1].Curves[0].Keys[0].Value += .5;
            data.Clips.Add(other);
            return data;
        }

        [Test]
        public void AClipWithTheTracksOfAnEarlierOneNamesIt()
        {
            var json = Json(WithSharedTracks());
            Assert.That(json["schemaVersion"], Is.EqualTo(2L));
            Assert.That(At(json, "clips", 2).Keys, Is.EqualTo(new[] { "name", "duration", "loop", "cycleOffset", "sameTracks" }));
            Assert.That(At(json, "clips", 2)["sameTracks"], Is.EqualTo(0L));
            Assert.That(At(json, "clips", 3).ContainsKey("tracks"), Is.True, "A clip without tracks names no other.");
            Assert.That(At(json, "clips", 4).ContainsKey("tracks"), Is.True, "Tracks that differ in one value are written.");

            var read = AvatarAnimatorData.Read(json);
            Assert.That((read.Clips[2].Name, read.Clips[2].Duration, read.Clips[2].Loop, read.Clips[2].CycleOffset), Is.EqualTo(("Hat On again", 2d, true, .5)));
            Assert.That(read.Clips[2].Tracks, Is.EqualTo(read.Clips[0].Tracks), "It plays the tracks of the clip it names.");
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(JsonDom.Serialize(json)), "Written again, it names the same clip.");
        }

        // Schema 1, written before sameTracks, is read as before; sameTracks is not part of it.
        [Test]
        public void ReadTakesSchema1WithoutSameTracks()
        {
            var json = Json(Sample());
            json["schemaVersion"] = 1L;
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(AvatarAnimatorData.Read(json))), Is.EqualTo(JsonDom.Serialize(Json(Sample()))));
            var shared = Json(WithSharedTracks());
            shared["schemaVersion"] = 1L;
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(shared)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void ReadRejectsSameTracksThatDoNotNameAnEarlierClipWithTracks()
        {
            void RejectsShared(Action<Dictionary<string, object>> change)
            {
                var json = Json(WithSharedTracks());
                change(json);
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
            }
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = 2L);
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = 3L);
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = 1L);
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = -1L);
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = "0");
            RejectsShared(json => At(json, "clips", 2)["sameTracks"] = .5);
            RejectsShared(json => At(json, "clips", 2)["tracks"] = new List<object>());
            // A clip that names another names one with tracks of its own.
            RejectsShared(json =>
            {
                At(json, "clips", 3).Remove("tracks");
                At(json, "clips", 3)["sameTracks"] = 2L;
            });
        }

        [Test]
        public void PackAndUnpackGiveBackTheDocument()
        {
            var data = WithSharedTracks();
            data.Clips[0].Name = "帽子 On";
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(data));
            var packed = AvatarAnimatorData.Pack(text);
            Assert.That((packed[0], packed[1]), Is.EqualTo(((byte)0x1f, (byte)0x8b)), "gzip");
            Assert.That(AvatarAnimatorData.Unpack(packed), Is.EqualTo(text));
            Assert.That(AvatarAnimatorData.Pack(text), Is.EqualTo(packed), "The same document packs to the same bytes.");
        }

        [Test]
        public void UnpackRejectsWhatIsNotAPackedDocument()
        {
            void RejectsPacked(byte[] packed) =>
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Unpack(packed)).Message, Is.EqualTo(Invalid));
            RejectsPacked(null);
            RejectsPacked(new byte[0]);
            RejectsPacked(new byte[] { 0x1f, 0x8b, 8 });
            RejectsPacked(Encoding.UTF8.GetBytes("{\"schemaVersion\":2}"));
            RejectsPacked(new byte[] { 0x1f, 0x8b, 8, 0, 0, 0, 0, 0, 0, 0xff, 1, 2, 3 });
            // gzip of bytes that are not UTF-8.
            RejectsPacked(Gzip(new byte[] { 0x7b, 0xff, 0xfe, 0x7d }));
            // A document one byte over the limit, as small as it compresses.
            RejectsPacked(Gzip(new byte[AvatarAnimatorData.MaximumDocumentBytes + 1]));
            Assert.That(AvatarAnimatorData.Unpack(Gzip(Encoding.UTF8.GetBytes(new string(' ', AvatarAnimatorData.MaximumDocumentBytes)))).Length,
                Is.EqualTo(AvatarAnimatorData.MaximumDocumentBytes), "A document of exactly the limit is read.");
        }

        static byte[] Gzip(byte[] bytes)
        {
            using (var output = new System.IO.MemoryStream())
            {
                using (var gzip = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionLevel.Fastest, true)) gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }

        // The extension in the glTF holds only the version and the bufferView,
        // and Read rejects it, as a reader of schema 1 does.
        [Test]
        public void APackedExtensionNamesOnlyItsBufferView()
        {
            var packed = (Dictionary<string, object>)JsonDom.Parse(JsonDom.Serialize(AvatarAnimatorData.Packed(3)));
            Assert.That(JsonDom.Serialize(packed), Is.EqualTo("{\"schemaVersion\":2,\"bufferView\":3}"));
            Assert.That((AvatarAnimatorData.IsPacked(packed), AvatarAnimatorData.IsPacked(Json(Sample()))), Is.EqualTo((true, false)));
            Assert.That(AvatarAnimatorData.PackedView(packed), Is.EqualTo(3));
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(packed)).Message, Is.EqualTo(Invalid));
            Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Packed(-1));
            foreach (var bad in new[] { "{\"schemaVersion\":1,\"bufferView\":3}", "{\"schemaVersion\":2,\"bufferView\":-1}", "{\"schemaVersion\":2,\"bufferView\":\"3\"}",
                         "{\"schemaVersion\":2,\"bufferView\":3,\"clips\":[]}", "{\"schemaVersion\":2}", "[]" })
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.PackedView(JsonDom.Parse(bad))).Message, Is.EqualTo(Invalid), bad);
        }

        [Test]
        public void BufferViewRangeIsAViewOfTheBinaryChunk()
        {
            var gltf = (Dictionary<string, object>)JsonDom.Parse("{\"bufferViews\":[{\"buffer\":0,\"byteOffset\":8,\"byteLength\":5},{\"buffer\":0,\"byteLength\":4}," +
                "{\"buffer\":1,\"byteLength\":4},{\"buffer\":0,\"byteLength\":4,\"byteStride\":4},{\"buffer\":0,\"byteLength\":0}," +
                "{\"buffer\":0,\"byteLength\":" + (AvatarAnimatorData.MaximumDocumentBytes + 1) + "},{\"buffer\":0,\"byteOffset\":-4,\"byteLength\":4}]}");
            Assert.That(AvatarAnimatorData.BufferViewRange(gltf, 0), Is.EqualTo((8, 5)));
            Assert.That(AvatarAnimatorData.BufferViewRange(gltf, 1), Is.EqualTo((0, 4)));
            foreach (var view in new[] { 2, 3, 4, 5, 6, 7, -1 })
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.BufferViewRange(gltf, view)).Message, Is.EqualTo(Invalid), view.ToString());
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.BufferViewRange(new Dictionary<string, object>(), 0)).Message, Is.EqualTo(Invalid));
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
            // Any other text is a kind a newer exporter writes (AnUnknownTargetIsReadAsOneNothingWrites).
            Rejects(json => At(json, "targets", 0)["property"] = "");
            Rejects(json => At(json, "targets", 0)["property"] = 1L);
            Rejects(json => At(json, "targets", 0).Remove("property"));
            Rejects(json => At(json, "targets", 0)["rest"] = 2L);
            // A kind a newer exporter adds comes after the known ones (AnOmittedKindANewerExporterAddsIsKept).
            Rejects(json => At(json, "omitted", 0)["kind"] = "camera");
        }

        [Test]
        public void AnOmittedKindANewerExporterAddsIsKept()
        {
            var json = Json(Sample());
            var omitted = (List<object>)json["omitted"];
            omitted.Add(new Dictionary<string, object> { ["kind"] = "camera", ["count"] = 2L });
            omitted.Add(new Dictionary<string, object> { ["kind"] = "light", ["count"] = 1L });
            var text = JsonDom.Serialize(json);
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(read.Omitted.Select(o => (o.Kind, o.Count)),
                Is.EqualTo(new[] { ("blendShape", 2), ("behaviour", 1), ("operation", 3), ("camera", 2), ("light", 1) }));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));

            // The kinds this exporter adds are known ones, in their place.
            var data = Sample();
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "materialProperty", Count = 4 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "materialSwitch", Count = 1 });
            Assert.That(AvatarAnimatorData.Read(Json(data)).Omitted.Select(o => o.Kind), Is.EqualTo(new[] { "blendShape", "behaviour", "operation", "materialProperty", "materialSwitch" }));
            Rejects(changed => ((List<object>)changed["omitted"]).Insert(0, new Dictionary<string, object> { ["kind"] = "materialSwitch", ["count"] = 1L }));

            // A newer kind comes once, after every known one, and its name is text.
            static void Add(Dictionary<string, object> changed, object kind) =>
                ((List<object>)changed["omitted"]).Add(new Dictionary<string, object> { ["kind"] = kind, ["count"] = 1L });
            Rejects(changed => { Add(changed, "camera"); Add(changed, "camera"); });
            Rejects(changed => { Add(changed, "camera"); Add(changed, "unreadParameter"); });
            Rejects(changed => Add(changed, ""));
            Rejects(changed => Add(changed, 1L));
            Rejects(changed => Add(changed, new string('a', 1025)));
            Rejects(changed =>
            {
                for (var i = 0; i <= AvatarAnimatorData.MaximumOmittedKinds; i++) Add(changed, "kind" + i);
            });
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

        // A target of a property the reader does not know, as a newer
        // exporter writes one, with a track of clip 0 on it.
        static Dictionary<string, object> WithUnknown(Dictionary<string, object> target)
        {
            var json = Json(Sample());
            var targets = (List<object>)json["targets"];
            targets.Add(target);
            var curve = At(json, "clips", 0, "tracks", 0)["curves"];
            ((List<object>)At(json, "clips", 0)["tracks"]).Add(new Dictionary<string, object> { ["target"] = (long)(targets.Count - 1), ["curves"] = curve });
            return json;
        }

        static Dictionary<string, object> MaterialVector(object rest) => new Dictionary<string, object>
        {
            ["node"] = 3L, ["property"] = "materialVector", ["material"] = 1L, ["name"] = "_Color", ["rest"] = rest,
            ["range"] = new List<object> { 0L, 1L }
        };

        [Test]
        public void AnUnknownTargetIsReadAsOneNothingWrites()
        {
            var json = WithUnknown(MaterialVector(.5));
            var text = JsonDom.Serialize(json);
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            var unknown = read.Targets[3];
            Assert.That((unknown.Known, unknown.Property, unknown.Node, unknown.Rest), Is.EqualTo((false, "materialVector", -1, .5)));
            Assert.That(read.Targets.Take(3).All(t => t.Known), Is.True);
            Assert.That(read.Clips[0].Tracks.Select(t => t.Target), Is.EqualTo(new[] { 0, 1, 3 }), "A clip may move it.");
            // Write keeps every key as read, so Write -> Read -> Write is the same text.
            var written = JsonDom.Serialize(AvatarAnimatorData.Write(read));
            Assert.That(written, Is.EqualTo(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(AvatarAnimatorData.Read(JsonDom.Parse(written)))), Is.EqualTo(written));

            // Its keys are not looked at: a rest that is not a number reads as 0,
            // no rest at all too, and two alike are both kept.
            Assert.That(AvatarAnimatorData.Read(WithUnknown(MaterialVector("half"))).Targets[3].Rest, Is.EqualTo(0));
            Assert.That(AvatarAnimatorData.Read(WithUnknown(new Dictionary<string, object> { ["property"] = "futureKind" })).Targets[3].Rest, Is.EqualTo(0));
            var twice = WithUnknown(MaterialVector(1L));
            ((List<object>)twice["targets"]).Add(MaterialVector(1L));
            Assert.That(AvatarAnimatorData.Read(twice).Targets.Count(t => !t.Known), Is.EqualTo(2));

            // One made in code, with no keys read, is written as its property and rest.
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Property = "futureKind", Node = -1, Rest = 1 });
            var made = Json(data);
            Assert.That(At(made, "targets", 3).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "property", "rest" }));
            Assert.That(AvatarAnimatorData.Read(made).Targets[3].Rest, Is.EqualTo(1));

            // A known kind still has to carry its own keys.
            Rejects(changed => At(changed, "targets", 0)["material"] = 1L);
        }

        // A renderer enabled target and two materialFloat targets follow the
        // sample's active and parameter targets, each with a track of clip 0.
        const int EnabledTarget = 3;

        static AvatarAnimatorData WithRendererAndMaterial()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "enabled", Component = "renderer", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialFloat", Material = 0, Name = "_Cutoff", Rest = .5 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialFloat", Material = 2, Name = "_Cutoff", Rest = .25 });
            for (var t = EnabledTarget; t < EnabledTarget + 3; t++)
            {
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = 0, OutTangent = 0 });
                var track = new AvatarAnimatorData.Track { Target = t };
                track.Curves.Add(curve);
                data.Clips[0].Tracks.Add(track);
            }
            return data;
        }

        static void RejectsRendererAndMaterial(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithRendererAndMaterial());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void EnabledAndMaterialFloatTargetsRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithRendererAndMaterial()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Targets.Skip(EnabledTarget).All(t => t.Known), Is.True);
            var enabled = read.Targets[EnabledTarget];
            Assert.That((enabled.Node, enabled.Property, enabled.Component, enabled.Rest), Is.EqualTo((3, "enabled", "renderer", 1d)));
            var material = read.Targets[EnabledTarget + 2];
            Assert.That((material.Node, material.Property, material.Material, material.Name, material.Rest), Is.EqualTo((-1, "materialFloat", 2, "_Cutoff", .25)));
            var json = Json(WithRendererAndMaterial());
            Assert.That(At(json, "targets", EnabledTarget).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "component", "node", "property", "rest" }));
            Assert.That(At(json, "targets", EnabledTarget + 1).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "material", "name", "property", "rest" }));
            // Another property of the same material, and the renderer of
            // another node, are their own targets.
            var data = WithRendererAndMaterial();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialFloat", Material = 0, Name = "_Alpha", Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "enabled", Component = "renderer", Rest = 0 });
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(data)));
        }

        [Test]
        public void ReadRejectsABrokenEnabledOrMaterialFloatTarget()
        {
            const int Material = EnabledTarget + 1;
            // An enabled target is a flag of a node's component.
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget)["rest"] = 2L);
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget)["rest"] = .5);
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget).Remove("node"));
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget).Remove("component"));
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget)["component"] = 1L);
            RejectsRendererAndMaterial(json => At(json, "targets", EnabledTarget)["material"] = 0L);
            RejectsRendererAndMaterial(json => ((List<object>)json["targets"]).Add(At(json, "targets", EnabledTarget)));
            // A materialFloat target names a glTF material and a property, and no node.
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["node"] = 3L);
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["material"] = -1L);
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["material"] = 1.5);
            RejectsRendererAndMaterial(json => At(json, "targets", Material).Remove("name"));
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["name"] = "");
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["name"] = 1L);
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["rest"] = "0.5");
            RejectsRendererAndMaterial(json => At(json, "targets", Material)["extra"] = 1L);
            RejectsRendererAndMaterial(json => ((List<object>)json["targets"]).Add(At(json, "targets", Material)));
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(WithRendererAndMaterial())));
        }

        // A materialColor and a materialTextureST target follow the sample's
        // active and parameter targets, each with a track of clip 0.
        const int ColorTarget = 3;

        static AvatarAnimatorData WithMaterialChannels()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialColor", Material = 1, Name = "_Color", Channel = "g", Rest = .75 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialTextureST", Material = 0, Name = "_MainTex", Channel = "z", Rest = .125 });
            for (var t = ColorTarget; t < ColorTarget + 2; t++)
            {
                var curve = new ExpressionAnimationData.Curve();
                curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0, InTangent = 0, OutTangent = 0 });
                var track = new AvatarAnimatorData.Track { Target = t };
                track.Curves.Add(curve);
                data.Clips[0].Tracks.Add(track);
            }
            return data;
        }

        static void RejectsMaterialChannels(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithMaterialChannels());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void MaterialColorAndTextureSTTargetsRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithMaterialChannels()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Targets.Skip(ColorTarget).Select(t => (t.Known, t.Node, t.Property, t.Material, t.Name, t.Channel, t.Rest)), Is.EqualTo(new[]
            {
                (true, -1, "materialColor", 1, "_Color", "g", .75),
                (true, -1, "materialTextureST", 0, "_MainTex", "z", .125),
            }));
            var json = Json(WithMaterialChannels());
            Assert.That(At(json, "targets", ColorTarget).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "channel", "material", "name", "property", "rest" }));
            Assert.That(At(json, "targets", ColorTarget + 1).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "channel", "material", "name", "property", "rest" }));
            // Another channel of the same property, the same channel of another
            // material, and a materialFloat of the same name are their own targets.
            var data = WithMaterialChannels();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialColor", Material = 1, Name = "_Color", Channel = "a", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialColor", Material = 0, Name = "_Color", Channel = "g", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialFloat", Material = 1, Name = "_Color", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "materialTextureST", Material = 0, Name = "_MainTex", Channel = "x", Rest = 1 });
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(data)));
        }

        [Test]
        public void ReadRejectsABrokenMaterialColorOrTextureSTTarget()
        {
            foreach (var t in new[] { ColorTarget, ColorTarget + 1 })
            {
                // Like a materialFloat target, with a channel of its kind.
                RejectsMaterialChannels(json => At(json, "targets", t)["node"] = 3L);
                RejectsMaterialChannels(json => At(json, "targets", t)["material"] = -1L);
                RejectsMaterialChannels(json => At(json, "targets", t).Remove("name"));
                RejectsMaterialChannels(json => At(json, "targets", t)["name"] = "");
                RejectsMaterialChannels(json => At(json, "targets", t).Remove("channel"));
                RejectsMaterialChannels(json => At(json, "targets", t)["channel"] = 0L);
                RejectsMaterialChannels(json => At(json, "targets", t)["channel"] = "R");
                RejectsMaterialChannels(json => At(json, "targets", t)["rest"] = "0.5");
                RejectsMaterialChannels(json => At(json, "targets", t)["extra"] = 1L);
                RejectsMaterialChannels(json => ((List<object>)json["targets"]).Add(At(json, "targets", t)));
            }
            // A color has r, g, b and a; a texture's scale and offset x, y, z and w.
            RejectsMaterialChannels(json => At(json, "targets", ColorTarget)["channel"] = "x");
            RejectsMaterialChannels(json => At(json, "targets", ColorTarget + 1)["channel"] = "r");
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(WithMaterialChannels())));
        }

        // A constraint's enabled target names the node the constraint moves
        // and the index of its VRVLOG_avatar_constraints entry. Two
        // constraints on one node are two targets; the same entry twice is
        // rejected like any repeated target, even from another node.
        [Test]
        public void AConstraintEnabledTargetNamesItsEntry()
        {
            Dictionary<string, object> Constraint(long node, long index) =>
                new Dictionary<string, object> { ["node"] = node, ["property"] = "enabled", ["component"] = "constraint", ["index"] = index, ["rest"] = 1L };
            var json = WithUnknown(Constraint(3, 2));
            var text = JsonDom.Serialize(json);
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            var target = read.Targets[3];
            Assert.That((target.Known, target.Node, target.Component, target.Index, target.Rest), Is.EqualTo((true, 3, "constraint", 2, 1d)));
            Assert.That(target.Fields, Is.Null);
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));

            // A known target made with Fields, as the capture makes it, is written as them.
            var data = AvatarAnimatorData.Read(JsonDom.Parse(text));
            data.Targets[3] = new AvatarAnimatorData.Target { Node = -1, Property = "enabled", Component = "constraint", Rest = 1, Fields = Constraint(3, 2) };
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(data)), Is.EqualTo(text));

            var second = WithUnknown(Constraint(3, 2));
            ((List<object>)second["targets"]).Add(Constraint(3, 5));
            Assert.That(AvatarAnimatorData.Read(second).Targets[4].Index, Is.EqualTo(5));
            ((List<object>)second["targets"]).Add(Constraint(3, 5));
            Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(second));
            var otherNode = WithUnknown(Constraint(3, 2));
            ((List<object>)otherNode["targets"]).Add(Constraint(2, 2));
            Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(otherNode));
            // A renderer and a constraint on one node are two targets.
            var both = WithUnknown(Constraint(3, 0));
            ((List<object>)both["targets"]).Add(new Dictionary<string, object> { ["node"] = 3L, ["property"] = "enabled", ["component"] = "renderer", ["rest"] = 1L });
            Assert.That(AvatarAnimatorData.Read(both).Targets.Count, Is.EqualTo(5));

            void Rejects(Action<Dictionary<string, object>> change)
            {
                var broken = Constraint(3, 2);
                change(broken);
                Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(WithUnknown(broken))).Message, Is.EqualTo(Invalid));
            }
            Rejects(t => t.Remove("index"));
            Rejects(t => t["index"] = -1L);
            Rejects(t => t["index"] = 1.5);
            Rejects(t => t.Remove("node"));
            Rejects(t => t["rest"] = .5);
            Rejects(t => t["material"] = 0L);
            var renderer = new Dictionary<string, object> { ["node"] = 3L, ["property"] = "enabled", ["component"] = "renderer", ["index"] = 0L, ["rest"] = 1L };
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(WithUnknown(renderer))).Message, Is.EqualTo(Invalid));
        }

        // An enabled target of a component the reader does not know is read
        // as an unknown target and written back as read.
        [Test]
        public void AnEnabledTargetOfAnUnknownComponentIsReadAsUnknown()
        {
            var json = WithUnknown(new Dictionary<string, object> { ["node"] = 3L, ["property"] = "enabled", ["component"] = "light", ["index"] = 0L, ["rest"] = 1L });
            var text = JsonDom.Serialize(json);
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That((read.Targets[3].Known, read.Targets[3].Property, read.Targets[3].Rest), Is.EqualTo((false, "enabled", 1d)));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            // With no component at all it is a broken renderer target.
            Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(WithUnknown(new Dictionary<string, object> { ["node"] = 3L, ["property"] = "enabled", ["rest"] = 1L })));
        }

        // An evaluator gives an unknown target values like any other: rest
        // before a layer runs, then what the clip holds.
        [Test]
        public void AnEvaluatorMovesAnUnknownTarget()
        {
            var data = new AvatarAnimatorData();
            var controller = new AvatarAnimatorData.Controller();
            var layer = new AvatarAnimatorData.Layer { Name = "Base", Weight = 1, Blending = "override" };
            layer.Machines.Add(new AvatarAnimatorData.Machine { Name = "Base", DefaultState = 0 });
            layer.States.Add(new AvatarAnimatorData.State { Name = "Hold", Machine = 0, Motion = 0 });
            controller.Layers.Add(layer);
            data.Controllers.Add(controller);
            data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = 0 });
            var clip = new AvatarAnimatorData.Clip { Name = "Hold", Duration = 1, Loop = true };
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = .75, InTangent = 0, OutTangent = 0 });
            var track = new AvatarAnimatorData.Track { Target = 0 };
            track.Curves.Add(curve);
            clip.Tracks.Add(track);
            data.Clips.Add(clip);
            data.Targets.Add(new AvatarAnimatorData.Target { Property = "materialVector", Node = -1, Rest = .5 });
            var read = AvatarAnimatorData.Read(Json(data));
            Assert.That(AvatarAnimatorEvaluator.Supports(read, out var reason), Is.True, reason);
            var evaluator = new AvatarAnimatorEvaluator(read, () => 0);
            Assert.That(evaluator.Values[0], Is.EqualTo(.5));
            evaluator.Advance(0);
            Assert.That(evaluator.Values[0], Is.EqualTo(.75).Within(1e-9));
            Assert.That(evaluator.Written[0], Is.True);
        }

        // A materialSlot target follows the sample's active and parameter
        // targets, with a track of clip 0 that swaps the slot and empties it.
        const int SlotTarget = 3;

        static AvatarAnimatorData WithMaterialSlot()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "materialSlot", Slot = 1, Rest = 2 });
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = 0 });
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 1d / 60, Value = -1 });
            var track = new AvatarAnimatorData.Track { Target = SlotTarget };
            track.Curves.Add(curve);
            data.Clips[0].Tracks.Add(track);
            return data;
        }

        static void RejectsMaterialSlot(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithMaterialSlot());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void AMaterialSlotTargetRoundTrips()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithMaterialSlot()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            var slot = read.Targets[SlotTarget];
            Assert.That((slot.Known, slot.Node, slot.Property, slot.Slot, slot.Rest, slot.Stepped), Is.EqualTo((true, 3, "materialSlot", 1, 2d, true)));
            Assert.That(read.Targets.Take(SlotTarget).Any(t => t.Stepped), Is.False);
            var json = Json(WithMaterialSlot());
            Assert.That(At(json, "targets", SlotTarget).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "node", "property", "rest", "slot" }));
            Assert.That(At(json, "targets", SlotTarget)["rest"], Is.EqualTo(2L));
            // An empty slot rests at -1; another slot of the node, and the
            // same slot of another node, are their own targets.
            var data = WithMaterialSlot();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "materialSlot", Slot = 0, Rest = -1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "materialSlot", Slot = 1, Rest = 0 });
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(data)));
        }

        [Test]
        public void ReadRejectsABrokenMaterialSlotTarget()
        {
            // A materialSlot target names a node and a slot, and rests on a
            // material index or -1.
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget).Remove("node"));
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["node"] = -1L);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget).Remove("slot"));
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["slot"] = -1L);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["slot"] = .5);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["rest"] = -2L);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["rest"] = 1.5);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["rest"] = "1");
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["material"] = 0L);
            RejectsMaterialSlot(json => At(json, "targets", SlotTarget)["extra"] = 1L);
            RejectsMaterialSlot(json => ((List<object>)json["targets"]).Add(At(json, "targets", SlotTarget)));
            // Its keys are material indices or -1 too.
            static List<object> Key(Dictionary<string, object> json, int key) =>
                (List<object>)((List<object>)At(json, "clips", 0, "tracks", 2, "curves", 0)["keys"])[key];
            RejectsMaterialSlot(json => Key(json, 0)[1] = .5);
            RejectsMaterialSlot(json => Key(json, 1)[1] = -2L);
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(WithMaterialSlot())));
        }

        // A clip for each of values, holding it on target 0 (a materialSlot)
        // and target 1 (a float).
        static AvatarAnimatorData Slots(params double[] values)
        {
            var data = new AvatarAnimatorData();
            data.Parameters.Add(new AvatarAnimatorData.Parameter { Name = "Blend", Type = "float", Default = 0, Origin = "animator" });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 0, Property = "materialSlot", Slot = 0, Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 0, Rest = 0 });
            foreach (var value in values)
            {
                var clip = new AvatarAnimatorData.Clip { Name = "Slot " + value, Duration = 1, Loop = true };
                for (var t = 0; t < 2; t++)
                {
                    var curve = new ExpressionAnimationData.Curve();
                    curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = value });
                    var track = new AvatarAnimatorData.Track { Target = t };
                    track.Curves.Add(curve);
                    clip.Tracks.Add(track);
                }
                data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = data.Clips.Count });
                data.Clips.Add(clip);
            }
            return data;
        }

        static AvatarAnimatorData.Layer Hold(string name, int motion)
        {
            var layer = new AvatarAnimatorData.Layer { Name = name, Weight = 1, Blending = "override" };
            layer.Machines.Add(new AvatarAnimatorData.Machine { Name = name, DefaultState = 0 });
            layer.States.Add(new AvatarAnimatorData.State { Name = name, Machine = 0, Motion = motion });
            return layer;
        }

        // A blend tree does not mix material indices: the slot takes the
        // child of the greatest weight, while a float mixes.
        [Test]
        public void AnEvaluatorDoesNotBlendAMaterialSlotInATree()
        {
            var data = Slots(4, 7);
            var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = "1d" };
            tree.Parameters.Add(0);
            tree.Children.Add(new AvatarAnimatorData.Child { Motion = 0, Threshold = 0 });
            tree.Children.Add(new AvatarAnimatorData.Child { Motion = 1, Threshold = 1 });
            data.Motions.Add(tree);
            var controller = new AvatarAnimatorData.Controller();
            controller.Layers.Add(Hold("Base", 2));
            data.Controllers.Add(controller);
            var read = AvatarAnimatorData.Read(Json(data));
            Assert.That(AvatarAnimatorEvaluator.Supports(read, out var reason), Is.True, reason);
            var evaluator = new AvatarAnimatorEvaluator(read, () => 0);
            foreach (var (blend, slot, mixed) in new[] { (.25, 4d, 4.75), (.75, 7d, 6.25), (0d, 4d, 4d), (1d, 7d, 7d) })
            {
                evaluator.Set(0, blend);
                evaluator.Advance(0);
                Assert.That(evaluator.Values[0], Is.EqualTo(slot), $"blend {blend}");
                Assert.That(evaluator.Values[1], Is.EqualTo(mixed).Within(1e-9), $"blend {blend}");
            }
        }

        // An override layer over another takes the slot from the upper one
        // only past half its weight.
        [Test]
        public void AnEvaluatorDoesNotBlendAMaterialSlotAcrossLayers()
        {
            var data = Slots(4, 7);
            var controller = new AvatarAnimatorData.Controller();
            controller.Layers.Add(Hold("Base", 0));
            controller.Layers.Add(Hold("Upper", 1));
            data.Controllers.Add(controller);
            var read = AvatarAnimatorData.Read(Json(data));
            Assert.That(AvatarAnimatorEvaluator.Supports(read, out var reason), Is.True, reason);
            var evaluator = new AvatarAnimatorEvaluator(read, () => 0);
            foreach (var (weight, slot, mixed) in new[] { (.25, 4d, 4.75), (.75, 7d, 6.25), (1d, 7d, 7d), (0d, 4d, 4d) })
            {
                evaluator.SetLayerWeight(1, weight);
                evaluator.Advance(0);
                Assert.That(evaluator.Values[0], Is.EqualTo(slot), $"weight {weight}");
                Assert.That(evaluator.Values[1], Is.EqualTo(mixed).Within(1e-9), $"weight {weight}");
            }
        }
        // Translation, quaternion rotation, Euler rotation and scale targets
        // follow the sample's active and parameter targets on nodes 3 and 4,
        // with a track of clip 0 on the first.
        const int TransformTarget = 3;

        static AvatarAnimatorData WithTransforms()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "translation", Channel = "y", Rest = .25 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "rotation", Channel = "w", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "rotation", Channel = "z", Euler = true, Rest = 30 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "scale", Channel = "x", Rest = 1.5 });
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = .5 });
            var track = new AvatarAnimatorData.Track { Target = TransformTarget };
            track.Curves.Add(curve);
            data.Clips[0].Tracks.Add(track);
            return data;
        }

        static void RejectsTransform(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithTransforms());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void TransformTargetsRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithTransforms()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Targets.Skip(TransformTarget).Select(t => (t.Known, t.Node, t.Property, t.Channel, t.Euler, t.Rest, t.Stepped)), Is.EqualTo(new[]
            {
                (true, 3, "translation", "y", false, .25, false), (true, 3, "rotation", "w", false, 1d, false),
                (true, 4, "rotation", "z", true, 30d, false), (true, 4, "scale", "x", false, 1.5, false),
            }));
            var json = Json(WithTransforms());
            Assert.That(At(json, "targets", TransformTarget).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "channel", "node", "property", "rest" }));
            Assert.That(At(json, "targets", TransformTarget + 2).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "channel", "euler", "node", "property", "rest" }));
            Assert.That(At(json, "targets", TransformTarget + 2)["euler"], Is.EqualTo(true));
            // The evaluator gives a Transform channel its value like any float.
            var controller = new AvatarAnimatorData.Controller();
            controller.Layers.Add(Hold("Base", 0));
            read.Controllers.Clear();
            read.Omitted.Clear();
            read.Controllers.Add(controller);
            Assert.That(AvatarAnimatorEvaluator.Supports(read, out var reason), Is.True, reason);
            var evaluator = new AvatarAnimatorEvaluator(read, () => 0);
            evaluator.Advance(0);
            Assert.That(evaluator.Values[TransformTarget], Is.EqualTo(.5).Within(1e-9));
            Assert.That(evaluator.Values[TransformTarget + 2], Is.EqualTo(30));
        }

        [Test]
        public void ReadRejectsABrokenTransformTarget()
        {
            // A Transform target names a node and one channel of its kind.
            RejectsTransform(json => At(json, "targets", TransformTarget).Remove("node"));
            RejectsTransform(json => At(json, "targets", TransformTarget)["node"] = -1L);
            RejectsTransform(json => At(json, "targets", TransformTarget).Remove("channel"));
            RejectsTransform(json => At(json, "targets", TransformTarget)["channel"] = "w");
            RejectsTransform(json => At(json, "targets", TransformTarget)["channel"] = "r");
            RejectsTransform(json => At(json, "targets", TransformTarget)["rest"] = "1");
            RejectsTransform(json => At(json, "targets", TransformTarget)["slot"] = 0L);
            RejectsTransform(json => At(json, "targets", TransformTarget + 3)["channel"] = "w");
            // Only a rotation is Euler, with euler true, and then on x, y or z.
            RejectsTransform(json => At(json, "targets", TransformTarget)["euler"] = true);
            RejectsTransform(json => At(json, "targets", TransformTarget + 2)["euler"] = false);
            RejectsTransform(json => At(json, "targets", TransformTarget + 2)["euler"] = 1L);
            RejectsTransform(json => At(json, "targets", TransformTarget + 2)["channel"] = "w");
            // One target per node, kind and channel.
            RejectsTransform(json => ((List<object>)json["targets"]).Add(At(json, "targets", TransformTarget)));
            // The rotation of a node is all Euler or all quaternion.
            RejectsTransform(json => ((List<object>)json["targets"]).Add(new Dictionary<string, object>
            {
                ["node"] = 3L, ["property"] = "rotation", ["channel"] = "x", ["euler"] = true, ["rest"] = 0L
            }));
            // Another channel, and the same channel of another node or kind, are their own targets.
            var data = WithTransforms();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "translation", Channel = "x", Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "translation", Channel = "y", Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Property = "scale", Channel = "y", Rest = 1 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 4, Property = "rotation", Channel = "x", Euler = true, Rest = 0 });
            Assert.DoesNotThrow(() => AvatarAnimatorData.Read(Json(data)));
        }

        // Two muscle targets after the sample's active and parameter targets,
        // the first with a track of clip 0.
        const int MuscleTarget = 3;

        static AvatarAnimatorData WithMuscles()
        {
            var data = Sample();
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "muscle", Muscle = 0, Rest = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = -1, Property = "muscle", Muscle = AvatarAnimatorData.MuscleCount - 1, Rest = 0 });
            var curve = new ExpressionAnimationData.Curve();
            curve.Keys.Add(new ExpressionAnimationData.Key { Time = 0, Value = -.5 });
            var track = new AvatarAnimatorData.Track { Target = MuscleTarget };
            track.Curves.Add(curve);
            data.Clips[0].Tracks.Add(track);
            return data;
        }

        static void RejectsMuscle(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithMuscles());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void MuscleTargetsRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithMuscles()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Targets.Skip(MuscleTarget).Select(t => (t.Known, t.Node, t.Property, t.Muscle, t.Rest)), Is.EqualTo(new[]
            {
                (true, -1, "muscle", 0, 0d), (true, -1, "muscle", 94, 0d),
            }));
            var json = Json(WithMuscles());
            Assert.That(At(json, "targets", MuscleTarget).Keys.OrderBy(key => key, StringComparer.Ordinal), Is.EqualTo(new[] { "muscle", "property", "rest" }));
            // The evaluator gives a muscle its value like any float. An FX
            // moves muscles only when its base layer has a mask (M16), here
            // one with the body part of muscle 0 alone.
            var controller = new AvatarAnimatorData.Controller();
            var layer = Hold("Base", 0);
            layer.Mask = new AvatarAnimatorData.Mask();
            layer.Mask.Humanoid.Add(1);
            controller.Layers.Add(layer);
            read.Controllers.Clear();
            read.Omitted.Clear();
            read.Controllers.Add(controller);
            Assert.That(AvatarAnimatorEvaluator.Supports(read, out var reason), Is.True, reason);
            var evaluator = new AvatarAnimatorEvaluator(read, () => 0);
            evaluator.Advance(0);
            Assert.That(evaluator.Values[MuscleTarget], Is.EqualTo(-.5).Within(1e-9));
            Assert.That(evaluator.Values[MuscleTarget + 1], Is.EqualTo(0));
        }

        [Test]
        public void ReadRejectsABrokenMuscleTarget()
        {
            // A muscle target names one of HumanTrait.MuscleName and no node.
            RejectsMuscle(json => At(json, "targets", MuscleTarget)["muscle"] = 95L);
            RejectsMuscle(json => At(json, "targets", MuscleTarget)["muscle"] = -1L);
            RejectsMuscle(json => At(json, "targets", MuscleTarget)["muscle"] = 1.5);
            RejectsMuscle(json => At(json, "targets", MuscleTarget).Remove("muscle"));
            RejectsMuscle(json => At(json, "targets", MuscleTarget).Remove("rest"));
            RejectsMuscle(json => At(json, "targets", MuscleTarget)["rest"] = "0");
            RejectsMuscle(json => At(json, "targets", MuscleTarget)["node"] = 3L);
            // One target per muscle.
            RejectsMuscle(json => ((List<object>)json["targets"]).Add(At(json, "targets", MuscleTarget)));
        }

        // The sample's FX, then a Gesture whose layer has a mask, then an Action.
        static AvatarAnimatorData WithPlayables()
        {
            var data = Sample();
            var gesture = new AvatarAnimatorData.Controller { Playable = "gesture" };
            var hands = Hold("Hands", 0);
            hands.Mask = new AvatarAnimatorData.Mask { Nodes = new List<int> { 1, 3 } };
            hands.Mask.Humanoid.AddRange(new[] { 7, 8 });
            gesture.Layers.Add(hands);
            data.Controllers.Add(gesture);
            var action = new AvatarAnimatorData.Controller { Playable = "action" };
            var body = Hold("Body", 1);
            body.Mask = new AvatarAnimatorData.Mask();
            body.Mask.Humanoid.AddRange(new[] { 0, 1, 2 });
            action.Layers.Add(body);
            data.Controllers.Add(action);
            return data;
        }

        [Test]
        public void GestureAndActionControllersWithMasksRoundTrip()
        {
            var text = JsonDom.Serialize(AvatarAnimatorData.Write(WithPlayables()));
            var read = AvatarAnimatorData.Read(JsonDom.Parse(text));
            Assert.That(JsonDom.Serialize(AvatarAnimatorData.Write(read)), Is.EqualTo(text));
            Assert.That(read.Controllers.Select(c => c.Playable), Is.EqualTo(new[] { "fx", "gesture", "action" }));
            Assert.That(read.Controllers[1].Layers[0].Mask.Humanoid, Is.EqualTo(new[] { 7, 8 }));
            Assert.That(read.Controllers[1].Layers[0].Mask.Nodes, Is.EqualTo(new[] { 1, 3 }));
            Assert.That(read.Controllers[2].Layers[0].Mask.Humanoid, Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(read.Controllers[2].Layers[0].Mask.Nodes, Is.Null, "A mask without transforms lets every node move.");
            Assert.That(read.Controllers[0].Layers[0].Mask, Is.Null);
            var json = Json(WithPlayables());
            Assert.That(At(json, "controllers", 0, "layers", 0).ContainsKey("mask"), Is.False, "A layer without a mask has no key.");
            Assert.That(At(json, "controllers", 2, "layers", 0, "mask").Keys, Is.EqualTo(new[] { "humanoid" }));
            // A mask that lets nothing move is a mask too.
            var none = WithPlayables();
            none.Controllers[2].Layers[0].Mask.Humanoid.Clear();
            Assert.That(AvatarAnimatorData.Read(Json(none)).Controllers[2].Layers[0].Mask.Humanoid, Is.Empty);
        }

        static void RejectsPlayables(Action<Dictionary<string, object>> change)
        {
            var json = Json(WithPlayables());
            change(json);
            Assert.That(Assert.Throws<InvalidOperationException>(() => AvatarAnimatorData.Read(json)).Message, Is.EqualTo(Invalid));
        }

        [Test]
        public void ReadRejectsControllersOutOfOrderAndBrokenMasks()
        {
            // The FX first, then the Gesture and the Action, each once.
            RejectsPlayables(json => At(json, "controllers", 1)["playable"] = "fx");
            RejectsPlayables(json => At(json, "controllers", 2)["playable"] = "gesture");
            RejectsPlayables(json => At(json, "controllers", 1)["playable"] = "action");
            RejectsPlayables(json => At(json, "controllers", 1)["playable"] = "additive");
            RejectsPlayables(json => ((List<object>)json["controllers"]).RemoveAt(0));
            RejectsPlayables(json => ((List<object>)json["controllers"]).Add(At(json, "controllers", 2)));
            // A mask names body parts 0 to 12 and nodes, each list in increasing order.
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["humanoid"]).Add(13L));
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["humanoid"]).Add(7L));
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["humanoid"]).Insert(0, 8L));
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["humanoid"]).Add(-1L));
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["nodes"]).Add(2L));
            RejectsPlayables(json => ((List<object>)At(json, "controllers", 1, "layers", 0, "mask")["nodes"]).Add(-1L));
            RejectsPlayables(json => At(json, "controllers", 1, "layers", 0, "mask").Remove("humanoid"));
            RejectsPlayables(json => At(json, "controllers", 1, "layers", 0, "mask")["transforms"] = new List<object>());
            RejectsPlayables(json => At(json, "controllers", 1, "layers", 0)["mask"] = new List<object>());
            RejectsPlayables(json => At(json, "controllers", 1, "layers", 0, "mask")["nodes"] = null);
        }
    }
}
