using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class UtsApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/UtsProbe";
        const string Name = "UnityChanToonShader/Toon_DoubleShadeWithFeather";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Texture2D Image(string name, float alpha = 1)
        {
            var image = Own(new Texture2D(4, 4) { name = name });
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(.2f, .4f, .6f, alpha);
            image.SetPixels(pixels);
            image.Apply();
            return image;
        }

        Material Source()
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "UtsSource" });
            source.SetTexture("_MainTex", Image("main"));
            return source;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, new List<Texture2D>());

        Material Substitute(Material source, out IReadOnlyList<string> dropped) => Substitute(source, Name, out _, out dropped);

        Material Substitute(Material source) => Substitute(source, out _);

        static void AssertColor(Material material, string name, Color expected)
        {
            var color = material.GetColor(name);
            Assert.That(color.r, Is.EqualTo(expected.r).Within(1e-4f), name);
            Assert.That(color.g, Is.EqualTo(expected.g).Within(1e-4f), name);
            Assert.That(color.b, Is.EqualTo(expected.b).Within(1e-4f), name);
        }

        // lilToon would mix the base color in by the alpha; the source reads RGB.
        [Test] public void AShadeImageWithAlphaIsRedrawnOpaque()
        {
            var source = Source();
            var first = Image("first", .5f);
            source.SetTexture("_1st_ShadeMap", first);
            var output = Substitute(source).GetTexture("_ShadowColorTex");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Assert.That(output, Is.SameAs(first)); return; }
            Assert.That(output, Is.Not.SameAs(first));
            var pixel = ((Texture2D)output).GetPixel(0, 0);
            Assert.That(pixel.a, Is.EqualTo(1).Within(1e-3f));
            Assert.That(pixel.r, Is.EqualTo(.2f).Within(.01f));
            Assert.That(pixel.b, Is.EqualTo(.6f).Within(.01f));
        }

        [Test] public void TheSecondShadeTakesTheFirstOnesLightSettings()
        {
            var source = Source();
            source.SetFloat("_ShadeColor_Step", .3f);
            source.SetFloat("_Is_NormalMapToBase", 1);
            source.SetFloat("_Set_SystemShadowsToBase", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_Shadow2ndNormalStrength"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_Shadow2ndReceive"), Is.EqualTo(1));
            source.SetFloat("_Is_NormalMapToBase", 0);
            source.SetFloat("_Set_SystemShadowsToBase", 0);
            output = Substitute(source);
            Assert.That(output.GetFloat("_Shadow2ndNormalStrength"), Is.EqualTo(0));
            Assert.That(output.GetFloat("_Shadow2ndReceive"), Is.EqualTo(0));
        }

        // lilToon reads them in the main image's UV.
        [Test] public void TilingOfAShadeImageIsReported()
        {
            var source = Source();
            source.SetTexture("_1st_ShadeMap", Image("first"));
            source.SetTextureScale("_1st_ShadeMap", new Vector2(2, 2));
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("Shade Map Tiling"));
            source.SetTextureScale("_1st_ShadeMap", Vector2.one);
            Substitute(source, out dropped);
            Assert.That(dropped, Does.Not.Contain("Shade Map Tiling"));
        }

        // The direction mask follows the light, not the shadow.
        [Test] public void TheRimsDirectionMaskFollowsTheLight()
        {
            var source = Source();
            source.SetFloat("_RimLight", 1);
            source.SetFloat("_LightDirection_MaskOn", 1);
            source.SetFloat("_Tweak_LightDirection_MaskLevel", .2f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_RimShadowMask"), Is.EqualTo(0));
            Assert.That(output.GetFloat("_RimDirStrength"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimDirRange"), Is.EqualTo(-.2f).Within(1e-4f));
            Assert.That(output.GetColor("_RimIndirColor").a, Is.EqualTo(0));
            source.SetFloat("_LightDirection_MaskOn", 0);
            Assert.That(Substitute(source).GetFloat("_RimDirStrength"), Is.EqualTo(0));
        }

        [Test] public void TheVariantsAreFoundByTheirNames()
        {
            var output = Substitute(Source(), Name, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
        }

        [Test] public void AMissingShaderIsFoundByItsSavedValues()
        {
            var source = Source();
            var main = source.GetTexture("_MainTex");
            var output = Substitute(source, null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
        }

        // The variant is read from the shader's own name.
        [Test] public void TheClippingVariantStaysACutout()
        {
            const string text = @"Shader ""Hidden/VRVlogTests/Uts/Toon_DoubleShadeWithFeather_Clipping"" {
                Properties {
                    _Is_LightColor_Base ("""", Float) = 1
                    _1st_ShadeColor ("""", Color) = (1,1,1,1)
                    _Clipping_Level ("""", Float) = 0
                    _ClippingMask ("""", 2D) = ""white"" {}
                }
                SubShader { Pass {
                    CGPROGRAM
                    #pragma vertex vert
                    #pragma fragment frag
                    #include ""UnityCG.cginc""
                    float4 vert(float4 v : POSITION) : SV_POSITION { return UnityObjectToClipPos(v); }
                    fixed4 frag() : SV_Target { return 1; }
                    ENDCG
                } }
            }";
            var shader = Own(UnityEditor.ShaderUtil.CreateShaderAsset(text));
            var source = Own(new Material(shader));
            source.SetFloat("_Clipping_Level", .3f);
            var output = Substitute(source, shader.name, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
        }

        [Test] public void TheTwoShadesKeepTheirStepsFeathersAndImages()
        {
            var source = Source();
            var first = Image("first"); var second = Image("second");
            source.SetTexture("_1st_ShadeMap", first); source.SetTexture("_2nd_ShadeMap", second);
            source.SetColor("_1st_ShadeColor", new Color(.8f, .7f, .6f));
            source.SetColor("_2nd_ShadeColor", new Color(.5f, .4f, .3f));
            source.SetFloat("_BaseColor_Step", .6f); source.SetFloat("_BaseShade_Feather", .1f);
            source.SetFloat("_ShadeColor_Step", .3f); source.SetFloat("_1st2nd_Shades_Feather", .2f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.55f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.1f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0));
            Assert.That(output.GetFloat("_Shadow2ndBorder"), Is.EqualTo(.2f).Within(1e-4f));
            Assert.That(output.GetFloat("_Shadow2ndBlur"), Is.EqualTo(.2f).Within(1e-4f));
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(first));
            Assert.That(output.GetTexture("_Shadow2ndColorTex"), Is.SameAs(second));
            AssertColor(output, "_ShadowColor", new Color(.8f, .7f, .6f));
            AssertColor(output, "_Shadow2ndColor", new Color(.5f, .4f, .3f));
        }

        [Test] public void WithoutTheSecondStepTheSecondShadeIsOff()
        {
            var output = Substitute(Source());
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(0));
        }

        // A shade set to a flat color with no map replaces the base image: it
        // is not the base image tinted.
        [Test] public void AFlatShadeColorIsAColorOfItsOwn()
        {
            var source = Source();
            source.SetColor("_1st_ShadeColor", new Color(.2f, .3f, .4f));
            var output = Substitute(source);
            var image = output.GetTexture("_ShadowColorTex") as Texture2D;
            Assert.That(image, Is.Not.Null);
            Assert.That(image, Is.Not.SameAs(Texture2D.whiteTexture));
            Assert.That(image.GetPixel(0, 0), Is.EqualTo(Color.white));
            AssertColor(output, "_ShadowColor", new Color(.2f, .3f, .4f));
        }

        // Base-as-first reuses the base image, so the color becomes the ratio
        // to the base color lilToon multiplies by.
        [Test] public void TheBaseAsTheShadeTintsTheBaseImage()
        {
            var source = Source();
            source.SetColor("_BaseColor", new Color(.5f, .5f, .5f));
            source.SetColor("_1st_ShadeColor", new Color(.25f, .25f, .25f));
            source.SetFloat("_Use_BaseAs1st", 1);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            AssertColor(output, "_ShadowColor", new Color(.5f, .5f, .5f));
        }

        [Test] public void TheFirstShadeCanStandInForTheSecond()
        {
            var source = Source();
            var first = Image("first");
            source.SetTexture("_1st_ShadeMap", first);
            source.SetFloat("_ShadeColor_Step", .3f);
            source.SetFloat("_Use_1stAs2nd", 1);
            Assert.That(Substitute(source).GetTexture("_Shadow2ndColorTex"), Is.SameAs(first));
        }

        [Test] public void TheFirstPositionMapForcesTheShade()
        {
            var source = Source();
            var position = Image("position");
            source.SetTexture("_Set_1st_ShadePosition", position);
            source.SetTexture("_Set_2nd_ShadePosition", Image("position2"));
            var output = Substitute(source, out var dropped);
            Assert.That(output.GetTexture("_ShadowBorderMask"), Is.SameAs(position));
            // The red channel scales the first shade's light, the second is left alone.
            Assert.That(output.GetVector("_ShadowAOShift"), Is.EqualTo(new Vector4(1, 0, 0, 1)));
            Assert.That(dropped, Does.Contain("2nd Shade Position"));
        }

        [Test] public void TheGradeTechniqueReadsItsOwnStepsAndMap()
        {
            var source = Source();
            var grade = Image("grade");
            source.SetFloat("_utsTechnique", 1);
            source.SetFloat("_1st_ShadeColor_Step", .7f); source.SetFloat("_1st_ShadeColor_Feather", .2f);
            source.SetFloat("_2nd_ShadeColor_Step", .4f); source.SetFloat("_2nd_ShadeColor_Feather", .1f);
            source.SetFloat("_BaseColor_Step", .1f);
            source.SetTexture("_ShadingGradeMap", grade);
            source.SetFloat("_Tweak_ShadingGradeMapLevel", .25f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.6f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.2f).Within(1e-4f));
            Assert.That(output.GetFloat("_Shadow2ndBorder"), Is.EqualTo(.35f).Within(1e-4f));
            Assert.That(output.GetTexture("_ShadowBorderMask"), Is.SameAs(grade));
            Assert.That(output.GetVector("_ShadowAOShift"), Is.EqualTo(new Vector4(1, .25f, 1, .25f)));
        }

        [Test] public void TheRimIsOffUnlessTurnedOn()
        {
            Assert.That(Substitute(Source()).GetFloat("_UseRim"), Is.EqualTo(0));
        }

        [TestCase(1f, 1f)]
        [TestCase(0f, 8f)]
        public void TheRimPowerIsTheShadersExponent(float power, float exponent)
        {
            var source = Source();
            source.SetFloat("_RimLight", 1);
            source.SetFloat("_RimLight_Power", power);
            source.SetColor("_RimLightColor", new Color(.1f, .2f, .3f));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(exponent).Within(1e-4f));
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1));
            AssertColor(output, "_RimColor", new Color(.1f, .2f, .3f));
        }

        [TestCase(0f, .75f, .5f)]
        [TestCase(1f, .5f, .001f)]
        public void TheRimInsideMaskPlacesTheRimEdge(float featherOff, float border, float blur)
        {
            var source = Source();
            source.SetFloat("_RimLight", 1);
            source.SetFloat("_RimLight_InsideMask", .5f);
            source.SetFloat("_RimLight_FeatherOff", featherOff);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(border).Within(1e-4f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(blur).Within(1e-4f));
        }

        // The shader's slot is black when empty, which adds nothing.
        [Test] public void AMatcapWithoutAnImageStaysOff()
        {
            var source = Source();
            source.SetFloat("_MatCap", 1);
            Assert.That(Substitute(source).GetFloat("_UseMatCap"), Is.EqualTo(0));
        }

        [TestCase(1f, 1f)]
        [TestCase(0f, 3f)]
        public void TheMatcapAddsOrMultiplies(float add, float mode)
        {
            var source = Source();
            var image = Image("matcap");
            source.SetFloat("_MatCap", 1);
            source.SetTexture("_MatCap_Sampler", image);
            source.SetFloat("_Is_BlendAddToMatCap", add);
            source.SetFloat("_BlurLevelMatcap", 2);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(image));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(mode));
            Assert.That(output.GetFloat("_MatCapLod"), Is.EqualTo(2));
        }

        [Test] public void WithoutAWidthThereIsNoOutline()
        {
            Assert.That(Substitute(Source()).shader.name, Does.Not.Contain("Outline"));
        }

        // Millimeters in the source, centimeters in lilToon.
        [Test] public void TheOutlineWidthChangesUnit()
        {
            var source = Source();
            var mask = Image("width");
            source.SetFloat("_Outline_Width", 2);
            source.SetColor("_Outline_Color", new Color(.2f, .1f, .3f));
            source.SetTexture("_Outline_Sampler", mask);
            var output = Substitute(source);
            Assert.That(output.shader.name, Does.Contain("Outline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.2f).Within(1e-4f));
            AssertColor(output, "_OutlineColor", new Color(.2f, .1f, .3f));
            if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.That(output.GetTexture("_OutlineWidthMask"), Is.Not.Null);
        }

        // The source multiplies in linear space, which differs in the dark.
        [Test] public void TheOutlineTakesTheBaseColorInLinearSpace()
        {
            var source = Source();
            source.SetFloat("_Outline_Width", 2);
            source.SetFloat("_Is_BlendBaseColor", 1);
            source.SetColor("_Outline_Color", Color.white);
            source.SetColor("_BaseColor", new Color(.2f, .2f, .2f));
            var linear = Mathf.GammaToLinearSpace(.2f);
            Assert.That(Substitute(source).GetColor("_OutlineColor").g, Is.EqualTo(Mathf.LinearToGammaSpace(linear * linear)).Within(1e-3f));
        }

        // A power of 0 draws nothing, and the default high color is not an effect lost.
        [Test] public void AHighColorWithoutPowerIsNotReported()
        {
            var source = Source();
            source.SetColor("_HighColor", Color.white);
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Not.Contain("High Color"));
            source.SetFloat("_Is_SpecularToHighColor", 1);
            Substitute(source, out dropped);
            Assert.That(dropped, Does.Contain("High Color"));
        }

        // Main image × color is the source's shade; the ratio would stop at 1.
        [Test] public void AShadeBrighterThanTheBaseTakesTheMainImage()
        {
            var source = Source();
            source.SetFloat("_Use_BaseAs1st", 1);
            source.SetColor("_BaseColor", new Color(.2f, .2f, .2f));
            source.SetColor("_1st_ShadeColor", new Color(.8f, .8f, .8f));
            var main = Image("main");
            source.SetTexture("_MainTex", main);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(main));
            Assert.That(output.GetColor("_ShadowColor").g, Is.EqualTo(.8f).Within(1e-3f));
        }

        [Test] public void AShadeDarkerThanTheBaseStaysARatio()
        {
            var source = Source();
            source.SetFloat("_Use_BaseAs1st", 1);
            source.SetColor("_BaseColor", new Color(.8f, .8f, .8f));
            source.SetColor("_1st_ShadeColor", new Color(.4f, .4f, .4f));
            source.SetTexture("_MainTex", Image("main"));
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            Assert.That(output.GetColor("_ShadowColor").g, Is.EqualTo(.5f).Within(1e-3f));
        }

        [Test] public void TheOutlineCanTakeAnImageOrTheBase()
        {
            var source = Source();
            var image = Image("outline");
            source.SetFloat("_Outline_Width", 1);
            source.SetFloat("_Is_OutlineTex", 1);
            source.SetTexture("_OutlineTex", image);
            Assert.That(Substitute(source).GetTexture("_OutlineTex"), Is.SameAs(image));
            source.SetFloat("_Is_OutlineTex", 0);
            source.SetFloat("_Is_BlendBaseColor", 1);
            Assert.That(Substitute(source).GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_MainTex")));
        }

        [Test] public void WhatLilToonCannotShowIsReported()
        {
            var source = Source();
            source.SetColor("_HighColor", Color.white);
            source.SetFloat("_HighColor_Power", .5f);
            source.SetFloat("_AngelRing", 1);
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("High Color"));
            Assert.That(dropped, Does.Contain("Angel Ring"));
        }

        [Test] public void StandardIsNotTakenForThisShader()
        {
            var shader = Shader.Find("Standard");
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader));
            Substitute(source, "Standard", out var route, out _);
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
