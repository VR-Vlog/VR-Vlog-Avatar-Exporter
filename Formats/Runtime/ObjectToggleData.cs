using System;
using System.Collections.Generic;
using System.Linq;

// Identical source in the exporter (Formats/Runtime) and the application (UniVrmRuntime). Keep both copies the same.
// No Unity/Editor dependency.
namespace VRVlog.ObjectToggles
{
    // VRVLOG_object_toggles: the GameObjects that the avatar's Expression Menu
    // toggles switch on and off. Entries that share a parameter form a group;
    // each state of a group lists activeSelf of every target node for one value
    // of that parameter, sampled with the other parameters at their defaults.
    // An item is on while its group's value equals the item's value; turning it
    // on sets the group to that value and turning it off sets the group to 0.
    public sealed class ObjectToggleData
    {
        public const string Extension = "VRVLOG_object_toggles";
        public const int SchemaVersion = 1;
        public readonly List<Group> Groups = new List<Group>();
        public readonly List<Item> Items = new List<Item>();

        public sealed class Group
        {
            // Parameter is also the group's id: saved state maps parameter to value.
            public string Parameter, Name;
            public int DefaultValue;
            // glTF node indices. A node is a target of at most one group.
            public readonly List<int> Targets = new List<int>();
            public readonly List<State> States = new List<State>();

            public State StateFor(int value) => States.FirstOrDefault(s => s.Value == value);
        }

        // Active[i] is activeSelf of Targets[i] while the group has Value.
        public sealed class State { public int Value; public readonly List<bool> Active = new List<bool>(); }

        // Path lists the parent submenu names; the item name is not repeated.
        public sealed class Item { public string Id, Name, Parameter; public int Value; public readonly List<string> Path = new List<string>(); }

        public Group GroupFor(string parameter) => Groups.FirstOrDefault(g => string.Equals(g.Parameter, parameter, StringComparison.Ordinal));

        public static Dictionary<string, object> Write(ObjectToggleData data)
        {
            if (data == null) throw Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["groups"] = data.Groups.Select(g => (object)new Dictionary<string, object>
                {
                    ["parameter"] = g.Parameter, ["name"] = g.Name, ["defaultValue"] = (long)g.DefaultValue,
                    ["targets"] = g.Targets.Select(t => (object)(long)t).ToList(),
                    ["states"] = g.States.Select(s => (object)new Dictionary<string, object>
                    {
                        ["value"] = (long)s.Value, ["active"] = s.Active.Select(a => (object)a).ToList()
                    }).ToList()
                }).ToList(),
                ["items"] = data.Items.Select(i => (object)new Dictionary<string, object>
                {
                    ["id"] = i.Id, ["name"] = i.Name, ["path"] = i.Path.Select(p => (object)p).ToList(),
                    ["parameter"] = i.Parameter, ["value"] = (long)i.Value
                }).ToList()
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static ObjectToggleData Read(object value)
        {
            var root = Obj(value, "schemaVersion", "groups", "items");
            if (Integer(root["schemaVersion"]) != SchemaVersion) throw Bad();
            var data = new ObjectToggleData();
            var groups = Arr(root["groups"], 1, 256);
            var targets = new HashSet<int>();
            foreach (var raw in groups)
            {
                var g = Obj(raw, "parameter", "name", "defaultValue", "targets", "states");
                var group = new Group { Parameter = Text(g["parameter"]), Name = Text(g["name"]), DefaultValue = Integer(g["defaultValue"]) };
                if (data.GroupFor(group.Parameter) != null) throw Bad();
                foreach (var rawTarget in Arr(g["targets"], 1, 1024))
                {
                    var target = NonNegative(rawTarget);
                    if (!targets.Add(target) || targets.Count > 4096) throw Bad();
                    group.Targets.Add(target);
                }
                foreach (var rawState in Arr(g["states"], 2, 256))
                {
                    var s = Obj(rawState, "value", "active");
                    var state = new State { Value = Integer(s["value"]) };
                    if (group.StateFor(state.Value) != null) throw Bad();
                    foreach (var active in Arr(s["active"], group.Targets.Count, group.Targets.Count))
                        state.Active.Add(active is bool b ? b : throw Bad());
                    group.States.Add(state);
                }
                // Turning an item off sets 0, and loading starts at the default.
                if (group.StateFor(0) == null || group.StateFor(group.DefaultValue) == null) throw Bad();
                data.Groups.Add(group);
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in Arr(root["items"], 1, 512))
            {
                var i = Obj(raw, "id", "name", "path", "parameter", "value");
                var item = new Item { Id = Text(i["id"]), Name = Text(i["name"]), Parameter = Text(i["parameter"]), Value = Integer(i["value"]) };
                foreach (var part in Arr(i["path"], 0, 16)) item.Path.Add(Text(part));
                var group = data.GroupFor(item.Parameter);
                if (!ids.Add(item.Id) || group == null || item.Value == 0 || group.StateFor(item.Value) == null) throw Bad();
                used.Add(item.Parameter);
                data.Items.Add(item);
            }
            if (data.Groups.Any(g => !used.Contains(g.Parameter))) throw Bad();
            return data;
        }

        // Objects carry exactly the listed keys.
        static Dictionary<string, object> Obj(object value, params string[] keys) =>
            value is Dictionary<string, object> obj && obj.Count == keys.Length && keys.All(obj.ContainsKey) ? obj : throw Bad();
        static List<object> Arr(object value, int minimum, int maximum) =>
            value is List<object> array && array.Count >= minimum && array.Count <= maximum ? array : throw Bad();
        static string Text(object value) => value is string s && s.Length > 0 && s.Length <= 1024 ? s : throw Bad();
        static double Number(object value)
        {
            if (!(value is double) && !(value is float) && !(value is long) && !(value is int)) throw Bad();
            var number = Convert.ToDouble(value);
            if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Floor(number)) throw Bad();
            return number;
        }
        static int Integer(object value)
        {
            var number = Number(value);
            if (number < -1000000 || number > 1000000) throw Bad();
            return (int)number;
        }
        static int NonNegative(object value)
        {
            var number = Number(value);
            if (number < 0 || number > int.MaxValue) throw Bad();
            return (int)number;
        }
        static InvalidOperationException Bad() => new InvalidOperationException("Object toggle data is invalid or exceeds the supported limits.");
    }
}
