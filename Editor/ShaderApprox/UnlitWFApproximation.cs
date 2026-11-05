using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // UnlitWF's UnToon family (UnToon, Outline, Mobile, PowerCap, Custom). The
    // generic mapping reads the main image, color, normal map, culling and
    // cutoff, and WfAlpha the alpha. This class reads the features the family
    // keeps under its own prefixes. Its fur, gem, grass and particle shaders
    // draw differently and stay with the generic mapping.
    internal sealed class UnlitWFApproximation : IShaderApproximation
    {
        const string NamePrefix = "UnlitWF/";

        // calcShadowPower weakens the shade by the ambient light, 1 - ambient / 2,
        // which is about this under a moderate ambient of 0.3.
        const float ShadeContrast = .85f;

        static readonly (string Switch, string Label)[] Unsupported =
        {
            ("_BKT_Enable", "Back Face Texture"), ("_CGR_Enable", "Gradient Map"), ("_CLC_Enable", "Color Change"),
            ("_NS_Enable", "Detail Normal"), ("_MT_Enable", "Reflection"), ("_LME_Enable", "Lame"),
            ("_TM_Enable", "Rim Shadow"), ("_TBL_Enable", "Back Light"), ("_AO_Enable", "Occlusion"),
            ("_DFD_Enable", "Distance Fade"), ("_TFG_Enable", "Fog"), ("_DSV_Enable", "Dissolve"),
            ("_LBE_Enable", "Light Bake Effects"), ("_ES_ScrollEnable", "Emission Scroll"), ("_ES_AuLinkEnable", "AudioLink"),
            ("_UseVertexColor", "Vertex Color"),
        };

        // A missing shader is known by the light and shade settings every
        // UnToon shader has and its fur shaders share only with _FUR_Height.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null
                ? values.TryFloat("_GL_LevelMin", out _) && values.TryFloat("_TS_Enable", out _) && !values.TryFloat("_FUR_Height", out _)
                : shaderName.StartsWith(NamePrefix, System.StringComparison.Ordinal) && shaderName.Contains("UnToon");

        // The variants state their mode in the name and queue, as the generic mapping reads.
        public Mode? RenderMode(SourceValues values) => null;

        public bool Outline(SourceValues values) => values.Float("_TL_Enable", 0) > .5f;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            ApplyLight(values, material);
            if (values.Float("_NM_Enable", 0) < .5f) Set(material, "_UseBumpMap", 0);
            ApplyLayer(values, material, textures, dropped);
            ApplyOverlay(values, material, textures, dropped);
            ApplyShade(values, material, textures, dropped);
            ApplyEmission(values, material, dropped);
            ApplyRim(values, material, textures, dropped);
            ApplyMatcap(values, material, textures, dropped, "", "");
            ApplyMatcap(values, material, textures, dropped, "_1", "2nd");
            for (var i = 2; i <= 7; i++)
                if (values.Float("_HL_Enable_" + i, 0) > .5f) { dropped.Add("Extra Matcaps"); break; }
            if (Outline(values)) ApplyOutline(values, material, textures, dropped);
            foreach (var (name, label) in Unsupported)
                if (values.Float(name, 0) > .5f) dropped.Add(label);
        }

        static Color ColorOf(SourceValues values, string name, Color fallback) =>
            values.TryVector(name, out var authored) && Finite(authored) ? (Color)authored : fallback;

        static Texture Image(SourceValues values, string name, out Vector2 scale, out Vector2 offset)
        {
            values.Texture(name, out var image, out scale, out offset);
            return Flat(image) ? image : null;
        }

        // SAMPLE_MASK_VALUE: one channel, inverted under the given switch.
        static Texture Mask(SourceValues values, string name, string invert, int channel, List<Texture2D> textures)
        {
            var mask = Image(values, name, out _, out _);
            if (mask == null) return null;
            var inverted = values.Float(invert, 0) > .5f;
            return !inverted && Grey(mask) ? mask : BakeMask(mask, channel, inverted, textures);
        }

        // calcLightColorFrag: lerp(saturate(power / max), 1, min), desaturated by
        // 1 - _GL_BlendPower. A full light stays 1 whatever the maximum, so
        // lilToon's limit stays at 1. _GL_LevelTweak moves the minimum toward 0 or 1.
        static void ApplyLight(SourceValues values, Material material)
        {
            Set(material, "_AsUnlit", 0);
            var tweak = Mathf.Clamp(values.Float("_GL_LevelTweak", 0), -1, 1);
            var level = Mathf.Clamp01(values.Float("_GL_LevelMin", .125f));
            level = tweak < 0 ? Mathf.Lerp(level, 0, -tweak) : Mathf.Lerp(level, 1, tweak);
            Set(material, "_LightMinLimit", Mathf.GammaToLinearSpace(level));
            Set(material, "_LightMaxLimit", 1);
            Set(material, "_MonochromeLighting", 1 - Mathf.Clamp01(values.Float("_GL_BlendPower", .8f)));
        }

        // blendColor's modes in lilToon's order (0 normal, 1 add, 2 screen, 3
        // multiply), or -1 for the ones it does not have. Mode 3 adds around a
        // middle gray, so the layer is added and the difference reported.
        static int BlendMode(float mode)
        {
            switch (Mathf.RoundToInt(mode))
            {
                case 0: return 0;
                case 1: case 3: return 1;
                case 2: return 3;
                case 4: return 2;
                default: return -1;
            }
        }

        static void SetLayer(Material material, string layer, Texture image, Vector2 scale, Vector2 offset, Color color, int blend, int uvMode, Texture mask)
        {
            Set(material, "_UseMain" + layer + "Tex", 1);
            SetTexture(material, "_Main" + layer + "Tex", image, scale, offset);
            material.SetColor("_Color" + layer, color);
            Set(material, "_Main" + layer + "TexBlendMode", blend);
            Set(material, "_Main" + layer + "Tex_UVMode", uvMode);
            Set(material, "_Main" + layer + "TexIsDecal", 0);
            if (mask != null) SetTexture(material, "_Main" + layer + "BlendMask", mask, Vector2.one, Vector2.zero);
        }

        static void ApplyLayer(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_TX2_Enable", 0) < .5f) return;
            var image = Image(values, "_TX2_MainTex", out var scale, out var offset);
            if (image == null) return;
            var blend = BlendMode(values.Float("_TX2_BlendType", 0));
            if (blend < 0) { dropped.Add("Second Texture Blend"); blend = 0; }
            if (Mathf.RoundToInt(values.Float("_TX2_BlendType", 0)) == 3) dropped.Add("Second Texture Blend");
            SetLayer(material, "2nd", image, scale, offset, ColorOf(values, "_TX2_Color", Color.white), blend,
                Mathf.RoundToInt(values.Float("_TX2_UVType", 0)), Mask(values, "_TX2_MaskTex", "_TX2_InvMaskVal", 0, textures));
        }

        static void ApplyOverlay(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_OVL_Enable", 0) < .5f) return;
            var image = Image(values, "_OVL_OverlayTex", out var scale, out var offset);
            if (image == null) return;
            // UV1, UV2 and the matcap direction have a match; the sky and the
            // angel ring are computed from the view.
            var uv = Mathf.RoundToInt(values.Float("_OVL_UVType", 0));
            if (uv == 2 || uv == 3) { dropped.Add("Overlay Texture"); return; }
            var blend = BlendMode(values.Float("_OVL_BlendType", 0));
            if (blend < 0) { dropped.Add("Overlay Blend"); blend = 0; }
            if (Mathf.RoundToInt(values.Float("_OVL_BlendType", 0)) == 3) dropped.Add("Overlay Blend");
            var color = ColorOf(values, "_OVL_Color", Color.white);
            color.a *= Mathf.Clamp01(values.Float("_OVL_Power", 1));
            // The vertex colors steering it have no match; its mask is one channel.
            SetLayer(material, "3rd", image, scale, offset, color, blend, uv == 4 ? 4 : uv, Mask(values, "_OVL_MaskTex", "_OVL_InvMaskVal", 0, textures));
            var decal = values.Float("_OVL_OutUVType", 0) > .5f;
            if (decal) Set(material, "_Main3rdTexIsDecal", 1);
            if (values.TryVector("_OVL_UVScroll", out var scroll) && Finite(scroll) && (scroll.x != 0 || scroll.y != 0))
            {
                // A decal in lilToon does not scroll.
                if (decal) dropped.Add("Overlay Scroll");
                else material.SetVector("_Main3rdTex_ScrollRotate", new Vector4(scroll.x / 20, scroll.y / 20, 0, 0));
            }
        }

        // drawToonShade works on the same half-Lambert axis as lilToon. Each
        // step is smoothstep(border, border + feather, brightness) toward
        // lerp(1, color × image / base, _TS_Power), which multiplies the color.
        // lilToon's border is the center of the edge.
        static void ApplyShade(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            Reset(material, "_ShadowColorTex", "_Shadow2ndColorTex", "_Shadow3rdColorTex");
            material.SetColor("_Shadow2ndColor", new Color(0, 0, 0, 0));
            material.SetColor("_Shadow3rdColor", new Color(0, 0, 0, 0));
            if (values.Float("_TS_Enable", 0) < .5f) { Set(material, "_UseShadow", 0); return; }
            Set(material, "_UseShadow", 1);
            Set(material, "_ShadowStrength", 1);
            Set(material, "_ShadowBorderRange", 0);
            Set(material, "_ShadowMainStrength", 0);
            Set(material, "_ShadowEnvStrength", 0);
            Set(material, "_ShadowReceive", 0);
            var steps = Mathf.Clamp(Mathf.RoundToInt(values.Float("_TS_Steps", 2)), 1, 3);
            var baseColor = ColorOf(values, "_TS_BaseColor", Color.white).linear;
            var baseImage = Image(values, "_TS_BaseTex", out _, out _);
            var power = Mathf.Max(0, values.Float("_TS_Power", 1)) * ShadeContrast;
            var normal = Mathf.Clamp01(values.Float("_TS_BlendNormal", .1f));
            Set(material, "_ShadowNormalStrength", normal);
            Set(material, "_Shadow2ndNormalStrength", normal);
            Set(material, "_Shadow3rdNormalStrength", normal);
            ShadeStep(values, material, textures, "1st", "", baseColor, baseImage, power, .4f, new Color(.81f, .81f, .9f));
            if (steps >= 2) ShadeStep(values, material, textures, "2nd", "2nd", baseColor, baseImage, power, .2f, new Color(.68f, .68f, .8f));
            if (steps >= 3) ShadeStep(values, material, textures, "3rd", "3rd", baseColor, baseImage, power, .1f, new Color(.595f, .595f, .7f));
            // A baked image replaces the color, with its layers, in the shade.
            var bakedShade = material.GetTexture("_ShadowColorTex") != null || material.GetTexture("_Shadow2ndColorTex") != null || material.GetTexture("_Shadow3rdColorTex") != null;
            if (bakedShade && (material.GetFloat("_UseMain2ndTex") > .5f || material.GetFloat("_UseMain3rdTex") > .5f))
                dropped.Add("Layers In Shade");

            // The mask pulls brightness toward 1, which weakens the shade there.
            // Its signed distance field mode reads the mask in another way.
            if (values.Float("_TS_MaskType", 0) > .5f) dropped.Add("Shade SDF");
            else
            {
                var anti = Image(values, "_TS_MaskTex", out _, out _);
                if (anti != null)
                {
                    var strength = BakeMask(anti, 0, values.Float("_TS_InvMaskVal", 0) < .5f, textures);
                    if (strength != null) SetTexture(material, "_ShadowStrengthMask", strength, Vector2.one, Vector2.zero);
                    else dropped.Add("Shade Mask");
                }
            }
        }

        static void ShadeStep(SourceValues values, Material material, List<Texture2D> textures, string source, string target, Color baseColor, Texture baseImage, float power, float border, Color fallback)
        {
            var step = "_TS_" + source;
            var feather = Mathf.Max(.001f, values.Float(step + "Feather", .05f));
            var name = "_Shadow" + target;
            Set(material, name + "Border", Mathf.Clamp01(values.Float(step + "Border", border) + feather / 2));
            Set(material, name + "Blur", Mathf.Clamp(feather, .001f, 1));
            var color = ColorOf(values, step + "Color", fallback).linear;
            var image = Image(values, step + "Tex", out _, out _);
            Color Factor(Color shade, Color under) => new Color(
                Mathf.Max(0, 1 + (color.r * shade.r / Mathf.Max(under.r, .001f) - 1) * power),
                Mathf.Max(0, 1 + (color.g * shade.g / Mathf.Max(under.g, .001f) - 1) * power),
                Mathf.Max(0, 1 + (color.b * shade.b / Mathf.Max(under.b, .001f) - 1) * power));

            if (image != null || baseImage != null)
            {
                var baked = BakeShade(material, textures, image, baseImage, baseColor, Factor, name);
                if (baked != null)
                {
                    SetTexture(material, name + "ColorTex", baked, Vector2.one, Vector2.zero);
                    var tint = material.GetColor("_Color");
                    material.SetColor(name + "Color", new Color(tint.r, tint.g, tint.b, 1));
                    return;
                }
            }
            var flat = Factor(Color.white, baseColor).gamma;
            material.SetColor(name + "Color", new Color(flat.r, flat.g, flat.b, 1));
        }

        // lilToon's shadow image replaces the color, so the image holds the
        // main image times the step's factor at every pixel.
        static Texture2D BakeShade(Material material, List<Texture2D> textures, Texture image, Texture baseImage, Color baseColor, System.Func<Color, Color, Color> factor, string name)
        {
            var main = material.GetTexture("_MainTex");
            var origin = main != null ? main : image != null ? image : baseImage;
            var width = Mathf.Min(origin.width, 1024); var height = Mathf.Min(origin.height, 1024);
            var mainPixels = main != null ? Sampled(main, width, height) : null;
            var shadePixels = image != null ? Sampled(image, width, height) : null;
            var basePixels = baseImage != null ? Sampled(baseImage, width, height) : null;
            if (main != null && mainPixels == null || image != null && shadePixels == null || baseImage != null && basePixels == null) return null;
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                var under = basePixels != null ? baseColor * basePixels[i] : baseColor;
                var multiplier = factor(shadePixels != null ? shadePixels[i] : Color.white, under);
                var color = mainPixels != null ? mainPixels[i] : Color.white;
                var shown = new Color(color.r * multiplier.r, color.g * multiplier.g, color.b * multiplier.b).gamma;
                pixels[i] = new Color(shown.r, shown.g, shown.b, 1);
            }
            return NewImage(origin, "__VRVlog" + name, width, height, pixels, textures, false);
        }

        // Only under _ES_Enable. _ES_BlendType 0 adds; the other two blend by
        // the map, which adding comes close to on a bright map. The map shares
        // the main image's coordinates.
        static void ApplyEmission(SourceValues values, Material material, ICollection<string> dropped)
        {
            Reset(material, "_UseEmission", "_EmissionColor", "_EmissionMap", "_EmissionMainStrength", "_EmissionBlendMode", "_EmissionBlink");
            if (values.Float("_ES_Enable", 0) < .5f) return;
            var color = ColorOf(values, "_EmissionColor", Color.white);
            if (new Color(color.r, color.g, color.b).maxColorComponent <= 0) return;
            if (values.Float("_ES_BlendType", 0) > .5f) dropped.Add("Emission Blend");
            Set(material, "_UseEmission", 1);
            material.SetColor("_EmissionColor", new Color(color.r, color.g, color.b, 1));
            Set(material, "_EmissionMainStrength", Mathf.Clamp01(values.Float("_ES_TintBaseCol", 0)));
            Set(material, "_EmissionBlendMode", 1);
            var map = Image(values, "_EmissionMap", out _, out _);
            if (map != null) SetTexture(material, "_EmissionMap", map, material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
        }

        // calcRimLightPower ramps over s = sin(angle to the view), from
        // 1 - (width + feather) d to 1 - width d, where d depends on the
        // direction on screen; the top, sides and bottom are averaged. lilToon
        // ramps over 1 - N·V = 1 - sqrt(1 - s²). The light direction scales it
        // by saturate(0.8 - angle between light and view on the ground): 0.8
        // with the switch that turns that off, 0.6 on average otherwise.
        static void ApplyRim(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_TR_Enable", 0) < .5f) return;
            var type = Mathf.RoundToInt(values.Float("_TR_BlendType", 0));
            var authored = ColorOf(values, "_TR_Color", new Color(.8f, .8f, .8f));
            var color = authored.linear;
            var blend = 1;
            if (type == 0)
            {
                var median = Mathf.GammaToLinearSpace(.5f);
                if (color.r < median || color.g < median || color.b < median) dropped.Add("Rim Light (Darkening)");
                color = new Color(Mathf.Max(0, color.r - median), Mathf.Max(0, color.g - median), Mathf.Max(0, color.b - median));
            }
            else if (type == 1) blend = 0;

            var width = Mathf.Max(0, values.Float("_TR_Width", .1f)) * .1f;
            var feather = Mathf.Max(0, values.Float("_TR_Feather", .05f));
            var direction = values.Float("_TR_WidthSide", 1) + (values.Float("_TR_WidthTop", .5f) + values.Float("_TR_WidthBottom", 1)) / 2;
            var low = Mathf.Clamp01(1 - (width + feather) * direction);
            var high = Mathf.Clamp01(1 - width * direction);
            // pow(ramp, exponent) reaches one half later on the ramp.
            var center = Mathf.Lerp(low, high, Mathf.Pow(.5f, 1 / Mathf.Max(1, values.Float("_TR_Exponent", 1))));
            float Ramp(float sine) => 1 - Mathf.Sqrt(1 - sine * sine);
            var strength = values.Float("_TR_DisableBackLit", 0) > .5f ? .8f : .6f;
            var shown = color.gamma;

            Set(material, "_UseRim", 1);
            Set(material, "_RimFresnelPower", 1);
            Set(material, "_RimBorder", Mathf.Clamp01(Ramp(center)));
            Set(material, "_RimBlur", Mathf.Clamp(Ramp(high) - Ramp(low), .001f, 1));
            material.SetColor("_RimColor", new Color(shown.r, shown.g, shown.b, strength));
            Set(material, "_RimBlendMode", blend);
            Set(material, "_RimMainStrength", Mathf.Clamp01(values.Float("_TR_TintBaseCol", 0)));
            Set(material, "_RimEnableLighting", 1);
            Set(material, "_RimShadowMask", 0);
            Set(material, "_RimNormalStrength", Mathf.Clamp01(values.Float("_TR_BlendNormal", 0)));
            var mask = Mask(values, "_TR_MaskTex", "_TR_InvMaskVal", 4, textures);
            if (mask != null) SetTexture(material, "_RimColorTex", mask, Vector2.one, Vector2.zero);
        }

        // calcMatcapColor. Types 1 and 2 add or multiply the image tinted by
        // LinearToGamma(tint × 2). Type 0, the default, adds image - median:
        // tint × 2 of what is above the median and the rest of what is below.
        // lilToon's blend goes one way, so the part above adds here and the
        // part below multiplies in the second matcap, when the material leaves
        // that free.
        static void ApplyMatcap(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped, string source, string target)
        {
            if (values.Float("_HL_Enable" + source, 0) < .5f) return;
            var image = Image(values, "_HL_MatcapTex" + source, out _, out _);
            if (image == null) return;
            var type = Mathf.RoundToInt(values.Float("_HL_CapType" + source, 0));
            var tint = ColorOf(values, "_HL_MatcapColor" + source, new Color(.5f, .5f, .5f)).linear * 2;
            var monochrome = Mathf.Clamp01(values.Float("_HL_MatcapMonochrome" + source, 0));
            var power = Mathf.Max(0, values.Float("_HL_Power" + source, 1));
            var mask = Mask(values, "_HL_MaskTex" + source, "_HL_InvMaskVal" + source, 4, textures);
            var median = type == 0 ? ColorOf(values, "_HL_MedianColor" + source, new Color(.5f, .5f, .5f)).linear : Color.black;
            Texture shown = image;
            if (type == 0 || monochrome > 0)
            {
                var baked = MatcapImage(image, median, Color.white, monochrome, type == 0, false, textures);
                if (baked != null) shown = baked;
                else if (type == 0) { dropped.Add("Matcap"); return; }
            }
            if (type != 0) tint = tint.gamma;
            // Multiplying above a power of 1 darkens past the matcap, which a blend of 1 is the nearest to.
            if (type == 2 && power > 1) dropped.Add("Matcap Darkening");
            SetMatcap(material, target, shown, (new Color(tint.r, tint.g, tint.b) * (type == 2 ? 1 : Mathf.Max(1, power))).gamma, Mathf.Min(1, power), type == 2 ? 3 : 1, mask);
            if (type != 0) return;

            var rest = new Color(Mathf.Clamp01(1 - tint.r), Mathf.Clamp01(1 - tint.g), Mathf.Clamp01(1 - tint.b)) * power;
            var darkening = MatcapImage(image, median, rest, monochrome, true, true, textures);
            if (darkening == null) return;
            if (source == "" && values.Float("_HL_Enable_1", 0) < .5f) SetMatcap(material, "2nd", darkening, Color.white, 1, 3, mask);
            else dropped.Add("Matcap Darkening");
        }

        static void SetMatcap(Material material, string target, Texture image, Color color, float blend, int mode, Texture mask)
        {
            var name = "_MatCap" + target;
            Set(material, "_UseMatCap" + target, 1);
            SetTexture(material, name + "Tex", image, Vector2.one, Vector2.zero);
            material.SetColor(name + "Color", new Color(color.r, color.g, color.b, 1));
            Set(material, name + "Blend", blend);
            Set(material, name + "BlendMode", mode);
            Set(material, name + "MainStrength", 0);
            // A multiplier is no light of its own for the scene to dim.
            Set(material, name + "EnableLighting", mode == 3 ? 0 : 1);
            // lilToon draws it before the shade, WF after.
            Set(material, name + "ShadowMask", 1);
            if (mask != null) SetTexture(material, name + "BlendMask", mask, Vector2.one, Vector2.zero);
        }

        // Without below, the image minus the median, or the image as it is.
        // With below, what multiplies an average channel of 0.5 by as much as
        // the source subtracts, or null when nothing lies below the median.
        static Texture2D MatcapImage(Texture image, Color median, Color tint, float monochrome, bool signed, bool below, List<Texture2D> textures)
        {
            var width = Mathf.Min(image.width, 512); var height = Mathf.Min(image.height, 512);
            var pixels = Sampled(image, width, height);
            if (pixels == null) return null;
            var found = false;
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = pixels[i];
                var average = (color.r + color.g + color.b) / 3;
                color = Color.Lerp(color, new Color(average, average, average), monochrome);
                if (below)
                {
                    var r = Mathf.Max(0, median.r - color.r) * tint.r; var g = Mathf.Max(0, median.g - color.g) * tint.g; var b = Mathf.Max(0, median.b - color.b) * tint.b;
                    found |= r > .005f || g > .005f || b > .005f;
                    color = new Color(Mathf.Clamp01(1 - 2 * r), Mathf.Clamp01(1 - 2 * g), Mathf.Clamp01(1 - 2 * b));
                }
                else if (signed) color = new Color(Mathf.Max(0, color.r - median.r), Mathf.Max(0, color.g - median.g), Mathf.Max(0, color.b - median.b));
                var shown = color.gamma;
                pixels[i] = new Color(shown.r, shown.g, shown.b, 1);
            }
            return below && !found ? null : NewImage(image, below ? "__VRVlog_MatcapDark" : "__VRVlog_Matcap", width, height, pixels, textures, false);
        }

        // The line moves _TL_LineWidth centimeters along the normal, as
        // lilToon's does. Its color is lerp(lerp(color, image, custom), main, base).
        static void ApplyOutline(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { dropped.Add("Outline"); return; }
            Set(material, "_OutlineWidth", Mathf.Max(0, values.Float("_TL_LineWidth", .05f)));
            Set(material, "_OutlineFixWidth", 0);
            Set(material, "_OutlineEnableLighting", 1);
            if (values.Float("_TL_LineType", 0) > .5f) dropped.Add("Outline Type");
            var color = ColorOf(values, "_TL_LineColor", new Color(.1f, .1f, .1f));
            var main = material.GetTexture("_MainTex");
            var custom = Mathf.Clamp01(values.Float("_TL_BlendCustom", 0));
            var mix = Mathf.Clamp01(values.Float("_TL_BlendBase", 0));
            var image = custom > 0 ? Image(values, "_TL_CustomColorTex", out _, out _) : null;
            // Holes and fading follow the main image's alpha; lilToon reads the
            // line's alpha from the line's own image.
            var withAlpha = material.GetFloat("_TransparentMode") != 0;
            var wanted = image != null || main != null && (mix > 0 || withAlpha);
            var baked = wanted ? OutlineImage(main, image, color.linear, custom, mix, material.GetColor("_Color").linear, withAlpha, textures) : null;
            if (baked != null)
            {
                SetTexture(material, "_OutlineTex", baked, material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                material.SetColor("_OutlineColor", new Color(1, 1, 1, color.a));
            }
            else
            {
                if (wanted) dropped.Add("Outline Color Image");
                material.SetColor("_OutlineColor", color);
            }
            var mask = Mask(values, "_TL_MaskTex", "_TL_InvMaskVal", 0, textures);
            if (mask != null) SetTexture(material, "_OutlineWidthMask", mask, Vector2.one, Vector2.zero);
        }

        static Texture2D OutlineImage(Texture main, Texture image, Color line, float custom, float mix, Color tint, bool withAlpha, List<Texture2D> textures)
        {
            var origin = main != null ? main : image;
            var width = Mathf.Min(origin.width, 1024); var height = Mathf.Min(origin.height, 1024);
            var mainPixels = main != null ? Sampled(main, width, height) : null;
            var imagePixels = image != null ? Sampled(image, width, height) : null;
            if (main != null && mainPixels == null || image != null && imagePixels == null) return null;
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = imagePixels != null ? Color.Lerp(line, imagePixels[i], custom) : line;
                if (mainPixels != null) color = Color.Lerp(color, mainPixels[i] * tint, mix);
                var shown = color.gamma;
                pixels[i] = new Color(shown.r, shown.g, shown.b, withAlpha && mainPixels != null ? mainPixels[i].a : 1);
            }
            return NewImage(origin, "__VRVlog_Outline", width, height, pixels, textures, false);
        }
    }
}
