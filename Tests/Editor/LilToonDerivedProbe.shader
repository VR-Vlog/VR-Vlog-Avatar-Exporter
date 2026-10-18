// A stand-in for a lilToon build outside the official set: it shares lilToon
// property names and adds one of its own.
Shader "Hidden/VRVlogTests/lilToonDerivedProbe"
{
    Properties
    {
        _lilToonVersion ("Version", Int) = 44
        _TransparentMode ("Rendering Mode", Int) = 0
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Cutoff", Range(-0.001, 1.001)) = 0.5
        _UseShadow ("Use Shadow", Int) = 0
        _ShadowColor ("Shadow Color", Color) = (0.82,0.76,0.85,1)
        _ShadowBorder ("Shadow Border", Range(0, 1)) = 0.5
        _Cull ("Cull", Int) = 2
        _DerivedOnly ("Derived Only", Float) = 0
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
            float4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
}
