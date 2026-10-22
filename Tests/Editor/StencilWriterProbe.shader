// A third-party stencil writer: the SubShader masks every color channel
// and turns depth writes off, so its only pass writes the stencil buffer
// for another material to read.
Shader "Hidden/VRVlogTests/StencilWriterProbe"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _StencilRef ("Stencil Ref", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        ColorMask 0
        ZWrite Off
        Stencil
        {
            Ref [_StencilRef]
            Comp Always
            Pass Replace
        }
        Pass
        {
            Tags { "LightMode"="ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return 1; }
            ENDCG
        }
    }
}
