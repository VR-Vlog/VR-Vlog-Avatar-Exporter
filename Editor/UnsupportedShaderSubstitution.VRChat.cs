using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // The shaders the VRChat SDK ships, each mapped by one row of the table
    // below onto official lilToon. A row names how the shader lights the
    // surface and blends it; its feature step writes the lilToon values of the
    // same role and names what lilToon has no counterpart for. The generic
    // mapping runs first (main image and color, normal map, emission, culling).
    internal static partial class UnsupportedShaderSubstitution
    {
        // Lambert: albedo × (light × N·L + ambient). Flat: albedo × (light +
        // ambient), the same on every side. Unlit: the albedo as it is.
        enum VrcLight { Lambert, Flat, Unlit }
        enum VrcBlend { Opaque, Alpha, Additive, Multiply }

        sealed class VRChatRow
        {
            internal readonly VrcLight Light;
            internal readonly VrcBlend Blend;
            internal readonly bool CullOff;
            internal readonly bool Outline;
            internal readonly Action<SourceValues, Material, List<Texture2D>, List<string>> Features;

            internal VRChatRow(VrcLight light, VrcBlend blend, bool cullOff = false, bool outline = false,
                Action<SourceValues, Material, List<Texture2D>, List<string>> features = null)
            {
                Light = light; Blend = blend; CullOff = cullOff; Outline = outline; Features = features;
            }
        }

        // Keyed by the exact shader name (VRChat SDK 3.10). Both shader files
        // named "Bumped Mapped Specular" read _SpecColor if declared, else white.
        internal static IEnumerable<string> VRChatShaderNames => VRChatShaders.Keys;

        static readonly Dictionary<string, VRChatRow> VRChatShaders = new Dictionary<string, VRChatRow>(StringComparer.Ordinal)
        {
            ["VRChat/Mobile/Toon Standard"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque, features: VrcToonStandard),
            ["VRChat/Mobile/Toon Standard (Outline)"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque, outline: true, features: VrcToonStandard),
            ["VRChat/Mobile/Standard Lite"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque, features: VrcStandardLite),
            ["VRChat/Mobile/Diffuse"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque),
            ["VRChat/Mobile/Bumped Diffuse"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque),
            ["VRChat/Mobile/Bumped Mapped Specular"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Opaque, features: VrcBlinnPhong),
            ["VRChat/Mobile/Toon Lit"] = new VRChatRow(VrcLight.Flat, VrcBlend.Opaque),
            ["VRChat/Mobile/MatCap Lit"] = new VRChatRow(VrcLight.Flat, VrcBlend.Opaque, features: VrcMatcapLit),
            // Avatars carry no lightmap; the image is drawn as it is.
            ["VRChat/Mobile/Lightmapped"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Opaque),
            ["VRChat/Mobile/Particles/Additive"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Additive, cullOff: true),
            ["VRChat/Mobile/Particles/Alpha Blended"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Alpha, cullOff: true),
            ["VRChat/Mobile/Particles/Multiply"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Multiply, cullOff: true),
            ["VRChat/Mobile/Worlds/Supersampled UI"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Alpha, cullOff: true),
            ["VRChat/Sprites/Default"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Alpha, cullOff: true),
            ["VRChat/Sprites/Diffuse"] = new VRChatRow(VrcLight.Lambert, VrcBlend.Alpha, cullOff: true),
            ["Video/RealtimeEmissiveGamma"] = new VRChatRow(VrcLight.Flat, VrcBlend.Opaque, features: VrcVideo),
            ["VRChat/Panosphere"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Opaque, features: (v, m, t, e) => Drop(e, "Panorama Projection")),
            ["VRChat/Mobile/Skybox"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Opaque, cullOff: true, features: (v, m, t, e) => Drop(e, "Cubemap")),
            ["FX/MirrorReflection"] = new VRChatRow(VrcLight.Unlit, VrcBlend.Opaque, features: (v, m, t, e) => Drop(e, "Mirror Reflection")),
        };

        static Material FromVRChat(Material source, SourceValues values, VRChatRow row, List<Texture2D> textures, out bool colorSkipped,
            out IReadOnlyList<string> dropped)
        {
            dropped = Array.Empty<string>();
            var material = FromGeneric(source, values, row.Outline, textures, out colorSkipped,
                row.Blend == VrcBlend.Opaque ? Mode.Opaque : Mode.Transparent);
            if (material == null) return null;
            // The rows name what lilToon leaves out; Toon Standard's hue shift
            // is baked below instead of reported as a skipped adjustment.
            colorSkipped = false;
            switch (row.Light)
            {
                case VrcLight.Lambert:
                    // lilToon's edge at border 0.75, blur 0.5 on N·L × 0.5 + 0.5
                    // is saturate(N·L); a black shadow color leaves the
                    // ambient light, which _ShadowEnvStrength adds back.
                    Set(material, "_UseShadow", 1);
                    Set(material, "_ShadowBorder", .75f);
                    Set(material, "_ShadowBlur", .5f);
                    Set(material, "_ShadowStrength", 1);
                    Set(material, "_ShadowMainStrength", 0);
                    Set(material, "_ShadowEnvStrength", 1);
                    Set(material, "_ShadowBorderRange", 0);
                    material.SetColor("_ShadowColor", Color.black);
                    Reset(material, "_ShadowColorTex");
                    break;
                case VrcLight.Flat:
                    Set(material, "_UseShadow", 0);
                    break;
                case VrcLight.Unlit:
                    Set(material, "_UseShadow", 0);
                    Set(material, "_AsUnlit", 1);
                    break;
            }
            switch (row.Blend)
            {
                case VrcBlend.Alpha:
                    Set(material, "_ZWrite", 0);
                    break;
                case VrcBlend.Additive:
                    // lilToon's transparent output is premultiplied: One/One adds
                    // color × alpha like SrcAlpha/One. The per-light pass adds nothing.
                    Set(material, "_SrcBlend", (int)BlendMode.One);
                    Set(material, "_DstBlend", (int)BlendMode.One);
                    Set(material, "_SrcBlendFA", (int)BlendMode.Zero);
                    Set(material, "_DstBlendFA", (int)BlendMode.One);
                    Set(material, "_ZWrite", 0);
                    break;
                case VrcBlend.Multiply:
                    ApplyMultiply(material);
                    Set(material, "_ZWrite", 0);
                    break;
            }
            if (row.CullOff) Set(material, "_Cull", (int)CullMode.Off);
            if (row.Features == null) return material;
            var effects = new List<string>();
            row.Features(values, material, textures, effects);
            dropped = effects;
            return material;
        }

        // Lambert diffuse with Unity's Standard specular (GGX) and reflection.
        static void VrcStandardLite(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            Set(material, "_UseReflection", 1);
            // Both shaders mark _Metallic [Gamma], so the value is copied.
            Set(material, "_Metallic", Mathf.Clamp01(values.Float("_Metallic", 1)));
            Set(material, "_Smoothness", Mathf.Clamp01(values.Float("_Glossiness", 1)));
            Set(material, "_Reflectance", Mathf.LinearToGammaSpace(.04f));
            Set(material, "_SpecularToon", 0);
            Set(material, "_ApplySpecular", values.Keyword("_SPECULARHIGHLIGHTS_OFF") ? 0 : 1);
            Set(material, "_ApplyReflection", values.Keyword("_GLOSSYREFLECTIONS_OFF") ? 0 : 1);
            // Metallic in R and smoothness in A, sampled through the main UV
            // like lilToon's own maps.
            if (values.Texture("_MetallicGlossMap", out var map, out _, out _) && Flat(map))
            {
                SetTexture(material, "_MetallicGlossMap", map, Vector2.one, Vector2.zero);
                var smoothness = BakeMask(map, 3, false, textures);
                if (smoothness != null) SetTexture(material, "_SmoothnessTex", smoothness, Vector2.one, Vector2.zero);
                else Drop(effects, "Smoothness Map");
            }
            if (values.Texture("_OcclusionMap", out var occlusion, out _, out _) && Flat(occlusion) && values.Float("_OcclusionStrength", 1) > 0)
                Drop(effects, "Occlusion");
            if (!values.Keyword("_DETAIL")) return;
            values.Texture("_DetailAlbedoMap", out var detailAlbedo, out var detailScale, out var detailOffset);
            if (Flat(detailAlbedo)) Drop(effects, "Detail Albedo x2");
            if (values.Texture("_DetailNormalMap", out var detailNormal, out _, out _) && Flat(detailNormal))
            {
                Set(material, "_UseBump2ndMap", 1);
                SetTexture(material, "_Bump2ndMap", detailNormal, detailScale, detailOffset);
                Set(material, "_Bump2ndScale", values.Float("_DetailNormalMapScale", 1));
                Set(material, "_Bump2ndMap_UVMode", values.Float("_UVSec", 0) > .5f ? 1 : 0);
                if (values.Texture("_DetailMask", out var mask, out _, out _) && Flat(mask))
                {
                    var baked = BakeMask(mask, 3, false, textures);
                    if (baked != null) SetTexture(material, "_Bump2ndScaleMask", baked, Vector2.one, Vector2.zero);
                    else { Set(material, "_UseBump2ndMap", 0); Drop(effects, "Detail Mask"); }
                }
            }
        }

        // Blinn-Phong: light × pow(N·H, shininess × 128) × gloss (main alpha)
        // × specular color. lilToon's toon specular at border 0.5, blur 1 is
        // pow(N·H, 1 / roughness) with roughness (1 - smoothness)².
        static void VrcBlinnPhong(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            var exponent = Mathf.Max(1, values.Float("_Shininess", .078125f) * 128);
            Set(material, "_UseReflection", 1);
            Set(material, "_Metallic", 0);
            Set(material, "_Smoothness", 1 - Mathf.Sqrt(1 / exponent));
            Set(material, "_SpecularToon", 1);
            Set(material, "_SpecularBorder", .5f);
            Set(material, "_SpecularBlur", 1);
            Set(material, "_ApplySpecular", 1);
            Set(material, "_ApplyReflection", 0);
            var color = values.TryVector("_SpecColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            material.SetColor("_ReflectionColor", new Color(color.r, color.g, color.b, 1));
            if (values.Texture("_MainTex", out var main, out _, out _) && Flat(main))
            {
                var gloss = BakeMask(main, 3, false, textures);
                if (gloss != null) SetTexture(material, "_ReflectionColorTex", gloss, Vector2.one, Vector2.zero);
                else Drop(effects, "Gloss (A)");
            }
        }

        // albedo × matcap, lit like the albedo.
        static void VrcMatcapLit(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            if (!values.Texture("_MatCap", out var matcap, out _, out _) || !Flat(matcap)) return;
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCapColor", Color.white);
            Set(material, "_MatCapBlend", 1);
            Set(material, "_MatCapBlendMode", 3);
            Set(material, "_MatCapMainStrength", 0);
            Set(material, "_MatCapEnableLighting", 0);
            Set(material, "_MatCapZRotCancel", 1);
        }

        // Black albedo and the image as emission × _Emission.
        static void VrcVideo(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            material.SetColor("_Color", new Color(0, 0, 0, 1));
            values.Texture("_MainTex", out var main, out var scale, out var offset);
            Set(material, "_UseEmission", 1);
            SetTexture(material, "_EmissionMap", Flat(main) ? main : null, scale, offset);
            var strength = Mathf.Max(0, values.Float("_Emission", 1));
            var emission = new Color(strength, strength, strength).gamma;
            material.SetColor("_EmissionColor", new Color(emission.r, emission.g, emission.b, 1));
            if (values.Float("_ApplyGamma", 0) > .5f || values.Keyword("APPLY_GAMMA")) Drop(effects, "Apply Gamma");
        }

        // Toon Standard: a shadow ramp, PBR specular, one matcap, a rim light,
        // detail maps, hue shift, a four-color mask, emission and (in the
        // outline variant) an outline.
        static void VrcToonStandard(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            if (values.Float("_VertexColor", 0) > .5f) Drop(effects, "Apply Vertex Color");
            if (!values.Keyword("USE_NORMAL_MAPS")) Set(material, "_UseBumpMap", 0);
            VrcToonShade(values, material);
            Set(material, "_LightMaxLimit", values.Float("_LimitBrightness", 1) > .5f ? 1 : 10);
            Set(material, "_LightMinLimit", Mathf.Clamp01(values.Float("_MinBrightness", 0)));
            Set(material, "_EmissionMap_UVMode", values.Float("_EmissionUV", 0) > .5f ? 1 : 0);
            VrcToonColor(values, material, textures, effects);
            if (values.Keyword("USE_SPECULAR")) VrcToonSpecular(values, material, textures, effects);
            if (values.Keyword("USE_MATCAP")) VrcToonMatcap(values, material, textures, effects);
            if (values.Keyword("USE_RIMLIGHT")) VrcToonRim(values, material);
            if (values.Keyword("USE_DETAIL_MAPS")) VrcToonDetail(values, material, textures, effects);
            if (values.Keyword("USE_OCCLUSION_MAP") && values.Texture("_OcclusionMap", out var occlusion, out _, out _) && Flat(occlusion) &&
                values.Float("_OcclusionStrength", 1) > 0) Drop(effects, "Occlusion Map");
            if (material.HasProperty("_OutlineWidth")) VrcToonOutline(values, material, textures, effects);
        }

        // brightness b = min(1, ramp(N·L × 0.5 + 0.5) + boost) lerps the lit
        // albedo between albedo × shadow tint and 1. lilToon reaches the same
        // shadow side with _ShadowColor = tint multiplied by the albedo again
        // (_ShadowMainStrength 1), its lowest factor 1 - _ShadowStrength at b's
        // dark end and its edge fitted to b. The ramp's color is read as grey.
        static void VrcToonShade(SourceValues values, Material material)
        {
            var boost = Mathf.Clamp01(values.Float("_ShadowBoost", 0));
            var tint = Mathf.Clamp01(values.Float("_ShadowAlbedo", .5f));
            if (!values.Texture("_Ramp", out var ramp, out _, out _) || !Flat(ramp)) { Set(material, "_UseShadow", 0); return; }
            material.SetColor("_ShadowColor", new Color(tint, tint, tint, 1).gamma);
            Set(material, "_ShadowMainStrength", 1);
            var row = ReadRow(ramp);
            // Without a graphics device the ramp cannot be read; the Lambert edge stands in.
            if (row == null) return;
            const int Steps = 128;
            var brightness = new float[Steps + 1];
            for (var i = 0; i <= Steps; i++)
            {
                var x = Mathf.Clamp(i / (float)Steps * row.Length - .5f, 0, row.Length - 1);
                var j = Mathf.Min((int)x, row.Length - 2);
                var c = Color.Lerp(row[j], row[j + 1], x - j);
                brightness[i] = Mathf.Min(1, (c.r + c.g + c.b) / 3 + boost);
            }
            if (FitShadowEdge(brightness, material))
            {
                Set(material, "_ShadowStrength", Mathf.Clamp01(1 - brightness[0]));
                return;
            }
            // Both ends match: the ramp darkens every angle by about its mean,
            // so the whole surface becomes shadow at that strength.
            var mean = 0f;
            foreach (var b in brightness) mean += b;
            mean /= brightness.Length;
            if (mean > 1 - 1e-3f) { Set(material, "_UseShadow", 0); return; }
            Set(material, "_ShadowBorder", 1);
            Set(material, "_ShadowBlur", .001f);
            Set(material, "_ShadowStrength", 1 - mean);
        }

        // f0 = 0.16 × reflectance² for dielectrics and the albedo for metals,
        // as lilToon's _Reflectance and _Metallic mix them (both [Gamma] in
        // lilToon, so the linear values are stored in gamma). Reflection probes
        // are used; the gloss map scales smoothness and reflectance alike.
        static void VrcToonSpecular(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            Set(material, "_UseReflection", 1);
            Set(material, "_SpecularToon", 0);
            Set(material, "_ApplySpecular", 1);
            Set(material, "_ApplyReflection", 1);
            // lilToon's _Metallic is [Gamma]; the source's is linear.
            Set(material, "_Metallic", Mathf.LinearToGammaSpace(Mathf.Clamp01(values.Float("_MetallicStrength", 0))));
            Set(material, "_Smoothness", Mathf.Clamp01(values.Float("_GlossStrength", .5f)));
            var reflectance = Mathf.Clamp01(values.Float("_Reflectance", .5f));
            Set(material, "_Reflectance", Mathf.LinearToGammaSpace(.16f * reflectance * reflectance));
            CopyMask(values, material, "_MetallicMap", "_MetallicMapChannel", "", "_MetallicGlossMap", "", textures, effects, "Metallic Map");
            CopyMask(values, material, "_GlossMap", "_GlossMapChannel", "", "_SmoothnessTex", "", textures, effects, "Gloss Map");
            if (values.Float("_SpecularSharpness", 0) > 0) Drop(effects, "Specular Sharpness");
        }

        // Additive adds the lit matcap to the albedo; multiplicative scales the
        // albedo by it. Both use the world-up VR matcap UV, lilToon's Z
        // rotation cancel.
        static void VrcToonMatcap(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            if (!values.Texture("_Matcap", out var matcap, out _, out _) || !Flat(matcap)) return;
            var strength = Mathf.Clamp01(values.Float("_MatcapStrength", 1));
            if (strength <= 0) return;
            var multiply = values.Float("_MatcapType", 0) > .5f;
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCapColor", Color.white);
            Set(material, "_MatCapBlend", strength);
            Set(material, "_MatCapBlendMode", multiply ? 3 : 1);
            Set(material, "_MatCapMainStrength", 0);
            Set(material, "_MatCapEnableLighting", multiply ? 0 : 1);
            Set(material, "_MatCapZRotCancel", 1);
            CopyMask(values, material, "_MatcapMask", "_MatcapMaskChannel", "", "_MatCapBlendMask", "_UseMatCap", textures, effects, "Matcap Mask");
        }

        // smoothstep(r - s, r + s, 1 - |N·V|) × intensity × color with r = 1 -
        // range; lilToon's edge at border r, blur 2s is the linear ramp over
        // the same span.
        static void VrcToonRim(SourceValues values, Material material)
        {
            var intensity = Mathf.Clamp01(values.Float("_RimIntensity", .5f));
            if (intensity <= 0) return;
            var color = values.TryVector("_RimColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            var scaled = (color.linear * intensity).gamma;
            Set(material, "_UseRim", 1);
            material.SetColor("_RimColor", new Color(scaled.r, scaled.g, scaled.b, 1));
            Set(material, "_RimBorder", Mathf.Clamp01(1 - values.Float("_RimRange", .3f)));
            Set(material, "_RimBlur", Mathf.Clamp(2 * values.Float("_RimSharpness", .1f), .001f, 1));
            Set(material, "_RimFresnelPower", 1);
            Set(material, "_RimMainStrength", Mathf.Clamp01(values.Float("_RimAlbedoTint", 0)));
            Set(material, "_RimEnableLighting", values.Float("_RimEnvironmental", 0) > .5f ? 1 : 0);
            Set(material, "_RimBlendMode", 1);
            Set(material, "_RimShadowMask", 0);
        }

        // The detail image blends over the albedo by mode (alpha blend, add,
        // multiply) through a mask on UV0; the detail normal map's scale is
        // multiplied by the same mask.
        static void VrcToonDetail(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            var uv = values.Float("_DetailUV", 0) > .5f ? 1 : 0;
            var mode = Mathf.RoundToInt(values.Float("_DetailMode", 0));
            if (values.Texture("_DetailAlbedoMap", out var detail, out var scale, out var offset) && Flat(detail))
            {
                if (mode == 3) Drop(effects, "Detail Mode (MultiplyX2)");
                else
                {
                    Set(material, "_UseMain2ndTex", 1);
                    SetTexture(material, "_Main2ndTex", detail, scale, offset);
                    material.SetColor("_Color2nd", Color.white);
                    Set(material, "_Main2ndTexBlendMode", mode == 1 ? 1 : mode == 2 ? 3 : 0);
                    Set(material, "_Main2ndTex_UVMode", uv);
                    CopyMask(values, material, "_DetailMask", "_DetailMaskChannel", "", "_Main2ndBlendMask", "_UseMain2ndTex", textures, effects, "Detail Mask");
                }
                if (values.Keyword("USE_HUE_SHIFT") && HueTurn(values.Float("_DetailHueShift", 0)) != 0) Drop(effects, "Detail Hue Shift");
            }
            if (values.Keyword("USE_NORMAL_MAPS") && values.Texture("_DetailNormalMap", out var normal, out scale, out offset) && Flat(normal))
            {
                Set(material, "_UseBump2ndMap", 1);
                SetTexture(material, "_Bump2ndMap", normal, scale, offset);
                Set(material, "_Bump2ndScale", values.Float("_DetailNormalMapScale", 1));
                Set(material, "_Bump2ndMap_UVMode", uv);
                CopyMask(values, material, "_DetailMask", "_DetailMaskChannel", "", "_Bump2ndScaleMask", "_UseBump2ndMap", textures, effects, "Detail Mask");
            }
        }

        // Pushed out along the normal by thickness × mask centimeters, drawn
        // unlit in lerp(outline color, albedo, from albedo).
        static void VrcToonOutline(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            Set(material, "_OutlineWidth", Mathf.Max(0, values.Float("_OutlineThickness", .05f)));
            Set(material, "_OutlineFixWidth", 0);
            Set(material, "_OutlineEnableLighting", 0);
            var color = values.TryVector("_OutlineColor", out var authored) && Finite(authored) ? (Color)authored : Color.black;
            var fromAlbedo = Mathf.Clamp01(values.Float("_OutlineFromAlbedo", 0));
            var main = material.GetTexture("_MainTex");
            var mainScale = material.GetTextureScale("_MainTex");
            var mainOffset = material.GetTextureOffset("_MainTex");
            var tint = material.GetColor("_Color");
            var outline = Color.Lerp(color, tint, fromAlbedo);
            if (main != null && fromAlbedo > 1e-3f)
            {
                // lilToon multiplies the outline color by the image, so a mix
                // other than all albedo is baked into an outline image.
                var image = fromAlbedo > 1 - 1e-3f ? main : OutlineImage(main, color, tint, fromAlbedo, textures);
                if (image != null)
                {
                    SetTexture(material, "_OutlineTex", image, mainScale, mainOffset);
                    if (image != main) outline = Color.white;
                }
            }
            material.SetColor("_OutlineColor", new Color(outline.r, outline.g, outline.b, 1));
            CopyMask(values, material, "_OutlineMask", "_OutlineMaskChannel", "", "_OutlineWidthMask", "", textures, effects, "Outline Mask");
        }

        // lerp(outline color, image × tint, amount) per pixel of the main image,
        // laid out like it. Null when the image cannot be read.
        static Texture2D OutlineImage(Texture main, Color color, Color tint, float amount, List<Texture2D> textures)
        {
            if (textures == null) return null;
            var size = Mathf.Min(Mathf.Max(4, main.width, main.height), 2048);
            var pixels = Sampled(main, size, size);
            if (pixels == null) return null;
            var line = color.linear;
            var linearTint = tint.linear;
            for (var i = 0; i < pixels.Length; i++)
            {
                var mixed = Color.Lerp(line, pixels[i] * linearTint, amount);
                var gamma = new Color(Mathf.Clamp01(mixed.r), Mathf.Clamp01(mixed.g), Mathf.Clamp01(mixed.b)).gamma;
                pixels[i] = new Color(gamma.r, gamma.g, gamma.b, 1);
            }
            return NewImage(main, "__VRVlog_Outline", size, pixels, textures);
        }

        // The source turns the hue in radians; a whole turn changes nothing.
        static float HueTurn(float angle)
        {
            var turn = Mathf.Repeat(angle, 2 * Mathf.PI);
            return turn < 1e-4f || turn > 2 * Mathf.PI - 1e-4f ? 0 : turn;
        }

        // Hue shift and the color mask change the albedo (and emission) after
        // the main color with formulas lilToon does not have, so the result is
        // baked into a new main image (and emission image) at the size of the
        // largest image involved, and _Color becomes white.
        static void VrcToonColor(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            var hue = values.Keyword("USE_HUE_SHIFT");
            var mask = values.Keyword("USE_COLOR_MASK");
            var albedoHue = hue ? HueTurn(values.Float("_HueShift", 0)) : 0;
            var emissionHue = hue ? HueTurn(values.Float("_EmissionHueShift", 0)) : 0;
            values.Texture("_ColorMask", out var colorMask, out var maskScale, out var maskOffset);
            if (!Flat(colorMask)) colorMask = null;
            // The default black mask changes nothing.
            mask &= colorMask != null;
            var maskEmission = new float[4];
            for (var i = 0; i < 4; i++) maskEmission[i] = mask ? Mathf.Max(0, values.Float("_ColorMaskEmissionStrength" + (i + 1), 0)) : 0;
            var bakeAlbedo = albedoHue != 0 || mask;
            var bakeEmission = emissionHue != 0 && Get(material, "_UseEmission") > .5f || maskEmission[0] + maskEmission[1] + maskEmission[2] + maskEmission[3] > 0;
            if (!bakeAlbedo && !bakeEmission) return;

            values.Texture("_HueShiftMask", out var hueMask, out var hueScale, out var hueOffset);
            if (!Flat(hueMask) || !hue) hueMask = null;
            var main = material.GetTexture("_MainTex");
            var mainScale = material.GetTextureScale("_MainTex");
            var mainOffset = material.GetTextureOffset("_MainTex");
            var emissionMap = material.GetTexture("_EmissionMap");
            var emissionScale = material.GetTextureScale("_EmissionMap");
            var emissionOffset = material.GetTextureOffset("_EmissionMap");
            // The color mask and hue mask are read on UV0; an emission image on
            // UV1 cannot be baked against them.
            if (bakeEmission && values.Float("_EmissionUV", 0) > .5f)
            {
                if (emissionHue != 0) Drop(effects, "Emission Hue Shift");
                if (maskEmission[0] + maskEmission[1] + maskEmission[2] + maskEmission[3] > 0) Drop(effects, "Color Mask Emission");
                bakeEmission = false;
            }
            var size = 4;
            foreach (var texture in new[] { main, hueMask, colorMask, bakeEmission ? emissionMap : null })
                if (texture != null) size = Mathf.Max(size, texture.width, texture.height);
            size = Mathf.Min(size, 2048);
            // When every image involved has the same tiling and offset, the
            // result is baked in image space and keeps them, so meshes whose UVs
            // leave 0 to 1 repeat it as before. Otherwise it is baked over the
            // mesh's 0 to 1 UV square.
            var involved = new List<(Texture texture, Vector2 scale, Vector2 offset)>
            {
                (main, mainScale, mainOffset), (hueMask, hueScale, hueOffset), (colorMask, maskScale, maskOffset),
            };
            if (bakeEmission && Get(material, "_UseEmission") > .5f) involved.Add((emissionMap, emissionScale, emissionOffset));
            var present = involved.FindAll(entry => entry.texture != null);
            var shared = present.TrueForAll(entry => entry.scale == present[0].scale && entry.offset == present[0].offset);
            var bakedScale = shared && present.Count > 0 ? present[0].scale : Vector2.one;
            var bakedOffset = shared && present.Count > 0 ? present[0].offset : Vector2.zero;

            Color[] Read(Texture texture, Vector2 scale, Vector2 offset, Color fallback)
            {
                if (texture == null)
                {
                    var flat = new Color[size * size];
                    for (var i = 0; i < flat.Length; i++) flat[i] = fallback;
                    return flat;
                }
                return shared ? Sampled(texture, size, size) : Sampled(texture, size, size, scale, offset);
            }
            var hueValues = Read(hueMask, hueScale, hueOffset, Color.white);
            var maskValues = Read(colorMask, maskScale, maskOffset, Color.clear);
            var channel = Mathf.Clamp(Mathf.RoundToInt(values.Float("_HueShiftMaskChannel", 1)), 0, 3);
            var maskColors = new Color[4];
            for (var i = 0; i < 4; i++)
            {
                var c = values.TryVector("_ColorMaskColor" + (i + 1), out var authored) && Finite(authored) ? (Color)authored : Color.white;
                maskColors[i] = new Color(c.linear.r, c.linear.g, c.linear.b, c.a);
            }
            var additive = values.Float("_ColorMaskBlendMode", 0) > .5f;

            if (bakeAlbedo)
            {
                var pixels = Read(main, mainScale, mainOffset, Color.white);
                var baked = pixels == null || hueValues == null || maskValues == null ? null : new Color[pixels.Length];
                if (baked != null)
                {
                    var tint = material.GetColor("_Color");
                    var linearTint = new Color(tint.linear.r, tint.linear.g, tint.linear.b, tint.a);
                    for (var i = 0; i < baked.Length; i++)
                    {
                        var c = pixels[i] * linearTint;
                        var rgb = new Vector3(c.r, c.g, c.b);
                        if (albedoHue != 0) rgb = ApplyHue(rgb, albedoHue, hueValues[i][channel]);
                        if (mask) rgb = ApplyColorMask(rgb, maskValues[i], maskColors, additive);
                        var gamma = new Color(Mathf.Clamp01(rgb.x), Mathf.Clamp01(rgb.y), Mathf.Clamp01(rgb.z)).gamma;
                        baked[i] = new Color(gamma.r, gamma.g, gamma.b, Mathf.Clamp01(c.a));
                    }
                }
                var image = baked == null ? null : NewImage(main, "__VRVlog_Albedo", size, baked, textures);
                if (image == null)
                {
                    if (albedoHue != 0) Drop(effects, "Hue Shift");
                    if (mask) Drop(effects, "Color Mask");
                }
                else
                {
                    SetTexture(material, "_MainTex", image, bakedScale, bakedOffset);
                    material.SetColor("_Color", Color.white);
                }
            }

            if (!bakeEmission) return;
            {
                var on = Get(material, "_UseEmission") > .5f;
                var color = on ? material.GetColor("_EmissionColor").linear : Color.black;
                var pixels = on ? Read(emissionMap, emissionScale, emissionOffset, Color.white) : Read(null, Vector2.one, Vector2.zero, Color.white);
                var baked = pixels == null || hueValues == null || maskValues == null ? null : new Vector3[pixels.Length];
                var peak = 0f;
                if (baked != null)
                    for (var i = 0; i < baked.Length; i++)
                    {
                        var e = new Vector3(pixels[i].r * color.r, pixels[i].g * color.g, pixels[i].b * color.b);
                        if (emissionHue != 0) e = ApplyHue(e, emissionHue, hueValues[i][channel]);
                        for (var k = 0; k < 4; k++)
                        {
                            var weight = maskValues[i][k] * maskEmission[k];
                            e += new Vector3(maskColors[k].r, maskColors[k].g, maskColors[k].b) * weight;
                        }
                        e = Vector3.Max(e, Vector3.zero);
                        baked[i] = e;
                        peak = Mathf.Max(peak, e.x, e.y, e.z);
                    }
                if (baked == null)
                {
                    if (emissionHue != 0) Drop(effects, "Emission Hue Shift");
                    if (maskEmission[0] + maskEmission[1] + maskEmission[2] + maskEmission[3] > 0) Drop(effects, "Color Mask Emission");
                    return;
                }
                if (peak <= 0) return;
                // Stored at most 1 with the scale moved into the emission color.
                var scale = Mathf.Max(1, peak);
                var stored = new Color[baked.Length];
                for (var i = 0; i < baked.Length; i++)
                {
                    var g = new Color(baked[i].x / scale, baked[i].y / scale, baked[i].z / scale).gamma;
                    stored[i] = new Color(g.r, g.g, g.b, 1);
                }
                var image = NewImage(emissionMap != null ? emissionMap : main, "__VRVlog_Emission", size, stored, textures);
                if (image == null)
                {
                    if (emissionHue != 0) Drop(effects, "Emission Hue Shift");
                    if (maskEmission[0] + maskEmission[1] + maskEmission[2] + maskEmission[3] > 0) Drop(effects, "Color Mask Emission");
                    return;
                }
                Set(material, "_UseEmission", 1);
                Set(material, "_EmissionMap_UVMode", 0);
                SetTexture(material, "_EmissionMap", image, bakedScale, bakedOffset);
                var intensity = new Color(scale, scale, scale).gamma;
                material.SetColor("_EmissionColor", new Color(intensity.r, intensity.g, intensity.b, 1));
            }
        }

        // Rotation around the grey axis by angle radians, mixed by mask.
        static Vector3 ApplyHue(Vector3 color, float angle, float mask)
        {
            var k = Vector3.one * .57735f;
            var cos = Mathf.Cos(angle);
            var shifted = color * cos + Vector3.Cross(k, color) * Mathf.Sin(angle) + k * Vector3.Dot(k, color) * (1 - cos);
            return Vector3.Lerp(color, shifted, Mathf.Clamp01(mask));
        }

        // Each mask channel replaces the albedo by its color over the grey of
        // the albedo, multiplied or added (capped at 1).
        static Vector3 ApplyColorMask(Vector3 albedo, Color mask, Color[] colors, bool additive)
        {
            var grey = Vector3.Dot(albedo, new Vector3(.3f, .59f, .11f));
            for (var i = 0; i < 4; i++)
            {
                var c = new Vector3(colors[i].r, colors[i].g, colors[i].b);
                var target = additive ? Vector3.Min(Vector3.one, Vector3.one * grey + c) : grey * c;
                albedo = Vector3.Lerp(albedo, target, Mathf.Clamp01(mask[i] * colors[i].a));
            }
            return albedo;
        }

        // An sRGB image owned by textures, with the wrap and filter of origin.
        static Texture2D NewImage(Texture origin, string suffix, int size, Color[] pixels, List<Texture2D> textures)
        {
            if (textures == null) return null;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = DeterministicNames.Unique((origin != null ? origin.name : "Color") + suffix,
                    candidate => textures.Exists(texture => texture != null && texture.name == candidate)),
                wrapModeU = origin != null ? origin.wrapModeU : TextureWrapMode.Repeat,
                wrapModeV = origin != null ? origin.wrapModeV : TextureWrapMode.Repeat,
                filterMode = origin != null ? origin.filterMode : FilterMode.Bilinear,
            };
            image.SetPixels(pixels);
            image.Apply(false, false);
            textures.Add(image);
            return image;
        }
    }
}
