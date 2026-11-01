using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using VRVlog.LilToon;

namespace VRVlog.LilToonExporter
{
    // 自動で軽量化する: lilToon 2.3.4 texture slots the shader samples only while
    // a feature switch is on. The app never changes these switches (expressions
    // edit colours and UVs only), so a slot whose switch is off is never drawn.
    // Every row was checked against the 2.3.4 shader source; a slot not listed
    // here, or a switch the material lacks, counts as sampled.
    internal static class LilToonInactiveSlots
    {
        const string Outline = "outline";

        // Slot → the switch it is sampled under. The outline slots are read
        // only by outline passes; the dissolve slots under _DissolveParams.x.
        static readonly Dictionary<string, string> Switches = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            {"_Main2ndTex", "_UseMain2ndTex"}, {"_Main2ndBlendMask", "_UseMain2ndTex"},
            {"_Main2ndDissolveMask", "_UseMain2ndTex"}, {"_Main2ndDissolveNoiseMask", "_UseMain2ndTex"},
            {"_Main3rdTex", "_UseMain3rdTex"}, {"_Main3rdBlendMask", "_UseMain3rdTex"},
            {"_Main3rdDissolveMask", "_UseMain3rdTex"}, {"_Main3rdDissolveNoiseMask", "_UseMain3rdTex"},
            {"_AlphaMask", "_AlphaMaskMode"},
            {"_BumpMap", "_UseBumpMap"},
            {"_Bump2ndMap", "_UseBump2ndMap"}, {"_Bump2ndScaleMask", "_UseBump2ndMap"},
            {"_AnisotropyTangentMap", "_UseAnisotropy"}, {"_AnisotropyScaleMask", "_UseAnisotropy"}, {"_AnisotropyShiftNoiseMask", "_UseAnisotropy"},
            {"_BacklightColorTex", "_UseBacklight"},
            {"_ShadowStrengthMask", "_UseShadow"}, {"_ShadowBorderMask", "_UseShadow"}, {"_ShadowBlurMask", "_UseShadow"},
            {"_ShadowColorTex", "_UseShadow"}, {"_Shadow2ndColorTex", "_UseShadow"}, {"_Shadow3rdColorTex", "_UseShadow"},
            {"_RimShadeMask", "_UseRimShade"},
            {"_SmoothnessTex", "_UseReflection"}, {"_MetallicGlossMap", "_UseReflection"},
            {"_ReflectionColorTex", "_UseReflection"}, {"_ReflectionCubeTex", "_UseReflection"},
            {"_MatCapTex", "_UseMatCap"}, {"_MatCapBlendMask", "_UseMatCap"}, {"_MatCapBumpMap", "_UseMatCap"},
            {"_MatCap2ndTex", "_UseMatCap2nd"}, {"_MatCap2ndBlendMask", "_UseMatCap2nd"}, {"_MatCap2ndBumpMap", "_UseMatCap2nd"},
            {"_RimColorTex", "_UseRim"},
            {"_GlitterColorTex", "_UseGlitter"}, {"_GlitterShapeTex", "_UseGlitter"},
            {"_EmissionMap", "_UseEmission"}, {"_EmissionBlendMask", "_UseEmission"}, {"_EmissionGradTex", "_UseEmission"},
            {"_Emission2ndMap", "_UseEmission2nd"}, {"_Emission2ndBlendMask", "_UseEmission2nd"}, {"_Emission2ndGradTex", "_UseEmission2nd"},
            {"_ParallaxMap", "_UseParallax"},
            {"_DissolveMask", "_DissolveParams"}, {"_DissolveNoiseMask", "_DissolveParams"},
            {"_OutlineTex", Outline}, {"_OutlineWidthMask", Outline}, {"_OutlineVectorTex", Outline},
        };

        // The gem pass and refraction blur read _SmoothnessTex, and the gem pass
        // the reflection cube, without _UseReflection.
        static readonly string[] UngatedReflectionFamilies = {"lts_gem", "lts_ref_blur"};

        internal static bool Sampled(Material material, string slot)
        {
            if (material == null || material.shader == null || !Switches.TryGetValue(slot ?? "", out var feature) ||
                !LilToon234Catalogue.Shaders.TryGetValue(material.shader.name, out var family)) return true;
            // lilToonMulti selects its features with keywords, not these floats.
            if (family.StartsWith("ltsmulti", StringComparison.Ordinal)) return true;
            if (feature == "_UseReflection" && Array.IndexOf(UngatedReflectionFamilies, family) >= 0) return true;
            if (feature == Outline) return OutlineDrawn(material);
            if (!material.HasProperty(feature)) return true;
            return feature == "_DissolveParams" ? material.GetVector(feature).x != 0 : material.GetFloat(feature) != 0;
        }

        // An outline pass of this shader is drawn: the pass is in the 2.3.4
        // record and its LightMode is enabled on the material.
        static bool OutlineDrawn(Material material)
        {
            var shader = material.shader.name;
            if (!LilToon234Catalogue.Passes.TryGetValue(shader, out var passes) ||
                !LilToon234Catalogue.PassLightModes.TryGetValue(shader, out var modes)) return true;
            return passes.Where(pass => pass.IndexOf("OUTLINE", StringComparison.Ordinal) >= 0)
                .Any(pass => !modes.TryGetValue(pass, out var mode) || material.GetShaderPassEnabled(mode));
        }
    }
}
