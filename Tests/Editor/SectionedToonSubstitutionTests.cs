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
            var textures = new List<Texture2D>();
            var output = UnsupportedShaderSubstitution.Create(source, out colorSkipped, out var toon, out dropped, textures);
            owned.AddRange(textures);
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

            var output = Substitute(source, out var colorSkipped, out var dropped);
            Assert.That(Vector4.Distance(output.GetVector("_MainTexHSVG"), new Vector4(-.25f, 1.5f, .5f, 1.2f)), Is.LessThan(1e-5f),
                output.GetVector("_MainTexHSVG").ToString("F4"));
            Assert.That(colorSkipped, Is.False);
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AnAnimatedHueShiftIsReportedByName()
        {
            var source = Probe();
            source.SetFloat("_MainColorAdjustToggle", 1);
            source.SetFloat("_MainHueShiftToggle", 1); source.SetFloat("_MainHueShift", .2f); source.SetFloat("_MainHueShiftSpeed", 1);

            var output = Substitute(source, out var colorSkipped, out var dropped);
            Assert.That(output.GetVector("_MainTexHSVG").x, Is.EqualTo(0f));
            Assert.That(colorSkipped, Is.False, "The generic notice would say hue and saturation are not kept.");
            Assert.That(dropped, Is.EqualTo(new[] { "Hue Shift" }));
        }

        [Test] public void AdjustingColorsUnderATintedMainColorIsReported()
        {
            var source = Probe();
            source.SetColor("_Color", new Color(1, .5f, .5f, 1));
            source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_Saturation", .5f);

            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetVector("_MainTexHSVG").y, Is.EqualTo(1.5f).Within(1e-6));
            Assert.That(dropped, Is.EqualTo(new[] { "Color Adjust with Main Color" }));
        }

        [Test] public void OnlyBrightnessUnderATintedMainColorIsNotReported()
        {
            var source = Probe();
            source.SetColor("_Color", new Color(1, .5f, .5f, 1));
            source.SetFloat("_MainColorAdjustToggle", 1); source.SetFloat("_MainBrightness", -.25f);

            Substitute(source, out _, out var dropped);
            Assert.That(dropped, Is.Empty, "Scaling the brightness and multiplying the color commute.");
        }

        [Test] public void MultiplyingShadeLayersThatNeedTheBaseDifferentlyAreReported()
        {
            var map = Own(new Texture2D(4, 4) { name = "shade" });
            var source = Probe();
            source.SetFloat("_LightingMode", 4); source.SetFloat("_ShadingShadeMapBlendType", 1);
            source.SetTexture("_1st_ShadeMap", map); source.SetFloat("_ShadeColor_Step", .3f);

            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetTexture("_ShadowColorTex"), Is.SameAs(map));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(1f).Within(1e-6));
            Assert.That(dropped, Is.EqualTo(new[] { "Multiplied Shadow Layers" }));
        }

        Texture2D Mask(Color color)
        {
            var mask = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "mask" });
            mask.SetPixels(Enumerable.Repeat(color, 16).ToArray());
            mask.Apply(false);
            return mask;
        }

        static void AssertGreyMask(Texture texture, Func<int, float> expected)
        {
            Assert.That(texture, Is.InstanceOf<Texture2D>());
            var pixels = ((Texture2D)texture).GetPixels();
            for (var i = 0; i < pixels.Length; i++)
                Assert.That(Vector4.Distance(pixels[i], new Color(expected(i), expected(i), expected(i), 1)), Is.LessThan(1.5f / 255), "Pixel " + i);
        }

        [Test] public void AGreyRedChannelMaskIsCopied()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Reading the mask needs a graphics device.");
            var mask = Mask(new Color(.5f, .5f, .5f, 1));
            var source = Probe(); source.SetFloat("_EnableRimLighting", 1); source.SetTexture("_RimMask", mask);

            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetTexture("_RimColorTex"), Is.SameAs(mask));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AColoredRedChannelMaskBecomesAGreyMaskInsteadOfTintingTheRim()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Reading the mask needs a graphics device.");
            var mask = Mask(new Color(1, 0, .5f, 1));
            var source = Probe(); source.SetFloat("_EnableRimLighting", 1); source.SetTexture("_RimMask", mask);

            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetTexture("_RimColorTex"), Is.Not.SameAs(mask), "lilToon multiplies the rim by the whole RGBA.");
            AssertGreyMask(output.GetTexture("_RimColorTex"), _ => 1);
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AnInvertedGreenChannelMaskKeepsAReplacingMatcapToItsArea()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) Assert.Ignore("Reading the mask needs a graphics device.");
            // Linear like a mask imported as data, so the stored values are the sampled ones.
            var mask = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "mask" });
            mask.SetPixels(Enumerable.Range(0, 16).Select(i => new Color(.9f, i / 15f, .1f, .3f)).ToArray());
            mask.Apply(false);
            var source = Probe();
            source.SetFloat("_MatcapEnable", 1); source.SetFloat("_MatcapReplace", 1);
            source.SetTexture("_MatcapMask", mask); source.SetFloat("_MatcapMaskChannel", 1); source.SetFloat("_MatcapMaskInvert", 1);
            source.SetTextureScale("_MatcapMask", new Vector2(2, 3)); source.SetTextureOffset("_MatcapMask", new Vector2(.25f, .5f));

            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapBlendMask"), Is.Not.SameAs(mask));
            AssertGreyMask(output.GetTexture("_MatCapBlendMask"), i => 1 - mask.GetPixel(i % 4, i / 4).g);
            Assert.That(output.GetTextureScale("_MatCapBlendMask"), Is.EqualTo(new Vector2(2, 3)));
            Assert.That(output.GetTextureOffset("_MatCapBlendMask"), Is.EqualTo(new Vector2(.25f, .5f)));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AMaskThatCannotBeBakedTurnsTheMatcapOff()
        {
            var source = Probe();
            source.SetFloat("_MatcapEnable", 1); source.SetFloat("_MatcapReplace", 1);
            source.SetTexture("_MatcapMask", Mask(Color.white)); source.SetFloat("_MatcapMaskChannel", 1);

            // Without a list to own the baked image, as when a graphics device is missing.
            var output = Own(UnsupportedShaderSubstitution.Create(source, out _, out _, out var dropped));
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(0f), "Unmasked, the matcap would cover the whole material.");
            Assert.That(dropped, Is.EqualTo(new[] { "Matcap Mask" }));
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

        Texture2D Solid(Color color, int size = 4)
        {
            var texture = Own(new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "solid" });
            texture.SetPixels(Enumerable.Repeat(color, size * size).ToArray());
            texture.Apply(false);
            return texture;
        }

        static void Decal(Material source, int index, Color color, int blend = 0, bool tiled = true)
        {
            var s = index == 0 ? "" : index.ToString();
            source.SetFloat("_DecalEnabled" + s, 1);
            source.SetColor("_DecalColor" + s, color);
            source.SetFloat("_DecalBlendType" + s, blend);
            source.SetFloat("_DecalTiled" + s, tiled ? 1 : 0);
        }

        static bool NoGraphics => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        [Test] public void WithoutDecalsTheMainImageAndLayersAreUntouched()
        {
            var main = Solid(Color.white);
            var source = Probe(); source.SetTexture("_MainTex", main);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetTexture("_MainTex"), Is.SameAs(main));
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseMain3rdTex"), Is.EqualTo(0f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void AWholeTiledReplacingDecalBecomesTheSecondLayer()
        {
            var image = Solid(Color.black);
            var source = Probe(); Decal(source, 0, new Color(.2f, .3f, .4f, .9f));
            source.SetTexture("_DecalTexture", image);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_Main2ndTex"), Is.SameAs(image));
            Assert.That(Vector2.Distance(output.GetTextureScale("_Main2ndTex"), Vector2.one), Is.LessThan(1e-5f));
            Assert.That(Vector2.Distance(output.GetTextureOffset("_Main2ndTex"), Vector2.zero), Is.LessThan(1e-5f));
            AssertColor(output, "_Color2nd", new Color(.2f, .3f, .4f, .9f));
            Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_Main2ndTexIsDecal"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_Main2ndTexAngle"), Is.EqualTo(0f).Within(1e-6));
            Assert.That(output.GetFloat("_UseMain3rdTex"), Is.EqualTo(0f));
            Assert.That(dropped, Is.Empty);
        }

        // The source samples at (R(θ)(uv - c) / size + 0.5) × st.xy + st.zw;
        // the layer must sample the same point of the image at every UV.
        [TestCase(true)]
        [TestCase(false)]
        public void APlacedTurnedDecalSamplesTheSamePointsAsTheSource(bool tiled)
        {
            var source = Probe(); Decal(source, 0, Color.white, 8, tiled);
            source.SetTexture("_DecalTexture", Solid(Color.white));
            var st = tiled ? new Vector4(2, 3, .1f, .2f) : new Vector4(1, 1, 0, 0);
            source.SetTextureScale("_DecalTexture", new Vector2(st.x, st.y)); source.SetTextureOffset("_DecalTexture", new Vector2(st.z, st.w));
            source.SetVector("_DecalPosition", new Vector4(.3f, .6f, 0, 0));
            source.SetVector("_DecalScale", new Vector4(.2f, .4f, 1, 0));
            source.SetVector("_DecalSideOffset", new Vector4(.02f, .04f, .01f, .03f));
            source.SetFloat("_DecalRotation", 30);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(dropped, Is.Empty);
            Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(1f), "Add");
            Assert.That(output.GetFloat("_Main2ndTexIsDecal"), Is.EqualTo(tiled ? 0f : 1f));
            var angle = output.GetFloat("_Main2ndTexAngle");
            Assert.That(angle, Is.EqualTo(30 * Mathf.Deg2Rad).Within(1e-5));
            var scale = output.GetTextureScale("_Main2ndTex"); var offset = output.GetTextureOffset("_Main2ndTex");
            Vector2 Turn(Vector2 v) => new Vector2(v.x * Mathf.Cos(angle) - v.y * Mathf.Sin(angle), v.x * Mathf.Sin(angle) + v.y * Mathf.Cos(angle));
            var size = new Vector2(.2f + .02f + .04f, .4f + .01f + .03f);
            var center = new Vector2(.3f, .6f) + new Vector2((.04f - .02f) / 2, (.03f - .01f) / 2);
            foreach (var uv in new[] { new Vector2(.1f, .2f), new Vector2(.3f, .6f), new Vector2(.35f, .55f), new Vector2(.9f, .05f) })
            {
                var local = Turn(uv - center);
                var expected = Vector2.Scale(new Vector2(local.x / size.x + .5f, local.y / size.y + .5f), new Vector2(st.x, st.y)) + new Vector2(st.z, st.w);
                var actual = Vector2.Scale(Turn(uv - new Vector2(.5f, .5f)) + new Vector2(.5f, .5f), scale) + offset;
                Assert.That(Vector2.Distance(actual, expected), Is.LessThan(1e-4f), uv.ToString("F2"));
            }
        }

        // A 2:1 image placed at half size: Shrink to Fit halves its height,
        // Grow to Fit doubles its width.
        [TestCase(0, 2f, 2f)]
        [TestCase(1, 2f, 4f)]
        [TestCase(2, 1f, 2f)]
        public void TheAspectRatioModeFitsTheImagesSides(int mode, float scaleX, float scaleY)
        {
            var image = Own(new Texture2D(4, 2, TextureFormat.RGBA32, false) { name = "wide" });
            var source = Probe(); Decal(source, 0, Color.white);
            source.SetTexture("_DecalTexture", image);
            source.SetVector("_DecalScale", new Vector4(.5f, .5f, 1, 0));
            source.SetFloat("_Decal0AspectRatioMode", mode);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(dropped, Is.Empty);
            Assert.That(Vector2.Distance(output.GetTextureScale("_Main2ndTex"), new Vector2(scaleX, scaleY)), Is.LessThan(1e-5f),
                output.GetTextureScale("_Main2ndTex").ToString("F3"));
        }

        [Test] public void TheLastTwoDrawableDecalsBecomeLayersAndTheFirstIsBakedUnderThem()
        {
            if (NoGraphics) Assert.Ignore("Baking needs a graphics device.");
            var main = Solid(Color.white);
            var source = Probe(); source.SetTexture("_MainTex", main);
            Decal(source, 0, Color.black); Decal(source, 1, Color.red, 2); Decal(source, 2, Color.green, 6);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(dropped, Is.Empty);
            AssertColor(output, "_Color2nd", Color.red);
            Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(3f), "Multiply");
            AssertColor(output, "_Color3rd", Color.green);
            Assert.That(output.GetFloat("_Main3rdTexBlendMode"), Is.EqualTo(2f), "Screen");
            var baked = output.GetTexture("_MainTex") as Texture2D;
            Assert.That(baked, Is.Not.Null.And.Not.SameAs(main));
            foreach (var pixel in baked.GetPixels()) Assert.That(Vector4.Distance(pixel, Color.black), Is.LessThan(1.5f / 255));
            AssertColor(output, "_Color", Color.white);
        }

        [Test] public void APlacedDarkenDecalIsBakedOnItsAreaOnly()
        {
            if (NoGraphics) Assert.Ignore("Baking needs a graphics device.");
            var source = Probe(); source.SetTexture("_MainTex", Solid(new Color(.8f, .8f, .8f, .5f), 8));
            source.SetColor("_Color", new Color(1, 1, 1, .7f));
            Decal(source, 0, new Color(.2f, .9f, .2f, 1), 1, false);
            source.SetVector("_DecalPosition", new Vector4(.25f, .5f, 0, 0));
            source.SetVector("_DecalScale", new Vector4(.5f, 1, 1, 0));
            var output = Substitute(source, out _, out var dropped);
            Assert.That(dropped, Is.Empty);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(0f), "Darken has no layer mode.");
            var baked = (Texture2D)output.GetTexture("_MainTex");
            Assert.That(baked.width, Is.EqualTo(8));
            for (var x = 0; x < 8; x++)
            {
                var expected = x < 4 ? new Color(.2f, .8f, .2f, .5f) : new Color(.8f, .8f, .8f, .5f);
                Assert.That(Vector4.Distance(baked.GetPixel(x, 3), expected), Is.LessThan(2f / 255), "Column " + x + " " + baked.GetPixel(x, 3));
            }
            AssertColor(output, "_Color", new Color(1, 1, 1, .7f));
        }

        [Test] public void AMaskOnTheMainImagesPlacementIsCopiedToTheLayer()
        {
            var mask = Mask(new Color(.5f, .5f, .5f, 1));
            var source = Probe(); Decal(source, 0, Color.red);
            source.SetTexture("_DecalMask", mask); source.SetFloat("_Decal0MaskChannel", 0);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetTexture("_Main2ndBlendMask"), Is.SameAs(mask));
            Assert.That(dropped, Is.Empty);
        }

        [TestCase("_DecalTextureUV", 1f)]
        [TestCase("_DecalMirroredUVMode", 1f)]
        [TestCase("_DecalSymmetryMode", 2f)]
        [TestCase("_DecalRotationSpeed", 10f)]
        [TestCase("_DecalBlendType", 3f)]
        public void ADecalNeitherLayerNorBakeCanDrawIsReported(string name, float value)
        {
            var source = Probe(); Decal(source, 0, Color.red);
            source.SetFloat(name, value);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(0f));
            Assert.That(dropped, Is.EquivalentTo(new[] { "Decal 0" }));
        }

        [Test] public void ADecalsGlowIsReportedAndItsColorKept()
        {
            var source = Probe(); Decal(source, 1, Color.red); source.SetFloat("_DecalEmissionStrength1", 2);
            var output = Substitute(source, out _, out var dropped);
            Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(1f));
            Assert.That(dropped, Is.EquivalentTo(new[] { "Decal 1 Emission" }));
        }

        static float Value(Material material, string name)
        {
            var index = material.shader.FindPropertyIndex(name);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), name);
            return material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Int ? material.GetInteger(name) : material.GetFloat(name);
        }
    }
}
