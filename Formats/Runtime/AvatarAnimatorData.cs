using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
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
    // for what the app can play (activeSelf, BlendShape weights, renderer
    // enabled and material floats); the rest are counted by kind.
    public sealed class AvatarAnimatorData
    {
        public const string Extension = "VRVLOG_avatar_animator";
        // 2: the glTF holds the document packed (Pack), and a clip may name an
        // earlier clip whose tracks it plays (sameTracks). Read takes a
        // document of schema 1 or 2.
        public const int SchemaVersion = 2;
        // The packed document's bytes, compressed and as UTF-8 JSON (Unpack).
        public const int MaximumDocumentBytes = 32 * 1024 * 1024;
        // The keys of all the curves of one clip, the limit the shared key reader counts to.
        public const int MaximumKeysPerClip = 16384;
        // The tracks of one clip.
        public const int MaximumTracksPerClip = 4096;
        // A blend tree of clips is 1 deep; each tree inside a tree adds 1.
        public const int MaximumBlendTreeDepth = 32;
        // The motions under one motion, itself included, counting a tree
        // once for each parent that holds it: what playing one state walks.
        public const int MaximumExpandedMotions = 4096;
        // The custom expressions listed, and the ways into states one lists.
        public const int MaximumExpressions = 4096;
        public const int MaximumEntrances = 16384;
        // The omitted kinds a reader takes, those it does not know included.
        public const int MaximumOmittedKinds = 64;

        public static readonly string[] ParameterTypes = { "bool", "int", "float", "trigger" };
        public static readonly string[] ParameterOrigins = { "expression", "animator", "builtin" };
        public static readonly string[] ExpressionTypes = { "bool", "int", "float" };
        public static readonly string[] ControlTypes = { "toggle", "button", "radial", "twoAxis", "fourAxis", "subMenu" };
        public static readonly string[] Blendings = { "override", "additive" };
        public static readonly string[] InterruptionSources = { "none", "source", "destination", "sourceThenDestination", "destinationThenSource" };
        public static readonly string[] ConditionModes = { "if", "ifNot", "greater", "less", "equals", "notEqual" };
        static readonly string[] IfTypes = { "bool", "trigger" }, IfNotTypes = { "bool" }, NumberTypes = { "float", "int" }, IntegerTypes = { "int" };

        // The parameter types Unity takes a condition of that mode on: on a
        // parameter of another type it never takes the transition. A trigger
        // is only waited for with If.
        public static string[] ConditionTypes(string mode) =>
            mode == "if" ? IfTypes : mode == "ifNot" ? IfNotTypes : mode == "greater" || mode == "less" ? NumberTypes : IntegerTypes;
        public static readonly string[] OperationTypes = { "set", "add", "random", "copy" };
        public static readonly string[] TrackingValues = { "noChange", "tracking", "animation" };
        public static readonly string[] LayerPlayables = { "action", "fx", "gesture", "additive" };
        public static readonly string[] PlayableLayerPlayables = { "action", "fx", "gesture", "additive", "sitting" };
        public static readonly string[] BlendTypes = { "1d", "2dSimpleDirectional", "2dFreeformDirectional", "2dFreeformCartesian", "direct" };
        public static readonly string[] Properties = { "active", "morph", "parameter", "enabled", "materialFloat", "materialColor", "materialTextureST", "materialSlot", "translation", "rotation", "scale", "muscle" };
        // The number of humanoid muscles, the indices of Unity's
        // HumanTrait.MuscleName a muscle target names.
        public const int MuscleCount = 95;
        // The body parts of Unity's AvatarMaskBodyPart a layer's mask names.
        public const int BodyPartCount = 13;
        // The playables of the controllers, in the order they are written:
        // the first is the FX, which the expressions name.
        public static readonly string[] ControllerPlayables = { "fx", "gesture", "action" };
        // The components an enabled target turns on and off: a renderer of
        // the node, or a constraint of VRVLOG_avatar_constraints by index.
        public static readonly string[] Components = { "renderer", "constraint" };
        // The channels a materialColor and a materialTextureST target name.
        public static readonly string[] ColorChannels = { "r", "g", "b", "a" };
        public static readonly string[] TextureChannels = { "x", "y", "z", "w" };
        // The channels a translation, a scale and an Euler rotation target
        // name, and those of a quaternion rotation target.
        public static readonly string[] VectorChannels = { "x", "y", "z" };
        public static readonly string[] QuaternionChannels = { "x", "y", "z", "w" };
        // The order the omitted kinds are written in. A newer exporter adds
        // kinds at the end; a reader keeps those it does not know (see Read).
        public static readonly string[] OmittedKinds = { "blendShape", "material", "transform", "enabled", "parameter", "muscle", "other", "missingNode", "behaviour", "operation", "unreadParameter", "materialProperty", "materialSwitch", "materialSlotShader", "humanoidTransform", "rotationMixed", "humanoidRoot" };
        // The omitted kinds that change which transitions a controller takes,
        // in OmittedKinds order. A gesture or action controller's own omitted
        // holds only these, and the kinds a newer exporter adds.
        public static readonly string[] TransitionOmittedKinds = { "parameter", "operation", "unreadParameter" };
        public static readonly string[] EntranceSources = { "default", "entry", "any", "state" };

        public readonly List<Parameter> Parameters = new List<Parameter>();
        public readonly List<Menu> Menus = new List<Menu>();
        public readonly List<Controller> Controllers = new List<Controller>();
        public readonly List<Motion> Motions = new List<Motion>();
        public readonly List<Clip> Clips = new List<Clip>();
        public readonly List<Target> Targets = new List<Target>();
        public readonly List<Omission> Omitted = new List<Omission>();
        public readonly List<Expression> Expressions = new List<Expression>();

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

        // Omitted of a gesture or action controller holds what the exporter
        // left out while reading it that changes its transitions
        // (TransitionOmittedKinds), so a reader can tell whose timing it
        // changes; the FX's, and the other kinds, go to the data's Omitted.
        public sealed class Controller
        {
            public string Playable = "fx";
            public readonly List<Layer> Layers = new List<Layer>();
            public readonly List<Omission> Omitted = new List<Omission>();
        }

        // Machines[0] is the layer's root; States lists the states of every machine.
        // A synced layer (SyncedLayer, the index of its source layer, or -1)
        // holds a copy of the source layer's machines with its own motions and
        // state behaviours. The source is the layer that owns the machines, so
        // it is never a synced layer itself. The first layer has Weight 1 and
        // Blending override, as Unity plays it.
        // Mask is the layer's AvatarMask, or null when it has none and moves
        // everything.
        public sealed class Layer
        {
            public string Name, Blending = "override";
            public double Weight;
            public int SyncedLayer = -1;
            public bool SyncedLayerAffectsTiming;
            public Mask Mask;
            public readonly List<Machine> Machines = new List<Machine>();
            public readonly List<State> States = new List<State>();
        }

        // A layer's AvatarMask: Humanoid lists the body parts it lets move
        // (Unity's AvatarMaskBodyPart, in increasing order), and Nodes the
        // nodes its transform mask lets move (in increasing order), or is
        // null when the mask names no transforms and so lets every node move.
        public sealed class Mask
        {
            public readonly List<int> Humanoid = new List<int>();
            public List<int> Nodes;
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
            public bool Exit, Timed;
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
            public bool ConvertRange, PreventRepeats;
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
            public double Duration, CycleOffset;
            public bool Loop;
            public readonly List<Track> Tracks = new List<Track>();
        }

        public sealed class Track { public int Target; public readonly List<ExpressionAnimationData.Curve> Curves = new List<ExpressionAnimationData.Curve>(); }

        // Rest is the value before any layer runs: 0 or 1 for active, a Unity
        // BlendShape weight for morph, the controller's default for parameter.
        // A morph target is morph target Morph of the mesh on Node; Baked is
        // the weight already in the base geometry and End the weight the glTF
        // target reaches at 1, so a weight v shows the glTF weight
        // (v - Baked) / (End - Baked). A parameter target is FX parameter
        // Parameter, which belongs to no node (Node is -1).
        // An enabled target is the enabled flag of Component on Node (a
        // renderer, the one the node draws), Rest 0 or 1. An enabled target
        // of a constraint also names Index, the entry of the
        // VRVLOG_avatar_constraints table, whose node is Node. A materialFloat
        // target is the float property Name of glTF material Material, which
        // belongs to no node (Node is -1). A materialColor target is Channel
        // (r, g, b or a) of its color property Name, and a materialTextureST
        // target Channel (x, y: scale, z, w: offset) of the scale and offset
        // of its texture property Name (Unity's Name_ST), each one float.
        // A materialSlot target is slot Slot of the renderer on Node, its
        // values glTF material indices (-1 an empty slot), Rest among them.
        // It is Stepped: an evaluator never blends it, but takes the value
        // with the most weight, as Unity does with an object reference curve;
        // in a transition the rest of Write Defaults gets no weight, so the
        // motion that writes it shows while it has any.
        // A translation, rotation or scale target is Channel of the local
        // position, rotation or scale of Node, in Unity's space as the curve
        // holds it (not glTF's), each one float. A rotation target is a
        // quaternion channel (x, y, z or w), or with Euler an angle in degrees
        // (x, y or z, Unity's localEulerAnglesRaw) that a player turns into a
        // rotation with Quaternion.Euler after blending, as Unity does. The
        // rotation targets of one node are all Euler or all quaternion.
        // A muscle target is humanoid muscle Muscle (an index of Unity's
        // HumanTrait.MuscleName), which belongs to no node (Node is -1), its
        // Rest the T-pose's 0.
        // A target of a property this reader does not know (Known is false),
        // or an enabled target of a component it does not know,
        // is read without looking at its keys: a newer exporter may write
        // kinds a reader takes later. Clips may move it and an evaluator
        // gives it values, but a player writes it nowhere. Its Node is -1,
        // its Rest the "rest" key when that is a number and 0 otherwise, and
        // Fields its keys as read, which Write writes back unchanged (a
        // target made with no Fields is written as its property and rest).
        // Write writes a known target made with Fields as those Fields too,
        // and Read checks them as that kind.
        public sealed class Target
        {
            public int Node; public string Property = "active"; public double Rest;
            public int Morph; public double Baked, End;
            public int Parameter;
            public string Component, Name, Channel;
            public int Material;
            public int Index;
            public int Slot;
            public int Muscle;
            public bool Euler;
            public Dictionary<string, object> Fields;
            public bool Stepped => Property == "materialSlot";
            public bool Known => Array.IndexOf(Properties, Property) >= 0 && (Property != "enabled" || Array.IndexOf(Components, Component) >= 0);
        }

        public sealed class Omission { public string Kind; public int Count; }

        // A custom expression of VRMC_vrm by name: the clips it was read from
        // (in increasing order) and the ways into the FX states that play one
        // of them, directly or in a blend tree.
        public sealed class Expression
        {
            public string Name;
            public readonly List<int> Clips = new List<int>();
            public readonly List<Entrance> When = new List<Entrance>();
        }

        // From picks how State of Layer is entered: default (the layer starts
        // in it), entry (an entry transition of Machine goes to it), any (an
        // Any State transition does) or state (a transition of FromState does).
        // Conditions are that transition's; default has none.
        public sealed class Entrance
        {
            public int Layer, State, Machine = -1, FromState = -1;
            public string From;
            public readonly List<Condition> Conditions = new List<Condition>();
        }

        public static Dictionary<string, object> Write(AvatarAnimatorData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["parameters"] = data.Parameters.Select(p => (object)WriteParameter(p)).ToList(),
                ["menus"] = data.Menus.Select(m => (object)new Dictionary<string, object> { ["controls"] = m.Controls.Select(c => (object)WriteControl(c)).ToList() }).ToList(),
                ["expressions"] = data.Expressions.Select(e => (object)new Dictionary<string, object>
                {
                    ["name"] = e.Name, ["clips"] = e.Clips.Select(c => (object)(long)c).ToList(), ["when"] = e.When.Select(w => (object)WriteEntrance(w)).ToList()
                }).ToList(),
                ["controllers"] = data.Controllers.Select(c =>
                {
                    var controller = new Dictionary<string, object> { ["playable"] = c.Playable, ["layers"] = c.Layers.Select(l => (object)WriteLayer(l)).ToList() };
                    if (c.Omitted.Count > 0) controller["omitted"] = WriteOmitted(c.Omitted);
                    return (object)controller;
                }).ToList(),
                ["motions"] = data.Motions.Select(m => (object)WriteMotion(m)).ToList(),
                ["clips"] = WriteClips(data.Clips),
                ["targets"] = data.Targets.Select(t => (object)WriteTarget(t)).ToList(),
                ["omitted"] = WriteOmitted(data.Omitted)
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        // A clip with the same tracks as an earlier one names that clip in
        // sameTracks instead: a controller often plays one animation under
        // several names, and its keys are most of the document.
        static List<object> WriteClips(List<Clip> clips)
        {
            var result = new List<object>(clips.Count);
            var first = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < clips.Count; i++)
            {
                var c = clips[i];
                var clip = new Dictionary<string, object> { ["name"] = c.Name, ["duration"] = c.Duration, ["loop"] = c.Loop, ["cycleOffset"] = c.CycleOffset };
                var tracks = c.Tracks.Select(t => (object)new Dictionary<string, object>
                {
                    ["target"] = (long)t.Target, ["curves"] = t.Curves.Select(curve => (object)ExpressionAnimationData.WriteCurve(curve)).ToList()
                }).ToList();
                var key = tracks.Count == 0 ? null : Canonical(tracks);
                if (key != null && first.TryGetValue(key, out var same)) clip["sameTracks"] = (long)same;
                else
                {
                    clip["tracks"] = tracks;
                    if (key != null) first[key] = i;
                }
                result.Add(clip);
            }
            return result;
        }

        // The written value as text, equal for two values exactly when their JSON is.
        static string Canonical(object value)
        {
            var text = new StringBuilder();
            Append(text, value);
            return text.ToString();
        }

        static void Append(StringBuilder text, object value)
        {
            switch (value)
            {
                case Dictionary<string, object> map:
                    text.Append('{');
                    foreach (var pair in map) { Append(text, pair.Key); text.Append(':'); Append(text, pair.Value); text.Append(','); }
                    text.Append('}');
                    break;
                case List<object> list:
                    text.Append('[');
                    foreach (var item in list) { Append(text, item); text.Append(','); }
                    text.Append(']');
                    break;
                case string s: text.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); break;
                case double d: text.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: text.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); break;
                case long l: text.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case int n: text.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case bool b: text.Append(b ? "true" : "false"); break;
                case null: text.Append("null"); break;
                default: throw Json.Bad();
            }
        }

        // The extension in the glTF (schema 2): {"schemaVersion": 2,
        // "bufferView": n}, the bufferView holding the document (Write) as
        // UTF-8 JSON compressed with gzip (Pack). A reader of schema 1 finds
        // neither its version nor its tables and leaves the extension out.
        public static Dictionary<string, object> Packed(int bufferView) =>
            bufferView >= 0 ? new Dictionary<string, object> { ["schemaVersion"] = (long)SchemaVersion, ["bufferView"] = (long)bufferView } : throw Json.Bad();

        // Whether the extension's value is packed; a document written in the
        // JSON (schema 1, or what Write returns) is not.
        public static bool IsPacked(object value) => value is Dictionary<string, object> map && map.ContainsKey("bufferView");

        // The bufferView of a packed extension.
        public static int PackedView(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "bufferView");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            return Json.NonNegative(root["bufferView"]);
        }

        // The byte range of a bufferView in the GLB binary chunk (buffer 0).
        public static (int Offset, int Length) BufferViewRange(Dictionary<string, object> gltf, int bufferView)
        {
            var views = Json.Arr(gltf == null ? null : JsonAccess.Get(gltf, "bufferViews"));
            if (bufferView < 0 || bufferView >= views.Count) throw Json.Bad();
            var view = Json.Obj(views[bufferView]);
            if (Json.NonNegative(JsonAccess.Get(view, "buffer")) != 0 || view.ContainsKey("byteStride")) throw Json.Bad();
            var offset = view.ContainsKey("byteOffset") ? Json.NonNegative(view["byteOffset"]) : 0;
            var length = Json.NonNegative(JsonAccess.Get(view, "byteLength"));
            if (length == 0 || length > MaximumDocumentBytes) throw Json.Bad();
            return (offset, length);
        }

        // The document as JSON text, compressed with gzip.
        public static byte[] Pack(string document)
        {
            var bytes = new UTF8Encoding(false).GetBytes(document ?? throw Json.Bad());
            if (bytes.Length > MaximumDocumentBytes) throw Json.Bad();
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionLevel.Optimal, true)) gzip.Write(bytes, 0, bytes.Length);
                return output.ToArray();
            }
        }

        // The JSON text of a packed document, at most MaximumDocumentBytes.
        public static string Unpack(byte[] packed)
        {
            if (packed == null || packed.Length == 0 || packed.Length > MaximumDocumentBytes) throw Json.Bad();
            try
            {
                using (var gzip = new GZipStream(new MemoryStream(packed, false), CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaximumDocumentBytes) throw Json.Bad();
                        output.Write(buffer, 0, read);
                    }
                    // A cut stream ends early without an error on some
                    // runtimes: its last four bytes are then not the length
                    // gzip writes there (ISIZE).
                    if (packed.Length < 18) throw Json.Bad();
                    var size = (uint)(packed[packed.Length - 4] | packed[packed.Length - 3] << 8 | packed[packed.Length - 2] << 16 | packed[packed.Length - 1] << 24);
                    if (size != (uint)output.Length) throw Json.Bad();
                    return new UTF8Encoding(false, true).GetString(output.GetBuffer(), 0, (int)output.Length);
                }
            }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException) { throw Json.Bad(); }
        }

        static List<object> WriteOmitted(List<Omission> omitted) =>
            omitted.Select(o => (object)new Dictionary<string, object> { ["kind"] = o.Kind, ["count"] = (long)o.Count }).ToList();

        static Dictionary<string, object> WriteTarget(Target t) =>
            t.Fields != null ? new Dictionary<string, object>(t.Fields)
            : !t.Known ? new Dictionary<string, object> { ["property"] = t.Property, ["rest"] = t.Rest }
            : t.Property == "enabled" && t.Component == "constraint" ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["component"] = t.Component, ["index"] = (long)t.Index, ["rest"] = (long)t.Rest }
            : t.Property == "morph" ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["morph"] = (long)t.Morph, ["rest"] = t.Rest, ["baked"] = t.Baked, ["end"] = t.End }
            : t.Property == "parameter" ? new Dictionary<string, object> { ["property"] = t.Property, ["parameter"] = (long)t.Parameter, ["rest"] = t.Rest }
            : t.Property == "enabled" ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["component"] = t.Component, ["rest"] = (long)t.Rest }
            : t.Property == "materialFloat" ? new Dictionary<string, object> { ["property"] = t.Property, ["material"] = (long)t.Material, ["name"] = t.Name, ["rest"] = t.Rest }
            : IsMaterialChannel(t.Property) ? new Dictionary<string, object> { ["property"] = t.Property, ["material"] = (long)t.Material, ["name"] = t.Name, ["channel"] = t.Channel, ["rest"] = t.Rest }
            : t.Property == "materialSlot" ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["slot"] = (long)t.Slot, ["rest"] = (long)t.Rest }
            : t.Property == "muscle" ? new Dictionary<string, object> { ["property"] = t.Property, ["muscle"] = (long)t.Muscle, ["rest"] = t.Rest }
            : IsTransform(t.Property) && t.Euler ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["channel"] = t.Channel, ["euler"] = true, ["rest"] = t.Rest }
            : IsTransform(t.Property) ? new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["channel"] = t.Channel, ["rest"] = t.Rest }
            : new Dictionary<string, object> { ["node"] = (long)t.Node, ["property"] = t.Property, ["rest"] = t.Rest == 0 || t.Rest == 1 ? (object)(long)t.Rest : t.Rest };

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

        static Dictionary<string, object> WriteLayer(Layer l)
        {
            var layer = new Dictionary<string, object>
            {
                ["name"] = l.Name, ["weight"] = l.Weight, ["blending"] = l.Blending,
                ["syncedLayer"] = (long)l.SyncedLayer, ["syncedLayerAffectsTiming"] = l.SyncedLayerAffectsTiming
            };
            if (l.Mask != null)
            {
                var mask = new Dictionary<string, object> { ["humanoid"] = l.Mask.Humanoid.Select(i => (object)(long)i).ToList() };
                if (l.Mask.Nodes != null) mask["nodes"] = l.Mask.Nodes.Select(i => (object)(long)i).ToList();
                layer["mask"] = mask;
            }
            layer["machines"] = l.Machines.Select(m => (object)new Dictionary<string, object>
            {
                ["name"] = m.Name, ["parent"] = (long)m.Parent, ["defaultState"] = (long)m.DefaultState,
                ["entryTransitions"] = WriteTransitions(m.EntryTransitions),
                ["anyStateTransitions"] = WriteTransitions(m.AnyStateTransitions),
                ["exitTransitions"] = WriteTransitions(m.ExitTransitions),
                ["behaviours"] = WriteBehaviours(m.Behaviours)
            }).ToList();
            layer["states"] = l.States.Select(s =>
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
            }).ToList();
            return layer;
        }

        static List<object> WriteTransitions(List<Transition> transitions) => transitions.Select(t =>
        {
            var result = new Dictionary<string, object>();
            if (t.ToState.HasValue) result["toState"] = (long)t.ToState.Value;
            if (t.ToMachine.HasValue) result["toMachine"] = (long)t.ToMachine.Value;
            if (t.Exit) result["exit"] = true;
            if (t.Timed)
            {
                result["hasExitTime"] = t.HasExitTime; result["exitTime"] = t.ExitTime;
                result["hasFixedDuration"] = t.HasFixedDuration; result["duration"] = t.Duration; result["offset"] = t.Offset;
                result["interruptionSource"] = t.InterruptionSource; result["orderedInterruption"] = t.OrderedInterruption;
                result["canTransitionToSelf"] = t.CanTransitionToSelf;
            }
            result["conditions"] = WriteConditions(t.Conditions);
            return (object)result;
        }).ToList();

        static List<object> WriteConditions(List<Condition> conditions) => conditions.Select(c => (object)new Dictionary<string, object>
        {
            ["parameter"] = (long)c.Parameter, ["mode"] = c.Mode, ["threshold"] = c.Threshold
        }).ToList();

        static Dictionary<string, object> WriteEntrance(Entrance w)
        {
            var result = new Dictionary<string, object> { ["layer"] = (long)w.Layer, ["state"] = (long)w.State, ["from"] = w.From };
            if (w.From == "entry") result["machine"] = (long)w.Machine;
            if (w.From == "state") result["fromState"] = (long)w.FromState;
            result["conditions"] = WriteConditions(w.Conditions);
            return result;
        }

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
                        operation["preventRepeats"] = o.PreventRepeats;
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
            var version = Json.Integer(root["schemaVersion"]);
            if (version != 1 && version != SchemaVersion) throw Json.Bad();
            var data = new AvatarAnimatorData();
            // Counts first: entries refer forwards as well as backwards.
            var parameters = Json.Arr(root["parameters"], 0, 8192);
            var menus = Json.Arr(root["menus"], 0, 1024);
            var motions = Json.Arr(root["motions"], 0, 65536);
            var clips = Json.Arr(root["clips"], 0, 16384);
            var targets = Json.Arr(root["targets"], 0, 65536);
            var expressions = Json.Arr(root["expressions"], 0, MaximumExpressions);

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
            // Menu controls point at Expression Parameters only.
            int Expression(object value, bool none = false)
            {
                var index = Index(value, parameterCount, none);
                return index < 0 || data.Parameters[index].Origin == "expression" ? index : throw Json.Bad();
            }

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
                    if (c.ContainsKey("parameter")) control.Parameter = Expression(c["parameter"]);
                    if (c.ContainsKey("menu")) control.Menu = control.Type == "subMenu" ? Index(c["menu"], menus.Count) : throw Json.Bad();
                    foreach (var sub in Json.Arr(c["subParameters"], 0, 64)) control.SubParameters.Add(Expression(sub, true));
                    foreach (var label in Json.Arr(c["labels"], 0, 64)) control.Labels.Add(Name(label));
                    menu.Controls.Add(control);
                }
                data.Menus.Add(menu);
            }

            // The FX first, then the gesture and the action, each at most once.
            var playable = -1;
            foreach (var raw in Json.Arr(root["controllers"], 0, ControllerPlayables.Length))
            {
                var omitted = raw is Dictionary<string, object> rawController && rawController.ContainsKey("omitted");
                var c = omitted ? Json.Obj(raw, "playable", "layers", "omitted") : Json.Obj(raw, "playable", "layers");
                var controller = new Controller { Playable = Choice(c["playable"], ControllerPlayables) };
                // Only a gesture or action controller has omitted of its own, and then some.
                if (omitted)
                {
                    if (controller.Playable == "fx") throw Json.Bad();
                    ReadOmitted(c["omitted"], controller.Omitted, TransitionOmittedKinds);
                    if (controller.Omitted.Count == 0) throw Json.Bad();
                }
                var next = Array.IndexOf(ControllerPlayables, controller.Playable);
                if (playable < 0 ? next != 0 : next <= playable) throw Json.Bad();
                playable = next;
                var layers = Json.Arr(c["layers"], 0, 1024);
                for (var index = 0; index < layers.Count; index++)
                    controller.Layers.Add(ReadLayer(layers[index], index, layers.Count, data.Parameters, motions.Count));
                // The source of a synced layer owns its machines: it is not synced itself.
                foreach (var layer in controller.Layers)
                    if (layer.SyncedLayer >= 0 && (controller.Layers[layer.SyncedLayer].SyncedLayer >= 0 || !SyncedShape(layer, controller.Layers[layer.SyncedLayer]))) throw Json.Bad();
                // A condition is of a mode Unity takes on its parameter's type,
                // and a state option reads a float, Mirror a bool.
                foreach (var layer in controller.Layers)
                {
                    foreach (var transition in layer.Machines.SelectMany(m => m.EntryTransitions.Concat(m.AnyStateTransitions).Concat(m.ExitTransitions))
                        .Concat(layer.States.SelectMany(s => s.Transitions)))
                        foreach (var condition in transition.Conditions) Reads(condition.Parameter, ConditionTypes(condition.Mode));
                    foreach (var state in layer.States)
                    {
                        Reads(state.SpeedParameter, "float"); Reads(state.CycleOffsetParameter, "float"); Reads(state.TimeParameter, "float");
                        Reads(state.MirrorParameter, "bool");
                    }
                }
                data.Controllers.Add(controller);
            }

            // Each use of a parameter takes its own types: Unity does not read
            // the value of a parameter of another type there.
            void Reads(int? parameter, params string[] types)
            {
                if (parameter.HasValue && Array.IndexOf(types, data.Parameters[parameter.Value].Type) < 0) throw Json.Bad();
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
                    foreach (var parameter in motion.Parameters) Reads(parameter, "float");
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
                        // Unity holds a position as a float. Past that range the
                        // 2D weights (M12) would overflow into NaN.
                        if (Math.Abs(child.X) > float.MaxValue || Math.Abs(child.Y) > float.MaxValue) throw Json.Bad();
                        if (direct) child.DirectParameter = Index(ch["directParameter"], parameterCount);
                        Reads(child.DirectParameter, "float");
                        motion.Children.Add(child);
                    }
                }
                data.Motions.Add(motion);
            }
            // A blend tree must not contain itself, trees nest at most
            // MaximumBlendTreeDepth deep, and one motion expands to at most
            // MaximumExpandedMotions. Walked without recursion: the input
            // decides how deep this goes.
            var height = new int[data.Motions.Count]; // 0 = not seen, -1 = on the path, else trees below + 1
            var expanded = new int[data.Motions.Count]; // set with height; stops counting past the limit
            var path = new Stack<(int Motion, int Next)>();
            for (var start = 0; start < data.Motions.Count; start++)
            {
                if (height[start] != 0) continue;
                height[start] = -1;
                path.Push((start, 0));
                while (path.Count > 0)
                {
                    var (motion, next) = path.Pop();
                    var children = data.Motions[motion].Children;
                    while (next < children.Count && (children[next].Motion < 0 || height[children[next].Motion] > 0)) next++;
                    if (next < children.Count)
                    {
                        var child = children[next].Motion;
                        if (height[child] == -1) throw Json.Bad();
                        height[child] = -1;
                        path.Push((motion, next + 1));
                        path.Push((child, 0));
                        continue;
                    }
                    var below = 0;
                    var size = 1;
                    foreach (var child in children)
                    {
                        if (child.Motion < 0) continue;
                        below = Math.Max(below, height[child.Motion]);
                        size = Math.Min(size + expanded[child.Motion], MaximumExpandedMotions + 1);
                    }
                    height[motion] = data.Motions[motion].Type == "clip" ? 1 : Math.Max(below, 1) + 1;
                    expanded[motion] = size;
                    if (height[motion] > MaximumBlendTreeDepth + 1 || size > MaximumExpandedMotions) throw Json.Bad();
                }
            }

            for (var i = 0; i < clips.Count; i++)
            {
                // Schema 2: sameTracks in place of tracks names an earlier clip
                // with tracks of its own, whose tracks this clip plays too.
                var shared = version >= 2 && clips[i] is Dictionary<string, object> map && map.ContainsKey("sameTracks");
                var c = Json.Obj(clips[i], "name", "duration", "loop", "cycleOffset", shared ? "sameTracks" : "tracks");
                var clip = new Clip { Name = Name(c["name"]), Duration = Json.Number(c["duration"]), Loop = Flag(c["loop"]), CycleOffset = Json.Number(c["cycleOffset"]) };
                if (clip.Duration < 0) throw Json.Bad();
                if (shared)
                {
                    var same = Index(c["sameTracks"], i);
                    if (!((Dictionary<string, object>)clips[same]).ContainsKey("tracks") || data.Clips[same].Tracks.Count == 0) throw Json.Bad();
                    clip.Tracks.AddRange(data.Clips[same].Tracks);
                    data.Clips.Add(clip);
                    continue;
                }
                var keyCount = 0;
                var seen = new HashSet<int>();
                foreach (var rawTrack in Json.Arr(c["tracks"], 0, MaximumTracksPerClip))
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

            var seenTargets = new HashSet<(int, string, int, string)>();
            var eulerNodes = new Dictionary<int, bool>();
            foreach (var raw in targets)
            {
                var t = Json.Obj(raw);
                var target = new Target { Property = Name(Get(t, "property")) };
                // An enabled target always names its component, known or not.
                if (target.Property == "enabled" && (target.Component = Get(t, "component") as string) == null) throw Json.Bad();
                if (!target.Known)
                {
                    // Another kind, kept for a reader that knows it.
                    if (target.Property.Length == 0) throw Json.Bad();
                    target.Node = -1;
                    target.Rest = IsNumber(Get(t, "rest")) ? Json.Number(t["rest"]) : 0;
                    target.Fields = new Dictionary<string, object>(t);
                    data.Targets.Add(target);
                    continue;
                }
                if (target.Property == "morph")
                {
                    Json.Obj(t, "node", "property", "morph", "rest", "baked", "end");
                    target.Node = Json.NonNegative(t["node"]);
                    target.Morph = Json.NonNegative(t["morph"]);
                    target.Rest = Json.Number(t["rest"]); target.Baked = Json.Number(t["baked"]); target.End = Json.Number(t["end"]);
                    // At End == Baked the glTF target cannot show any weight.
                    if (target.End == target.Baked) throw Json.Bad();
                }
                else if (target.Property == "parameter")
                {
                    Json.Obj(t, "property", "parameter", "rest");
                    target.Node = -1;
                    target.Parameter = Index(t["parameter"], parameterCount);
                    target.Rest = Json.Number(t["rest"]);
                    // Rest is a value the parameter's type can hold.
                    var type = data.Parameters[target.Parameter].Type;
                    if (type == "int" ? target.Rest != Math.Floor(target.Rest) || target.Rest < int.MinValue || target.Rest > int.MaxValue : type != "float" && target.Rest != 0 && target.Rest != 1) throw Json.Bad();
                }
                else if (target.Property == "materialFloat")
                {
                    Json.Obj(t, "property", "material", "name", "rest");
                    target.Node = -1;
                    target.Material = Json.NonNegative(t["material"]);
                    target.Name = Json.Text(t["name"]);
                    target.Rest = Json.Number(t["rest"]);
                }
                else if (IsMaterialChannel(target.Property))
                {
                    Json.Obj(t, "property", "material", "name", "channel", "rest");
                    target.Node = -1;
                    target.Material = Json.NonNegative(t["material"]);
                    target.Name = Json.Text(t["name"]);
                    target.Channel = Choice(t["channel"], target.Property == "materialColor" ? ColorChannels : TextureChannels);
                    target.Rest = Json.Number(t["rest"]);
                }
                else if (target.Property == "materialSlot")
                {
                    Json.Obj(t, "node", "property", "slot", "rest");
                    target.Node = Json.NonNegative(t["node"]);
                    target.Slot = Json.NonNegative(t["slot"]);
                    target.Rest = SlotValue(t["rest"]);
                }
                else if (target.Property == "muscle")
                {
                    Json.Obj(t, "property", "muscle", "rest");
                    target.Node = -1;
                    target.Muscle = Index(t["muscle"], MuscleCount);
                    target.Rest = Json.Number(t["rest"]);
                }
                else if (IsTransform(target.Property))
                {
                    // Only a rotation is Euler, and its euler key is true.
                    target.Euler = target.Property == "rotation" && Get(t, "euler") != null;
                    if (target.Euler && !(t["euler"] is bool eulerKey && eulerKey)) throw Json.Bad();
                    Json.Obj(t, target.Euler ? new[] { "node", "property", "channel", "euler", "rest" } : new[] { "node", "property", "channel", "rest" });
                    target.Node = Json.NonNegative(t["node"]);
                    target.Channel = Choice(t["channel"], target.Property == "rotation" && !target.Euler ? QuaternionChannels : VectorChannels);
                    target.Rest = Json.Number(t["rest"]);
                    if (target.Property == "rotation" && eulerNodes.TryGetValue(target.Node, out var wasEuler) && wasEuler != target.Euler) throw Json.Bad();
                    if (target.Property == "rotation") eulerNodes[target.Node] = target.Euler;
                }
                else
                {
                    var constraint = target.Property == "enabled" && target.Component == "constraint";
                    Json.Obj(t, constraint ? new[] { "node", "property", "component", "index", "rest" }
                        : target.Property == "enabled" ? new[] { "node", "property", "component", "rest" } : new[] { "node", "property", "rest" });
                    target.Node = Json.NonNegative(t["node"]);
                    if (constraint) target.Index = Json.NonNegative(t["index"]);
                    target.Rest = Json.Integer(t["rest"]);
                    if (target.Rest != 0 && target.Rest != 1) throw Json.Bad();
                }
                // Unique per kind: (node) for active, (node, morph) for morph, (parameter) for
                // parameter, (node, component) for enabled, (component, index) for a
                // constraint's enabled (an entry moves one node), (material, name) for materialFloat,
                // (material, name, channel) for materialColor and materialTextureST,
                // (node, slot) for materialSlot, (node, channel) for translation, rotation and scale,
                // (muscle) for muscle.
                var material = target.Property == "materialFloat" || IsMaterialChannel(target.Property);
                var key = target.Property == "morph" ? target.Morph : target.Property == "parameter" ? target.Parameter : material ? target.Material :
                    target.Property == "materialSlot" ? target.Slot : target.Property == "enabled" ? target.Index : target.Property == "muscle" ? target.Muscle : 0;
                var keyName = target.Property == "enabled" ? target.Component : material ? target.Name + (target.Channel != null ? "." + target.Channel : "")
                    : IsTransform(target.Property) ? target.Channel : null;
                var keyNode = target.Property == "enabled" && target.Component == "constraint" ? -1 : target.Node;
                if (!seenTargets.Add((keyNode, target.Property, key, keyName))) throw Json.Bad();
                data.Targets.Add(target);
            }
            // Unity animates a float parameter only; a curve on an int, bool or
            // trigger holds it at its default, which a target without a track shows.
            foreach (var track in data.Clips.SelectMany(c => c.Tracks))
            {
                var target = data.Targets[track.Target];
                if (target.Property == "parameter" && data.Parameters[target.Parameter].Type != "float") throw Json.Bad();
                // A materialSlot curve holds material indices.
                if (target.Stepped)
                    foreach (var key in track.Curves.SelectMany(curve => curve.Keys))
                        if (key.Value != Math.Floor(key.Value) || key.Value < -1) throw Json.Bad();
            }

            // A face names the states that play its clips, in the FX above.
            var layerList = data.Controllers.Count > 0 ? data.Controllers[0].Layers : new List<Layer>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in expressions)
            {
                var e = Json.Obj(raw, "name", "clips", "when");
                var expression = new Expression { Name = Name(e["name"]) };
                if (expression.Name.Length == 0 || !names.Add(expression.Name)) throw Json.Bad();
                foreach (var clip in Json.Arr(e["clips"], 1, 1024))
                {
                    var index = Index(clip, data.Clips.Count);
                    if (expression.Clips.Count > 0 && index <= expression.Clips[expression.Clips.Count - 1]) throw Json.Bad();
                    expression.Clips.Add(index);
                }
                var faceClips = new HashSet<int>(expression.Clips);
                foreach (var rawEntrance in Json.Arr(e["when"], 0, MaximumEntrances))
                {
                    var w = Json.Obj(rawEntrance);
                    var from = Choice(Get(w, "from"), EntranceSources);
                    Json.Obj(w, from == "entry" ? new[] { "layer", "state", "from", "machine", "conditions" }
                        : from == "state" ? new[] { "layer", "state", "from", "fromState", "conditions" } : new[] { "layer", "state", "from", "conditions" });
                    var entrance = new Entrance { From = from, Layer = Index(w["layer"], layerList.Count) };
                    var layer = layerList[entrance.Layer];
                    entrance.State = Index(w["state"], layer.States.Count);
                    if (!Plays(data, layer.States[entrance.State].Motion, faceClips)) throw Json.Bad();
                    if (from == "entry") entrance.Machine = Index(w["machine"], layer.Machines.Count);
                    if (from == "state") entrance.FromState = Index(w["fromState"], layer.States.Count);
                    var conditions = Json.Arr(w["conditions"], 0, from == "default" ? 0 : 256);
                    foreach (var rawCondition in conditions)
                    {
                        var c = Json.Obj(rawCondition, "parameter", "mode", "threshold");
                        var condition = new Condition { Parameter = Index(c["parameter"], parameterCount), Mode = Choice(c["mode"], ConditionModes), Threshold = Json.Number(c["threshold"]) };
                        Reads(condition.Parameter, ConditionTypes(condition.Mode));
                        entrance.Conditions.Add(condition);
                    }
                    expression.When.Add(entrance);
                }
                data.Expressions.Add(expression);
            }

            ReadOmitted(root["omitted"], data.Omitted, OmittedKinds);
            return data;
        }

        // Known kinds in the order of known, then the kinds a newer exporter
        // added, each once. A kind of OmittedKinds that known leaves out is
        // not read.
        static void ReadOmitted(object value, List<Omission> into, string[] known)
        {
            var kind = -1;
            var newer = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in Json.Arr(value, 0, MaximumOmittedKinds))
            {
                var o = Json.Obj(raw, "kind", "count");
                var omission = new Omission { Kind = Name(o["kind"]), Count = Json.NonNegative(o["count"]) };
                var next = Array.IndexOf(known, omission.Kind);
                if (omission.Kind.Length == 0 || omission.Count == 0 || next < 0 && Array.IndexOf(OmittedKinds, omission.Kind) >= 0) throw Json.Bad();
                if (next < 0 ? !newer.Add(omission.Kind) : next <= kind || newer.Count > 0) throw Json.Bad();
                kind = Math.Max(kind, next);
                into.Add(omission);
            }
        }

        static Layer ReadLayer(object raw, int layerIndex, int layerCount, List<Parameter> parameters, int motionCount)
        {
            var parameterCount = parameters.Count;
            var layerKeys = new List<string> { "name", "weight", "blending", "syncedLayer", "syncedLayerAffectsTiming", "machines", "states" };
            if (raw is Dictionary<string, object> rawLayer && rawLayer.ContainsKey("mask")) layerKeys.Add("mask");
            var l = Json.Obj(raw, layerKeys.ToArray());
            var layer = new Layer
            {
                Name = Name(l["name"]), Weight = Json.Number(l["weight"]), Blending = Choice(l["blending"], Blendings),
                SyncedLayer = Index(l["syncedLayer"], layerCount, true), SyncedLayerAffectsTiming = Flag(l["syncedLayerAffectsTiming"])
            };
            if (l.ContainsKey("mask"))
            {
                var m = Json.Obj(l["mask"]);
                Json.Obj(m, m.ContainsKey("nodes") ? new[] { "humanoid", "nodes" } : new[] { "humanoid" });
                layer.Mask = new Mask();
                foreach (var part in Json.Arr(m["humanoid"], 0, BodyPartCount)) layer.Mask.Humanoid.Add(Index(part, BodyPartCount));
                if (m.ContainsKey("nodes"))
                {
                    layer.Mask.Nodes = new List<int>();
                    foreach (var node in Json.Arr(m["nodes"], 0, 65536)) layer.Mask.Nodes.Add(Json.NonNegative(node));
                }
                // Each list is in increasing order, each entry once.
                foreach (var list in new[] { layer.Mask.Humanoid, layer.Mask.Nodes ?? new List<int>() })
                    for (var i = 1; i < list.Count; i++)
                        if (list[i] <= list[i - 1]) throw Json.Bad();
            }
            // A layer is not synced to itself.
            if (layer.SyncedLayer == layerIndex) throw Json.Bad();
            var machines = Json.Arr(l["machines"], 1, 4096);
            var states = Json.Arr(l["states"], 0, 16384);
            Transition ReadOne(object rawTransition, bool timed)
            {
                var t = Json.Obj(rawTransition);
                var keys = new List<string> { "conditions" };
                if (timed) keys.AddRange(new[] { "hasExitTime", "exitTime", "hasFixedDuration", "duration", "offset", "interruptionSource", "orderedInterruption", "canTransitionToSelf" });
                var destinations = new[] { "toState", "toMachine", "exit" }.Where(t.ContainsKey).ToList();
                if (destinations.Count != 1) throw Json.Bad();
                keys.Add(destinations[0]);
                Json.Obj(t, keys.ToArray());
                var transition = new Transition { Timed = timed };
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
                // Only a machine inside another is left through Exit, and
                // Unity looks at Any State transitions on the root alone.
                if ((index == 0 ? machine.ExitTransitions : machine.AnyStateTransitions).Count > 0) throw Json.Bad();
                ReadBehaviours(m["behaviours"], machine.Behaviours, parameters);
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
                ReadBehaviours(s["behaviours"], state.Behaviours, parameters);
                layer.States.Add(state);
            }
            return layer;
        }

        static void ReadBehaviours(object raw, List<Behaviour> into, List<Parameter> parameters)
        {
            var parameterCount = parameters.Count;
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
                            // A copy has a source and nothing else has.
                            if (o.ContainsKey("source") != (o.ContainsKey("type") && o["type"] as string == "copy")) throw Json.Bad();
                            if (o.ContainsKey("source")) keys.Add("source");
                            // Data written before Prevent Repeats was read has no
                            // preventRepeats: the setting is off there.
                            if (o.ContainsKey("preventRepeats")) keys.Add("preventRepeats");
                            Json.Obj(o, keys.ToArray());
                            var operation = new Operation
                            {
                                Type = Choice(o["type"], OperationTypes), Parameter = Index(o["parameter"], parameterCount),
                                Source = o.ContainsKey("source") ? Index(o["source"], parameterCount) : (int?)null,
                                Value = Json.Number(o["value"]), ValueMin = Json.Number(o["valueMin"]), ValueMax = Json.Number(o["valueMax"]),
                                Chance = Json.Number(o["chance"]), ConvertRange = Flag(o["convertRange"]),
                                SourceMin = Json.Number(o["sourceMin"]), SourceMax = Json.Number(o["sourceMax"]),
                                DestMin = Json.Number(o["destMin"]), DestMax = Json.Number(o["destMax"]),
                                PreventRepeats = o.ContainsKey("preventRepeats") && Flag(o["preventRepeats"])
                            };
                            // The SDK offers Prevent Repeats only on a random into an int.
                            if (operation.PreventRepeats && (operation.Type != "random" || parameters[operation.Parameter].Type != "int")) throw Json.Bad();
                            behaviour.Operations.Add(operation);
                        }
                        break;
                    case "trackingControl":
                        if (b.Count < 2 || b.Count > 33) throw Json.Bad();
                        foreach (var pair in b)
                        {
                            if (pair.Key == "type") continue;
                            if (!IsTrackingPart(pair.Key)) throw Json.Bad();
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

        // A trackingControl key: an ASCII lower-case letter, then up to 63 ASCII
        // letters or digits (the schema's pattern).
        public static bool IsTrackingPart(string key) =>
            key != null && key != "type" && key.Length > 0 && key.Length <= 64 && key[0] >= 'a' && key[0] <= 'z' &&
            key.All(ch => ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9');

        static object Get(Dictionary<string, object> owner, string key) => owner.TryGetValue(key, out var value) ? value : null;

        // Names may be empty: Unity and the SDK allow it.
        static string Name(object value) => value is string s && s.Length <= 1024 ? s : throw Json.Bad();

        // Whether a motion plays one of the clips, itself or through the
        // children of its blend trees. The motion table has no cycle.
        public static bool Plays(AvatarAnimatorData data, int motion, ICollection<int> clips)
        {
            var seen = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(motion);
            while (pending.Count > 0)
            {
                var index = pending.Pop();
                if (index < 0 || !seen.Add(index)) continue;
                var m = data.Motions[index];
                if (m.Type == "clip") { if (clips.Contains(m.Clip)) return true; continue; }
                foreach (var child in m.Children) pending.Push(child.Motion);
            }
            return false;
        }

        // A synced layer copies its source's machines and states, index for index.
        internal static bool SyncedShape(Layer layer, Layer source)
        {
            if (layer.States.Count != source.States.Count || layer.Machines.Count != source.Machines.Count) return false;
            for (var i = 0; i < layer.States.Count; i++)
                if (layer.States[i].Machine != source.States[i].Machine) return false;
            return true;
        }

        static bool IsNumber(object value) => (value is double || value is float || value is long || value is int) && !double.IsNaN(Convert.ToDouble(value)) && !double.IsInfinity(Convert.ToDouble(value));
        static bool Flag(object value) => value is bool b ? b : throw Json.Bad();

        // A material index, or -1 for an empty slot.
        static double SlotValue(object raw)
        {
            var value = Json.Integer(raw);
            if (value < -1) throw Json.Bad();
            return value;
        }

        static bool IsMaterialChannel(string property) => property == "materialColor" || property == "materialTextureST";

        // A translation, rotation or scale target, a channel of a node's local transform.
        public static bool IsTransform(string property) => property == "translation" || property == "rotation" || property == "scale";
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
