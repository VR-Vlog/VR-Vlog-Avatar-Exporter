using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 細い輪郭線を省く: a lilToon outline drawn thinner than the threshold
    // costs a full extra draw of its mesh for a line the viewer barely sees.
    // Such a material's outline passes are written switched off in the
    // lilToon extension; the app already skips a pass the record switches
    // off. The material, its other passes and the MToon fallback stay as
    // they are. The width is the one the scene draws: _OutlineWidth (cm)
    // times the renderer's scale (OutlineRendererScale), the largest among
    // the renderers that use the material. That is the widest the line
    // gets: the width mask, vertex color width and _OutlineFixWidth (which
    // narrows the line closer than 1 m) only scale it down. A material
    // with an outline vector map keeps its outline. Expressions that
    // animate the width are not read.
    internal static class OutlineLightweight
    {
        internal const float DefaultThresholdMillimeters = .5f;
        internal const float MaximumThresholdMillimeters = 5f;

        // 0 for anything that is not a usable width.
        internal static float ValidThreshold(float millimeters) =>
            float.IsNaN(millimeters) || millimeters <= 0f ? 0f : Mathf.Min(millimeters, MaximumThresholdMillimeters);

        // The materials whose outline passes the snapshot switches off.
        internal static HashSet<Material> Omitted(IEnumerable<Renderer> renderers, float thresholdMillimeters)
        {
            var threshold = ValidThreshold(thresholdMillimeters);
            var widths = new Dictionary<Material, float>();
            if (threshold <= 0f) return new HashSet<Material>();
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                var scale = float.NaN;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!Outlined(material)) continue;
                    if (float.IsNaN(scale)) scale = OutlineRendererScale.Scale(renderer);
                    var width = WidthMillimeters(material, scale);
                    widths[material] = widths.TryGetValue(material, out var other) ? Mathf.Max(other, width) : width;
                }
            }
            return new HashSet<Material>(widths.Where(item => item.Value < threshold).Select(item => item.Key));
        }

        // An outline that is not finite counts as wide.
        internal static float WidthMillimeters(Material material, float scale)
        {
            // An outline vector map bends and can lengthen the push; treated as wide.
            if (material.HasProperty("_OutlineVectorTex") && material.GetTexture("_OutlineVectorTex") != null) return float.PositiveInfinity;
            var width = material.GetFloat("_OutlineWidth") * 10f * (float.IsNaN(scale) || scale <= 0f ? 1f : scale);
            return float.IsNaN(width) || float.IsInfinity(width) ? float.PositiveInfinity : Mathf.Max(0f, width);
        }

        static bool Outlined(Material material) =>
            material != null && material.shader != null && material.HasProperty("_OutlineWidth") &&
            LilToon.LilToon234Catalogue.Shaders.ContainsKey(material.shader.name) && LilToonMaterialReader.HasOutline(material);

        internal static bool IsOutlinePass(string name) =>
            string.Equals(name, "FORWARD_OUTLINE", StringComparison.Ordinal) || string.Equals(name, "FORWARD_ADD_OUTLINE", StringComparison.Ordinal);

        internal static string ResultLine(int count, float thresholdMillimeters) => count == 0 ? null :
            string.Format(CultureInfo.InvariantCulture, ExporterLocalization.T(ExportNoteText.ThinOutlinesOmitted), count,
                ValidThreshold(thresholdMillimeters).ToString("0.##", CultureInfo.InvariantCulture));
    }
}
