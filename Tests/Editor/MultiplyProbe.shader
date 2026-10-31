// A particle multiply shader written the legacy way: the Blend and ZWrite
// commands sit in the Category around the SubShader.
Shader "Hidden/VRVlogTests/MultiplyProbe"
{
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    Category
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Blend Zero SrcColor
        Cull Off Lighting Off ZWrite Off
        SubShader
        {
            Pass
            {
                SetTexture [_MainTex] { combine texture * primary }
                SetTexture [_MainTex] { constantColor (1,1,1,1) combine previous lerp (previous) constant }
            }
        }
    }
}
