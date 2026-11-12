using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;
using VRVlog.Expressions;

// Reads through ContractJson and the key lists of ExpressionAnimationData.cs,
// so a copy of this file needs that file too. No Unity/Editor dependency.
namespace VRVlog.AvatarAnimator
{
    // VRVLOG_avatar_animator: the avatar's VRChat FX controller, Expression
    // Menu and Expression Parameters as the build left them, for the app to
    // play. Sub-state machines are flat tables per layer; every reference is
    // an index into a table of the same extension. Curves are written only
    // for what the app can play (activeSelf); the rest are counted by kind.
    public sealed class AvatarAnimatorData
    {
        public const string Extension = "VRVLOG_avatar_animator";
        public const int SchemaVersion = 1;
        // The keys of all the curves of one clip, the limit the shared key reader counts to.
        public const int MaximumKeysPerClip = 16384;

        public static readonly string[] ParameterTypes = { "bool", "int", "float", "trigger" };
        public static readonly string[] ParameterOrigins = { "expression", "animator", "builtin" };
        public static readonly string[] ExpressionTypes = { "bool", "int", "float" };
        public static readonly string[] ControlTypes = { "toggle", "button", "radial", "twoAxis", "fourAxis", "subMenu" };
        public static readonly string[] Blendings = { "override", "additive" };
        public static readonly string[] InterruptionSources = { "none", "source", "destination", "sourceThenDestination", "destinationThenSource" };
        public static readonly string[] ConditionModes = { "if", "ifNot", "greater", "less", "equals", "notEqual" };
        public static readonly string[] OperationTypes = { "set", "add", "random", "copy" };
        public static readonly string[] TrackingValues = { "noChange", "tracking", "animation" };
        public static readonly string[] LayerPlayables = { "action", "fx", "gesture", "additive" };
        public static readonly string[] PlayableLayerPlayables = { "action", "fx", "gesture", "additive", "sitting" };
        public static readonly string[] BlendTypes = { "1d", "2dSimpleDirectional", "2dFreeformDirectional", "2dFreeformCartesian", "direct" };
        public static readonly string[] Properties = { "active" };
        // The order the omitted kinds are written in.
        public static readonly string[] OmittedKinds = { "blendShape", "material", "transform", "enabled", "parameter", "muscle", "other", "missingNode", "behaviour" };

        public readonly List<Parameter> Parameters = new List<Parameter>();
        public readonly List<Menu> Menus = new List<Menu>();
        public readonly List<Controller> Controllers = new List<Controller>();
        public readonly List<Motion> Motions = new List<Motion>();
        public readonly List<Clip> Clips = new List<Clip>();
        public readonly List<Target> Targets = new List<Target>();
        public readonly List<Omission> Omitted = new List<Omission>();

        // Saved, Synced and ExpressionType belong to origin "expression" only.
        public sealed class Parameter
        {
            public string Name, Type, Origin, ExpressionType;
            public double Default;
            public bool Saved, Synced;
        }

        // The first menu is the one that opens first.
        public sealed class Menu { public readonly List<Control> Controls = new List<Control>(); }

        // SubParameters hold -1 for a name that is not a parameter.
        public sealed class Control
        {
            public string Name, Type;
            public int? Parameter, Menu;
            public double Value;
            public readonly List<int> SubParameters = new List<int>();
            public readonly List<string> Labels = new List<string>();
        }

        public sealed class Controller { public string Playable = "fx"; public readonly List<Layer> Layers = new List<Layer>(); }

        // Machines[0] is the layer's root; States lists the states of every machine.
        public sealed class Layer
        {
            public string Name, Blending = "override";
            public double Weight;
            public readonly List<Machine> Machines = new List<Machine>();
            public readonly List<State> States = new List<State>();
        }

        // Entry and exit transitions are untimed; Any State transitions are timed.
        // ExitTransitions are the parent's transitions out of this machine.
        public sealed class Machine
        {
            public string Name;
            public int Parent = -1, DefaultState = -1;
            public readonly List<Transition> EntryTransitions = new List<Transition>();
            public readonly List<Transition> AnyStateTransitions = new List<Transition>();
            public readonly List<Transition> ExitTransitions = new List<Transition>();
            public readonly List<Behaviour> Behaviours = new List<Behaviour>();
        }

