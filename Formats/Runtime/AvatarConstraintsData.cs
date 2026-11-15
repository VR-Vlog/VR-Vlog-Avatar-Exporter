using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

// Reads through ContractJson in ExpressionAnimationData.cs, so a copy of this
// file needs that file too. No Unity/Editor dependency.
namespace VRVlog.AvatarConstraints
{
    // VRVLOG_avatar_constraints: the Unity and VRChat constraints the
    // exporter could not turn into VRMC_node_constraint (several sources,
    // weights, Scale, Parent offsets, Solve In Local Space and the like), with
    // their settings as authored, for the app to solve the way Unity does.
    // Each entry is one constraint component; FX curves on its enabled flag
    // are VRVLOG_avatar_animator enabled targets of component "constraint"
    // whose index is the entry's position in Constraints.
    //
    // Vectors are [x, y, z] in Unity's frames as the editor shows them
    // (positions in metres, rotations as Euler angles in degrees): the app
    // reads the nodes back into the same frames through UniVRM. Node is the
    // glTF node the constraint moves, each source's Node the node it follows.
    // Every type writes the keys of the settings it has and no others:
    // position: positionAxes, positionAtRest, positionOffset
    // rotation: rotationAxes, rotationAtRest, rotationOffset
    // scale: scaleAxes, scaleAtRest, scaleOffset
    // parent: positionAxes, rotationAxes, positionAtRest, rotationAtRest, and
    //   positionOffset and rotationOffset on each source
    // aim: rotationAxes, rotationAtRest, rotationOffset, aimAxis, upAxis,
    //   worldUp, worldUpVector, worldUpNode
    // lookAt: rotationAtRest, rotationOffset, roll, useUpNode, worldUpNode
    // worldUpNode is -1 when no object is set.
    public sealed class AvatarConstraintsData
    {
        public const string Extension = "VRVLOG_avatar_constraints";
        public const int SchemaVersion = 1;
        public const int MaximumConstraints = 4096;
        public const int MaximumSources = 1024;
        public static readonly string[] Types = { "position", "rotation", "scale", "parent", "aim", "lookAt" };
        // Which SDK the component came from: Unity's UnityEngine.Animations
        // constraints or VRChat's VRC Constraints, which solve slightly differently.
        public static readonly string[] Origins = { "unity", "vrchat" };
        // Unity's AimConstraint.WorldUpType, in its order (VRChat's has the same).
        public static readonly string[] WorldUpTypes = { "sceneUp", "objectUp", "objectRotationUp", "vector", "none" };

        public readonly List<Constraint> Constraints = new List<Constraint>();

        // Enabled is the component's enabled flag (m_Enabled, what FX turns on
        // and off); Active is Is Active (constraintActive). Freeze is VRChat's
        // Freeze To World and LocalSpace its Solve In Local Space; both are
        // false for a Unity constraint.
        public sealed class Constraint
        {
            public int Node;
            public string Type = "rotation", Origin = "unity";
            public bool Enabled = true, Active = true, Locked, LocalSpace, Freeze;
            public double Weight = 1;
            public bool[] PositionAxes, RotationAxes, ScaleAxes;
            public double[] PositionAtRest, RotationAtRest, ScaleAtRest, PositionOffset, RotationOffset, ScaleOffset;
            public double[] AimAxis, UpAxis, WorldUpVector;
            public string WorldUp = "sceneUp";
            public int WorldUpNode = -1;
            public double Roll;
            public bool UseUpNode;
            public readonly List<Source> Sources = new List<Source>();
        }

        // PositionOffset and RotationOffset are a parent constraint's offsets
        // from this source, null for the other types.
        public sealed class Source
        {
            public int Node;
            public double Weight;
            public double[] PositionOffset, RotationOffset;
        }

        static readonly string[] CommonKeys = { "node", "type", "origin", "enabled", "active", "locked", "weight", "localSpace", "freeze", "sources" };

        static string[] TypeKeys(string type)
        {
            switch (type)
            {
                case "position": return new[] { "positionAxes", "positionAtRest", "positionOffset" };
                case "rotation": return new[] { "rotationAxes", "rotationAtRest", "rotationOffset" };
                case "scale": return new[] { "scaleAxes", "scaleAtRest", "scaleOffset" };
                case "parent": return new[] { "positionAxes", "rotationAxes", "positionAtRest", "rotationAtRest" };
                case "aim": return new[] { "rotationAxes", "rotationAtRest", "rotationOffset", "aimAxis", "upAxis", "worldUp", "worldUpVector", "worldUpNode" };
                case "lookAt": return new[] { "rotationAtRest", "rotationOffset", "roll", "useUpNode", "worldUpNode" };
                default: throw Json.Bad();
            }
        }

