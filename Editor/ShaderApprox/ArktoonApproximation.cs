using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Arktoon. The generic mapping reads the main image and color, normal map,
    // emission, and transparency (Alpha.cs); this class replaces its shade and
    // adds rim, the two matcaps and outline.
    internal sealed class ArktoonApproximation : IShaderApproximation
    {
        // Every variant declares both; a missing shader keeps them in its
        // saved values.
        public bool Matches(string shaderName, SourceValues values) =>
            values.TryFloat("_Shadowborder", out _) && values.TryFloat("_ShadowborderBlur", out _);

        // The variant is in the shader's name and in the queue, which the
        // generic reading takes.
        public Mode? RenderMode(SourceValues values) => null;

        public bool Outline(SourceValues values) =>
            values.Float("_UseOutline", 0) > .5f && values.Float("_OutlineWidth", 0) > 0;

        // Double-sided parts light their back faces at a lower intensity, which
        // lilToon draws as that much of the shade.
        static void Backface(SourceValues values, Material material)
        {
            if (values.Float("_UseDoubleSided", 0) <= .5f) return;
            Set(material, "_FlipNormal", values.Float("_DoubleSidedFlipBackfaceNormal", 0) > .5f ? 1 : 0);
            Set(material, "_BackfaceForceShadow", 1 - Mathf.Clamp01(values.Float("_DoubleSidedBackfaceLightIntensity", .5f)));
        }

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            Backface(values, material);
            Shade(values, material, textures, dropped);
            Rim(values, material, textures, dropped);
            Matcap(values, material, textures, dropped);
            ShadowCap(values, material, textures, dropped);
            if (Outline(values)) ApplyOutline(values, material, textures, dropped);
            if (values.Float("_UseGloss", 0) > .5f) Drop(dropped, "Gloss");
            if (values.Float("_UseReflection", 0) > .5f) Drop(dropped, "Reflection");
            if (values.Float("_UseEmissionParallax", 0) > .5f) Drop(dropped, "Emission Parallax");
            if (Opaque(values, "_EmissiveFreak1Color", Color.black).maxColorComponent > 0 ||
                Opaque(values, "_EmissiveFreak2Color", Color.black).maxColorComponent > 0) Drop(dropped, "Emissive Freak");
            if (values.Float("_RefractionStrength", 0) != 0) Drop(dropped, "Refraction");
            if (values.Float("_VertexColorBlendDiffuse", 0) > 0) Drop(dropped, "Vertex Color");
        }

        // The shader's light is half-Lambert with its edge centered on the
        // border and the blur the full width, as lilToon's is. In the shade the
        // light falls to the indirect intensity by the strength, which is
        // lilToon's lerp(1, color, strength) over the albedo.
        static void Shade(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var border = Mathf.Clamp01(values.Float("_Shadowborder", .6f));
            var strength = Mathf.Clamp01(values.Float("_ShadowStrength", .5f));
            var indirect = Mathf.Clamp01(values.Float("_ShadowIndirectIntensity", .25f));
            Set(material, "_UseShadow", 1);
            Set(material, "_ShadowStrength", strength);
            Set(material, "_ShadowBorderRange", 0);
            Set(material, "_ShadowBorder", border);
            Set(material, "_ShadowBlur", Mathf.Clamp(values.Float("_ShadowborderBlur", .05f), .001f, 1));
            Set(material, "_ShadowNormalStrength", 1);
            Set(material, "_Shadow2ndNormalStrength", 1);
            Set(material, "_ShadowReceive", 1);
            Set(material, "_Shadow2ndReceive", 1);
            material.SetTexture("_ShadowColorTex", null);
            CopyMask(values, material, "_ShadowStrengthMask", "", "", "_ShadowStrengthMask", "", textures, dropped, "Shadow Strength Mask");
            CopyMask(values, material, "_ShadowborderBlurMask", "", "", "_ShadowBlurMask", "", textures, dropped, "Shadow Blur Mask");
            if (values.Float("_ShadowUseStep", 0) > .5f) Drop(dropped, "Shadow Steps");
            material.SetColor("_Shadow2ndColor", Color.clear);

            if (values.Float("_ShadowPlanBUsePlanB", 0) < .5f)
            {
                material.SetColor("_ShadowColor", Gray(indirect));
                return;
            }

            // Plan B replaces the shade with an image or a shifted base color,
            // darkened toward the default shade by the mix.
            var mix = Mathf.Lerp(1, 1 - strength * (1 - indirect), Mathf.Clamp01(values.Float("_ShadowPlanBDefaultShadowMix", 1)));
            Layer(values, material, "_ShadowPlanBUseCustomShadowTexture", "_ShadowPlanBCustomShadowTexture", "_ShadowPlanBHueShiftFromBase",
                "_ShadowPlanBSaturationFromBase", "_ShadowPlanBValueFromBase", "_ShadowColor", "_ShadowColorTex", mix, textures, dropped);
            if (values.Float("_CustomShadow2nd", 0) < .5f) return;
            Set(material, "_Shadow2ndBorder", Mathf.Clamp01(values.Float("_ShadowPlanB2border", .55f) * border));
            Set(material, "_Shadow2ndBlur", Mathf.Clamp(values.Float("_ShadowPlanB2borderBlur", .55f), .001f, 1));
            Layer(values, material, "_ShadowPlanB2UseCustomShadowTexture", "_ShadowPlanB2CustomShadowTexture", "_ShadowPlanB2HueShiftFromBase",
                "_ShadowPlanB2SaturationFromBase", "_ShadowPlanB2ValueFromBase", "_Shadow2ndColor", "_Shadow2ndColorTex", mix, textures, dropped);
        }

        static void Layer(SourceValues values, Material material, string useImage, string image, string hue, string saturation, string value,
            string colorName, string textureName, float mix, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float(useImage, 0) > .5f)
            {
                var tint = Opaque(values, image + "RGB", Color.white).linear * mix;
                // The empty slot is black.
                if (values.Texture(image, out var map, out var scale, out var offset) && Flat(map) && map != null)
                {
                    ShadeLayers.ReportOwnUv(material, scale, offset, dropped, "Shade Map Tiling");
                    SetTexture(material, textureName, ShadeLayers.WithoutAlpha(map, textures), scale, offset);
                }
                else tint = Color.black;
                tint.a = 1;
                material.SetColor(colorName, tint.gamma);
                return;
            }
            var shift = values.Float(hue, 0);
            var saturationScale = values.Float(saturation, 1);
            var level = Mathf.Max(0, values.Float(value, 1)) * mix;
            var baked = Mathf.Abs(shift) > 1e-4f || Mathf.Abs(saturationScale - 1) > 1e-4f ? Shifted(material, shift, saturationScale, textures) : null;
            if (baked != null) { material.SetTexture(textureName, baked); material.SetColor(colorName, Gray(level)); return; }
            if (Mathf.Abs(shift) > 1e-4f || Mathf.Abs(saturationScale - 1) > 1e-4f) Drop(dropped, "Plan B Color Shift");
            material.SetColor(colorName, Gray(level));
        }

        // The base image and color through the shader's color shift, as a
        // shade image in the base image's UV space.
        static Texture Shifted(Material material, float shift, float saturation, List<Texture2D> textures)
        {
            var main = material.GetTexture("_MainTex");
            if (main == null || textures == null) return null;
            var fit = Mathf.Min(1, 1024f / Mathf.Max(main.width, main.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(main.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(main.height * fit));
            var pixels = Sampled(main, width, height);
            if (pixels == null) return null;
            var tint = material.GetColor("_Color").linear;
            for (var i = 0; i < pixels.Length; i++)
            {
                var diffuse = pixels[i] * tint;
                Color.RGBToHSV(diffuse, out var h, out var s, out var v);
                var hue = Color.HSVToRGB(Mathf.Repeat(h + shift, 1), 1, 1);
                var shifted = Color.LerpUnclamped(Color.white, hue, s * saturation) * v;
                shifted.a = 1;
                pixels[i] = new Color(Mathf.Clamp01(shifted.r), Mathf.Clamp01(shifted.g), Mathf.Clamp01(shifted.b), 1).gamma;
            }
            return NewImage(main, "__VRVlog_ShiftedShade", width, height, pixels, textures, false);
        }

        // pow(1 - N·V, power) with no edge of its own, added to the emission.
        static void Rim(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_UseRim", 0) < .5f) return;
            Set(material, "_UseRim", 1);
            var useBase = values.Float("_RimUseBaseTexture", 0) > .5f;
            var shadeMix = Mathf.Clamp01(values.Float("_RimShadeMix", 0));
            material.SetColor("_RimColor", Scaled(Opaque(values, "_RimColor", Color.white), values.Float("_RimBlend", 1)));
            Set(material, "_RimFresnelPower", Mathf.Clamp(values.Float("_RimFresnelPower", 1), .01f, 50));
            Set(material, "_RimBorder", .5f);
            Set(material, "_RimBlur", 1);
            Set(material, "_RimBlendMode", 1);
            Set(material, "_RimMainStrength", useBase ? 1 : 0);
            Set(material, "_RimEnableLighting", shadeMix);
            Set(material, "_RimShadowMask", shadeMix);
            Set(material, "_RimNormalStrength", 1);
            // lilToon has one image for both the rim's image and its mask.
            var hasMask = values.Texture("_RimBlendMask", out var mask, out _, out _) && Flat(mask) && mask != null;
            if (!useBase && values.Texture("_RimTexture", out var image, out var scale, out var offset) && Flat(image) && image != null)
            {
                SetTexture(material, "_RimColorTex", image, scale, offset);
                if (hasMask) Drop(dropped, "Rim Blend Mask");
            }
            else if (hasMask) CopyMask(values, material, "_RimBlendMask", "", "", "_RimColorTex", "_UseRim", textures, dropped, "Rim Blend Mask");
            if (values.Float("_RimUpperSideWidth", 0) > 0) Drop(dropped, "Rim Upper Width");
        }

        // Add, Lighten, Screen, off. lilToon has no lighten; screen does not
        // pass white either.
        static void Matcap(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var mode = Mathf.RoundToInt(values.Float("_MatcapBlendMode", 3));
            if (mode < 0 || mode > 2) return;
            if (!values.Texture("_MatcapTexture", out var image, out var scale, out var offset) || !Flat(image) || image == null) return;
            var shadeMix = Mathf.Clamp01(values.Float("_MatcapShadeMix", 0));
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", image, scale, offset);
            material.SetColor("_MatCapColor", Scaled(Opaque(values, "_MatcapColor", Color.white), values.Float("_MatcapBlend", 1)));
            Set(material, "_MatCapBlend", 1);
            Set(material, "_MatCapBlendMode", mode == 0 ? 1 : 2);
            Set(material, "_MatCapMainStrength", 0);
            Set(material, "_MatCapEnableLighting", shadeMix);
            Set(material, "_MatCapShadowMask", shadeMix);
            Set(material, "_MatCapNormalStrength", Mathf.Clamp01(values.Float("_MatcapNormalMix", 1)));
            CopyMask(values, material, "_MatcapBlendMask", "", "", "_MatCapBlendMask", "_UseMatCap", textures, dropped, "Matcap Blend Mask");
        }

        // 1 - mask × (1 - image) × blend multiplies the color in the darken and
        // multiply modes, and the light in light shutter: all of them are
        // lilToon's multiplying second matcap.
        static void ShadowCap(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var mode = Mathf.RoundToInt(values.Float("_ShadowCapBlendMode", 3));
            if (mode < 0 || mode > 2) return;
            if (!values.Texture("_ShadowCapTexture", out var image, out var scale, out var offset) || !Flat(image) || image == null) return;
            Set(material, "_UseMatCap2nd", 1);
            SetTexture(material, "_MatCap2ndTex", image, scale, offset);
            material.SetColor("_MatCap2ndColor", Color.white);
            Set(material, "_MatCap2ndBlend", Mathf.Clamp01(values.Float("_ShadowCapBlend", 1)));
            Set(material, "_MatCap2ndBlendMode", 3);
            Set(material, "_MatCap2ndMainStrength", 0);
            Set(material, "_MatCap2ndEnableLighting", 0);
            Set(material, "_MatCap2ndShadowMask", 0);
            Set(material, "_MatCap2ndNormalStrength", Mathf.Clamp01(values.Float("_ShadowCapNormalMix", 1)));
            CopyMask(values, material, "_ShadowCapBlendMask", "", "", "_MatCap2ndBlendMask", "_UseMatCap2nd", textures, dropped, "Shadow Cap Blend Mask");
        }

        // The width is in centimeters in both. The source draws
        // base × rate + color × image × (1 - rate). lilToon draws one image, so
        // the larger side of the rate gives it, and the color follows the rate.
        static void ApplyOutline(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { Drop(dropped, "Outline"); return; }
            Set(material, "_OutlineWidth", values.Float("_OutlineWidth", 0));
            Set(material, "_OutlineFixWidth", 0);
            Set(material, "_OutlineEnableLighting", Mathf.Clamp01(values.Float("_OutlineShadeMix", 0)));
            var color = Opaque(values, "_OutlineColor", Color.black);
            var rate = Mathf.Clamp01(values.Float("_OutlineTextureColorRate", 0));
            material.SetTexture("_OutlineTex", null);
            var hasImage = values.Texture("_OutlineTexture", out var image, out var scale, out var offset) && Flat(image) && image != null;
            if (rate >= .5f)
            {
                SetTexture(material, "_OutlineTex", material.GetTexture("_MainTex"), material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                if (hasImage) Drop(dropped, "Outline Image");
            }
            else if (hasImage) SetTexture(material, "_OutlineTex", image, scale, offset);
            color = Color.Lerp(color.linear, material.GetColor("_Color").linear, rate).gamma;
            color.a = 1;
            material.SetColor("_OutlineColor", color);
            CopyMask(values, material, "_OutlineWidthMask", "", "", "_OutlineWidthMask", "", textures, dropped, "Outline Width Mask");
            if (values.Float("_OutlineUseColorShift", 0) > .5f) Drop(dropped, "Outline Color Shift");
        }

        static Color Gray(float linear)
        {
            var color = new Color(linear, linear, linear, 1).gamma;
            color.a = 1;
            return color;
        }

        static Color Scaled(Color color, float scale)
        {
            var scaled = (color.linear * Mathf.Max(0, scale)).gamma;
            scaled.a = 1;
            return scaled;
        }

        static Color Opaque(SourceValues values, string name, Color fallback)
        {
            var color = values.TryVector(name, out var vector) && Finite(vector) ? (Color)vector : fallback;
            color.a = 1;
            return color;
        }
    }
}
