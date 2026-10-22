using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using ShaderPropertyType = UnityEngine.Rendering.ShaderPropertyType;

namespace VRVlog.LilToonExporter
{
    // A toon shader built of switchable sections (lighting type, matcaps,
    // rim, outline, stencil and more) mapped section by section onto official
    // lilToon. The generic mapping runs first (main image and color, render
    // mode, normal map, emission); each section below then writes the lilToon
    // values it owns from the source properties of the same role. Sections
    // official lilToon does not have are reported by name.
    internal static partial class UnsupportedShaderSubstitution
    {
        // Known by its properties, which a missing shader keeps in its saved
        // values: _LightingMode with _ShadingEnabled (current versions) or
        // with _LightingRampType (older versions).
        static bool IsSectionedToon(Material source, SourceValues values) =>
            values.TryFloat("_LightingMode", out _) && (values.TryFloat("_ShadingEnabled", out _) || values.TryFloat("_LightingRampType", out _));

        static Material FromSectionedToon(Material source, SourceValues values, List<Texture2D> textures, out bool colorSkipped, out IReadOnlyList<string> dropped)
        {
            dropped = Array.Empty<string>();
            var material = FromGeneric(source, values, values.Float("_EnableOutlines", 0) > .5f, out colorSkipped);
            if (material == null) return null;
            var effects = new List<string>();
            var main = material.GetTexture("_MainTex");
            var color = material.GetColor("_Color");
            // Left-out adjustments are named among the dropped effects; the
            // generic notice would list hue and saturation, which are kept here.
            colorSkipped = false;
            ToonColorAdjust(values, material, effects);
            ToonRenderMode(values, material, GenericMode(source, values), effects);
            ToonShade(values, material, color, effects);
            Set(material, "_LightMaxLimit", values.Float("_LightingCapEnabled", 1) > .5f ? Mathf.Max(0, values.Float("_LightingCap", 1)) : 10);
            Set(material, "_LightMinLimit", Mathf.Clamp01(values.Float("_LightingMinLightBrightness", 0)));
            Set(material, "_MonochromeLighting", Mathf.Clamp01(values.Float("_LightingMonochromatic", 0)));
            ToonMatcap(values, material, "", "", textures, effects);
            ToonMatcap(values, material, "2", "2nd", textures, effects);
            ToonRim(values, material, textures, effects);
            ToonOutline(values, material, main, color, effects);
            ToonEmission(values, material, textures, effects);
            ToonStencil(values, material, effects);
            foreach (var (name, label) in ToonDroppedSections)
                if (values.Float(name, 0) > .5f) Drop(effects, label);
            dropped = effects;
            return material;
        }

        // Toggles of sections official lilToon has no counterpart for.
        static readonly (string Toggle, string Label)[] ToonDroppedSections =
        {
            ("_UseBump2ndMap", "Detail Normal"), ("_EnableBentNormal", "Bent Normal"), ("_DetailEnabled", "Details"),
            ("_PoiSkinEnabled", "Skin"), ("_BackFaceEnabled", "Backface"), ("_RGBMaskEnabled", "RGB Mask"),
            ("_DecalEnabled", "Decal 0"), ("_DecalEnabled1", "Decal 1"), ("_DecalEnabled2", "Decal 2"), ("_DecalEnabled3", "Decal 3"),
            ("_EnableAniso", "Anisotropics"), ("_SubsurfaceScattering", "Subsurface Scattering"), ("_MochieBRDF", "Reflections & Specular"),
            ("_GGXAnisotropics", "Anisotropic Specular"), ("_ClearCoatBRDF", "Clear Coat"), ("_StylizedSpecular", "Stylized Specular"),
            ("_EnableEnvironmentalRim", "Environmental Rim"), ("_BacklightEnabled", "Backlight"), ("_LTCGIEnabled", "Area Lights"),
            ("_CubeMapEnabled", "CubeMap"), ("_EnableDepthRimLighting", "Depth Rim Lighting"),
            ("_Matcap3Enable", "Matcap 3"), ("_Matcap4Enable", "Matcap 4"), ("_EnableRim2Lighting", "Rim Lighting 2"),
            ("_EnableEmission1", "Emission 1"), ("_EnableEmission2", "Emission 2"), ("_EnableEmission3", "Emission 3"),
            ("_EnableUDIMDiscardOptions", "UDIM Discard"), ("_EnableDepthBulge", "Depth Bulge"), ("_EnableDissolve", "Dissolve"),
            ("_EnableFlipbook", "Flipbook 0"), ("_EnableFlipbook1", "Flipbook 1"), ("_EnablePathing", "Pathing"),
            ("_EnableMirrorOptions", "Mirror"), ("_EnableTouchGlow", "Depth FX"), ("_TextEnabled", "Text"),
            ("_PoiInternalParallax", "Internal Parallax"), ("_PoiParallax", "Parallax"), ("_VideoEffectsEnable", "Video Effects"),
            ("_VoronoiEnabled", "Voronoi"), ("_EnableTruchet", "Truchet"), ("_PostProcess", "Post Processing"),
            ("_EnableDistortion", "Distortion"), ("_EnableAudioLink", "Audio Link"), ("_EnableALDecal", "Audio Link Decal"),
            ("_VertexManipulationsEnabled", "Vertex Options"), ("_VertexGlitchingEnabled", "Vertex Glitching"),
            ("_VertexLookAtEnabled", "Vertex Look At"), ("_BlackLightMaskingEnabled", "BlackLight Masking"),
            ("_GlobalMaskTexturesEnable", "Global Masks"), ("_GlitterEnable", "Glitter"), ("_MainVertexColoringEnabled", "Vertex Colors"),
        };

