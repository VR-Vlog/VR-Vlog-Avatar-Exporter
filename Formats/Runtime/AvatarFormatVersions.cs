using System;
using System.Collections.Generic;

// Shared by the exporter and player through VRVlog.AvatarFormats. No Unity/Editor dependency.
namespace VRVlog.AvatarFormats
{
    // glTF asset.extras[RequiredAppVersionsKey] maps each VRVLOG_ extension in
    // extensionsUsed to the lowest app version that reads the written content.
    public static class AvatarFormatVersions
    {
        public const string RequiredAppVersionsKey = "VRVLOG_requiredAppVersions";

        // Keyed by extension name and schema version (schemaMajor for
        // VRVLOG_materials_liltoon). Values come from the app's release history;
        // see Documentation~/decisions/2026-10-19-expression-animations-v2.md.
        static readonly Dictionary<(string, int), string> RequiredAppVersions = new Dictionary<(string, int), string>
        {
            [(LilToon.LilToonFullContract.ExtensionName, 2)] = "1.0.0",
            [(Expressions.ExpressionAnimationData.Extension, 1)] = "1.0.0",
            [(Expressions.ExpressionAnimationData.Extension, 2)] = "1.0.5",
            [("VRVLOG_humanoid_poses", 1)] = "1.0.0",
            [("VRVLOG_humanoid_animations", 1)] = "1.0.4",
            // Provisional: see Documentation~/decisions/2026-10-23-expression-gestures.md.
            [(Expressions.ExpressionGestureData.Extension, Expressions.ExpressionGestureData.SchemaVersion)] = "1.0.8",
        };

        public static string RequiredAppVersion(string extension, int schemaVersion) =>
            RequiredAppVersions.TryGetValue((extension, schemaVersion), out var version) ? version :
                throw new InvalidOperationException("No app version is known for " + extension + " schema " + schemaVersion + ".");

        // Compares major.minor.patch numerically: Compare("1.0.5", "1.0.10") < 0.
        public static int Compare(string left, string right)
        {
            var a = Parse(left); var b = Parse(right);
            for (var i = 0; i < 3; i++)
                if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
            return 0;
        }

        static int[] Parse(string version)
        {
            var parts = (version ?? throw new FormatException("Invalid app version.")).Split('.');
            if (parts.Length != 3) throw new FormatException("Invalid app version: " + version);
            var result = new int[3];
            for (var i = 0; i < 3; i++)
            {
                var part = parts[i];
                // Digits only, without a sign or leading zero, as in Semantic Versioning.
                if (part.Length == 0 || part.Length > 9 || part.Length > 1 && part[0] == '0') throw new FormatException("Invalid app version: " + version);
                foreach (var c in part)
                    if (c < '0' || c > '9') throw new FormatException("Invalid app version: " + version);
                result[i] = int.Parse(part, System.Globalization.CultureInfo.InvariantCulture);
            }
            return result;
        }
    }
}
