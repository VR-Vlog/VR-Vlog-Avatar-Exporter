// Declares the property names of a third-party toon shader built of
// switchable sections, with that shader's defaults, so the section-by-section
// substitution can be tested without shipping it.
Shader "Hidden/VRVlogTests/SectionedToonProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _MainTexPan ("Main Pan", Vector) = (0,0,0,0)
        _Mode ("Rendering Preset", Int) = 0
        _Cutoff ("Cutoff", Range(0, 1.001)) = 0.5
        _SrcBlend ("Source Blend", Int) = 1
        _DstBlend ("Destination Blend", Int) = 0
        _Cull ("Cull", Int) = 2
        // Color adjust.
        _MainColorAdjustToggle ("Adjust Colors", Float) = 0
        _MainColorAdjustTexture ("Mask", 2D) = "white" {}
        _Saturation ("Saturation", Range(-1, 10)) = 0
        _MainBrightness ("Brightness", Range(-1, 1)) = 0
        _MainGamma ("Gamma", Range(0.01, 5)) = 1
        _MainHueShiftToggle ("Hue Shift", Float) = 0
        _MainHueShift ("Hue Shift", Range(0, 1)) = 0
        _MainHueShiftSelectOrShift ("Select or Shift", Int) = 1
        _MainHueShiftReplace ("Replace", Float) = 1
        _MainHueShiftSpeed ("Hue Shift Speed", Float) = 0
        // Lighting.
        _LightingMode ("Lighting Type", Int) = 5
        _ShadingEnabled ("Shading", Float) = 1
        _LightingIgnoreAmbientColor ("Ignore Indirect Shadow Color", Range(0, 1)) = 1
        _ShadowStrength ("Shadow Strength", Range(0, 1)) = 1
        _LightingCastedShadows ("Receive Casted Shadows", Range(0, 1)) = 0
        _LightingCapEnabled ("Limit Brightness", Float) = 1
        _LightingCap ("Max Brightness", Range(0, 10)) = 1
        _LightingMinLightBrightness ("Min Brightness", Range(0, 1)) = 0
        _LightingMonochromatic ("Monochrome Lighting", Range(0, 1)) = 0
        _LightingGradientStart ("Gradient Start", Range(0, 1)) = 0
        _LightingGradientEnd ("Gradient End", Range(0, 1)) = 0.5
        _BaseColor_Step ("Base Step", Range(0.01, 1)) = 0.5
        _BaseShade_Feather ("Base Feather", Range(0.0001, 1)) = 0.0001
        _ShadeColor_Step ("Shade Step", Range(0, 1)) = 0
        _1st2nd_Shades_Feather ("Shade Feather", Range(0.0001, 1)) = 0.0001
        _1st_ShadeColor ("1st Shade Color", Color) = (1,1,1,1)
        _1st_ShadeMap ("1st Shade Map", 2D) = "white" {}
        _2nd_ShadeColor ("2nd Shade Color", Color) = (1,1,1,1)
        _2nd_ShadeMap ("2nd Shade Map", 2D) = "white" {}
        _Use_BaseAs1st ("Use Base as 1st", Float) = 0
        _Use_1stAs2nd ("Use 1st as 2nd", Float) = 0
        _ShadingShadeMapBlendType ("Blend Mode", Int) = 0
        _ShadowColor ("Shadow Color", Color) = (0.82,0.76,0.85,1)
        _ShadowBorder ("Shadow Border", Range(0, 1)) = 0.5
        _ShadowBlur ("Shadow Blur", Range(0, 1)) = 0.1
        _Shadow2ndColor ("Shadow 2nd Color", Color) = (0.68,0.66,0.79,1)
        _ShadowBorderColor ("Shadow Border Color", Color) = (1,0.1,0,1)
        // Matcap.
        _MatcapEnable ("Matcap", Float) = 0
        _Matcap ("Matcap", 2D) = "white" {}
        _MatcapColor ("Color", Color) = (1,1,1,1)
        _MatcapIntensity ("Intensity", Range(0, 5)) = 1
        _MatcapBorder ("Border", Range(0, 0.5)) = 0.5
        _MatcapSmoothness ("Smoothness", Range(0, 1)) = 1
        _MatcapReplace ("Replace", Range(0, 1)) = 1
        _MatcapMultiply ("Multiply", Range(0, 1)) = 0
        _MatcapAdd ("Add", Range(0, 1)) = 0
        _MatcapUVMode ("UV Mode", Int) = 1
        _MatcapMask ("Mask", 2D) = "white" {}
        _MatcapMaskChannel ("Mask Channel", Int) = 0
        _MatcapMaskInvert ("Invert Mask", Float) = 0
        // Rim.
        _EnableRimLighting ("Rim Lighting", Float) = 0
        _RimStyle ("Rim Style", Int) = 0
        _RimLightColor ("Rim Color", Color) = (1,1,1,1)
        _RimWidth ("Width", Range(0, 1)) = 0.8
        _RimBlur ("Sharpness", Range(0, 1)) = 0.65
        _RimPower ("Power", Range(0, 10)) = 1
        _RimBrightness ("Brightness", Range(0, 10)) = 1
        _RimBlendStrength ("Blend Strength", Range(0, 1)) = 1
        _RimStrength ("Emission", Range(0, 20)) = 0
        _RimPoiBlendMode ("Blend Mode", Int) = 0
        _RimMask ("Mask", 2D) = "white" {}
        _RimMaskChannel ("Mask Channel", Int) = 0
        _RimMaskInvert ("Invert Mask", Float) = 0
        // Outline.
        _EnableOutlines ("Outline", Float) = 0
        _LineWidth ("Width", Float) = 1
        _LineColor ("Color", Color) = (0.79,0.77,0.87,1)
        _OutlineFixedSize ("Fixed Size", Float) = 1
        _OutlineFixWidth ("Fixed Width", Range(0, 1)) = 0.5
        // Stencil.
        _StencilRef ("Ref", Int) = 0
        _StencilCompareFunction ("Compare", Int) = 8
        _StencilPassOp ("Pass", Int) = 0
        _StencilType ("Stencil Type", Float) = 0
        // Decals.
        _DecalMask ("Decal Mask", 2D) = "white" {}
        _DecalMaskUV ("Decal Mask UV", Int) = 0
        _DecalMaskPan ("Decal Mask Pan", Vector) = (0,0,0,0)
        _DecalEnabled ("DecalEnabled", Float) = 0
        _DecalTexture ("DecalTexture", 2D) = "white" {}
        _DecalTextureUV ("DecalTextureUV", Int) = 0
        _DecalTexturePan ("DecalTexturePan", Vector) = (0,0,0,0)
        _DecalPosition ("DecalPosition", Vector) = (0.5,0.5,0,0)
        _DecalRotation ("DecalRotation", Range(0, 360)) = 0
        _DecalRotationSpeed ("DecalRotationSpeed", Float) = 0
        _DecalScale ("DecalScale", Vector) = (1,1,1,0)
        _DecalSideOffset ("DecalSideOffset", Vector) = (0,0,0,0)
        _DecalMirroredUVMode ("DecalMirroredUVMode", Int) = 0
        _DecalSymmetryMode ("DecalSymmetryMode", Int) = 0
        _DecalTiled ("DecalTiled", Float) = 0
        _DecalColor ("DecalColor", Color) = (1,1,1,1)
        _DecalEmissionStrength ("DecalEmissionStrength", Range(0, 20)) = 0
        _DecalBlendType ("DecalBlendType", Range(0, 1)) = 0
        _DecalBlendAlpha ("DecalBlendAlpha", Range(0, 1)) = 1
        _DecalAlphaIntensity ("DecalAlphaIntensity", Range(0, 10)) = 1
        _Decal0MaskChannel ("Decal 0 Mask Channel", Int) = 0
        _DecalEnabled1 ("DecalEnabled1", Float) = 0
        _DecalTexture1 ("DecalTexture1", 2D) = "white" {}
        _DecalTexture1UV ("DecalTexture1UV", Int) = 0
        _DecalTexture1Pan ("DecalTexture1Pan", Vector) = (0,0,0,0)
        _DecalPosition1 ("DecalPosition1", Vector) = (0.5,0.5,0,0)
        _DecalRotation1 ("DecalRotation1", Range(0, 360)) = 0
        _DecalRotationSpeed1 ("DecalRotationSpeed1", Float) = 0
        _DecalScale1 ("DecalScale1", Vector) = (1,1,1,0)
        _DecalSideOffset1 ("DecalSideOffset1", Vector) = (0,0,0,0)
        _DecalMirroredUVMode1 ("DecalMirroredUVMode1", Int) = 0
        _DecalSymmetryMode1 ("DecalSymmetryMode1", Int) = 0
        _DecalTiled1 ("DecalTiled1", Float) = 0
        _DecalColor1 ("DecalColor1", Color) = (1,1,1,1)
        _DecalEmissionStrength1 ("DecalEmissionStrength1", Range(0, 20)) = 0
        _DecalBlendType1 ("DecalBlendType1", Range(0, 1)) = 0
        _DecalBlendAlpha1 ("DecalBlendAlpha1", Range(0, 1)) = 1
        _DecalAlphaIntensity1 ("DecalAlphaIntensity1", Range(0, 10)) = 1
        _Decal1MaskChannel ("Decal 1 Mask Channel", Int) = 1
        _DecalEnabled2 ("DecalEnabled2", Float) = 0
        _DecalTexture2 ("DecalTexture2", 2D) = "white" {}
        _DecalTexture2UV ("DecalTexture2UV", Int) = 0
        _DecalTexture2Pan ("DecalTexture2Pan", Vector) = (0,0,0,0)
        _DecalPosition2 ("DecalPosition2", Vector) = (0.5,0.5,0,0)
        _DecalRotation2 ("DecalRotation2", Range(0, 360)) = 0
        _DecalRotationSpeed2 ("DecalRotationSpeed2", Float) = 0
        _DecalScale2 ("DecalScale2", Vector) = (1,1,1,0)
        _DecalSideOffset2 ("DecalSideOffset2", Vector) = (0,0,0,0)
        _DecalMirroredUVMode2 ("DecalMirroredUVMode2", Int) = 0
        _DecalSymmetryMode2 ("DecalSymmetryMode2", Int) = 0
        _DecalTiled2 ("DecalTiled2", Float) = 0
        _DecalColor2 ("DecalColor2", Color) = (1,1,1,1)
        _DecalEmissionStrength2 ("DecalEmissionStrength2", Range(0, 20)) = 0
        _DecalBlendType2 ("DecalBlendType2", Range(0, 1)) = 0
        _DecalBlendAlpha2 ("DecalBlendAlpha2", Range(0, 1)) = 1
        _DecalAlphaIntensity2 ("DecalAlphaIntensity2", Range(0, 10)) = 1
        _Decal2MaskChannel ("Decal 2 Mask Channel", Int) = 2
        _DecalEnabled3 ("DecalEnabled3", Float) = 0
        _DecalTexture3 ("DecalTexture3", 2D) = "white" {}
        _DecalTexture3UV ("DecalTexture3UV", Int) = 0
        _DecalTexture3Pan ("DecalTexture3Pan", Vector) = (0,0,0,0)
        _DecalPosition3 ("DecalPosition3", Vector) = (0.5,0.5,0,0)
        _DecalRotation3 ("DecalRotation3", Range(0, 360)) = 0
        _DecalRotationSpeed3 ("DecalRotationSpeed3", Float) = 0
        _DecalScale3 ("DecalScale3", Vector) = (1,1,1,0)
        _DecalSideOffset3 ("DecalSideOffset3", Vector) = (0,0,0,0)
        _DecalMirroredUVMode3 ("DecalMirroredUVMode3", Int) = 0
        _DecalSymmetryMode3 ("DecalSymmetryMode3", Int) = 0
        _DecalTiled3 ("DecalTiled3", Float) = 0
        _DecalColor3 ("DecalColor3", Color) = (1,1,1,1)
        _DecalEmissionStrength3 ("DecalEmissionStrength3", Range(0, 20)) = 0
        _DecalBlendType3 ("DecalBlendType3", Range(0, 1)) = 0
        _DecalBlendAlpha3 ("DecalBlendAlpha3", Range(0, 1)) = 1
        _DecalAlphaIntensity3 ("DecalAlphaIntensity3", Range(0, 10)) = 1
        _Decal3MaskChannel ("Decal 3 Mask Channel", Int) = 3
        // Sections official lilToon does not have.
        _EnableDissolve ("Dissolve", Float) = 0
        _GlitterEnable ("Glitter", Float) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
}