        static void Drop(List<string> effects, string label)
        {
            if (!effects.Contains(label)) effects.Add(label);
        }

        // Color adjustment applied after the main color: hue, saturation,
        // brightness and gamma become _MainTexHSVG, the gradation map lilToon's
        // own. What is left out is reported by name.
        static void ToonColorAdjust(SourceValues values, Material material, List<string> effects)
        {
            if (values.TryVector("_MainTexPan", out var pan) && Finite(pan) && (pan.x != 0 || pan.y != 0))
                material.SetVector("_MainTex_ScrollRotate", new Vector4(pan.x, pan.y, 0, 0));
            var hsvg = new Vector4(0, 1, 1, 1);
            var gradient = false;
            if (values.Float("_MainColorAdjustToggle", 0) > .5f)
            {
                // One mask per adjustment (R hue, G brightness, B saturation,
                // A gamma); lilToon has a single mask for all of them.
                if (values.Texture("_MainColorAdjustTexture", out var mask, out _, out _) && mask != null) Drop(effects, "Color Adjust Mask");
                else
                {
                    var hue = values.Float("_MainHueShift", 0);
                    if (values.Float("_MainHueShiftToggle", 0) > .5f && hue != 0)
                    {
                        if (values.Float("_MainHueShiftSelectOrShift", 1) == 1 && values.Float("_MainHueShiftSpeed", 0) == 0 &&
                            values.Float("_MainHueShiftReplace", 1) > .5f)
                        {
                            hue = Mathf.Repeat(hue, 1);
                            hsvg.x = hue > .5f ? hue - 1 : hue;
                        }
                        else Drop(effects, "Hue Shift");
                    }
                    hsvg.y = Mathf.Max(0, 1 + values.Float("_Saturation", 0));
                    var brightness = values.Float("_MainBrightness", 0);
                    if (brightness != 0 && values.Float("_MainBrightnessGlobalMask", 0) > 0) Drop(effects, "Brightness Mask");
                    else hsvg.z = Mathf.Max(0, 1 + brightness);
                    hsvg.w = Mathf.Max(.01f, values.Float("_MainGamma", 1));
                    if (values.Float("_ColorGradingToggle", 0) > .5f && values.Float("_MainGradationStrength", 0) > 0 &&
                        values.Texture("_MainGradationTex", out var gradation, out _, out _) && Flat(gradation))
                    {
                        if (values.Float("_MainGradationMaskChannel", 0) > 0) Drop(effects, "Gradation Mask");
                        else
                        {
                            material.SetTexture("_MainGradationTex", gradation);
                            Set(material, "_MainGradationStrength", Mathf.Clamp01(values.Float("_MainGradationStrength", 0)));
                            gradient = true;
                        }
                    }
                }
                if (values.Float("_MainChromatize", 0) != 0) Drop(effects, "Chromatize");
                if (values.TryVector("_MainTintColor", out var tint) && tint.w > 0) Drop(effects, "Tint");
            }
            material.SetVector("_MainTexHSVG", hsvg);
            // lilToon adjusts the main image before multiplying _Color, the
            // source after. Brightness scales both alike; hue, saturation, gamma
            // and the gradation agree only under a white _Color.
            var main = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            var white = Mathf.Abs(main.r - 1) < .004f && Mathf.Abs(main.g - 1) < .004f && Mathf.Abs(main.b - 1) < .004f;
            if (!white && (hsvg.x != 0 || hsvg.y != 1 || hsvg.w != 1 || gradient)) Drop(effects, "Color Adjust with Main Color");
        }

        static void ToonRenderMode(SourceValues values, Material material, Mode mode, List<string> effects)
        {
            var preset = values.Float("_Mode", 0);
            // TransClipping blends and still clips by its cutoff.
            if (preset == 9 && values.TryFloat("_Cutoff", out var cutoff)) Set(material, "_Cutoff", Mathf.Clamp01(cutoff));
            // Additive and multiplicative presets keep their blend factors.
            if (preset >= 4 && preset <= 7)
            {
                if (values.TryFloat("_SrcBlend", out var src)) Set(material, "_SrcBlend", src);
                if (values.TryFloat("_DstBlend", out var dst)) Set(material, "_DstBlend", dst);
            }
            if (values.TryFloat("_AlphaToCoverage", out var coverage)) Set(material, "_AlphaToMask", coverage > .5f ? 1 : 0);
            if (mode == Mode.Opaque || values.Float("_MainAlphaMaskMode", 2) == 0 ||
                !values.Texture("_AlphaMask", out var alpha, out var scale, out var offset) || !Flat(alpha)) return;
            // lilToon multiplies the alpha by saturate(mask.r * scale + value).
            var minMax = values.TryVector("_AlphaMaskMinMax", out var range) ? range : new Vector4(0, 1, 0, 1);
            if (values.Float("_MainAlphaMaskMode", 2) == 2 && values.Float("_AlphaMaskR", 1) == 1 && values.Float("_AlphaMaskG", 0) == 0 &&
                values.Float("_AlphaMaskB", 0) == 0 && values.Float("_AlphaMaskA", 0) == 0 && minMax.x == 0 && minMax.y == 1 &&
                values.Float("_AlphaMaskGamma", 1) == 1 && values.Float("_AlphaMaskInvert", 0) < .5f)
            {
                Set(material, "_AlphaMaskMode", 2);
                SetTexture(material, "_AlphaMask", alpha, scale, offset);
                Set(material, "_AlphaMaskScale", values.Float("_AlphaMaskBlendStrength", 1));
                Set(material, "_AlphaMaskValue", values.Float("_AlphaMaskValue", 0));
            }
            else Drop(effects, "Alpha Map");
        }

