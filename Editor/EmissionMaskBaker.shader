// lilToon 2.3.4 emission equations (MIT, lilxyzw); see ThirdPartyNotices/lilToon-LICENSE.txt.
// Output pixels are in main-texture UV because MToon samples the emissive
// image with the main transform; the lilToon images are sampled in UV0.
Shader "Hidden/VRVlog/EmissionMaskBaker"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionMap_ScrollRotate ("Emission angle", Vector) = (0,0,0,0)
        _EmissionBlendMask ("Mask", 2D) = "white" {}
        _EmissionBlendMask_ScrollRotate ("Mask angle", Vector) = (0,0,0,0)
        _EmissionMainStrength ("Main strength", Float) = 0
        _VrvMainST ("Source main UV transform", Vector) = (1,1,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_TEX2D(_MainTex);
            UNITY_DECLARE_TEX2D(_EmissionMap);
            UNITY_DECLARE_TEX2D_NOSAMPLER(_EmissionBlendMask);
            float4 _Color, _EmissionMap_ST, _EmissionMap_ScrollRotate, _EmissionBlendMask_ST, _EmissionBlendMask_ScrollRotate, _VrvMainST;
            float _EmissionMainStrength;
            // lilCalcUVWithoutAnimation
            float2 StaticUV(float2 uv, float4 st, float angle)
            {
                float si, co;
                sincos(angle, si, co);
                float2 outuv = uv * st.xy + st.zw - 0.5;
                return float2(outuv.x * co - outuv.y * si, outuv.x * si + outuv.y * co) + 0.5;
            }
            float4 frag(v2f_img input) : SV_Target
            {
                float2 uv0 = (input.uv - _VrvMainST.zw) / _VrvMainST.xy;
                float4 emission = UNITY_SAMPLE_TEX2D(_EmissionMap, StaticUV(uv0, _EmissionMap_ST, _EmissionMap_ScrollRotate.z));
                emission *= UNITY_SAMPLE_TEX2D_SAMPLER(_EmissionBlendMask, _MainTex, StaticUV(uv0, _EmissionBlendMask_ST, _EmissionBlendMask_ScrollRotate.z));
                float3 albedo = UNITY_SAMPLE_TEX2D(_MainTex, input.uv).rgb * _Color.rgb;
                emission.rgb = lerp(emission.rgb, emission.rgb * albedo, _EmissionMainStrength);
                // MToon has no emissive alpha; lilToon scales the blend by it.
                return float4(emission.rgb * emission.a, 1);
            }
            ENDCG
        }
    }
}
