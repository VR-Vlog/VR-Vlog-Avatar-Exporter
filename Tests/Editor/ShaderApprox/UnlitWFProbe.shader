// A stand-in with the property names of UnlitWF's UnToon shaders that the
// approximation reads. Tests name the shader "UnlitWF/..." when they call Create.
Shader "Hidden/VRVlogTests/UnlitWFProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Cutoff ("Cutoff", Range(0, 1)) = 0.5
        _BumpMap ("Normal", 2D) = "bump" {}
        _NM_Enable ("Normal", Float) = 0
        _AL_Source ("Alpha Source", Float) = 0
        _AL_Power ("Alpha Power", Float) = 1
        _GL_LevelMin ("Level Min", Float) = 0.125
        _GL_LevelMax ("Level Max", Float) = 0.8
        _GL_LevelTweak ("Level Tweak", Float) = 0
        _GL_BlendPower ("Blend Power", Float) = 0.8
        _TS_Enable ("Shade", Float) = 0
        _TS_Steps ("Steps", Float) = 2
        _TS_BaseColor ("Base", Color) = (1,1,1,1)
        _TS_BaseTex ("Base Image", 2D) = "white" {}
        _TS_1stColor ("1st", Color) = (0.81,0.81,0.9,1)
        _TS_1stTex ("1st Image", 2D) = "white" {}
        _TS_2ndColor ("2nd", Color) = (0.68,0.68,0.8,1)
        _TS_2ndTex ("2nd Image", 2D) = "white" {}
        _TS_3rdColor ("3rd", Color) = (0.595,0.595,0.7,1)
        _TS_3rdTex ("3rd Image", 2D) = "white" {}
        _TS_Power ("Power", Float) = 1
        _TS_BlendNormal ("Normal", Float) = 0.1
        _TS_1stBorder ("1st Border", Float) = 0.4
        _TS_2ndBorder ("2nd Border", Float) = 0.2
        _TS_3rdBorder ("3rd Border", Float) = 0.1
        _TS_1stFeather ("1st Feather", Float) = 0.05
        _TS_2ndFeather ("2nd Feather", Float) = 0.05
        _TS_3rdFeather ("3rd Feather", Float) = 0.05
        _TS_MaskType ("Mask Type", Float) = 0
        _TS_MaskTex ("Anti Shadow", 2D) = "black" {}
        _TS_InvMaskVal ("Invert", Float) = 0
        _ES_Enable ("Emission", Float) = 0
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _ES_BlendType ("Emission Blend", Float) = 0
        _ES_TintBaseCol ("Emission Tint", Float) = 0
        _ES_ScrollEnable ("Emission Scroll", Float) = 0
        _TR_Enable ("Rim", Float) = 0
        _TR_Color ("Rim Color", Color) = (0.8,0.8,0.8,1)
        _TR_BlendType ("Rim Blend", Float) = 0
        _TR_Width ("Rim Width", Float) = 0.1
        _TR_Feather ("Rim Feather", Float) = 0.05
        _TR_Exponent ("Rim Exponent", Float) = 1
        _TR_WidthTop ("Rim Top", Float) = 0.5
        _TR_WidthSide ("Rim Side", Float) = 1
        _TR_WidthBottom ("Rim Bottom", Float) = 1
        _TR_DisableBackLit ("Rim Back Lit Off", Float) = 0
        _TR_MaskTex ("Rim Mask", 2D) = "white" {}
        _TR_InvMaskVal ("Rim Invert", Float) = 0
        _HL_Enable ("Matcap", Float) = 0
        _HL_CapType ("Matcap Type", Float) = 0
        _HL_MatcapTex ("Matcap Image", 2D) = "gray" {}
        _HL_MatcapColor ("Matcap Color", Color) = (0.5,0.5,0.5,1)
        _HL_MedianColor ("Matcap Median", Color) = (0.5,0.5,0.5,1)
        _HL_Power ("Matcap Power", Float) = 1
        _HL_Enable_1 ("Matcap 1", Float) = 0
        _HL_CapType_1 ("Matcap 1 Type", Float) = 0
        _HL_MatcapTex_1 ("Matcap 1 Image", 2D) = "gray" {}
        _HL_Enable_2 ("Matcap 2", Float) = 0
        _TX2_Enable ("Second", Float) = 0
        _TX2_MainTex ("Second Image", 2D) = "white" {}
        _TX2_Color ("Second Color", Color) = (1,1,1,1)
        _TX2_BlendType ("Second Blend", Float) = 0
        _TX2_UVType ("Second UV", Float) = 0
        _OVL_Enable ("Overlay", Float) = 0
        _OVL_OverlayTex ("Overlay Image", 2D) = "white" {}
        _OVL_Color ("Overlay Color", Color) = (1,1,1,1)
        _OVL_Power ("Overlay Power", Float) = 1
        _OVL_UVType ("Overlay UV", Float) = 0
        _OVL_BlendType ("Overlay Blend", Float) = 0
        _OVL_UVScroll ("Overlay Scroll", Vector) = (0,0,0,0)
        _OVL_OutUVType ("Overlay Out UV", Float) = 0
        _TL_Enable ("Outline", Float) = 0
        _TL_LineColor ("Line", Color) = (0.1,0.1,0.1,1)
        _TL_LineWidth ("Line Width", Float) = 0.05
        _TL_BlendBase ("Line Base", Float) = 0
        _TL_BlendCustom ("Line Custom", Float) = 0
        _TL_MaskTex ("Line Mask", 2D) = "white" {}
        _TL_InvMaskVal ("Line Invert", Float) = 0
        _DSV_Enable ("Dissolve", Float) = 0
        _LME_Enable ("Lame", Float) = 0
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
