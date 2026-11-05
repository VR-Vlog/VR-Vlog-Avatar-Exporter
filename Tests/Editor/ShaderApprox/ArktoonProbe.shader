// A stand-in with the property names of Arktoon that the approximation reads.
// Tests name the shader when they call Create.
Shader "Hidden/VRVlogTests/ArktoonProbe"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "white" {}
        _Color ("_Color", Color) = (1,1,1,1)
        _Shadowborder ("_Shadowborder", Float) = 0.6
        _ShadowborderBlur ("_ShadowborderBlur", Float) = 0.05
        _ShadowborderBlurMask ("_ShadowborderBlurMask", 2D) = "white" {}
        _ShadowStrength ("_ShadowStrength", Float) = 0.5
        _ShadowStrengthMask ("_ShadowStrengthMask", 2D) = "white" {}
        _ShadowIndirectIntensity ("_ShadowIndirectIntensity", Float) = 0.25
        _ShadowUseStep ("_ShadowUseStep", Float) = 0
        _ShadowPlanBUsePlanB ("_ShadowPlanBUsePlanB", Float) = 0
        _ShadowPlanBDefaultShadowMix ("_ShadowPlanBDefaultShadowMix", Float) = 1
        _ShadowPlanBUseCustomShadowTexture ("_ShadowPlanBUseCustomShadowTexture", Float) = 0
        _ShadowPlanBCustomShadowTexture ("_ShadowPlanBCustomShadowTexture", 2D) = "black" {}
        _ShadowPlanBCustomShadowTextureRGB ("_ShadowPlanBCustomShadowTextureRGB", Color) = (1,1,1,1)
        _ShadowPlanBHueShiftFromBase ("_ShadowPlanBHueShiftFromBase", Float) = 0
        _ShadowPlanBSaturationFromBase ("_ShadowPlanBSaturationFromBase", Float) = 1
        _ShadowPlanBValueFromBase ("_ShadowPlanBValueFromBase", Float) = 1
        _CustomShadow2nd ("_CustomShadow2nd", Float) = 0
        _ShadowPlanB2border ("_ShadowPlanB2border", Float) = 0.55
        _ShadowPlanB2borderBlur ("_ShadowPlanB2borderBlur", Float) = 0.55
        _ShadowPlanB2UseCustomShadowTexture ("_ShadowPlanB2UseCustomShadowTexture", Float) = 0
        _ShadowPlanB2CustomShadowTexture ("_ShadowPlanB2CustomShadowTexture", 2D) = "black" {}
        _ShadowPlanB2CustomShadowTextureRGB ("_ShadowPlanB2CustomShadowTextureRGB", Color) = (1,1,1,1)
        _ShadowPlanB2HueShiftFromBase ("_ShadowPlanB2HueShiftFromBase", Float) = 0
        _ShadowPlanB2SaturationFromBase ("_ShadowPlanB2SaturationFromBase", Float) = 1
        _ShadowPlanB2ValueFromBase ("_ShadowPlanB2ValueFromBase", Float) = 1
        _UseGloss ("_UseGloss", Float) = 0
        _UseReflection ("_UseReflection", Float) = 0
        _UseEmissionParallax ("_UseEmissionParallax", Float) = 0
        _UseOutline ("_UseOutline", Float) = 0
        _OutlineWidth ("_OutlineWidth", Float) = 0.1
        _OutlineColor ("_OutlineColor", Color) = (0,0,0,1)
        _OutlineTexture ("_OutlineTexture", 2D) = "white" {}
        _OutlineShadeMix ("_OutlineShadeMix", Float) = 0
        _OutlineTextureColorRate ("_OutlineTextureColorRate", Float) = 0.05
        _OutlineWidthMask ("_OutlineWidthMask", 2D) = "white" {}
        _OutlineUseColorShift ("_OutlineUseColorShift", Float) = 0
        _MatcapBlendMode ("_MatcapBlendMode", Float) = 3
        _MatcapBlend ("_MatcapBlend", Float) = 1
        _MatcapBlendMask ("_MatcapBlendMask", 2D) = "white" {}
        _MatcapNormalMix ("_MatcapNormalMix", Float) = 1
        _MatcapShadeMix ("_MatcapShadeMix", Float) = 0
        _MatcapTexture ("_MatcapTexture", 2D) = "black" {}
        _MatcapColor ("_MatcapColor", Color) = (1,1,1,1)
        _UseRim ("_UseRim", Float) = 0
        _RimBlend ("_RimBlend", Float) = 1
        _RimBlendMask ("_RimBlendMask", 2D) = "white" {}
        _RimUseBaseTexture ("_RimUseBaseTexture", Float) = 0
        _RimFresnelPower ("_RimFresnelPower", Float) = 1
        _RimUpperSideWidth ("_RimUpperSideWidth", Float) = 0
        _RimColor ("_RimColor", Color) = (1,1,1,1)
        _RimTexture ("_RimTexture", 2D) = "white" {}
        _RimShadeMix ("_RimShadeMix", Float) = 0
        _ShadowCapBlendMode ("_ShadowCapBlendMode", Float) = 3
        _ShadowCapBlend ("_ShadowCapBlend", Float) = 1
        _ShadowCapBlendMask ("_ShadowCapBlendMask", 2D) = "white" {}
        _ShadowCapNormalMix ("_ShadowCapNormalMix", Float) = 1
        _ShadowCapTexture ("_ShadowCapTexture", 2D) = "white" {}
        _UseDoubleSided ("_UseDoubleSided", Float) = 0
        _DoubleSidedFlipBackfaceNormal ("_DoubleSidedFlipBackfaceNormal", Float) = 0
        _DoubleSidedBackfaceLightIntensity ("_DoubleSidedBackfaceLightIntensity", Float) = 0.5
        _VertexColorBlendDiffuse ("_VertexColorBlendDiffuse", Float) = 0
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
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
