using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

// Reads through ContractJson in ExpressionAnimationData.cs, so a copy of this
// file needs that file too. No Unity/Editor dependency.
namespace VRVlog.AvatarPhysBones
{
    // VRVLOG_avatar_physbones: the PhysBones of the avatar with the settings
    // the app needs to feed the parameters VRChat writes for them. The swing
    // itself is the VRM SpringBone the exporter already writes; this table
    // adds nothing to it. Each entry is one VRCPhysBone component.
    //
    // Node is the glTF node of the PhysBone's root (its Root Transform, or
    // its own object when none is set). Parameter is the prefix VRChat adds
    // _IsGrabbed, _IsPosed, _Angle, _Stretch and _Squish to, empty when the
    // PhysBone has none. MaxStretch and MaxSquish are its Max Stretch and Max
    // Squish (the base values, without their curves; Max Squish is 0 on
    // PhysBone 1.0). AllowGrabbing and AllowPosing are whether anyone may
    // grab or pose it (VRChat's True or Other). LimitAngle is the angle in
    // degrees between the root's child and its rest direction at which
    // _Angle reads 1: VRChat normalizes _Angle to 180 degrees whatever the
    // PhysBone's limit, so the exporter writes 180.
    //
    // FX parameters named <Parameter>_IsGrabbed and the like are written in
    // VRVLOG_avatar_animator with origin "builtin": the app writes them
    // before each Advance.
    public sealed class AvatarPhysBonesData
    {
        public const string Extension = "VRVLOG_avatar_physbones";
        public const int SchemaVersion = 1;
        public const int MaximumPhysBones = 4096;
        // The longest parameter prefix read, as other contracts limit their strings.
        public const int MaximumParameterLength = 1024;
        // The suffixes of the parameters VRChat writes for a PhysBone, in this order.
        public static readonly string[] ParameterSuffixes = { "_IsGrabbed", "_IsPosed", "_Angle", "_Stretch", "_Squish" };

        public readonly List<PhysBone> PhysBones = new List<PhysBone>();

        public sealed class PhysBone
        {
            public int Node;
            public string Parameter = "";
            public double MaxStretch, MaxSquish;
            public bool AllowGrabbing, AllowPosing;
            public double LimitAngle = 180;
        }

        static readonly string[] Keys = { "node", "parameter", "maxStretch", "maxSquish", "allowGrabbing", "allowPosing", "limitAngle" };

        public static Dictionary<string, object> Write(AvatarPhysBonesData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["physBones"] = data.PhysBones.Select(p => (object)new Dictionary<string, object>
                {
                    ["node"] = (long)p.Node, ["parameter"] = p.Parameter, ["maxStretch"] = p.MaxStretch, ["maxSquish"] = p.MaxSquish,
                    ["allowGrabbing"] = p.AllowGrabbing, ["allowPosing"] = p.AllowPosing, ["limitAngle"] = p.LimitAngle,
                }).ToList(),
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static AvatarPhysBonesData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "physBones");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new AvatarPhysBonesData();
            foreach (var raw in Json.Arr(root["physBones"], 0, MaximumPhysBones))
            {
                var o = Json.Obj(raw, Keys);
                var p = new PhysBone
                {
                    Node = Json.NonNegative(o["node"]), Parameter = o["parameter"] is string s && s.Length <= MaximumParameterLength ? s : throw Json.Bad(),
                    MaxStretch = NonNegative(o["maxStretch"]), MaxSquish = NonNegative(o["maxSquish"]),
                    AllowGrabbing = Flag(o["allowGrabbing"]), AllowPosing = Flag(o["allowPosing"]), LimitAngle = Json.Number(o["limitAngle"]),
                };
                // _Angle divides by it.
                if (!(p.LimitAngle > 0 && p.LimitAngle <= 180)) throw Json.Bad();
                data.PhysBones.Add(p);
            }
            return data;
        }

        // The names VRChat writes for a PhysBone parameter prefix; none for an empty one.
        public static IEnumerable<string> ParameterNames(string parameter) =>
            string.IsNullOrEmpty(parameter) ? Enumerable.Empty<string>() : ParameterSuffixes.Select(suffix => parameter + suffix);

        static double NonNegative(object value) { var v = Json.Number(value); return v >= 0 ? v : throw Json.Bad(); }
        static bool Flag(object value) => value is bool b ? b : throw Json.Bad();

        static readonly ContractJson Json = new ContractJson("Avatar PhysBone data is invalid or exceeds the supported limits.");
    }
}