        // A parameter index is set only where the state uses that parameter.
        public sealed class State
        {
            public string Name;
            public int Machine, Motion = -1;
            public bool WriteDefaults, Mirror;
            public double Speed = 1, CycleOffset;
            public int? SpeedParameter, CycleOffsetParameter, MirrorParameter, TimeParameter;
            public readonly List<Transition> Transitions = new List<Transition>();
            public readonly List<Behaviour> Behaviours = new List<Behaviour>();
        }

        // Exactly one of ToState, ToMachine and Exit. The timing fields belong
        // to state and Any State transitions only (Timed).
        public sealed class Transition
        {
            public int? ToState, ToMachine;
            public bool Exit, Mute, Solo, Timed;
            public bool HasExitTime, HasFixedDuration, OrderedInterruption, CanTransitionToSelf;
            public double ExitTime, Duration, Offset;
            public string InterruptionSource = "none";
            public readonly List<Condition> Conditions = new List<Condition>();
        }

        public sealed class Condition { public int Parameter; public string Mode; public double Threshold; }

        // Type picks the fields: parameterDriver (LocalOnly, Operations),
        // trackingControl (Tracking: part name to value), layerControl
        // (Playable, Layer, GoalWeight, BlendDuration) or playableLayerControl
        // (Playable, GoalWeight, BlendDuration).
        public sealed class Behaviour
        {
            public string Type, Playable;
            public bool LocalOnly;
            public int Layer;
            public double GoalWeight, BlendDuration;
            public readonly List<Operation> Operations = new List<Operation>();
            public readonly List<KeyValuePair<string, string>> Tracking = new List<KeyValuePair<string, string>>();
        }

        // Source is set only when the operation names one.
        public sealed class Operation
        {
            public string Type;
            public int Parameter;
            public int? Source;
            public bool ConvertRange;
            public double Value, ValueMin, ValueMax, Chance, SourceMin, SourceMax, DestMin, DestMax;
        }

        // Type "clip" uses Clip; "blendTree" the rest. Parameters has one entry
        // for 1d, two for 2d and none for direct, whose children name theirs.
        public sealed class Motion
        {
            public string Type, BlendType;
            public int Clip;
            public bool NormalizedBlendValues;
            public readonly List<int> Parameters = new List<int>();
            public readonly List<Child> Children = new List<Child>();
        }

        public sealed class Child
        {
            public int Motion = -1;
            public int? DirectParameter;
            public bool Mirror;
            public double Threshold, X, Y, TimeScale = 1, CycleOffset;
        }

        public sealed class Clip
        {
            public string Name;
            public double Duration;
            public bool Loop;
            public readonly List<Track> Tracks = new List<Track>();
        }

        public sealed class Track { public int Target; public readonly List<ExpressionAnimationData.Curve> Curves = new List<ExpressionAnimationData.Curve>(); }

        // Rest is the node's value before any layer runs (0 or 1 for active).
        public sealed class Target { public int Node; public string Property = "active"; public int Rest; }

        public sealed class Omission { public string Kind; public int Count; }

