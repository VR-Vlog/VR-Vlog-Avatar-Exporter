using System;
using System.Collections.Generic;
using VRVlog.AvatarFormats;

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
            if (data == null) throw Json.Bad();
            // Checked before rounding, so a value just outside the range is not rounded into it.
            var seconds = Json.Number(data.TransitionSeconds);
            if (seconds < 0 || seconds > MaximumTransitionSeconds) throw Json.Bad();
            var result = new Dictionary<string, object>
            {
                ["schemaVersion"] = (long)SchemaVersion,
                ["dominantViseme"] = data.DominantViseme,
                // Written to the millisecond, so 0.05f is written as 0.05.
                ["transitionSeconds"] = Math.Round(seconds, 3)
            };
            Read(result); // The exporter and runtime enforce the same contract.
            return result;
        }

        public static LipSyncData Read(object value)
        {
            var root = Json.Obj(value, "schemaVersion", "dominantViseme", "transitionSeconds");
            if (Json.Integer(root["schemaVersion"]) != SchemaVersion) throw Json.Bad();
            var seconds = Json.Number(root["transitionSeconds"]);
            if (seconds < 0 || seconds > MaximumTransitionSeconds) throw Json.Bad();
            return new LipSyncData
            {
                DominantViseme = root["dominantViseme"] is bool dominant ? dominant : throw Json.Bad(),
                TransitionSeconds = (float)seconds
            };
        }

        static readonly ContractJson Json = new ContractJson("Lip sync data is invalid or exceeds the supported limits.");
    }
}
