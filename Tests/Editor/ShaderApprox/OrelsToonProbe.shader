// A stand-in with the property names and keywords of orels Toon (the first
// version and v2). Tests name the shader "orels1/Toon/..." when they call
// Create.
Shader "Hidden/VRVlogTests/OrelsToonProbe"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,1)
        _BackfaceColor ("Backface Color", Color) = (1,1,1,1)
        _BackfaceAlbedoTint ("Backface Albedo Tint", Range(0,1)) = 1
        _TintByVertexColor ("Tint By Vertex Color", Int) = 0
        _MainTex ("Albedo", 2D) = "white" {}
        _MainTexUVSet ("UV Set", Int) = 0
        _AlphaTex ("Alpha", 2D) = "white" {}
        _AlphaScale ("Alpha Scale", Range(-4,4)) = 1
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Hue ("Hue", Range(0,1)) = 0
        _Saturation ("Saturation", Range(-1,1)) = 0
        _Value ("Value", Range(-1,1)) = 0
        _CullMode ("Culling Mode", Int) = 2
        _MonochromeLighting ("Monochrome Lighting", Range(0,1)) = 0
        _Ramp ("Ramp", 2D) = "grayscaleRamp" {}
        _ShadowSharpness ("Shadow Sharpness", Range(0,1)) = 0
        _OffsetRampByOcclusion ("Offset Ramp By Occlusion", Int) = 1
        _OcclusionMode ("Occlusion Mode", Int) = 0
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpMapTilingMode ("Normal Map Tiling Mode", Int) = 0
        _BumpMapTiling ("Normal Map Tiling", Float) = 1
        _BumpScale ("Normal Map Scale", Range(-2,2)) = 1
        _FlipBumpY ("Flip Y", Int) = 0
        _DetailNormalMap ("Detail Normal", 2D) = "bump" {}
        _OutlineTex ("Outline Color", 2D) = "white" {}
        [HDR] _OutlineColor ("Outline Color", Color) = (0.5,0.5,0.5,1)
        _OutlineLightingMode ("Outline Lighting Mode", Int) = 0
        _OutlineAlbedoTint ("Outline Albedo Tint", Range(0,1)) = 0
        _OutlineMask ("Outline Mask", 2D) = "white" {}
        _OutlineWidth ("Outline Width", Range(0,5)) = 1
        _OutlineWidthAdjustByVertexColor ("Adjust By Vertex Color", Int) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionTintToDiffuse ("Tint To Diffuse", Range(0,1)) = 0
        _EmissionScaleWithLight ("Scale With Light", Int) = 0
        _EmissionMapChannel ("Emission Map Channel", Int) = 0
        _EmissionMaskByUVTile ("Mask By UV Tile", Int) = 0
        _EmissionPower ("Emission Power", Float) = 1
        _EmissionHue ("Emission Hue", Range(0,1)) = 0
        _EmissionSaturation ("Emission Saturation", Range(-1,1)) = 0
        _EmissionValue ("Emission Value", Range(-1,1)) = 0
        _EmissionRimMasking ("Rim Masking", Range(-1,1)) = 0
        _RimTint ("Rim Tint", Color) = (1,1,1,1)
        _RimIntensity ("Rim Intensity", Float) = 0
        _RimAlbedoTint ("Rim Albedo Tint", Range(0,1)) = 0
        _RimEnvironmentTint ("Rim Environment Tint", Range(0,1)) = 0
        _RimAttenuation ("Rim Attenuation", Range(0,1)) = 1
        _RimRange ("Rim Range", Range(0,1)) = 0.7
        _RimSpread ("Rim Spread", Range(0,1)) = 0.3
        _RimThreshold ("Rim Threshold", Range(0,1)) = 0.1
        _RimSharpness ("Rim Sharpness", Range(0,1)) = 0.1
        _Matcap0Tex ("Matcap 1", 2D) = "black" {}
        _Matcap0Tint ("Matcap 1 Tint", Color) = (1,1,1,1)
        _Matcap0BlendMode ("Matcap 1 Blend Mode", Int) = 0
        _Matcap0Strength ("Matcap 1 Strength", Range(0,1)) = 1
        _Matcap0TintToDiffuse ("Matcap 1 Tint To Diffuse", Range(0,1)) = 0
        _Matcap0MaskChannel ("Matcap 1 Mask Channel", Int) = 0
        _Matcap0MaskStrength ("Matcap 1 Mask Strength", Range(0,1)) = 1
        _Matcap1Tex ("Matcap 2", 2D) = "black" {}
        _Matcap1Tint ("Matcap 2 Tint", Color) = (1,1,1,1)
        _Matcap1BlendMode ("Matcap 2 Blend Mode", Int) = 0
        _Matcap1Strength ("Matcap 2 Strength", Range(0,1)) = 1
        _Matcap1TintToDiffuse ("Matcap 2 Tint To Diffuse", Range(0,1)) = 0
        _Matcap1MaskChannel ("Matcap 2 Mask Channel", Int) = 0
        _Matcap1MaskStrength ("Matcap 2 Mask Strength", Range(0,1)) = 1
        _MatcapsMask ("Matcaps Mask", 2D) = "white" {}
        _MatcapsMaskUVSet ("Matcaps Mask UV Set", Int) = 0
        _Matcap ("Matcap", 2D) = "black" {}
        _MatcapTintToDiffuse ("Matcap Tint To Diffuse", Range(0,1)) = 0
        _ReflectionBlendMode ("Reflection Blend Mode", Int) = 0
        _MetallicGlossMap ("Metal Smoothness", 2D) = "white" {}
        _MetallicGlossMapTiling ("Metal Smoothness Tiling", Float) = 1
        _MetallicGlossMapTilingMode ("Metal Smoothness Tiling Mode", Int) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _RoughnessMode ("Roughness Mode", Int) = 0
        _Metallic ("Metal", Range(0,1)) = 0
        _MetallicRemap ("Metal Remap", Vector) = (0,1,0,1)
        _SmoothnessRemap ("Smoothness Remap", Vector) = (0,1,0,1)
        _ReflectionAnisotropy ("Anisotropy", Range(-1,1)) = 0
        _ReflectivityMask ("Reflectivity Mask", 2D) = "white" {}
        _ReflectivityMaskTiling ("Reflectivity Mask Tiling", Float) = 1
        _ReflectivityMaskTilingMode ("Reflectivity Mask Tiling Mode", Int) = 0
        _ReflectivityLevel ("Reflectivity", Range(0,1)) = 0.5
        _ReflectionMask ("Reflection Mask", 2D) = "white" {}
        _ReflectionMaskStrength ("Reflection Mask Strength", Range(0,1)) = 1
        _ReflectionStrength ("Reflection Strength", Range(0,1)) = 1
        _SpecularIntensity ("Specular Intensity", Float) = 1
        AL_MODE ("Audio Link Mode", Int) = 0
    }
    SubShader
    {
        Pass
        {
            Cull [_CullMode]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local SEPARATE_ALPHA
            #pragma shader_feature_local OUTLINE_ENABLED
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local RIMLIGHT_ON
            #pragma shader_feature_local RIMSHADOW_ON
            #pragma shader_feature_local MATCAP_0
            #pragma shader_feature_local MATCAP_1
            #pragma shader_feature_local MATCAP_2
            #pragma shader_feature_local MATCAP_3
            #pragma shader_feature_local REFLECTION_MODE_OFF REFLECTION_MODE_PBR REFLECTION_MODE_BAKED_CUBEMAP REFLECTION_MODE_MATCAP
            #pragma shader_feature_local REFLECTIONS_ON
            #pragma shader_feature_local SPECULAR_ON
            #pragma shader_feature_local UV_DISCARD
            #pragma shader_feature_local INTEGRATE_LTCGI
            #pragma shader_feature_local DETAIL_NORMALS_0
            #pragma shader_feature_local DECAL_0
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