        public static Dictionary<string, object> Write(AvatarConstraintsData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["constraints"] = data.Constraints.Select(c => (object)WriteConstraint(c)).ToList(),
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        static Dictionary<string, object> WriteConstraint(Constraint c)
        {
            var result = new Dictionary<string, object>
            {
                ["node"] = (long)c.Node, ["type"] = c.Type, ["origin"] = c.Origin, ["enabled"] = c.Enabled, ["active"] = c.Active, ["locked"] = c.Locked,
                ["weight"] = c.Weight, ["localSpace"] = c.LocalSpace, ["freeze"] = c.Freeze,
                ["sources"] = c.Sources.Select(s =>
                {
                    var source = new Dictionary<string, object> { ["node"] = (long)s.Node, ["weight"] = s.Weight };
                    if (c.Type == "parent") { source["positionOffset"] = Vector(s.PositionOffset); source["rotationOffset"] = Vector(s.RotationOffset); }
                    return (object)source;
                }).ToList(),
            };
            foreach (var key in TypeKeys(c.Type)) result[key] = Setting(c, key);
            return result;
        }

        static object Setting(Constraint c, string key)
        {
            switch (key)
            {
                case "positionAxes": return Axes(c.PositionAxes);
                case "rotationAxes": return Axes(c.RotationAxes);
                case "scaleAxes": return Axes(c.ScaleAxes);
                case "positionAtRest": return Vector(c.PositionAtRest);
                case "rotationAtRest": return Vector(c.RotationAtRest);
                case "scaleAtRest": return Vector(c.ScaleAtRest);
                case "positionOffset": return Vector(c.PositionOffset);
                case "rotationOffset": return Vector(c.RotationOffset);
                case "scaleOffset": return Vector(c.ScaleOffset);
                case "aimAxis": return Vector(c.AimAxis);
                case "upAxis": return Vector(c.UpAxis);
                case "worldUpVector": return Vector(c.WorldUpVector);
                case "worldUp": return c.WorldUp;
                case "worldUpNode": return (long)c.WorldUpNode;
                case "roll": return c.Roll;
                case "useUpNode": return c.UseUpNode;
                default: throw Json.Bad();
            }
        }

        static object Vector(double[] value) => value != null && value.Length == 3 ? value.Select(v => (object)v).ToList() : throw Json.Bad();
        static object Axes(bool[] value) => value != null && value.Length == 3 ? value.Select(v => (object)v).ToList() : throw Json.Bad();

        public static AvatarConstraintsData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "constraints");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new AvatarConstraintsData();
            foreach (var raw in Json.Arr(root["constraints"], 0, MaximumConstraints))
            {
                var type = Json.Obj(raw).TryGetValue("type", out var rawType) && rawType is string s && Array.IndexOf(Types, s) >= 0 ? s : throw Json.Bad();
                var o = Json.Obj(raw, CommonKeys.Concat(TypeKeys(type)).ToArray());
                var c = new Constraint
                {
                    Node = Json.NonNegative(o["node"]), Type = type, Origin = Choice(o["origin"], Origins),
                    Enabled = Flag(o["enabled"]), Active = Flag(o["active"]), Locked = Flag(o["locked"]), Weight = Json.Number(o["weight"]),
                    LocalSpace = Flag(o["localSpace"]), Freeze = Flag(o["freeze"]),
                };
                // Only a VRC Constraint has these settings.
                if (c.Origin == "unity" && (c.LocalSpace || c.Freeze)) throw Json.Bad();
                foreach (var rawSource in Json.Arr(o["sources"], 0, MaximumSources))
                {
                    var src = type == "parent" ? Json.Obj(rawSource, "node", "weight", "positionOffset", "rotationOffset") : Json.Obj(rawSource, "node", "weight");
                    var source = new Source { Node = Json.NonNegative(src["node"]), Weight = Json.Number(src["weight"]) };
                    if (type == "parent") { source.PositionOffset = ReadVector(src["positionOffset"]); source.RotationOffset = ReadVector(src["rotationOffset"]); }
                    c.Sources.Add(source);
                }
                foreach (var key in TypeKeys(type))
                {
                    var v = o[key];
                    switch (key)
                    {
                        case "positionAxes": c.PositionAxes = ReadAxes(v); break;
                        case "rotationAxes": c.RotationAxes = ReadAxes(v); break;
                        case "scaleAxes": c.ScaleAxes = ReadAxes(v); break;
                        case "positionAtRest": c.PositionAtRest = ReadVector(v); break;
                        case "rotationAtRest": c.RotationAtRest = ReadVector(v); break;
                        case "scaleAtRest": c.ScaleAtRest = ReadVector(v); break;
                        case "positionOffset": c.PositionOffset = ReadVector(v); break;
                        case "rotationOffset": c.RotationOffset = ReadVector(v); break;
                        case "scaleOffset": c.ScaleOffset = ReadVector(v); break;
                        case "aimAxis": c.AimAxis = ReadVector(v); break;
                        case "upAxis": c.UpAxis = ReadVector(v); break;
                        case "worldUpVector": c.WorldUpVector = ReadVector(v); break;
                        case "worldUp": c.WorldUp = Choice(v, WorldUpTypes); break;
                        case "worldUpNode": c.WorldUpNode = ReadNodeOrNone(v); break;
                        case "roll": c.Roll = Json.Number(v); break;
                        case "useUpNode": c.UseUpNode = Flag(v); break;
                    }
                }
                data.Constraints.Add(c);
            }
            return data;
        }

        static double[] ReadVector(object value) => Json.Arr(value, 3, 3).Select(Json.Number).ToArray();
        static bool[] ReadAxes(object value) => Json.Arr(value, 3, 3).Select(Flag).ToArray();
        static int ReadNodeOrNone(object value) => Json.Number(value) == -1 ? -1 : Json.NonNegative(value);
        static bool Flag(object value) => value is bool b ? b : throw Json.Bad();
        static string Choice(object value, string[] allowed) => value is string s && Array.IndexOf(allowed, s) >= 0 ? s : throw Json.Bad();

        static readonly ContractJson Json = new ContractJson("Avatar constraint data is invalid or exceeds the supported limits.");
    }
}