        public static Dictionary<string, object> Write(AvatarAnimatorData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["parameters"] = data.Parameters.Select(p => (object)WriteParameter(p)).ToList(),
                ["menus"] = data.Menus.Select(m => (object)new Dictionary<string, object> { ["controls"] = m.Controls.Select(c => (object)WriteControl(c)).ToList() }).ToList(),
                ["expressions"] = new List<object>(),
                ["controllers"] = data.Controllers.Select(c => (object)new Dictionary<string, object>
                {
                    ["playable"] = c.Playable, ["layers"] = c.Layers.Select(l => (object)WriteLayer(l)).ToList()
                }).ToList(),
                ["motions"] = data.Motions.Select(m => (object)WriteMotion(m)).ToList(),
                ["clips"] = data.Clips.Select(c => (object)new Dictionary<string, object>
                {
                    ["name"] = c.Name, ["duration"] = c.Duration, ["loop"] = c.Loop,
                    ["tracks"] = c.Tracks.Select(t => (object)new Dictionary<string, object>
                    {
                        ["target"] = (long)t.Target, ["curves"] = t.Curves.Select(curve => (object)ExpressionAnimationData.WriteCurve(curve)).ToList()
                    }).ToList()
                }).ToList(),
                ["targets"] = data.Targets.Select(t => (object)new Dictionary<string, object>
                {
                    ["node"] = (long)t.Node, ["property"] = t.Property, ["rest"] = (long)t.Rest
                }).ToList(),
                ["omitted"] = data.Omitted.Select(o => (object)new Dictionary<string, object> { ["kind"] = o.Kind, ["count"] = (long)o.Count }).ToList()
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        static Dictionary<string, object> WriteParameter(Parameter p)
        {
            var result = new Dictionary<string, object> { ["name"] = p.Name, ["type"] = p.Type, ["default"] = p.Default, ["origin"] = p.Origin };
            if (p.Origin == "expression")
            {
                result["saved"] = p.Saved; result["synced"] = p.Synced; result["expressionType"] = p.ExpressionType;
            }
            return result;
        }

        static Dictionary<string, object> WriteControl(Control c)
        {
            var result = new Dictionary<string, object> { ["name"] = c.Name, ["type"] = c.Type };
            if (c.Parameter.HasValue) result["parameter"] = (long)c.Parameter.Value;
            result["value"] = c.Value;
            if (c.Menu.HasValue) result["menu"] = (long)c.Menu.Value;
            result["subParameters"] = c.SubParameters.Select(i => (object)(long)i).ToList();
            result["labels"] = c.Labels.Select(l => (object)l).ToList();
            return result;
        }

        static Dictionary<string, object> WriteLayer(Layer l) => new Dictionary<string, object>
        {
            ["name"] = l.Name, ["weight"] = l.Weight, ["blending"] = l.Blending,
            ["machines"] = l.Machines.Select(m => (object)new Dictionary<string, object>
            {
                ["name"] = m.Name, ["parent"] = (long)m.Parent, ["defaultState"] = (long)m.DefaultState,
                ["entryTransitions"] = WriteTransitions(m.EntryTransitions),
                ["anyStateTransitions"] = WriteTransitions(m.AnyStateTransitions),
                ["exitTransitions"] = WriteTransitions(m.ExitTransitions),
                ["behaviours"] = WriteBehaviours(m.Behaviours)
            }).ToList(),
            ["states"] = l.States.Select(s =>
            {
                var state = new Dictionary<string, object>
                {
                    ["name"] = s.Name, ["machine"] = (long)s.Machine, ["motion"] = (long)s.Motion, ["writeDefaults"] = s.WriteDefaults,
                    ["speed"] = s.Speed, ["cycleOffset"] = s.CycleOffset, ["mirror"] = s.Mirror
                };
                if (s.SpeedParameter.HasValue) state["speedParameter"] = (long)s.SpeedParameter.Value;
                if (s.CycleOffsetParameter.HasValue) state["cycleOffsetParameter"] = (long)s.CycleOffsetParameter.Value;
                if (s.MirrorParameter.HasValue) state["mirrorParameter"] = (long)s.MirrorParameter.Value;
                if (s.TimeParameter.HasValue) state["timeParameter"] = (long)s.TimeParameter.Value;
                state["transitions"] = WriteTransitions(s.Transitions);
                state["behaviours"] = WriteBehaviours(s.Behaviours);
                return (object)state;
            }).ToList()
        };

        static List<object> WriteTransitions(List<Transition> transitions) => transitions.Select(t =>
        {
            var result = new Dictionary<string, object>();
            if (t.ToState.HasValue) result["toState"] = (long)t.ToState.Value;
            if (t.ToMachine.HasValue) result["toMachine"] = (long)t.ToMachine.Value;
            if (t.Exit) result["exit"] = true;
            result["mute"] = t.Mute; result["solo"] = t.Solo;
            if (t.Timed)
            {
                result["hasExitTime"] = t.HasExitTime; result["exitTime"] = t.ExitTime;
                result["hasFixedDuration"] = t.HasFixedDuration; result["duration"] = t.Duration; result["offset"] = t.Offset;
                result["interruptionSource"] = t.InterruptionSource; result["orderedInterruption"] = t.OrderedInterruption;
                result["canTransitionToSelf"] = t.CanTransitionToSelf;
            }
            result["conditions"] = t.Conditions.Select(c => (object)new Dictionary<string, object>
            {
                ["parameter"] = (long)c.Parameter, ["mode"] = c.Mode, ["threshold"] = c.Threshold
            }).ToList();
            return (object)result;
        }).ToList();

        static List<object> WriteBehaviours(List<Behaviour> behaviours) => behaviours.Select(b =>
        {
            var result = new Dictionary<string, object> { ["type"] = b.Type };
            switch (b.Type)
            {
                case "parameterDriver":
                    result["localOnly"] = b.LocalOnly;
                    result["operations"] = b.Operations.Select(o =>
                    {
                        var operation = new Dictionary<string, object>
                        {
                            ["type"] = o.Type, ["parameter"] = (long)o.Parameter, ["value"] = o.Value,
                            ["valueMin"] = o.ValueMin, ["valueMax"] = o.ValueMax, ["chance"] = o.Chance
                        };
                        if (o.Source.HasValue) operation["source"] = (long)o.Source.Value;
                        operation["convertRange"] = o.ConvertRange;
                        operation["sourceMin"] = o.SourceMin; operation["sourceMax"] = o.SourceMax;
                        operation["destMin"] = o.DestMin; operation["destMax"] = o.DestMax;
                        return (object)operation;
                    }).ToList();
                    break;
                case "trackingControl":
                    foreach (var part in b.Tracking) result[part.Key] = part.Value;
                    break;
                case "layerControl":
                    result["playable"] = b.Playable; result["layer"] = (long)b.Layer;
                    result["goalWeight"] = b.GoalWeight; result["blendDuration"] = b.BlendDuration;
                    break;
                default:
                    result["playable"] = b.Playable;
                    result["goalWeight"] = b.GoalWeight; result["blendDuration"] = b.BlendDuration;
                    break;
            }
            return (object)result;
        }).ToList();

        static Dictionary<string, object> WriteMotion(Motion m)
        {
            if (m.Type == "clip") return new Dictionary<string, object> { ["type"] = m.Type, ["clip"] = (long)m.Clip };
            return new Dictionary<string, object>
            {
                ["type"] = m.Type, ["blendType"] = m.BlendType, ["parameters"] = m.Parameters.Select(p => (object)(long)p).ToList(),
                ["normalizedBlendValues"] = m.NormalizedBlendValues,
                ["children"] = m.Children.Select(c =>
                {
                    var child = new Dictionary<string, object>
                    {
                        ["motion"] = (long)c.Motion, ["threshold"] = c.Threshold, ["position"] = new List<object> { c.X, c.Y },
                        ["timeScale"] = c.TimeScale, ["cycleOffset"] = c.CycleOffset, ["mirror"] = c.Mirror
                    };
                    if (c.DirectParameter.HasValue) child["directParameter"] = (long)c.DirectParameter.Value;
                    return (object)child;
                }).ToList()
            };
        }

        public static AvatarAnimatorData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "parameters", "menus", "expressions", "controllers", "motions", "clips", "targets", "omitted");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new AvatarAnimatorData();
            // Counts first: entries refer forwards as well as backwards.
            var parameters = Json.Arr(root["parameters"], 0, 8192);
            var menus = Json.Arr(root["menus"], 0, 1024);
            var motions = Json.Arr(root["motions"], 0, 65536);
            var clips = Json.Arr(root["clips"], 0, 16384);
            var targets = Json.Arr(root["targets"], 0, 65536);
            Json.Arr(root["expressions"], 0, 0);

