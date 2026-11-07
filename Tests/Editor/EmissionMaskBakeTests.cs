using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class EmissionMaskBakeTests
    {
        // Under -nographics nothing can be read back: the bakers keep their
        // source images and the export warns.
        static bool NoGraphics => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        static bool WarnsUnbaked(IEnumerable<string> warnings, string what) =>
            warnings.Any(warning => warning.Contains("GPUを使えないため、" + what));

        [Test]
        public void MaskLimitsStandardMToonEmissionToMaskedArea()
        {
            var material = Create();
            var emission = TestTextures.Solid(new Color(.6f, .6f, .6f, 1));
            var mask = TestTextures.Image(Color.black, Color.white, Color.black, Color.white);
            var main = TestTextures.Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            var warnings = new List<string>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_EmissionMap", emission);
                material.SetTexture("_EmissionBlendMask", mask);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, warnings, false, textures);
                Assert.AreSame(emission, material.GetTexture("_EmissionMap"));
                Assert.AreSame(mask, material.GetTexture("_EmissionBlendMask"));
                Assert.IsFalse(warnings.Any(warning => warning.Contains("発光のマスク・合成方法")), "A baked mask is not an approximation.");
                Assert.That(WarnsUnbaked(warnings, "発光"), Is.EqualTo(NoGraphics));
                if (NoGraphics)
                {
                    Assert.That(textures, Is.Empty);
                    Assert.AreSame(emission, fallback.GetTexture("_EmissionMap"));
                    return;
                }
                Assert.That(textures, Has.Count.EqualTo(1));
                Assert.AreSame(textures[0], fallback.GetTexture("_EmissionMap"));
                Assert.That(TestTextures.Linear(textures[0].GetPixel(0, 0)).maxColorComponent, Is.EqualTo(0f).Within(.015f), "Black mask must not emit.");
                Assert.That(TestTextures.Linear(textures[0].GetPixel(1, 0)).r, Is.EqualTo(.6f).Within(.015f), "White mask keeps the emission image.");
                Assert.That(TestTextures.Linear(textures[0].GetPixel(1, 1)).g, Is.EqualTo(.6f).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, emission, mask, main); }
        }

        [Test]
        public void MainStrengthTintsEmissionByMainTexture()
        {
            var material = Create();
            var emission = TestTextures.Solid(new Color(.6f, .6f, .6f, 1));
            var main = TestTextures.Solid(new Color(.5f, .25f, 1, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_EmissionMap", emission);
                material.SetFloat("_EmissionMainStrength", 1);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                if (NoGraphics)
                {
                    Assert.That(textures, Is.Empty);
                    Assert.AreSame(emission, fallback.GetTexture("_EmissionMap"));
                    return;
                }
                Assert.That(textures, Has.Count.EqualTo(1));
                var pixel = TestTextures.Linear(((Texture2D)fallback.GetTexture("_EmissionMap")).GetPixel(0, 0));
                Assert.That(pixel.r, Is.EqualTo(.3f).Within(.015f));
                Assert.That(pixel.g, Is.EqualTo(.15f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(.6f).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, emission, main); }
        }

        [Test]
        public void WithoutMaskOrMainStrengthEmissionImageIsUnchanged()
        {
            var material = Create();
            var emission = TestTextures.Solid(new Color(.6f, .6f, .6f, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_EmissionMap", emission);
                foreach (var mask in new Texture[] { null, Texture2D.whiteTexture })
                {
                    material.SetTexture("_EmissionBlendMask", mask);
                    var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                    Assert.AreSame(emission, fallback.GetTexture("_EmissionMap"));
                    Assert.That(textures, Is.Empty);
                }
            }
            finally { TestObjects.Destroy(material, created, textures, emission); }
        }

        private static Material Create()
        {
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var material = new Material(shader);
            material.SetFloat("_UseEmission", 1);
            return material;
        }

        [TestCase(1, .4f)]
        [TestCase(2, .1f)]
        [TestCase(3, .65f)]
        [TestCase(4, 0f)]
        public void AlphaModesPreserveRgbAndIncludeColorAlphaOnce(int mode, float expected)
        {
            var shader = Shader.Find("lilToonTransparent");
            if (shader == null) shader = Shader.Find("Hidden/lilToonTransparent");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var material = new Material(shader);
            var source = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            var mask = new Texture2D(2,2,TextureFormat.RGBA32,false,true);
            var owned = new List<Texture2D>();
            try
            {
                source.SetPixels(new[] { new Color(.2f,.3f,.4f,.5f),new Color(.2f,.3f,.4f,.5f),new Color(.2f,.3f,.4f,.5f),new Color(.2f,.3f,.4f,.5f) }); source.Apply();
                mask.SetPixels(new[] { Color.gray,Color.gray,Color.gray,Color.gray }); mask.Apply();
                material.SetTexture("_MainTex",source); material.SetTexture("_AlphaMask",mask);
                material.SetColor("_Color",new Color(1,1,1,.5f));
                material.SetFloat("_AlphaMaskMode",mode); material.SetFloat("_AlphaMaskScale",.6f); material.SetFloat("_AlphaMaskValue",.1f);
                var warnings = new List<string>();
                AlphaMaskBaker.Bake(material,owned,warnings);
                Assert.That(source.GetPixel(0,0).a, Is.EqualTo(.5f).Within(.01f));
                Assert.That(WarnsUnbaked(warnings, "アルファマスク"), Is.EqualTo(NoGraphics));
                if (NoGraphics)
                {
                    // Unread, the material keeps its image, mask and color.
                    Assert.That(owned, Is.Empty);
                    Assert.That(material.GetTexture("_MainTex"), Is.SameAs(source));
                    Assert.That(material.GetFloat("_AlphaMaskMode"), Is.EqualTo((float)mode));
                    Assert.That(material.GetColor("_Color").a, Is.EqualTo(.5f));
                    return;
                }
                Assert.That(owned, Has.Count.EqualTo(1));
                Assert.That(material.GetColor("_Color").a, Is.EqualTo(1));
                Assert.That(material.GetFloat("_AlphaMaskMode"), Is.Zero);
                var pixel = owned[0].GetPixel(0,0);
                Assert.That(pixel.a, Is.EqualTo(expected).Within(.012f));
                var rgb = QualitySettings.activeColorSpace == ColorSpace.Linear ? pixel.linear : pixel;
                Assert.That(rgb.r, Is.EqualTo(.2f).Within(.015f));
            }
            finally { foreach (var texture in owned) Object.DestroyImmediate(texture); Object.DestroyImmediate(source); Object.DestroyImmediate(mask); Object.DestroyImmediate(material); }
        }

        // An sRGB mask counts with its linear value, as in a Linear project
        // and in the app, also in a Gamma project; a linear one as it is.
        [TestCase(true, .5f * .214f + .1f)]
        [TestCase(false, .5f * .5f + .1f)]
        public void AnAlphaMaskIsReadInLinearValues(bool srgb, float expected)
        {
            var shader = Shader.Find("lilToonTransparent");
            if (shader == null) shader = Shader.Find("Hidden/lilToonTransparent");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var material = new Material(shader);
            var source = new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            var mask = new Texture2D(2,2,TextureFormat.RGBA32,false,!srgb);
            var owned = new List<Texture2D>();
            try
            {
                var color = new Color(.5f,.6f,.7f,1);
                source.SetPixels(new[] { color,color,color,color }); source.Apply();
                mask.SetPixels(new[] { Color.gray,Color.gray,Color.gray,Color.gray }); mask.Apply();
                material.SetTexture("_MainTex",source); material.SetTexture("_AlphaMask",mask);
                material.SetColor("_Color",Color.white);
                material.SetFloat("_AlphaMaskMode",1); material.SetFloat("_AlphaMaskScale",.5f); material.SetFloat("_AlphaMaskValue",.1f);
                AlphaMaskBaker.Bake(material,owned,null);
                // Without a graphics device nothing is baked.
                if (NoGraphics) { Assert.That(owned, Is.Empty); return; }
                Assert.That(owned, Has.Count.EqualTo(1));
                var pixel = owned[0].GetPixel(0,0);
                Assert.That(pixel.a, Is.EqualTo(expected).Within(.012f));
                // The sRGB main image is written back as it was.
                Assert.That(pixel.r, Is.EqualTo(.5f).Within(.01f));
                Assert.That(pixel.b, Is.EqualTo(.7f).Within(.01f));
            }
            finally { foreach (var texture in owned) Object.DestroyImmediate(texture); Object.DestroyImmediate(source); Object.DestroyImmediate(mask); Object.DestroyImmediate(material); }
        }

        // A black and white mask blended up to a larger main image: an sRGB one
        // is decoded before the blend, as in a Linear project, so its edge
        // lies where the same mask imported as linear data puts it.
        [Test]
        public void AnSrgbMaskEdgeIsBlendedInLinearValues()
        {
            var shader = Shader.Find("lilToonCutout");
            if (shader == null) shader = Shader.Find("Hidden/lilToonCutout");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var owned = new List<Texture2D>();
            var created = new List<Object>();
            try
            {
                float[] Alpha(bool srgb)
                {
                    var source = new Texture2D(8, 1, TextureFormat.RGBA32, false, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                    source.SetPixels(Enumerable.Repeat(Color.white, 8).ToArray()); source.Apply();
                    var mask = new Texture2D(2, 1, TextureFormat.RGBA32, false, !srgb);
                    mask.SetPixels(new[] { Color.black, Color.white }); mask.Apply();
                    var material = new Material(shader);
                    created.Add(source); created.Add(mask); created.Add(material);
                    material.SetTexture("_MainTex", source); material.SetTexture("_AlphaMask", mask);
                    material.SetColor("_Color", Color.white);
                    material.SetFloat("_AlphaMaskMode", 1); material.SetFloat("_AlphaMaskScale", 1); material.SetFloat("_AlphaMaskValue", 0);
                    AlphaMaskBaker.Bake(material, owned, null);
                    // Without a graphics device nothing is baked.
                    if (NoGraphics) { Assert.That(owned, Is.Empty); return null; }
                    Assert.That(owned, Has.Count.GreaterThan(0));
                    return owned[owned.Count - 1].GetPixels().Select(pixel => pixel.a).ToArray();
                }
                var linear = Alpha(false);
                var decoded = Alpha(true);
                if (NoGraphics) return;
                Assert.That(linear.Any(a => a > .2f && a < .8f), Is.True, "The edge is blended over several texels.");
                Assert.That(decoded, Is.EqualTo(linear).Within(.012f));
            }
            finally { foreach (var texture in owned) Object.DestroyImmediate(texture); foreach (var value in created) Object.DestroyImmediate(value); }
        }

        // The factor is in linear values: an sRGB image is decoded and one
        // imported as linear data is read as it is, in a Gamma project too.
        [TestCase(false)]
        [TestCase(true)]
        public void AGradationImageIsReadInLinearValues(bool srgb)
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the gradation properties.");
            var material = new Material(shader);
            var gradation = new Texture2D(4, 1, TextureFormat.RGBA32, false, !srgb) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            try
            {
                gradation.SetPixels(new[] { Color.gray, Color.gray, Color.gray, Color.gray }); gradation.Apply();
                material.SetFloat("_EmissionUseGrad", 1f);
                material.SetTexture("_EmissionGradTex", gradation);
                material.SetFloat("_EmissionGradSpeed", 0f);
                // Without a graphics device the gradation is left out.
                var expected = NoGraphics ? 1f : srgb ? Mathf.GammaToLinearSpace(.5f) : .5f;
                Assert.AreEqual(expected, LilToonEmissionPolicy.GradientFactor(material).r, .01f);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(gradation);
            }
        }

        // A gradation image wider than the row that is read was averaged after
        // one bilinear shrink, which skips texels: here every 4th texel is
        // white, and a 256-wide row samples between the black ones only.
        [Test]
        public void AWideGradationImageAveragesEveryTexel()
        {
            var shader = OptionalTools.RequireShader("lilToon", "Install lilToon for the gradation properties.");
            const int width = 1024;
            var material = new Material(shader);
            var gradation = new Texture2D(width, 1, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            try
            {
                var pixels = new Color[width];
                for (var i = 0; i < width; i++) pixels[i] = i % 4 == 0 ? Color.white : Color.black;
                gradation.SetPixels(pixels); gradation.Apply();
                material.SetFloat("_EmissionUseGrad", 1f);
                material.SetTexture("_EmissionGradTex", gradation);
                material.SetFloat("_EmissionGradSpeed", 1f);
                var factor = LilToonEmissionPolicy.GradientFactor(material);
                if (NoGraphics) { Assert.That(factor, Is.EqualTo(Color.white)); return; }
                Assert.AreEqual(.25f, factor.r, .01f);
                Assert.AreEqual(.25f, factor.g, .01f);
                Assert.AreEqual(.25f, factor.b, .01f);
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(gradation);
            }
        }
    }
}
