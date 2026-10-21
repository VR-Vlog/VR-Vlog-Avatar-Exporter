// A third-party shader that scales its emission color by a strength value,
// so the substitution can be checked against what that shader shows.
Shader "Hidden/VRVlogTests/EmissionStrengthProbe"
{
    Properties
    {
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _EmissionStrength ("Emission Strength", Float) = 1
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
            float _EmissionStrength;
            float4 vert(float4 vertex : POSITION) : SV_POSITION { return UnityObjectToClipPos(vertex); }
            float4 frag() : SV_Target { return float4(_EmissionColor.rgb * _EmissionStrength, 1); }
            ENDCG
        }
    }
}
