// A stand-in with the property names and keywords of Mochie Standard. Tests
// name the shader "Mochie/Standard" when they call Create.
Shader "Hidden/VRVlogTests/MochieStandardProbe"
{
    Properties
    {
        _BlendMode ("Blending Mode", Int) = 0
        _AlphaSource ("Alpha Source", Int) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _SmoothnessToggle ("Use Smoothness", Int) = 0
        _PrimaryWorkflow ("Primary Workflow", Int) = 0
        _PrimarySampleMode ("Primary Sampling Mode", Int) = 0
        _MainTex ("Base Color", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0,1)) = 1
        _SampleMetallic ("Sample Metallic Map", Int) = 0
        _MetallicMap ("Metallic Map", 2D) = "white" {}
        _MetallicStrength ("Metallic Strength", Range(0,1)) = 0
        _SampleRoughness ("Sample Roughness Map", Int) = 0
        _RoughnessMap ("Roughness Map", 2D) = "white" {}
        _RoughnessStrength ("Roughness Strength", Range(0,1)) = 1
        _SampleOcclusion ("Sample Occlusion Map", Int) = 0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _PackedMap ("Packed Map", 2D) = "white" {}
        _SmoothnessSource ("Smoothness Source", Int) = 0
        _PackedRoughnessStrength ("Packed Roughness Strength", Range(0,1)) = 1
        _PackedMetallicStrength ("Packed Metallic Strength", Range(0,1)) = 1
        _PackedOcclusionStrength ("Packed Occlusion Strength", Range(0,1)) = 1
        _RoughnessChannel ("Roughness Channel", Int) = 1
        _MetallicChannel ("Metallic Channel", Int) = 2
        _OcclusionChannel ("Occlusion Channel", Int) = 0
        _UVMainSet ("UV Set for primary textures", Int) = 0
        _UVMainScroll ("Scroll Speed", Vector) = (0,0,0,0)
        _UVMainRotation ("Rotation", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionStrength ("Emission Strength", Float) = 1
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0)
        _EmissionPulseWave ("Pulse", Int) = 0
        _EmissionMask ("Mask", 2D) = "white" {}
        _EmissionMaskChannel ("Emission Mask Channel", Int) = 0
        _AlphaMask ("Alpha Mask", 2D) = "white" {}
        _AlphaMaskChannel ("Alpha Mask Channel", Int) = 0
        _UVAlphaMaskSet ("UV Set for Alpha mask", Int) = 0
        _AlphaMaskOpacity ("Alpha Mask Opacity", Range(0,1)) = 1
        _ReflectionsToggle ("Reflections", Int) = 1
        _SpecularHighlightsToggle ("Specular Highlights", Int) = 1
        _ReflectionStrength ("Reflection Strength", Float) = 1
        _SpecularHighlightStrength ("Specular Strength", Float) = 1
        _WindToggle ("Wind Toggle", Int) = 0
        _Subsurface ("Subsurface Scattering", Int) = 0
        _Filtering ("Filtering", Int) = 0
        _HueMode ("Hue Mode", Int) = 0
        _MonoTint ("Mono Tint", Int) = 0
        _Hue ("Hue", Range(0,1)) = 0
        _Contrast ("Contrast", Float) = 1
        _Saturation ("Saturation", Float) = 1
        _Brightness ("Brightness", Float) = 1
        _HueEmiss ("Hue", Range(0,1)) = 0
        _ContrastEmiss ("Contrast", Float) = 1
        _SaturationEmiss ("Saturation", Float) = 1
        _BrightnessEmiss ("Brightness", Float) = 1
        _VertexBaseColor ("Vertex Base Color", Int) = 0
        _Culling ("Cull", Int) = 2
        _SrcBlend ("__src", Float) = 1
        _DstBlend ("__dst", Float) = 0
        _ZWrite ("__zw", Float) = 1
    }
    SubShader
    {
        // Mochie Standard grabs the screen before it draws.
        GrabPass { Tags { "LightMode" = "GrabPass" } "_MochieGrab" }
        Pass
        {
            Cull [_Culling]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local _ _STOCHASTIC_ON _TRIPLANAR_ON _SUPERSAMPLING_ON
            #pragma shader_feature_local _REFLECTIONS_ON
            #pragma shader_feature_local _SPECULAR_HIGHLIGHTS_ON
            #pragma shader_feature_local _ _WORKFLOW_PACKED_ON _WORKFLOW_SPECULAR_ON
            #pragma shader_feature_local _EMISSION_ON
            #pragma shader_feature_local _PARALLAX_ON
            #pragma shader_feature_local _NORMALMAP_ON
            #pragma shader_feature_local _DETAIL_MAINTEX_ON
            #pragma shader_feature_local _AUDIOLINK_ON
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
