using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.AvatarFormats;

namespace VRVlog.LilToonExporter
{
    // Every export path calls this on its final bytes. It rebuilds
    // asset.extras[VRVLOG_requiredAppVersions] from the VRVLOG_ extensions in
    // extensionsUsed, reading each version from the written extension, and
    // keeps the other asset.extras keys. Without such extensions it writes no key.
    internal static class AvatarFormatRequirements
    {
        internal static byte[] Write(byte[] bytes)
        {
            var glb = GlbDocument.Read(bytes);
            var json = glb.Json;
            var required = new Dictionary<string, object>();
            if (json.TryGetValue("extensionsUsed", out var rawUsed) && rawUsed is List<object> used)
                foreach (var name in used.OfType<string>().Where(name => name.StartsWith("VRVLOG_", StringComparison.Ordinal)).Distinct())
                    required[name] = AvatarFormatVersions.RequiredAppVersion(name, Version(json, name));
            if (!(json.TryGetValue("asset", out var rawAsset) && rawAsset is Dictionary<string, object> asset))
                throw new InvalidOperationException("glTF asset is missing.");
            var extras = asset.TryGetValue("extras", out var rawExtras) ? rawExtras as Dictionary<string, object> : null;
            if (rawExtras != null && extras == null) throw new InvalidOperationException("glTF asset.extras is not an object.");
            var existing = extras != null && extras.TryGetValue(AvatarFormatVersions.RequiredAppVersionsKey, out var value) ? value as Dictionary<string, object> : null;
            // Unchanged requirements keep the bytes as they are.
            if (required.Count == 0 ? extras == null || !extras.ContainsKey(AvatarFormatVersions.RequiredAppVersionsKey) :
                existing != null && existing.Count == required.Count && required.All(pair => existing.TryGetValue(pair.Key, out var version) && Equals(version, pair.Value)))
                return bytes;
            if (required.Count == 0) extras.Remove(AvatarFormatVersions.RequiredAppVersionsKey);
            else
            {
                if (extras == null) asset["extras"] = extras = new Dictionary<string, object>();
                extras[AvatarFormatVersions.RequiredAppVersionsKey] = required;
            }
            return glb.Write();
        }

        // VRVLOG_materials_liltoon carries schemaMajor; the others schemaVersion.
        static int Version(Dictionary<string, object> json, string name)
        {
            if (!(json.TryGetValue("extensions", out var raw) && raw is Dictionary<string, object> extensions &&
                extensions.TryGetValue(name, out var rawExtension) && rawExtension is Dictionary<string, object> extension))
                throw new InvalidOperationException("Declared extension is missing: " + name);
            var key = name == LilToon.LilToonFullContract.ExtensionName ? "schemaMajor" : "schemaVersion";
            if (!extension.TryGetValue(key, out var number) || !(number is long || number is int || number is double))
                throw new InvalidOperationException("Extension " + name + " has no " + key + ".");
            var version = Convert.ToDouble(number);
            if (version != Math.Floor(version) || version < 0 || version > int.MaxValue)
                throw new InvalidOperationException("Extension " + name + " has an invalid " + key + ".");
            return (int)version;
        }
    }
}
