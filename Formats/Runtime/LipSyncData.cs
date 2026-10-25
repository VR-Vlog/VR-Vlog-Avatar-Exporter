using System;
using System.Collections.Generic;
using System.Linq;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.Expressions
{
    // VRVLOG_lip_sync: how the app mixes the lip sync vowels. With
    // DominantViseme only the strongest vowel shows, as VRChat's Viseme
    // Parameter Only does, and a change of the strongest vowel blends from the
    // previous one over TransitionSeconds (0 switches at once).
    public sealed class LipSyncData
    {
        public const string Extension = "VRVLOG_lip_sync";
        public const int SchemaVersion = 1;
        public const double MaximumTransitionSeconds = 10;

        public bool DominantViseme;
        public float TransitionSeconds;

        public static Dictionary<string, object> Write(LipSyncData data)
        {
            if (data == null) throw Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["dominantViseme"] = data.DominantViseme,
                // Written to the millisecond, so 0.05f is written as 0.05.
                ["transitionSeconds"] = Math.Round((double)data.TransitionSeconds, 3)
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static LipSyncData Read(object value)
        {
            var root = Obj(value, "schemaVersion", "dominantViseme", "transitionSeconds");
            if (Integer(root["schemaVersion"]) != SchemaVersion) throw Bad();
            var seconds = Number(root["transitionSeconds"]);
            if (seconds < 0 || seconds > MaximumTransitionSeconds) throw Bad();
            return new LipSyncData
            {
                DominantViseme = root["dominantViseme"] is bool dominant ? dominant : throw Bad(),
                TransitionSeconds = (float)seconds
            };
        }

        // Objects carry exactly the listed keys.
        static Dictionary<string, object> Obj(object value, params string[] keys) =>
            value is Dictionary<string, object> obj && obj.Count == keys.Length && keys.All(obj.ContainsKey) ? obj : throw Bad();
        static double Number(object value)
        {
            if (!(value is double) && !(value is float) && !(value is long) && !(value is int)) throw Bad();
            var number = Convert.ToDouble(value);
            if (double.IsNaN(number) || double.IsInfinity(number)) throw Bad();
            return number;
        }
        static int Integer(object value)
        {
            var number = Number(value);
            if (number != Math.Floor(number) || Math.Abs(number) > 1000000) throw Bad();
            return (int)number;
        }
        static InvalidOperationException Bad() => new InvalidOperationException("Lip sync data is invalid or exceeds the supported limits.");
    }
}
