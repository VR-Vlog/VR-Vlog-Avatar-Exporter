// A third-party shader whose emission is switched by the _ES_ENABLE keyword
// alone, with no switch property, and a white default color.
Shader "Hidden/VRVlogTests/EmissionKeywordProbe"
{
    Properties
    {
        _EmissionMap ("Emission Map", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _ES_ENABLE
            #include "UnityCG.cginc"
            float4 _EmissionColor;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target
            {
                #if _ES_ENABLE
                return float4(_EmissionColor.rgb, 1);
                #else
                return 0;
                #endif
            }
            ENDCG
        }
    }
}
