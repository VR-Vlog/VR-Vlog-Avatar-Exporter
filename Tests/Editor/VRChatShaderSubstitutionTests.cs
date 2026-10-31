using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The shaders the VRChat SDK ships are mapped by one table row each. The
    // probes declare the SDK shaders' properties and keywords; the row is
    // chosen by the shader name given to Create.
    public sealed class VRChatShaderSubstitutionTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            foreach (var value in owned) if (value != null) Object.DestroyImmediate(value);
            owned.Clear();
        }

        T Own<T>(T value) where T : Object { owned.Add(value); return value; }
        Material Mobile() => Own(new Material(Shader.Find("Hidden/VRVlogTests/VRChatMobileProbe")) { name = "Mobile" });
        Material ToonStandard() => Own(new Material(Shader.Find("Hidden/VRVlogTests/VRChatToonStandardProbe")) { name = "ToonStandard" });
        static bool CanRead => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        Texture2D Image(Color left, Color right, bool linear = false)
        {
            var image = Own(new Texture2D(8, 1, TextureFormat.RGBA32, false, linear) { name = "image", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp });
            var pixels = new Color[8];
            for (var i = 0; i < 8; i++) pixels[i] = i < 4 ? left : right;
            image.SetPixels(pixels);
            image.Apply(false, false);
            return image;
        }

        Material Substitute(Material source, string shader, out IReadOnlyList<string> dropped)
        {
            var textures = new List<Texture2D>();
            var output = UnsupportedShaderSubstitution.Create(source, shader, out var colorSkipped, out var route, out dropped, textures);
            owned.AddRange(textures);
            Assert.That(output, Is.Not.Null);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.VRChat));
            Assert.That(colorSkipped, Is.False);
            return Own(output);
        }

        Material Substitute(Material source, string shader) => Substitute(source, shader, out _);

        static void AssertColor(Color actual, Color expected, string what, float tolerance = 2e-3f) =>
            Assert.That(Vector4.Distance(actual, expected), Is.LessThan(tolerance), what + " " + actual.ToString("F4"));

        [Test] public void EveryShaderOfTheVRChatSdkHasARow()
        {
            var expected = new[]
            {
                "VRChat/Mobile/Toon Standard", "VRChat/Mobile/Toon Standard (Outline)", "VRChat/Mobile/Standard Lite",
                "VRChat/Mobile/Diffuse", "VRChat/Mobile/Bumped Diffuse", "VRChat/Mobile/Bumped Mapped Specular",
                "VRChat/Mobile/Toon Lit", "VRChat/Mobile/MatCap Lit", "VRChat/Mobile/Lightmapped",
                "VRChat/Mobile/Particles/Additive", "VRChat/Mobile/Particles/Alpha Blended", "VRChat/Mobile/Particles/Multiply",
                "VRChat/Mobile/Worlds/Supersampled UI", "VRChat/Sprites/Default", "VRChat/Sprites/Diffuse",
                "Video/RealtimeEmissiveGamma", "VRChat/Panosphere", "VRChat/Mobile/Skybox", "FX/MirrorReflection",
            };
            Assert.That(UnsupportedShaderSubstitution.VRChatShaderNames, Is.EquivalentTo(expected));
        }

        [Test] public void AnotherShaderNameKeepsTheGenericMapping()
        {
            UnsupportedShaderSubstitution.Create(Own(new Material(Shader.Find("Standard"))), "Standard", out _, out var route, out var dropped);
            Assert.That(route, Is.EqualTo(UnsupportedShaderSubstitution.Route.Generic));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void LambertShadersShadeByTheAngleToTheLight()
        {
            foreach (var shader in new[] { "VRChat/Mobile/Diffuse", "VRChat/Mobile/Bumped Diffuse", "VRChat/Mobile/Standard Lite" })
            {
                var output = Substitute(Mobile(), shader);
                Assert.That(output.shader.name, Is.EqualTo("lilToon"), shader);
                Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f), shader);
                Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.75f).Within(1e-6), shader);
                Assert.That(output.GetFloat("_ShadowBlur"), Is.EqualTo(.5f).Within(1e-6), shader);
                Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(1f).Within(1e-6), shader);
                Assert.That(output.GetFloat("_ShadowEnvStrength"), Is.EqualTo(1f).Within(1e-6), shader);
                Assert.That(output.GetFloat("_ShadowBorderRange"), Is.EqualTo(0f).Within(1e-6), shader);
                AssertColor(output.GetColor("_ShadowColor"), Color.black, shader);
                Assert.That(output.GetFloat("_AsUnlit"), Is.EqualTo(0f), shader);
            }
        }

        [Test] public void FlatAndUnlitShadersCastNoShadow()
        {
            var flat = Substitute(Mobile(), "VRChat/Mobile/Toon Lit");
            Assert.That(flat.GetFloat("_UseShadow"), Is.EqualTo(0f));
            Assert.That(flat.GetFloat("_AsUnlit"), Is.EqualTo(0f));
            foreach (var shader in new[] { "VRChat/Mobile/Lightmapped", "VRChat/Panosphere", "FX/MirrorReflection" })
            {
                var unlit = Substitute(Mobile(), shader);
                Assert.That(unlit.shader.name, Is.EqualTo("lilToon"), shader);
                Assert.That(unlit.GetFloat("_UseShadow"), Is.EqualTo(0f), shader);
                Assert.That(unlit.GetFloat("_AsUnlit"), Is.EqualTo(1f), shader);
            }
        }

        [Test] public void AdditiveParticlesAddUnlitAndDrawBothSides()
        {
            var output = Substitute(Mobile(), "VRChat/Mobile/Particles/Additive");
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(output.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(output.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.One));
            Assert.That(output.GetFloat("_SrcBlendFA"), Is.EqualTo((float)BlendMode.Zero));
            Assert.That(output.GetFloat("_DstBlendFA"), Is.EqualTo((float)BlendMode.One));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off));
            Assert.That(output.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_AsUnlit"), Is.EqualTo(1f));
        }

        [Test] public void MultiplyParticlesMultiplyWhatIsBehind()
        {
            var output = Substitute(Mobile(), "VRChat/Mobile/Particles/Multiply");
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"));
            Assert.That(output.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.DstColor));
            Assert.That(output.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
            Assert.That(output.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off));
        }

        [Test] public void AlphaBlendedShadersBlendByAlphaOnBothSides()
        {
            foreach (var shader in new[] { "VRChat/Mobile/Particles/Alpha Blended", "VRChat/Sprites/Default", "VRChat/Mobile/Worlds/Supersampled UI", "VRChat/Sprites/Diffuse" })
            {
                var output = Substitute(Mobile(), shader);
                Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonTransparent"), shader);
                Assert.That(output.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.One), shader);
                Assert.That(output.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha), shader);
                Assert.That(output.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off), shader);
                Assert.That(output.GetFloat("_ZWrite"), Is.EqualTo(0f), shader);
                Assert.That(output.GetFloat("_AsUnlit"), Is.EqualTo(shader == "VRChat/Sprites/Diffuse" ? 0f : 1f), shader);
            }
        }

        [Test] public void StandardLiteKeepsMetallicSmoothnessAndItsSwitches()
        {
            var source = Mobile();
            source.SetFloat("_Metallic", .3f); source.SetFloat("_Glossiness", .6f);
            source.SetTexture("_MetallicGlossMap", Image(new Color(1, 0, 0, .5f), new Color(.5f, 0, 0, 1), true));
            // The SDK's [ToggleOff] defaults are 0, so a new material starts with
            // both switches off; turn reflections back on to tell them apart.
            source.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            source.SetFloat("_GlossyReflections", 1); source.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
            var output = Substitute(source, "VRChat/Mobile/Standard Lite", out var dropped);
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(.3f).Within(1e-6));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(.6f).Within(1e-6));
            Assert.That(output.GetFloat("_SpecularToon"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_ApplySpecular"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_ApplyReflection"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MetallicGlossMap"), Is.SameAs(source.GetTexture("_MetallicGlossMap")));
            if (!CanRead) { Assert.That(dropped, Does.Contain("Smoothness Map")); return; }
            var smoothness = (Texture2D)output.GetTexture("_SmoothnessTex");
            Assert.That(smoothness, Is.Not.Null);
            Assert.That(smoothness.GetPixel(0, 0).r, Is.EqualTo(.5f).Within(.01f), "Smoothness is read from alpha.");
            Assert.That(smoothness.GetPixel(7, 0).r, Is.EqualTo(1f).Within(.01f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void StandardLiteDetailNormalBecomesTheSecondNormalMapAndTheRestIsReported()
        {
            var source = Mobile();
            source.EnableKeyword("_DETAIL");
            var normal = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "detailNormal" });
            source.SetTexture("_DetailNormalMap", normal); source.SetFloat("_DetailNormalMapScale", .4f);
            source.SetTexture("_DetailAlbedoMap", Image(Color.grey, Color.grey));
            source.SetTextureScale("_DetailAlbedoMap", new Vector2(3, 3));
            source.SetFloat("_UVSec", 1);
            source.SetTexture("_OcclusionMap", Image(Color.white, Color.black));
            var output = Substitute(source, "VRChat/Mobile/Standard Lite", out var dropped);
            Assert.That(output.GetFloat("_UseBump2ndMap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_Bump2ndMap"), Is.SameAs(normal));
            Assert.That(output.GetTextureScale("_Bump2ndMap"), Is.EqualTo(new Vector2(3, 3)));
            Assert.That(output.GetFloat("_Bump2ndScale"), Is.EqualTo(.4f).Within(1e-6));
            Assert.That(output.GetFloat("_Bump2ndMap_UVMode"), Is.EqualTo(1f));
            Assert.That(dropped, Does.Contain("Detail Albedo x2"));
            Assert.That(dropped, Does.Contain("Occlusion"));
        }

        [Test] public void BlinnPhongSpecularBecomesTheSameToonSpecularCurve()
        {
            var source = Mobile();
            source.SetFloat("_Shininess", .25f); source.SetColor("_SpecColor", new Color(.5f, .6f, .7f, 1));
            source.SetTexture("_MainTex", Image(new Color(1, 1, 1, .2f), Color.white));
            var output = Substitute(source, "VRChat/Mobile/Bumped Mapped Specular", out var dropped);
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_SpecularToon"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_SpecularBorder"), Is.EqualTo(.5f).Within(1e-6));
            Assert.That(output.GetFloat("_SpecularBlur"), Is.EqualTo(1f).Within(1e-6));
            Assert.That(output.GetFloat("_ApplyReflection"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(0f));
            // 1 / roughness = 1 / (1 - smoothness)² is the exponent 0.25 × 128.
            var smoothness = output.GetFloat("_Smoothness");
            Assert.That(1 / ((1 - smoothness) * (1 - smoothness)), Is.EqualTo(32f).Within(1e-3));
            AssertColor(output.GetColor("_ReflectionColor"), new Color(.5f, .6f, .7f, 1), "_ReflectionColor");
            if (!CanRead) return;
            var gloss = (Texture2D)output.GetTexture("_ReflectionColorTex");
            Assert.That(gloss, Is.Not.Null, string.Join(", ", dropped));
            Assert.That(gloss.GetPixel(0, 0).r, Is.EqualTo(.2f).Within(.01f), "Gloss is the main alpha.");
        }

        [Test] public void MatcapLitMultipliesTheMatcapUnlitOverTheLitAlbedo()
        {
            var source = Mobile();
            var matcap = Image(Color.red, Color.blue);
            source.SetTexture("_MatCap", matcap);
            var output = Substitute(source, "VRChat/Mobile/MatCap Lit");
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_MatCapTex"), Is.SameAs(matcap));
            Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(3f));
            Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(0f));
        }

        [Test] public void TheVideoShaderGlowsWithItsImage()
        {
            var source = Mobile();
            var main = Image(Color.red, Color.green);
            source.SetTexture("_MainTex", main); source.SetFloat("_Emission", 2);
            source.SetFloat("_ApplyGamma", 1); source.EnableKeyword("APPLY_GAMMA");
            var output = Substitute(source, "Video/RealtimeEmissiveGamma", out var dropped);
            AssertColor(output.GetColor("_Color"), Color.black, "_Color");
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            Assert.That(output.GetTexture("_EmissionMap"), Is.SameAs(main));
            AssertColor(output.GetColor("_EmissionColor").linear, new Color(2, 2, 2, 1), "_EmissionColor");
            Assert.That(dropped, Is.EqualTo(new[] { "Apply Gamma" }));
        }

        [Test] public void ProjectionShadersReportWhatIsLeftOut()
        {
            Substitute(Mobile(), "VRChat/Panosphere", out var pano);
            Substitute(Mobile(), "VRChat/Mobile/Skybox", out var sky);
            Substitute(Mobile(), "FX/MirrorReflection", out var mirror);
            Assert.That(pano, Is.EqualTo(new[] { "Panorama Projection" }));
            Assert.That(sky, Is.EqualTo(new[] { "Cubemap" }));
            Assert.That(mirror, Is.EqualTo(new[] { "Mirror Reflection" }));
        }

        [Test] public void ToonStandardWithoutARampOrFeaturesIsFlatAndReportsNothing()
        {
            var source = ToonStandard();
            source.SetTexture("_BumpMap", Own(new Texture2D(4, 4, TextureFormat.RGBA32, false, true)));
            var output = Substitute(source, "VRChat/Mobile/Toon Standard", out var dropped);
            Assert.That(output.shader.name, Is.EqualTo("lilToon"));
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(0f), "A white ramp leaves every side lit.");
            Assert.That(output.GetFloat("_UseBumpMap"), Is.EqualTo(0f), "Normal maps are off without USE_NORMAL_MAPS.");
            Assert.That(output.GetFloat("_LightMaxLimit"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(0f));
            Assert.That(dropped, Is.Empty);
        }

        [Test] public void ToonStandardRampBecomesTheShadowEdgeAndTint()
        {
            var source = ToonStandard();
            source.SetTexture("_Ramp", Image(Color.black, Color.white, true));
            source.SetFloat("_ShadowAlbedo", .25f); source.SetFloat("_ShadowBoost", .2f);
            source.SetFloat("_LimitBrightness", 0); source.SetFloat("_MinBrightness", .05f);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard");
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowMainStrength"), Is.EqualTo(1f));
            AssertColor(output.GetColor("_ShadowColor").linear, new Color(.25f, .25f, .25f, 1), "_ShadowColor");
            Assert.That(output.GetFloat("_LightMaxLimit"), Is.EqualTo(10f));
            Assert.That(output.GetFloat("_LightMinLimit"), Is.EqualTo(.05f).Within(1e-6));
            if (!CanRead) return;
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(.8f).Within(.01f), "The boost lifts the dark end to 0.2.");
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(.5f).Within(.03f));
        }

        [Test] public void ToonStandardFlatRampDarkensEveryAngle()
        {
            if (!CanRead) Assert.Ignore("The ramp is read through the GPU.");
            var source = ToonStandard();
            source.SetTexture("_Ramp", Image(Color.grey, Color.grey, true));
            var output = Substitute(source, "VRChat/Mobile/Toon Standard");
            Assert.That(output.GetFloat("_UseShadow"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowBorder"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_ShadowStrength"), Is.EqualTo(.5f).Within(.01f));
            var white = ToonStandard();
            white.SetTexture("_Ramp", Image(Color.white, Color.white, true));
            Assert.That(Substitute(white, "VRChat/Mobile/Toon Standard").GetFloat("_UseShadow"), Is.EqualTo(0f));
        }

        [Test] public void ToonStandardSpecularUsesTheSameReflectanceAndMetallic()
        {
            var source = ToonStandard();
            source.EnableKeyword("USE_SPECULAR");
            source.SetFloat("_MetallicStrength", .5f); source.SetFloat("_GlossStrength", .7f); source.SetFloat("_Reflectance", .5f);
            source.SetFloat("_SpecularSharpness", .3f);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard", out var dropped);
            Assert.That(output.GetFloat("_UseReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_SpecularToon"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_ApplyReflection"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_Metallic"), Is.EqualTo(Mathf.LinearToGammaSpace(.5f)).Within(1e-5));
            Assert.That(output.GetFloat("_Smoothness"), Is.EqualTo(.7f).Within(1e-6));
            Assert.That(output.GetFloat("_Reflectance"), Is.EqualTo(Mathf.LinearToGammaSpace(.04f)).Within(1e-5));
            Assert.That(dropped, Does.Contain("Specular Sharpness"));
        }

        [Test] public void ToonStandardMatcapTypesPickAddOrMultiply()
        {
            var matcap = Image(Color.white, Color.grey);
            foreach (var (type, mode, lit) in new[] { (0, 1f, 1f), (1, 3f, 0f) })
            {
                var source = ToonStandard();
                source.EnableKeyword("USE_MATCAP");
                source.SetTexture("_Matcap", matcap); source.SetFloat("_MatcapType", type); source.SetFloat("_MatcapStrength", .6f);
                var output = Substitute(source, "VRChat/Mobile/Toon Standard");
                Assert.That(output.GetFloat("_UseMatCap"), Is.EqualTo(1f));
                Assert.That(output.GetFloat("_MatCapBlendMode"), Is.EqualTo(mode));
                Assert.That(output.GetFloat("_MatCapEnableLighting"), Is.EqualTo(lit));
                Assert.That(output.GetFloat("_MatCapBlend"), Is.EqualTo(.6f).Within(1e-6));
                Assert.That(output.GetFloat("_MatCapZRotCancel"), Is.EqualTo(1f));
            }
        }

        [Test] public void ToonStandardRimKeepsItsRangeSoftnessAndIntensity()
        {
            var source = ToonStandard();
            source.EnableKeyword("USE_RIMLIGHT");
            source.SetColor("_RimColor", Color.red); source.SetFloat("_RimIntensity", .5f);
            source.SetFloat("_RimRange", .4f); source.SetFloat("_RimSharpness", .2f);
            source.SetFloat("_RimAlbedoTint", .3f); source.SetFloat("_RimEnvironmental", 1);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard");
            Assert.That(output.GetFloat("_UseRim"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimBorder"), Is.EqualTo(.6f).Within(1e-6));
            Assert.That(output.GetFloat("_RimBlur"), Is.EqualTo(.4f).Within(1e-6));
            Assert.That(output.GetFloat("_RimFresnelPower"), Is.EqualTo(1f));
            Assert.That(output.GetFloat("_RimMainStrength"), Is.EqualTo(.3f).Within(1e-6));
            Assert.That(output.GetFloat("_RimEnableLighting"), Is.EqualTo(1f));
            AssertColor(output.GetColor("_RimColor").linear, new Color(.5f, 0, 0, 1), "_RimColor");
        }

        [Test] public void ToonStandardDetailModesMapToTheSecondMainImage()
        {
            var detail = Image(Color.red, Color.red);
            foreach (var (mode, blend) in new[] { (0, 0f), (1, 1f), (2, 3f) })
            {
                var source = ToonStandard();
                source.EnableKeyword("USE_DETAIL_MAPS");
                source.SetTexture("_DetailAlbedoMap", detail); source.SetFloat("_DetailMode", mode); source.SetFloat("_DetailUV", 1);
                var output = Substitute(source, "VRChat/Mobile/Toon Standard");
                Assert.That(output.GetFloat("_UseMain2ndTex"), Is.EqualTo(1f));
                Assert.That(output.GetTexture("_Main2ndTex"), Is.SameAs(detail));
                Assert.That(output.GetFloat("_Main2ndTexBlendMode"), Is.EqualTo(blend));
                Assert.That(output.GetFloat("_Main2ndTex_UVMode"), Is.EqualTo(1f));
            }
            var doubled = ToonStandard();
            doubled.EnableKeyword("USE_DETAIL_MAPS");
            doubled.SetTexture("_DetailAlbedoMap", detail); doubled.SetFloat("_DetailMode", 3);
            Substitute(doubled, "VRChat/Mobile/Toon Standard", out var dropped);
            Assert.That(dropped, Does.Contain("Detail Mode (MultiplyX2)"));
        }

        [Test] public void ToonStandardOutlineKeepsThicknessAndColor()
        {
            var source = ToonStandard();
            source.SetFloat("_OutlineThickness", .2f); source.SetColor("_OutlineColor", Color.blue);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard (Outline)");
            Assert.That(output.shader.name, Is.EqualTo("Hidden/lilToonOutline"));
            Assert.That(output.GetFloat("_OutlineWidth"), Is.EqualTo(.2f).Within(1e-6));
            Assert.That(output.GetFloat("_OutlineFixWidth"), Is.EqualTo(0f));
            Assert.That(output.GetFloat("_OutlineEnableLighting"), Is.EqualTo(0f));
            AssertColor(output.GetColor("_OutlineColor"), Color.blue, "_OutlineColor");
        }

        [Test] public void ToonStandardOutlineMixedWithTheAlbedoIsBakedPerPixel()
        {
            if (!CanRead) Assert.Ignore("Baking reads the images through the GPU.");
            var main = Image(Color.red, Color.green);
            var source = ToonStandard();
            source.SetTexture("_MainTex", main); source.SetTextureScale("_MainTex", new Vector2(2, 1));
            source.SetColor("_OutlineColor", Color.blue); source.SetFloat("_OutlineFromAlbedo", .25f);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard (Outline)");
            var baked = (Texture2D)output.GetTexture("_OutlineTex");
            Assert.That(baked, Is.Not.SameAs(main));
            AssertColor(output.GetColor("_OutlineColor"), Color.white, "_OutlineColor");
            Assert.That(output.GetTextureScale("_OutlineTex"), Is.EqualTo(new Vector2(2, 1)));
            var left = baked.GetPixel(0, 0).linear;
            AssertColor(new Color(left.r, left.g, left.b, 1), new Color(.25f, 0, .75f, 1), "left", 8e-3f);
            var right = baked.GetPixel(baked.width - 1, 0).linear;
            AssertColor(new Color(right.r, right.g, right.b, 1), new Color(0, .25f, .75f, 1), "right", 8e-3f);

            var all = ToonStandard();
            all.SetTexture("_MainTex", main); all.SetColor("_Color", Color.grey);
            all.SetFloat("_OutlineFromAlbedo", 1);
            var whole = Substitute(all, "VRChat/Mobile/Toon Standard (Outline)");
            Assert.That(whole.GetTexture("_OutlineTex"), Is.SameAs(main));
            AssertColor(whole.GetColor("_OutlineColor"), Color.grey, "_OutlineColor");
        }

        [Test] public void ToonStandardHueShiftAndColorMaskAreBakedIntoTheMainImage()
        {
            if (!CanRead) Assert.Ignore("Baking reads the images through the GPU.");
            var source = ToonStandard();
            source.EnableKeyword("USE_HUE_SHIFT"); source.EnableKeyword("USE_COLOR_MASK");
            source.SetTexture("_MainTex", Image(Color.red, Color.red));
            source.SetFloat("_HueShift", 2 * Mathf.PI / 3);
            // The left half of the color mask (R) recolors the grey of the albedo blue.
            source.SetTexture("_ColorMask", Image(new Color(1, 0, 0, 0), new Color(0, 0, 0, 0), true));
            source.SetColor("_ColorMaskColor1", Color.blue);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard", out var dropped);
            Assert.That(dropped, Is.Empty);
            AssertColor(output.GetColor("_Color"), Color.white, "_Color");
            var baked = (Texture2D)output.GetTexture("_MainTex");
            Assert.That(baked, Is.Not.SameAs(source.GetTexture("_MainTex")));
            // A third of a turn takes red to green; the masked half becomes grey × blue.
            var right = baked.GetPixel(baked.width - 1, 0).linear;
            AssertColor(new Color(right.r, right.g, right.b, 1), new Color(0, 1, 0, 1), "hue");
            var left = baked.GetPixel(0, 0).linear;
            AssertColor(new Color(left.r, left.g, left.b, 1), new Color(0, 0, .59f, 1), "color mask");
        }

        [Test] public void BakingKeepsTheTilingTheImagesShare()
        {
            if (!CanRead) Assert.Ignore("Baking reads the images through the GPU.");
            foreach (var (maskScale, expected) in new[] { (new Vector2(.5f, 1), new Vector2(.5f, 1)), (Vector2.one, Vector2.one) })
            {
                var source = ToonStandard();
                source.EnableKeyword("USE_COLOR_MASK");
                source.SetTexture("_MainTex", Image(Color.red, Color.red)); source.SetTextureScale("_MainTex", new Vector2(.5f, 1));
                source.SetTexture("_ColorMask", Image(new Color(1, 0, 0, 0), new Color(0, 0, 0, 0), true));
                source.SetTextureScale("_ColorMask", maskScale);
                source.SetColor("_ColorMaskColor1", Color.blue);
                var output = Substitute(source, "VRChat/Mobile/Toon Standard");
                Assert.That(output.GetTexture("_MainTex"), Is.Not.SameAs(source.GetTexture("_MainTex")));
                Assert.That(output.GetTextureScale("_MainTex"), Is.EqualTo(expected), "mask scale " + maskScale);
            }
        }

        [Test] public void ToonStandardColorMaskEmissionGlowsInTheMaskColor()
        {
            if (!CanRead) Assert.Ignore("Baking reads the images through the GPU.");
            var source = ToonStandard();
            source.EnableKeyword("USE_COLOR_MASK");
            source.SetTexture("_ColorMask", Image(new Color(0, 1, 0, 0), new Color(0, 0, 0, 0), true));
            source.SetColor("_ColorMaskColor2", Color.green); source.SetFloat("_ColorMaskEmissionStrength2", 2);
            var output = Substitute(source, "VRChat/Mobile/Toon Standard");
            Assert.That(output.GetFloat("_UseEmission"), Is.EqualTo(1f));
            var map = (Texture2D)output.GetTexture("_EmissionMap");
            Assert.That(map, Is.Not.Null);
            var scale = output.GetColor("_EmissionColor").linear.r;
            AssertColor(map.GetPixel(0, 0).linear * scale, new Color(0, 2, 0, scale), "masked glow");
            AssertColor(map.GetPixel(map.width - 1, 0).linear * scale, new Color(0, 0, 0, scale), "unmasked");
        }

        [Test] public void ToonStandardVertexColorAndOcclusionAreReported()
        {
            var source = ToonStandard();
            source.SetFloat("_VertexColor", 1);
            source.EnableKeyword("USE_OCCLUSION_MAP");
            source.SetTexture("_OcclusionMap", Image(Color.white, Color.black));
            Substitute(source, "VRChat/Mobile/Toon Standard", out var dropped);
            Assert.That(dropped, Is.EquivalentTo(new[] { "Apply Vertex Color", "Occlusion Map" }));
        }
    }
}
