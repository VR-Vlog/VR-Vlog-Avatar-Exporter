// A stand-in with the property names of Unity-chan Toon Shader 2 and 3 that
// the approximation reads. Tests name the shader when they call Create.
Shader "Hidden/VRVlogTests/UtsProbe"
{
    Properties
    {
        _utsTechnique ("Technique", Int) = 0
        _MainTex ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Is_LightColor_Base ("Light Color Base", Float) = 1
        _Is_NormalMapToBase ("Normal Map To Base", Float) = 0
        _Set_SystemShadowsToBase ("System Shadows To Base", Float) = 1
        _Clipping_Level ("Clipping Level", Range(0, 1)) = 0
        _ClippingMask ("Clipping Mask", 2D) = "white" {}
        _Use_BaseAs1st ("Base As 1st", Float) = 0
        _1st_ShadeMap ("1st Shade Map", 2D) = "white" {}
        _1st_ShadeColor ("1st Shade Color", Color) = (1,1,1,1)
        _Use_1stAs2nd ("1st As 2nd", Float) = 0
        _2nd_ShadeMap ("2nd Shade Map", 2D) = "white" {}
        _2nd_ShadeColor ("2nd Shade Color", Color) = (1,1,1,1)
        _BaseColor_Step ("Base Step", Range(0, 1)) = 0.5
        _BaseShade_Feather ("Base Feather", Range(0.0001, 1)) = 0.0001
        _ShadeColor_Step ("Shade Step", Range(0, 1)) = 0
        _1st2nd_Shades_Feather ("Shade Feather", Range(0.0001, 1)) = 0.0001
        _1st_ShadeColor_Step ("1st Step", Range(0, 1)) = 0.5
        _1st_ShadeColor_Feather ("1st Feather", Range(0.0001, 1)) = 0.0001
        _2nd_ShadeColor_Step ("2nd Step", Range(0, 1)) = 0
        _2nd_ShadeColor_Feather ("2nd Feather", Range(0.0001, 1)) = 0.0001
        _Set_1st_ShadePosition ("1st Position", 2D) = "white" {}
        _Set_2nd_ShadePosition ("2nd Position", 2D) = "white" {}
        _ShadingGradeMap ("Grade Map", 2D) = "white" {}
        _Tweak_ShadingGradeMapLevel ("Grade Level", Range(-0.5, 0.5)) = 0
        _RimLight ("Rim", Float) = 0
        _RimLightColor ("Rim Color", Color) = (1,1,1,1)
        _Is_LightColor_RimLight ("Rim Light Color", Float) = 1
        _Is_NormalMapToRimLight ("Rim Normal", Float) = 0
        _RimLight_Power ("Rim Power", Range(0, 1)) = 0.1
        _RimLight_InsideMask ("Rim Inside", Range(0.0001, 1)) = 0.0001
        _RimLight_FeatherOff ("Rim Feather Off", Float) = 0
        _LightDirection_MaskOn ("Rim Direction", Float) = 0
        _Tweak_LightDirection_MaskLevel ("Rim Direction Level", Float) = 0
        _Add_Antipodean_RimLight ("Antipodean", Float) = 0
        _Set_RimLightMask ("Rim Mask", 2D) = "white" {}
        _Tweak_RimLightMaskLevel ("Rim Mask Level", Range(-1, 1)) = 0
        _MatCap ("MatCap", Float) = 0
        _MatCap_Sampler ("MatCap Image", 2D) = "black" {}
        _BlurLevelMatcap ("MatCap Blur", Range(0, 10)) = 0
        _MatCapColor ("MatCap Color", Color) = (1,1,1,1)
        _Is_LightColor_MatCap ("MatCap Light Color", Float) = 1
        _Is_BlendAddToMatCap ("MatCap Add", Float) = 1
        _Is_NormalMapForMatCap ("MatCap Normal", Float) = 0
        _Set_MatcapMask ("MatCap Mask", 2D) = "white" {}
        _Inverse_MatcapMask ("MatCap Mask Invert", Float) = 0
        _Outline_Width ("Outline Width", Float) = 0
        _Outline_Color ("Outline Color", Color) = (0.5,0.5,0.5,1)
        _Is_BlendBaseColor ("Outline Base", Float) = 0
        _Is_LightColor_Outline ("Outline Light Color", Float) = 1
        _Is_OutlineTex ("Outline Image On", Float) = 0
        _OutlineTex ("Outline Image", 2D) = "white" {}
        _Outline_Sampler ("Outline Mask", 2D) = "white" {}
        _HighColor ("High Color", Color) = (0,0,0,1)
        _HighColor_Power ("High Color Power", Float) = 0
        _Is_SpecularToHighColor ("Specular To High Color", Float) = 0
        _AngelRing ("Angel Ring", Float) = 0
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