            foreach (var raw in parameters)
            {
                var origin = Choice(Get(Json.Obj(raw), "origin"), ParameterOrigins);
                var p = origin == "expression"
                    ? Json.Obj(raw, "name", "type", "default", "origin", "saved", "synced", "expressionType")
                    : Json.Obj(raw, "name", "type", "default", "origin");
                var parameter = new Parameter { Name = Name(p["name"]), Type = Choice(p["type"], ParameterTypes), Default = Json.Number(p["default"]), Origin = origin };
                if (origin == "expression")
                {
                    parameter.Saved = Flag(p["saved"]); parameter.Synced = Flag(p["synced"]);
                    parameter.ExpressionType = Choice(p["expressionType"], ExpressionTypes);
                }
                data.Parameters.Add(parameter);
            }
            var parameterCount = data.Parameters.Count;

            foreach (var raw in menus)
            {
                var menu = new Menu();
                foreach (var rawControl in Json.Arr(Json.Obj(raw, "controls")["controls"], 0, 64))
                {
                    var c = Json.Obj(rawControl);
                    var keys = new List<string> { "name", "type", "value", "subParameters", "labels" };
                    if (c.ContainsKey("parameter")) keys.Add("parameter");
                    if (c.ContainsKey("menu")) keys.Add("menu");
                    Json.Obj(rawControl, keys.ToArray());
                    var control = new Control { Name = Name(c["name"]), Type = Choice(c["type"], ControlTypes), Value = Json.Number(c["value"]) };
                    if (c.ContainsKey("parameter")) control.Parameter = Index(c["parameter"], parameterCount);
                    if (c.ContainsKey("menu")) control.Menu = control.Type == "subMenu" ? Index(c["menu"], menus.Count) : throw Json.Bad();
                    foreach (var sub in Json.Arr(c["subParameters"], 0, 64)) control.SubParameters.Add(Index(sub, parameterCount, true));
                    foreach (var label in Json.Arr(c["labels"], 0, 64)) control.Labels.Add(Name(label));
                    menu.Controls.Add(control);
                }
                data.Menus.Add(menu);
            }

