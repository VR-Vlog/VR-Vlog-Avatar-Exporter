using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.Expressions
{
    // VRVLOG_eye_shapes: the BlendShapes that move the eyes, by the same
    // vertices the blink moves. The blink expressions bind morph targets the
    // exporter adds (`__VRVlog_Blink_`), so a player cannot tell from them
    // that the avatar's own FX closes or shapes the eyes when it moves a
    // shape such as 「まばたき」, as an avatar that blinks from its FX does.
    // Each shape is a morph target of the node's mesh by its name in the
    // mesh's `extras.targetNames`. A name the mesh does not have is nothing.
    public sealed class EyeShapeData
    {
        public const string Extension = "VRVLOG_eye_shapes";
        public const int SchemaVersion = 1;
        public const int MaximumShapes = 4096;

        public sealed class Shape { public int Node; public string Name; }

        public readonly List<Shape> Shapes = new List<Shape>();

        public static Dictionary<string, object> Write(EyeShapeData data)
        {
            if (data == null) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["shapes"] = data.Shapes.Select(shape => (object)new Dictionary<string, object>
                {
                    ["node"] = (long)shape.Node, ["name"] = shape.Name
                }).ToList()
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static EyeShapeData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "shapes");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var data = new EyeShapeData();
            var seen = new HashSet<(int, string)>();
            foreach (var raw in Json.Arr(root["shapes"], 1, MaximumShapes))
            {
                var s = Json.Obj(raw, "node", "name");
                var shape = new Shape { Node = Json.NonNegative(s["node"]), Name = Json.Text(s["name"]) };
                if (!seen.Add((shape.Node, shape.Name))) throw Json.Bad();
                data.Shapes.Add(shape);
            }
            return data;
        }

        static readonly ContractJson Json = new ContractJson("Eye shape data is invalid or exceeds the supported limits.");
    }
}
