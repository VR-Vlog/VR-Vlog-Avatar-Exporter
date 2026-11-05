using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Sunao Shader. The generic mapping reads the main image and color, _Bright,
    // the normal map, _Culling and _Cutout, and the alpha (see SunaoAlpha). This
    // class reads what the shader keeps under its own names: the shade, the
    // light limits, the emission, the rim, the matcap, the decal and the outline.
    internal sealed class SunaoApproximation : IShaderApproximation
    {
        const string NamePrefix = "Sunao Shader/";

        // A missing shader is known by the property every variant has.
        public bool Matches(string shaderName, SourceValues values) =>
            shaderName == null ? values.TryFloat("_SunaoShaderType", out _) : shaderName.StartsWith(NamePrefix, System.StringComparison.Ordinal);

        // _SunaoShaderType: 0 opaque, 1 transparent, 2 cutout, 3-5 the same with
        // the stencil outline, 6 stencil read and 7 stencil write, which cut out.
        // An older material without it is read from its queue by the generic mapping.
        public Mode? RenderMode(SourceValues values)
        {
            if (!values.TryFloat("_SunaoShaderType", out var type)) return null;
            switch (Mathf.RoundToInt(type))
            {
                case 0: case 3: return Mode.Opaque;
                case 1: case 4: return Mode.Transparent;
                case 2: case 5: case 6: case 7: return Mode.Cutout;
                default: return null;
            }
        }

        public bool Outline(SourceValues values) => values.Float("_OutLineEnable", 0) > .5f;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            ApplyLight(values, material, dropped);
            ApplyShade(values, material, textures, dropped);
            ApplyEmission(values, material, dropped);
            ApplyRim(values, material, dropped);
            ApplyReflection(values, material, textures, dropped);
            ApplyDecal(values, material, dropped);
            if (Outline(values)) ApplyOutline(values, material, textures, dropped);

            if (values.TryFloat("_SunaoShaderType", out var type) && type >= 3) dropped.Add("Stencil");
            if (values.Float("_VertexColor", 0) > .5f) dropped.Add("Vertex Color");
            if (values.Texture("_OcclusionMap", out var occlusion, out _, out _) && Flat(occlusion)) dropped.Add("Occlusion");
            if (values.Texture("_LightMask", out var boost, out _, out _) && Flat(boost)) dropped.Add("Light Boost");
            if (values.Float("_UVAnimation", 0) > 0) dropped.Add("UV Animation");
            if (values.Float("_ParallaxEnable", 0) > .5f) dropped.Add("Parallax Emission");
            if (values.Float("_EnableGammaFix", 0) > .5f || values.Float("_EnableBlightFix", 0) > .5f) dropped.Add("Output Correction");
        }

        static void ApplyLight(SourceValues values, Material material, ICollection<string> dropped)
        {
            Set(material, "_AsUnlit", Mathf.Clamp01(values.Float("_Unlit", 0)));
            Set(material, "_MonochromeLighting", values.Float("_MonochromeLit", 0) > .5f ? 1 : 0);
            Set(material, "_LightMinLimit", Mathf.Clamp01(values.Float("_MinimumLight", 0)));
            // _LightLimitter scales the light down to 1, _LimitterEnable clamps
            // the output; lilToon's limit does both for its light. Without either
            // the source does not limit the light.
            var limit = values.Float("_LightLimitter", 1) > .5f ? 1f : 10f;
            if (values.Float("_LimitterEnable", 0) > .5f) limit = Mathf.Min(limit, Mathf.Max(0, values.Float("_LimitterMax", 1)));
            Set(material, "_LightMaxLimit", limit);
            if (values.Float("_DirectionalLight", 1) != 1 || values.Float("_SHLight", 1) != 1 || values.Float("_PointLight", 1) != 1)
                dropped.Add("Light Strength");

            var scrollX = values.Float("_UVScrollX", 0); var scrollY = values.Float("_UVScrollY", 0);
            if (scrollX != 0 || scrollY != 0) material.SetVector("_MainTex_ScrollRotate", new Vector4(scrollX, scrollY, 0, 0));
        }

        // Diffuse = saturate((N·L - 0.5) g + 1.5 - w) goes from 0 to 1 over a
        // width of 1 / g on N·L. lilToon's edge is on N·L × 0.5 + 0.5, at the
        // center of the ramp.
        static void ApplyShade(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            // _Unlit fades the lit result, shade included, toward the plain color.
            var strength = Mathf.Clamp01(values.Float("_Shade", .3f)) * (1 - Mathf.Clamp01(values.Float("_Unlit", 0)));
            Set(material, "_UseShadow", strength > 0 ? 1 : 0);
            if (strength <= 0) return;
            Set(material, "_ShadowStrength", strength);
            Set(material, "_ShadowBorderRange", 0);
            var gradient = Mathf.Max(.01f, values.Float("_ShadeGradient", .75f));
            var width = values.Float("_ShadeWidth", .75f);
            var center = .5f + (width - 1) / gradient;
            Set(material, "_ShadowBorder", Mathf.Clamp01(center * .5f + .5f));
            var blur = .5f / gradient;
            if (values.Float("_ToonEnable", 0) > .5f)
            {
                // Toon(): the steps are max(11 - _Toon, 2) and each step's edge
                // narrows by (1 + sharpness^3)^10. Two steps are one edge, which
                // lilToon draws; with more it stays a smooth ramp.
                var steps = Mathf.Max(11 - Mathf.RoundToInt(values.Float("_Toon", 9)), 2);
                if (steps == 2)
                {
                    var sharpness = values.Float("_ToonSharpness", 1);
                    blur /= 2 * Mathf.Pow(1 + sharpness * sharpness * sharpness, 10);
                }
                else dropped.Add("Toon Steps");
            }
            Set(material, "_ShadowBlur", Mathf.Clamp(blur, .001f, 1));
            if (values.Texture("_ShadeMask", out var mask, out _, out _) && Flat(mask))
            {
                var grey = Grey(mask) ? mask : BakeMask(mask, 4, false, textures);
                if (grey != null) SetTexture(material, "_ShadowStrengthMask", grey, Vector2.one, Vector2.zero);
                else dropped.Add("Shade Mask");
            }
            ApplyShadeColor(values, material, textures);
        }

        // The shade color is saturate(Color × 3 - 1.5) × _ShadeColor, darker and
        // more saturated than the base at each pixel, mixed with
        // _CustomShadeColor by its alpha; the result multiplies the base. lilToon
        // reads its shadow image in the main image's UV. Without an image, or
        // when only the custom color counts, it multiplies the base by one color.
        static void ApplyShadeColor(SourceValues values, Material material, List<Texture2D> textures)
        {
            var custom = values.TryVector("_CustomShadeColor", out var authored) && Finite(authored) ? (Color)authored : new Color(0, 0, 0, 0);
            var amount = Mathf.Clamp01(custom.a);
            var shade = Mathf.Clamp01(values.Float("_ShadeColor", .5f));
            var tint = (material.GetColor("_Color") * Mathf.Clamp01(values.Float("_Bright", 1))).linear;
            var linearCustom = custom.linear;
            Color Factor(Color color) => Color.Lerp(new Color(Mathf.Clamp01(color.r * 3 - 1.5f) * shade, Mathf.Clamp01(color.g * 3 - 1.5f) * shade,
                Mathf.Clamp01(color.b * 3 - 1.5f) * shade), linearCustom, amount);

            Set(material, "_ShadowMainStrength", 0);
            Reset(material, "_ShadowColorTex");
            var main = material.GetTexture("_MainTex");
            if (main != null && textures != null && amount < 1)
            {
                var fit = Mathf.Min(1, 1024f / Mathf.Max(main.width, main.height));
                var width = Mathf.Max(1, Mathf.RoundToInt(main.width * fit));
                var height = Mathf.Max(1, Mathf.RoundToInt(main.height * fit));
                var pixels = Sampled(main, width, height);
                if (pixels != null)
                {
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        var color = new Color(pixels[i].r * tint.r, pixels[i].g * tint.g, pixels[i].b * tint.b);
                        var factor = Factor(color);
                        var shaded = new Color(color.r * factor.r, color.g * factor.g, color.b * factor.b).gamma;
                        pixels[i] = new Color(shaded.r, shaded.g, shaded.b, 1);
                    }
                    var image = NewImage(main, "__VRVlog_Shade", width, height, pixels, textures, false);
                    if (image != null)
                    {
                        SetTexture(material, "_ShadowColorTex", image, Vector2.one, Vector2.zero);
                        material.SetColor("_ShadowColor", Color.white);
                        return;
                    }
                }
            }
            var flat = Factor(main == null ? tint : tint * .5f).gamma;
            material.SetColor("_ShadowColor", new Color(flat.r, flat.g, flat.b, 1));
        }

        // Emission = _Emission × _EmissionColor × map × wave, with only
        // _EmissionEnable turning it on. The generic mapping guesses from the
        // image, so its result is replaced.
        static void ApplyEmission(SourceValues values, Material material, ICollection<string> dropped)
        {
            Reset(material, "_UseEmission", "_EmissionColor", "_EmissionMap", "_EmissionMainStrength", "_EmissionBlendMode", "_EmissionBlink", "_EmissionMap_ScrollRotate");
            if (values.Float("_EmissionEnable", 0) < .5f) return;
            var mode = Mathf.RoundToInt(values.Float("_EmissionMode", 0));
            if (mode != 0) { dropped.Add("Emission (" + (mode == 1 ? "Multiply" : "Minus") + ")"); return; }
            var color = values.TryVector("_EmissionColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            var scaled = (color.linear * Mathf.Max(0, values.Float("_Emission", 1))).gamma;
            if (scaled.maxColorComponent <= 0) return;
            Set(material, "_UseEmission", 1);
            material.SetColor("_EmissionColor", new Color(scaled.r, scaled.g, scaled.b, 1));
            if (values.Texture("_EmissionMap", out var map, out var scale, out var offset) && Flat(map))
            {
                // MixingTransformTex: the map's tiling is on top of the main image's.
                var mainScale = material.GetTextureScale("_MainTex"); var mainOffset = material.GetTextureOffset("_MainTex");
                SetTexture(material, "_EmissionMap", map, Vector2.Scale(mainScale, scale), mainOffset + offset);
            }
            var scrollX = values.Float("_EmissionScrX", 0); var scrollY = values.Float("_EmissionScrY", 0);
            if (scrollX != 0 || scrollY != 0) material.SetVector("_EmissionMap_ScrollRotate", new Vector4(scrollX, scrollY, 0, 0));

            // lilToon's blink is lerp(1, sin(time × z + w) × 0.5 + 0.5, x), round
            // for y = 1. Waveform 0 is the same over cos(2π time f), 3 is a square.
            var blink = Mathf.Clamp01(values.Float("_EmissionBlink", 0));
            if (blink > 0)
            {
                var speed = 2 * Mathf.PI * values.Float("_EmissionFrequency", 1);
                var wave = Mathf.RoundToInt(values.Float("_EmissionWaveform", 0));
                if (wave == 0) material.SetVector("_EmissionBlink", new Vector4(blink, 0, speed, Mathf.PI / 2));
                else if (wave == 3) material.SetVector("_EmissionBlink", new Vector4(blink, 1, speed, Mathf.PI));
                else dropped.Add("Emission Waveform");
            }

            if (values.Texture("_EmissionMap2", out var second, out _, out _) && Flat(second)) dropped.Add("Emission Map 2");
            if (values.Float("_EmissionAnimation", 0) > 0) dropped.Add("Emission Animation");
            if (values.Float("_EmissionLighting", 0) > .5f) dropped.Add("Emission Lighting");
            if (values.Float("_EmissionInTheDark", 0) > 0) dropped.Add("Emission In The Dark");
        }

        // rim = saturate(1 - |N·V|)^2, then
        // saturate((rim - 0.5) g^3 + 0.5 + power - 1): a ramp of width 1 / g^3
        // centered at 0.5 + (1 - power) / g^3.
        // RimLightCalc: x = (1 - N·V)², a ramp of slope g³ around 0.5, plus
        // _RimLit - 1. Below 1 that moves the ramp. Above 1 it also lifts the
        // whole surface, which lilToon's ramp cannot start from, so the ramp
        // starts at the center of the surface and ends where the source reaches 1.
        static void ApplyRim(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (values.Float("_RimLitEnable", 0) < .5f) return;
            if (values.Float("_RimLitMode", 0) != 0) { dropped.Add("Rim Light (Multiply or Minus)"); return; }
            var gradient = Mathf.Max(.01f, values.Float("_RimLitGradient", 1));
            var cube = gradient * gradient * gradient;
            Set(material, "_UseRim", 1);
            Set(material, "_RimFresnelPower", 2);
            var power = values.Float("_RimLit", 1);
            var lift = Mathf.Clamp01(power - 1);
            if (lift > 0) dropped.Add("Rim Light Lift");
            var full = Mathf.Max(.001f, .5f + (.5f - lift) / cube);
            Set(material, "_RimBorder", lift > 0 ? full / 2 : Mathf.Clamp01(.5f + (1 - power) / cube));
            Set(material, "_RimBlur", Mathf.Clamp(lift > 0 ? full : 1 / cube, .001f, 1));
            var color = values.TryVector("_RimLitColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            material.SetColor("_RimColor", color);
            Set(material, "_RimBlendMode", 1);
            Set(material, "_RimEnableLighting", values.Float("_RimLitLighthing", 1) > .5f ? 1 : 0);
            Set(material, "_RimMainStrength", values.Float("_RimLitTexColor", 0) > .5f ? 1 : 0);
            Set(material, "_RimShadowMask", 0);
            if (values.Texture("_RimLitMask", out var mask, out _, out _) && Flat(mask))
                SetTexture(material, "_RimColorTex", mask, Vector2.one, Vector2.zero);
        }

        // Only under _ReflectionEnable. The matcap adds to the color, and both
        // the matcap and the reflection are masked by _MetallicGlossMap unless
        // _MatCapMaskEnable is off.
        static void ApplyReflection(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_ReflectionEnable", 0) < .5f) return;
            // The specular and the environment reflection have no match in the
            // VRM; lilToon's differ enough that they are reported.
            if (values.Float("_Specular", 1) > 0 || values.Float("_Metallic", .5f) > 0) dropped.Add("Reflection");
            if (!values.Texture("_MatCap", out var matcap, out _, out _) || !Flat(matcap)) return;
            var color = values.TryVector("_MatCapColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            var scaled = (color.linear * Mathf.Max(0, values.Float("_MatCapStrength", 1))).gamma;
            if (scaled.maxColorComponent <= 0) return;
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", matcap, Vector2.one, Vector2.zero);
            material.SetColor("_MatCapColor", new Color(scaled.r, scaled.g, scaled.b, 1));
            Set(material, "_MatCapBlend", 1);
            Set(material, "_MatCapBlendMode", 1);
            Set(material, "_MatCapMainStrength", values.Float("_MatCapTexColor", 0) > .5f ? 1 : 0);
            Set(material, "_MatCapEnableLighting", values.Float("_MatCapLit", 3) > .5f ? 1 : 0);
            Set(material, "_MatCapLod", 3 * (1 - Mathf.Clamp01(values.Float("_GlossMapScale", .75f))));
            var name = values.Float("_MatCapMaskEnable", 1) > .5f ? "_MetallicGlossMap" : "_MatCapMask";
            if (values.Texture(name, out var mask, out _, out _) && Flat(mask))
            {
                var grey = Grey(mask) ? mask : BakeMask(mask, 4, false, textures);
                if (grey != null) SetTexture(material, "_MatCapBlendMask", grey, Vector2.one, Vector2.zero);
                else dropped.Add("MatCap Mask");
            }
        }

        // The source samples at (R(θ)(uv - p) + h) / size: the image of
        // the given size centered on p, with the longer side of the image fitted
        // to it. lilToon samples at (R(θ)(uv - 0.5) + 0.5) × S + T, which agree
        // for S = 1 / size and T = S × (h - 0.5 - R(θ)(p - 0.5)).
        static void ApplyDecal(SourceValues values, Material material, ICollection<string> dropped)
        {
            if (values.Float("_DecalEnable", 0) < .5f) return;
            if (!values.Texture("_DecalTex", out var texture, out _, out _) || !Flat(texture)) return;
            var mode = Mathf.RoundToInt(values.Float("_DecalMode", 0));
            var blend = mode == 0 ? 0 : mode == 1 ? 1 : mode == 2 ? 3 : -1;
            if (blend < 0 || values.Float("_DecalMirror", 0) != 0) { dropped.Add("Decal"); return; }
            var size = new Vector2(Mathf.Max(.00001f, values.Float("_DecalSizeX", .5f)), Mathf.Max(.00001f, values.Float("_DecalSizeY", .5f)));
            if (texture.width < texture.height) size.x *= (float)texture.width / texture.height;
            if (texture.width > texture.height) size.y *= (float)texture.height / texture.width;
            var angle = values.Float("_DecalRotation", 0) * Mathf.Deg2Rad;
            var scale = new Vector2(1 / size.x, 1 / size.y);
            var center = new Vector2(values.Float("_DecalPosX", .5f) - .5f, values.Float("_DecalPosY", .5f) - .5f);
            var cos = Mathf.Cos(angle); var sin = Mathf.Sin(angle);
            var turned = new Vector2(center.x * cos - center.y * sin, center.x * sin + center.y * cos);
            var offset = Vector2.Scale(scale, size * .5f - new Vector2(.5f, .5f) - turned);
            Set(material, "_UseMain2ndTex", 1);
            SetTexture(material, "_Main2ndTex", texture, scale, offset);
            var color = values.TryVector("_DecalColor", out var authored) && Finite(authored) ? (Color)authored : Color.white;
            material.SetColor("_Color2nd", color);
            Set(material, "_Main2ndTexAngle", angle);
            Set(material, "_Main2ndTexIsDecal", 1);
            Set(material, "_Main2ndTexBlendMode", blend);
            Set(material, "_Main2ndTex_UVMode", 0);
            Set(material, "_Main2ndEnableLighting", 1);
            if (values.Float("_DecalScrollX", 0) != 0 || values.Float("_DecalScrollY", 0) != 0 || values.Float("_DecalAnimation", 0) > 0)
                dropped.Add("Decal Animation");
        }

        // The width is _OutLineSize × 0.01 (× 10 with _OutLineFixScale) in the
        // mesh's scale along the normal; lilToon's _OutlineWidth is in centimeters.
        // The color multiplies _OutLineTexture, which is sampled in the main
        // image's UV, and the mask is its luminance.
        void ApplyOutline(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { dropped.Add("Outline"); return; }
            var size = Mathf.Max(0, values.Float("_OutLineSize", .1f)) * (values.Float("_OutLineFixScale", 0) > .5f ? 10 : 1);
            Set(material, "_OutlineWidth", size);
            Set(material, "_OutlineFixWidth", 0);
            var color = values.TryVector("_OutLineColor", out var authored) && Finite(authored) ? (Color)authored : Color.black;
            var transparent = RenderMode(values) == Mode.Transparent;
            var opacity = transparent ? Mathf.Clamp01(material.GetColor("_Color").a) : 1;
            material.SetColor("_OutlineColor", new Color(color.r, color.g, color.b, opacity));
            Set(material, "_OutlineEnableLighting", values.Float("_OutLineLighthing", 1) > .5f ? 1 : 0);
            var mainScale = material.GetTextureScale("_MainTex"); var mainOffset = material.GetTextureOffset("_MainTex");
            values.Texture("_OutLineTexture", out var image, out _, out _);
            if (!Flat(image)) image = null;
            var main = material.GetTexture("_MainTex");
            var withMain = values.Float("_OutLineTexColor", 0) > .5f;
            // The line takes the main image's color under _OutLineTexColor and,
            // when blended, its alpha; lilToon reads both from the line's image.
            var baked = main != null && (withMain || transparent) ? OutlineImage(main, image, withMain, transparent, textures) : null;
            if (baked != null || image != null) SetTexture(material, "_OutlineTex", baked != null ? baked : image, mainScale, mainOffset);
            else if (withMain && main != null) dropped.Add("Outline Main Color");
            if (values.Texture("_OutLineMask", out var mask, out _, out _) && Flat(mask))
            {
                var grey = Grey(mask) ? mask : BakeMask(mask, 4, false, textures);
                if (grey != null) SetTexture(material, "_OutlineWidthMask", grey, Vector2.one, Vector2.zero);
                else dropped.Add("Outline Mask");
            }
        }

        static Texture2D OutlineImage(Texture main, Texture line, bool withMain, bool withAlpha, List<Texture2D> textures)
        {
            var width = Mathf.Min(main.width, 1024); var height = Mathf.Min(main.height, 1024);
            var pixels = Sampled(main, width, height);
            var linePixels = line != null ? Sampled(line, width, height) : null;
            if (pixels == null || line != null && linePixels == null) return null;
            for (var i = 0; i < pixels.Length; i++)
            {
                var color = withMain ? pixels[i] : Color.white;
                if (linePixels != null) color *= linePixels[i];
                var shown = color.gamma;
                pixels[i] = new Color(shown.r, shown.g, shown.b, withAlpha ? pixels[i].a : 1);
            }
            return NewImage(main, "__VRVlog_Outline", width, height, pixels, textures, false);
        }
    }
}
