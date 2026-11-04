using System.Collections.Generic;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;
using static VRVlog.LilToonExporter.UnsupportedShaderSubstitution;

namespace VRVlog.LilToonExporter
{
    // Unity-chan Toon Shader 2 and 3. Both keep the same names for what is
    // written here. The generic mapping reads the main image and color, normal
    // map, emission, cull and transparency (Alpha.cs); this class replaces its
    // shade with the shader's two layers and adds rim, matcap and outline.
    //
    // Both lit values use half-Lambert, as lilToon's shadow does. The shader
    // fades a layer in over step - feather .. step; lilToon centers its fade on
    // the border and its blur is the full width.
    internal sealed class UtsApproximation : IShaderApproximation
    {
        // The variants are many shaders with these names in common, and a
        // missing shader keeps them in its saved values.
        public bool Matches(string shaderName, SourceValues values) =>
            values.TryFloat("_Is_LightColor_Base", out _) && values.TryVector("_1st_ShadeColor", out _);

        // The variant and mode are read by the transparency mapping.
        public Mode? RenderMode(SourceValues values) => null;

        // The variants without an outline do not declare the width.
        public bool Outline(SourceValues values) => values.Float("_Outline_Width", 0) > 0;

        public void Apply(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            Shade(values, material, textures, dropped);
            Rim(values, material, textures, dropped);
            Matcap(values, material, textures, dropped);
            if (Outline(values)) ApplyOutline(values, material, textures, dropped);
            if (Opaque(values, "_HighColor", Color.black).maxColorComponent > 0) Drop(dropped, "High Color");
            if (values.Float("_AngelRing", 0) > .5f) Drop(dropped, "Angel Ring");
            if (values.Keyword("_EMISSIVE_ANIMATION")) Drop(dropped, "Emission Animation");
        }

        static void Shade(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            // _utsTechnique: 0 is the double shade with feather, 1 the shading
            // grade map, which has its own steps and multiplies the light by a
            // map as lilToon's border mask does.
            var graded = values.TryFloat("_utsTechnique", out var technique) ? technique > .5f : !values.TryFloat("_BaseColor_Step", out _);
            var step = values.Float(graded ? "_1st_ShadeColor_Step" : "_BaseColor_Step", .5f);
            var feather = Mathf.Max(.001f, values.Float(graded ? "_1st_ShadeColor_Feather" : "_BaseShade_Feather", .0001f));
            Set(material, "_UseShadow", 1);
            Set(material, "_ShadowStrength", 1);
            Set(material, "_ShadowBorderRange", 0);
            Set(material, "_ShadowBorder", Mathf.Clamp01(step - feather / 2));
            Set(material, "_ShadowBlur", Mathf.Clamp(feather, .001f, 1));
            Set(material, "_ShadowNormalStrength", values.Float("_Is_NormalMapToBase", 0) > .5f ? 1 : 0);
            Set(material, "_ShadowReceive", values.Float("_Set_SystemShadowsToBase", 1) > .5f ? 1 : 0);
            material.SetTexture("_ShadowColorTex", null);

            var mainColor = material.GetColor("_Color");
            var useMain = values.Float("_Use_BaseAs1st", 0) > .5f;
            if (!values.Texture("_1st_ShadeMap", out var map, out _, out _) || !Flat(map)) map = null;
            Texture white = null;
            Layer(material, "_ShadowColor", "_ShadowColorTex", Opaque(values, "_1st_ShadeColor", Color.white), map, useMain, mainColor, textures, ref white);

            var step2 = values.Float(graded ? "_2nd_ShadeColor_Step" : "_ShadeColor_Step", 0);
            if (step2 > 0)
            {
                var feather2 = Mathf.Max(.001f, values.Float(graded ? "_2nd_ShadeColor_Feather" : "_1st2nd_Shades_Feather", .0001f));
                Set(material, "_Shadow2ndBorder", Mathf.Clamp01(step2 - feather2 / 2));
                Set(material, "_Shadow2ndBlur", Mathf.Clamp(feather2, .001f, 1));
                var useFirst = values.Float("_Use_1stAs2nd", 0) > .5f;
                if (!useFirst && (!values.Texture("_2nd_ShadeMap", out map, out _, out _) || !Flat(map))) map = null;
                Layer(material, "_Shadow2ndColor", "_Shadow2ndColorTex", Opaque(values, "_2nd_ShadeColor", Color.white), map, useFirst && useMain, mainColor, textures, ref white);
            }
            else material.SetColor("_Shadow2ndColor", Color.clear);

            // lilToon multiplies its light by the mask's red for the first
            // layer and green for the second.
            if (graded)
            {
                var level = values.Float("_Tweak_ShadingGradeMapLevel", 0);
                if (values.Texture("_ShadingGradeMap", out var grade, out var scale, out var offset) && Flat(grade))
                {
                    SetTexture(material, "_ShadowBorderMask", grade, scale, offset);
                    material.SetVector("_ShadowAOShift", new Vector4(1, level, 1, level));
                }
            }
            else
            {
                // Black in the position map always takes the shade.
                if (values.Texture("_Set_1st_ShadePosition", out var position, out var scale, out var offset) && Flat(position))
                {
                    SetTexture(material, "_ShadowBorderMask", position, scale, offset);
                    material.SetVector("_ShadowAOShift", new Vector4(1, 0, 0, 1));
                }
                if (values.Texture("_Set_2nd_ShadePosition", out position, out _, out _) && Flat(position)) Drop(dropped, "2nd Shade Position");
            }
        }

