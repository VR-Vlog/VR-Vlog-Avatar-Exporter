// A stand-in with the property names of NonToon (main image marked as the
// main texture under _BaseTexture, render mode in _RenderingMode, outline
// width and color). The switches are Integer properties as in NonToon.
// Tests name the shader "NonToon" when they call Create.
Shader "Hidden/VRVlogTests/NonToonProbe"
{
    Properties
    {
        _RenderingMode ("Rendering Mode", Integer) = 0
        [MainTexture] _BaseTexture ("Texture", 2D) = "white" {}
        _NormalScale ("Normal Scale", Float) = 1
        _NormalMap ("Normal", 2D) = "bump" {}
        _NormalMapWithRoughness ("Normal With Roughness", Integer) = 0
        _Cutoff ("Cutoff", Range(-0.001, 1.001)) = 0.5
        _NTShadowBias ("Shadow Bias", Float) = 0.1
        _OutlineColor ("Outline Color", Color) = (0.6,0.45,0.55,1)
        _OutlineWidth ("Outline Width", Float) = 0.1
        _OutlineZOffset ("Outline Z Offset", Float) = 0
        _OutlineFromVertexColor ("From Vertex Color", Integer) = 0
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
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_BaseTexture, i.uv); }
            ENDCG
        }
    }
}
