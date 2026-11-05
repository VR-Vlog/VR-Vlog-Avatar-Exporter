using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class UnlitWFApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/UnlitWFProbe";
        const string Opaque = "UnlitWF/WF_UnToon_Opaque";
        const string Cutout = "UnlitWF/WF_UnToon_TransCutout";
        const string OutlineOpaque = "UnlitWF/UnToon_Outline/WF_UnToon_Outline_Opaque";
        const string OutlineCutout = "UnlitWF/UnToon_Outline/WF_UnToon_Outline_TransCutout";
        const float Contrast = .85f;
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
            return Own(new Material(shader) { name = "WFSource" });
        }

        Material Substitute(Material source, string shader, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out _, out dropped, new List<Texture2D>());

        Material Substitute(Material source, string shader = Opaque) => Substitute(source, shader, out _);

        // Under -nographics no image is read back, so nothing is baked: the
        // shade keeps its color, and what needs a baked image is reported or left out.
        static bool NoGraphics => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        static void AssertColor(Color actual, float r, float g, float b, float a, float within = 1e-3f) =>
            Assert.That(new[] { actual.r, actual.g, actual.b, actual.a }, Is.EqualTo(new[] { r, g, b, a }).Within(within));

        [Test] public void OnlyTheUnToonFamilyMatches()
        {
            var values = new UnsupportedShaderSubstitution.SourceValues(Source());
            var approximation = new UnlitWFApproximation();
            Assert.That(approximation.Matches(Opaque, values), Is.True);
            Assert.That(approximation.Matches("UnlitWF/UnToon_Mobile/WF_UnToon_Mobile_Opaque", values), Is.True);
            Assert.That(approximation.Matches("UnlitWF/UnToon_PowerCap/WF_UnToon_PowerCap_Opaque", values), Is.True);
            Assert.That(approximation.Matches("UnlitWF/WF_FakeFur_Opaque", values), Is.False);
            Assert.That(approximation.Matches("UnlitWF/WF_Gem_Opaque", values), Is.False);
            Assert.That(approximation.Matches("Standard", values), Is.False);
        }

        [Test] public void AMissingShaderIsKnownByItsLightAndShadeSettings()
        {
            var values = new UnsupportedShaderSubstitution.SourceValues(Source());
            Assert.That(new UnlitWFApproximation().Matches(null, values), Is.True);
        }

        // The word in the name means the shader has no standard lighting, and it still takes the scene's light.
        [Test] public void TheMaterialStaysLit()
        {
            Assert.That(Substitute(Source()).GetFloat("_AsUnlit"), Is.EqualTo(0f));
        }

        [Test] public void TheLightMinimumIsReadInGammaAndTweaked()
        {
            var source = Source();
            source.SetFloat("_GL_LevelMin", .5f);
            source.SetFloat("_GL_LevelTweak", -.5f);
            source.SetFloat("_GL_BlendPower", .25f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_LightMinLimit"), Is.EqualTo(Mathf.GammaToLinearSpace(.25f)).Within(1e-4f));
            Assert.That(output.GetFloat("_LightMaxLimit"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MonochromeLighting"), Is.EqualTo(.75f).Within(1e-5f));
        }

        [Test] public void TheNormalMapFollowsItsSwitch()
        {
            var source = Source();
            source.SetTexture("_BumpMap", Image("normal", new Color(.5f, .5f, 1)));
            Assert.That(Substitute(source).GetFloat("_UseBumpMap"), Is.EqualTo(0f));
            source.SetFloat("_NM_Enable", 1);
            Assert.That(Substitute(source).GetFloat("_UseBumpMap"), Is.EqualTo(1f));
        }

        [Test] public void TheShadeIsOffUnlessItsSwitchIsOn()
        {
            Assert.That(Substitute(Source()).GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void EachShadeStepKeepsItsBorderAndColor()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetFloat("_TS_Steps", 3);
            source.SetFloat("_TS_1stBorder", .5f);
            source.SetFloat("_TS_1stFeather", .1f);
            source.SetFloat("_TS_3rdBorder", .15f);
            source.SetFloat("_TS_3rdFeather", .02f);
            var linear = new Color(.25f, .5f, .75f);
            source.SetColor("_TS_1stColor", linear.gamma);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.55f).Within(1e-5f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.1f).Within(1e-5f));
            Assert.That(output.GetFloat("_Shadow3rdBorder"), Is.EqualTo(.16f).Within(1e-5f));
            Assert.That(output.GetFloat("_Shadow3rdBlur"), Is.EqualTo(.02f).Within(1e-5f));
            var first = output.GetColor("_ShadowColor");
            Assert.That(first.linear.r, Is.EqualTo(1 + (.25f - 1) * Contrast).Within(.01f));
            Assert.That(first.linear.b, Is.EqualTo(1 + (.75f - 1) * Contrast).Within(.01f));
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(1f));
            Assert.That(output.GetColor("_Shadow3rdColor").a, Is.EqualTo(1f));
        }

        [Test] public void OneStepLeavesTheOtherStepsEmpty()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetFloat("_TS_Steps", 1);
            var output = Substitute(source);
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(0f));
            Assert.That(output.GetColor("_Shadow3rdColor").a, Is.EqualTo(0f));
        }

        // The base color is what the step is measured against, so a matching step does nothing.
        [Test] public void AStepEqualToTheBaseColorLeavesTheColor()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetColor("_TS_BaseColor", new Color(.8f, .6f, .4f));
            source.SetColor("_TS_1stColor", new Color(.8f, .6f, .4f));
            var color = Substitute(source).GetColor("_ShadowColor");
            AssertColor(color, 1, 1, 1, 1, .01f);
        }

        [Test] public void AShadeImageIsMultipliedIntoTheMainImage()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetFloat("_TS_Power", 1 / Contrast);
            source.SetColor("_TS_1stColor", Color.white);
            source.SetTexture("_MainTex", Image("main", new Color(1, 1, 1, 1)));
            source.SetTexture("_TS_1stTex", Image("shade", new Color(.5f, 1, 1, 1)));
            var output = Substitute(source);
            var image = output.GetTexture("_ShadowColorTex") as Texture2D;
            if (NoGraphics) { Assert.That(image, Is.Null); return; }
            Assert.That(image, Is.Not.Null);
            // The image holds the color itself, read in the same space as the main image.
            Assert.That(image.GetPixel(0, 0).r, Is.EqualTo(.5f).Within(.02f));
            Assert.That(image.GetPixel(0, 0).g, Is.EqualTo(1f).Within(.02f));
        }

        [Test] public void TheSdfShadeIsReported()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetFloat("_TS_MaskType", 1);
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, Does.Contain("Shade SDF"));
        }

        [Test] public void TheEmissionColorDoesNotShowWithoutItsSwitch()
        {
            var source = Source();
            source.SetColor("_EmissionColor", Color.red);
            Assert.That(Substitute(source).GetFloat("_UseEmission"), Is.EqualTo(0f));
        }

        [Test] public void TheEmissionAdds()
        {
            var source = Source();
            source.SetFloat("_ES_Enable", 1);
            source.SetColor("_EmissionColor", Color.red);
            source.SetTexture("_EmissionMap", Image("glow", new Color(1, 1, 1)));
            source.SetFloat("_ES_TintBaseCol", .5f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_EmissionBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_EmissionMainStrength"), Is.EqualTo(.5f));
            Assert.That(output.GetTexture("_EmissionMap").name, Is.EqualTo("glow"));
        }

        [Test] public void TheRimIsOffUnlessItsSwitchIsOn()
        {
            Assert.That(Substitute(Source()).GetFloat("_UseRim"), Is.EqualTo(0f));
        }

        // Width 0.2 and feather 0.2 over a direction of 1 + (0.5 + 1) / 2: the
        // ramp is 0.615 to 0.965 in sin, measured here in 1 - cos.
        [Test] public void TheRimRampLandsOnTheSameAngle()
        {
            var source = Source();
            source.SetFloat("_TR_Enable", 1);
            source.SetFloat("_TR_Width", .2f);
            source.SetFloat("_TR_Feather", .2f);
            var output = Substitute(source);
            float Ramp(float sine) => 1 - Mathf.Sqrt(1 - sine * sine);
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(Ramp((.615f + .965f) / 2)).Within(1e-3f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(Ramp(.965f) - Ramp(.615f)).Within(1e-3f));
        }

        [Test] public void TheDefaultRimAddsWhatIsAboveMedianGray()
        {
            var source = Source();
            source.SetFloat("_TR_Enable", 1);
            source.SetColor("_TR_Color", Color.white);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_RimColor").linear.r, Is.EqualTo(1 - Mathf.GammaToLinearSpace(.5f)).Within(.01f));
        }

        [Test] public void TheRimBlendTypesAreKept()
        {
            var source = Source();
            source.SetFloat("_TR_Enable", 1);
            source.SetFloat("_TR_BlendType", 1);
            Assert.That(Substitute(source).GetFloat("_RimBlendMode"), Is.EqualTo(0f));
            source.SetFloat("_TR_BlendType", 2);
            source.SetColor("_TR_Color", new Color(.3f, .3f, .3f));
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_RimColor").r, Is.EqualTo(.3f).Within(.01f));
            Assert.That(dropped, Does.Not.Contain("Rim Light (Darkening)"));
        }

        [Test] public void ARimThatDarkensIsReported()
        {
            var source = Source();
            source.SetFloat("_TR_Enable", 1);
            source.SetColor("_TR_Color", new Color(.1f, .1f, .1f));
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, Does.Contain("Rim Light (Darkening)"));
        }

        [Test] public void AnAddedMatcapKeepsItsImageAndTint()
        {
            var source = Source();
            source.SetFloat("_HL_Enable", 1);
            source.SetFloat("_HL_CapType", 1);
            source.SetFloat("_HL_Power", .5f);
            source.SetTexture("_HL_MatcapTex", Image("cap", Color.white));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.5f));
            Assert.That(output.GetTexture("_MatCapTex").name, Is.EqualTo("cap"));
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(0f));
        }

        [Test] public void AMultipliedMatcapMultiplies()
        {
            var source = Source();
            source.SetFloat("_HL_Enable", 1);
            source.SetFloat("_HL_CapType", 2);
            source.SetTexture("_HL_MatcapTex", Image("cap", Color.white));
            Assert.That(Substitute(source).GetFloat("_MatCapBlendMode"), Is.EqualTo(3f));
        }

        [Test] public void AMedianMatcapLightensAndDarkensInTwoLayers()
        {
            var source = Source();
            source.SetFloat("_HL_Enable", 1);
            source.SetTexture("_HL_MatcapTex", Image("cap", new Color(.1f, .1f, .1f)));
            var output = Substitute(source, Opaque, out var dropped);
            if (NoGraphics) { Assert.That(dropped, Does.Contain("Matcap")); return; }
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(1f));
            Assert.That(((Texture2D)output.GetTexture("_MatCapTex")).GetPixel(0, 0).r, Is.EqualTo(0f).Within(.02f));
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCap2ndBlendMode"), Is.EqualTo(3f));
            Assert.That(((Texture2D)output.GetTexture("_MatCap2ndTex")).GetPixel(0, 0).r, Is.LessThan(.95f));
            Assert.That(dropped, Does.Not.Contain("Matcap Darkening"));
        }

        [Test] public void ALightMedianMatcapNeedsNoSecondLayer()
        {
            var source = Source();
            source.SetFloat("_HL_Enable", 1);
            source.SetTexture("_HL_MatcapTex", Image("cap", Color.white));
            Assert.That(Substitute(source).GetFloat("_UseMatCap2nd"), Is.EqualTo(0f));
        }

        [Test] public void TheDarkeningIsReportedWhenTheSecondMatcapIsTaken()
        {
            var source = Source();
            source.SetFloat("_HL_Enable", 1);
            source.SetTexture("_HL_MatcapTex", Image("cap", new Color(.1f, .1f, .1f)));
            source.SetFloat("_HL_Enable_1", 1);
            source.SetFloat("_HL_CapType_1", 1);
            source.SetTexture("_HL_MatcapTex_1", Image("second", Color.white));
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetTexture("_MatCap2ndTex").name, Is.EqualTo("second"));
            // Without a graphics device the first matcap, which needs a baked image, is left out.
            Assert.That(dropped, Does.Contain(NoGraphics ? "Matcap" : "Matcap Darkening"));
        }

        [Test] public void TheMatcapsLilToonHasNoRoomForAreReported()
        {
            var source = Source();
            source.SetFloat("_HL_Enable_2", 1);
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, Does.Contain("Extra Matcaps"));
        }

        [Test] public void TheSecondTextureIsALayer()
        {
            var source = Source();
            source.SetFloat("_TX2_Enable", 1);
            source.SetTexture("_TX2_MainTex", Image("second", Color.white));
            source.SetColor("_TX2_Color", new Color(.2f, .4f, .6f, .8f));
            source.SetFloat("_TX2_BlendType", 2);
            source.SetFloat("_TX2_UVType", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_Main2ndTex").name, Is.EqualTo("second"));
            AssertColor(output.GetColor("_Color2nd"), .2f, .4f, .6f, .8f);
            Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(3f));
            Assert.That(output.GetFloat("_Main2ndTex_UVMode"), Is.EqualTo(1f));
        }

        [Test] public void TheOverlayIsALayerAndFollowsTheMatcapDirection()
        {
            var source = Source();
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            source.SetFloat("_OVL_UVType", 4);
            source.SetFloat("_OVL_BlendType", 1);
            source.SetFloat("_OVL_Power", .5f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMain3rdTex"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Main3rdTex_UVMode"), Is.EqualTo(4f));
            Assert.That(output.GetFloat("_Main3rdTexBlendMode"), Is.EqualTo(1f));
            Assert.That(output.GetColor("_Color3rd").a, Is.EqualTo(.5f).Within(1e-5f));
        }

        // _Time.x in the source, _Time.y in lilToon.
        [Test] public void TheOverlayScrollsAtTheSameSpeed()
        {
            var source = Source();
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            source.SetVector("_OVL_UVScroll", new Vector4(1, 2, 0, 0));
            var scroll = Substitute(source).GetVector("_Main3rdTex_ScrollRotate");
            Assert.That(scroll.x, Is.EqualTo(.05f).Within(1e-5f));
            Assert.That(scroll.y, Is.EqualTo(.1f).Within(1e-5f));
        }

        [Test] public void AHalfAddedOverlayIsAddedAndReported()
        {
            var source = Source();
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            source.SetFloat("_OVL_BlendType", 3);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_Main3rdTexBlendMode"), Is.EqualTo(1f));
            Assert.That(dropped, Does.Contain("Overlay Blend"));
        }

        [Test] public void AnOverlayOverABakedShadeIsReported()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetTexture("_TS_1stTex", Image("shade", Color.grey));
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, NoGraphics ? Does.Not.Contain("Layers In Shade") : Does.Contain("Layers In Shade"));
        }

        [Test] public void AnOverlayOverOnlyTheSecondBakedShadeIsReported()
        {
            var source = Source();
            source.SetFloat("_TS_Enable", 1);
            source.SetFloat("_TS_Steps", 2);
            source.SetTexture("_TS_2ndTex", Image("shade", Color.grey));
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            if (NoGraphics)
            {
                Assert.That(output.GetTexture("_Shadow2ndColorTex"), Is.Null);
                Assert.That(dropped, Does.Not.Contain("Layers In Shade"));
                return;
            }
            Assert.That(output.GetTexture("_Shadow2ndColorTex"), Is.Not.Null);
            Assert.That(dropped, Does.Contain("Layers In Shade"));
        }

        [Test] public void AScrollingOverlayCutAtTheEdgeIsReported()
        {
            var source = Source();
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            source.SetFloat("_OVL_OutUVType", 1);
            source.SetVector("_OVL_UVScroll", new Vector4(1, 0, 0, 0));
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_Main3rdTexIsDecal"), Is.EqualTo(1f));
            Assert.That(dropped, Does.Contain("Overlay Scroll"));
        }

        [Test] public void AnOverlayFromTheSkyIsReported()
        {
            var source = Source();
            source.SetFloat("_OVL_Enable", 1);
            source.SetTexture("_OVL_OverlayTex", Image("overlay", Color.white));
            source.SetFloat("_OVL_UVType", 2);
            var output = Substitute(source, Opaque, out var dropped);
            Assert.That(output.GetFloat("_UseMain3rdTex"), Is.EqualTo(0f));
            Assert.That(dropped, Does.Contain("Overlay Texture"));
        }

        [Test] public void TheOutlineIsOffUnlessItsSwitchIsOn()
        {
            Assert.That(Substitute(Source(), OutlineOpaque).shader.name, Does.Not.Contain("Outline"));
        }

        [Test] public void TheOutlineKeepsItsWidthAndColor()
        {
            var source = Source();
            source.SetFloat("_TL_Enable", 1);
            source.SetFloat("_TL_LineWidth", .4f);
            source.SetColor("_TL_LineColor", new Color(.1f, .2f, .3f));
            var output = Substitute(source, OutlineOpaque);
            Assert.That(output.shader.name, Does.Contain("Outline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.4f).Within(1e-5f));
            Assert.That(output.GetFloat("_OutlineFixWidth"), Is.EqualTo(0f));
            AssertColor(output.GetColor("_OutlineColor"), .1f, .2f, .3f, 1);
        }

        [Test] public void ACutoutOutlineHasTheSameHoles()
        {
            var source = Source();
            source.renderQueue = 2450;
            source.SetFloat("_TL_Enable", 1);
            source.SetTexture("_MainTex", Image("main", new Color(1, 0, 0, .25f)));
            source.SetColor("_TL_LineColor", new Color(0, 0, 1));
            var image = Substitute(source, OutlineCutout, out var dropped).GetTexture("_OutlineTex") as Texture2D;
            if (NoGraphics) { Assert.That(image, Is.Null); Assert.That(dropped, Does.Contain("Outline Color Image")); return; }
            Assert.That(image, Is.Not.Null);
            AssertColor(image.GetPixel(0, 0), 0, 0, 1, .25f, .02f);
        }

        [Test] public void TheOutlineCanMixInTheMainImage()
        {
            var source = Source();
            source.SetFloat("_TL_Enable", 1);
            source.SetFloat("_TL_BlendBase", 1);
            source.SetTexture("_MainTex", Image("main", new Color(1, 0, 0, 1)));
            source.SetColor("_TL_LineColor", new Color(0, 0, 1));
            var image = Substitute(source, OutlineOpaque, out var dropped).GetTexture("_OutlineTex") as Texture2D;
            if (NoGraphics) { Assert.That(image, Is.Null); Assert.That(dropped, Does.Contain("Outline Color Image")); return; }
            AssertColor(image.GetPixel(0, 0), 1, 0, 0, 1, .02f);
        }

        [Test] public void EffectsLilToonDoesNotDrawAreReported()
        {
            var source = Source();
            source.SetFloat("_DSV_Enable", 1);
            source.SetFloat("_LME_Enable", 1);
            Substitute(source, Opaque, out var dropped);
            Assert.That(dropped, Does.Contain("Dissolve"));
            Assert.That(dropped, Does.Contain("Lame"));
        }

        [Test] public void TheFamilyGoesThroughTheApproximationAndTheCutoutStaysCutout()
        {
            var source = Source();
            source.SetFloat("_Cutoff", .3f);
            source.renderQueue = 2450;
            var output = TestObjects.Substitute(owned, source, Cutout, out _, out var route, out _, new List<Texture2D>());
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonCutout"));
        }

        [Test] public void OtherShadersOfTheVendorStayWithTheGenericMapping()
        {
            var source = Source();
            TestObjects.Substitute(owned, source, "UnlitWF/WF_FakeFur_Opaque", out _, out var route, out _, new List<Texture2D>());
            Assert.That(route, Is.Not.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }
    }
}