            foreach (var raw in Json.Arr(root["controllers"], 0, 1))
            {
                var c = Json.Obj(raw, "playable", "layers");
                var controller = new Controller { Playable = Choice(c["playable"], "fx") };
                foreach (var rawLayer in Json.Arr(c["layers"], 0, 1024))
                    controller.Layers.Add(ReadLayer(rawLayer, parameterCount, motions.Count));
                data.Controllers.Add(controller);
            }

            for (var index = 0; index < motions.Count; index++)
            {
                var m = Json.Obj(motions[index]);
                var motion = new Motion { Type = Choice(Get(m, "type"), "clip", "blendTree") };
                if (motion.Type == "clip")
                {
                    Json.Obj(m, "type", "clip");
                    motion.Clip = Index(m["clip"], clips.Count);
                }
                else
                {
                    Json.Obj(m, "type", "blendType", "parameters", "normalizedBlendValues", "children");
                    motion.BlendType = Choice(m["blendType"], BlendTypes);
                    var direct = motion.BlendType == "direct";
                    var axes = direct ? 0 : motion.BlendType == "1d" ? 1 : 2;
                    foreach (var p in Json.Arr(m["parameters"], axes, axes)) motion.Parameters.Add(Index(p, parameterCount));
                    motion.NormalizedBlendValues = Flag(m["normalizedBlendValues"]);
                    foreach (var rawChild in Json.Arr(m["children"], 0, 1024))
                    {
                        var ch = direct
                            ? Json.Obj(rawChild, "motion", "threshold", "position", "timeScale", "cycleOffset", "mirror", "directParameter")
                            : Json.Obj(rawChild, "motion", "threshold", "position", "timeScale", "cycleOffset", "mirror");
                        var position = Json.Arr(ch["position"], 2, 2);
                        var child = new Child
                        {
                            Motion = Index(ch["motion"], motions.Count, true), Threshold = Json.Number(ch["threshold"]),
                            X = Json.Number(position[0]), Y = Json.Number(position[1]),
                            TimeScale = Json.Number(ch["timeScale"]), CycleOffset = Json.Number(ch["cycleOffset"]), Mirror = Flag(ch["mirror"])
                        };
                        if (direct) child.DirectParameter = Index(ch["directParameter"], parameterCount);
                        motion.Children.Add(child);
                    }
                }
                data.Motions.Add(motion);
            }
            // A blend tree must not contain itself.
            var visit = new int[data.Motions.Count];
            void Acyclic(int index)
            {
                if (visit[index] == 2) return;
                if (visit[index] == 1) throw Json.Bad();
                visit[index] = 1;
                foreach (var child in data.Motions[index].Children) if (child.Motion >= 0) Acyclic(child.Motion);
                visit[index] = 2;
            }
            for (var index = 0; index < data.Motions.Count; index++) Acyclic(index);

