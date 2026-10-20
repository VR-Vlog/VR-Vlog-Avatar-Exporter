// Declares the property names a typical third-party toon shader uses, so the
// unsupported-shader substitution can be tested without shipping one.
Shader "Hidden/VRVlogTests/ToonPropertyProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _1st_ShadeMap ("Shade Map", 2D) = "white" {}
        _1st_ShadeColor ("Shade Color", Color) = (1,1,1,1)
        _NormalMap ("Normal", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Emissive_Tex ("Emissive", 2D) = "white" {}
        [HDR] _Emissive_Color ("Emissive Color", Color) = (0,0,0,1)
        _ClippingMode ("Clipping Mode", Float) = 0
        _Clipping_Level ("Clipping Level", Range(0, 1)) = 0
        _CullMode ("Cull", Float) = 2
        _BaseColor_Step ("Step", Range(0, 1)) = 0.5
        _BaseShade_Feather ("Feather", Range(0.0001, 1)) = 0.0001
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
            float4 _BaseColor;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _BaseColor; }
            ENDCG
        }
    }
}
