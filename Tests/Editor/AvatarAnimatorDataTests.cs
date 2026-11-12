using System;
using System.Collections.Generic;
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
            var root = new AvatarAnimatorData.Menu();
            root.Controls.Add(new AvatarAnimatorData.Control { Name = "Hat", Type = "toggle", Parameter = 0, Value = 1 });
            var more = new AvatarAnimatorData.Control { Name = "More", Type = "subMenu", Menu = 1 };
            root.Controls.Add(more);
            var sub = new AvatarAnimatorData.Menu();
            var radial = new AvatarAnimatorData.Control { Name = "", Type = "radial" };
            radial.SubParameters.Add(1); radial.SubParameters.Add(-1); radial.Labels.Add("Size");
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
            synced.Machines.Add(new AvatarAnimatorData.Machine { Name = "Hat" });
            controller.Layers.Add(synced);
            data.Controllers.Add(controller);

            data.Motions.Add(new AvatarAnimatorData.Motion { Type = "clip", Clip = 0 });
            var tree = new AvatarAnimatorData.Motion { Type = "blendTree", BlendType = "2dFreeformCartesian" };
            tree.Parameters.Add(1); tree.Parameters.Add(2);
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
            data.Clips.Add(clip);
            data.Clips.Add(new AvatarAnimatorData.Clip { Name = "Empty", Duration = 0 });
            data.Targets.Add(new AvatarAnimatorData.Target { Node = 3, Rest = 0 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "blendShape", Count = 2 });
            data.Omitted.Add(new AvatarAnimatorData.Omission { Kind = "behaviour", Count = 1 });
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
            Assert.That(read.Menus[1].Controls[0].SubParameters, Is.EqualTo(new[] { 1, -1 }));
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
            Assert.That((layer.SyncedLayer, layer.SyncedLayerAffectsTiming), Is.EqualTo((-1, false)));
            Assert.That((read.Controllers[0].Layers[1].SyncedLayer, read.Controllers[0].Layers[1].SyncedLayerAffectsTiming), Is.EqualTo((0, true)));
            Assert.That((read.Clips[0].CycleOffset, read.Clips[1].CycleOffset), Is.EqualTo((.25, 0d)));

            var json = Json(Sample());
            Assert.That(json["expressions"], Is.Empty);
            Assert.That(At(json, "parameters", 1).ContainsKey("saved"), Is.False, "Only expression parameters carry saved, synced and expressionType.");
            Assert.That(At(json, "controllers", 0, "layers", 0, "machines", 0, "entryTransitions", 0).ContainsKey("hasExitTime"), Is.False,
                "Entry and exit transitions have no timing.");
            Assert.That(At(json, "motions", 1, "children", 0).ContainsKey("directParameter"), Is.False);
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
            Rejects(json => At(json, "controllers", 0, "layers", 0, "states", 0, "transitions", 0, "conditions", 0)["parameter"] = 3L);
            Rejects(json => At(json, "menus", 0, "controls", 1)["menu"] = 2L);
            Rejects(json => At(json, "menus", 0, "controls", 0)["parameter"] = -1L);
            Rejects(json => At(json, "motions", 0)["clip"] = 2L);
            Rejects(json => At(json, "clips", 0, "tracks", 0)["target"] = 1L);
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
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = double.NaN);
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = double.PositiveInfinity);
            Rejects(json => At(json, "clips", 0)["cycleOffset"] = "0.25");
        }

        // Adds a chain of blend trees on top of the first clip. Outer first puts
        // the outermost tree at the lowest index, so that the check walks down
        // the whole chain from one root.
        static void Nest(Dictionary<string, object> json, int trees, bool outerFirst)
        {
            var motions = (List<object>)json["motions"];
            var first = motions.Count;
            for (var index = 0; index < trees; index++)
            {
                var inner = outerFirst ? (index == trees - 1 ? 0 : first + index + 1) : (index == 0 ? 0 : first + index - 1);
                motions.Add(new Dictionary<string, object>
                {
                    ["type"] = "blendTree", ["blendType"] = "direct", ["parameters"] = new List<object>(), ["normalizedBlendValues"] = false,
                    ["children"] = new List<object>
                    {
                        new Dictionary<string, object>
                        {
                            ["motion"] = (long)inner, ["threshold"] = 0.0, ["position"] = new List<object> { 0.0, 0.0 },
                            ["timeScale"] = 1.0, ["cycleOffset"] = 0.0, ["mirror"] = false, ["directParameter"] = 1L
                        }
                    }
                });
            }
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
    }
}
