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
        _VrvDecodeMain ("Decode main", Float) = 0
        _VrvDecodeMask ("Decode mask", Float) = 0
        _VrvEncode ("Encode", Float) = 0
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        CGINCLUDE
        #include "UnityCG.cginc"
        UNITY_DECLARE_TEX2D(_MainTex);
        UNITY_DECLARE_TEX2D(_MatCapBlendMask);
        float4 _Color, _MatCapBlendMask_ST, _VrvMeanProduct;
        float _MatCapMainStrength, _VrvScreen, _VrvMeanAlpha, _VrvDecodeMain, _VrvDecodeMask, _VrvEncode;
        // A Gamma project samples sRGB images and writes the sRGB target
        // without conversion, so the shader does it to work in linear values.
        float3 Decode(float3 rgb, float decode)
        {
            return decode > .5 ? float3(GammaToLinearSpaceExact(rgb.r), GammaToLinearSpaceExact(rgb.g), GammaToLinearSpaceExact(rgb.b)) : rgb;
        }
        float3 Encode(float3 rgb)
        {
            return _VrvEncode > .5 ? float3(LinearToGammaSpaceExact(rgb.r), LinearToGammaSpaceExact(rgb.g), LinearToGammaSpaceExact(rgb.b)) : rgb;
        }
        float4 Main(float2 uv)
        {
            float4 color = UNITY_SAMPLE_TEX2D(_MainTex, uv);
            return float4(Decode(color.rgb, _VrvDecodeMain), color.a);
        }
        float3 Mask(float2 uv) { return Decode(UNITY_SAMPLE_TEX2D(_MatCapBlendMask, uv * _MatCapBlendMask_ST.xy + _MatCapBlendMask_ST.zw).rgb, _VrvDecodeMask); }
        ENDCG
        // 0: weight of the MatCap that MToon adds, for its rim multiply image.
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            float4 frag(v2f_img input) : SV_Target
            {
                float3 albedo = Main(input.uv).rgb * _Color.rgb;
                // matCapColor.rgb = lerp(rgb, rgb * albedo, _MatCapMainStrength)
                float3 weight = Mask(input.uv) * lerp(1, albedo, _MatCapMainStrength);
                // Screen max(dst + src - dst * src, dst) adds src * (1 - dst);
                // the albedo stands in for dst.
                if (_VrvScreen > 0.5) weight *= saturate(1 - albedo);
                return float4(Encode(weight), 1);
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
                float4 color = Main(input.uv);
                float3 albedo = color.rgb * _Color.rgb;
                float3 darkening = _VrvMeanAlpha - _VrvMeanProduct.rgb * lerp(1, albedo, _MatCapMainStrength);
                color.rgb *= saturate(1 - Mask(input.uv) * darkening);
                return float4(Encode(color.rgb), color.a);
            }
            ENDCG
        }
    }
}
