// A shader whose main texture and color are marked with [MainTexture] and
// [MainColor] under names outside the common list, with its rendering mode
// held only in blend factors and the queue, as shaders built on a shader
// framework do. A _MainTex of another use sits beside it.
Shader "Hidden/VRVlogTests/MarkedMainProbe"
{
    Properties
    {
        [MainTexture] _BaseTexture ("Texture", 2D) = "white" {}
        [MainColor] _Tint ("Tint", Color) = (1,1,1,1)
        _MainTex ("Other", 2D) = "white" {}
        _NormalMap ("Normal", 2D) = "bump" {}
        _NormalScale ("Normal Scale", Float) = 1
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _Cull ("Cull", Int) = 2
        _SrcBlend ("Src", Int) = 1
        _DstBlend ("Dst", Int) = 0
        _ZWrite ("ZWrite", Int) = 1
    }
    SubShader
    {
        Pass
        {
            Cull [_Cull]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _BaseTexture;
            float _Cutoff;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { float4 c = tex2D(_BaseTexture, i.uv); clip(c.a - _Cutoff); return c; }
            ENDCG
        }
    }
}
