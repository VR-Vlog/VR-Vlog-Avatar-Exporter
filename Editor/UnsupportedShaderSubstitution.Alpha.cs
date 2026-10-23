using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Toon shaders whose transparency is not the main image's alpha times the
    // color's alpha against one cutoff. Each family is known by property names
    // its shader declares (a missing shader keeps them in its saved values); its
    // alpha is rebuilt with lilToon's alpha mask, which the export bakes into the
    // main image. lilToon reads m = saturate(mask.r * _AlphaMaskScale +
    // _AlphaMaskValue), at the main image's coordinates times the mask's tiling,
    // and then sets a = m (mode 1), a *= m (2), a = saturate(a + m) (3) or
    // a = saturate(a - m) (4) before it clips by _Cutoff.
    internal static partial class UnsupportedShaderSubstitution
    {
        enum AlphaFamily { None, Uts2, Uts3, UnlitWF, Sunao, Arktoon, MToon }

        static AlphaFamily Family(SourceValues values)
        {
            if (values.TryFloat("_Clipping_Level", out _)) return values.TryFloat("_ClippingMode", out _) ? AlphaFamily.Uts3 : AlphaFamily.Uts2;
            if (values.TryFloat("_AL_Source", out _)) return AlphaFamily.UnlitWF;
            if (values.TryFloat("_AlphaMaskStrength", out _) && values.TryFloat("_Culling", out _)) return AlphaFamily.Sunao;
            if (values.TryFloat("_CutoutCutoutAdjust", out _) ||
                values.TryFloat("_UseDoubleSided", out _) && values.TryFloat("_DoubleSidedFlipBackfaceNormal", out _)) return AlphaFamily.Arktoon;
            if (values.TryFloat("_BlendMode", out _) && values.TryFloat("_ShadeToony", out _)) return AlphaFamily.MToon;
            return AlphaFamily.None;
        }

        // The rendering mode a family states in its own values, ahead of
        // keywords another shader may have left on the material.
        static Mode? FamilyMode(Material source, SourceValues values)
        {
            switch (Family(values))
            {
                case AlphaFamily.Uts3:
                    if (values.Float("_TransparentEnabled", 0) > .5f) return Mode.Transparent;
                    var clipping = Mathf.RoundToInt(values.Float("_ClippingMode", 0));
                    return clipping == 2 ? Mode.Transparent : clipping == 1 ? Mode.Cutout : Mode.Opaque;
                case AlphaFamily.Uts2:
                    // Each variant is its own shader. Transparent blends, and so
                    // does TransClipping except in the ToonColor and Mobile
                    // shaders, which only clip like Clipping.
                    if (source.shader != null && source.shader.name != MissingShader)
                    {
                        var name = source.shader.name;
                        if (Contains(name, "Transparent")) return Mode.Transparent;
                        if (Contains(name, "TransClipping") && !Contains(name, "ToonColor") && !Contains(name, "Mobile")) return Mode.Transparent;
                        return Contains(name, "Clipping") ? Mode.Cutout : Mode.Opaque;
                    }
                    // Only the clipping variants declare _Clipping_Level, so a
                    // missing one at least clips; the error shader's queue says
                    // nothing about it.
                    return values.Queue > 2500 ? Mode.Transparent : Mode.Cutout;
                case AlphaFamily.MToon:
                    var blend = Mathf.RoundToInt(values.Float("_BlendMode", 0));
                    return blend == 0 ? Mode.Opaque : blend == 1 ? Mode.Cutout : Mode.Transparent;
                default:
                    return null;
            }
        }

        static void ApplyFamilyAlpha(Material source, SourceValues values, Material material, Mode mode,
            Texture main, Vector2 mainScale, Vector2 mainOffset, List<Texture2D> textures)
        {
            switch (Family(values))
            {
                case AlphaFamily.Uts2: case AlphaFamily.Uts3: UtsAlpha(source, values, material, mode, main, mainScale, mainOffset, textures); break;
                case AlphaFamily.UnlitWF: WfAlpha(values, material, mode, main, textures); break;
                case AlphaFamily.Sunao: SunaoAlpha(values, material, mode, textures); break;
                case AlphaFamily.Arktoon: ArktoonAlpha(values, material, mode, mainScale, mainOffset); break;
            }
        }

        // UTS keeps a pixel where saturate(m + _Clipping_Level) >= 0.5 and, when
        // it blends, draws it with alpha saturate(m + _Tweak_transparency). m is
        // the clipping mask's red or, in the TransClipping variants when chosen,
        // the main image's alpha, inverted by _Inverse_Clipping. The base color's
        // alpha is not used. UTS2 samples the mask at its own tiling, UTS3 at the
        // main image's.
        static void UtsAlpha(Material source, SourceValues values, Material material, Mode mode,
            Texture main, Vector2 mainScale, Vector2 mainOffset, List<Texture2D> textures)
        {
            var color = material.GetColor("_Color");
            material.SetColor("_Color", new Color(color.r, color.g, color.b, 1));
            if (mode == Mode.Opaque) return;
            var uts3 = Family(values) == AlphaFamily.Uts3;
            var transMode = uts3 ? values.Float("_TransparentEnabled", 0) > .5f || Mathf.RoundToInt(values.Float("_ClippingMode", 0)) == 2
                : mode == Mode.Transparent || source.shader != null && Contains(source.shader.name, "TransClipping");
            var level = values.Float("_Clipping_Level", 0);
            var inverted = values.Float("_Inverse_Clipping", 0) > .5f;
            var blended = mode == Mode.Transparent;
            var tweak = values.Float("_Tweak_transparency", 0);
            // A blend draws m + tweak and clips where it falls below 0.5 -
            // level + tweak; a cutout draws m + level against 0.5.
            var shift = blended ? tweak : level;
            Set(material, "_Cutoff", blended ? Mathf.Clamp01(.5f - level + tweak) : .5f);

            if (transMode && values.Float("_IsBaseMapAlphaAsClippingMask", 0) > .5f && main != null)
            {
                if (!inverted)
                {
                    if (shift > 0) SetAlphaMask(material, 3, null, Vector2.one, Vector2.zero, 0, shift);
                    else if (shift < 0) SetAlphaMask(material, 4, null, Vector2.one, Vector2.zero, 0, -shift);
                    return;
                }
                var baked = BakeMask(main, 3, true, textures);
                if (baked != null) SetAlphaMask(material, 1, baked, Vector2.one, Vector2.zero, 1, shift);
                // Unbaked, the main alpha would be read the wrong way round;
                // drawing every pixel keeps the part visible.
                else { SetAlphaMask(material, 1, null, Vector2.one, Vector2.zero, 0, 1); Set(material, "_Cutoff", 0); }
                return;
            }
            // Without an image the mask is white.
            values.Texture("_ClippingMask", out var mask, out var scale, out var offset);
            if (!Flat(mask)) { mask = null; scale = Vector2.one; offset = Vector2.zero; }
            if (!uts3) MainRelative(mainScale, mainOffset, ref scale, ref offset);
            else { scale = Vector2.one; offset = Vector2.zero; }
            SetAlphaMask(material, 1, mask, scale, offset, inverted ? -1 : 1, (inverted ? 1 : 0) + shift);
        }

        // UnlitWF takes the alpha from the main image times the color (_AL_Source
        // 0), the mask's red (1) or the mask's alpha (2), inverted by
        // _AL_InvMaskVal; the mask shares the main image's coordinates. A cutout
        // compares it with _Cutoff and a blend scales it by _AL_Power.
        static void WfAlpha(SourceValues values, Material material, Mode mode, Texture main, List<Texture2D> textures)
        {
            if (mode == Mode.Opaque) return;
            // Its blended variants have no _CullMode and draw the back faces,
            // then the front faces.
            if (mode == Mode.Transparent && !values.TryFloat("_CullMode", out _)) Set(material, "_Cull", 0);
            var power = mode == Mode.Transparent ? Mathf.Max(0, values.Float("_AL_Power", 1)) : 1;
            var inverted = values.Float("_AL_InvMaskVal", 0) > .5f;
            var from = Mathf.RoundToInt(values.Float("_AL_Source", 0));
            if (from == 1 || from == 2)
            {
                values.Texture("_AL_MaskTex", out var mask, out _, out _);
                if (!Flat(mask)) mask = null;
                if (from == 2 && mask != null)
                {
                    var alpha = BakeMask(mask, 3, inverted, textures);
                    if (alpha != null) SetAlphaMask(material, 1, alpha, Vector2.one, Vector2.zero, power, 0);
                    return;
                }
                SetAlphaMask(material, 1, mask, Vector2.one, Vector2.zero, inverted ? -power : power, inverted ? power : 0);
                return;
            }
            var color = material.GetColor("_Color");
            var scaled = color.a * power;
            if (main == null || !inverted && scaled <= 1)
            {
                color.a = Mathf.Clamp01(inverted ? (1 - color.a) * power : scaled);
                material.SetColor("_Color", color);
                return;
            }
            var baked = BakeMask(main, 3, inverted, textures);
            if (baked == null)
            {
                if (!inverted) material.SetColor("_Color", new Color(color.r, color.g, color.b, 1));
                return;
            }
            // Inverted, 1 - a * c = (1 - a) * c + (1 - c).
            SetAlphaMask(material, 1, baked, Vector2.one, Vector2.zero, scaled, inverted ? (1 - color.a) * power : 0);
        }

        // Sunao multiplies the alpha by _Alpha (up to 2), saturates it and
        // multiplies it by lerp(1, luminance of the alpha mask, strength); the
        // mask shares the main image's coordinates.
        static void SunaoAlpha(SourceValues values, Material material, Mode mode, List<Texture2D> textures)
        {
            if (mode == Mode.Opaque) return;
            var color = material.GetColor("_Color");
            var factor = color.a * Mathf.Max(0, values.Float("_Alpha", 1));
            material.SetColor("_Color", new Color(color.r, color.g, color.b, Mathf.Min(1, factor)));
            // a * factor >= cutoff where a >= cutoff / factor.
            if (mode == Mode.Cutout && factor > 1) Set(material, "_Cutoff", material.GetFloat("_Cutoff") / factor);
            var strength = Mathf.Clamp01(values.Float("_AlphaMaskStrength", 1));
            if (strength <= 0 || !values.Texture("_AlphaMask", out var mask, out _, out _) || !Flat(mask)) return;
            var luminance = Grey(mask) ? mask : BakeMask(mask, 4, false, textures);
            if (luminance != null) SetAlphaMask(material, 2, luminance, Vector2.one, Vector2.zero, strength, 1 - strength);
        }

        // arktoon draws both faces itself when _UseDoubleSided is on. Its Fade
        // variant multiplies the alpha by the mask's red at the mask's own tiling.
        static void ArktoonAlpha(SourceValues values, Material material, Mode mode, Vector2 mainScale, Vector2 mainOffset)
        {
            if (values.TryFloat("_UseDoubleSided", out var sides)) Set(material, "_Cull", sides > .5f ? 0 : 2);
            if (mode != Mode.Transparent || !values.Texture("_AlphaMask", out var mask, out var scale, out var offset) || !Flat(mask)) return;
            MainRelative(mainScale, mainOffset, ref scale, ref offset);
            SetAlphaMask(material, 2, mask, scale, offset, 1, 0);
        }

        static void SetAlphaMask(Material material, int mode, Texture mask, Vector2 scale, Vector2 offset, float maskScale, float maskValue)
        {
            Set(material, "_AlphaMaskMode", mode);
            SetTexture(material, "_AlphaMask", mask, scale, offset);
            Set(material, "_AlphaMaskScale", maskScale);
            Set(material, "_AlphaMaskValue", maskValue);
        }

        // A source mask sampled at uv * scale + offset, as lilToon's tiling on top
        // of the main image's: (uv * mainScale + mainOffset) * s + o.
        static void MainRelative(Vector2 mainScale, Vector2 mainOffset, ref Vector2 scale, ref Vector2 offset)
        {
            if (mainScale.x == 0 || mainScale.y == 0) return;
            var relative = new Vector2(scale.x / mainScale.x, scale.y / mainScale.y);
            offset -= Vector2.Scale(mainOffset, relative);
            scale = relative;
        }

        // Whether the red channel equals the luminance (also when it cannot be checked).
        static bool Grey(Texture mask)
        {
            foreach (var c in Sampled(mask, 32, 32) ?? System.Array.Empty<Color>())
                if (Mathf.Abs(c.r - c.g) > .02f || Mathf.Abs(c.r - c.b) > .02f) return false;
            return true;
        }
    }
}
