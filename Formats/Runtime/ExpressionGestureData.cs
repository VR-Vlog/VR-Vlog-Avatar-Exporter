using System;
using System.Collections.Generic;
using System.Linq;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.Expressions
{
    // VRVLOG_expression_gestures: the custom expressions the avatar's FX shows
    // for each pair of VRChat hand gestures, played with every other parameter
    // at its default. Cells[left * 8 + right] lists the expressions in the order
    // they apply (a later one overwrites the BlendShapes an earlier one sets);
    // an empty cell shows no gesture expression and a null cell could not be
    // determined. Gesture values are VRChat's: 0 neutral, 1 fist, 2 open hand,
    // 3 point, 4 victory, 5 rock'n'roll, 6 hand gun, 7 thumbs up.
    public sealed class ExpressionGestureData
    {
        public const string Extension = "VRVLOG_expression_gestures";
        public const int SchemaVersion = 1;
        public const int Gestures = 8;
        public const int CellCount = Gestures * Gestures;
        public const int ReferenceLimit = 8;
        public const string DriveLeft = "left", DriveRight = "right";

        public readonly List<Reference>[] Cells = new List<Reference>[CellCount];

        // Drive names the hand whose trigger (GestureLeftWeight or
        // GestureRightWeight) is the expression's weight; null holds it at 1.
        public sealed class Reference { public string Expression, Drive; }

        public static int Cell(int left, int right) =>
            left >= 0 && left < Gestures && right >= 0 && right < Gestures ? left * Gestures + right : throw new ArgumentOutOfRangeException();

        public static Dictionary<string, object> Write(ExpressionGestureData data)
        {
            if (data == null) throw Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["cells"] = data.Cells.Select(cell => cell == null ? null : (object)cell.Select(reference =>
                {
                    var item = new Dictionary<string, object> { ["expression"] = reference?.Expression };
                    if (reference?.Drive != null) item["drive"] = reference.Drive;
                    return (object)item;
                }).ToList()).ToList()
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static ExpressionGestureData Read(object value)
        {
            var root = Obj(value, "schemaVersion", "cells");
            if (Integer(root["schemaVersion"]) != SchemaVersion) throw Bad();
            var data = new ExpressionGestureData();
            var cells = Arr(root["cells"], CellCount, CellCount);
            var any = false;
            for (var index = 0; index < CellCount; index++)
            {
                if (cells[index] == null) continue;
                var cell = new List<Reference>();
                foreach (var raw in Arr(cells[index], 0, ReferenceLimit))
                {
                    var r = raw is Dictionary<string, object> map && map.ContainsKey("drive") ? Obj(raw, "expression", "drive") : Obj(raw, "expression");
                    var reference = new Reference { Expression = Text(r["expression"]) };
                    if (r.TryGetValue("drive", out var drive))
                        reference.Drive = drive is string hand && (hand == DriveLeft || hand == DriveRight) ? hand : throw Bad();
                    if (cell.Any(other => string.Equals(other.Expression, reference.Expression, StringComparison.Ordinal))) throw Bad();
                    cell.Add(reference);
                }
                any |= cell.Count > 0;
                data.Cells[index] = cell;
            }
            // A table without any expression is not written.
            if (!any) throw Bad();
            return data;
        }

        // Objects carry exactly the listed keys.
        static Dictionary<string, object> Obj(object value, params string[] keys) =>
            value is Dictionary<string, object> obj && obj.Count == keys.Length && keys.All(obj.ContainsKey) ? obj : throw Bad();
        static List<object> Arr(object value, int minimum, int maximum) =>
            value is List<object> array && array.Count >= minimum && array.Count <= maximum ? array : throw Bad();
        static string Text(object value) => value is string s && s.Length > 0 && s.Length <= 1024 ? s : throw Bad();
        static int Integer(object value)
        {
            if (!(value is double) && !(value is float) && !(value is long) && !(value is int)) throw Bad();
            var number = Convert.ToDouble(value);
            if (double.IsNaN(number) || double.IsInfinity(number) || number != Math.Floor(number) || Math.Abs(number) > 1000000) throw Bad();
            return (int)number;
        }
        static InvalidOperationException Bad() => new InvalidOperationException("Expression gesture data is invalid or exceeds the supported limits.");
    }
}