            foreach (var raw in clips)
            {
                var c = Json.Obj(raw, "name", "duration", "loop", "tracks");
                var clip = new Clip { Name = Name(c["name"]), Duration = Json.Number(c["duration"]), Loop = Flag(c["loop"]) };
                if (clip.Duration < 0) throw Json.Bad();
                var keyCount = 0;
                var seen = new HashSet<int>();
                foreach (var rawTrack in Json.Arr(c["tracks"], 0, 4096))
                {
                    var t = Json.Obj(rawTrack, "target", "curves");
                    var track = new Track { Target = Index(t["target"], targets.Count) };
                    if (!seen.Add(track.Target)) throw Json.Bad();
                    // One curve per track: every property written so far has one value.
                    foreach (var rawCurve in Json.Arr(t["curves"], 1, 1))
                        track.Curves.Add(ReadCurve(rawCurve, ref keyCount));
                    clip.Tracks.Add(track);
                }
                data.Clips.Add(clip);
            }

            var nodes = new HashSet<(int, string)>();
            foreach (var raw in targets)
            {
                var t = Json.Obj(raw, "node", "property", "rest");
                var target = new Target { Node = Json.NonNegative(t["node"]), Property = Choice(t["property"], Properties), Rest = Json.Integer(t["rest"]) };
                if (target.Rest != 0 && target.Rest != 1 || !nodes.Add((target.Node, target.Property))) throw Json.Bad();
                data.Targets.Add(target);
            }

            var kind = -1;
            foreach (var raw in Json.Arr(root["omitted"], 0, OmittedKinds.Length))
            {
                var o = Json.Obj(raw, "kind", "count");
                var omission = new Omission { Kind = Choice(o["kind"], OmittedKinds), Count = Json.NonNegative(o["count"]) };
                var next = Array.IndexOf(OmittedKinds, omission.Kind);
                if (next <= kind || omission.Count == 0) throw Json.Bad();
                kind = next;
                data.Omitted.Add(omission);
            }
            return data;
        }

