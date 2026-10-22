using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal static class LilToonEmissionPolicy
    {
        public static bool HasHdrTextureEmission(Material material, bool second=false)
        {
            var suffix=second?"2nd":"";var prefix="_Emission"+suffix;
            if (material == null || !material.HasProperty("_UseEmission"+suffix) || material.GetFloat("_UseEmission"+suffix) <= 0.5f ||
                !material.HasProperty(prefix+"Map") || material.GetTexture(prefix+"Map") == null ||
                !material.HasProperty(prefix+"Color")) return false;
            if (material.HasProperty(prefix+"Blend") && material.GetFloat(prefix+"Blend") <= 0) return false;
            var color = material.GetColor(prefix+"Color");
            return color.a > 0 && (color.r > 1 || color.g > 1 || color.b > 1);
        }

        // A common mobile-unfriendly setup adds the entire base image back as
        // HDR emission. Make this appearance approximation explicit and opt-out;
        // never identify eyes or a particular avatar by their material names.
        public static bool IsSuppressed(Material material, bool suppressSharedTextureEmission, bool second=false)
        {
            var map=second?"_Emission2ndMap":"_EmissionMap";
            if (!suppressSharedTextureEmission || material == null ||
                !material.HasProperty("_MainTex") || !material.HasProperty(map)) return false;
            var main = material.GetTexture("_MainTex");
            return main != null && main == material.GetTexture(map);
        }

        // lilToon 2.3.4 multiplies emission by _EmissionBlendMask and by
        // lerp(1, albedo, _EmissionMainStrength). MToon has neither, so the
        // fallback bakes them into its emissive image (EmissionMaskBaker).
        public static bool NeedsEmissionBake(Material material) => HasEmissionMask(material) ||
            material.HasProperty("_EmissionMainStrength") && material.GetFloat("_EmissionMainStrength") > 0;

        public static bool HasEmissionMask(Material material) => material.HasProperty("_EmissionBlendMask") &&
            material.GetTexture("_EmissionBlendMask") != null && material.GetTexture("_EmissionBlendMask") != Texture2D.whiteTexture;

        // The baked image is sampled with the main UV transform, so it needs
        // UV0 emission and an invertible main texture placement.
        public static bool CanBakeEmission(Material material)
        {
            if (material.HasProperty("_EmissionMap_UVMode") && material.GetFloat("_EmissionMap_UVMode") != 0) return false;
            if (!material.HasProperty("_MainTex")) return true;
            var scale = material.GetTextureScale("_MainTex");
            var offset = material.GetTextureOffset("_MainTex");
            return Finite(scale.x) && Finite(scale.y) && scale.x != 0 && scale.y != 0 && Finite(offset.x) && Finite(offset.y);
        }

#if !EXPORTER_BEHAVIOR_TESTS
        // Reads the image on the GPU, which the behavior tests do not have.
        // lilToon 2.3.4 multiplies the emission by _EmissionGradTex sampled at
        // (_EmissionGradSpeed * time, 0.5), and the alpha weights the blend.
        // MToon emission does not change over time, so the fallback uses the
        // average of that row over a cycle, or its value at time 0 when the
        // gradation does not move.
        public static Color GradientFactor(Material material)
        {
            if (material == null || !material.HasProperty("_EmissionUseGrad") || material.GetFloat("_EmissionUseGrad") <= 0.5f ||
                !material.HasProperty("_EmissionGradTex")) return Color.white;
            var texture = material.GetTexture("_EmissionGradTex");
            if (texture == null) return Color.white; // lilToon's default image is white
            var row = TextureColors.ReadLinear(texture, Mathf.Clamp(texture.width, 1, 256), 1);
            var speed = material.HasProperty("_EmissionGradSpeed") ? material.GetFloat("_EmissionGradSpeed") : 1f;
            var sum = Vector4.zero;
            var count = 0;
            for (var i = 0; i < row.Length; i++)
            {
                // At u = 0 the repeating bilinear sample mixes the first and last texels.
                if (speed == 0 && i != 0 && i != row.Length - 1) continue;
                sum += new Vector4(row[i].r * row[i].a, row[i].g * row[i].a, row[i].b * row[i].a, 0);
                count++;
            }
            sum /= count;
            return Finite(sum.x) && Finite(sum.y) && Finite(sum.z) ? new Color(sum.x, sum.y, sum.z, 1) : Color.white;
        }
#endif

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
