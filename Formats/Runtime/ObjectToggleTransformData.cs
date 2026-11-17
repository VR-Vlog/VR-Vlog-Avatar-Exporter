using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

// Shared by the exporter and player through VRVlog.AvatarFormats. Reads through
// ContractJson in ExpressionAnimationData.cs. No Unity/Editor dependency.
namespace VRVlog.ObjectToggles
{
    // VRVLOG_object_toggle_transforms: the local transforms of the nodes (bones)
    // that the Expression Menu toggles of VRVLOG_object_toggles move, such as a
    // hat that shrinks the hair bone under it. Each group extends the group of
    // VRVLOG_object_toggles with the same parameter and has one state for each
    // of that group's states. Values are Unity local values (not converted to
    // glTF space) and absolute, so the state of the default value also holds
    // where the written rest pose differs from the avatar's default.
    // A separate extension, so a reader of VRVLOG_object_toggles 1 that rejects
    // unknown keys keeps its toggles.
    public sealed class ObjectToggleTransformData
    {
        public const string Extension = "VRVLOG_object_toggle_transforms";
        public const int SchemaVersion = 1;
        public const string Translation = "translation", Rotation = "rotation", Scale = "scale";
        static readonly string[] Properties = { Translation, Rotation, Scale };
        public readonly List<Group> Groups = new List<Group>();

        public sealed class Group
        {
            public string Parameter;
            public readonly List<Target> Targets = new List<Target>();
            public readonly List<State> States = new List<State>();

            public State StateFor(int value) => States.FirstOrDefault(s => s.Value == value);
        }

        // Properties lists the parts of the transform the toggle moves, in the
        // order translation, rotation, scale.
        public sealed class Target { public int Node; public readonly List<string> Properties = new List<string>(); }

        // Transforms[i] is the transform of Targets[i] while the group has Value.
        public sealed class State { public int Value; public readonly List<Transform> Transforms = new List<Transform>(); }

        // Translation and scale are x, y, z; rotation is a quaternion x, y, z, w.
        // Only the target's properties are set.
        public sealed class Transform { public double[] Translation, Rotation, Scale; }

        public Group GroupFor(string parameter) => Groups.FirstOrDefault(g => string.Equals(g.Parameter, parameter, StringComparison.Ordinal));

        public static Dictionary<string, object> Write(ObjectToggleTransformData data, ObjectToggleData toggles)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["groups"] = data.Groups.Select(g => (object)new Dictionary<string, object>
                {
                    ["parameter"] = g.Parameter,
                    ["targets"] = g.Targets.Select(t => (object)new Dictionary<string, object>
                    {
                        ["node"] = (long)t.Node, ["properties"] = t.Properties.Select(p => (object)p).ToList()
                    }).ToList(),
                    ["states"] = g.States.Select(s => (object)new Dictionary<string, object>
                    {
                        ["value"] = (long)s.Value,
                        ["transforms"] = s.Transforms.Select((t, i) => (object)WriteTransform(t, i < g.Targets.Count ? g.Targets[i].Properties : null)).ToList()
                    }).ToList()
                }).ToList()
            };
            Read(result, toggles); // The exporter and runtime enforce the same contract.
            return result;
        }

        static Dictionary<string, object> WriteTransform(Transform transform, List<string> properties)
        {
            if (transform == null || properties == null) throw Json.Bad();
            var result = new Dictionary<string, object>();
            foreach (var property in properties)
            {
                var values = property == Translation ? transform.Translation : property == Rotation ? transform.Rotation : property == Scale ? transform.Scale : null;
                result[property] = values?.Select(v => (object)v).ToList() ?? throw Json.Bad();
            }
            return result;
        }

        // toggles is the VRVLOG_object_toggles of the same file: each group here
        // names one of its groups and has exactly its state values.
        public static ObjectToggleTransformData Read(object value, ObjectToggleData toggles)
        {
            if (toggles == null) throw Json.Bad();
            var root = Json.Obj(value, "schemaVersion", "groups");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new ObjectToggleTransformData();
            var nodes = new HashSet<int>();
            foreach (var raw in Json.Arr(root["groups"], 1, 256))
            {
                var g = Json.Obj(raw, "parameter", "targets", "states");
                var group = new Group { Parameter = Json.Text(g["parameter"]) };
                var toggle = toggles.GroupFor(group.Parameter);
                if (toggle == null || data.GroupFor(group.Parameter) != null) throw Json.Bad();
                foreach (var rawTarget in Json.Arr(g["targets"], 1, 1024))
                {
                    var t = Json.Obj(rawTarget, "node", "properties");
                    var target = new Target { Node = Json.NonNegative(t["node"]) };
                    if (!nodes.Add(target.Node) || nodes.Count > 4096) throw Json.Bad();
                    foreach (var rawProperty in Json.Arr(t["properties"], 1, 3))
                        target.Properties.Add(rawProperty is string property && Properties.Contains(property) ? property : throw Json.Bad());
                    // In the order of Properties, each once.
                    if (!target.Properties.SequenceEqual(Properties.Where(target.Properties.Contains))) throw Json.Bad();
                    group.Targets.Add(target);
                }
                foreach (var rawState in Json.Arr(g["states"], 2, 256))
                {
                    var s = Json.Obj(rawState, "value", "transforms");
                    var state = new State { Value = Json.Integer(s["value"]) };
                    if (group.StateFor(state.Value) != null || toggle.StateFor(state.Value) == null) throw Json.Bad();
                    var transforms = Json.Arr(s["transforms"], group.Targets.Count, group.Targets.Count);
                    for (var i = 0; i < transforms.Count; i++)
                    {
                        var properties = group.Targets[i].Properties;
                        var t = Json.Obj(transforms[i], properties.ToArray());
                        state.Transforms.Add(new Transform
                        {
                            Translation = properties.Contains(Translation) ? Vector(t[Translation], 3) : null,
                            Rotation = properties.Contains(Rotation) ? Quaternion(t[Rotation]) : null,
                            Scale = properties.Contains(Scale) ? Vector(t[Scale], 3) : null
                        });
                    }
                    group.States.Add(state);
                }
                if (group.States.Count != toggle.States.Count) throw Json.Bad();
                data.Groups.Add(group);
            }
            return data;
        }

        static double[] Vector(object value, int count) => Json.Arr(value, count, count).Select(Json.Number).ToArray();

        // A quaternion of length 1 (within 1e-3); readers normalize it.
        static double[] Quaternion(object value)
        {
            var q = Vector(value, 4);
            var length = Math.Sqrt(q.Sum(v => v * v));
            if (Math.Abs(length - 1) > 1e-3) throw Json.Bad();
            return q;
        }

        static readonly ContractJson Json = new ContractJson("Object toggle transform data is invalid or exceeds the supported limits.");
    }
}
