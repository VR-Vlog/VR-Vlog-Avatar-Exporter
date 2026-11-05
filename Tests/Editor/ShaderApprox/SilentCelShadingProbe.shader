// A stand-in with the property names and keywords of Silent's Cel Shading.
// Tests name the shader "Silent's Cel Shading/..." when they call Create.
Shader "Hidden/VRVlogTests/SilentCelShadingProbe"
{
    Properties
    {
        _MainTex ("Main Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _ColorMask ("Color Mask Map", 2D) = "white" {}
        _ClippingMask ("Alpha Transparency Map", 2D) = "white" {}
        _Tweak_Transparency ("Transparency Adjustment", Range(-1, 1)) = 0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Map Scale", Float) = 1.0
        _VertexColorType ("Vertex Colour Type", Float) = 2.0
        _ToggleHueControls ("Toggle Hue Controls", Float) = 0.0
        _ShiftHue ("Shift Hue", Range(-180, 180)) = 0.0
        _ShiftSaturation ("Shift Saturation", Range(0, 6)) = 1.0
        _ShiftValue ("Shift Value", Range(0, 6)) = 1.0
        _1st_ShadeMap ("1st Shading Tone", 2D) = "white" {}
        _1st_ShadeColor ("1st Shading Tone Tint", Color) = (0,0,0,1)
        _2nd_ShadeMap ("2nd Shading Tone", 2D) = "white" {}
        _2nd_ShadeColor ("2nd Shading Tone Tint", Color) = (0,0,0,1)
        _ShadingGradeMap ("Shading Adjustment Map", 2D) = "white" {}
        _Tweak_ShadingGradeMapLevel ("Shading Adjustment Level", Range(-0.5, 0.5)) = 0
        _1st_ShadeColor_Step ("1st Shade Step", Range(0, 1)) = 0.5
        _1st_ShadeColor_Feather ("1st Shade Feather", Range(0.001, 1)) = 0.01
        _2nd_ShadeColor_Step ("2nd Shade Step", Range(0, 1)) = 0
        _2nd_ShadeColor_Feather ("2nd Shade Feather", Range(0.001, 1)) = 0.01
        _CrosstoneToneSeparation ("Tone Separation", Float) = 0
        _Crosstone2ndSeparation ("2nd Separation", Float) = 0
        _ShadowBorderColor ("Shadow Border Color", Color) = (0,0,0,1)
        _ShadowBorderRange ("Shadow Border Range", Range(0, 1)) = 0
        _SDFMode ("SDF Mode", Float) = 0
        _LightRampType ("Light Ramp Type", Float) = 0.0
        _Ramp ("Lighting Ramp", 2D) = "white" {}
        _ShadowMaskType ("Shadow Mask Type", Float) = 0.0
        _ShadowMask ("Shadow Mask", 2D) = "white" {}
        _ShadowMaskColor ("Shadow Mask Tint", Color) = (1,1,1,1)
        _Shadow ("Shadow Mask Strength", Range(0, 1)) = 0.5
        _ShadowLift ("Shadow Lift", Range(-1, 1)) = 0.0
        _IndirectLightingBoost ("Indirect Lighting Boost", Range(0, 1)) = 0.0
        _OutlineMode ("Outline Mode", Float) = 1.0
        _OutlineMask ("Outline Mask", 2D) = "white" {}
        _OutlineZPush ("Outline Z Push", Float) = 0.0
        _OutlineCalculationMode ("Outline Calculation Mode", Float) = 0
        _outline_width ("Outline Width", Float) = 0.1
        _outline_color ("Outline Colour", Color) = (0.5,0.5,0.5,1)
        _FurLength ("Fur Length", Float) = 1.0
        _UseFresnel ("Rim Lighting Style", Float) = 0.0
        _FresnelTint ("Rim Tint", Color) = (1,1,1,1)
        _FresnelWidth ("Rim Width", Range(0, 20)) = .5
        _FresnelStrength ("Rim Softness", Range(0.01, 0.9999)) = 0.5
        _UseFresnelLightMask ("Rim Light Mask", Float) = 0.0
        _SpecularType ("Specular Style", Float) = 0.0
        _SpecColor ("Specular Tint", Color) = (1,1,1)
        _SpecGlossMap ("Specular Map", 2D) = "white" {}
        _UseMetallic ("Use as Metalness", Float) = 0.0
        _SpecularHighlights ("Specular Highlights", Float) = 1.0
        _GlossyReflections ("Reflections", Float) = 1.0
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _CelSpecularSoftness ("Softness", Range(1, 0)) = 0.02
        _CelSpecularSteps ("Steps", Range(1, 4)) = 1
        _UseMatcap ("Matcap Style", Float) = 0.0
        _MatcapMask ("Matcap Mask", 2D) = "white" {}
        _Matcap1 ("Matcap 1", 2D) = "black" {}
        _Matcap1Strength ("Matcap 1 Strength", Range(0, 2)) = 1.0
        _Matcap1Blend ("Matcap 1 Blend", Float) = 0.0
        _Matcap1Tint ("Matcap 1 Tint", Color) = (1,1,1,1)
        _Matcap2 ("Matcap 2", 2D) = "black" {}
        _Matcap2Strength ("Matcap 2 Strength", Range(0, 2)) = 1.0
        _Matcap2Blend ("Matcap 2 Blend", Float) = 0.0
        _Matcap2Tint ("Matcap 2 Tint", Color) = (1,1,1,1)
        _Matcap3 ("Matcap 3", 2D) = "black" {}
        _Matcap4 ("Matcap 4", 2D) = "black" {}
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionMode ("Emission Mode", Float) = 0.0
        _EmissionUVSec ("Emission UV", Float) = 0
        [HDR] _EmissionColor ("Emission Colour", Color) = (0,0,0,1)
        _DetailEmissionMap ("Detail Emission", 2D) = "white" {}
        _EmissionMap2nd ("Emission 2nd", 2D) = "white" {}
        _EmissionMode2nd ("Emission Mode 2nd", Float) = 0.0
        _EmissionUVSec2nd ("Emission UV 2nd", Float) = 0
        [HDR] _EmissionColor2nd ("Emission Colour 2nd", Color) = (0,0,0,1)
        _DetailEmissionMap2nd ("Detail Emission 2nd", 2D) = "white" {}
        _EmissionRimPower ("Emission Rim Power", Float) = 0.0
        _EmissionRimPower2nd ("Emission Rim Power 2nd", Float) = 0.0
        _UseEmissiveLightSense ("Emissive Light Sense", Float) = 0.0
        _UseDetailMaps ("Detail Maps", Float) = 0.0
        _UseHatching ("Hatching", Float) = 0.0
        _UseAlphaFresnel ("Alpha Fresnel", Float) = 0
        _UseEmissiveAudiolink ("Audiolink", Float) = 0.0
        _UseSubsurfaceScattering ("Subsurface Scattering", Float) = 0.0
        _UseContactShadows ("Contact Shadows", Float) = 0
        _UseAnimation ("Animation", Float) = 0.0
        _UseVanishing ("Vanishing", Float) = 0.0
        _UseProximityShadow ("Proximity Shadow", Float) = 0.0
        _UseInventory ("Inventory", Float) = 0.0
        _UseBackfaceTexture ("Backface Texture", Float) = 0.0
        _AlbedoAlphaMode ("Albedo Alpha Mode", Float) = 0.0
        _PixelSampleMode ("Pixel Sample Mode", Float) = 0.0
        _LightingCalculationType ("Lighting Calculation Type", Float) = 3.0
        _IndirectShadingType ("Indirect Shading Type", Float) = 0.0
        _Mode ("Rendering Mode", Float) = 0
        _CustomMode ("Custom Rendering Mode", Float) = 0
        _SrcBlend ("__src", Float) = 1
        _DstBlend ("__dst", Float) = 0
        _ZWrite ("__zw", Float) = 1
        _CullMode ("Cull Mode", Float) = 2
    }
    SubShader
    {
        Pass
        {
            Cull [_CullMode]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _EMISSION
            #pragma shader_feature_local _EMISSION_2ND
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _Color;
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata_base v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.texcoord.xy; return o; }
            float4 frag(v2f i) : SV_Target { return tex2D(_MainTex, i.uv) * _Color; }
            ENDCG
        }
    }
}