        static void Layer(Material material, string colorName, string textureName, Color color, Texture map, bool useMain, Color mainColor,
            List<Texture2D> textures, ref Texture white)
        {
            if (useMain) material.SetColor(colorName, Ratio(color, mainColor));
            else ShadeLayers.Absolute(material, colorName, textureName, color, map, textures, ref white);
        }

        // The shader's rim is pow(1 - N·V, 2^lerp(3, 0, power)), rising from
        // the inside mask to 1, or stepping at it with the feather off.
        static void Rim(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_RimLight", 0) < .5f) return;
            Set(material, "_UseRim", 1);
            material.SetColor("_RimColor", Opaque(values, "_RimLightColor", Color.white));
            Set(material, "_RimFresnelPower", Mathf.Pow(2, Mathf.Lerp(3, 0, Mathf.Clamp01(values.Float("_RimLight_Power", .1f)))));
            var inside = Mathf.Clamp01(values.Float("_RimLight_InsideMask", .0001f));
            var hard = values.Float("_RimLight_FeatherOff", 0) > .5f;
            Set(material, "_RimBorder", hard ? inside : (1 + inside) / 2);
            Set(material, "_RimBlur", hard ? .001f : Mathf.Max(.001f, 1 - inside));
            Set(material, "_RimBlendMode", 1);
            Set(material, "_RimMainStrength", 0);
            Set(material, "_RimEnableLighting", values.Float("_Is_LightColor_RimLight", 1) > .5f ? 1 : 0);
            Set(material, "_RimNormalStrength", values.Float("_Is_NormalMapToRimLight", 0) > .5f ? 1 : 0);
            // The direction mask keeps the rim on the lit side.
            Set(material, "_RimShadowMask", values.Float("_LightDirection_MaskOn", 0) > .5f ? 1 : 0);
            // The mask's green is read.
            CopyMask(values, material, "_Set_RimLightMask", "", "", "_RimColorTex", "_UseRim", textures, dropped, "Rim Light Mask", 1);
            if (values.Float("_Tweak_RimLightMaskLevel", 0) != 0) Drop(dropped, "Rim Light Mask Level");
            if (values.Float("_Add_Antipodean_RimLight", 0) > .5f) Drop(dropped, "Antipodean Rim Light");
        }

