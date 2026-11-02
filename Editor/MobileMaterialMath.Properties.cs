using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Reads a material's float properties. Kept apart from the colour math so
    // that the host harnesses whose Unity shim has no Mathf or Color can compile it.
    internal static partial class MobileMaterialMath
    {
        internal static float Float(Material material, string property, float fallback = 0f) =>
            material.HasProperty(property) ? material.GetFloat(property) : fallback;

        // lilToon's toggles are on above 0.5. NonZero is a different check: a
        // value between 0 and 1 passes it, so each caller keeps the one it had.
        internal static bool Enabled(Material material, string property) => Float(material, property) > 0.5f;
        internal static bool NonZero(Material material, string property) => Float(material, property) != 0f;
    }
}
