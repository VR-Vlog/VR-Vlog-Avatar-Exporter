using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class XsToonApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/XsToonProbe";
        const string Plain = "Xiexe/Toon3/Standard";
        const string Outlined = "Xiexe/Toon3/Standard_Outlined";
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

        Material Source(int shadowType = 2)
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "XsToonSource" });
            source.SetTexture("_MainTex", Image("main"));
            source.SetFloat("_ShadowType", shadowType);
            return source;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, new List<Texture2D>());

        Material Substitute(Material source, out IReadOnlyList<string> dropped) => Substitute(source, Plain, out _, out dropped);

        Material Substitute(Material source) => Substitute(source, out _);

        [TestCase(0, "lilToon")]
        [TestCase(1, "Hidden/lilToonCutout")]
        [TestCase(2, "Hidden/lilToonCutout")]
        [TestCase(3, "Hidden/lilToonCutout")]
        [TestCase(4, "Hidden/lilToonTransparent")]
        [TestCase(5, "Hidden/lilToonTransparent")]
        [TestCase(6, "Hidden/lilToonTransparent")]
        public void TheBlendModeChoosesTheLilToonShader(int blend, string expected)
        {
            var source = Source();
            var main = source.GetTexture("_MainTex");
            source.SetFloat("_BlendMode", blend);
            source.SetFloat("_Cutoff", .3f);
            var output = Substitute(source, Plain, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Is.EqualTo(expected));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            if (expected == "Hidden/lilToonCutout") Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-5f));
        }

        [Test] public void AMissingShaderIsFoundByItsSavedValues()
        {
            Substitute(Source(), null, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
        }

        [Test] public void BackFacesFlipTheNormal()
        {
            Assert.That(Substitute(Source()).GetFloat("_FlipNormal"), Is.EqualTo(1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TheCullingIsKept(int culling)
        {
            var source = Source();
            source.SetFloat("_Culling", culling);
            Assert.That(Substitute(source).GetFloat("_Cull"), Is.EqualTo(culling));
        }

        // The fade is range ± (1 - sharpness).
        [Test] public void TheSolidColorShadeMultipliesTheAlbedo()
        {
            var source = Source();
            source.SetFloat("_ShadowRange", .4f);
            source.SetFloat("_ShadowSharpness", .8f);
            source.SetColor("_ShadowColor", new Color(.5f, .6f, .7f));
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.4f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.4f).Within(1e-4f));
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            Assert.That(output.GetColor("_ShadowColor").g, Is.EqualTo(.6f).Within(1e-4f));
        }

        [Test] public void TheShadeMapReplacesTheAlbedo()
        {
            var source = Source(1);
            var image = Image("shade");
            source.SetTexture("_ShadeMap", image);
            source.SetColor("_ShadowColor", new Color(.5f, .6f, .7f));
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(image));
            Assert.That(output.GetColor("_ShadowColor").b, Is.EqualTo(.7f).Within(1e-4f));
        }

        [Test] public void TheShadeMapWithoutAnImageIsTheTint()
        {
            var image = Substitute(Source(1)).GetTexture("_ShadowColorTex") as Texture2D;
            Assert.That(image, Is.Not.Null);
            Assert.That(image, Is.Not.SameAs(Texture2D.whiteTexture));
        }

        // A white ramp does not shade at all.
        [Test] public void AnEmptyRampLeavesTheShadeOff()
        {
            Assert.That(Substitute(Source(0)).GetFloat("_UseShadow"), Is.EqualTo(0));
        }

        [Test] public void TheColorAdjustmentCarriesOver()
        {
            var source = Source();
            source.SetFloat("_Hue", .75f);
            source.SetFloat("_Saturation", 1.5f);
            source.SetFloat("_Value", .5f);
            var hsvg = Substitute(source).GetVector("_MainTexHSVG");
            Assert.That(hsvg.x, Is.EqualTo(-.25f).Within(1e-4f));
            Assert.That(hsvg.y, Is.EqualTo(1.5f).Within(1e-4f));
            Assert.That(hsvg.z, Is.EqualTo(.5f).Within(1e-4f));
        }

        // The source adjusts image × color; lilToon adjusts the image and then
        // multiplies by the color. Value would count twice, so only hue and
        // saturation move the color.
        [Test] public void TheColorAdjustmentAlsoMovesTheBaseColor()
        {
            var source = Source();
            source.SetColor("_Color", new Color(1, 0, 0, .5f));
            source.SetFloat("_Hue", 1f / 3);
            source.SetFloat("_Value", .5f);
            var color = Substitute(source).GetColor("_Color");
            Assert.That(color.r, Is.EqualTo(0).Within(1e-3f));
            Assert.That(color.g, Is.EqualTo(1).Within(1e-3f));
            Assert.That(color.b, Is.EqualTo(0).Within(1e-3f));
            Assert.That(color.a, Is.EqualTo(.5f).Within(1e-3f));
        }

        [Test] public void AMaskedColorAdjustmentLeavesTheBaseColor()
        {
            var source = Source();
            source.SetColor("_Color", new Color(1, 0, 0));
            source.SetFloat("_Hue", 1f / 3);
            source.SetTexture("_HSVMask", Image("mask"));
            Assert.That(Substitute(source).GetColor("_Color").r, Is.EqualTo(1).Within(1e-3f));
        }

        [Test] public void AShadeMapWithAlphaOrTilingIsHandled()
        {
            var source = Source();
            source.SetFloat("_ShadowType", 1);
            var image = Image("shade", .5f);
            source.SetTexture("_ShadeMap", image);
            source.SetTextureScale("_ShadeMap", new Vector2(2, 2));
            var output = Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("Shade Map Tiling"));
            var drawn = output.GetTexture("_ShadowColorTex");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Assert.That(drawn, Is.SameAs(image)); return; }
            Assert.That(((Texture2D)drawn).GetPixel(0, 0).a, Is.EqualTo(1).Within(1e-3f));
        }

        [Test] public void ARampSelectionMaskIsReported()
        {
            var source = Source();
            source.SetFloat("_ShadowType", 0);
            source.SetTexture("_RampSelectionMask", Image("selection"));
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("Ramp Selection"));
        }

        [Test] public void TheSecondShadeUsesTheSameLightSettings()
        {
            var output = Substitute(Source());
            Assert.That(output.GetFloat("_Shadow2ndNormalStrength"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_Shadow2ndReceive"), Is.EqualTo(1));
        }

        [TestCase(1f, -.5f)]
        [TestCase(.1f, 1f)]
        [TestCase(2f, -.6f)]
        public void TheRimsDirectionMatchesTheThreshold(float threshold, float range)
        {
            var source = Source();
            source.SetFloat("_RimIntensity", 1);
            source.SetFloat("_RimThreshold", threshold);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_RimDirStrength"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimDirRange"), Is.EqualTo(range).Within(1e-3f));
        }

        [Test] public void ARimThresholdOfZeroHasNoDirection()
        {
            var source = Source();
            source.SetFloat("_RimIntensity", 1);
            source.SetFloat("_RimThreshold", 0);
            Assert.That(Substitute(source).GetFloat("_RimDirStrength"), Is.EqualTo(0));
        }

        [Test] public void TheRimIsOffAtZeroIntensity()
        {
            Assert.That(Substitute(Source()).GetFloat("_UseRim"), Is.EqualTo(0));
        }

        [Test] public void TheRimFadesOverItsRange()
        {
            var source = Source();
            source.SetFloat("_RimIntensity", 1);
            source.SetFloat("_RimRange", .6f);
            source.SetFloat("_RimSharpness", .2f);
            source.SetFloat("_RimAlbedoTint", .5f);
            source.SetFloat("_RimThreshold", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.6f).Within(1e-4f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.4f).Within(1e-4f));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimMainStrength"), Is.EqualTo(.5f).Within(1e-4f));
            Assert.That(output.GetFloat("_RimDirStrength"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimDirRange"), Is.EqualTo(-.5f).Within(1e-4f));
            Assert.That(output.GetColor("_RimIndirColor").a, Is.EqualTo(0));
        }

        [TestCase(0, 1, .25f, 1)]
        [TestCase(1, 3, .5f, 0)]
        public void TheMatcapReflectionAddsOrMultiplies(int blend, int expected, float strength, int lighting)
        {
            var source = Source();
            var image = Image("matcap");
            source.SetFloat("_ReflectionMode", 2);
            source.SetFloat("_ReflectionBlendMode", blend);
            source.SetFloat("_Reflectivity", .5f);
            source.SetTexture("_Matcap", image);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(image));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(expected));
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(strength).Within(1e-4f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(lighting));
        }

        [Test] public void OtherReflectionsAreReported()
        {
            var source = Source();
            source.SetFloat("_ReflectionMode", 0);
            var output = Substitute(source, out var dropped);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(0));
            Assert.That(dropped, Does.Contain("Reflection"));
        }

        // Both shaders declare the width, and the plain one has no outline.
        [Test] public void OnlyTheOutlinedShaderHasAnOutline()
        {
            var source = Source();
            source.SetFloat("_OutlineWidth", 2);
            Assert.That(Substitute(source).shader.name, Does.Not.Contain("Outline"));
            var output = Substitute(source, Outlined, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.shader.name, Does.Contain("Outline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(2).Within(1e-4f));
        }

        [Test] public void TheOutlineTakesItsColorAndTheAlbedoTint()
        {
            var source = Source();
            source.SetFloat("_OutlineWidth", 1);
            source.SetColor("_OutlineColor", new Color(.2f, .3f, .4f));
            source.SetFloat("_OutlineAlbedoTint", 1);
            source.SetColor("_Color", new Color(.5f, .5f, .5f));
            var output = Substitute(source, Outlined, out _, out _);
            Assert.That(output.GetColor("_OutlineColor").g, Is.EqualTo(.15f).Within(1e-4f));
            Assert.That(output.GetColor("_OutlineColor").a, Is.EqualTo(1));
            Assert.That(output.GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_MainTex")));
        }

        [Test] public void WhatLilToonCannotShowIsReported()
        {
            var source = Source();
            source.SetFloat("_BlendMode", 6);
            source.SetFloat("_SpecularIntensity", 1);
            source.SetFloat("_HalftoneType", 0);
            source.SetFloat("_UseClipsForDissolve", 1);
            source.SetColor("_ShadowRim", Color.gray);
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("Additive Blend").And.Contain("Specular").And.Contain("Halftones").And.Contain("Dissolve").And.Contain("Shadow Rim"));
        }
    }
}
