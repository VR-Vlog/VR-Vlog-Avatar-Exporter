using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The stand-in shader declares orels Toon's property names and keywords;
    // Create is told the real shader name.
    public sealed class OrelsToonApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/OrelsToonProbe";
        const string First = "orels1/Toon/Main";
        const string Second = "orels1/Toon/v2/Main";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Material Source()
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "OrelsSource" });
            source.SetTexture("_MainTex", Solid("main", Color.white));
            return source;
        }

        Texture2D Solid(string name, Color color, int size = 4)
        {
            var texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = name });
            var pixels = new Color[size * size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, new List<Texture2D>());

        Material Substitute(Material source, string shader = First) => Substitute(source, shader, out _, out _);

        static Color Center(Texture texture) => ((Texture2D)texture).GetPixel(2, 2);

        static void AssertColor(Color actual, Color expected) =>
            Assert.That(new[] { actual.r, actual.g, actual.b, actual.a }, Is.EqualTo(new[] { expected.r, expected.g, expected.b, expected.a }).Within(.01f));

        [TestCase("orels1/Toon/Main", "lilToon")]
        [TestCase("orels1/Toon/Cutout", "Hidden/lilToonCutout")]
        [TestCase("orels1/Toon/Transparent", "Hidden/lilToonTransparent")]
        [TestCase("orels1/Toon/v2/Main", "lilToon")]
        [TestCase("orels1/Toon/v2/Cutout LTCGI", "Hidden/lilToonCutout")]
        [TestCase("orels1/Toon/v2/Transparent PrePass", "Hidden/lilToonTransparent")]
        public void TheShaderNameChoosesTheLilToonShaderAndTheMainImageIsKept(string shader, string expected)
        {
            var source = Source();
            source.SetFloat("_Cutoff", .3f);
            source.SetFloat("_CullMode", 0);
            var output = Substitute(source, shader, out var route, out var dropped);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(source.GetTexture("_MainTex")));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
            if (expected == "Hidden/lilToonCutout") Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6f));
            if (shader.Contains("LTCGI")) Assert.That(dropped, Is.EquivalentTo(new[] { "LTCGI" }));
            else Assert.That(dropped, Is.Empty);
        }

        // Saturation and value are added in HSV and clamped, the hue shifted;
        // the result is baked with the main color.
        [Test] public void TheHueSaturationAndValueAreBakedIntoTheMainImage()
        {
            var source = Source();
            source.SetTexture("_MainTex", Solid("main", new Color(.5f, .25f, .25f, 1)));
            source.SetColor("_Color", new Color(1, 1, 1, .8f));
            source.SetFloat("_Hue", .25f);
            source.SetFloat("_Saturation", .2f);
            source.SetFloat("_Value", -.1f);
            var output = Substitute(source);
            Color.RGBToHSV(new Color(.5f, .25f, .25f), out var h, out var s, out var v);
            var expected = Color.HSVToRGB(h + .25f, s + .2f, v - .1f).gamma;
            AssertColor(Center(output.GetTexture("_MainTex")), new Color(expected.r, expected.g, expected.b, .8f));
            AssertColor(output.GetColor("_Color"), Color.white);
        }

        [Test] public void WithoutAMainImageTheShiftIsAppliedToTheColor()
        {
            var source = Source();
            source.SetTexture("_MainTex", null);
            source.SetColor("_Color", Color.red);
            source.SetFloat("_Hue", .5f);
            AssertColor(Substitute(source).GetColor("_Color"), Color.cyan);
        }

        [Test] public void TheAlphaScaleIsBakedForBlendedShadersOnly()
        {
            var source = Source();
            source.SetTexture("_MainTex", Solid("main", new Color(1, 1, 1, .5f)));
            source.SetFloat("_AlphaScale", .5f);
            Assert.That(Center(Substitute(source, "orels1/Toon/Transparent").GetTexture("_MainTex")).a, Is.EqualTo(.25f).Within(.01f));
            Assert.That(Substitute(source).GetTexture("_MainTex"), Is.SameAs(source.GetTexture("_MainTex")));
        }

        // SEPARATE_ALPHA: red × scale, or 1 − red × −scale for a negative one.
        [TestCase(.5f, 0f)]
        [TestCase(-1f, 1f)]
        public void ASeparateAlphaReplacesTheAlphaByTheRedOfItsImage(float scale, float value)
        {
            var source = Source();
            source.EnableKeyword("SEPARATE_ALPHA");
            source.SetTexture("_AlphaTex", Solid("alpha", new Color(.6f, .1f, .1f, 1)));
            source.SetFloat("_AlphaScale", scale);
            var output = Substitute(source, "orels1/Toon/v2/Transparent");
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(scale).Within(1e-6f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(value));
            Assert.That(Center(output.GetTexture("_AlphaMask")).r, Is.EqualTo(.6f).Within(.01f));
        }

        // The first version clamps red × scale, so a negative scale gives 0.
        [Test] public void TheFirstVersionDoesNotInvertTheSeparateAlpha()
        {
            var source = Source();
            source.EnableKeyword("SEPARATE_ALPHA");
            source.SetTexture("_AlphaTex", Solid("alpha", new Color(.6f, .1f, .1f, 1)));
            source.SetFloat("_AlphaScale", -1);
            var output = Substitute(source, "orels1/Toon/Transparent");
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(0f));
        }

        [Test] public void TheRampIsFittedWithOneEdgeAndItsDarkEndSetsTheShadowColor()
        {
            var source = Source();
            var ramp = Own(new Texture2D(64, 1, TextureFormat.RGBA32, false, true) { name = "ramp", wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[64];
            for (var i = 0; i < 64; i++) pixels[i] = i < 32 ? new Color(.2f, .2f, .2f, 1) : Color.white;
            ramp.SetPixels(pixels);
            ramp.Apply();
            source.SetTexture("_Ramp", ramp);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(.03f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.LessThan(.1f));
            Assert.That(output.GetFloat("_ShadowEnvStrength"), Is.EqualTo(1f));
            var expected = new Color(.2f, .2f, .2f, 1).gamma;
            AssertColor(output.GetColor("_ShadowColor"), new Color(expected.r, expected.g, expected.b, 1));
        }

        // The built-in grayscale ramp runs from black to white.
        [Test] public void TheDefaultRampIsAGradientFromBlack()
        {
            var output = Substitute(Source());
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(.01f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.GreaterThan(.5f));
            AssertColor(output.GetColor("_ShadowColor"), Color.black);
        }

        [Test] public void AWhiteRampHasNoShadow()
        {
            var source = Source();
            source.SetTexture("_Ramp", Solid("ramp", Color.white, 64));
            Assert.That(Substitute(source).GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void TheOutlineKeepsItsWidthColorAndLighting()
        {
            var source = Source();
            Assert.That(Substitute(source).shader.name, Is.EqualTo("lilToon"));
            source.EnableKeyword("OUTLINE_ENABLED");
            source.SetFloat("_OutlineWidth", 2);
            source.SetColor("_OutlineColor", new Color(.1f, .2f, .3f, 1));
            source.SetFloat("_OutlineLightingMode", 1);
            var mask = Solid("outline mask", Color.grey);
            source.SetTexture("_OutlineMask", mask);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(2f).Within(1e-6f));
            Assert.That(output.GetFloat("_OutlineEnableLighting"), Is.EqualTo(0f));
            AssertColor(output.GetColor("_OutlineColor"), new Color(.1f, .2f, .3f, 1));
            Assert.That(output.GetTexture("_OutlineTex"), Is.Null);
            Assert.That(output.GetTexture("_OutlineWidthMask"), Is.SameAs(mask));
        }

        [Test] public void AnAlbedoTintedOutlineTakesTheMainImage()
        {
            var source = Source();
            source.EnableKeyword("OUTLINE_ENABLED");
            source.SetFloat("_OutlineAlbedoTint", 1);
            source.SetColor("_OutlineColor", Color.white);
            source.SetColor("_Color", new Color(.5f, .5f, .5f, 1));
            var output = Substitute(source);
            Assert.That(output.GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_MainTex")));
            AssertColor(output.GetColor("_OutlineColor"), new Color(.5f, .5f, .5f, 1));
        }

        // The first version multiplies the map by itself; without _EMISSION
        // (no map was assigned) there is none.
        [Test] public void TheFirstVersionsEmissionCountsTheMapTwice()
        {
            var source = Source();
            source.SetTexture("_EmissionMap", Solid("emission", new Color(.5f, .5f, .5f, 1)));
            source.SetColor("_EmissionColor", Color.white);
            source.SetFloat("_EmissionTintToDiffuse", .5f);
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f));
            source.EnableKeyword("_EMISSION");
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_EmissionMainStrength"), Is.EqualTo(.5f).Within(1e-6f));
            Assert.That(Center(output.GetTexture("_EmissionMap")).r, Is.EqualTo(new Color(.25f, 0, 0).gamma.r).Within(.01f));
        }

        // v2 needs a map taller than 16 pixels and raises it to the power.
        [Test] public void TheSecondVersionsEmissionReadsTheChannelAndThePower()
        {
            var source = Source();
            source.SetColor("_EmissionColor", Color.white);
            source.SetTexture("_EmissionMap", Solid("small", Color.white));
            Assert.That(Substitute(source, Second).GetFloat("_UseEmission"), Is.EqualTo(0f));
            var map = Solid("emission", new Color(1, .5f, 0, 1), 32);
            source.SetTexture("_EmissionMap", map);
            Assert.That(Substitute(source, Second).GetTexture("_EmissionMap"), Is.SameAs(map));
            source.SetFloat("_EmissionMapChannel", 2);
            source.SetFloat("_EmissionPower", 2);
            var output = Substitute(source, Second);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            AssertColor(Center(output.GetTexture("_EmissionMap")), new Color(.25f, .25f, .25f, 1).gamma);
        }

        [Test] public void TheEmissionHueIsBakedAndTheLargestValueBecomesTheColor()
        {
            var source = Source();
            source.SetTexture("_EmissionMap", Solid("emission", Color.white, 32));
            source.SetColor("_EmissionColor", new Color(2, 0, 0, 1));
            source.SetFloat("_EmissionHue", 1 / 3f);
            var output = Substitute(source, Second);
            var map = Center(output.GetTexture("_EmissionMap"));
            Assert.That(map.g, Is.EqualTo(1f).Within(.01f));
            Assert.That(map.r, Is.LessThan(.01f));
            var peak = new Color(2, 0, 0).linear.r;
            Assert.That(output.GetColor("_EmissionColor").r, Is.EqualTo(Mathf.LinearToGammaSpace(peak)).Within(.01f));
        }

        [Test] public void TheFirstVersionsRimKeepsItsRangeAndSharpness()
        {
            var source = Source();
            source.EnableKeyword("RIMLIGHT_ON");
            Assert.That(Substitute(source).GetFloat("_UseRim"), Is.EqualTo(0f), "the first version's intensity is 0 by default");
            source.SetFloat("_RimIntensity", 1);
            source.SetColor("_RimTint", new Color(1, 1, 1, .5f));
            source.SetFloat("_RimAlbedoTint", .3f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.7f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.2f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimMainStrength"), Is.EqualTo(.3f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimShadowMask"), Is.EqualTo(1f));
            var expected = Mathf.LinearToGammaSpace(.5f);
            AssertColor(output.GetColor("_RimColor"), new Color(expected, expected, expected, 1));
        }

        [Test] public void TheSecondVersionsRimInvertsSpreadAndSharpness()
        {
            var source = Source();
            source.EnableKeyword("RIMLIGHT_ON");
            source.SetFloat("_RimIntensity", 1);
            source.SetFloat("_RimSpread", .3f);
            source.SetFloat("_RimSharpness", .95f);
            source.SetFloat("_RimThreshold", 0);
            var output = Substitute(source, Second);
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.7f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.1f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimShadowMask"), Is.EqualTo(0f));
        }

        [Test] public void TheFirstTwoMatcapsAreAddedOrMultiplied()
        {
            var source = Source();
            var add = Solid("add", Color.white);
            var multiply = Solid("multiply", Color.grey);
            source.EnableKeyword("MATCAP_0");
            source.SetTexture("_Matcap0Tex", add);
            source.SetFloat("_Matcap0Strength", .6f);
            source.SetTexture("_MatcapsMask", Solid("mask", new Color(0, .5f, 0, 1)));
            source.SetFloat("_Matcap0MaskChannel", 1);
            source.SetFloat("_Matcap0MaskStrength", .5f);
            source.EnableKeyword("MATCAP_1");
            source.SetTexture("_Matcap1Tex", multiply);
            source.SetFloat("_Matcap1BlendMode", 1);
            var output = Substitute(source, Second, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(add));
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.6f).Within(1e-6f));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(1f));
            Assert.That(Center(output.GetTexture("_MatCapBlendMask")).r, Is.EqualTo(.75f).Within(.01f));
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCap2ndTex"), Is.SameAs(multiply));
            Assert.That(output.GetFloat("_MatCap2ndBlendMode"), Is.EqualTo(3f));
            Assert.That(output.GetFloat("_MatCap2ndEnableLighting"), Is.EqualTo(0f));
            Assert.That(dropped, Is.Empty);

            source.SetFloat("_Matcap1BlendMode", 2);
            output = Substitute(source, Second, out _, out dropped);
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Matcap Subtract"));
        }

        [Test] public void TheFirstVersionsMatcapReflectionBecomesAMatcap()
        {
            var source = Source();
            var matcap = Solid("matcap", Color.white);
            source.EnableKeyword("REFLECTION_MODE_MATCAP");
            source.SetTexture("_Matcap", matcap);
            source.SetFloat("_ReflectivityLevel", .4f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(matcap));
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.4f).Within(1e-6f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(0f));
            source.SetFloat("_ReflectionBlendMode", 1);
            output = Substitute(source);
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(3f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(0f));
        }

        [Test] public void ReflectionsReadMetallicSmoothnessAndTheReflectivity()
        {
            var source = Source();
            source.SetFloat("_Metallic", .5f);
            source.SetFloat("_Smoothness", .7f);
            source.SetFloat("_ReflectivityLevel", .5f);
            Assert.That(Substitute(source, Second).GetFloat("_UseReflection"), Is.EqualTo(0f));
            source.EnableKeyword("REFLECTIONS_ON");
            var output = Substitute(source, Second);
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(Mathf.LinearToGammaSpace(.5f)).Within(1e-5f));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(.7f).Within(1e-5f));
            Assert.That(output.GetFloat("_Reflectance"), Is.EqualTo(Mathf.LinearToGammaSpace(.04f)).Within(1e-5f));
            // The first version uses the same 0.16 × level².
            source.DisableKeyword("REFLECTIONS_ON");
            source.EnableKeyword("REFLECTION_MODE_PBR");
            source.SetFloat("_ReflectivityLevel", 1);
            Assert.That(Substitute(source).GetFloat("_Reflectance"), Is.EqualTo(Mathf.LinearToGammaSpace(.16f)).Within(1e-5f));
        }

        // Red is metal, alpha smoothness, each remapped; roughness mode flips it.
        [Test] public void TheMetalSmoothnessMapIsRemappedAndBaked()
        {
            var source = Source();
            source.EnableKeyword("REFLECTION_MODE_PBR");
            source.SetTexture("_MetallicGlossMap", Solid("metal", new Color(.5f, 0, 0, .25f), 16));
            source.SetVector("_MetallicRemap", new Vector4(.2f, .6f, 0, 1));
            source.SetFloat("_RoughnessMode", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(1f));
            Assert.That(Center(output.GetTexture("_MetallicGlossMap")).r, Is.EqualTo(.4f).Within(.01f));
            Assert.That(Center(output.GetTexture("_SmoothnessTex")).r, Is.EqualTo(.75f).Within(.01f));
        }

        [Test] public void AFaceTintWithoutTheAlbedoColorsTheBackFaces()
        {
            var source = Source();
            source.SetFloat("_CullMode", 0);
            source.SetColor("_BackfaceColor", Color.red);
            source.SetFloat("_BackfaceAlbedoTint", 0);
            AssertColor(Substitute(source).GetColor("_BackfaceColor"), Color.red);
            source.SetFloat("_BackfaceAlbedoTint", 1);
            Substitute(source, First, out _, out var dropped);
            Assert.That(dropped, Does.Contain("Backface Color"));
        }

        [Test] public void TheNormalMapTakesItsOwnTilingOnTheMainImage()
        {
            var source = Source();
            source.SetTexture("_BumpMap", Solid("normal", new Color(.5f, .5f, 1, 1)));
            source.SetFloat("_BumpMapTiling", 3);
            source.SetTextureScale("_MainTex", new Vector2(2, 2));
            var output = Substitute(source);
            Assert.That(output.GetTextureScale("_BumpMap"), Is.EqualTo(new Vector2(3, 3)));
            source.SetFloat("_BumpMapTilingMode", 1);
            Assert.That(Substitute(source).GetTextureScale("_BumpMap"), Is.EqualTo(new Vector2(1.5f, 1.5f)));
        }

        [Test] public void EffectsLilToonHasNoCounterpartForAreReported()
        {
            var source = Source();
            source.EnableKeyword("RIMSHADOW_ON");
            source.EnableKeyword("SPECULAR_ON");
            source.EnableKeyword("UV_DISCARD");
            source.EnableKeyword("DECAL_0");
            source.EnableKeyword("DETAIL_NORMALS_0");
            source.EnableKeyword("MATCAP_2");
            source.SetFloat("AL_MODE", 1);
            source.SetFloat("_TintByVertexColor", 1);
            Substitute(source, Second, out _, out var dropped);
            Assert.That(dropped, Is.SupersetOf(new[]
            {
                "Rim Shadow", "Specular", "UV Discard", "Decals", "Detail Normal", "Matcap Layer 3", "Audio Link", "Tint By Vertex Color",
            }));
        }

        [Test] public void AMissingShaderIsKnownByItsSavedValues()
        {
            var source = Source();
            source.SetFloat("_Hue", .5f);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.HasProperty("_BackfaceAlbedoTint"), Is.False);
            var output = Substitute(source, null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.GetTexture("_MainTex"), Is.Not.SameAs(source.GetTexture("_MainTex")), "the hue is baked");
        }

        [Test] public void OtherShadersWithTheSameNamesAreNotTakenForOrelsToon()
        {
            Substitute(Source(), Probe, out var route, out _);
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