        // Older versions number their lighting types differently; they are
        // renumbered to the current ones (0 ramp, 1 multilayer, 2 wrapped, 3 skin,
        // 4 shade map, 5 flat, 6 realistic, 7 cloth, 8 SDF).
        static int ToonLightingMode(SourceValues values)
        {
            var mode = Mathf.RoundToInt(values.Float("_LightingMode", 5));
            if (!values.TryFloat("_LightingRampType", out var ramp)) return mode;
            switch (mode)
            {
                case 0: return ramp == 2 ? 4 : ramp == 1 ? 1 : 0;
                case 1: return 6;
                case 4: return 5;
                default: return mode;
            }
        }

        static readonly string[] ToonMultilayer =
        {
            "_ShadowColor", "_ShadowColorTex", "_ShadowBorder", "_ShadowBlur", "_ShadowReceive", "_ShadowNormalStrength",
            "_Shadow2ndColor", "_Shadow2ndColorTex", "_Shadow2ndBorder", "_Shadow2ndBlur", "_Shadow2ndReceive", "_Shadow2ndNormalStrength",
            "_Shadow3rdColor", "_Shadow3rdColorTex", "_Shadow3rdBorder", "_Shadow3rdBlur", "_Shadow3rdReceive", "_Shadow3rdNormalStrength",
            "_ShadowBorderColor", "_ShadowBorderRange", "_ShadowMainStrength", "_ShadowEnvStrength", "_ShadowStrength",
            "_ShadowStrengthMask", "_ShadowBorderMask", "_ShadowAOShift", "_ShadowAOShift2", "_ShadowPostAO",
        };

        // Borders are on lilToon's half-Lambert axis (N·L × 0.5 + 0.5), which
        // the source's light map follows. A blur is the full width of the edge.
        static void ToonShade(SourceValues values, Material material, Color mainColor, List<string> effects)
        {
            Reset(material, "_UseShadow", "_ShadowColor", "_ShadowColorTex", "_ShadowBorder", "_ShadowBlur");
            var mode = ToonLightingMode(values);
            if (values.Float("_ShadingEnabled", 1) < .5f || mode == 5) return;
            if (mode == 8) { Drop(effects, "Lighting Type: SDF"); return; }
            // Skin reads its shading from a lookup texture that is white by
            // default, so it is lit like Flat and the type is reported.
            if (mode == 3) { Drop(effects, "Lighting Type: Skin"); return; }
            if (mode == 7) Drop(effects, "Lighting Type: Cloth");
            // Below 1 the source lights its shadow with the scene's ambient
            // light; lilToon always uses the main light, so it is reported.
            if (mode <= 1 && values.Float("_LightingIgnoreAmbientColor", 1) < 1) Drop(effects, "Indirect Shadow Color");
            var strength = Mathf.Clamp01(values.Float("_ShadowStrength", 1));
            if (mode == 1)
            {
                // Same property names and the same formula.
                Copy(values, material, ToonMultilayer);
                if (values.TryVector("_ShadowColor", out var first) && first.w <= 0) material.SetColor("_ShadowColor", Color.white);
                if (values.TryVector("_ShadowBorderColor", out var edge) && Finite(edge))
                    material.SetColor("_ShadowBorderColor", new Color(edge.x * edge.w, edge.y * edge.w, edge.z * edge.w, 1));
                // The source multiplies a shadow texture by the base color; lilToon
                // replaces the base color with it unless the main strength is 1.
                if (values.Texture("_ShadowColorTex", out var texture, out _, out _) && Flat(texture))
                {
                    Set(material, "_ShadowMainStrength", 1);
                    // The strength is shared, so a visible second layer without
                    // its own image is multiplied by the base color twice.
                    if (values.TryVector("_Shadow2ndColor", out var second) && second.w > 0 &&
                        !(values.Texture("_Shadow2ndColorTex", out var texture2, out _, out _) && Flat(texture2)))
                        Drop(effects, "Multiplied Shadow Layers");
                }
                Set(material, "_UseShadow", 1);
                return;
            }
            // A single layer: no second layer and no colored edge.
            material.SetColor("_Shadow2ndColor", new Color(0, 0, 0, 0));
            material.SetColor("_ShadowBorderColor", new Color(0, 0, 0, 1));
            var black = new Color(0, 0, 0, 1);
            switch (mode)
            {
                case 0:
                    if (!ToonRamp(values, material)) return;
                    strength = 1;
                    break;
                case 2:
                {
                    var start = values.Float("_LightingGradientStart", 0);
                    var end = values.Float("_LightingGradientEnd", .5f);
                    Set(material, "_ShadowBorder", Mathf.Clamp01(1 - (start + end) / 2));
                    Set(material, "_ShadowBlur", Mathf.Clamp(end - start, .001f, 1));
                    material.SetColor("_ShadowColor", black);
                    Set(material, "_ShadowEnvStrength", 1);
                    break;
                }
                case 4:
                    ToonShadeMap(values, material, mainColor, effects);
                    break;
                default:
                    // Lambert N·L from 0 to 1 is 0.5 to 1 on the half-Lambert axis.
                    Set(material, "_ShadowBorder", .75f);
                    Set(material, "_ShadowBlur", .5f);
                    material.SetColor("_ShadowColor", black);
                    Set(material, "_ShadowEnvStrength", 1);
                    if (mode == 7) strength = 1;
                    break;
            }
            Set(material, "_UseShadow", 1);
            Set(material, "_ShadowStrength", strength);
            Set(material, "_ShadowReceive", Mathf.Clamp01(values.Float("_LightingCastedShadows", 0)));
        }

