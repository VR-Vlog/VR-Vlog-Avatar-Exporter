using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

// Reads through ContractJson in ExpressionAnimationData.cs, so a copy of this
// file needs that file too. No Unity/Editor dependency.
namespace VRVlog.AvatarContacts
{
    // VRVLOG_avatar_contacts: the avatar's VRChat Contact Senders and
    // Receivers, with the shape, tags and parameter the app needs to solve
    // their overlaps and feed the Receivers' parameters. Each entry is one
    // VRCContactSender or VRCContactReceiver component, enabled or not, in
    // hierarchy order.
    //
    // Node is the glTF node the contact hangs from: the node of its Root
    // Transform (its own object when none is set) or, when the export left
    // that object out, of its nearest written ancestor; -1 for the avatar
    // root, the VRM model space. Position and Rotation place the shape in
    // that node's space; Rotation is x, y, z, w. Radius and Height are in the
    // node's units: the app scales them by the largest absolute component of
    // the node's world scale, as VRChat scales them by its Root Transform's.
    // Height is the capsule's whole length along its local Y, the end caps
    // included; a sphere's is 0.
    //
    // A Receiver adds its Receiver Type, Parameter, Allow Self, Allow Others,
    // Local Only and Min Velocity. FX parameters of the Receivers' names are
    // the app's to write before each Advance.
    //
    // Omitted counts what the exporter left out: contactLimit, Receivers
    // past the first MaximumReceivers in hierarchy order.
    public sealed class AvatarContactsData
    {
        public const string Extension = "VRVLOG_avatar_contacts";
        public const int SchemaVersion = 1;
        // The Receivers the app solves per avatar; the exporter counts more as contactLimit.
        public const int MaximumReceivers = 64;
        public const int MaximumSenders = 4096;
        public const int MaximumTags = 64;
        // The longest parameter read, as other contracts limit their strings.
        public const int MaximumParameterLength = 1024;
        public static readonly string[] Shapes = { "sphere", "capsule" };
        public static readonly string[] ReceiverTypes = { "constant", "onEnter", "proximity" };
        // The order the omitted kinds are written in.
        public static readonly string[] OmittedKinds = { "contactLimit" };

        public readonly List<Contact> Senders = new List<Contact>();
        public readonly List<Contact> Receivers = new List<Contact>();
        public readonly List<Omission> Omitted = new List<Omission>();

        public sealed class Contact
        {
            public int Node = -1;
            public string Shape = "sphere";
            public double Radius, Height;
            public double[] Position = { 0, 0, 0 };
            public double[] Rotation = { 0, 0, 0, 1 };
            public readonly List<string> Tags = new List<string>();
            // Receivers only.
            public string Type = "constant";
            public string Parameter = "";
            public bool AllowSelf, AllowOthers, LocalOnly;
            public double MinVelocity;
        }

        public sealed class Omission
        {
            public string Kind;
            public int Count;
        }

        static readonly string[] SenderKeys = { "node", "shape", "radius", "height", "position", "rotation", "tags" };
        static readonly string[] ReceiverKeys = SenderKeys.Concat(new[] { "type", "parameter", "allowSelf", "allowOthers", "localOnly", "minVelocity" }).ToArray();

        public static Dictionary<string, object> Write(AvatarContactsData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["senders"] = data.Senders.Select(c => (object)WriteContact(c, false)).ToList(),
                ["receivers"] = data.Receivers.Select(c => (object)WriteContact(c, true)).ToList(),
                ["omitted"] = data.Omitted.Select(o => (object)new Dictionary<string, object> { ["kind"] = o.Kind, ["count"] = (long)o.Count }).ToList(),
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        static Dictionary<string, object> WriteContact(Contact c, bool receiver)
        {
            if (c == null || c.Position == null || c.Rotation == null) throw Json.Bad();
            var entry = new Dictionary<string, object>
            {
                ["node"] = (long)c.Node, ["shape"] = c.Shape, ["radius"] = c.Radius, ["height"] = c.Height,
                ["position"] = c.Position.Select(v => (object)v).ToList(), ["rotation"] = c.Rotation.Select(v => (object)v).ToList(),
                ["tags"] = c.Tags.Select(t => (object)t).ToList(),
            };
            if (receiver)
            {
                entry["type"] = c.Type; entry["parameter"] = c.Parameter; entry["allowSelf"] = c.AllowSelf;
                entry["allowOthers"] = c.AllowOthers; entry["localOnly"] = c.LocalOnly; entry["minVelocity"] = c.MinVelocity;
            }
            return entry;
        }

        public static AvatarContactsData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "senders", "receivers", "omitted");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new AvatarContactsData();
            foreach (var raw in Json.Arr(root["senders"], 0, MaximumSenders)) data.Senders.Add(ReadContact(raw, false));
            foreach (var raw in Json.Arr(root["receivers"], 0, MaximumReceivers)) data.Receivers.Add(ReadContact(raw, true));
            var next = 0;
            foreach (var raw in Json.Arr(root["omitted"], 0, OmittedKinds.Length))
            {
                var o = Json.Obj(raw, "kind", "count");
                var kind = Array.IndexOf(OmittedKinds, o["kind"] as string);
                // Each kind once, in OmittedKinds order, with a count.
                if (kind < next) throw Json.Bad();
                next = kind + 1;
                var count = Json.NonNegative(o["count"]);
                if (count == 0) throw Json.Bad();
                data.Omitted.Add(new Omission { Kind = OmittedKinds[kind], Count = count });
            }
            return data;
        }

        static Contact ReadContact(object raw, bool receiver)
        {
            var o = Json.Obj(raw, receiver ? ReceiverKeys : SenderKeys);
            var c = new Contact
            {
                Node = Json.Integer(o["node"]) is var node && node >= -1 ? node : throw Json.Bad(),
                Shape = Choice(o["shape"], Shapes),
                Radius = NonNegative(o["radius"]), Height = NonNegative(o["height"]),
                Position = Numbers(o["position"], 3), Rotation = Numbers(o["rotation"], 4),
            };
            foreach (var tag in Json.Arr(o["tags"], 0, MaximumTags)) c.Tags.Add(Json.Text(tag));
            if (receiver)
            {
                c.Type = Choice(o["type"], ReceiverTypes);
                c.Parameter = o["parameter"] is string s && s.Length <= MaximumParameterLength ? s : throw Json.Bad();
                c.AllowSelf = Flag(o["allowSelf"]); c.AllowOthers = Flag(o["allowOthers"]); c.LocalOnly = Flag(o["localOnly"]);
                c.MinVelocity = NonNegative(o["minVelocity"]);
            }
            return c;
        }

        static string Choice(object value, string[] choices) =>
            value is string s && Array.IndexOf(choices, s) >= 0 ? s : throw Json.Bad();
        static double[] Numbers(object value, int count) => Json.Arr(value, count, count).Select(Json.Number).ToArray();
        static double NonNegative(object value) { var v = Json.Number(value); return v >= 0 ? v : throw Json.Bad(); }
        static bool Flag(object value) => value is bool b ? b : throw Json.Bad();

        static readonly ContractJson Json = new ContractJson("Avatar contact data is invalid or exceeds the supported limits.");
    }
}
