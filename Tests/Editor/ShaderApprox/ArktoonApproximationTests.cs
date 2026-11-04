using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ArktoonApproximationTests
    {
        const string Probe = "Hidden/VRVlogTests/ArktoonProbe";
        const string Name = "arktoon/Opaque";
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }

        Texture2D Image(string name) => Own(new Texture2D(4, 4) { name = name });

        Material Source()
        {
            var shader = Shader.Find(Probe);
            Assume.That(shader, Is.Not.Null);
            var source = Own(new Material(shader) { name = "ArktoonSource" });
            source.SetTexture("_MainTex", Image("main"));
            return source;
        }

        Material Substitute(Material source, string shader, out UnsupportedShaderSubstitution.Route route, out IReadOnlyList<string> dropped) =>
            TestObjects.Substitute(owned, source, shader, out _, out route, out dropped, new List<Texture2D>());

        Material Substitute(Material source, out IReadOnlyList<string> dropped) => Substitute(source, Name, out _, out dropped);

        Material Substitute(Material source) => Substitute(source, out _);

        static void AssertLinearGray(Material material, string name, float expected)
        {
            var color = material.GetColor(name).linear;
            Assert.That(color.r, Is.EqualTo(expected).Within(1e-3f), name);
            Assert.That(color.g, Is.EqualTo(expected).Within(1e-3f), name);
            Assert.That(color.b, Is.EqualTo(expected).Within(1e-3f), name);
        }

        [TestCase("arktoon/Opaque")]
        [TestCase(null)]
        public void TheShaderIsFoundByNameOrSavedValues(string name)
        {
            var source = Source();
            var main = source.GetTexture("_MainTex");
            var output = Substitute(source, name, out var route, out _);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Approximated));
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
        }

        [Test] public void DoubleSidedBackFacesKeepTheirFlipAndLight()
        {
            var source = Source();
            source.SetFloat("_UseDoubleSided", 1);
            source.SetFloat("_DoubleSidedFlipBackfaceNormal", 1);
            source.SetFloat("_DoubleSidedBackfaceLightIntensity", .25f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_FlipNormal"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_BackfaceForceShadow"), Is.EqualTo(.75f).Within(1e-4f));
        }

        // The shade falls toward the indirect intensity by the strength.
        [Test] public void TheShadeKeepsItsBorderBlurAndStrength()
        {
            var source = Source();
            source.SetFloat("_Shadowborder", .7f); source.SetFloat("_ShadowborderBlur", .2f);
            source.SetFloat("_ShadowStrength", .8f); source.SetFloat("_ShadowIndirectIntensity", .3f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.7f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.2f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(.8f).Within(1e-4f));
            Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0));
            AssertLinearGray(output, "_ShadowColor", .3f);
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(0));
        }

        [Test] public void PlanBTakesItsImageAndTint()
        {
            var source = Source();
            var image = Image("custom");
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_ShadowPlanBDefaultShadowMix", 0);
            source.SetFloat("_ShadowPlanBUseCustomShadowTexture", 1);
            source.SetTexture("_ShadowPlanBCustomShadowTexture", image);
            source.SetColor("_ShadowPlanBCustomShadowTextureRGB", new Color(.5f, .5f, .5f));
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(image));
            Assert.That(output.GetColor("_ShadowColor").r, Is.EqualTo(.5f).Within(1e-3f));
        }

        // The slot of the custom image is black when empty.
        [Test] public void PlanBWithAnEmptyImageIsBlack()
        {
            var source = Source();
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_ShadowPlanBUseCustomShadowTexture", 1);
            AssertLinearGray(Substitute(source), "_ShadowColor", 0);
        }

        [Test] public void PlanBScalesTheBaseByItsValue()
        {
            var source = Source();
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_ShadowPlanBDefaultShadowMix", 0);
            source.SetFloat("_ShadowPlanBValueFromBase", .4f);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.Null);
            AssertLinearGray(output, "_ShadowColor", .4f);
        }

        [Test] public void PlanBMixDarkensTowardTheDefaultShade()
        {
            var source = Source();
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_ShadowPlanBDefaultShadowMix", 1);
            source.SetFloat("_ShadowStrength", 1); source.SetFloat("_ShadowIndirectIntensity", .25f);
            AssertLinearGray(Substitute(source), "_ShadowColor", .25f);
        }

        // Without a graphics device the shift is reported instead of baked.
        [Test] public void PlanBShiftBakesTheBaseOrIsReported()
        {
            var source = Source();
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_ShadowPlanBHueShiftFromBase", .25f);
            var output = Substitute(source, out var dropped);
            var baked = output.GetTexture("_ShadowColorTex");
            if (baked == null) Assert.That(dropped, Does.Contain("Plan B Color Shift"));
            else Assert.That(baked, Is.Not.SameAs(source.GetTexture("_MainTex")));
        }

        // Its border is relative to the first one.
        [Test] public void TheSecondShadeIsPlacedInsideTheFirst()
        {
            var source = Source();
            var image = Image("second");
            source.SetFloat("_Shadowborder", .6f);
            source.SetFloat("_ShadowPlanBUsePlanB", 1);
            source.SetFloat("_CustomShadow2nd", 1);
            source.SetFloat("_ShadowPlanB2border", .5f); source.SetFloat("_ShadowPlanB2borderBlur", .1f);
            source.SetFloat("_ShadowPlanB2UseCustomShadowTexture", 1);
            source.SetTexture("_ShadowPlanB2CustomShadowTexture", image);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_Shadow2ndBorder"), Is.EqualTo(.3f).Within(1e-4f));
            Assert.That(output.GetFloat("_Shadow2ndBlur"), Is.EqualTo(.1f).Within(1e-4f));
            Assert.That(output.GetTexture("_Shadow2ndColorTex"), Is.SameAs(image));
            Assert.That(output.GetColor("_Shadow2ndColor").a, Is.EqualTo(1));
        }

        [Test] public void TheRimAddsWithItsPowerAndImage()
        {
            var source = Source();
            var image = Image("rim");
            source.SetFloat("_UseRim", 1);
            source.SetFloat("_RimFresnelPower", 4);
            source.SetTexture("_RimTexture", image);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(4));
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.5f));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(1));
            Assert.That(output.GetTexture("_RimColorTex"), Is.SameAs(image));
        }

        [Test] public void TheRimCanTakeTheBaseColor()
        {
            var source = Source();
            source.SetFloat("_UseRim", 1);
            source.SetFloat("_RimUseBaseTexture", 1);
            Assert.That(Substitute(source).GetFloat("_RimMainStrength"), Is.EqualTo(1));
        }

        [TestCase(0, 1)]
        [TestCase(1, 2)]
        [TestCase(2, 2)]
        public void TheMatcapModesMap(int mode, int expected)
        {
            var source = Source();
            var image = Image("matcap");
            source.SetFloat("_MatcapBlendMode", mode);
            source.SetTexture("_MatcapTexture", image);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(image));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(expected));
        }

        [Test] public void TheUnusedMatcapModeStaysOff()
        {
            var source = Source();
            source.SetTexture("_MatcapTexture", Image("matcap"));
            Assert.That(Substitute(source).GetFloat("_UseMatCap"), Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void TheShadowCapMultipliesAsTheSecondMatcap(int mode)
        {
            var source = Source();
            var image = Image("cap");
            source.SetFloat("_ShadowCapBlendMode", mode);
            source.SetTexture("_ShadowCapTexture", image);
            source.SetFloat("_ShadowCapBlend", .5f);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap2nd"), Is.EqualTo(1));
            Assert.That(output.GetTexture("_MatCap2ndTex"), Is.SameAs(image));
            Assert.That(output.GetFloat("_MatCap2ndBlendMode"), Is.EqualTo(3));
            Assert.That(output.GetFloat("_MatCap2ndBlend"), Is.EqualTo(.5f).Within(1e-4f));
        }

        [TestCase(0f, 1f, false)]
        [TestCase(1f, 0f, false)]
        [TestCase(1f, .5f, true)]
        public void TheOutlineFollowsItsSwitchAndWidth(float use, float width, bool outlined)
        {
            var source = Source();
            source.SetFloat("_UseOutline", use);
            source.SetFloat("_OutlineWidth", width);
            var output = Substitute(source);
            Assert.That(output.shader.name.Contains("Outline"), Is.EqualTo(outlined));
            if (outlined) Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(width).Within(1e-4f));
        }

        [Test] public void TheOutlineRateTakesTheBase()
        {
            var source = Source();
            source.SetFloat("_UseOutline", 1);
            source.SetFloat("_OutlineWidth", .1f);
            source.SetFloat("_OutlineTextureColorRate", 1);
            source.SetColor("_Color", new Color(.2f, .4f, .6f));
            source.SetColor("_OutlineColor", Color.black);
            var output = Substitute(source);
            Assert.That(output.GetTexture("_OutlineTex"), Is.SameAs(source.GetTexture("_MainTex")));
            Assert.That(output.GetColor("_OutlineColor").b, Is.EqualTo(.6f).Within(1e-3f));
        }

        [Test] public void WhatLilToonCannotShowIsReported()
        {
            var source = Source();
            source.SetFloat("_UseGloss", 1);
            source.SetFloat("_UseReflection", 1);
            source.SetFloat("_UseEmissionParallax", 1);
            source.SetFloat("_ShadowUseStep", 1);
            Substitute(source, out var dropped);
            Assert.That(dropped, Does.Contain("Gloss").And.Contain("Reflection").And.Contain("Emission Parallax").And.Contain("Shadow Steps"));
        }
    }
}