        // Shade map: the shadow lies below step - feather and the lit side
        // above step. Replace uses the shade color as is; Multiply multiplies
        // it by the base color.
        static void ToonShadeMap(SourceValues values, Material material, Color mainColor, List<string> effects)
        {
            var step = values.Float("_BaseColor_Step", .5f);
            var feather = Mathf.Max(.001f, values.Float("_BaseShade_Feather", .0001f));
            Set(material, "_ShadowBorder", Mathf.Clamp01(step - feather / 2));
            Set(material, "_ShadowBlur", Mathf.Clamp(feather, .001f, 1));
            var multiply = values.Float("_ShadingShadeMapBlendType", 0) > .5f;
            var useBase = values.Float("_Use_BaseAs1st", 0) > .5f;
            if (!values.Texture("_1st_ShadeMap", out var map, out _, out _) || !Flat(map)) map = null;
            var main = ToonShadeLayer(material, "_ShadowColor", "_ShadowColorTex", map,
                values.TryVector("_1st_ShadeColor", out var color) ? color : Vector4.one, useBase, multiply, mainColor);
            var step2 = values.Float("_ShadeColor_Step", 0);
            if (step2 > 0)
            {
                var feather2 = Mathf.Max(.001f, values.Float("_1st2nd_Shades_Feather", .0001f));
                Set(material, "_Shadow2ndBorder", Mathf.Clamp01(step2 - feather2 / 2));
                Set(material, "_Shadow2ndBlur", Mathf.Clamp(feather2, .001f, 1));
                var second = values.Float("_Use_1stAs2nd", 0) > .5f;
                if (!second && (!values.Texture("_2nd_ShadeMap", out map, out _, out _) || !Flat(map))) map = null;
                var main2 = ToonShadeLayer(material, "_Shadow2ndColor", "_Shadow2ndColorTex", map,
                    values.TryVector("_2nd_ShadeColor", out color) ? color : Vector4.one, second && useBase, multiply, mainColor);
                // One _ShadowMainStrength serves both layers, so a layer that
                // multiplies an image and one that does not cannot both match.
                if (main != main2) Drop(effects, "Multiplied Shadow Layers");
                main |= main2;
            }
            if (main) Set(material, "_ShadowMainStrength", 1);
        }

        // lilToon's shadow is lerp(albedo, texture, texture.a) × color, then
        // × albedo again by _ShadowMainStrength. Returns true when the layer
        // needs that second multiplication.
        static bool ToonShadeLayer(Material material, string colorName, string textureName, Texture map, Color color, bool useBase, bool multiply,
            Color mainColor)
        {
            color.a = 1;
            if (!useBase && map != null) material.SetTexture(textureName, map);
            // An absolute shade color without an image becomes a ratio to the
            // main color, which lilToon multiplies back.
            else if (!useBase && !multiply) color = Ratio(color, mainColor);
            material.SetColor(colorName, color);
            return multiply && (useBase || map != null);
        }

        static Color Ratio(Color value, Color basis)
        {
            float Channel(float v, float b) => Mathf.Clamp01(b > .0001f ? v / b : v);
            return new Color(Channel(value.r, basis.r), Channel(value.g, basis.g), Channel(value.b, basis.b), value.a);
        }

