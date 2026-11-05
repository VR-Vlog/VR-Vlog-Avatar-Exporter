using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using Mode = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.Mode;
using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;

namespace VRVlog.LilToonExporter
{
    // NonToon and NonToonFur. The generic mapping reads the main image
    // (_BaseTexture, marked as the main texture), the normal map (_NormalMap,
    // _NormalScale) and _Cull. This class reads the render mode (_RenderingMode),
    // the cutoff that the transparent mode also clips at, and the outline.
    internal sealed class NonToonApproximation : IShaderApproximation
    {
        // A missing shader is known by a property only these shaders have.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null ? values.TryFloat("_NTShadowBias", out _) : shaderName == "NonToon" || shaderName == "NonToonFur";

        // _RenderingMode: 0 opaque, 1 cutout, 2 transparent. NonToonFur has no
        // such property and draws its base opaque.
        public Mode? RenderMode(SourceValues values)
        {
            switch (values.Float("_RenderingMode", 0))
            {
                case 1: return Mode.Cutout;
                case 2: return Mode.Transparent;
                default: return Mode.Opaque;
            }
        }

        // NonToon draws its outline whenever the width is not zero.
        public bool Outline(SourceValues values) => values.TryVector("_OutlineColor", out _) && values.Float("_OutlineWidth", 0) > 0;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            // The transparent mode blends and also clips at _Cutoff.
            if (values.Float("_RenderingMode", 0) == 2 && values.TryFloat("_Cutoff", out var cutoff))
                UnsupportedShaderSubstitution.Set(material, "_Cutoff", Mathf.Clamp01(cutoff));

            // With _NormalMapWithRoughness the image keeps roughness next to the
            // normal, so it is not a normal map lilToon can read.
            if (values.Float("_NormalMapWithRoughness", 0) > .5f && material.GetTexture("_BumpMap") != null)
            {
                material.SetTexture("_BumpMap", null);
                UnsupportedShaderSubstitution.Set(material, "_UseBumpMap", 0);
                dropped.Add("Normal Map With Roughness");
            }

            if (values.TryFloat("_FurNoiseTiling", out _)) dropped.Add("Fur");
            if (Outline(values)) ApplyOutline(values, material, dropped);
        }

        // Both shaders take the width in centimeters along the normal. NonToon
        // multiplies the lit color by the outline color, so lilToon draws the
        // main image under it.
        static void ApplyOutline(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { dropped.Add("Outline"); return; }
            UnsupportedShaderSubstitution.Set(material, "_OutlineWidth", values.Float("_OutlineWidth", 0));
            UnsupportedShaderSubstitution.Set(material, "_OutlineFixWidth", 0);
            UnsupportedShaderSubstitution.Set(material, "_OutlineEnableLighting", 1);
            values.TryVector("_OutlineColor", out var color);
            material.SetColor("_OutlineColor", Finite(color) ? (Color)color : Color.white);
            if (material.HasProperty("_OutlineTex"))
            {
                material.SetTexture("_OutlineTex", material.GetTexture("_MainTex"));
                material.SetTextureScale("_OutlineTex", material.GetTextureScale("_MainTex"));
                material.SetTextureOffset("_OutlineTex", material.GetTextureOffset("_MainTex"));
            }
            if (values.Float("_OutlineFromVertexColor", 0) > .5f) dropped.Add("Outline From Vertex Color");
            if (values.Float("_OutlineZOffset", 0) != 0) dropped.Add("Outline Z Offset");
        }
    }
}
