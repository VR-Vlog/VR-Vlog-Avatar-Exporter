using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // A toon shader built of switchable sections is mapped section by section
    // onto official lilToon (SectionedToonProbe declares its property names).
    public sealed class SectionedToonSubstitutionTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        Material Probe(string name = "Toon") => Own(new Material(Shader.Find("Hidden/VRVlogTests/SectionedToonProbe")) { name = name });

        Material Substitute(Material source, out bool colorSkipped, out IReadOnlyList<string> dropped)
        {
            var output = UnsupportedShaderSubstitution.Create(source, out colorSkipped, out var toon, out dropped);
            Assert.That(output, Is.Not.Null);
            Assert.That(toon, Is.True, "The sections are mapped one by one.");
            return Own(output);
        }

        Material Substitute(Material source) => Substitute(source, out _, out _);

        static void AssertColor(Material output, string name, Color expected) =>
            Assert.That(Vector4.Distance(output.GetColor(name), expected), Is.LessThan(1e-4f), name + " " + output.GetColor(name).ToString("F4"));

        [Test] public void FlatLightingCastsNoShadowAndKeepsTheBrightnessLimit()
        {
            var output = Substitute(Probe(), out var colorSkipped, out var dropped);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_LightMaxLimit"), Is.EqualTo(1f).Within(1e-6));
            Assert.That(output.GetFloat("_LightMinLimit"), Is.EqualTo(0f).Within(1e-6));
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(0f));
            Assert.That(colorSkipped, Is.False);
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AnUnlimitedBrightnessUsesLilToonsUpperBound()
        {
            var source = Probe(); source.SetFloat("_LightingCapEnabled", 0);
            Assert.That(Substitute(source).GetFloat("_LightMaxLimit"), Is.EqualTo(10f).Within(1e-6));
        }

        [Test] public void AShadeMapStepBecomesASingleShadowWithoutTheDefaultSecondLayer()
        {
            var source = Probe();
            source.SetColor("_Color", new Color(1, .5f, .5f, 1));
            source.SetFloat("_LightingMode", 4);
            source.SetFloat("_BaseColor_Step", .6f); source.SetFloat("_BaseShade_Feather", .2f);
            source.SetColor("_1st_ShadeColor", new Color(.5f, .25f, .25f, 1));

            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(1e-6), "The middle of the feathered edge.");
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.2f).Within(1e-6));
            AssertColor(output, "_ShadowColor", new Color(.5f, .5f, .5f, 1));
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(1f).Within(1e-6));
            AssertColor(output, "_Shadow2ndColor", new Color(0, 0, 0, 0));
            AssertColor(output, "_ShadowBorderColor", new Color(0, 0, 0, 1));
        }

        [Test] public void AMultiplyingShadeMapKeepsItsColorAndMultipliesTheBase()
        {
            var source = Probe();
            source.SetFloat("_LightingMode", 4); source.SetFloat("_ShadingShadeMapBlendType", 1);
            source.SetFloat("_Use_BaseAs1st", 1);
            source.SetColor("_1st_ShadeColor", new Color(.6f, .5f, .7f, 1));

            var output = Substitute(source);
            AssertColor(output, "_ShadowColor", new Color(.6f, .5f, .7f, 1));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(1f).Within(1e-6));
        }

        [Test] public void LightingSwitchedOffCastsNoShadow()
        {
            var source = Probe(); source.SetFloat("_LightingMode", 4); source.SetFloat("_ShadingEnabled", 0);
            Assert.That(Substitute(source).GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void SkinLightingCastsNoShadowAndIsReported()
        {
            var source = Probe(); source.SetFloat("_LightingMode", 3);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(0f));
            Assert.That(dropped, Is.EqualTo(new[] { "Lighting Type: Skin" }));
        }

        [Test] public void AShadowLitByTheAmbientLightIsReported()
        {
            var source = Probe(); source.SetFloat("_LightingMode", 1); source.SetFloat("_LightingIgnoreAmbientColor", 0);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(dropped, Is.EqualTo(new[] { "Indirect Shadow Color" }));
        }

        [Test] public void AWrappedGradientPlacesTheEdgeBetweenItsEnds()
        {
            var source = Probe();
            source.SetFloat("_LightingMode", 2);
            source.SetFloat("_LightingGradientStart", .2f); source.SetFloat("_LightingGradientEnd", .6f);

            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.6f).Within(1e-6));
            Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.4f).Within(1e-6));
            AssertColor(output, "_ShadowColor", new Color(0, 0, 0, 1));
        }

        [Test] public void ABlendedPresetThatClipsKeepsItsCutoff()
        {
            var source = Probe(); source.SetFloat("_Mode", 9); source.SetFloat("_Cutoff", .3f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(output.GetFloat("_Cutoff"), Is.EqualTo(.3f).Within(1e-6));
        }

        [Test] public void TheColorAdjustSectionBecomesTheToneCorrection()
        {
            var source = Probe();
            source.SetFloat("_MainColorAdjustToggle", 1);
            source.SetFloat("_Saturation", .5f); source.SetFloat("_MainBrightness", -.5f); source.SetFloat("_MainGamma", 1.2f);
            source.SetFloat("_MainHueShiftToggle", 1); source.SetFloat("_MainHueShift", .75f);

            var output = Substitute(source, out var colorSkipped, out _);
            Assert.That(Vector4.Distance(output.GetVector("_MainTexHSVG"), new Vector4(-.25f, 1.5f, .5f, 1.2f)), Is.LessThan(1e-5f),
                output.GetVector("_MainTexHSVG").ToString("F4"));
            Assert.That(colorSkipped, Is.False);
        }

        [Test] public void AnAnimatedHueShiftIsReportedAsLeftOut()
        {
            var source = Probe();
            source.SetFloat("_MainColorAdjustToggle", 1);
            source.SetFloat("_MainHueShiftToggle", 1); source.SetFloat("_MainHueShift", .2f); source.SetFloat("_MainHueShiftSpeed", 1);

            var output = Substitute(source, out var colorSkipped, out _);
            Assert.That(output.GetVector("_MainTexHSVG").x, Is.EqualTo(0f));
            Assert.That(colorSkipped, Is.True);
        }

        [Test] public void TheStrongestMatcapBlendPicksLilToonsMode()
        {
            var matcap = Own(new Texture2D(4, 4) { name = "matcap" });
            var source = Probe();
            source.SetFloat("_MatcapEnable", 1); source.SetTexture("_Matcap", matcap);
            source.SetFloat("_MatcapReplace", 0); source.SetFloat("_MatcapMultiply", .8f); source.SetFloat("_MatcapAdd", .3f);
            source.SetFloat("_MatcapSmoothness", .5f); source.SetColor("_MatcapColor", new Color(.8f, .6f, .4f, 1));

            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(matcap));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(3f), "Multiply.");
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.8f).Within(1e-6));
            AssertColor(output, "_MatCapColor", new Color(.8f, .6f, .4f, 1));
            Assert.That(output.GetTextureScale("_MatCapTex"), Is.EqualTo(Vector2.one));
            Assert.That(output.GetFloat("_MatCapLod"), Is.EqualTo(1f).Within(1e-5), "Half of the two mip levels of a 4 px image.");
        }

        [Test] public void RimWidthAndSharpnessBecomeLilToonsBorderAndBlur()
        {
            var source = Probe(); source.SetFloat("_EnableRimLighting", 1);
            var output = Substitute(source);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            // Width .8 is .79 on N·V and the sharpness .65 starts the edge at .14.
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.535f).Within(1e-5));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.65f).Within(1e-5));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(1f).Within(1e-6));
            Assert.That(output.GetFloat("_RimBlendMode"), Is.EqualTo(1f), "Add.");
            Assert.That(output.GetFloat("_RimEnableLighting"), Is.EqualTo(1f).Within(1e-6));
            AssertColor(output, "_RimColor", Color.white);
        }

        [Test] public void AnOutlinedSourceUsesTheOutlineShaderWithItsWidthAndColor()
        {
            var source = Probe(); source.SetFloat("_EnableOutlines", 1); source.SetFloat("_LineWidth", .3f);
            var output = Substitute(source);
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.3f).Within(1e-6));
            Assert.That(output.GetFloat("_OutlineFixWidth"), Is.EqualTo(.5f).Within(1e-6));
            AssertColor(output, "_OutlineColor", new Color(.79f, .77f, .87f, 1));
        }

        [Test] public void StencilSettingsKeepTheirMeaningUnderLilToonsNames()
        {
            var source = Probe();
            source.SetFloat("_StencilRef", 3); source.SetFloat("_StencilCompareFunction", 3); source.SetFloat("_StencilPassOp", 2);
            var output = Substitute(source);
            Assert.That(Value(output, "_StencilRef"), Is.EqualTo(3f));
            Assert.That(Value(output, "_StencilComp"), Is.EqualTo(3f));
            Assert.That(Value(output, "_StencilPass"), Is.EqualTo(2f));
        }

        static float Value(Material material, string name)
        {
            var index = material.shader.FindPropertyIndex(name);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
            return material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Int ? material.GetInteger(name) : material.GetFloat(name);
        }

        [Test] public void ApplyNamesTheMappedMaterialsAndTheSectionsLeftOut()
        {
            var toon = Probe(); toon.SetFloat("_EnableDissolve", 1); toon.SetFloat("_GlitterEnable", 1);
            var plain = Own(new Material(Shader.Find("Standard")) { name = "Plain" });
            var avatar = Own(new GameObject("avatar"));
            foreach (var (material, index) in new[] { toon, plain }.Select((material, index) => (material, index)))
            {
                var part = GameObject.CreatePrimitive(PrimitiveType.Cube); part.name = "part" + index;
                part.transform.SetParent(avatar.transform, false);
                part.GetComponent<Renderer>().sharedMaterial = material;
            }
            var created = new List<Material>(); var warnings = new List<string>();
            UnsupportedShaderSubstitution.Apply(avatar, created, warnings, new ExportFallbackReport(), null);
            owned.AddRange(created);
            Assert.That(warnings, Is.EqualTo(new[]
            {
                ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Plain",
                ExporterLocalization.T(UnsupportedShaderSubstitution.ToonMapped) + "Toon",
                ExporterLocalization.T(UnsupportedShaderSubstitution.DroppedEffects) + "Toon (Dissolve, Glitter)",
            }));
        }
    }
}
