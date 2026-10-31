// Declares the properties and keywords of the VRChat SDK's Toon Standard
// (and its outline variant) so the mapping can be tested without the SDK.
Shader "Hidden/VRVlogTests/VRChatToonStandardProbe"
{
    Properties
    {
        _Culling ("Culling", Int) = 2
        _Color ("Main Color", Color) = (1,1,1,1)
        _MainTex ("Main Texture", 2D) = "white" {}
        _VertexColor ("Apply Vertex Color", Float) = 0
        _Ramp ("Shadow Ramp", 2D) = "white" {}
        _ShadowBoost ("Shadow Boost", Range(0,1)) = 0
        _ShadowAlbedo ("Shadow Tint", Range(0,1)) = 0.5
        _MinBrightness ("Min Brightness", Range(0,0.1)) = 0
        _LimitBrightness ("Limit Brightness", Float) = 1
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _MetallicMap ("Metallic Map", 2D) = "white" {}
        _MetallicMapChannel ("Color Channel", Int) = 0
        _MetallicStrength ("Metallic Strength", Range(0,1)) = 0
        _GlossMap ("Gloss Map", 2D) = "white" {}
        _GlossMapChannel ("Color Channel", Int) = 3
        _GlossStrength ("Gloss Strength", Range(0,1)) = 0.5
        _Reflectance ("Reflectance", Range(0,1)) = 0.5
        _SpecularSharpness ("Sharpness", Range(0,1)) = 0
        _Matcap ("Matcap", 2D) = "white" {}
        _MatcapMask ("Matcap Mask", 2D) = "white" {}
        _MatcapMaskChannel ("Color Channel", Int) = 0
        _MatcapType ("Matcap Type", Int) = 0
        _MatcapStrength ("Matcap Strength", Range(0,1)) = 1
        _RimColor ("Color", Color) = (1,1,1,1)
        _RimAlbedoTint ("Albedo Tint", Range(0,1)) = 0
        _RimIntensity ("Intensity", Range(0,1)) = 0.5
        _RimRange ("Range", Range(0,1)) = 0.3
        _RimSharpness ("Softness", Range(0,1)) = 0.1
        _RimEnvironmental ("Environmental", Float) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0)
        _EmissionStrength ("Strength", Range(0,2)) = 1
        _EmissionUV ("UV Map", Int) = 0
        _OcclusionMap ("Occlusion Map", 2D) = "white" {}
        _OcclusionMapChannel ("Color Channel", Int) = 1
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1
        _DetailMode ("Detail Mode", Int) = 0
        _DetailMask ("Detail Mask", 2D) = "white" {}
        _DetailMaskChannel ("Color Channel", Int) = 3
        _DetailAlbedoMap ("Detail Texture", 2D) = "black" {}
        _DetailNormalMap ("Detail Normal Map", 2D) = "bump" {}
        _DetailNormalMapScale ("Detail Normal Scale", Float) = 1
        _DetailUV ("UV Map", Int) = 0
        _HueShift ("Albedo Hue Shift", Range(0,6.283185)) = 0
        _DetailHueShift ("Detail Hue Shift", Range(0,6.283185)) = 0
        _EmissionHueShift ("Emission Hue Shift", Range(0,6.283185)) = 0
        _HueShiftMask ("Hue Shift Mask", 2D) = "white" {}
        _HueShiftMaskChannel ("Color Channel", Int) = 1
        _ColorMask ("Color Mask", 2D) = "black" {}
        _ColorMaskColor1 ("Color 1", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength1 ("Color 1 Emission", Range(0,2)) = 0
        _ColorMaskColor2 ("Color 2", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength2 ("Color 2 Emission", Range(0,2)) = 0
        _ColorMaskColor3 ("Color 3", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength3 ("Color 3 Emission", Range(0,2)) = 0
        _ColorMaskColor4 ("Color 4", Color) = (1,1,1,1)
        _ColorMaskEmissionStrength4 ("Color 4 Emission", Range(0,2)) = 0
        _ColorMaskBlendMode ("Color Mask Blend Mode", Int) = 0
        _OutlineMask ("Thickness", 2D) = "white" {}
        _OutlineMaskChannel ("Color Channel", Int) = 0
        _OutlineThickness ("Thickness Multiplier", Range(0,0.5)) = 0.05
        _OutlineColor ("Color", Color) = (0,0,0,1)
        _OutlineFromAlbedo ("Color From Albedo", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Cull [_Culling]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local_fragment _ USE_SPECULAR
            #pragma shader_feature_local_fragment _ USE_MATCAP
            #pragma shader_feature_local_fragment _ USE_DETAIL_MAPS
            #pragma shader_feature_local_fragment _ USE_NORMAL_MAPS
            #pragma shader_feature_local_fragment _ USE_OCCLUSION_MAP
            #pragma dynamic_branch_local_fragment _ USE_RIMLIGHT
            #pragma dynamic_branch_local_fragment _ USE_HUE_SHIFT
            #pragma dynamic_branch_local_fragment _ USE_COLOR_MASK
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert (appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = TRANSFORM_TEX(v.texcoord, _MainTex); return o; }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                #if defined(USE_SPECULAR) || defined(USE_MATCAP) || defined(USE_DETAIL_MAPS) || defined(USE_NORMAL_MAPS) || defined(USE_OCCLUSION_MAP)
                c.rgb *= 0.5;
                #endif
                if (USE_RIMLIGHT) c.r += 0.1;
                if (USE_HUE_SHIFT) c.g += 0.1;
                if (USE_COLOR_MASK) c.b += 0.1;
                return c;
            }
            ENDCG
        }
    }
}
