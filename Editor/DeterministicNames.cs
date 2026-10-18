using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Names written into the VRM (generated morph targets, material copies,
    // baked textures) derive from stable inputs, so exporting the same avatar
    // twice produces the same names. Temporary files keep random GUIDs.
    internal static class DeterministicNames
    {
        // Lowercase hex of the first 16 SHA-256 bytes: the same 32 characters
        // as Guid.ToString("N"), so existing name lengths stay unchanged.
        internal static string Hash(params string[] parts)
        {
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            // Length-prefix each part so ("a", "bc") and ("ab", "c") differ.
            var text = new StringBuilder();
            foreach (var part in parts)
            {
                var value = part ?? "";
                text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
            }
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()));
            var hex = new StringBuilder(32);
            for (var i = 0; i < 16; i++) hex.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            return hex.ToString();
        }

        // Repeated inputs (same-named sibling renderers, duplicate entry names
        // or leftovers of an earlier export) get _2, _3 in generation order.
        internal static string Unique(string name, Func<string, bool> taken)
        {
            if (!taken(name)) return name;
            for (var suffix = 2; ; suffix++)
            {
                var candidate = name + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                if (!taken(candidate)) return candidate;
            }
        }

        internal static string Unique(string name, ISet<string> used)
        {
            var result = Unique(name, used.Contains);
            used.Add(result);
            return result;
        }

#if !EXPORTER_BAKE_TESTS
        // Generated morph names are matched by name alone (menu targets,
        // tracking profiles, optimizer mapping), so reserve every name already
        // on the export copy, including inactive renderers.
        internal static HashSet<string> BlendShapeNames(GameObject root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skin.sharedMesh != null)
                    for (var index = 0; index < skin.sharedMesh.blendShapeCount; index++)
                        names.Add(skin.sharedMesh.GetBlendShapeName(index));
            return names;
        }
#endif
    }
}
