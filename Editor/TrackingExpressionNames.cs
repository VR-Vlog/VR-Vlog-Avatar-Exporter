using System;

namespace VRVlog.LilToonExporter
{
    // The ARKit (Perfect Sync) channel names and how the app recognizes them
    // in an expression name. Kept free of Unity types so the host tests can
    // check the names an exported expression must not take.
    internal static class TrackingExpressionNames
    {
        internal static readonly string[] PerfectSync = {
            "BrowDownLeft", "BrowDownRight", "BrowInnerUp", "BrowOuterUpLeft", "BrowOuterUpRight",
            "CheekPuff", "CheekSquintLeft", "CheekSquintRight", "EyeBlinkLeft", "EyeBlinkRight",
            "EyeLookDownLeft", "EyeLookDownRight", "EyeLookInLeft", "EyeLookInRight",
            "EyeLookOutLeft", "EyeLookOutRight", "EyeLookUpLeft", "EyeLookUpRight",
            "EyeSquintLeft", "EyeSquintRight", "EyeWideLeft", "EyeWideRight",
            "JawForward", "JawLeft", "JawOpen", "JawRight", "MouthClose",
            "MouthDimpleLeft", "MouthDimpleRight", "MouthFrownLeft", "MouthFrownRight",
            "MouthFunnel", "MouthLeft", "MouthLowerDownLeft", "MouthLowerDownRight",
            "MouthPressLeft", "MouthPressRight", "MouthPucker", "MouthRight",
            "MouthRollLower", "MouthRollUpper", "MouthShrugLower", "MouthShrugUpper",
            "MouthSmileLeft", "MouthSmileRight", "MouthStretchLeft", "MouthStretchRight",
            "MouthUpperUpLeft", "MouthUpperUpRight", "NoseSneerLeft", "NoseSneerRight", "TongueOut"
        };

        // Same rule as the app's Perfect Sync name matching: the trimmed name
        // equals a channel ignoring case, or ends with one (ignoring case)
        // right after '.', '/', '\', '_', '-' or ':' (a mesh namespace or a
        // "Fcl_ALL_" style prefix). The app skips names under "UE/" or
        // "VRChat /"; reserving those too only numbers a few more names.
        internal static bool IsPerfectSync(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var candidate = name.Trim();
            foreach (var channel in PerfectSync)
            {
                if (!candidate.EndsWith(channel, StringComparison.OrdinalIgnoreCase)) continue;
                var start = candidate.Length - channel.Length;
                if (start == 0 || "./\\_-:".IndexOf(candidate[start - 1]) >= 0) return true;
            }
            return false;
        }
    }
}
