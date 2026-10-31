// Declares the properties and keywords of the VRChat SDK's mobile shaders
// (Standard Lite, Blinn-Phong specular, matcap, video) so their rows can be
// tested without the SDK.
Shader "Hidden/VRVlogTests/VRChatMobileProbe"
{
    Properties
    {
        _MainTex ("Albedo(RGB)", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        [NoScaleOffset] _MetallicGlossMap ("Metallic(R) Smoothness(A) Map", 2D) = "white" {}
        [Gamma] _Metallic ("Metallic", Range(0,1)) = 1
        _Glossiness ("Smoothness", Range(0,1)) = 1
        _BumpScale ("Scale", Float) = 1
        [NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        [NoScaleOffset] _OcclusionMap ("Occlusion(G)", 2D) = "white" {}
        _OcclusionStrength ("Strength", Range(0,1)) = 1
        [NoScaleOffset] _EmissionMap ("Emission(RGB)", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0)
        _UVSec ("UV Set for secondary textures", Float) = 0
        [NoScaleOffset] _DetailMask ("Detail Mask(A)", 2D) = "white" {}
        _DetailAlbedoMap ("Detail Albedo x2(RGB)", 2D) = "grey" {}
        _DetailNormalMapScale ("Scale", Float) = 1
        [NoScaleOffset] _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        [ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 0
        [ToggleOff] _GlossyReflections ("Glossy Reflections", Float) = 0
        _Shininess ("Shininess", Range(0.03,1)) = 0.078125
        _SpecColor ("Specular Color", Color) = (1,1,1,1)
        _MatCap ("MatCap", 2D) = "white" {}
        _Emission ("Emission Scale", Float) = 1
        [Toggle(APPLY_GAMMA)] _ApplyGamma ("Apply Gamma", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ _DETAIL
            #pragma shader_feature_local _ _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local _ _GLOSSYREFLECTIONS_OFF
            #pragma shader_feature_local _ _EMISSION
            #pragma shader_feature_local _ APPLY_GAMMA
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                #if defined(_DETAIL) || defined(_SPECULARHIGHLIGHTS_OFF) || defined(_GLOSSYREFLECTIONS_OFF) || defined(_EMISSION) || defined(APPLY_GAMMA)
                c.rgb *= 0.5;
                #endif
                return c;
            }
            ENDCG
        }
    }
}
