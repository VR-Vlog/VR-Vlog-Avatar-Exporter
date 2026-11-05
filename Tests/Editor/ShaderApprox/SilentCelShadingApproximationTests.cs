using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The stand-in shader declares Silent's Cel Shading's property names and
    // keywords; Create is told the real shader name.
    public sealed class SilentCelShadingApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/SilentCelShadingProbe";
        const string Crosstone = "Silent's Cel Shading/Crosstone";
        const string Lightramp = "Silent's Cel Shading/Lightramp";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Material Source(int mode = 0)
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "SilentSource" });
            source.SetTexture("_MainTex", Own(new Texture2D(4, 4) { name = "main" }));
            source.SetFloat("_Mode", mode);
            return source;
        }

        Texture2D Solid(string name, Color color, int width = 4)
        {
            var texture = Own(new Texture2D(width, 1, TextureFormat.RGBA32, false, true) { name = name, wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[width];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped,
            List<Texture2D> textures = null) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, textures ?? new List<Texture2D>());

        Material Substitute(Material source, string shader = Crosstone) => Substitute(source, shader, out _, out _);

        static void AssertColor(Color actual, Color expected) =>
            Assert.That(new[] { actual.r, actual.g, actual.b, actual.a }, Is.EqualTo(new[] { expected.r, expected.g, expected.b, expected.a }).Within(1e-3f));

        [TestCase(0, 0, "lilToon")]
        [TestCase(1, 0, "Hidden/lilToonCutout")]
        [TestCase(2, 0, "Hidden/lilToonTransparent")]
        [TestCase(3, 0, "Hidden/lilToonTransparent")]
        [TestCase(5, 1, "Hidden/lilToonCutout")]
        [TestCase(5, 3, "Hidden/lilToonTransparent")]
        public void TheRenderingModeChoosesTheLilToonShaderAndTheMainImageIsKept(int mode, int custom, string expected)
        {
            var source = Source(mode);
            source.SetFloat("_CustomMode", custom);
            source.SetFloat("_Cutoff", .3f);
            source.SetFloat("_CullMode", 0);
            var output = Substitute(source, Crosstone, out var route, out var dropped);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(source.GetTexture("_MainTex")));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_FlipNormal"), Is.EqualTo(1f), "back faces are lit like the front");
            if (expected == "Hidden/lilToonCutout") Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AdditiveIsDrawnTransparentAndReported()
        {
            var output = Substitute(Source(4), Crosstone, out _, out var dropped);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(dropped, Does.Contain("Additive"));
        }

        [Test] public void OnlyTheOutlineShadersDrawTheOutline()
        {
            var source = Source();
            Assert.That(Substitute(source, Crosstone).shader.name, Is.EqualTo("lilToon"));
            Assert.That(Substitute(source, Lightramp + " (Outline)").shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            source.SetFloat("_OutlineMode", 0);
            Assert.That(Substitute(source, Crosstone + " (Outline)").shader.name, Is.EqualTo("lilToon"));
        }

        [Test] public void ATintedOutlineIsDrawnOverTheMainImageAtTheSameWidth()
        {
            var source = Source();
            source.SetFloat("_outline_width", .3f);
            source.SetColor("_outline_color", new Color(.2f, .4f, .6f, 1));
            var mask = Solid("outlineMask", Color.white);
            source.SetTexture("_OutlineMask", mask);
            var output = Substitute(source, Crosstone + " (Outline)");
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.3f).Within(1e-6f));
            AssertColor(output.GetColor("_OutlineColor"), new Color(.2f, .4f, .6f, 1));
            Assert.That(output.GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_MainTex")));
            Assert.That(output.GetTexture("_OutlineWidthMask"), Is.SameAs(mask));

            source.SetFloat("_OutlineMode", 2);
            Assert.That(Substitute(source, Crosstone + " (Outline)").GetTexture("_OutlineTex"), Is.Null);
        }

        [Test] public void TheFirstToneBecomesTheShadowAtItsStepAndFeather()
        {
            var source = Source();
            source.SetColor("_1st_ShadeColor", new Color(.6f, .5f, .4f, 1));
            source.SetFloat("_1st_ShadeColor_Step", .6f);
            source.SetFloat("_1st_ShadeColor_Feather", .2f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            AssertColor(output.GetColor("_ShadowColor"), new Color(.6f, .5f, .4f, 1));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.2f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0f));
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(0f), "a step of 0 has no second tone");
        }

        [Test] public void ACombinedSecondToneIsMultipliedByTheFirst()
        {
            var source = Source();
            source.SetColor("_1st_ShadeColor", new Color(.6f, .5f, .4f, 1));
            source.SetColor("_2nd_ShadeColor", new Color(.5f, .5f, .5f, 1));
            source.SetFloat("_2nd_ShadeColor_Step", .3f);
            source.SetFloat("_2nd_ShadeColor_Feather", .1f);
            var output = Substitute(source);
            AssertColor(output.GetColor("_Shadow2ndColor"), new Color(.3f, .25f, .2f, 1));
            Assert.That(output.GetFloat("_Shadow2ndBorder"), Is.EqualTo(.25f).Within(1e-5f));
            Assert.That(output.GetFloat("_Shadow2ndBlur"), Is.EqualTo(.1f).Within(1e-5f));

            // A separate first tone carries the tint into the combined second.
            source.SetFloat("_CrosstoneToneSeparation", 1);
            source.SetColor("_Color", new Color(.5f, .5f, .5f, 1));
            AssertColor(Substitute(source).GetColor("_Shadow2ndColor"), new Color(.15f, .125f, .1f, 1));
        }

        [Test] public void AShadeMapIsMultipliedByTheAlbedoUnlessTheTonesAreSeparate()
        {
            var source = Source();
            var map = Solid("shade", new Color(.5f, .5f, .8f, 1));
            source.SetTexture("_1st_ShadeMap", map);
            source.SetColor("_1st_ShadeColor", Color.white);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(map));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(1f));

            source.SetFloat("_CrosstoneToneSeparation", 1);
            Assert.That(Substitute(source).GetFloat("_ShadowMainStrength"), Is.EqualTo(0f));
        }

        [Test] public void ASeparateToneWithoutAnImageTakesAWhiteImageInPlaceOfTheMainImage()
        {
            var source = Source();
            source.SetFloat("_CrosstoneToneSeparation", 1);
            source.SetColor("_Color", new Color(.5f, .5f, .5f, 1));
            source.SetColor("_1st_ShadeColor", new Color(.25f, .25f, .25f, 1));
            var output = Substitute(source);
            var white = output.GetTexture("_ShadowColorTex") as Texture2D;
            Assert.That(white, Is.Not.Null);
            Assert.That(white, Is.Not.SameAs(Texture2D.whiteTexture));
            AssertColor(white.GetPixel(0, 0), Color.white);
            // The tone times the tint, not multiplied by the main image again.
            AssertColor(output.GetColor("_ShadowColor"), new Color(.125f, .125f, .125f, 1));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(0f));

            // A separate second tone takes the same white image.
            source.SetFloat("_2nd_ShadeColor_Step", .3f);
            source.SetFloat("_Crosstone2ndSeparation", 1);
            var both = Substitute(source);
            Assert.That(both.GetTexture("_ShadowColorTex"), Is.Not.Null);
            Assert.That(both.GetTexture("_Shadow2ndColorTex"), Is.SameAs(both.GetTexture("_ShadowColorTex")));
        }

        [Test] public void TheLightRampIsFittedWithOneEdgeAndItsDarkEndSetsTheColor()
        {
            var source = Source();
            var ramp = Own(new Texture2D(64, 1, TextureFormat.RGBA32, false, true) { name = "ramp", wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[64];
            for (var i = 0; i < 64; i++) pixels[i] = i < 32 ? new Color(.2f, .2f, .2f, 1) : Color.white;
            ramp.SetPixels(pixels);
            ramp.Apply();
            source.SetTexture("_Ramp", ramp);
            var output = Substitute(source, Lightramp);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(.03f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.LessThan(.1f));
            // The tone is black (no boost), lerped to white by the ramp's 0.2.
            var expected = new Color(.2f, .2f, .2f, 1).gamma;
            AssertColor(output.GetColor("_ShadowColor"), new Color(expected.r, expected.g, expected.b, 1));
        }

        [Test] public void TheDefaultWhiteRampHasNoShadow()
        {
            Assert.That(Substitute(Source(), Lightramp).GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void AGreyRampShadesTheWholeSurface()
        {
            var source = Source();
            source.SetTexture("_Ramp", Solid("ramp", new Color(.5f, .5f, .5f, 1), 64));
            var output = Substitute(source, Lightramp);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(1f));
            var expected = new Color(128 / 255f, 128 / 255f, 128 / 255f, 1).gamma;
            AssertColor(output.GetColor("_ShadowColor"), new Color(expected.r, expected.g, expected.b, 1));
        }

        [Test] public void TheSmoothRampTypeIsLambert()
        {
            var source = Source();
            source.SetFloat("_LightRampType", 3);
            var output = Substitute(source, Lightramp);
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.75f).Within(.01f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.5f).Within(.01f));
        }

        [Test] public void TheClippingMaskReplacesTheAlphaTimesTheTint()
        {
            var source = Source(2);
            source.SetFloat("_AlbedoAlphaMode", 2);
            source.SetColor("_Color", new Color(1, 1, 1, .8f));
            var mask = Solid("clip", new Color(.5f, .5f, .5f, 1));
            source.SetTexture("_ClippingMask", mask);
            source.SetFloat("_Tweak_Transparency", .1f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(1f));
            // saturate(mask + tweak) is baked, then times the tint.
            var baked = output.GetTexture("_AlphaMask") as Texture2D;
            Assert.That(baked, Is.Not.Null.And.Not.SameAs(mask));
            Assert.That(baked.GetPixel(0, 0).r, Is.EqualTo(.6f).Within(.01f));
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(.8f).Within(1e-5f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(0f));

            source.SetFloat("_Tweak_Transparency", .7f);
            Assert.That(((Texture2D)Substitute(source).GetTexture("_AlphaMask")).GetPixel(0, 0).r, Is.EqualTo(1f).Within(.01f),
                "the sum is clamped before the tint");

            source.SetFloat("_Mode", 0);
            Assert.That(Substitute(source).GetFloat("_AlphaMaskMode"), Is.EqualTo(0f), "an opaque material keeps no mask");
        }

        [Test] public void WithSmoothnessInTheMainAlphaOnlyTheTintAlphaIsKept()
        {
            var source = Source(1);
            source.SetFloat("_AlbedoAlphaMode", 1);
            source.SetColor("_Color", new Color(1, 1, 1, .7f));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(.7f).Within(1e-5f));
        }

        [TestCase(90, .25f)]
        [TestCase(-90, -.25f)]
        public void TheHueControlsBecomeHueSaturationAndValue(float hue, float expected)
        {
            var source = Source();
            source.SetFloat("_ToggleHueControls", 1);
            source.SetFloat("_ShiftHue", hue);
            source.SetFloat("_ShiftSaturation", 2);
            source.SetFloat("_ShiftValue", .5f);
            var hsvg = Substitute(source).GetVector("_MainTexHSVG");
            Assert.That(new[] { hsvg.x, hsvg.y, hsvg.z, hsvg.w }, Is.EqualTo(new[] { expected, 2, .5f, 1 }).Within(1e-5f));
        }

        [Test] public void TheRimLightEdgeIsPlacedOnOneMinusNDotV()
        {
            var source = Source();
            Assert.That(Substitute(source).GetFloat("_UseRim"), Is.EqualTo(0f));
            source.SetFloat("_UseFresnel", 1);
            source.SetColor("_FresnelTint", new Color(1, .5f, .5f, 1));
            var output = Substitute(source);
            var inner = Mathf.Pow(.5f, .25f);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo((inner + 1) / 2).Within(1e-4f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(1 - inner).Within(1e-4f));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimMainStrength"), Is.EqualTo(1f), "the lit rim multiplies the albedo");
            AssertColor(output.GetColor("_RimColor"), new Color(1, .5f, .5f, 1));
        }

        [Test] public void TheFirstTwoMatcapsAreMappedWithTheirBlendModes()
        {
            var source = Source();
            source.SetFloat("_UseMatcap", 1);
            var first = Solid("matcap1", Color.white);
            var second = Solid("matcap2", Color.white);
            source.SetTexture("_Matcap1", first);
            source.SetFloat("_Matcap1Blend", 1);
            source.SetFloat("_Matcap1Strength", .5f);
            source.SetTexture("_Matcap2", second);
            source.SetFloat("_Matcap2Blend", 2);
            source.SetTexture("_Matcap3", Solid("matcap3", Color.white));
            var output = Substitute(source, Crosstone, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(first));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(3f), "multiply");
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCap2ndTex"), Is.SameAs(second));
            Assert.That(output.GetFloat("_MatCap2ndBlendMode"), Is.EqualTo(1f), "median adds");
            Assert.That(output.GetFloat("_MatCap2ndMainStrength"), Is.EqualTo(1f), "median multiplies by the albedo");
            Assert.That(dropped, Does.Contain("Matcap 3 and 4"));
        }

        [Test] public void TheEmissionColorIsAddedEvenWithoutItsKeywordButTheMapIsNot()
        {
            var source = Source();
            var map = Solid("emission", Color.white);
            source.SetTexture("_EmissionMap", map);
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f), "black adds nothing");
            source.SetColor("_EmissionColor", Color.red);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.Null);
            AssertColor(output.GetColor("_EmissionColor"), Color.red);

            source.EnableKeyword("_EMISSION");
            source.SetFloat("_EmissionMode", 1);
            output = Substitute(source);
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(map));
            Assert.That(output.GetFloat("_EmissionMainStrength"), Is.EqualTo(1f));
        }

        [Test] public void TheSecondEmissionIsMappedToLilToonsSecond()
        {
            var source = Source();
            var map = Solid("emission2", Color.white);
            source.EnableKeyword("_EMISSION_2ND");
            source.SetTexture("_EmissionMap2nd", map);
            source.SetColor("_EmissionColor2nd", Color.green);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission2nd"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_Emission2ndMap"), Is.SameAs(map));
            AssertColor(output.GetColor("_Emission2ndColor"), Color.green);
        }

        [Test] public void CelSpecularIsToonSpecularAndMetalnessIsReadFromTheTint()
        {
            var source = Source();
            Assert.That(Substitute(source).GetFloat("_UseReflection"), Is.EqualTo(0f));
            source.SetFloat("_SpecularType", 4);
            source.SetFloat("_CelSpecularSoftness", .1f);
            source.SetFloat("_UseMetallic", 1);
            source.SetColor("_SpecColor", new Color(.6f, .6f, .6f, 1));
            source.SetFloat("_Smoothness", .7f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_SpecularToon"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_SpecularBlur"), Is.EqualTo(.1f).Within(1e-5f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(.6f).Within(1e-5f));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(.7f).Within(1e-5f));
        }

        [Test] public void EffectsLilToonHasNoCounterpartForAreReported()
        {
            var source = Source();
            source.SetFloat("_UseHatching", 1);
            source.SetFloat("_UseSubsurfaceScattering", 1);
            source.SetFloat("_SDFMode", 1);
            source.SetFloat("_VertexColorType", 0);
            Substitute(source, Crosstone + " (Fur)", out _, out var dropped);
            Assert.That(dropped, Is.SupersetOf(new[] { "Hatching", "Subsurface Scattering", "SDF", "Vertex Color", "Fur" }));
        }

        [Test] public void AMissingShaderIsKnownByItsSavedValues()
        {
            var source = Source(1);
            source.SetColor("_EmissionColor", Color.white);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.HasProperty("_LightingCalculationType"), Is.False);
            var output = Substitute(source, null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"), "a missing shader draws no outline");
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
        }

        [Test] public void OtherShadersWithTheSameNamesAreNotTakenForSilentsCelShading()
        {
            Substitute(Source(), Probe, out var route, out _);
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
