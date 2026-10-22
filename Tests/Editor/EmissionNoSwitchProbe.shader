// A third-party shader that always adds its emission, with a white default
// color, an image named _Emission and an option to tint by the base color.
Shader "Hidden/VRVlogTests/EmissionNoSwitchProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Emission ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionBaseColorAsMap ("Base Color As Map", Float) = 0
    }
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _EmissionColor;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return float4(_EmissionColor.rgb, 1); }
            ENDCG
        }
    }
}
