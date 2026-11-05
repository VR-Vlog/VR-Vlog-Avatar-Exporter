// A stand-in with the property names of Xiexe XSToon 3 that the approximation
// reads. Tests name the shader when they call Create.
Shader "Hidden/VRVlogTests/XsToonProbe"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "white" {}
        _Color ("_Color", Color) = (1,1,1,1)
        _Cutoff ("_Cutoff", Float) = 0.5
        _BlendMode ("_BlendMode", Float) = 0
        _Culling ("_Culling", Float) = 2
        _Hue ("_Hue", Float) = 0
        _Saturation ("_Saturation", Float) = 1
        _Value ("_Value", Float) = 1
        _HSVMask ("_HSVMask", 2D) = "white" {}
        _ShadowType ("_ShadowType", Float) = 0
        _Ramp ("_Ramp", 2D) = "white" {}
        _RampSelectionMask ("_RampSelectionMask", 2D) = "black" {}
        _ShadeMap ("_ShadeMap", 2D) = "white" {}
        _ShadowSharpness ("_ShadowSharpness", Float) = 0.9
        _ShadowRange ("_ShadowRange", Float) = 0.5
        _ShadowColor ("_ShadowColor", Color) = (1,1,1,1)
        _ShadowRim ("_ShadowRim", Color) = (1,1,1,1)
        _ShadowRimThreshold ("_ShadowRimThreshold", Float) = 0.1
        _RimIntensity ("_RimIntensity", Float) = 0
        _RimColor ("_RimColor", Color) = (1,1,1,1)
        _RimRange ("_RimRange", Float) = 0.7
        _RimSharpness ("_RimSharpness", Float) = 0.1
        _RimThreshold ("_RimThreshold", Float) = 0.1
        _RimAlbedoTint ("_RimAlbedoTint", Float) = 0
        _RimAttenEffect ("_RimAttenEffect", Float) = 1
        _RimCubemapTint ("_RimCubemapTint", Float) = 0
        _RimMask ("_RimMask", 2D) = "white" {}
        _ReflectionMode ("_ReflectionMode", Float) = 3
        _Matcap ("_Matcap", 2D) = "black" {}
        _MatcapTint ("_MatcapTint", Color) = (1,1,1,1)
        _MatcapTintToDiffuse ("_MatcapTintToDiffuse", Float) = 0
        _Reflectivity ("_Reflectivity", Float) = 0.5
        _ReflectivityMask ("_ReflectivityMask", 2D) = "white" {}
        _ReflectionBlendMode ("_ReflectionBlendMode", Float) = 0
        _OutlineWidth ("_OutlineWidth", Float) = 1
        _OutlineColor ("_OutlineColor", Color) = (0,0,0,1)
        _OutlineAlbedoTint ("_OutlineAlbedoTint", Float) = 0
        _OutlineLighting ("_OutlineLighting", Float) = 0
        _OutlineMask ("_OutlineMask", 2D) = "white" {}
        _OutlineNormalMode ("_OutlineNormalMode", Float) = 0
        _SpecularIntensity ("_SpecularIntensity", Float) = 0
        _SSColor ("_SSColor", Color) = (0,0,0,0)
        _HalftoneType ("_HalftoneType", Float) = 3
        _UseClipsForDissolve ("_UseClipsForDissolve", Float) = 0
        _EmissionToDiffuse ("_EmissionToDiffuse", Float) = 0
        _EmissionAudioLinkChannel ("_EmissionAudioLinkChannel", Float) = 0
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
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv); }
            ENDCG
        }
    }
}
