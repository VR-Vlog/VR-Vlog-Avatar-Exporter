using System;
using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using Mode = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.Mode;
using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;
using S = VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // orels Toon, the first version (orels1/Toon/...) and v2
    // (orels1/Toon/v2/...). The generic mapping reads the main image and
    // color, the cutoff, the normal map, the emission image and color, and
    // _CullMode. This class bakes the hue, saturation and value the shader
    // adds to the albedo, reads the separate alpha and its scale, fits the
    // ramp onto lilToon's shadow, and maps the outline, the emission as the
    // shader computes it, the rim light, the matcaps and the reflections.
    // The effects lilToon has no counterpart for are reported.
    internal sealed class OrelsToonApproximation : IShaderApproximation
    {
        const string Prefix = "orels1/Toon/";

        // A missing shader is known by two properties of the main module only
        // these shaders declare.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null
                ? values.TryFloat("_BackfaceAlbedoTint", out _) && values.TryFloat("_TintByVertexColor", out _)
                : shaderName.StartsWith(Prefix, StringComparison.Ordinal);

        // The variant is in the name only: Cutout tests _Cutoff, Transparent
        // (and Transparent PrePass) blends by the alpha. A missing shader keeps
        // its render queue, which the generic path reads.
        public Mode? RenderMode(SourceValues values)
        {
            var name = values.ShaderName;
            if (name == null) return null;
            if (name.Contains("Transparent")) return Mode.Transparent;
            if (name.Contains("Cutout")) return Mode.Cutout;
            return Mode.Opaque;
        }

        public bool Outline(SourceValues values) => values.Keyword("OUTLINE_ENABLED");

        // v2 has its own emission module with _EmissionPower; the first
        // version does not declare it.
        static bool Second(SourceValues values) =>
            values.ShaderName != null ? values.ShaderName.StartsWith(Prefix + "v2/", StringComparison.Ordinal) : values.TryFloat("_EmissionPower", out _);

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var second = Second(values);
            ApplyMain(values, material, textures, dropped);
            ApplyBackface(values, material, dropped);
            ApplyNormal(values, material, dropped);
            ApplyShading(values, material, dropped);
            if (Outline(values)) ApplyOutline(values, material, dropped);
            if (second) ApplyEmission2(values, material, textures, dropped);
            else ApplyEmission1(values, material, textures, dropped);
            ApplyRim(values, material, second, dropped);
            if (values.Keyword("RIMSHADOW_ON")) S.Drop(dropped, "Rim Shadow");
            if (second)
            {
                ApplyMatcap(values, material, 0, "", textures, dropped);
                ApplyMatcap(values, material, 1, "2nd", textures, dropped);
                if (values.Keyword("MATCAP_2")) S.Drop(dropped, "Matcap Layer 3");
                if (values.Keyword("MATCAP_3")) S.Drop(dropped, "Matcap Layer 4");
            }
            else ApplyReflectionMatcap(values, material, textures, dropped);
            ApplyReflection(values, material, second, textures, dropped);

            // lilToon's own monochrome lighting has the same meaning.
            if (values.TryFloat("_MonochromeLighting", out var mono)) S.Set(material, "_MonochromeLighting", Mathf.Clamp01(mono));
            if (values.Keyword("SPECULAR_ON") && values.Float("_SpecularIntensity", 0) > 0) S.Drop(dropped, "Specular");
            if (values.Texture("_OcclusionMap", out var occlusion, out _, out _) && S.Flat(occlusion) && values.Float("_OcclusionStrength", 0) > 0)
                S.Drop(dropped, "Occlusion");
            if (values.Float("_TintByVertexColor", 0) >= .5f) S.Drop(dropped, "Tint By Vertex Color");
            if (values.Float("_MainTexUVSet", 0) != 0) S.Drop(dropped, "UV Set");
            if (values.Keyword("UV_DISCARD")) S.Drop(dropped, "UV Discard");
            if (values.Keyword("INTEGRATE_LTCGI") || values.ShaderName != null && values.ShaderName.Contains("LTCGI")) S.Drop(dropped, "LTCGI");
            if (values.Float("AL_MODE", 0) >= .5f) S.Drop(dropped, "Audio Link");
            for (var i = 0; i < 4; i++)
                if (values.Keyword("DECAL_" + i)) { S.Drop(dropped, "Decals"); break; }
        }

        // albedo = _MainTex × _Color; then in HSV the hue is shifted by _Hue
        // and _Saturation and _Value are added and clamped to 0..1. lilToon's
        // _MainTexHSVG multiplies the saturation and value instead, so the
        // result is baked into the main image. The alpha is saturate(alpha ×
        // _AlphaScale); with SEPARATE_ALPHA it is the red of _AlphaTex at the
        // main image's UV (a negative scale inverts it), which lilToon's mask
        // replaces the alpha with.
        static void ApplyMain(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var hue = values.Float("_Hue", 0);
            var saturation = values.Float("_Saturation", 0);
            var value = values.Float("_Value", 0);
            var alphaScale = values.Float("_AlphaScale", 1);
            if (!Finite(new Vector4(hue, saturation, value, alphaScale))) return;
            var shifted = hue > 0 && hue < 1 || saturation != 0 || value != 0;
            var blended = material.GetFloat("_TransparentMode") != (int)Mode.Opaque;
            var separate = blended && values.Keyword("SEPARATE_ALPHA");
            if (separate) ApplySeparateAlpha(values, material, alphaScale, textures, dropped);
            var scaled = blended && !separate && alphaScale != 1;
            if (!shifted && !scaled) return;

            Vector3 Shift(Vector3 c)
            {
                if (!shifted) return c;
                Color.RGBToHSV(new Color(Mathf.Max(0, c.x), Mathf.Max(0, c.y), Mathf.Max(0, c.z)), out var h, out var s, out var v);
                var rgb = Color.HSVToRGB(Mathf.Repeat(h + hue, 1), Mathf.Clamp01(s + saturation), Mathf.Clamp01(v + value), true);
                return new Vector3(rgb.r, rgb.g, rgb.b);
            }
            float Alpha(float a) => scaled ? Mathf.Clamp01(a * alphaScale) : a;

            var tint = material.GetColor("_Color");
            var linearTint = new Vector3(tint.linear.r, tint.linear.g, tint.linear.b);
            var main = material.GetTexture("_MainTex");
            if (main == null)
            {
                var flat = Shift(linearTint);
                var gamma = new Color(Mathf.Clamp01(flat.x), Mathf.Clamp01(flat.y), Mathf.Clamp01(flat.z)).gamma;
                material.SetColor("_Color", new Color(gamma.r, gamma.g, gamma.b, Alpha(tint.a)));
                return;
            }
            var baked = Bake(main, "__VRVlog_Adjusted", textures, false, p =>
            {
                var c = Shift(new Vector3(p.r * linearTint.x, p.g * linearTint.y, p.b * linearTint.z));
                var gamma = new Color(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z)).gamma;
                return new Color(gamma.r, gamma.g, gamma.b, Alpha(p.a * tint.a));
            });
            if (baked != null)
            {
                S.SetTexture(material, "_MainTex", baked, material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                material.SetColor("_Color", Color.white);
                return;
            }
            // Without a graphics device: lilToon adds the hue the same way; the
            // alpha can be lowered by a white mask but not raised.
            if (shifted && hue > 0 && hue < 1) material.SetVector("_MainTexHSVG", new Vector4(hue, 1, 1, 1));
            if (saturation != 0 || value != 0) S.Drop(dropped, "Saturation and Value");
            if (scaled && alphaScale < 1) S.SetAlphaMask(material, 2, null, Vector2.one, Vector2.zero, 0, Mathf.Max(0, alphaScale));
            else if (scaled) S.Drop(dropped, "Alpha Scale");
        }

        static void ApplySeparateAlpha(SourceValues values, Material material, float scale, List<Texture2D> textures, ICollection<string> dropped)
        {
            // scale ≥ 0: red × scale; scale < 0: 1 − red × −scale. lilToon's
            // mask gives saturate(mask × _AlphaMaskScale + _AlphaMaskValue), and
            // an empty mask reads as white as the shader's does.
            var maskValue = scale < 0 ? 1 : 0;
            if (!values.Texture("_AlphaTex", out var mask, out _, out _) || !S.Flat(mask))
            {
                S.SetAlphaMask(material, 1, null, Vector2.one, Vector2.zero, scale, maskValue);
                return;
            }
            var copy = S.Grey(mask) ? mask : S.BakeMask(mask, 0, false, textures);
            if (copy == null) { S.Drop(dropped, "Transparency Texture"); return; }
            // The shader samples it at the main image's UV, as lilToon does.
            S.SetAlphaMask(material, 1, copy, Vector2.one, Vector2.zero, scale, maskValue);
        }

        // Back faces (with _CullMode not Back) take lerp(_BackfaceColor,
        // albedo × _BackfaceColor, _BackfaceAlbedoTint). lilToon's
        // _BackfaceColor replaces the albedo by its alpha, which covers the
        // flat color only.
        static void ApplyBackface(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (Mathf.RoundToInt(values.Float("_CullMode", 2)) == 2) return;
            var color = values.TryVector("_BackfaceColor", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            var tint = Mathf.Clamp01(values.Float("_BackfaceAlbedoTint", 1));
            if (tint <= .001f) material.SetColor("_BackfaceColor", new Color(color.r, color.g, color.b, 1));
            else if (color.r < .999f || color.g < .999f || color.b < .999f) S.Drop(dropped, "Backface Color");
        }

        // The normal map is sampled at the main image's UV × _BumpMapTiling, or
        // at the first UV × the tiling when _BumpMapTilingMode is Independent;
        // its own tiling is not used. lilToon tiles it on top of the main
        // image's.
        static void ApplyNormal(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (values.Texture("_DetailNormalMap", out var detail, out _, out _) && S.Flat(detail)) S.Drop(dropped, "Detail Normal");
            for (var i = 0; i < 4; i++)
                if (values.Keyword("DETAIL_NORMALS_" + i)) { S.Drop(dropped, "Detail Normal"); break; }
            if (material.GetTexture("_BumpMap") == null) return;
            var tiling = values.Float("_BumpMapTiling", 1);
            if (!float.IsFinite(tiling)) tiling = 1;
            var scale = new Vector2(tiling, tiling);
            var offset = Vector2.zero;
            if (values.Float("_BumpMapTilingMode", 0) >= .5f)
                S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
            material.SetTextureScale("_BumpMap", scale);
            material.SetTextureOffset("_BumpMap", offset);
            if (values.Float("_FlipBumpY", 0) >= .5f) S.Drop(dropped, "Normal Flip Y");
        }

        // diffuse = albedo × (light × ramp(N·L × 0.5 + 0.5) + ambient), with the
        // ramp's bottom row. The default ramp is the built-in grayscale ramp,
        // taken here as a straight gradient from black to white. lilToon's
        // single edge is fitted where the brightness is halfway with the blur
        // from its 10% and 90% points, and the dark end is the shadow color;
        // lilToon adds the ambient light in the shadow with _ShadowEnvStrength.
        static void ApplyShading(SourceValues values, Material material, ICollection<string> dropped)
        {
            S.Reset(material, "_ShadowColorTex", "_Shadow2ndColorTex");
            S.Set(material, "_UseShadow", 1);
            S.Set(material, "_ShadowStrength", 1);
            S.Set(material, "_ShadowMainStrength", 0);
            S.Set(material, "_ShadowEnvStrength", 1);
            S.Set(material, "_ShadowBorderRange", 0);
            material.SetColor("_ShadowBorderColor", Color.black);
            material.SetColor("_Shadow2ndColor", new Color(0, 0, 0, 0));

            var occlusion = values.Texture("_OcclusionMap", out var map, out _, out _) && S.Flat(map) && values.Float("_OcclusionStrength", 0) > 0;
            if (occlusion && (values.Float("_OffsetRampByOcclusion", 0) >= .5f || values.Float("_OcclusionMode", 0) >= .5f))
                S.Drop(dropped, "Offset Ramp By Occlusion");

            Color[] ramp = null;
            if (values.Texture("_Ramp", out var texture, out _, out _) && S.Flat(texture))
            {
                ramp = S.Sampled(texture, 64, 64);
                // Without a graphics device the ramp cannot be read; lilToon's
                // default shadow stands in.
                if (ramp == null) return;
            }
            Color Ramp(float position)
            {
                position = Mathf.Clamp01(position);
                if (ramp == null) return new Color(position, position, position, 1);
                var at = position * 63;
                var i = Mathf.Min(62, Mathf.FloorToInt(at));
                return Color.Lerp(ramp[i], ramp[i + 1], at - i);
            }
            const int Steps = 128;
            var brightness = new float[Steps + 1];
            for (var i = 0; i <= Steps; i++)
            {
                var c = Ramp(i / (float)Steps);
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
            var dark = Ramp(0);
            material.SetColor("_ShadowColor", new Color(Mathf.Clamp01(dark.r), Mathf.Clamp01(dark.g), Mathf.Clamp01(dark.b), 1).gamma);
        }

        // The outline moves the vertices along the normal by _OutlineWidth ×
        // _OutlineMask.r centimeters, as lilToon's width and mask do. Its color
        // is _OutlineColor × _OutlineTex (at the first UV), times the albedo by
        // _OutlineAlbedoTint, lit (mode 0) or not (1, Emissive).
        static void ApplyOutline(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { S.Drop(dropped, "Outline"); return; }
            S.Set(material, "_OutlineWidth", Mathf.Max(0, values.Float("_OutlineWidth", 1)));
            S.Set(material, "_OutlineFixWidth", 0);
            S.Set(material, "_OutlineEnableLighting", values.Float("_OutlineLightingMode", 0) >= .5f ? 0 : 1);
            var color = values.TryVector("_OutlineColor", out var vector) && Finite(vector) ? (Color)vector : new Color(.5f, .5f, .5f, 1);
            color.a = 1;
            var hasTexture = values.Texture("_OutlineTex", out var texture, out _, out _) && S.Flat(texture);
            if (values.Float("_OutlineAlbedoTint", 0) >= .5f)
            {
                if (hasTexture) S.Drop(dropped, "Outline Albedo Tint");
                else
                {
                    // The albedo carries the main color; the main image is
                    // taken as the outline's texture.
                    var tint = material.GetColor("_Color");
                    color = new Color(color.r * tint.r, color.g * tint.g, color.b * tint.b, 1);
                    material.SetTexture("_OutlineTex", material.GetTexture("_MainTex"));
                    material.SetTextureScale("_OutlineTex", material.GetTextureScale("_MainTex"));
                    material.SetTextureOffset("_OutlineTex", material.GetTextureOffset("_MainTex"));
                    hasTexture = false;
                    material.SetColor("_OutlineColor", color);
                    ApplyOutlineMask(values, material, dropped);
                    return;
                }
            }
            if (hasTexture)
            {
                var scale = Vector2.one;
                var offset = Vector2.zero;
                S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
                S.SetTexture(material, "_OutlineTex", texture, scale, offset);
            }
            else S.Reset(material, "_OutlineTex");
            material.SetColor("_OutlineColor", color);
            ApplyOutlineMask(values, material, dropped);
        }

        static void ApplyOutlineMask(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (values.Texture("_OutlineMask", out var mask, out _, out _) && S.Flat(mask)) S.SetTexture(material, "_OutlineWidthMask", mask, Vector2.one, Vector2.zero);
            if (values.Float("_OutlineWidthAdjustByVertexColor", 0) >= .5f) S.Drop(dropped, "Outline Width By Vertex Color");
        }

        // First version: only with _EMISSION (set when _EmissionMap is
        // assigned), emission = map × lerp(map, map × albedo, tint) ×
        // _EmissionColor, so the map counts twice; the main hue, saturation and
        // value are then applied to its direction. The map is read at the main
        // image's UV.
        static void ApplyEmission1(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!values.Keyword("_EMISSION") || !values.Texture("_EmissionMap", out var map, out _, out _) || !S.Flat(map)) { Off(material); return; }
            Emission(values, material, map, p => new Vector3(p.r * p.r, p.g * p.g, p.b * p.b),
                values.Float("_Hue", 0), values.Float("_Saturation", 0), values.Float("_Value", 0), textures, dropped);
        }

        // v2: only with a map taller than 16 pixels, emission = pow(map (RGB or
        // one channel), _EmissionPower) × _EmissionColor × lerp(1, albedo,
        // tint), then its own hue, saturation and value.
        static void ApplyEmission2(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!values.Texture("_EmissionMap", out var map, out _, out _) || !S.Flat(map) || map.height <= 16) { Off(material); return; }
            var channel = Mathf.Clamp(Mathf.RoundToInt(values.Float("_EmissionMapChannel", 0)), 0, 4);
            var power = values.Float("_EmissionPower", 1);
            if (!float.IsFinite(power)) power = 1;
            Vector3 Shape(Color p)
            {
                var c = channel == 0 ? new Vector3(p.r, p.g, p.b) : Vector3.one * p[channel - 1];
                return power == 1 ? c : new Vector3(Pow(c.x), Pow(c.y), Pow(c.z));
            }
            float Pow(float x) => x <= 0 ? (power <= 0 ? 1 : 0) : Mathf.Pow(x, power);
            Emission(values, material, map, channel == 0 && power == 1 ? null : (Func<Color, Vector3>)Shape,
                values.Float("_EmissionHue", 0), values.Float("_EmissionSaturation", 0), values.Float("_EmissionValue", 0), textures, dropped);
            if (values.Float("_EmissionMaskByUVTile", 0) >= .5f) S.Drop(dropped, "Emission Mask By UV Tile");
            if (Mathf.Abs(values.Float("_EmissionRimMasking", 0)) > .0001f) S.Drop(dropped, "Emission Rim Masking");
        }

        static void Off(Material material)
        {
            S.Reset(material, "_EmissionMap", "_EmissionColor");
            S.Set(material, "_UseEmission", 0);
        }

        // shape is the map's value per pixel (null: the map as it is). The hue,
        // saturation and value act on the normalized color and keep its length;
        // they are baked with the color into the map, whose largest value
        // becomes the color. lilToon multiplies by the albedo after them.
        static void Emission(SourceValues values, Material material, Texture map, Func<Color, Vector3> shape, float hue, float saturation,
            float value, List<Texture2D> textures, ICollection<string> dropped)
        {
            var color = values.TryVector("_EmissionColor", out var vector) && Finite(vector) ? ((Color)vector).linear : Color.black;
            if (color.maxColorComponent <= 0) { Off(material); return; }
            var shifted = Finite(new Vector3(hue, saturation, value)) && (hue > 0 && hue < 1 || saturation != 0 || value != 0);
            S.Set(material, "_UseEmission", 1);
            S.Set(material, "_EmissionMainStrength", Mathf.Clamp01(values.Float("_EmissionTintToDiffuse", 0)));
            if (values.Float("_EmissionScaleWithLight", 0) >= .5f) S.Drop(dropped, "Emission Scale With Light");
            var mainScale = material.GetTextureScale("_MainTex");
            var mainOffset = material.GetTextureOffset("_MainTex");

            Texture2D baked = null;
            var peak = 1f;
            if (shifted)
            {
                var linear = new Vector3(color.r, color.g, color.b);
                var fit = Mathf.Min(1, 2048f / Mathf.Max(map.width, map.height));
                var width = Mathf.Max(1, Mathf.RoundToInt(map.width * fit));
                var height = Mathf.Max(1, Mathf.RoundToInt(map.height * fit));
                var pixels = S.Sampled(map, width, height);
                if (pixels != null)
                {
                    var glow = new Vector3[pixels.Length];
                    peak = 0;
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var e = Vector3.Scale(shape?.Invoke(pixels[i]) ?? new Vector3(pixels[i].r, pixels[i].g, pixels[i].b), linear);
                        var length = e.magnitude;
                        if (length > 0)
                        {
                            var n = e / length;
                            Color.RGBToHSV(new Color(Mathf.Max(0, n.x), Mathf.Max(0, n.y), Mathf.Max(0, n.z)), out var h, out var s, out var v);
                            var rgb = Color.HSVToRGB(Mathf.Repeat(h + hue, 1), Mathf.Clamp01(s + saturation), Mathf.Clamp01(v + value), true);
                            e = new Vector3(rgb.r, rgb.g, rgb.b) * length;
                        }
                        glow[i] = e;
                        peak = Mathf.Max(peak, Mathf.Max(e.x, Mathf.Max(e.y, e.z)));
                    }
                    if (peak > 0)
                        for (var i = 0; i < pixels.Length; i++)
                        {
                            var g = new Color(Mathf.Clamp01(glow[i].x / peak), Mathf.Clamp01(glow[i].y / peak), Mathf.Clamp01(glow[i].z / peak)).gamma;
                            pixels[i] = new Color(g.r, g.g, g.b, 1);
                        }
                    baked = peak > 0 ? S.NewImage(map, "__VRVlog_Emission", width, height, pixels, textures, false) : null;
                    if (peak <= 0) { Off(material); return; }
                }
                if (baked == null) S.Drop(dropped, "Emission Hue, Saturation and Value");
            }
            if (baked == null && shape != null)
                baked = Bake(map, "__VRVlog_Emission", textures, false, p =>
                {
                    var c = shape(p);
                    var g = new Color(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z)).gamma;
                    return new Color(g.r, g.g, g.b, 1);
                });
            if (baked != null && shifted && peak > 0)
            {
                var g = new Color(peak, peak, peak).gamma;
                material.SetColor("_EmissionColor", new Color(g.r, g.g, g.b, 1));
            }
            else
            {
                var g = color.gamma;
                material.SetColor("_EmissionColor", new Color(g.r, g.g, g.b, 1));
            }
            // lilToon reads its emission map at the first UV with its own tiling.
            S.SetTexture(material, "_EmissionMap", (Texture)baked ?? map, mainScale, mainOffset);
        }

        // rim = smoothstep(range − sharpness, range + sharpness, (1 − N·V) ×
        // light term) × _RimIntensity × _RimTint × _RimTint.a × lerp(1, albedo,
        // _RimAlbedoTint), lit by the light and the ambient, and added. v2 names
        // the range Spread and uses 1 − Spread and 1 − Sharpness. The light term
        // is pow(N·L, _RimThreshold) (1 at 0), which keeps the rim on the lit
        // side; lilToon's shadow mask does the same.
        static void ApplyRim(SourceValues values, Material material, bool second, ICollection<string> dropped)
        {
            var intensity = values.Float("_RimIntensity", second ? .4f : 0);
            if (!values.Keyword("RIMLIGHT_ON") || !(intensity > 0) || !float.IsFinite(intensity)) { S.Set(material, "_UseRim", 0); return; }
            var range = second ? 1 - values.Float("_RimSpread", .3f) : values.Float("_RimRange", .7f);
            var sharpness = second ? 1 - values.Float("_RimSharpness", .95f) : values.Float("_RimSharpness", .1f);
            if (!Finite(new Vector3(range, sharpness, 0))) { S.Set(material, "_UseRim", 0); return; }
            var tint = values.TryVector("_RimTint", out var vector) && Finite(vector) ? (Color)vector : Color.white;
            var linear = tint.linear * (Mathf.Clamp01(tint.a) * intensity);
            var color = new Color(linear.r, linear.g, linear.b).gamma;
            S.Set(material, "_UseRim", 1);
            S.Set(material, "_RimBorder", Mathf.Clamp01(range));
            S.Set(material, "_RimBlur", Mathf.Clamp(2 * Mathf.Abs(sharpness), .001f, 1));
            S.Set(material, "_RimFresnelPower", 1);
            S.Set(material, "_RimBlendMode", 1);
            S.Set(material, "_RimEnableLighting", 1);
            S.Set(material, "_RimMainStrength", Mathf.Clamp01(values.Float("_RimAlbedoTint", second ? 1 : 0)));
            S.Set(material, "_RimShadowMask", values.Float("_RimThreshold", second ? 0 : .1f) > .0001f ? 1 : 0);
            material.SetColor("_RimColor", new Color(color.r, color.g, color.b, 1));
            if (values.Float("_RimEnvironmentTint", 0) > 0) S.Drop(dropped, "Rim Environment Tint");
        }

        // v2 layer n (MATCAP_n) adds matcap × tint × lerp(1, albedo,
        // TintToDiffuse) lit by the ambient and half the light (Additive),
        // multiplies by it unlit (Multiply) or subtracts it (Subtract), by
        // Strength × lerp(1, _MatcapsMask[channel], MaskStrength). lilToon has
        // two matcaps.
        static void ApplyMatcap(SourceValues values, Material material, int index, string target, List<Texture2D> textures, ICollection<string> dropped)
        {
            string P(string name) => "_Matcap" + index + name;
            var toggle = "_UseMatCap" + target;
            if (!values.Keyword("MATCAP_" + index) || !values.Texture(P("Tex"), out var matcap, out _, out _) || !S.Flat(matcap))
            {
                S.Set(material, toggle, 0);
                return;
            }
            var blend = Mathf.RoundToInt(values.Float(P("BlendMode"), 0));
            var strength = Mathf.Clamp01(values.Float(P("Strength"), 1));
            if (blend == 2) S.Drop(dropped, "Matcap Subtract");
            if (blend == 2 || strength <= 0) { S.Set(material, toggle, 0); return; }
            var tint = values.TryVector(P("Tint"), out var vector) && Finite(vector) ? (Color)vector : Color.white;
            S.Set(material, toggle, 1);
            S.SetTexture(material, "_MatCap" + target + "Tex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCap" + target + "Color", new Color(tint.r, tint.g, tint.b, 1));
            S.Set(material, "_MatCap" + target + "Blend", strength);
            S.Set(material, "_MatCap" + target + "BlendMode", blend == 1 ? 3 : 1);
            S.Set(material, "_MatCap" + target + "MainStrength", Mathf.Clamp01(values.Float(P("TintToDiffuse"), 0)));
            S.Set(material, "_MatCap" + target + "EnableLighting", blend == 1 ? 0 : 1);
            S.Set(material, "_MatCap" + target + "ShadowMask", 0);
            var maskStrength = Mathf.Clamp01(values.Float(P("MaskStrength"), 1));
            if (maskStrength > 0 && values.Texture("_MatcapsMask", out var mask, out var scale, out var offset) && S.Flat(mask))
            {
                var channel = Mathf.Clamp(Mathf.RoundToInt(values.Float(P("MaskChannel"), 0)), 0, 3);
                var copy = Bake(mask, "__VRVlog_MatcapMask" + "RGBA"[channel], textures, true, p =>
                {
                    var v = Mathf.Lerp(1, Mathf.Clamp01(p[channel]), maskStrength);
                    return new Color(v, v, v, 1);
                });
                if (copy == null) S.Drop(dropped, "Matcap Mask");
                else
                {
                    S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
                    S.SetTexture(material, "_MatCap" + target + "BlendMask", copy, scale, offset);
                }
                if (values.Float("_MatcapsMaskUVSet", 0) != 0) S.Drop(dropped, "Matcap Mask UV Set");
            }
        }

        // First version, REFLECTION_MODE_MATCAP: the matcap × lerp(1, albedo,
        // _MatcapTintToDiffuse) is the reflection, added (or multiplied) by
        // _ReflectivityLevel × _ReflectivityMask.r without lighting.
        static void ApplyReflectionMatcap(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!values.Keyword("REFLECTION_MODE_MATCAP")) return;
            var blend = Mathf.RoundToInt(values.Float("_ReflectionBlendMode", 0));
            var level = Mathf.Clamp01(values.Float("_ReflectivityLevel", .5f));
            if (blend == 2) S.Drop(dropped, "Reflection Subtract");
            if (blend == 2 || level <= 0 || !values.Texture("_Matcap", out var matcap, out _, out _) || !S.Flat(matcap)) return;
            S.Set(material, "_UseMatCap", 1);
            S.SetTexture(material, "_MatCapTex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCapColor", Color.white);
            S.Set(material, "_MatCapBlend", level);
            S.Set(material, "_MatCapBlendMode", blend == 1 ? 3 : 1);
            S.Set(material, "_MatCapMainStrength", Mathf.Clamp01(values.Float("_MatcapTintToDiffuse", 0)));
            S.Set(material, "_MatCapEnableLighting", 0);
            S.Set(material, "_MatCapShadowMask", 0);
            ReflectivityMask(values, material, "_MatCapBlendMask", textures, dropped);
        }

        static void ReflectivityMask(SourceValues values, Material material, string target, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!values.Texture("_ReflectivityMask", out var mask, out _, out _) || !S.Flat(mask)) return;
            var copy = S.Grey(mask) ? mask : S.BakeMask(mask, 0, false, textures);
            if (copy == null) { S.Drop(dropped, "Reflectivity Mask"); return; }
            // Tiled by _ReflectivityMaskTiling on the main image's UV, or on the
            // first UV when the tiling mode is Independent.
            var tiling = values.Float("_ReflectivityMaskTiling", 1);
            if (!float.IsFinite(tiling)) tiling = 1;
            var scale = new Vector2(tiling, tiling);
            var offset = Vector2.zero;
            if (values.Float("_ReflectivityMaskTilingMode", 0) >= .5f)
                S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
            S.SetTexture(material, target, copy, scale, offset);
        }

        // PBR reflections (first version: REFLECTION_MODE_PBR or BAKED_CUBEMAP;
        // v2: REFLECTIONS_ON). Metallic and smoothness are _Metallic and
        // _Smoothness, or with a map wider than 8 pixels its red and alpha
        // (1 − alpha in roughness mode) remapped from 0..1 to the remap
        // ranges. v2's f0 for non-metals is 0.16 × _ReflectivityLevel².
        static void ApplyReflection(SourceValues values, Material material, bool second, List<Texture2D> textures, ICollection<string> dropped)
        {
            var on = second ? values.Keyword("REFLECTIONS_ON") : values.Keyword("REFLECTION_MODE_PBR") || values.Keyword("REFLECTION_MODE_BAKED_CUBEMAP");
            if (!on) return;
            S.Set(material, "_UseReflection", 1);
            S.Set(material, "_ApplyReflection", 1);
            S.Set(material, "_ApplySpecular", 0);
            S.Set(material, "_SpecularToon", 0);
            var level = Mathf.Clamp01(values.Float("_ReflectivityLevel", .5f));
            S.Set(material, "_Reflectance", Mathf.LinearToGammaSpace(second ? .16f * level * level : .04f));
            Vector2 Remap(string name) => values.TryVector(name, out var v) && Finite(v) ? new Vector2(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y)) : new Vector2(0, 1);
            if (values.Texture("_MetallicGlossMap", out var map, out var scale, out var offset) && S.Flat(map) && map.width > 8)
            {
                var metal = Remap("_MetallicRemap");
                var smooth = Remap("_SmoothnessRemap");
                var rough = values.Float("_RoughnessMode", 0) >= .5f;
                var metallic = Bake(map, "__VRVlog_Metallic", textures, true, p =>
                {
                    var v = Mathf.Lerp(metal.x, metal.y, Mathf.Clamp01(p.r));
                    return new Color(v, v, v, 1);
                });
                var smoothness = Bake(map, "__VRVlog_Smoothness", textures, true, p =>
                {
                    var a = Mathf.Clamp01(p.a);
                    var v = Mathf.Lerp(smooth.x, smooth.y, rough ? 1 - a : a);
                    return new Color(v, v, v, 1);
                });
                // v2 samples it at the first UV with its own tiling; the first
                // version on the main image's UV × _MetallicGlossMapTiling.
                if (!second)
                {
                    var tiling = values.Float("_MetallicGlossMapTiling", 1);
                    if (!float.IsFinite(tiling)) tiling = 1;
                    scale = new Vector2(tiling, tiling);
                    offset = Vector2.zero;
                }
                if (second || values.Float("_MetallicGlossMapTilingMode", 0) >= .5f)
                    S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
                if (metallic != null && smoothness != null)
                {
                    S.Set(material, "_Metallic", 1);
                    S.Set(material, "_Smoothness", 1);
                    S.SetTexture(material, "_MetallicGlossMap", metallic, scale, offset);
                    S.SetTexture(material, "_SmoothnessTex", smoothness, scale, offset);
                }
                else
                {
                    S.Drop(dropped, "Metallic Smoothness Map");
                    S.Set(material, "_Metallic", Mathf.LinearToGammaSpace(Mathf.Clamp01(values.Float("_Metallic", 0))));
                    S.Set(material, "_Smoothness", Mathf.Clamp01(values.Float("_Smoothness", .5f)));
                }
            }
            else
            {
                S.Set(material, "_Metallic", Mathf.LinearToGammaSpace(Mathf.Clamp01(values.Float("_Metallic", 0))));
                S.Set(material, "_Smoothness", Mathf.Clamp01(values.Float("_Smoothness", .5f)));
            }
            if (values.Float("_ReflectionAnisotropy", 0) != 0) S.Drop(dropped, "Reflection Anisotropy");
            if (second)
            {
                if (values.Float("_ReflectionStrength", 1) < 1) S.Drop(dropped, "Reflection Strength");
                if (values.Texture("_ReflectionMask", out var mask, out _, out _) && S.Flat(mask) && values.Float("_ReflectionMaskStrength", 1) > 0)
                    S.Drop(dropped, "Reflection Mask");
            }
            else
            {
                if (values.Float("_ReflectionBlendMode", 0) >= .5f) S.Drop(dropped, "Reflection Blend Mode");
                if (values.Texture("_ReflectivityMask", out var mask, out _, out _) && S.Flat(mask)) S.Drop(dropped, "Reflectivity Mask");
            }
        }

        // The source's pixels as it samples them (linear), mapped by f and saved
        // at up to 2048 pixels, the largest size the export keeps; null
        // without a graphics device.
        static Texture2D Bake(Texture source, string suffix, List<Texture2D> textures, bool linear, Func<Color, Color> f)
        {
            if (textures == null) return null;
            var fit = Mathf.Min(1, 2048f / Mathf.Max(source.width, source.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * fit));
            var pixels = S.Sampled(source, width, height);
            if (pixels == null) return null;
            for (var i = 0; i < pixels.Length; i++) pixels[i] = f(pixels[i]);
            return S.NewImage(source, suffix, width, height, pixels, textures, linear);
        }
    }
}
