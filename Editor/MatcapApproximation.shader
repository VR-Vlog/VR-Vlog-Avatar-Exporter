// lilToon 2.3.4 MatCap blend (lilGetMatCap, lilBlendColor; MIT, lilxyzw); see ThirdPartyNotices/lilToon-LICENSE.txt.
// Output pixels are in the main UV (uvMain): MToon samples its rim multiply
// and base images with the main transform, and lilToon samples the MatCap
// mask with uvMain and the mask's own Tiling/Offset.
Shader "Hidden/VRVlog/MatcapApproximation"
{
    Properties
    {
        _MainTex ("Base", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _MatCapBlendMask ("Mask", 2D) = "white" {}
        _MatCapMainStrength ("Main strength", Float) = 0
        _VrvScreen ("Screen", Float) = 0
        _VrvMeanAlpha ("Mean alpha", Float) = 0
        _VrvMeanProduct ("Mean product", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_TEX2D(_MainTex);
        UNITY_DECLARE_TEX2D(_MatCapBlendMask);
        float4 _Color, _MatCapBlendMask_ST, _VrvMeanProduct;
        float _MatCapMainStrength, _VrvScreen, _VrvMeanAlpha;
        float3 Mask(float2 uv) { return UNITY_SAMPLE_TEX2D(_MatCapBlendMask, uv * _MatCapBlendMask_ST.xy + _MatCapBlendMask_ST.zw).rgb; }
        ENDCG
        // 0: weight of the MatCap that MToon adds, for its rim multiply image.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img input) : SV_Target
            {
                float3 albedo = UNITY_SAMPLE_TEX2D(_MainTex, input.uv).rgb * _Color.rgb;
                // matCapColor.rgb = lerp(rgb, rgb * albedo, _MatCapMainStrength)
                float3 weight = Mask(input.uv) * lerp(1, albedo, _MatCapMainStrength);
                // Screen max(dst + src - dst * src, dst) adds src * (1 - dst);
                // the albedo stands in for dst.
                if (_VrvScreen > 0.5) weight *= saturate(1 - albedo);
                return float4(weight, 1);
            }
            ENDCG
        }
        // 1: main image darkened by a normal or multiply MatCap, which MToon
        // cannot draw. lilToon blends lerp(dst, out, blend * a * mask); seen
        // from every direction this averages to dst * (1 - mask * darkening).
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img input) : SV_Target
            {
                float4 color = UNITY_SAMPLE_TEX2D(_MainTex, input.uv);
                float3 albedo = color.rgb * _Color.rgb;
                float3 darkening = _VrvMeanAlpha - _VrvMeanProduct.rgb * lerp(1, albedo, _MatCapMainStrength);
                color.rgb *= saturate(1 - Mask(input.uv) * darkening);
                return color;
            }
            ENDCG
        }
    }
}