        static void Matcap(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (values.Float("_MatCap", 0) < .5f) return;
            // Without an image the shader's slot is black, which adds nothing
            // and would need no matcap in lilToon either.
            if (!values.Texture("_MatCap_Sampler", out var image, out var scale, out var offset) || !Flat(image)) return;
            Set(material, "_UseMatCap", 1);
            SetTexture(material, "_MatCapTex", image, scale, offset);
            material.SetColor("_MatCapColor", Opaque(values, "_MatCapColor", Color.white));
            Set(material, "_MatCapBlend", 1);
            Set(material, "_MatCapBlendMode", values.Float("_Is_BlendAddToMatCap", 1) > .5f ? 1 : 3);
            Set(material, "_MatCapMainStrength", 0);
            Set(material, "_MatCapEnableLighting", values.Float("_Is_LightColor_MatCap", 1) > .5f ? 1 : 0);
            Set(material, "_MatCapLod", Mathf.Max(0, values.Float("_BlurLevelMatcap", 0)));
            Set(material, "_MatCapNormalStrength", 0);
            Set(material, "_MatCapShadowMask", values.Float("_Is_UseTweakMatCapOnShadow", 0) > .5f ? Mathf.Clamp01(1 - values.Float("_TweakMatCapOnShadow", 0)) : 0);
            // The mask's green is read, and the shader can invert it.
            CopyMask(values, material, "_Set_MatcapMask", "", "_Inverse_MatcapMask", "_MatCapBlendMask", "_UseMatCap", textures, dropped, "Matcap Mask", 1);
            if (values.Float("_Tweak_MatcapMaskLevel", 0) != 0) Drop(dropped, "Matcap Mask Level");
            if (values.Float("_Tweak_MatCapUV", 0) != 0 || values.Float("_Rotate_MatCapUV", 0) != 0) Drop(dropped, "Matcap UV");
            if (values.Float("_Is_NormalMapForMatCap", 0) > .5f) Drop(dropped, "Matcap Normal Map");
        }

        // The shader takes the width in millimeters along the normal; lilToon
        // takes centimeters.
        static void ApplyOutline(SourceValues values, Material material, List<Texture2D> textures, ICollection<string> dropped)
        {
            if (!material.HasProperty("_OutlineWidth")) { Drop(dropped, "Outline"); return; }
            Set(material, "_OutlineWidth", values.Float("_Outline_Width", 0) * .1f);
            Set(material, "_OutlineFixWidth", 0);
            Set(material, "_OutlineEnableLighting", values.Float("_Is_LightColor_Outline", 1) > .5f ? 1 : 0);
            var color = Opaque(values, "_Outline_Color", Color.white);
            material.SetTexture("_OutlineTex", null);
            if (values.Float("_Is_OutlineTex", 0) > .5f && values.Texture("_OutlineTex", out var image, out var scale, out var offset) && Flat(image))
                SetTexture(material, "_OutlineTex", image, scale, offset);
            else if (values.Float("_Is_BlendBaseColor", 0) > .5f)
            {
                // The source multiplies by the base twice. lilToon draws one
                // image, so the second one is the base color only.
                SetTexture(material, "_OutlineTex", material.GetTexture("_MainTex"), material.GetTextureScale("_MainTex"), material.GetTextureOffset("_MainTex"));
                var main = material.GetColor("_Color");
                color *= main * main;
                color.a = 1;
            }
            material.SetColor("_OutlineColor", color);
            CopyMask(values, material, "_Outline_Sampler", "", "", "_OutlineWidthMask", "", textures, dropped, "Outline Mask");
            if (values.Float("_Is_BakedNormal", 0) > .5f) Drop(dropped, "Baked Normal for Outline");
        }

        static Color Opaque(SourceValues values, string name, Color fallback)
        {
            var color = values.TryVector(name, out var vector) && Finite(vector) ? (Color)vector : fallback;
            color.a = 1;
            return color;
        }
    }
}