        // Texture ramp: the shaded color is base × (r + (1 - r) × tint × r)
        // with r = lerp(1, ramp(lightMap + offset), strength). The ramp's middle
        // row is read and fitted with lilToon's single border: the color at the
        // dark end, the border where the brightness is halfway and the blur
        // from its 10% and 90% points. Returns false when there is no shadow.
        static bool ToonRamp(SourceValues values, Material material)
        {
            if (!values.Texture("_ToonRamp", out var ramp, out _, out _) || !Flat(ramp)) return false;
            var row = ReadRow(ramp);
            // Without a graphics device the ramp cannot be read; lilToon's
            // default shadow stands in.
            if (row == null) return true;
            var strength = Mathf.Clamp01(values.Float("_ShadowStrength", 1));
            var offset = values.Float("_ShadowOffset", 0);
            var tint = values.TryVector("_LightingShadowColor", out var authored) && Finite(authored) ? ((Color)authored).linear : Color.white;
            Color Shaded(float u)
            {
                var x = Mathf.Clamp(Mathf.Clamp01(u + offset) * row.Length - .5f, 0, row.Length - 1);
                var i = Mathf.Min((int)x, row.Length - 2);
                var sample = Color.Lerp(row[i], row[i + 1], x - i);
                float Channel(float value, float t)
                {
                    var r = Mathf.Lerp(1, Mathf.Clamp01(value), strength);
                    return r + (1 - r) * t * r;
                }
                return new Color(Channel(sample.r, tint.r), Channel(sample.g, tint.g), Channel(sample.b, tint.b), 1);
            }
            const int Steps = 128;
            var brightness = new float[Steps + 1];
            for (var i = 0; i <= Steps; i++)
            {
                var c = Shaded(i / (float)Steps);
                brightness[i] = (c.r + c.g + c.b) / 3;
            }
            var dark = brightness[0];
            var lit = brightness[Steps];
            if (Mathf.Abs(lit - dark) < 1e-3f) return false;
            float Crossing(float level)
            {
                var target = dark + (lit - dark) * level;
                for (var i = 1; i <= Steps; i++)
                {
                    var a = brightness[i - 1] - target;
                    var b = brightness[i] - target;
                    if (a == 0) return (i - 1) / (float)Steps;
                    if (a * b <= 0) return (i - 1 + a / (a - b)) / Steps;
                }
                return 1;
            }
            var shadow = Shaded(0).gamma;
            material.SetColor("_ShadowColor", new Color(shadow.r, shadow.g, shadow.b, 1));
            Set(material, "_ShadowBorder", Crossing(.5f));
            Set(material, "_ShadowBlur", Mathf.Clamp((Crossing(.9f) - Crossing(.1f)) / .8f, .001f, 1));
            return true;
        }

        // The middle row of an image in linear values, read through the GPU so
        // that images without CPU access are read too.
        static Color[] ReadRow(Texture texture)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            const int Width = 64;
            var target = RenderTexture.GetTemporary(Width, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            Texture2D row = null;
            try
            {
                Graphics.Blit(texture, target, new Vector2(1, 0), new Vector2(0, .5f));
                RenderTexture.active = target;
                row = new Texture2D(Width, 1, TextureFormat.RGBAFloat, false, true);
                row.ReadPixels(new Rect(0, 0, Width, 1), 0, 0, false);
                row.Apply(false);
                return row.GetPixels();
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                if (row != null) UnityEngine.Object.DestroyImmediate(row);
            }
        }

        // The pixels as a shader samples them, at the given size. Null when they
        // cannot be read (no graphics device).
        static Color[] Sampled(Texture texture, int width, int height)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return null;
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var active = RenderTexture.active;
            Texture2D read = null;
            try
            {
                Graphics.Blit(texture, target);
                RenderTexture.active = target;
                read = new Texture2D(width, height, TextureFormat.RGBAFloat, false, true);
                read.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                read.Apply(false);
                return read.GetPixels();
            }
            catch (Exception) { return null; }
            finally
            {
                RenderTexture.active = active;
                RenderTexture.ReleaseTemporary(target);
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
            }
        }

        // The source reads one channel of a mask (R, G, B, A), maybe inverted;
        // lilToon multiplies the matcap by its mask's RGB and the rim and
        // emission by RGBA. A grey, opaque red-channel mask is copied (also when
        // it cannot be checked); any other is baked into a grey, opaque image of
        // the value the source reads, owned by textures. When that cannot be
        // done the effect is turned off and reported: unmasked, it would cover
        // the whole material.
        static void CopyMask(SourceValues values, Material material, string name, string channel, string invert, string target,
            string toggle, List<Texture2D> textures, List<string> effects, string label)
        {
            if (!values.Texture(name, out var mask, out var scale, out var offset) || !Flat(mask)) return;
            var index = Mathf.Clamp(Mathf.RoundToInt(values.Float(channel, 0)), 0, 3);
            var inverted = values.Float(invert, 0) >= .5f;
            Texture copy = null;
            if (index == 0 && !inverted)
            {
                copy = mask;
                foreach (var c in Sampled(mask, 32, 32) ?? Array.Empty<Color>())
                    if (Mathf.Abs(c.r - c.g) > .02f || Mathf.Abs(c.r - c.b) > .02f || c.a < .98f) { copy = null; break; }
            }
            if (copy == null) copy = BakeMask(mask, index, inverted, textures);
            if (copy != null) { SetTexture(material, target, copy, scale, offset); return; }
            Set(material, toggle, 0);
            Drop(effects, label);
        }

