using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class SunaoApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/SunaoProbe";
        const string Opaque = "Sunao Shader/Opaque";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Texture2D Image(string name, Color color)
        {
            var image = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name });
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = color;
            image.SetPixels(pixels);
            image.Apply();
            return image;
        }

        Material Source()
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            return Own(new Material(shader) { name = "SunaoSource" });
        }

        Material Substitute(Material source, string shader, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out _, out dropped, new List<Texture2D>());

        Material Substitute(Material source) => Substitute(source, Opaque, out _);

        [TestCase(0, "lilToon")]
        [TestCase(1, "Hidden/lilToonTransparent")]
        [TestCase(2, "Hidden/lilToonCutout")]
        [TestCase(3, "lilToon")]
        [TestCase(4, "Hidden/lilToonTransparent")]
        [TestCase(5, "Hidden/lilToonCutout")]
        [TestCase(6, "Hidden/lilToonCutout")]
        [TestCase(7, "Hidden/lilToonCutout")]
        public void TheVariantChoosesTheLilToonShader(int type, string expected)
        {
            var source = Source();
            source.SetFloat("_SunaoShaderType", type);
            var output = TestObjects.Substitute(owned, source, Opaque, out _, out var route, out _, new List<Texture2D>());
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
        }

        [Test] public void TheStencilVariantsReportTheStencil()
        {
            var source = Source();
            source.SetFloat("_SunaoShaderType", 3);
            Substitute(source, "Sunao Shader/[Stencil Outline]/Opaque", out var dropped);
            Assert.That(dropped, Does.Contain("Stencil"));
        }

        [Test] public void AMissingShaderIsKnownByItsSavedValues()
        {
            var source = Source();
            source.SetFloat("_SunaoShaderType", 2);
            source.shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(source.HasProperty("_SunaoShaderType"), Is.False);
            var output = TestObjects.Substitute(owned, source, null, out _, out var route, out _, new List<Texture2D>());
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
        }

        [Test] public void OtherShadersAreNotTakenForSunao()
        {
            var values = new UnsupportedShaderSubstitution.SourceValues(Source());
            Assert.That(new SunaoApproximation().Matches("Standard", values), Is.False);
            Assert.That(new SunaoApproximation().Matches("Sunao Shader/Cutout", values), Is.True);
        }

        // Diffuse = saturate((N·L - 0.5) × 1 + 1.5 - 1) goes from 0 at N·L = 0
        // to 1 at N·L = 1: half-Lambert 0.5 to 1, center 0.75, width 0.5.
        [Test] public void TheShadeRampLandsOnTheSameLightDirections()
        {
            var source = Source();
            source.SetFloat("_Shade", .8f);
            source.SetFloat("_ShadeWidth", 1);
            source.SetFloat("_ShadeGradient", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(.8f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.75f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test] public void AnUnlitMaterialFadesTheShade()
        {
            var source = Source();
            source.SetFloat("_Shade", .8f);
            source.SetFloat("_Unlit", .5f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(.4f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0f));
        }

        [Test] public void NoShadeTurnsTheShadowOff()
        {
            var source = Source();
            source.SetFloat("_Shade", 0);
            Assert.That(Substitute(source).GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void TwoToonStepsSharpenTheEdgeAndMoreAreReported()
        {
            var source = Source();
            source.SetFloat("_ToonEnable", 1);
            source.SetFloat("_ShadeGradient", 1);
            var sharp = Substitute(source, Opaque, out var dropped);
            Assert.That(sharp.GetFloat("_ShadowBlur"), Is.LessThan(.01f));
            Assert.That(dropped, Does.Not.Contain("Toon Steps"));

            source.SetFloat("_Toon", 5);
            var smooth = Substitute(source, Opaque, out dropped);
            Assert.That(smooth.GetFloat("_ShadowBlur"), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(dropped, Does.Contain("Toon Steps"));
        }

        [Test] public void AnOpaqueCustomShadeColorIsTheShadowColor()
        {
            var source = Source();
            source.SetTexture("_MainTex", Image("main", Color.white));
            source.SetColor("_CustomShadeColor", new Color(.4f, .2f, .7f, 1));
            var output = Substitute(source);
            Assert.That(output.GetColor("_ShadowColor").b, Is.EqualTo(.7f).Within(.01f));
            Assert.That(output.GetColor("_ShadowColor").r, Is.EqualTo(.4f).Within(.01f));
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(0f));
        }

        // saturate(c × 3 - 1.5) × _ShadeColor × c on the linear values of the
        // image: a bright channel stays, a dark one goes to black.
        [Test] public void TheDefaultShadeColorIsBakedFromTheMainImage()
        {
            var source = Source();
            var texture = Image("main", new Color(1, .1f, 1, 1));
            source.SetTexture("_MainTex", texture);
            source.SetFloat("_ShadeColor", .5f);
            var output = Substitute(source);
            var baked = output.GetTexture("_ShadowColorTex") as Texture2D;
            Assert.That(baked, Is.Not.Null);
            Assert.That(output.GetColor("_ShadowColor"), Is.EqualTo(Color.white));
            var pixel = baked.GetPixel(0, 0);
            Assert.That(pixel.g, Is.EqualTo(0).Within(.01f));
            Assert.That(pixel.r, Is.EqualTo(new Color(.5f, 0, 0).gamma.r).Within(.01f));
            Assert.That(pixel.a, Is.EqualTo(1).Within(.01f));
        }

        [Test] public void WithoutAMainImageTheShadeIsOneColor()
        {
            var source = Source();
            source.SetFloat("_ShadeColor", .5f);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            Assert.That(output.GetColor("_ShadowColor").r, Is.EqualTo(new Color(.5f, 0, 0).gamma.r).Within(.01f));
        }

        [Test] public void TheLightSettingsAreCopied()
        {
            var source = Source();
            source.SetFloat("_Unlit", .4f);
            source.SetFloat("_MonochromeLit", 1);
            source.SetFloat("_MinimumLight", .2f);
            source.SetFloat("_UVScrollX", .5f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_AsUnlit"), Is.EqualTo(.4f).Within(1e-5f));
            Assert.That(output.GetFloat("_MonochromeLighting"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_LightMinLimit"), Is.EqualTo(.2f).Within(1e-5f));
            Assert.That(output.GetFloat("_LightMaxLimit"), Is.EqualTo(1f));
            Assert.That(output.GetVector("_MainTex_ScrollRotate").x, Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test] public void WithoutTheLimiterTheLightIsNotLimited()
        {
            var source = Source();
            source.SetFloat("_LightLimitter", 0);
            Assert.That(Substitute(source).GetFloat("_LightMaxLimit"), Is.EqualTo(10f));
        }

        [Test] public void AnEmissionImageDoesNotGlowWhileTheEmissionIsOff()
        {
            var source = Source();
            source.SetTexture("_EmissionMap", Image("glow", Color.white));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(0f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.Null);
        }

        [Test] public void TheEmissionStrengthScalesTheColorAndTheMapTakesTheMainTiling()
        {
            var source = Source();
            source.SetFloat("_EmissionEnable", 1);
            source.SetTexture("_MainTex", Image("main", Color.white));
            source.SetTextureScale("_MainTex", new Vector2(2, 2));
            var map = Image("glow", Color.white);
            source.SetTexture("_EmissionMap", map);
            source.SetTextureScale("_EmissionMap", new Vector2(3, 1));
            source.SetColor("_EmissionColor", new Color(.5f, .5f, .5f, 1));
            source.SetFloat("_Emission", 2);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_EmissionColor").r, Is.EqualTo((new Color(.5f, .5f, .5f).linear * 2).gamma.r).Within(1e-4f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(map));
            Assert.That(output.GetTextureScale("_EmissionMap"), Is.EqualTo(new Vector2(6, 2)));
        }

        [TestCase(0, 0f, Mathf.PI / 2)]
        [TestCase(3, 1f, Mathf.PI)]
        public void TheBlinkKeepsItsSpeedAndPhase(int waveform, float type, float phase)
        {
            var source = Source();
            source.SetFloat("_EmissionEnable", 1);
            source.SetFloat("_EmissionWaveform", waveform);
            source.SetFloat("_EmissionBlink", .5f);
            source.SetFloat("_EmissionFrequency", 2);
            var blink = Substitute(source).GetVector("_EmissionBlink");
            Assert.That(blink.x, Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(blink.y, Is.EqualTo(type));
            Assert.That(blink.z, Is.EqualTo(4 * Mathf.PI).Within(1e-4f));
            Assert.That(blink.w, Is.EqualTo(phase).Within(1e-4f));
        }

        [TestCase(1, "Emission (Multiply)")]
        [TestCase(2, "Emission (Minus)")]
        public void EmissionModesLilToonDoesNotDrawAreReported(int mode, string label)
        {
            var source = Source();
            source.SetFloat("_EmissionEnable", 1);
            source.SetFloat("_EmissionMode", mode);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain(label));
        }

        // A ramp of slope 8 around 0.5, moved down by 1 - 0.75: it starts at
        // 0.46875, stops at 0.5625 with the height 0.75.
        [Test] public void TheRimRampLandsOnTheSameAngle()
        {
            var source = Source();
            source.SetFloat("_RimLitEnable", 1);
            source.SetFloat("_RimLit", .75f);
            source.SetFloat("_RimLitGradient", 2);
            source.SetColor("_RimLitColor", new Color(.1f, .2f, .3f, .5f));
            source.SetFloat("_RimLitTexColor", 1);
            source.SetFloat("_RimLitLighthing", 0);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(2f));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.515625f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.09375f).Within(1e-5f));
            var rim = output.GetColor("_RimColor");
            Assert.That(new[] { rim.r, rim.g, rim.b, rim.a }, Is.EqualTo(new[] { .1f, .2f, .3f, .375f }).Within(1e-5f));
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimMainStrength"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimEnableLighting"), Is.EqualTo(0f));
        }

        [Test] public void NoRimPowerMeansNoRim()
        {
            var source = Source();
            source.SetFloat("_RimLitEnable", 1);
            source.SetFloat("_RimLit", 0);
            Assert.That(Substitute(source).GetFloat("_UseRim"), Is.EqualTo(0f));
        }

        // Above 1 the source starts lifted and reaches 1 at x = 0.5; lilToon's
        // ramp goes from 0 at the center to the same point.
        [Test] public void ARimAboveOneLiftsTheSurface()
        {
            var source = Source();
            source.SetFloat("_RimLitEnable", 1);
            source.SetFloat("_RimLit", 1.5f);
            source.SetFloat("_RimLitGradient", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.25f).Within(1e-5f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.5f).Within(1e-5f));
        }

        [Test] public void ARimThatMultipliesIsReported()
        {
            var source = Source();
            source.SetFloat("_RimLitEnable", 1);
            source.SetFloat("_RimLitMode", 1);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Rim Light (Multiply or Minus)"));
        }

        [Test] public void TheMatcapIsOnlyDrawnUnderTheReflectionSwitch()
        {
            var source = Source();
            var matcap = Image("matcap", Color.white);
            source.SetTexture("_MatCap", matcap);
            Assert.That(Substitute(source).GetFloat("_UseMatCap"), Is.EqualTo(0f));

            source.SetFloat("_ReflectionEnable", 1);
            source.SetFloat("_GlossMapScale", .25f);
            source.SetFloat("_MatCapTexColor", 1);
            source.SetFloat("_MatCapLit", 0);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(matcap));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapMainStrength"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_MatCapLod"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Reflection"));
        }

        [Test] public void TheMatcapMaskIsTheMetallicMapUnlessItsOwnIsChosen()
        {
            var source = Source();
            source.SetFloat("_ReflectionEnable", 1);
            source.SetTexture("_MatCap", Image("matcap", Color.white));
            var metallic = Image("metallic", Color.grey);
            var own = Image("own", Color.white);
            source.SetTexture("_MetallicGlossMap", metallic);
            source.SetTexture("_MatCapMask", own);
            Assert.That(Substitute(source).GetTexture("_MatCapBlendMask"), Is.SameAs(metallic));
            source.SetFloat("_MatCapMaskEnable", 0);
            Assert.That(Substitute(source).GetTexture("_MatCapBlendMask"), Is.SameAs(own));
        }

        [Test] public void TheMatcapBlurComesFromTheMaskAlpha()
        {
            var source = Source();
            source.SetFloat("_ReflectionEnable", 1);
            source.SetTexture("_MatCap", Image("matcap", Color.white));
            source.SetTexture("_MetallicGlossMap", Image("metallic", new Color(1, 1, 1, .5f)));
            Assert.That(Substitute(source).GetFloat("_MatCapLod"), Is.EqualTo(1.5f).Within(.02f));
        }

        // _Bright scales the linear color: 0.8 gives the factor saturate(2.4 - 1.5) × 0.5.
        [Test] public void BrightScalesTheLinearColorOfTheShade()
        {
            var source = Source();
            source.SetFloat("_Bright", .8f);
            source.SetFloat("_ShadeColor", .5f);
            source.SetColor("_Color", Color.white);
            var shade = Substitute(source).GetColor("_ShadowColor").linear;
            Assert.That(shade.r, Is.EqualTo(.45f).Within(.01f));
        }

        // A square decal of size 0.5 centered at (0.75, 0.5), unrotated: its
        // left edge at u = 0.5 must sample 0, so 0.5 × 2 + T = 0.
        [Test] public void TheDecalIsPlacedOnTheSecondLayer()
        {
            var source = Source();
            var decal = Image("decal", Color.white);
            source.SetFloat("_DecalEnable", 1);
            source.SetTexture("_DecalTex", decal);
            source.SetFloat("_DecalPosX", .75f);
            source.SetFloat("_DecalPosY", .5f);
            source.SetFloat("_DecalSizeX", .5f);
            source.SetFloat("_DecalSizeY", .5f);
            source.SetFloat("_DecalMode", 2);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_Main2ndTex"), Is.SameAs(decal));
            Assert.That(output.GetTextureScale("_Main2ndTex"), Is.EqualTo(new Vector2(2, 2)));
            Assert.That(output.GetTextureOffset("_Main2ndTex").x, Is.EqualTo(-1).Within(1e-5f));
            Assert.That(output.GetTextureOffset("_Main2ndTex").y, Is.EqualTo(-.5f).Within(1e-5f));
            Assert.That(output.GetFloat("_Main2ndTexIsDecal"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(3f));
        }

        // lilToon draws the baked shade image in place of the color, so the
        // decal on the second layer is gone in the shade.
        [Test] public void ADecalOverABakedShadeIsReported()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Baking the shade needs a graphics device.");
            var source = Source();
            source.SetFloat("_DecalEnable", 1);
            source.SetTexture("_DecalTex", Image("decal", Color.white));
            var flat = Substitute(source, Opaque, out var dropped);
            Assert.That(flat.GetTexture("_ShadowColorTex"), Is.Null);
            Assert.That(dropped, Does.Not.Contain("Layers In Shade"), "Without a main image the shade is one color over the layers.");
            source.SetTexture("_MainTex", Image("main", new Color(1, .1f, 1, 1)));
            var baked = Substitute(source, Opaque, out dropped);
            Assert.That(baked.GetTexture("_ShadowColorTex"), Is.Not.Null);
            Assert.That(dropped, Does.Contain("Layers In Shade"));
            source.SetFloat("_DecalEnable", 0);
            Substitute(source, Opaque, out dropped);
            Assert.That(dropped, Does.Not.Contain("Layers In Shade"));
        }

        [Test] public void AMirroredDecalIsReported()
        {
            var source = Source();
            source.SetFloat("_DecalEnable", 1);
            source.SetTexture("_DecalTex", Image("decal", Color.white));
            source.SetFloat("_DecalMirror", 1);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Decal"));
        }

        [TestCase(0, .3f)]
        [TestCase(1, 3f)]
        public void TheOutlineKeepsItsWidthInCentimeters(int fixScale, float expected)
        {
            var source = Source();
            source.SetFloat("_OutLineEnable", 1);
            source.SetFloat("_OutLineSize", .3f);
            source.SetFloat("_OutLineFixScale", fixScale);
            source.SetColor("_OutLineColor", Color.red);
            source.SetFloat("_OutLineLighthing", 0);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(expected).Within(1e-5f));
            Assert.That(output.GetColor("_OutlineColor"), Is.EqualTo(Color.red));
            Assert.That(output.GetFloat("_OutlineEnableLighting"), Is.EqualTo(0f));
        }

        [Test] public void ABlendedOutlineTakesTheMainAlpha()
        {
            var source = Source();
            source.SetFloat("_SunaoShaderType", 1);
            source.SetFloat("_OutLineEnable", 1);
            source.SetTexture("_MainTex", Image("main", new Color(1, 0, 0, .25f)));
            var image = Substitute(source).GetTexture("_OutlineTex") as Texture2D;
            Assert.That(image, Is.Not.Null);
            Assert.That(image.GetPixel(0, 0).a, Is.EqualTo(.25f).Within(.01f));
            Assert.That(image.GetPixel(0, 0).g, Is.EqualTo(1f).Within(.01f));
        }

        [Test] public void AnOpaqueOutlineCanTakeTheMainColor()
        {
            var source = Source();
            source.SetFloat("_OutLineEnable", 1);
            source.SetFloat("_OutLineTexColor", 1);
            source.SetTexture("_MainTex", Image("main", new Color(1, 0, 0, 1)));
            var image = Substitute(source).GetTexture("_OutlineTex") as Texture2D;
            Assert.That(image.GetPixel(0, 0).r, Is.EqualTo(1f).Within(.01f));
            Assert.That(image.GetPixel(0, 0).g, Is.EqualTo(0f).Within(.01f));
        }

        [Test] public void TheOutlineIsOffUnlessItsSwitchIsOn()
        {
            Assert.That(Substitute(Source()).shader.name, Is.EqualTo("lilToon"));
        }

        [Test] public void EffectsLilToonDoesNotDrawAreReported()
        {
            var source = Source();
            source.SetFloat("_VertexColor", 1);
            source.SetFloat("_ParallaxEnable", 1);
            source.SetFloat("_UVAnimation", 1);
            source.SetTexture("_OcclusionMap", Image("occlusion", Color.white));
            source.SetFloat("_SHLight", .5f);
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, Is.SupersetOf(new[] { "Vertex Color", "Parallax Emission", "UV Animation", "Occlusion", "Light Strength" }));
        }

        [Test] public void DefaultValuesReportNothing()
        {
            Substitute(Source(), Opaque, out var dropped);
            Assert.That(dropped, Is.Empty);
        }
    }
}
