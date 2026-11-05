using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The stand-in shader declares Mochie Standard's property names and
    // keywords; Create is told the real shader name.
    public sealed class MochieStandardApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/MochieStandardProbe";
        const string Name = "Mochie/Standard";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Material Source(int blendMode = 0)
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "MochieSource" });
            source.SetTexture("_MainTex", Own(new Texture2D(4, 4) { name = "main" }));
            source.SetFloat("_BlendMode", blendMode);
            source.EnableKeyword("_REFLECTIONS_ON");
            source.EnableKeyword("_SPECULAR_HIGHLIGHTS_ON");
            return source;
        }

        Texture2D Solid(string name, Color color)
        {
            var texture = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = name });
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped,
            List<Texture2D> textures = null) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, textures ?? new List<Texture2D>());

        static float Center(Texture texture) => ((Texture2D)texture).GetPixel(2, 2).r;

        [TestCase(0, "lilToon")]
        [TestCase(1, "Hidden/lilToonCutout")]
        [TestCase(2, "Hidden/lilToonTransparent")]
        [TestCase(3, "Hidden/lilToonTransparent")]
        public void TheBlendingModeChoosesTheLilToonShaderAndTheMainImageIsKept(int blendMode, string expected)
        {
            var source = Source(blendMode);
            source.SetFloat("_Cutoff", .3f);
            source.SetFloat("_Culling", 0);
            var flip = blendMode == 3 ? 1f : 0f;
            source.SetFloat("_FlipBackfaceNormals", flip);
            var output = Substitute(source, Name, out var route, out var dropped);
            Assert.That(output.GetFloat("_FlipNormal"), Is.EqualTo(flip), "back faces are flipped only when the source flips them");
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(source.GetTexture("_MainTex")));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo(0f));
            if (blendMode == 1) Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6f));
            Assert.That(dropped, Is.Empty);
        }

        [TestCase("Mochie/Standard Lite")]
        [TestCase("Mochie/Standard Mobile")]
        public void TheLiteAndMobileShadersAreReadTheSameWay(string shader)
        {
            var output = Substitute(Source(1), shader, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
        }

        // Mobile is built with STANDARD_MOBILE, which skips _AlphaSource, the
        // emission mask and the pulse.
        [Test] public void TheMobileShaderSkipsTheAlphaSourceAndTheEmissionMask()
        {
            var source = Source(2);
            source.SetFloat("_AlphaSource", 1);
            source.SetTexture("_AlphaMask", Solid("mask", Color.grey));
            source.EnableKeyword("_EMISSION_ON");
            source.SetColor("_EmissionColor", Color.white);
            source.SetTexture("_EmissionMask", Solid("emission mask", new Color(.5f, 0, 0, 1)));
            source.SetFloat("_EmissionPulseWave", 1);
            var output = Substitute(source, "Mochie/Standard Mobile", out _, out var dropped);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionBlendMask"), Is.Not.EqualTo(source.GetTexture("_EmissionMask")));
            Assert.That(dropped, Is.Empty);

            source.SetFloat("_AlphaSource", 2);
            Substitute(source, "Mochie/Standard Mobile", out _, out dropped);
            Assert.That(dropped, Does.Not.Contain("Vertex Color Alpha"));
        }

        [Test] public void TheAlphaMaskChannelReplacesTheAlphaAtItsOwnTiling()
        {
            var source = Source(2);
            source.SetTextureScale("_MainTex", new Vector2(2, 2));
            source.SetFloat("_AlphaSource", 1);
            source.SetTexture("_AlphaMask", Solid("mask", new Color(0, .25f, 0, 1)));
            source.SetTextureScale("_AlphaMask", new Vector2(4, 4));
            source.SetFloat("_AlphaMaskChannel", 1);
            source.SetFloat("_AlphaMaskOpacity", .5f);
            var textures = new List<Texture2D>();
            var output = Substitute(source, Name, out _, out _, textures);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(.5f).Within(1e-6f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(0f));
            Assert.That(output.GetTextureScale("_AlphaMask"), Is.EqualTo(new Vector2(2, 2)));
            var mask = output.GetTexture("_AlphaMask");
            Assert.That(textures, Does.Contain(mask));
            Assert.That(Center(mask), Is.EqualTo(.25f).Within(.01f));
        }

        [Test] public void AnEmptyAlphaMaskLeavesTheOpacity()
        {
            var source = Source(2);
            source.SetFloat("_AlphaSource", 1);
            source.SetFloat("_AlphaMaskOpacity", .4f);
            var output = Substitute(source, Name, out _, out var dropped);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_AlphaMask"), Is.Null);
            Assert.That(output.GetFloat("_AlphaMaskScale"), Is.EqualTo(.4f).Within(1e-6f));
            Assert.That(output.GetFloat("_AlphaMaskValue"), Is.EqualTo(0f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AnOpaqueMaterialIgnoresTheAlphaMask()
        {
            var source = Source(0);
            source.SetFloat("_AlphaSource", 1);
            source.SetTexture("_AlphaMask", Solid("mask", Color.grey));
            var output = Substitute(source, Name, out _, out _);
            Assert.That(output.GetFloat("_AlphaMaskMode"), Is.EqualTo(0f));
        }

        [Test] public void VertexColorAlphaIsReported()
        {
            var source = Source(2);
            source.SetFloat("_AlphaSource", 2);
            Substitute(source, Name, out _, out var dropped);
            Assert.That(dropped, Does.Contain("Vertex Color Alpha"));
        }

        [Test] public void TheNormalMapIsReadAtItsStrengthOnlyWithItsKeyword()
        {
            var source = Source();
            var normal = Own(new Texture2D(4, 4) { name = "normal" });
            source.SetTexture("_NormalMap", normal);
            source.SetFloat("_NormalStrength", .4f);
            var off = Substitute(source, Name, out _, out _);
            Assert.That(off.GetTexture("_BumpMap"), Is.Null);
            Assert.That(off.GetFloat("_UseBumpMap"), Is.EqualTo(0f));
            source.EnableKeyword("_NORMALMAP_ON");
            var on = Substitute(source, Name, out _, out _);
            Assert.That(on.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(on.GetFloat("_UseBumpMap"), Is.EqualTo(1f));
            Assert.That(on.GetFloat("_BumpScale"), Is.EqualTo(.4f).Within(1e-6f));
        }

        [Test] public void EmissionNeedsItsKeyword()
        {
            var source = Source();
            source.SetColor("_EmissionColor", Color.white);
            source.SetTexture("_EmissionMap", Own(new Texture2D(4, 4) { name = "emission" }));
            Assert.That(Substitute(source, Name, out _, out _).GetFloat("_UseEmission"), Is.EqualTo(0f));
        }

        // pow(linear color, 2.2) × strength, saved in gamma.
        [Test] public void TheEmissionColorIsRaisedTo2Point2AndMaskedByItsChannel()
        {
            var source = Source();
            source.EnableKeyword("_EMISSION_ON");
            var color = new Color(.5f, .25f, 1, 1);
            source.SetColor("_EmissionColor", color);
            source.SetFloat("_EmissionStrength", 2);
            var map = Own(new Texture2D(4, 4) { name = "emission" });
            source.SetTexture("_EmissionMap", map);
            source.SetTexture("_EmissionMask", Solid("mask", new Color(0, 0, .5f, 1)));
            source.SetFloat("_EmissionMaskChannel", 2);
            var textures = new List<Texture2D>();
            var output = Substitute(source, Name, out _, out var dropped, textures);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(map));
            var linear = output.GetColor("_EmissionColor").linear;
            Assert.That(linear.r, Is.EqualTo(Mathf.Pow(color.linear.r, 2.2f) * 2).Within(1e-3f));
            Assert.That(linear.g, Is.EqualTo(Mathf.Pow(color.linear.g, 2.2f) * 2).Within(1e-3f));
            Assert.That(linear.b, Is.EqualTo(2f).Within(1e-3f));
            var mask = output.GetTexture("_EmissionBlendMask");
            Assert.That(textures, Does.Contain(mask));
            Assert.That(Center(mask), Is.EqualTo(.5f).Within(.01f));
            Assert.That(dropped, Is.Empty);
        }

        // Mochie's saturation is lerp(luma, color, s): red at 0 becomes the
        // grey of its luma (0.3), where HSV's would stay at full brightness.
        [Test] public void FilteringIsBakedIntoTheMainImageWithMochiesFormula()
        {
            var source = Source();
            source.SetTexture("_MainTex", Solid("main", new Color(1, 0, 0, .5f)));
            source.SetTextureScale("_MainTex", new Vector2(2, 3));
            source.SetColor("_Color", new Color(1, 1, 1, .75f));
            source.SetFloat("_Filtering", 1);
            source.SetFloat("_Saturation", 0);
            var textures = new List<Texture2D>();
            var output = Substitute(source, Name, out _, out var dropped, textures);
            var main = (Texture2D)output.GetTexture("_MainTex");
            Assert.That(textures, Does.Contain(main));
            Assert.That(output.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(2, 3)));
            Assert.That(output.GetColor("_Color"), Is.EqualTo(new Color(1, 1, 1, .75f)));
            Assert.That(output.GetVector("_MainTexHSVG"), Is.EqualTo(new Vector4(0, 1, 1, 1)));
            var pixel = main.GetPixel(2, 2);
            Assert.That(pixel.a, Is.EqualTo(.5f).Within(.01f));
            var linear = pixel.linear;
            Assert.That(linear.r, Is.EqualTo(.3f).Within(.01f));
            Assert.That(linear.g, Is.EqualTo(.3f).Within(.01f));
            Assert.That(dropped, Is.Empty);

            // Hue in HSV, then saturation, contrast around 0.5 and brightness.
            source.SetFloat("_Hue", .2f);
            source.SetFloat("_Saturation", .5f);
            source.SetFloat("_Contrast", 1.2f);
            source.SetFloat("_Brightness", .8f);
            output = Substitute(source, Name, out _, out dropped, textures);
            linear = ((Texture2D)output.GetTexture("_MainTex")).GetPixel(2, 2).linear;
            var shifted = new Vector3(.8f, 1, 0);
            var luma = Vector3.Dot(shifted, new Vector3(.3f, .59f, .11f));
            var expected = (Vector3.one * .5f + (Vector3.Lerp(Vector3.one * luma, shifted, .5f) - Vector3.one * .5f) * 1.2f) * .8f;
            Assert.That(linear.r, Is.EqualTo(expected.x).Within(.01f));
            Assert.That(linear.g, Is.EqualTo(expected.y).Within(.01f));
            Assert.That(linear.b, Is.EqualTo(expected.z).Within(.01f));
            Assert.That(dropped, Is.Empty);
            Assert.That(dropped, Does.Not.Contain("Post Filtering"));

            // The lit color is filtered again by the Post values.
            source.SetFloat("_BrightnessPost", .5f);
            Substitute(source, Name, out _, out dropped, textures);
            Assert.That(dropped, Does.Contain("Post Filtering"));
        }

        [Test] public void FilteringWithoutAMainImageFiltersTheColor()
        {
            var source = Source();
            source.SetTexture("_MainTex", null);
            source.SetColor("_Color", new Color(0, 1, 0, 1));
            source.SetFloat("_Filtering", 1);
            source.SetFloat("_Saturation", 0);
            var output = Substitute(source, Name, out _, out _);
            var color = output.GetColor("_Color").linear;
            Assert.That(color.r, Is.EqualTo(.59f).Within(1e-3f));
            Assert.That(color.b, Is.EqualTo(.59f).Within(1e-3f));
        }

        [Test] public void FilteringOffLeavesTheColorAlone()
        {
            var source = Source();
            source.SetFloat("_Brightness", .5f);
            var output = Substitute(source, Name, out _, out _);
            Assert.That(output.GetVector("_MainTexHSVG"), Is.EqualTo(new Vector4(0, 1, 1, 1)));
        }

        [Test] public void TheSurfaceIsLitLikeUnityStandard()
        {
            var output = Substitute(Source(), Name, out _, out _);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.75f).Within(1e-6f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.5f).Within(1e-6f));
            Assert.That(output.GetColor("_ShadowColor"), Is.EqualTo(Color.black));
            Assert.That(output.GetFloat("_ShadowEnvStrength"), Is.EqualTo(1f));
        }

        [Test] public void MetallicAndRoughnessScalarsBecomeMetallicAndSmoothness()
        {
            var source = Source();
            source.SetFloat("_MetallicStrength", .5f);
            source.SetFloat("_RoughnessStrength", .3f);
            var output = Substitute(source, Name, out _, out _);
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(Mathf.LinearToGammaSpace(.5f)).Within(1e-5f));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(.7f).Within(1e-5f));
            Assert.That(output.GetFloat("_ApplyReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ApplySpecular"), Is.EqualTo(1f));
        }

        [Test] public void TheSmoothnessToggleReadsTheValueAsSmoothness()
        {
            var source = Source();
            source.SetFloat("_SmoothnessToggle", 1);
            source.SetFloat("_RoughnessStrength", .3f);
            Assert.That(Substitute(source, Name, out _, out _).GetFloat("_Smoothness"), Is.EqualTo(.3f).Within(1e-5f));
        }

        // smoothness = 1 − green × strength, baked with _Smoothness at 1.
        [Test] public void TheRoughnessMapIsBakedToSmoothness()
        {
            var source = Source();
            source.SetFloat("_SampleRoughness", 1);
            source.SetTexture("_RoughnessMap", Solid("roughness", new Color(0, .5f, 0, 1)));
            source.SetFloat("_RoughnessStrength", .6f);
            source.SetFloat("_SampleMetallic", 1);
            source.SetTexture("_MetallicMap", Solid("metallic", new Color(0, .4f, 0, 1)));
            source.SetFloat("_MetallicStrength", 1);
            var textures = new List<Texture2D>();
            var output = Substitute(source, Name, out _, out _, textures);
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(1f));
            Assert.That(Center(output.GetTexture("_SmoothnessTex")), Is.EqualTo(1 - .5f * .6f).Within(.01f));
            Assert.That(Center(output.GetTexture("_MetallicGlossMap")), Is.EqualTo(.4f).Within(.01f));
        }

        [Test] public void ThePackedMapIsReadByItsChannels()
        {
            var source = Source();
            source.EnableKeyword("_WORKFLOW_PACKED_ON");
            source.SetFloat("_PrimaryWorkflow", 1);
            source.SetTexture("_PackedMap", Solid("packed", new Color(1, .2f, .7f, 1)));
            source.SetFloat("_PackedOcclusionStrength", 0);
            var output = Substitute(source, Name, out _, out var dropped);
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(Center(output.GetTexture("_MetallicGlossMap")), Is.EqualTo(.7f).Within(.01f));
            Assert.That(Center(output.GetTexture("_SmoothnessTex")), Is.EqualTo(.8f).Within(.01f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void EffectsLilToonHasNoCounterpartForAreReported()
        {
            var source = Source();
            source.EnableKeyword("_PARALLAX_ON");
            source.EnableKeyword("_TRIPLANAR_ON");
            source.SetFloat("_WindToggle", 1);
            source.SetVector("_UVMainScroll", new Vector4(.1f, 0, 0, 0));
            Substitute(source, Name, out _, out var dropped);
            Assert.That(dropped, Is.SupersetOf(new[] { "Height Map", "Triplanar", "Wind", "Scroll Speed" }));
        }

        [Test] public void AMissingMochieShaderIsKnownByItsSavedValues()
        {
            var source = Source(1);
            source.EnableKeyword("_EMISSION_ON");
            source.SetColor("_EmissionColor", Color.white);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.HasProperty("_BlendMode"), Is.False);
            var output = Substitute(source, null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
        }

        [Test] public void OtherShadersWithTheSameNamesAreNotTakenForMochieStandard()
        {
            Substitute(Source(), Probe, out var route, out _);
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