        static Texture2D BakeMask(Texture mask, int channel, bool inverted, List<Texture2D> textures)
        {
            if (textures == null) return null;
            var fit = Mathf.Min(1, 2048f / Mathf.Max(mask.width, mask.height));
            var width = Mathf.Max(1, Mathf.RoundToInt(mask.width * fit));
            var height = Mathf.Max(1, Mathf.RoundToInt(mask.height * fit));
            var pixels = Sampled(mask, width, height);
            if (pixels == null) return null;
            // The pixels are the values the source shader sampled, so the image
            // is kept linear for lilToon to sample the same ones.
            for (var i = 0; i < pixels.Length; i++)
            {
                var v = Mathf.Clamp01(pixels[i][channel]);
                if (inverted) v = 1 - v;
                pixels[i] = new Color(v, v, v, 1);
            }
            var baked = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = DeterministicNames.Unique(mask.name + "__VRVlog_Mask" + "RGBA"[channel] + (inverted ? "_Inverted" : ""),
                    candidate => textures.Exists(texture => texture != null && texture.name == candidate)),
                wrapModeU = mask.wrapModeU, wrapModeV = mask.wrapModeV, filterMode = mask.filterMode,
            };
            baked.SetPixels(pixels);
            baked.Apply(false, false);
            textures.Add(baked);
            return baked;
        }

        // prefix "" is the first matcap and "2" the second; lilToon's second
        // matcap carries "2nd".
        static void ToonMatcap(SourceValues values, Material material, string prefix, string suffix, List<Texture2D> textures, List<string> effects)
        {
            string P(string name) => "_Matcap" + prefix + name;
            string L(string name) => "_MatCap" + suffix + name;
            var label = prefix.Length == 0 ? "Matcap" : "Matcap " + prefix;
            if (values.Float(prefix.Length == 0 ? "_MatcapEnable" : "_Matcap2Enable", 0) < .5f) return;
            // The largest of the source's blend weights picks lilToon's one mode.
            var weights = new[]
            {
                (P("Replace"), 0, false, false), (P("Multiply"), 3, false, false), (P("Add"), 1, false, false), (P("Screen"), 2, false, false),
                (P("Mixed"), 1, true, false), (P("EmissionStrength"), 1, false, true), (P("AddToLight"), 1, false, true),
            };
            var weight = 0f; var blendMode = 0; var mixed = false; var unlit = false;
            foreach (var (name, mode, mix, emission) in weights)
            {
                var value = values.Float(name, 0);
                if (value > weight) { weight = value; blendMode = mode; mixed = mix; unlit = emission; }
            }
            if (weight <= 0) return;
            Set(material, "_UseMatCap" + suffix, 1);
            values.Texture(P(""), out var texture, out _, out _);
            if (!Flat(texture)) texture = null;
            material.SetTexture(L("Tex"), texture);
            var border = values.Float(P("Border"), .5f);
            material.SetTextureScale(L("Tex"), Vector2.one * Mathf.Max(.01f, border / .5f));
            material.SetTextureOffset(L("Tex"), Vector2.zero);
            var color = values.TryVector(P("Color"), out var authored) && Finite(authored) ? (Color)authored : Color.white;
            var scaled = (color.linear * Mathf.Max(0, values.Float(P("Intensity"), 1))).gamma;
            material.SetColor(L("Color"), new Color(scaled.r, scaled.g, scaled.b, color.a));
            Set(material, L("Blend"), Mathf.Clamp01(weight));
            Set(material, L("BlendMode"), blendMode);
            Set(material, L("MainStrength"), mixed ? 1 : Mathf.Clamp01(values.Float(P("BaseColorMix"), 0)));
            Set(material, L("EnableLighting"), unlit ? 0 : 1);
            Set(material, L("ShadowMask"), Mathf.Clamp01(values.Float(P("LightMask"), 0)));
            Set(material, L("NormalStrength"), Mathf.Clamp01(values.Float(P("Normal"), 1)));
            var size = texture == null ? 0 : Mathf.Max(texture.width, texture.height);
            Set(material, L("Lod"), size > 1 ? (1 - Mathf.Clamp01(values.Float(P("Smoothness"), 1))) * Mathf.Floor(Mathf.Log(size, 2)) : 0);
            CopyMask(values, material, P("Mask"), P("MaskChannel"), P("MaskInvert"), L("BlendMask"), "_UseMatCap" + suffix, textures, effects, label + " Mask");
            var uv = values.Float(P("UVMode"), 1);
            if (uv == 3 || uv == 4) Drop(effects, label + " UV Mode");
            if (values.Float(P("HueShiftEnabled"), 0) > .5f) Drop(effects, label + " Hue Shift");
        }

        static readonly string[] ToonRimLilToonStyle =
        {
            "_RimColor", "_RimBorder", "_RimBlur", "_RimFresnelPower", "_RimEnableLighting", "_RimShadowMask", "_RimBlendMode",
            "_RimMainStrength", "_RimColorTex", "_RimDirStrength", "_RimDirRange", "_RimIndirRange", "_RimIndirColor",
            "_RimIndirBorder", "_RimIndirBlur", "_RimNormalStrength", "_RimBackfaceMask", "_RimVRParallaxStrength",
        };

        static void ToonRim(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            if (values.Float("_EnableRimLighting", 0) < .5f) return;
            var style = values.Float("_RimStyle", 0);
            if (style == 1) { Drop(effects, "Rim Lighting (Style 1)"); return; }
            if (style == 2)
            {
                // The lilToon style uses lilToon's own names and formula.
                Copy(values, material, ToonRimLilToonStyle);
                Set(material, "_UseRim", 1);
                return;
            }
            // rim = 1 - saturate((|N·V|^power - (w - blur)) / blur) is full
            // below |N·V| = (w - blur)^(1/power) and gone above w^(1/power);
            // lilToon's edge on 1 - |N·V| is placed between the two.
            var direct = Mathf.Max(0, values.Float("_RimBlendStrength", 1));
            var glow = Mathf.Max(0, values.Float("_RimStrength", 0));
            if (direct + glow <= 0) return;
            var power = Mathf.Max(.01f, values.Float("_RimPower", 1));
            var width = Mathf.Lerp(-.05f, 1, Mathf.Clamp01(values.Float("_RimWidth", .8f)));
            var blur = Mathf.Max(0, values.Float("_RimBlur", .65f));
            var inner = Mathf.Pow(Mathf.Max(0, width - blur), 1 / power);
            var outer = Mathf.Pow(Mathf.Max(0, width), 1 / power);
            Set(material, "_UseRim", 1);
            Set(material, "_RimBorder", Mathf.Clamp01(1 - (inner + outer) / 2));
            Set(material, "_RimBlur", Mathf.Clamp(outer - inner, .001f, 1));
            Set(material, "_RimFresnelPower", 1);
            var color = values.TryVector("_RimLightColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            var scaled = (color.linear * Mathf.Max(0, values.Float("_RimBrightness", 1)) * (direct + glow)).gamma;
            material.SetColor("_RimColor", new Color(scaled.r, scaled.g, scaled.b, color.a));
            // The part added as emission does not take the light.
            Set(material, "_RimEnableLighting", direct / (direct + glow));
            var blend = Mathf.RoundToInt(values.Float("_RimPoiBlendMode", 0));
            Set(material, "_RimBlendMode", blend == 1 ? 0 : blend == 2 ? 3 : blend == 4 ? 2 : 1);
            Set(material, "_RimMainStrength", blend == 3 ? 1 : Mathf.Clamp01(values.Float("_RimBaseColorMix", 0)));
            Set(material, "_RimShadowMask", values.Float("_RimShadowToggle", 0) > .5f ? Mathf.Clamp01(values.Float("_RimShadowMaskStrength", 1)) : 0);
            if (values.Texture("_RimTex", out var texture, out var scale, out var offset) && Flat(texture))
            {
                SetTexture(material, "_RimColorTex", texture, scale, offset);
                if (values.Texture("_RimMask", out var unused, out _, out _) && Flat(unused)) Drop(effects, "Rim Mask");
            }
            else CopyMask(values, material, "_RimMask", "_RimMaskChannel", "_RimMaskInvert", "_RimColorTex", "_UseRim", textures, effects, "Rim Mask");
            if (values.Float("_RimLightingInvert", 0) > .5f) Drop(effects, "Rim Lighting Invert");
            if (values.Float("_RimLinear", 0) > .5f) Drop(effects, "Rim Linear");
        }

        static void ToonOutline(SourceValues values, Material material, Texture main, Color mainColor, List<string> effects)
        {
            if (values.Float("_EnableOutlines", 0) < .5f) return;
            // Without the outline variant the outline is lost, so it is reported.
            if (!material.HasProperty("_OutlineWidth")) { Drop(effects, "Outline"); return; }
            Set(material, "_OutlineWidth", Mathf.Max(0, values.Float("_LineWidth", 1)));
            Set(material, "_OutlineFixWidth", values.Float("_OutlineFixedSize", 1) > .5f ? Mathf.Clamp01(values.Float("_OutlineFixWidth", .5f)) : 0);
            var color = values.TryVector("_LineColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            if (values.Texture("_OutlineTexture", out var texture, out var scale, out var offset) && Flat(texture))
                SetTexture(material, "_OutlineTex", texture, scale, offset);
            // Tinted by the base color without an own image: lilToon draws the
            // main image under the outline color.
            else if (values.Float("_OutlineTintMix", 0) > .5f)
            {
                SetTexture(material, "_OutlineTex", main, material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                color *= mainColor;
            }
            material.SetColor("_OutlineColor", color);
            if (values.Texture("_OutlineMask", out var mask, out scale, out offset) && Flat(mask))
            {
                if (values.Float("_OutlineMaskChannel", 0) == 0) SetTexture(material, "_OutlineWidthMask", mask, scale, offset);
                else Drop(effects, "Outline Mask");
            }
            Set(material, "_OutlineEnableLighting", Mathf.Clamp01(values.Float("_OutlineLit", 1)));
            if (values.TryFloat("_OutlineCull", out var cull) && (cull == 0 || cull == 1 || cull == 2)) Set(material, "_OutlineCull", cull);
            if (values.Float("_OutlineExpansionMode", 1) != 1 || values.Float("_OutlineSpace", 0) != 0) Drop(effects, "Outline Mode");
            if (values.Float("_OutlineEmission", 0) > 0) Drop(effects, "Outline Emission");
        }

        static void ToonEmission(SourceValues values, Material material, List<Texture2D> textures, List<string> effects)
        {
            if (Get(material, "_UseEmission") < .5f) return;
            if (values.TryVector("_EmissionMapPan", out var pan) && Finite(pan) && (pan.x != 0 || pan.y != 0))
                material.SetVector("_EmissionMap_ScrollRotate", new Vector4(pan.x, pan.y, 0, 0));
            CopyMask(values, material, "_EmissionMask", "_EmissionMaskChannel", "_EmissionMaskInvert", "_EmissionBlendMask", "_UseEmission", textures,
                effects, "Emission Mask");
            // Both blink with sin(time × velocity + phase); the source between min
            // and max, lilToon between 1 - x and 1 of its color.
            var max = values.Float("_EmissiveBlink_Max", 1);
            if (values.Float("_EmissionBlinkingEnabled", 0) > .5f && max > 0)
            {
                var min = Mathf.Clamp(values.Float("_EmissiveBlink_Min", 0), 0, max);
                var color = material.GetColor("_EmissionColor");
                var scaled = (color.linear * max).gamma;
                material.SetColor("_EmissionColor", new Color(scaled.r, scaled.g, scaled.b, color.a));
                var velocity = values.Float("_EmissiveBlink_Velocity", 4);
                material.SetVector("_EmissionBlink", new Vector4(1 - min / max, values.Float("_EmissiveBlink_Mode", 0) == 1 ? 1 : 0,
                    velocity, values.Float("_EmissionBlinkingOffset", 0) * velocity));
            }
            if (values.Float("_ScrollingEmission", 0) > .5f) Drop(effects, "Scrolling Emission");
            if (values.Float("_EmissionHueShiftEnabled", 0) > .5f) Drop(effects, "Emission Hue Shift");
            if (values.Float("_EmissionCenterOutEnabled", 0) > .5f) Drop(effects, "Emission Center Out");
            if (values.Float("_EnableGITDEmission", 0) > .5f) Drop(effects, "Light Based Emission");
        }

        static readonly (string Source, string LilToon)[] ToonStencilNames =
        {
            ("Ref", "Ref"), ("ReadMask", "ReadMask"), ("WriteMask", "WriteMask"),
            ("CompareFunction", "Comp"), ("PassOp", "Pass"), ("FailOp", "Fail"), ("ZFailOp", "ZFail"),
        };

        static void ToonStencil(SourceValues values, Material material, List<string> effects)
        {
            foreach (var prefix in new[] { "_Stencil", "_OutlineStencil" })
                foreach (var (name, lilToon) in ToonStencilNames)
                    if (values.TryFloat(prefix + name, out var value)) Set(material, prefix + lilToon, value);
            if (values.Float("_StencilType", 0) > .5f) Drop(effects, "Stencil Type");
        }

        // Puts back the shader's own defaults.
        static void Reset(Material material, params string[] names)
        {
            var shader = material.shader;
            foreach (var name in names)
            {
                var index = shader.FindPropertyIndex(name);
                if (index < 0) continue;
                switch (shader.GetPropertyType(index))
                {
                    case ShaderPropertyType.Texture: SetTexture(material, name, null, Vector2.one, Vector2.zero); break;
                    case ShaderPropertyType.Color: case ShaderPropertyType.Vector: material.SetVector(name, shader.GetPropertyDefaultVectorValue(index)); break;
                    case ShaderPropertyType.Int: material.SetInteger(name, shader.GetPropertyDefaultIntValue(index)); break;
                    default: material.SetFloat(name, shader.GetPropertyDefaultFloatValue(index)); break;
                }
            }
        }

        // Copies values whose names are the same in both shaders.
        static void Copy(SourceValues values, Material material, IEnumerable<string> names)
        {
            var shader = material.shader;
            foreach (var name in names)
            {
                var index = shader.FindPropertyIndex(name);
                if (index < 0) continue;
                switch (shader.GetPropertyType(index))
                {
                    case ShaderPropertyType.Texture:
                        if (values.Texture(name, out var texture, out var scale, out var offset) && (texture == null || Flat(texture)))
                            SetTexture(material, name, texture, scale, offset);
                        break;
                    case ShaderPropertyType.Color: case ShaderPropertyType.Vector:
                        if (values.TryVector(name, out var vector) && Finite(vector)) material.SetVector(name, vector);
                        break;
                    default:
                        if (values.TryFloat(name, out var number)) Set(material, name, number);
                        break;
                }
            }
        }

        static void SetTexture(Material material, string name, Texture texture, Vector2 scale, Vector2 offset)
        {
            if (!material.HasProperty(name)) return;
            material.SetTexture(name, texture);
            material.SetTextureScale(name, scale); material.SetTextureOffset(name, offset);
        }

        static float Get(Material material, string name)
        {
            var index = material.shader.FindPropertyIndex(name);
            if (index < 0) return 0;
            return material.shader.GetPropertyType(index) == ShaderPropertyType.Int ? material.GetInteger(name) : material.GetFloat(name);
        }
    }
}
