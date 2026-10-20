// Declares the main-color adjustment property names that third-party toon
// shaders use, so the substitution of those adjustments can be tested
// without shipping one.
Shader "Hidden/VRVlogTests/ToonColorAdjustProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        // Brightness factor with output-stage gamma and brightness fixes.
        _Bright ("Brightness", Range(0, 1)) = 1
        _EnableGammaFix ("Gamma Fix", Int) = 0
        _GammaR ("Gamma R", Range(0, 5)) = 1
        _GammaG ("Gamma G", Range(0, 5)) = 1
        _GammaB ("Gamma B", Range(0, 5)) = 1
        _EnableBlightFix ("Brightness Fix", Int) = 0
        _BlightOutput ("Output Brightness", Range(0, 5)) = 1
        _BlightOffset ("Brightness Offset", Range(-5, 5)) = 0
        // Color-adjust section.
        _MainColorAdjustToggle ("Adjust Colors", Float) = 0
        _MainColorAdjustTexture ("Mask", 2D) = "white" {}
        _Saturation ("Saturation", Range(-1, 10)) = 0
        _MainBrightness ("Brightness", Range(-1, 1)) = 0
        _MainHueShiftToggle ("Hue Shift", Float) = 0
        _MainHueShift ("Hue Shift", Range(0, 1)) = 0
        _MainHueShiftSpeed ("Hue Shift Speed", Float) = 0
        _MainBrightnessGlobalMask ("Brightness Global Mask", Int) = 0
        _MainGradationStrength ("Gradation Strength", Range(0, 1)) = 0
        // Hue rotation in radians.
        _HueShift ("Hue Shift", Range(0, 6.283185)) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ USE_HUE_SHIFT
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
