// A stand-in with the property names of Sunao Shader that the approximation
// reads. Tests name the shader "Sunao Shader/..." when they call Create.
Shader "Hidden/VRVlogTests/SunaoProbe"
{
    Properties
    {
        _MainTex ("Main", 2D) = "white" {}
        _Color ("Color", Color) = (1,1,1,1)
        _Alpha ("Alpha", Range(0, 2)) = 1
        _Cutout ("Cutout", Range(0, 1)) = 0.5
        _BumpMap ("Normal", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _AlphaMask ("Alpha Mask", 2D) = "white" {}
        _Bright ("Bright", Range(0, 1)) = 1
        _AlphaMaskStrength ("Alpha Mask Strength", Range(0, 1)) = 1
        _VertexColor ("Vertex Color", Int) = 0
        _UVScrollX ("Scroll X", Float) = 0
        _UVScrollY ("Scroll Y", Float) = 0
        _UVAnimation ("UV Animation", Float) = 0
        _DecalEnable ("Decal", Int) = 0
        _DecalTex ("Decal Image", 2D) = "white" {}
        _DecalColor ("Decal Color", Color) = (1,1,1,1)
        _DecalPosX ("Decal X", Float) = 0.5
        _DecalPosY ("Decal Y", Float) = 0.5
        _DecalSizeX ("Decal Width", Float) = 0.5
        _DecalSizeY ("Decal Height", Float) = 0.5
        _DecalRotation ("Decal Rotation", Float) = 0
        _DecalMode ("Decal Mode", Int) = 0
        _DecalMirror ("Decal Mirror", Int) = 0
        _DecalScrollX ("Decal Scroll X", Float) = 0
        _DecalScrollY ("Decal Scroll Y", Float) = 0
        _DecalAnimation ("Decal Animation", Float) = 0
        _ShadeMask ("Shade Mask", 2D) = "white" {}
        _Shade ("Shade", Range(0, 1)) = 0.3
        _ShadeWidth ("Shade Width", Float) = 0.75
        _ShadeGradient ("Shade Gradient", Float) = 0.75
        _ShadeColor ("Shade Color", Range(0, 1)) = 0.5
        _CustomShadeColor ("Custom Shade Color", Color) = (0,0,0,0)
        _ToonEnable ("Toon", Int) = 0
        _Toon ("Toon Steps", Float) = 9
        _ToonSharpness ("Toon Sharpness", Float) = 1
        _LightMask ("Light Mask", 2D) = "black" {}
        _Unlit ("Unlit", Range(0, 1)) = 0
        _MonochromeLit ("Monochrome", Int) = 0
        _OutLineEnable ("Outline", Int) = 0
        _OutLineMask ("Outline Mask", 2D) = "white" {}
        _OutLineColor ("Outline Color", Color) = (0,0,0,1)
        _OutLineSize ("Outline Size", Float) = 0.1
        _OutLineLighthing ("Outline Lighting", Int) = 1
        _OutLineTexture ("Outline Image", 2D) = "white" {}
        _OutLineFixScale ("Outline Scale", Int) = 0
        _OutLineTexColor ("Outline Uses Main", Int) = 0
        _EmissionEnable ("Emission", Int) = 0
        _EmissionMap ("Emission Map", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (1,1,1,1)
        _Emission ("Emission Strength", Float) = 1
        _EmissionMap2 ("Emission Map 2", 2D) = "white" {}
        _EmissionMode ("Emission Mode", Int) = 0
        _EmissionBlink ("Emission Blink", Float) = 0
        _EmissionFrequency ("Emission Frequency", Float) = 1
        _EmissionWaveform ("Emission Waveform", Int) = 0
        _EmissionScrX ("Emission Scroll X", Float) = 0
        _EmissionScrY ("Emission Scroll Y", Float) = 0
        _EmissionAnimation ("Emission Animation", Float) = 0
        _EmissionLighting ("Emission Lighting", Int) = 0
        _EmissionInTheDark ("Emission In The Dark", Float) = 0
        _ParallaxEnable ("Parallax", Int) = 0
        _ReflectionEnable ("Reflection", Int) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        _Specular ("Specular", Float) = 1
        _Metallic ("Metallic Strength", Float) = 0.5
        _GlossMapScale ("Smoothness", Float) = 0.75
        _MatCap ("MatCap", 2D) = "black" {}
        _MatCapColor ("MatCap Color", Color) = (1,1,1,1)
        _MatCapMaskEnable ("MatCap Uses Metallic", Int) = 1
        _MatCapMask ("MatCap Mask", 2D) = "white" {}
        _MatCapStrength ("MatCap Strength", Float) = 1
        _MatCapTexColor ("MatCap Base", Int) = 0
        _MatCapLit ("MatCap Light", Int) = 3
        _RimLitEnable ("Rim", Int) = 0
        _RimLitMask ("Rim Mask", 2D) = "white" {}
        _RimLitColor ("Rim Color", Color) = (1,1,1,1)
        _RimLit ("Rim Power", Float) = 1
        _RimLitGradient ("Rim Gradient", Float) = 1
        _RimLitLighthing ("Rim Lighting", Int) = 1
        _RimLitTexColor ("Rim Base", Int) = 0
        _RimLitMode ("Rim Mode", Int) = 0
        _Culling ("Culling", Int) = 0
        _DirectionalLight ("Directional", Float) = 1
        _SHLight ("SH", Float) = 1
        _PointLight ("Point", Float) = 1
        _LightLimitter ("Limiter", Int) = 1
        _MinimumLight ("Minimum Light", Float) = 0
        _EnableGammaFix ("Gamma Fix", Int) = 0
        _EnableBlightFix ("Bright Fix", Int) = 0
        _LimitterEnable ("Output Limit", Int) = 0
        _LimitterMax ("Output Limit Max", Float) = 1
        _SunaoShaderType ("Type", Int) = 0
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