        static Layer ReadLayer(object raw, int parameterCount, int motionCount)
        {
            var l = Json.Obj(raw, "name", "weight", "blending", "machines", "states");
            var layer = new Layer { Name = Name(l["name"]), Weight = Json.Number(l["weight"]), Blending = Choice(l["blending"], Blendings) };
            var machines = Json.Arr(l["machines"], 1, 4096);
            var states = Json.Arr(l["states"], 0, 16384);
            Transition ReadOne(object rawTransition, bool timed)
            {
                var t = Json.Obj(rawTransition);
                var keys = new List<string> { "mute", "solo", "conditions" };
                if (timed) keys.AddRange(new[] { "hasExitTime", "exitTime", "hasFixedDuration", "duration", "offset", "interruptionSource", "orderedInterruption", "canTransitionToSelf" });
                var destinations = new[] { "toState", "toMachine", "exit" }.Where(t.ContainsKey).ToList();
                if (destinations.Count != 1) throw Json.Bad();
                keys.Add(destinations[0]);
                Json.Obj(t, keys.ToArray());
                var transition = new Transition { Mute = Flag(t["mute"]), Solo = Flag(t["solo"]), Timed = timed };
                if (t.ContainsKey("toState")) transition.ToState = Index(t["toState"], states.Count);
                if (t.ContainsKey("toMachine")) transition.ToMachine = Index(t["toMachine"], machines.Count);
                if (t.ContainsKey("exit")) transition.Exit = t["exit"] is bool exit && exit ? true : throw Json.Bad();
                if (timed)
                {
                    transition.HasExitTime = Flag(t["hasExitTime"]); transition.ExitTime = Json.Number(t["exitTime"]);
                    transition.HasFixedDuration = Flag(t["hasFixedDuration"]); transition.Duration = Json.Number(t["duration"]);
                    transition.Offset = Json.Number(t["offset"]); transition.InterruptionSource = Choice(t["interruptionSource"], InterruptionSources);
                    transition.OrderedInterruption = Flag(t["orderedInterruption"]); transition.CanTransitionToSelf = Flag(t["canTransitionToSelf"]);
                }
                foreach (var rawCondition in Json.Arr(t["conditions"], 0, 256))
                {
                    var c = Json.Obj(rawCondition, "parameter", "mode", "threshold");
                    transition.Conditions.Add(new Condition { Parameter = Index(c["parameter"], parameterCount), Mode = Choice(c["mode"], ConditionModes), Threshold = Json.Number(c["threshold"]) });
                }
                return transition;
            }
            void ReadAll(object rawList, List<Transition> into, bool timed)
            {
                foreach (var rawTransition in Json.Arr(rawList, 0, 1024)) into.Add(ReadOne(rawTransition, timed));
            }
            for (var index = 0; index < machines.Count; index++)
            {
                var m = Json.Obj(machines[index], "name", "parent", "defaultState", "entryTransitions", "anyStateTransitions", "exitTransitions", "behaviours");
                var machine = new Machine { Name = Name(m["name"]), Parent = Index(m["parent"], index, true), DefaultState = Index(m["defaultState"], states.Count, true) };
                // The root comes first; every other machine follows its parent.
                if ((index == 0) != (machine.Parent == -1)) throw Json.Bad();
                ReadAll(m["entryTransitions"], machine.EntryTransitions, false);
                ReadAll(m["anyStateTransitions"], machine.AnyStateTransitions, true);
                ReadAll(m["exitTransitions"], machine.ExitTransitions, false);
                if (index == 0 && machine.ExitTransitions.Count > 0) throw Json.Bad();
                ReadBehaviours(m["behaviours"], machine.Behaviours, parameterCount);
                layer.Machines.Add(machine);
            }
            foreach (var rawState in states)
            {
                var s = Json.Obj(rawState);
                var keys = new List<string> { "name", "machine", "motion", "writeDefaults", "speed", "cycleOffset", "mirror", "transitions", "behaviours" };
                var optional = new[] { "speedParameter", "cycleOffsetParameter", "mirrorParameter", "timeParameter" };
                keys.AddRange(optional.Where(s.ContainsKey));
                Json.Obj(s, keys.ToArray());
                var state = new State
                {
                    Name = Name(s["name"]), Machine = Index(s["machine"], machines.Count), Motion = Index(s["motion"], motionCount, true),
                    WriteDefaults = Flag(s["writeDefaults"]), Speed = Json.Number(s["speed"]), CycleOffset = Json.Number(s["cycleOffset"]), Mirror = Flag(s["mirror"])
                };
                int? Optional(string key) => s.ContainsKey(key) ? Index(s[key], parameterCount) : (int?)null;
                state.SpeedParameter = Optional("speedParameter"); state.CycleOffsetParameter = Optional("cycleOffsetParameter");
                state.MirrorParameter = Optional("mirrorParameter"); state.TimeParameter = Optional("timeParameter");
                ReadAll(s["transitions"], state.Transitions, true);
                ReadBehaviours(s["behaviours"], state.Behaviours, parameterCount);
                layer.States.Add(state);
            }
            return layer;
        }

