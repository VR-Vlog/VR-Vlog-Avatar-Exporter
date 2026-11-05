using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using Mode = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.Mode;
using SourceValues = VRVlog.LilToonExporter.UnsupportedShaderSubstitution.SourceValues;
using S = VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Mochie Standard, Standard Lite and Standard Mobile: Unity's Standard
    // lighting with its own property names. The generic mapping reads the main
    // image and color, the cutoff, the normal map (_NormalMap), the emission
    // image and color, and _Culling. This class reads the blending mode, the
    // alpha source, the normal strength, the emission the shader turns on with
    // a keyword, the color filtering, metallic and roughness, and reports the
    // effects lilToon has no counterpart for.
    // The Mobile shader declares the same properties but is built with
    // STANDARD_MOBILE, which skips _AlphaSource, the emission mask and the
    // pulse; only its name tells it apart, so a missing shader is read as
    // Standard.
    internal sealed class MochieStandardApproximation : IShaderApproximation
    {
        // A missing shader is known by the two workflow properties only these
        // shaders declare.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null
                ? values.TryFloat("_PrimaryWorkflow", out _) && values.TryFloat("_PrimarySampleMode", out _)
                : shaderName.StartsWith("Mochie/Standard", System.StringComparison.Ordinal);

        // _BlendMode: 0 opaque, 1 cutout, 2 fade, 3 transparent (the reflection
        // stays at full strength; lilToon has one transparent mode).
        public Mode? RenderMode(SourceValues values)
        {
            if (!values.TryFloat("_BlendMode", out var blend)) return null;
            return blend >= 2 ? Mode.Transparent : blend >= 1 ? Mode.Cutout : Mode.Opaque;
        }

        public bool Outline(SourceValues values) => false;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var mobile = values.ShaderName == "Mochie/Standard Mobile";
            if (!mobile) ApplyAlpha(values, material, textures, dropped);
            ApplyNormal(values, material);
            // Both turn the normal toward the viewer on back faces.
            S.Set(material, "_FlipNormal", values.Float("_FlipBackfaceNormals", 0) >= .5f ? 1 : 0);
            ApplyFiltering(values, material, textures, dropped);
            ApplyEmission(values, material, textures, dropped, mobile);
            ApplyLambert(material);
            ApplyReflection(values, material, textures, dropped);
            ApplyUV(values, dropped);

            // Effects the shader turns on with a keyword or a toggle.
            if (values.Keyword("_PARALLAX_ON")) S.Drop(dropped, "Height Map");
            if (values.Keyword("_DETAIL_MAINTEX_ON") || values.Keyword("_DETAIL_NORMAL_ON") || values.Keyword("_DETAIL_METALLIC_ON") ||
                values.Keyword("_DETAIL_ROUGHNESS_ON") || values.Keyword("_DETAIL_OCCLUSION_ON")) S.Drop(dropped, "Detail");
            if (values.Keyword("_RAIN_DROPLETS_ON") || values.Keyword("_RAIN_RIPPLES_ON") || values.Keyword("_RAIN_AUTO_ON")) S.Drop(dropped, "Rain");
            if (values.Keyword("_SSR_ON")) S.Drop(dropped, "Screenspace Reflections");
            if (values.Keyword("_VERTEX_MANIPULATION_ON")) S.Drop(dropped, "Vertex Manipulation");
            if (values.Float("_WindToggle", 0) > .5f) S.Drop(dropped, "Wind");
            if (values.Float("_Subsurface", 0) > .5f) S.Drop(dropped, "Subsurface Scattering");
            if (values.Float("_VertexBaseColor", 0) > .5f) S.Drop(dropped, "Vertex Base Color");
        }

        // _AlphaSource: 0 the base color's alpha, 1 _AlphaMask's chosen channel
        // times _AlphaMaskOpacity in place of it, 2 the vertex color's alpha.
        // The mask has its own tiling on the first UV; lilToon's mask (mode 1
        // replaces the alpha) is tiled on top of the main image's.
        static void ApplyAlpha(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (material.GetFloat("_TransparentMode") == (int)Mode.Opaque) return;
            var source = Mathf.RoundToInt(values.Float("_AlphaSource", 0));
            if (source == 2) { S.Drop(dropped, "Vertex Color Alpha"); return; }
            if (source != 1) return;
            var opacity = Mathf.Clamp01(values.Float("_AlphaMaskOpacity", 1));
            // An empty slot reads as white, so the alpha is the opacity; lilToon's
            // empty mask is white too.
            if (!values.Texture("_AlphaMask", out var mask, out var scale, out var offset) || mask == null)
            {
                S.SetAlphaMask(material, 1, null, Vector2.one, Vector2.zero, opacity, 0);
                return;
            }
            if (!S.Flat(mask)) { S.Drop(dropped, "Alpha Mask"); return; }
            var channel = Mathf.Clamp(Mathf.RoundToInt(values.Float("_AlphaMaskChannel", 0)), 0, 3);
            var copy = channel == 0 && S.Grey(mask) ? mask : S.BakeMask(mask, channel, false, textures);
            if (copy == null) { S.Drop(dropped, "Alpha Mask"); return; }
            if (values.Float("_UVAlphaMaskSet", 0) != 0) S.Drop(dropped, "Alpha Mask UV Set");
            S.MainRelative(material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"), ref scale, ref offset);
            S.SetAlphaMask(material, 1, copy, scale, offset, opacity, 0);
        }

        // The shader reads _NormalMap only with _NORMALMAP_ON, at _NormalStrength.
        static void ApplyNormal(SourceValues values, Material material)
        {
            if (!values.Texture("_NormalMap", out var normal, out _, out _) || normal == null || material.GetTexture("_BumpMap") != normal) return;
            if (!values.Keyword("_NORMALMAP_ON"))
            {
                S.Reset(material, "_BumpMap", "_BumpScale");
                S.Set(material, "_UseBumpMap", 0);
                return;
            }
            if (values.TryFloat("_NormalStrength", out var strength)) S.Set(material, "_BumpScale", strength);
        }

        // With _Filtering on, the base color (image times _Color) is hue
        // shifted (_HueMode 0 in HSV, 1 in Oklab; _MonoTint sets the hue
        // instead of adding it), then lerp(luma, color, _Saturation), then
        // lerp(0.5, color, _Contrast), then times _Brightness. lilToon's
        // _MainTexHSVG multiplies the HSV saturation, which also changes the
        // brightness, and has no contrast, so the result is baked into the
        // main image. Without a GPU the HSV values are used instead.
        static void ApplyFiltering(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_Filtering", 0) < .5f) return;
            // The lit color is filtered again with the Post values (and ACES);
            // MToon has no counterpart.
            var post = values.Float("_HuePost", 0);
            if ((post > 0 && post < 1) || values.Float("_SaturationPost", 1) != 1 || values.Float("_BrightnessPost", 1) != 1 ||
                values.Float("_ContrastPost", 1) != 1 || values.Float("_ACES", 0) != 0) S.Drop(dropped, "Post Filtering");

            var hue = values.Float("_Hue", 0);
            var mono = values.Float("_MonoTint", 0) > .5f;
            var shifted = hue > 0 && hue < 1 || mono;
            var oklab = shifted && values.Float("_HueMode", 0) > .5f;
            var saturation = values.Float("_Saturation", 1);
            var contrast = values.Float("_Contrast", 1);
            var brightness = Mathf.Max(0, values.Float("_Brightness", 1));
            if (!Finite(new Vector4(hue, saturation, contrast, brightness))) return;
            if (!shifted && saturation == 1 && contrast == 1 && brightness == 1) return;
            if (oklab) S.Drop(dropped, "Hue (Oklab)");

            Vector3 Filter(Vector3 c)
            {
                if (shifted && !oklab)
                {
                    Color.RGBToHSV(new Color(Mathf.Max(0, c.x), Mathf.Max(0, c.y), Mathf.Max(0, c.z)), out var h, out var sat, out var v);
                    h = Mathf.Repeat((mono ? 0 : h) + hue, 1);
                    var rgb = Color.HSVToRGB(h, sat, v, true);
                    c = new Vector3(rgb.r, rgb.g, rgb.b);
                }
                var luma = Vector3.Dot(c, new Vector3(.3f, .59f, .11f));
                c = Vector3.LerpUnclamped(new Vector3(luma, luma, luma), c, saturation);
                c = Vector3.LerpUnclamped(new Vector3(.5f, .5f, .5f), c, contrast);
                return c * brightness;
            }

            var tint = material.GetColor("_Color");
            var linearTint = new Vector3(tint.linear.r, tint.linear.g, tint.linear.b);
            var main = material.GetTexture("_MainTex");
            if (main == null)
            {
                var flat = Filter(linearTint);
                var gamma = new Color(Mathf.Clamp01(flat.x), Mathf.Clamp01(flat.y), Mathf.Clamp01(flat.z)).gamma;
                material.SetColor("_Color", new Color(gamma.r, gamma.g, gamma.b, tint.a));
                return;
            }
            var fit = Mathf.Min(1, 2048f / Mathf.Max(main.width, main.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(main.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(main.height * fit));
            var pixels = S.Flat(main) ? S.Sampled(main, width, height) : null;
            Texture2D baked = null;
            if (pixels != null)
            {
                for (var i = 0; i < pixels.Length; i++)
                {
                    var p = pixels[i];
                    var c = Filter(new Vector3(p.r * linearTint.x, p.g * linearTint.y, p.b * linearTint.z));
                    var gamma = new Color(Mathf.Clamp01(c.x), Mathf.Clamp01(c.y), Mathf.Clamp01(c.z)).gamma;
                    pixels[i] = new Color(gamma.r, gamma.g, gamma.b, p.a);
                }
                baked = S.NewImage(main, "__VRVlog_Filtered", width, height, pixels, textures, false);
            }
            if (baked != null)
            {
                S.SetTexture(material, "_MainTex", baked, material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                material.SetColor("_Color", new Color(1, 1, 1, tint.a));
                return;
            }
            // lilToon adds the hue in HSV and multiplies saturation and value.
            if (mono) S.Drop(dropped, "Mono Tint");
            if (contrast != 1) S.Drop(dropped, "Contrast");
            var hsvg = new Vector4(shifted && !oklab && !mono ? hue : 0, Mathf.Max(0, saturation), brightness, 1);
            if (hsvg != new Vector4(0, 1, 1, 1)) material.SetVector("_MainTexHSVG", hsvg);
        }

        // The shader adds _EmissionMap × pow(_EmissionColor, 2.2) ×
        // _EmissionStrength, masked by one channel of _EmissionMask, only with
        // _EMISSION_ON. The color reaches the shader linear and is raised to
        // 2.2 again; lilToon reads its color linear, so the result is saved in
        // gamma.
        static void ApplyEmission(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped, bool mobile)
        {
            if (!values.TryVector("_EmissionColor", out var vector) || !values.TryFloat("_EmissionStrength", out var strength)) return;
            if (!values.Keyword("_EMISSION_ON") || !Finite(vector))
            {
                S.Reset(material, "_EmissionMap", "_EmissionColor");
                S.Set(material, "_UseEmission", 0);
                return;
            }
            var linear = ((Color)vector).linear;
            var factor = Mathf.Max(0, strength);
            if (values.Float("_Filtering", 0) > .5f)
            {
                factor *= Mathf.Max(0, values.Float("_BrightnessEmiss", 1));
                var hue = values.Float("_HueEmiss", 0);
                if (hue > 0 && hue < 1 || values.Float("_SaturationEmiss", 1) != 1 || values.Float("_ContrastEmiss", 1) != 1)
                    S.Drop(dropped, "Emission Filtering");
            }
            var color = new Color(Mathf.Pow(Mathf.Max(0, linear.r), 2.2f) * factor, Mathf.Pow(Mathf.Max(0, linear.g), 2.2f) * factor,
                Mathf.Pow(Mathf.Max(0, linear.b), 2.2f) * factor, 1);
            if (color.maxColorComponent <= 0)
            {
                S.Reset(material, "_EmissionMap", "_EmissionColor");
                S.Set(material, "_UseEmission", 0);
                return;
            }
            S.Set(material, "_UseEmission", 1);
            var gamma = color.gamma;
            material.SetColor("_EmissionColor", new Color(gamma.r, gamma.g, gamma.b, 1));
            if (values.Texture("_EmissionMap", out var map, out var scale, out var offset) && S.Flat(map)) S.SetTexture(material, "_EmissionMap", map, scale, offset);
            else S.Reset(material, "_EmissionMap");
            if (!mobile)
            {
                S.CopyMask(values, material, "_EmissionMask", "_EmissionMaskChannel", "", "_EmissionBlendMask", "_UseEmission", textures, dropped, "Emission Mask");
                if (values.Float("_EmissionPulseWave", 0) > .5f) S.Drop(dropped, "Emission Pulse");
            }
            if (values.Keyword("_AUDIOLINK_ON")) S.Drop(dropped, "Audio Link");
        }

        // Unity's Standard diffuse is Lambert. lilToon's edge at border 0.75,
        // blur 0.5 on N·L × 0.5 + 0.5 is saturate(N·L); a black shadow color
        // leaves the ambient light, which _ShadowEnvStrength adds back.
        static void ApplyLambert(Material material)
        {
            S.Set(material, "_UseShadow", 1);
            S.Set(material, "_ShadowBorder", .75f);
            S.Set(material, "_ShadowBlur", .5f);
            S.Set(material, "_ShadowStrength", 1);
            S.Set(material, "_ShadowMainStrength", 0);
            S.Set(material, "_ShadowEnvStrength", 1);
            S.Set(material, "_ShadowBorderRange", 0);
            material.SetColor("_ShadowColor", Color.black);
            S.Reset(material, "_ShadowColorTex");
        }

        // Metallic and roughness come from scalars and the green channel of
        // their maps (metallic workflow), from chosen channels of the packed
        // map (_WORKFLOW_PACKED_ON), or the specular workflow sets metallic to
        // 0. With _SmoothnessToggle the roughness value is smoothness. lilToon
        // multiplies _Metallic ([Gamma]) by its map's red and _Smoothness by
        // its map's red; the maps are baked to those values.
        static void ApplyReflection(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            S.Set(material, "_UseReflection", 1);
            S.Set(material, "_Reflectance", Mathf.LinearToGammaSpace(.04f));
            S.Set(material, "_SpecularToon", 0);
            S.Set(material, "_ApplyReflection", values.Keyword("_REFLECTIONS_ON") ? 1 : 0);
            S.Set(material, "_ApplySpecular", values.Keyword("_SPECULAR_HIGHLIGHTS_ON") ? 1 : 0);
            if (values.Keyword("_REFLECTIONS_ON") && values.Float("_ReflectionStrength", 1) != 1) S.Drop(dropped, "Reflection Strength");
            if (values.Keyword("_SPECULAR_HIGHLIGHTS_ON") && values.Float("_SpecularHighlightStrength", 1) != 1) S.Drop(dropped, "Specular Strength");
            var smoothnessFlag = values.Float("_SmoothnessToggle", 0) > .5f;
            if (values.Keyword("_WORKFLOW_PACKED_ON"))
            {
                values.Texture("_PackedMap", out var packed, out _, out _);
                Metallic(material, packed, Channel(values, "_MetallicChannel", 2), values.Float("_PackedMetallicStrength", 1), textures, dropped);
                Smoothness(material, packed, Channel(values, "_RoughnessChannel", 1), values.Float("_PackedRoughnessStrength", 1), smoothnessFlag,
                    textures, dropped);
                if (S.Flat(packed) && values.Float("_PackedOcclusionStrength", 1) > 0) S.Drop(dropped, "Occlusion");
                return;
            }
            if (values.Keyword("_WORKFLOW_SPECULAR_ON"))
            {
                S.Set(material, "_Metallic", 0);
                S.Drop(dropped, "Specular Color");
                if (values.Float("_SmoothnessSource", 0) > .5f) S.Drop(dropped, "Smoothness Source");
            }
            else
            {
                Texture metallicMap = null;
                if (values.Float("_SampleMetallic", 0) > .5f) values.Texture("_MetallicMap", out metallicMap, out _, out _);
                Metallic(material, metallicMap, 1, values.Float("_MetallicStrength", 0), textures, dropped);
            }
            Texture roughnessMap = null;
            if (values.Float("_SampleRoughness", 0) > .5f) values.Texture("_RoughnessMap", out roughnessMap, out _, out _);
            Smoothness(material, roughnessMap, 1, values.Float("_RoughnessStrength", 1), smoothnessFlag, textures, dropped);
            if (values.Float("_SampleOcclusion", 0) > .5f && values.Texture("_OcclusionMap", out var occlusion, out _, out _) && S.Flat(occlusion) &&
                values.Float("_OcclusionStrength", 1) > 0) S.Drop(dropped, "Occlusion");
        }

        static int Channel(SourceValues values, string name, int fallback) => Mathf.Clamp(Mathf.RoundToInt(values.Float(name, fallback)), 0, 3);

        // metallic = map[channel] × strength (or strength without a map).
        static void Metallic(Material material, Texture map, int channel, float strength, List<Texture2D> textures, ICollection<string> dropped)
        {
            S.Set(material, "_Metallic", Mathf.LinearToGammaSpace(Mathf.Clamp01(strength)));
            if (!S.Flat(map) || strength <= 0) return;
            var baked = S.BakeMask(map, channel, false, textures);
            if (baked != null) S.SetTexture(material, "_MetallicGlossMap", baked, Vector2.one, Vector2.zero);
            else S.Drop(dropped, "Metallic Map");
        }

        // roughness = map[channel] × strength (or strength without a map);
        // smoothness is 1 − roughness, or the value itself with the flag.
        static void Smoothness(Material material, Texture map, int channel, float strength, bool smoothness, List<Texture2D> textures,
            ICollection<string> dropped)
        {
            var value = Mathf.Clamp01(strength);
            if (!S.Flat(map))
            {
                S.Set(material, "_Smoothness", smoothness ? value : 1 - value);
                return;
            }
            var baked = S.BakeMask(map, channel, !smoothness, textures, value);
            if (baked != null)
            {
                S.Set(material, "_Smoothness", 1);
                S.SetTexture(material, "_SmoothnessTex", baked, Vector2.one, Vector2.zero);
            }
            else
            {
                S.Set(material, "_Smoothness", smoothness ? value : 1 - value);
                S.Drop(dropped, "Roughness Map");
            }
        }

        // The primary maps share _UVMainSet, scrolling and rotation; triplanar
        // sampling projects them in world or local space.
        static void ApplyUV(SourceValues values, ICollection<string> dropped)
        {
            if (values.Keyword("_TRIPLANAR_ON")) S.Drop(dropped, "Triplanar");
            if (values.Float("_UVMainSet", 0) != 0) S.Drop(dropped, "UV Set");
            if (values.TryVector("_UVMainScroll", out var scroll) && (scroll.x != 0 || scroll.y != 0)) S.Drop(dropped, "Scroll Speed");
            if (values.Float("_UVMainRotation", 0) != 0) S.Drop(dropped, "Rotation");
        }
    }
}
