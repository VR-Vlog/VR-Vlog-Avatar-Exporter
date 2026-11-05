using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Xiexe XSToon 3. The generic mapping reads the main image and color, normal
    // map, emission, cull and cutoff; this class replaces its shade and adds
    // color adjustment, rim, matcap and outline. The plain and the outlined
    // shader declare the same names, so there is one instance for each. A
    // missing shader keeps only its values, which do not say which one it was,
    // and is drawn without the outline.
    internal sealed class XsToonApproximation : IShaderApproximation
    {
        const string OutlinedName = "Outlined";
        readonly bool outlined;

        internal XsToonApproximation(bool outlined) { this.outlined = outlined; }

        public bool Matches(string shaderName, SourceValues values)
        {
            if (!values.TryFloat("_ShadowType", out _) || !values.TryFloat("_ShadowRimThreshold", out _)) return false;
            return outlined == (shaderName != null && shaderName.IndexOf(OutlinedName, System.StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Opaque, Cutout, Dithered, Alpha To Coverage, Transparent, Fade, Additive.
        public Mode? RenderMode(SourceValues values)
        {
            if (!values.TryFloat("_BlendMode", out var blend)) return null;
            var mode = Mathf.RoundToInt(blend);
            return mode <= 0 ? Mode.Opaque : mode <= 3 ? Mode.Cutout : Mode.Transparent;
        }

        public bool Outline(SourceValues values) => outlined && values.Float("_OutlineWidth", 1) > 0;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            // Back faces are lit by the flipped normal.
            Set(material, "_FlipNormal", 1);
            // The shade reads the base color the adjustment leaves.
            Adjust(values, material, textures, dropped);
            Shade(values, material, textures, dropped);
            Rim(values, material, dropped);
            Reflection(values, material, textures, dropped);
            if (Outline(values)) ApplyOutline(values, material, textures, dropped);
            var blend = Mathf.RoundToInt(values.Float("_BlendMode", 0));
            // These cover a pixel partly; a cutout shows it or not.
            if (blend == 2) Drop(dropped, "Dithered Blend");
            if (blend == 3) Drop(dropped, "Alpha To Coverage");
            if (blend == 6) Drop(dropped, "Additive Blend");
            var shadowRim = Opaque(values, "_ShadowRim", Color.white);
            if (Mathf.Min(shadowRim.r, shadowRim.g, shadowRim.b) < 1) Drop(dropped, "Shadow Rim");
            if (values.Float("_SpecularIntensity", 0) > 0) Drop(dropped, "Specular");
            if (Opaque(values, "_SSColor", Color.black).maxColorComponent > 0) Drop(dropped, "Subsurface");
            if (Mathf.RoundToInt(values.Float("_HalftoneType", 3)) != 3) Drop(dropped, "Halftones");
            if (values.Float("_UseClipsForDissolve", 0) > .5f) Drop(dropped, "Dissolve");
            if (values.Float("_EmissionAudioLinkChannel", 0) > 0) Drop(dropped, "AudioLink");
            Set(material, "_EmissionMainStrength", Mathf.Clamp01(values.Float("_EmissionToDiffuse", 0)));
        }

        static void Shade(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            Set(material, "_ShadowStrength", 1);
            Set(material, "_ShadowBorderRange", 0);
            Set(material, "_ShadowNormalStrength", 1);
            Set(material, "_Shadow2ndNormalStrength", 1);
            Set(material, "_ShadowReceive", 1);
            Set(material, "_Shadow2ndReceive", 1);
            material.SetTexture("_ShadowColorTex", null);
            material.SetColor("_Shadow2ndColor", Color.clear);
            var type = Mathf.RoundToInt(values.Float("_ShadowType", 0));
            if (type == 0) { Ramp(values, material, dropped); return; }

            // The fade is range ± (1 - sharpness) over half-Lambert.
            Set(material, "_UseShadow", 1);
            var half = 1 - Mathf.Clamp01(values.Float("_ShadowSharpness", .9f));
            Set(material, "_ShadowBorder", Mathf.Clamp01(values.Float("_ShadowRange", .5f)));
            Set(material, "_ShadowBlur", Mathf.Clamp(half * 2, .001f, 1));
            var tint = Opaque(values, "_ShadowColor", Color.white);
            if (type == 1)
            {
                // Shade Map draws map × tint instead of the albedo, on the albedo's UV.
                if (!values.Texture("_ShadeMap", out var map, out _, out _) || !Flat(map)) map = null;
                Texture white = null;
                ShadeLayers.Absolute(material, "_ShadowColor", "_ShadowColorTex", tint, map, textures, ref white);
            }
            else material.SetColor("_ShadowColor", tint);
        }

        // The ramp's row is chosen by a mask, black by default, and only that
        // row is drawn; the ramp multiplies the albedo by half-Lambert.
        static void Ramp(SourceValues values, Material material, ICollection<string> dropped)
        {
            Set(material, "_UseShadow", 0);
            if (values.Texture("_RampSelectionMask", out var selection, out _, out _) && Flat(selection)) Drop(dropped, "Ramp Selection");
            if (!values.Texture("_Ramp", out var ramp, out _, out _) || !Flat(ramp) || ramp == null) return;
            var row = Sampled(ramp, 64, 1, new Vector2(1, 0), Vector2.zero);
            if (row == null) return;
            var brightness = new float[row.Length];
            for (var i = 0; i < row.Length; i++) brightness[i] = (row[i].r + row[i].g + row[i].b) / 3;
            if (!FitShadowEdge(brightness, material)) return;
            Set(material, "_UseShadow", 1);
            var dark = row[0].gamma;
            material.SetColor("_ShadowColor", new Color(dark.r, dark.g, dark.b, 1));
        }

        // Hue added, saturation and value multiplied, as lilToon's main color
        // adjustment. The source's mask reads a channel for each of the three;
        // lilToon has one, so a set mask takes the red. The source adjusts the
        // image times the color and lilToon only the image, so the color takes
        // the hue and saturation here unless a mask limits them to part of it.
        static void Adjust(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var hue = values.Float("_Hue", 0);
            var saturation = values.Float("_Saturation", 1);
            var value = values.Float("_Value", 1);
            if (hue == 0 && saturation == 1 && value == 1) return;
            material.SetVector("_MainTexHSVG", new Vector4(Mathf.DeltaAngle(0, hue * 360) / 360, saturation, value, 1));
            CopyMask(values, material, "_HSVMask", "", "", "_MainColorAdjustMask", "", textures, dropped, "HSV Mask");
            if (values.Texture("_HSVMask", out var mask, out _, out _) && Flat(mask)) return;
            var color = material.GetColor("_Color");
            Color.RGBToHSV(color, out var h, out var s, out var v);
            var adjusted = Color.HSVToRGB(Mathf.Repeat(h + hue, 1), Mathf.Clamp01(s * saturation), v);
            adjusted.a = color.a;
            material.SetColor("_Color", adjusted);
        }

        // saturate(1 - N·V) × N·L^threshold, fading over range ± sharpness.
        static void Rim(SourceValues values, Material material, ICollection<string> dropped)
        {
            var intensity = values.Float("_RimIntensity", 0);
            if (intensity <= 0) return;
            Set(material, "_UseRim", 1);
            var color = (Opaque(values, "_RimColor", Color.white).linear * intensity).gamma;
            color.a = 1;
            material.SetColor("_RimColor", color);
            Set(material, "_RimFresnelPower", 1);
            // lilToon scales the rim by half-Lambert moved by the range. The range
            // is chosen to equal N·L^threshold where N·L is .25; a threshold of 1
            // gives -0.5, which is N·L itself. Behind the light the source is 0
            // and lilToon's shadow mask below hides the rest.
            var threshold = values.Float("_RimThreshold", .1f);
            var atQuarter = Mathf.Pow(.25f, Mathf.Max(0, threshold));
            Set(material, "_RimDirStrength", threshold > 0 ? 1 : 0);
            Set(material, "_RimDirRange", atQuarter >= 1 ? 1 : Mathf.Clamp((atQuarter - .625f) / (1 - atQuarter), -.9f, 1));
            material.SetColor("_RimIndirColor", Color.clear);
            Set(material, "_RimBorder", Mathf.Clamp01(values.Float("_RimRange", .7f)));
            Set(material, "_RimBlur", Mathf.Clamp(values.Float("_RimSharpness", .1f) * 2, .001f, 1));
            Set(material, "_RimBlendMode", 1);
            Set(material, "_RimMainStrength", Mathf.Clamp01(values.Float("_RimAlbedoTint", 0)));
            Set(material, "_RimEnableLighting", 1);
            Set(material, "_RimShadowMask", Mathf.Clamp01(values.Float("_RimAttenEffect", 1)));
            Set(material, "_RimNormalStrength", 1);
            // The source samples _RimMask but its lighting never reads the result.
            if (values.Float("_RimCubemapTint", 0) > 0) Drop(dropped, "Rim Environment Tint");
        }

        // PBR, baked cubemap, matcap, off. MToon shows only the matcap.
        static void Reflection(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            var mode = Mathf.RoundToInt(values.Float("_ReflectionMode", 3));
            if (mode == 3) return;
            if (mode != 2) { Drop(dropped, "Reflection"); return; }
            if (!values.Texture("_Matcap", out var image, out var scale, out var offset) || !Flat(image) || image == null) return;
            var blend = Mathf.RoundToInt(values.Float("_ReflectionBlendMode", 0));
            if (blend == 2) { Drop(dropped, "Matcap (Subtractive)"); return; }
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", image, scale, offset);
            material.SetColor("_MatCapColor", Opaque(values, "_MatcapTint", Color.white));
            // Only the additive blend is lit, by the ambient plus half the
            // direct light; lilToon multiplies by the whole light.
            var added = blend == 0;
            Set(material, "_MatCapBlend", Mathf.Clamp01(values.Float("_Reflectivity", .5f)) * (added ? .5f : 1));
            Set(material, "_MatCapBlendMode", blend == 0 ? 1 : 3);
            Set(material, "_MatCapMainStrength", Mathf.Clamp01(values.Float("_MatcapTintToDiffuse", 0)));
            Set(material, "_MatCapEnableLighting", added ? 1 : 0);
            Set(material, "_MatCapNormalStrength", 1);
            CopyMask(values, material, "_ReflectivityMask", "", "", "_MatCapBlendMask", "_UseMatCap", textures, dropped, "Reflectivity Mask");
        }

        // The width is in centimeters in both.
        static void ApplyOutline(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { Drop(dropped, "Outline"); return; }
            Set(material, "_OutlineWidth", values.Float("_OutlineWidth", 1));
            Set(material, "_OutlineFixWidth", 0);
            Set(material, "_OutlineEnableLighting", values.Float("_OutlineLighting", 0) > .5f ? 0 : 1);
            material.SetTexture("_OutlineTex", null);
            var color = Opaque(values, "_OutlineColor", Color.black);
            // The tint multiplies by the image times the base color.
            if (values.Float("_OutlineAlbedoTint", 0) > .5f)
            {
                SetTexture(material, "_OutlineTex", material.GetTexture("_MainTex"), material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                color = (color.linear * material.GetColor("_Color").linear).gamma;
                color.a = 1;
            }
            material.SetColor("_OutlineColor", color);
            CopyMask(values, material, "_OutlineMask", "", "", "_OutlineWidthMask", "", textures, dropped, "Outline Mask");
            if (Mathf.RoundToInt(values.Float("_OutlineNormalMode", 0)) != 0) Drop(dropped, "Outline Normal Mode");
        }

        static Color Opaque(SourceValues values, string name, Color fallback)
        {
            var color = values.TryVector(name, out var vector) && Finite(vector) ? (Color)vector : fallback;
            color.a = 1;
            return color;
        }
    }
}
