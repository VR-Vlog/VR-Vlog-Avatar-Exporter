using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using Mode = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.Mode;
using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;
using S = VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Silent's Cel Shading: Crosstone and Lightramp, each with or without an
    // outline (and a fur variant). The generic mapping reads the main image and
    // color, the cutoff, the normal map and _CullMode. This class reads the
    // rendering mode, the alpha source, the color adjustment, the shading
    // (Crosstone's two tones or Lightramp's ramp), the outline, the rim light,
    // the first two matcaps, both emissions and the specular, and reports the
    // effects lilToon has no counterpart for.
    // All positions are on the half-Lambert axis N·L × 0.5 + 0.5, the same as
    // lilToon's shadow border.
    internal sealed class SilentCelShadingApproximation : IShaderApproximation
    {
        const string Prefix = "Silent's Cel Shading/";

        // A missing shader is known by its two lighting settings, which no other
        // shader the exporter knows declares.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null
                ? values.TryFloat("_LightingCalculationType", out _) && values.TryFloat("_IndirectShadingType", out _)
                : shaderName.StartsWith(Prefix, System.StringComparison.Ordinal);

        // _Mode: 0 opaque, 1 cutout, 2 fade, 3 premultiplied transparent, 4
        // additive, 5 custom with _CustomMode (0 opaque, 1 cutout, 2 fade, 3
        // premultiplied). lilToon has one transparent mode.
        public Mode? RenderMode(SourceValues values)
        {
            if (!values.TryFloat("_Mode", out var mode)) return null;
            var index = Mathf.RoundToInt(mode);
            if (index == 5) index = Mathf.RoundToInt(values.Float("_CustomMode", 0));
            return index >= 2 ? Mode.Transparent : index == 1 ? Mode.Cutout : Mode.Opaque;
        }

        // Only the "(Outline)" shaders draw it; a missing shader's name is not
        // known, so it gets none. _OutlineMode 0 is none.
        public bool Outline(SourceValues values) =>
            values.ShaderName != null && values.ShaderName.Contains("(Outline)") && values.Float("_OutlineMode", 1) >= .5f;

        // The outline and fur variants share their family's properties, so the
        // variant is read from the name; a missing shader is Lightramp when it
        // has a ramp type.
        static bool Crosstone(SourceValues values) =>
            values.ShaderName != null ? values.ShaderName.StartsWith(Prefix + "Crosstone", System.StringComparison.Ordinal)
                : !values.TryFloat("_LightRampType", out _);

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (Mathf.RoundToInt(values.Float("_Mode", 0)) == 4) S.Drop(dropped, "Additive");
            ApplyAlpha(values, material, textures, dropped);
            ApplyColor(values, material, textures, dropped);
            // The source turns the normal toward the viewer on back faces, so a
            // double-sided back face is lit like its front.
            S.Set(material, "_FlipNormal", 1);
            if (Crosstone(values)) ApplyCrosstone(values, material, textures, dropped);
            else ApplyLightramp(values, material, dropped);
            if (Outline(values)) ApplyOutline(values, material, dropped);
            ApplyRim(values, material, textures, dropped);
            ApplyMatcap(values, material, "1", "", 0, textures, dropped);
            ApplyMatcap(values, material, "2", "2nd", 1, textures, dropped);
            ApplyEmission(values, material, "", "", "_EMISSION", dropped);
            ApplyEmission(values, material, "2nd", "2nd", "_EMISSION_2ND", dropped);
            ApplySpecular(values, material, textures, dropped);

            if (values.Float("_UseMatcap", 0) >= .5f &&
                (S.Flat(Texture(values, "_Matcap3")) || S.Flat(Texture(values, "_Matcap4")))) S.Drop(dropped, "Matcap 3 and 4");
            if (values.Float("_UseDetailMaps", 0) >= .5f) S.Drop(dropped, "Detail Maps");
            if (values.Float("_UseHatching", 0) >= .5f) S.Drop(dropped, "Hatching");
            if (values.Float("_UseAlphaFresnel", 0) >= .5f) S.Drop(dropped, "Alpha Fresnel");
            if (values.Float("_UseEmissiveAudiolink", 0) >= .5f) S.Drop(dropped, "Audiolink");
            if (values.Float("_UseSubsurfaceScattering", 0) >= .5f) S.Drop(dropped, "Subsurface Scattering");
            if (values.Float("_UseContactShadows", 0) >= .5f) S.Drop(dropped, "Contact Shadows");
            if (values.Float("_UseAnimation", 0) >= .5f) S.Drop(dropped, "Animation");
            if (values.Float("_UseVanishing", 0) >= .5f) S.Drop(dropped, "Vanishing");
            if (values.Float("_UseProximityShadow", 0) >= .5f) S.Drop(dropped, "Proximity Shadow");
            if (values.Float("_UseInventory", 0) >= .5f) S.Drop(dropped, "Inventory");
            if (values.Float("_UseBackfaceTexture", 0) >= .5f) S.Drop(dropped, "Backface Texture");
            if (values.Float("_PixelSampleMode", 0) >= .5f) S.Drop(dropped, "Pixel Sample Mode");
            if (Mathf.RoundToInt(values.Float("_VertexColorType", 2)) == 0) S.Drop(dropped, "Vertex Color");
            if (values.ShaderName != null && values.ShaderName.Contains("(Fur)")) S.Drop(dropped, "Fur");
        }

        static Texture Texture(SourceValues values, string name) =>
            values.Texture(name, out var texture, out _, out _) ? texture : null;

        // _AlbedoAlphaMode: 0 the main image's alpha, 1 none (the image's alpha
        // is smoothness), 2 saturate(_ClippingMask.r + _Tweak_Transparency).
        // Each is multiplied by _Color.a. lilToon's replacing alpha mask is
        // mask × scale + value, without _Color.a.
        static void ApplyAlpha(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (material.GetFloat("_TransparentMode") == (int)Mode.Opaque) return;
            var tint = values.TryVector("_Color", out var color) && Finite(color) ? Mathf.Clamp01(color.w) : 1;
            switch (Mathf.RoundToInt(values.Float("_AlbedoAlphaMode", 0)))
            {
                case 1:
                    S.SetAlphaMask(material, 1, null, Vector2.one, Vector2.zero, 0, tint);
                    break;
                case 2:
                    var tweak = values.Float("_Tweak_Transparency", 0);
                    if (!values.Texture("_ClippingMask", out var mask, out var scale, out var offset) || !S.Flat(mask))
                    {
                        S.SetAlphaMask(material, 1, null, Vector2.one, Vector2.zero, 0, Mathf.Clamp01(1 + tweak) * tint);
                        break;
                    }
                    S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
                    // The sum is clamped before the tint, so it is baked.
                    var baked = Clipping(mask, tweak, textures);
                    if (baked != null) S.SetAlphaMask(material, 1, baked, scale, offset, tint, 0);
                    else S.SetAlphaMask(material, 1, mask, scale, offset, tint, tweak * tint);
                    break;
            }
        }

        // saturate(mask.r + tweak) as a grey, opaque image owned by textures,
        // or null when it cannot be read.
        static Texture2D Clipping(Texture mask, float tweak, List<Texture2D> textures)
        {
            if (textures == null) return null;
            var fit = Mathf.Min(1, 2048f / Mathf.Max(mask.width, mask.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(mask.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(mask.height * fit));
            var pixels = S.Sampled(mask, width, height);
            if (pixels == null) return null;
            for (var i = 0; i < pixels.Length; i++)
            {
                var v = Mathf.Clamp01(pixels[i].r + tweak);
                pixels[i] = new Color(v, v, v, 1);
            }
            return S.NewImage(mask, "__VRVlog_Clipping", width, height, pixels, textures, true);
        }

        // _ColorMask's green masks the tint and the hue shift (lilToon cannot
        // mask the tint); _ToggleHueControls 1 shifts hue (degrees, in YIQ),
        // saturation and value, which lilToon's _MainTexHSVG does in HSV.
        static void ApplyColor(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            values.Texture("_ColorMask", out var mask, out _, out _);
            var masked = S.Flat(mask);
            if (masked && values.TryVector("_Color", out var color) && (color.x < 1 || color.y < 1 || color.z < 1)) S.Drop(dropped, "Tint Mask");
            if (values.Float("_ToggleHueControls", 0) < .5f) return;
            var hue = Mathf.Repeat(values.Float("_ShiftHue", 0) / 360 + .5f, 1) - .5f;
            var hsvg = new Vector4(hue, Mathf.Max(0, values.Float("_ShiftSaturation", 1)), Mathf.Max(0, values.Float("_ShiftValue", 1)), 1);
            if (!Finite(hsvg) || hsvg == new Vector4(0, 1, 1, 1)) return;
            material.SetVector("_MainTexHSVG", hsvg);
            if (!masked) return;
            var green = S.BakeMask(mask, 1, false, textures);
            if (green != null) S.SetTexture(material, "_MainColorAdjustMask", green, Vector2.one, Vector2.zero);
            else S.Drop(dropped, "Color Mask");
        }

        // Crosstone: tone0 = _1st_ShadeMap × _1st_ShadeColor below
        // _1st_ShadeColor_Step, tone1 = _2nd_ShadeMap × _2nd_ShadeColor below
        // _2nd_ShadeColor_Step, each a smooth edge _Feather wide that ends at its
        // step. With _Crosstone2ndSeparation 0 tone1 is also multiplied by tone0;
        // with _CrosstoneToneSeparation 0 both are multiplied by the albedo, and
        // with 1 they are absolute colors (times the tint). lilToon's shadow is
        // lerp(albedo, texture, texture.a) × color, multiplied by the albedo
        // again by _ShadowMainStrength, which both layers share.
        static void ApplyCrosstone(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            ShadowCommon(material);
            var separate = values.Float("_CrosstoneToneSeparation", 0) >= .5f;
            var separate2 = values.Float("_Crosstone2ndSeparation", 0) >= .5f;
            var tint = material.GetColor("_Color");
            tint.a = 1;
            var map0 = Map(values, "_1st_ShadeMap");
            var color0 = Shade(values, "_1st_ShadeColor") * (separate ? tint : Color.white);
            Edge(material, "_ShadowBorder", "_ShadowBlur", values.Float("_1st_ShadeColor_Step", .5f), values.Float("_1st_ShadeColor_Feather", .01f));
            var multiply = Layer(material, "_ShadowColor", "_ShadowColorTex", map0, color0, separate, tint, textures);

            var step2 = values.Float("_2nd_ShadeColor_Step", 0);
            if (step2 > 0)
            {
                var map1 = Map(values, "_2nd_ShadeMap");
                var color1 = Shade(values, "_2nd_ShadeColor") * (separate2 ? tint : Color.white);
                if (!separate2)
                {
                    color1 *= color0;
                    if (map0 != null && map1 != null) S.Drop(dropped, "Combined Shade Maps");
                    map1 = map1 ?? map0;
                }
                Edge(material, "_Shadow2ndBorder", "_Shadow2ndBlur", step2, values.Float("_2nd_ShadeColor_Feather", .01f));
                if (Layer(material, "_Shadow2ndColor", "_Shadow2ndColorTex", map1, color1, separate, tint, textures) != multiply)
                    S.Drop(dropped, "Multiplied Shadow Layers");
            }
            if (multiply) S.Set(material, "_ShadowMainStrength", 1);

            if (values.Float("_ShadowBorderRange", 0) > 0 && values.TryVector("_ShadowBorderColor", out var border) &&
                (border.x > 0 || border.y > 0 || border.z > 0)) S.Drop(dropped, "Shadow Border Color");
            if (S.Flat(Texture(values, "_ShadingGradeMap")) || values.Float("_Tweak_ShadingGradeMapLevel", 0) != 0) S.Drop(dropped, "Shading Grade Map");
            if (values.Float("_SDFMode", 0) >= .5f) S.Drop(dropped, "SDF");
        }

        static Texture Map(SourceValues values, string name)
        {
            var map = Texture(values, name);
            return S.Flat(map) ? map : null;
        }

        static Color Shade(SourceValues values, string name)
        {
            var color = values.TryVector(name, out var vector) && Finite(vector) ? (Color)vector : Color.black;
            color.a = 1;
            return color;
        }

        // The tone is smootherstep(step - feather, step, h); lilToon's edge is a
        // ramp blur wide around its border.
        static void Edge(Material material, string border, string blur, float step, float feather)
        {
            feather = Mathf.Clamp(feather, .001f, 1);
            S.Set(material, border, Mathf.Clamp01(step - feather / 2));
            S.Set(material, blur, feather);
        }

        // Returns true when the layer needs lilToon to multiply by the albedo
        // again (an image multiplied by the albedo).
        static bool Layer(Material material, string colorName, string textureName, Texture map, Color color, bool separate, Color tint, List<Texture2D> textures)
        {
            color.a = 1;
            if (map != null) S.SetTexture(material, textureName, map, Vector2.one, Vector2.zero);
            else if (separate)
            {
                // An absolute color takes a white image in place of the main
                // image, so the main image's pattern does not show through. When
                // none can be made it becomes a ratio to the main color, which
                // lilToon multiplies back.
                var white = S.NewImage(null, "__VRVlog_Shade", 4, 4, Enumerable.Repeat(Color.white, 16).ToArray(), textures, false);
                if (white != null) S.SetTexture(material, textureName, white, Vector2.one, Vector2.zero);
                else color = S.Ratio(color, tint);
            }
            material.SetColor(colorName, color);
            return map != null && !separate;
        }

        static void ShadowCommon(Material material)
        {
            S.Reset(material, "_ShadowColorTex", "_Shadow2ndColorTex");
            S.Set(material, "_UseShadow", 1);
            S.Set(material, "_ShadowStrength", 1);
            S.Set(material, "_ShadowMainStrength", 0);
            S.Set(material, "_ShadowEnvStrength", 1);
            S.Set(material, "_ShadowBorderRange", 0);
            material.SetColor("_ShadowBorderColor", Color.black);
            material.SetColor("_Shadow2ndColor", new Color(0, 0, 0, 0));
        }

        // Lightramp: the lit color is albedo × lerp(tone, 1, ramp(position)),
        // position = lift + h × bias × (1 - lift). The tone and bias come from
        // _ShadowMask by _ShadowMaskType, with bias lerped to 1 by _Shadow.
        // _LightRampType: 0 the ramp's bottom row, 1 its left column, 2 a sharp
        // edge at 0.5, 3 saturate(2 × position - 1). The ramp is sampled over h
        // and fitted with lilToon's single edge; its dark end sets the color.
        static void ApplyLightramp(SourceValues values, Material material, ICollection<string> dropped)
        {
            ShadowCommon(material);
            var type = Mathf.RoundToInt(values.Float("_ShadowMaskType", 0));
            var maskColor = values.TryVector("_ShadowMaskColor", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            var boost = values.Float("_IndirectLightingBoost", 0);
            if (S.Flat(Texture(values, "_ShadowMask"))) S.Drop(dropped, "Shadow Mask");
            if (type == 2) S.Drop(dropped, "Auto-Tone");
            if (values.Float("_SDFMode", 0) >= .5f) S.Drop(dropped, "SDF");
            // The mask is taken as white.
            // Occlusion: saturate(boost + 1 - a); Tone: saturate(rgb + boost);
            // Median is not implemented in the source and leaves black.
            var level = type == 1 ? Mathf.Clamp01(1 + boost) : type == 3 ? 0 : Mathf.Clamp01(boost);
            var tone = new Color(level * maskColor.r, level * maskColor.g, level * maskColor.b, 1);
            var shadow = Mathf.Clamp01(values.Float("_Shadow", .5f));
            var bias = (1 - shadow) * (type == 3 ? 0 : Mathf.Clamp01(maskColor.a)) + shadow;
            var lift = Mathf.Clamp(values.Float("_ShadowLift", 0), -1, 1);

            var rampType = Mathf.RoundToInt(values.Float("_LightRampType", 0));
            Color[] ramp = null;
            if (rampType <= 1)
            {
                var texture = Map(values, "_Ramp");
                // The default ramp is white: no shadow.
                if (texture == null) { S.Set(material, "_UseShadow", 0); return; }
                ramp = S.Sampled(texture, 64, 64);
                // Without a graphics device the ramp cannot be read; lilToon's
                // default shadow stands in.
                if (ramp == null) { material.SetColor("_ShadowColor", tone.gamma); return; }
            }
            Color Ramp(float position)
            {
                position = Mathf.Clamp01(position);
                switch (rampType)
                {
                    case 3: return Color.white * Mathf.Clamp01(position * 2 - 1);
                    case 2: return position >= .5f ? Color.white : Color.black;
                    default:
                        var at = position * 63;
                        var i = Mathf.Min(62, Mathf.FloorToInt(at));
                        var a = rampType == 1 ? ramp[i * 64] : ramp[i];
                        var b = rampType == 1 ? ramp[(i + 1) * 64] : ramp[i + 1];
                        return Color.Lerp(a, b, at - i);
                }
            }
            const int Steps = 128;
            var brightness = new float[Steps + 1];
            for (var i = 0; i <= Steps; i++)
            {
                var c = Ramp(lift + i / (float)Steps * bias * (1 - lift));
                brightness[i] = c.r * .2126f + c.g * .7152f + c.b * .0722f;
            }
            if (!S.FitShadowEdge(brightness, material))
            {
                // A ramp that does not change shades the whole surface by its
                // value; white leaves it unshaded.
                if (brightness[0] > .999f) { S.Set(material, "_UseShadow", 0); return; }
                S.Set(material, "_ShadowBorder", 1);
                S.Set(material, "_ShadowBlur", .001f);
            }
            var dark = Ramp(lift);
            var linear = tone.linear;
            var color = new Color(Mathf.Lerp(linear.r, 1, dark.r), Mathf.Lerp(linear.g, 1, dark.g), Mathf.Lerp(linear.b, 1, dark.b), 1);
            material.SetColor("_ShadowColor", color.gamma);
        }

        // _outline_width is in centimeters along the normal, as lilToon's.
        // _OutlineMode 1 multiplies _outline_color by the albedo, 2 uses it as
        // is. _OutlineMask's red scales the width.
        static void ApplyOutline(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { S.Drop(dropped, "Outline"); return; }
            S.Set(material, "_OutlineWidth", Mathf.Max(0, values.Float("_outline_width", .1f)));
            S.Set(material, "_OutlineFixWidth", 0);
            S.Set(material, "_OutlineEnableLighting", 1);
            var color = values.TryVector("_outline_color", out var vector) && Finite(vector) ? (Color)vector : new Color(.5f, .5f, .5f, 1);
            if (values.Float("_OutlineMode", 1) < 1.5f)
            {
                // Tinted multiplies by the albedo, which carries the tint.
                if (values.TryVector("_Color", out var tint) && Finite(tint))
                    color = new Color(color.r * tint.x, color.g * tint.y, color.b * tint.z, color.a);
                material.SetTexture("_OutlineTex", material.GetTexture("_MainTex"));
                material.SetTextureScale("_OutlineTex", material.GetTextureScale("_MainTex"));
                material.SetTextureOffset("_OutlineTex", material.GetTextureOffset("_MainTex"));
            }
            else S.Reset(material, "_OutlineTex");
            material.SetColor("_OutlineColor", color);
            var mask = Map(values, "_OutlineMask");
            if (mask != null) S.SetTexture(material, "_OutlineWidthMask", mask, Vector2.one, Vector2.zero);
            if (values.Float("_OutlineZPush", 0) != 0) S.Drop(dropped, "Outline Z Push");
            if (values.Float("_OutlineCalculationMode", 0) >= .5f) S.Drop(dropped, "Outline Calculation Mode");
        }

        // _UseFresnel 1 adds albedo × rim × tint before lighting, 2 adds rim ×
        // tint, 3 and 4 also brighten the shading. rim = smoothstep(0.5 × (1 -
        // s), 0.5 + 0.5 × s, (1 - N·V)^4 × width) × tint.a × _ColorMask.b, with s
        // _FresnelStrength. On 1 - N·V that edge runs from (start / width)^0.25 to
        // (end / width)^0.25, which lilToon's border and blur cover.
        static void ApplyRim(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var mode = Mathf.RoundToInt(values.Float("_UseFresnel", 0));
            if (mode == 0) { S.Set(material, "_UseRim", 0); return; }
            if (mode >= 3) S.Drop(dropped, "Rim Light Bias");
            if (mode == 3) { S.Set(material, "_UseRim", 0); return; }
            var width = values.Float("_FresnelWidth", .5f);
            var softness = Mathf.Clamp(values.Float("_FresnelStrength", .5f), .01f, .9999f);
            if (width <= 0) { S.Set(material, "_UseRim", 0); return; }
            var inner = Mathf.Pow(Mathf.Clamp01(.5f * (1 - softness) / width), .25f);
            var outer = Mathf.Pow(Mathf.Clamp01((.5f + .5f * softness) / width), .25f);
            S.Set(material, "_UseRim", 1);
            S.Set(material, "_RimBorder", Mathf.Clamp01((inner + outer) / 2));
            S.Set(material, "_RimBlur", Mathf.Clamp(outer - inner, .001f, 1));
            S.Set(material, "_RimFresnelPower", 1);
            S.Set(material, "_RimBlendMode", 1);
            S.Set(material, "_RimMainStrength", mode == 2 ? 0 : 1);
            S.Set(material, "_RimEnableLighting", mode == 2 ? 0 : 1);
            S.Set(material, "_RimShadowMask", 0);
            var tint = values.TryVector("_FresnelTint", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            material.SetColor("_RimColor", tint);
            if (values.Texture("_ColorMask", out var mask, out _, out _) && S.Flat(mask))
            {
                var blue = S.BakeMask(mask, 2, false, textures);
                if (blue != null) S.SetTexture(material, "_RimColorTex", blue, Vector2.one, Vector2.zero);
                else S.Drop(dropped, "Rim Mask");
            }
            if (values.Float("_UseFresnelLightMask", 0) >= .5f) S.Drop(dropped, "Rim Light Mask");
        }

        // _UseMatcap 1 (2 is anisotropic) applies each matcap to the albedo
        // before lighting with strength _MatcapNStrength × one channel of
        // _MatcapMask (R, G, B, A for 1 to 4): Additive albedo + matcap × t,
        // Multiply albedo × lerp(1, matcap, t), Median albedo + matcap × albedo
        // × t. A tint with alpha 1 multiplies the matcap; below 1 it mixes in a
        // way lilToon has no counterpart for. lilToon has two matcaps.
        static void ApplyMatcap(SourceValues values, Material material, string index, string target, int channel, List<Texture2D> textures,
            ICollection<string> dropped)
        {
            var toggle = "_UseMatCap" + target;
            var use = Mathf.RoundToInt(values.Float("_UseMatcap", 0));
            if (use == 0 || !values.Texture("_Matcap" + index, out var matcap, out _, out _) || !S.Flat(matcap))
            {
                S.Set(material, toggle, 0);
                return;
            }
            if (use == 2) S.Drop(dropped, "Anisotropic Matcap");
            var strength = Mathf.Clamp(values.Float("_Matcap" + index + "Strength", 1), 0, 2);
            if (strength <= 0) { S.Set(material, toggle, 0); return; }
            var blend = Mathf.RoundToInt(values.Float("_Matcap" + index + "Blend", 0));
            var tint = values.TryVector("_Matcap" + index + "Tint", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            if (tint.a < 1) S.Drop(dropped, "Matcap Tint Alpha");
            var color = blend == 1 ? tint : tint * Mathf.Max(1, strength);
            color.a = 1;
            S.Set(material, toggle, 1);
            S.SetTexture(material, "_MatCap" + target + "Tex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCap" + target + "Color", color);
            S.Set(material, "_MatCap" + target + "Blend", Mathf.Min(1, strength));
            S.Set(material, "_MatCap" + target + "BlendMode", blend == 1 ? 3 : 1);
            S.Set(material, "_MatCap" + target + "MainStrength", blend == 2 ? 1 : 0);
            S.Set(material, "_MatCap" + target + "EnableLighting", 1);
            S.Set(material, "_MatCap" + target + "ShadowMask", 0);
            if (values.Texture("_MatcapMask", out var mask, out _, out _) && S.Flat(mask))
            {
                var copy = channel == 0 && S.Grey(mask) ? mask : S.BakeMask(mask, channel, false, textures);
                if (copy != null) S.SetTexture(material, "_MatCap" + target + "BlendMask", copy, Vector2.one, Vector2.zero);
                else S.Drop(dropped, "Matcap Mask");
            }
        }

        // emission = (keyword ? _EmissionMap : 1) × _EmissionColor, times the
        // albedo with _EmissionMode 1; it is added even without the keyword.
        // The second emission is the same with "2nd" names.
        static void ApplyEmission(SourceValues values, Material material, string source, string target, string keyword,
            ICollection<string> dropped)
        {
            var toggle = "_UseEmission" + target;
            var color = values.TryVector("_EmissionColor" + source, out var vector) && Finite(vector) ? (Color)vector : Color.black;
            if (color.maxColorComponent <= 0)
            {
                S.Reset(material, "_Emission" + target + "Map", "_Emission" + target + "Color");
                S.Set(material, toggle, 0);
                return;
            }
            color.a = 1;
            S.Set(material, toggle, 1);
            material.SetColor("_Emission" + target + "Color", color);
            if (values.Keyword(keyword) && values.Texture("_EmissionMap" + source, out var map, out var scale, out var offset) && S.Flat(map))
                S.SetTexture(material, "_Emission" + target + "Map", map, scale, offset);
            else S.Reset(material, "_Emission" + target + "Map");
            // The mode multiplies the map by the albedo, so it needs the keyword.
            S.Set(material, "_Emission" + target + "MainStrength", values.Keyword(keyword) && values.Float("_EmissionMode" + source, 0) >= .5f ? 1 : 0);
            if (values.Keyword(keyword) && values.Float("_EmissionUVSec" + source, 0) != 0) S.Drop(dropped, "Emission UV Set");
            if (values.Keyword(keyword) && S.Flat(Texture(values, "_DetailEmissionMap" + source))) S.Drop(dropped, "Detail Emission");
            if (values.Float("_EmissionRimPower" + source, 0) != 0) S.Drop(dropped, "Emission Rim Power");
            if (source == "" && values.Float("_UseEmissiveLightSense", 0) >= .5f) S.Drop(dropped, "Emissive Light Sense");
        }

        // _SpecularType 0 none, 1 Standard, 4 Cel (sharpened by
        // _CelSpecularSoftness); the others are drawn as Standard and reported.
        // _SpecGlossMap × _SpecColor × _SpecColor.a is the specular color (or its
        // red the metallic with _UseMetallic), its alpha × _Smoothness the
        // smoothness.
        static void ApplySpecular(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var type = Mathf.RoundToInt(values.Float("_SpecularType", 0));
            if (type == 0) { S.Set(material, "_UseReflection", 0); return; }
            if (type != 1 && type != 4) S.Drop(dropped, "Specular Type");
            if (type == 4 && values.Float("_CelSpecularSteps", 1) > 1) S.Drop(dropped, "Cel Specular Steps");
            S.Set(material, "_UseReflection", 1);
            S.Set(material, "_SpecularToon", type == 4 ? 1 : 0);
            if (type == 4)
            {
                S.Set(material, "_SpecularBorder", .5f);
                S.Set(material, "_SpecularBlur", Mathf.Clamp01(values.Float("_CelSpecularSoftness", .02f)));
            }
            S.Set(material, "_ApplySpecular", values.Float("_SpecularHighlights", 1) >= .5f ? 1 : 0);
            S.Set(material, "_ApplyReflection", values.Float("_GlossyReflections", 1) >= .5f ? 1 : 0);
            var spec = values.TryVector("_SpecColor", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            var specColor = new Color(spec.r * spec.a, spec.g * spec.a, spec.b * spec.a, 1);
            var map = Map(values, "_SpecGlossMap");
            var smoothness = Mathf.Clamp01(values.Float("_Smoothness", 1));
            if (values.Float("_UseMetallic", 0) >= .5f)
            {
                S.Set(material, "_Metallic", Mathf.Clamp01(specColor.r));
                material.SetColor("_ReflectionColor", Color.white);
                if (map != null)
                {
                    var red = S.BakeMask(map, 0, false, textures);
                    if (red != null) S.SetTexture(material, "_MetallicGlossMap", red, Vector2.one, Vector2.zero);
                    else S.Drop(dropped, "Metallic Map");
                }
            }
            else
            {
                S.Set(material, "_Metallic", 0);
                material.SetColor("_ReflectionColor", specColor);
                if (map != null) S.SetTexture(material, "_ReflectionColorTex", map, Vector2.one, Vector2.zero);
            }
            S.Set(material, "_Smoothness", smoothness);
            if (Mathf.RoundToInt(values.Float("_AlbedoAlphaMode", 0)) == 1) S.Drop(dropped, "Smoothness From Albedo Alpha");
            else if (map != null)
            {
                var alpha = S.BakeMask(map, 3, false, textures);
                if (alpha != null) S.SetTexture(material, "_SmoothnessTex", alpha, Vector2.one, Vector2.zero);
                else S.Drop(dropped, "Smoothness Map");
            }
        }
    }
}
