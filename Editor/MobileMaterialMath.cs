using System;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal static class MobileMaterialMath
    {
        internal static float ShadowShift(float border, float blur)
        {
            var lower = Mathf.Clamp01(border - Mathf.Clamp01(blur) * 0.5f);
            var upper = Mathf.Clamp01(border + Mathf.Clamp01(blur) * 0.5f);
            return 1f - lower - upper;
        }

        internal static float ShadowToony(float border, float blur)
        {
            var lower = Mathf.Clamp01(border - Mathf.Clamp01(blur) * 0.5f);
            var upper = Mathf.Clamp01(border + Mathf.Clamp01(blur) * 0.5f);
            return 1f - (upper - lower);
        }

        internal static Color ShadeColor(Color main, Color shade, float strength)
        {
            var weight = Mathf.Clamp01(strength);
            main = main.linear;
            shade = shade.linear;
            return new Color(main.r + (shade.r - main.r) * weight,
                main.g + (shade.g - main.g) * weight,
                main.b + (shade.b - main.b) * weight, 1f).gamma;
        }

        // Without a shadow color texture lilToon 2.3.4 shades with
        // albedo * _ShadowColor, and _ShadowStrength fades the shadow toward
        // the lit color: albedo * lerp(1, shade, strength). MToon multiplies
        // its shade image (the main image here) by this factor, so the main
        // color must be kept as a factor rather than blended toward the shade
        // color. lilToon also never lets the shadow be brighter than the lit
        // side, hence the upper limit of 1.
        internal static Color MultipliedShadeColor(Color main, Color shade, float strength)
        {
            var weight = Mathf.Clamp01(strength);
            main = main.linear;
            shade = shade.linear;
            return new Color(main.r * Math.Min(1f, 1f + (shade.r - 1f) * weight),
                main.g * Math.Min(1f, 1f + (shade.g - 1f) * weight),
                main.b * Math.Min(1f, 1f + (shade.b - 1f) * weight), 1f).gamma;
        }

        // lilToon blends _Shadow2ndColor over the 1st shadow color by its alpha
        // where the light falls below _Shadow2ndBorder. MToon has one shade
        // band, so the 2nd color is weighted by the share of the 1st band it
        // covers and folded into the shade color passed to ShadeColor. The
        // blend runs in linear space like the shader; an unused 2nd shadow
        // (alpha 0 or border 0) returns the 1st color unchanged.
        internal static Color Shadow2ndShade(Color shadow, Color shadow2nd, float border, float border2nd)
        {
            var coverage = Mathf.Clamp01(Mathf.Clamp01(border2nd) / Math.Max(Mathf.Clamp01(border), 0.0001f));
            var weight = Mathf.Clamp01(shadow2nd.a) * coverage;
            if (!(weight > 0f)) return shadow;
            var first = shadow.linear;
            var second = shadow2nd.linear;
            return new Color(first.r + (second.r - first.r) * weight,
                first.g + (second.g - first.g) * weight,
                first.b + (second.b - first.b) * weight, shadow.a).gamma;
        }

        internal static float RimPower(float border, float fresnel)
        {
            // Standard MToon has no independent rim border/blur. Match the
            // authored half-intensity angle without a positive lift, which
            // would add a highlight even on a front-facing surface.
            var threshold = Math.Min(0.999f, Math.Max(0.001f, border));
            var power = Math.Max(0.0001f, fresnel);
            return (float)Math.Min(100.0, Math.Max(0.0001, Math.Log(0.5) * power / Math.Log(threshold)));
        }

        internal static Color MatcapColor(Color color, float blend)
        {
            // lilToon multiplies the MatCap contribution by color alpha.
            // MToon samples RGB only, so fold alpha into its RGB factor.
            var strength = Mathf.Clamp01(color.a) * Mathf.Clamp01(blend);
            // Material color factors are sRGB. Multiply the light contribution
            // in linear space before returning an sRGB material property.
            var linear = color.linear;
            return new Color(linear.r * strength, linear.g * strength, linear.b * strength, 1f).gamma;
        }

        internal static Color OutlineMask(Color sampled)
        {
            // lilToon reads red; MToon reads green. The input has already
            // been sampled in linear space, respecting the source importer.
            var width = Mathf.Clamp01(sampled.r);
            return new Color(width, width, width, 1f);
        }
    }
}