        static void ReadBehaviours(object raw, List<Behaviour> into, int parameterCount)
        {
            foreach (var rawBehaviour in Json.Arr(raw, 0, 1024))
            {
                var b = Json.Obj(rawBehaviour);
                var behaviour = new Behaviour { Type = Choice(Get(b, "type"), "parameterDriver", "trackingControl", "layerControl", "playableLayerControl") };
                switch (behaviour.Type)
                {
                    case "parameterDriver":
                        Json.Obj(b, "type", "localOnly", "operations");
                        behaviour.LocalOnly = Flag(b["localOnly"]);
                        foreach (var rawOperation in Json.Arr(b["operations"], 0, 1024))
                        {
                            var o = Json.Obj(rawOperation);
                            var keys = new List<string> { "type", "parameter", "value", "valueMin", "valueMax", "chance", "convertRange", "sourceMin", "sourceMax", "destMin", "destMax" };
                            if (o.ContainsKey("source")) keys.Add("source");
                            Json.Obj(o, keys.ToArray());
                            behaviour.Operations.Add(new Operation
                            {
                                Type = Choice(o["type"], OperationTypes), Parameter = Index(o["parameter"], parameterCount),
                                Source = o.ContainsKey("source") ? Index(o["source"], parameterCount) : (int?)null,
                                Value = Json.Number(o["value"]), ValueMin = Json.Number(o["valueMin"]), ValueMax = Json.Number(o["valueMax"]),
                                Chance = Json.Number(o["chance"]), ConvertRange = Flag(o["convertRange"]),
                                SourceMin = Json.Number(o["sourceMin"]), SourceMax = Json.Number(o["sourceMax"]),
                                DestMin = Json.Number(o["destMin"]), DestMax = Json.Number(o["destMax"])
                            });
                        }
                        break;
                    case "trackingControl":
                        if (b.Count < 2 || b.Count > 33) throw Json.Bad();
                        foreach (var pair in b)
                        {
                            if (pair.Key == "type") continue;
                            if (pair.Key.Length == 0 || pair.Key.Length > 64 || !char.IsLower(pair.Key[0]) || !pair.Key.All(char.IsLetterOrDigit)) throw Json.Bad();
                            behaviour.Tracking.Add(new KeyValuePair<string, string>(pair.Key, Choice(pair.Value, TrackingValues)));
                        }
                        break;
                    case "layerControl":
                        Json.Obj(b, "type", "playable", "layer", "goalWeight", "blendDuration");
                        behaviour.Playable = Choice(b["playable"], LayerPlayables); behaviour.Layer = Json.NonNegative(b["layer"]);
                        behaviour.GoalWeight = Json.Number(b["goalWeight"]); behaviour.BlendDuration = Json.Number(b["blendDuration"]);
                        break;
                    default:
                        Json.Obj(b, "type", "playable", "goalWeight", "blendDuration");
                        behaviour.Playable = Choice(b["playable"], PlayableLayerPlayables);
                        behaviour.GoalWeight = Json.Number(b["goalWeight"]); behaviour.BlendDuration = Json.Number(b["blendDuration"]);
                        break;
                }
                into.Add(behaviour);
            }
        }

        // The shared key list reader, with this extension's limit of keys per clip.
        static ExpressionAnimationData.Curve ReadCurve(object raw, ref int keyCount)
        {
            var c = Json.Obj(raw, "preWrap", "postWrap", "keys");
            try { return ExpressionAnimationData.ReadCurve(c, ref keyCount); }
            catch (InvalidOperationException) { throw Json.Bad(); }
        }

        static object Get(Dictionary<string, object> owner, string key) => owner.TryGetValue(key, out var value) ? value : null;

        // Names may be empty: Unity and the SDK allow it.
        static string Name(object value) => value is string s && s.Length <= 1024 ? s : throw Json.Bad();

        static bool Flag(object value) => value is bool b ? b : throw Json.Bad();

        static string Choice(object value, params string[] allowed) => value is string s && Array.IndexOf(allowed, s) >= 0 ? s : throw Json.Bad();

        // An index into a table of count entries; -1 too when none is allowed.
        static int Index(object value, int count, bool none = false)
        {
            var number = Json.Number(value);
            if (number != Math.Floor(number) || number < (none ? -1 : 0) || number >= count) throw Json.Bad();
            return (int)number;
        }

        static readonly ContractJson Json = new ContractJson("Avatar animator data is invalid or exceeds the supported limits.");
    }
}
