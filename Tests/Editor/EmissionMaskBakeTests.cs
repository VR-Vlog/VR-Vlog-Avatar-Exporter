using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class EmissionMaskBakeTests
    {
        // Under -nographics these bakers still draw and read back, and the
        // pixels they get are undefined: there is no fallback to check.
        static bool NoGraphics => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

        const string Unread = "Without a graphics device the read-back pixels are undefined, and this baker has no fallback for it.";

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
                Assert.That(textures, Has.Count.EqualTo(1));
                Assert.AreSame(textures[0], fallback.GetTexture("_EmissionMap"));
                Assert.IsFalse(warnings.Any(warning => warning.Contains("発光のマスク")), "A baked mask is not an approximation.");
                Assert.AreSame(emission, material.GetTexture("_EmissionMap"));
                Assert.AreSame(mask, material.GetTexture("_EmissionBlendMask"));
                if (NoGraphics) Assert.Ignore(Unread);
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
                Assert.That(textures, Has.Count.EqualTo(1));
                if (NoGraphics) Assert.Ignore(Unread);
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
                AlphaMaskBaker.Bake(material,owned,null);
                Assert.That(owned, Has.Count.EqualTo(1));
                Assert.That(material.GetColor("_Color").a, Is.EqualTo(1));
                Assert.That(material.GetFloat("_AlphaMaskMode"), Is.Zero);
                Assert.That(source.GetPixel(0,0).a, Is.EqualTo(.5f).Within(.01f));
                if (NoGraphics) Assert.Ignore(Unread);
                var pixel = owned[0].GetPixel(0,0);
                Assert.That(pixel.a, Is.EqualTo(expected).Within(.012f));
                var rgb = QualitySettings.activeColorSpace == ColorSpace.Linear ? pixel.linear : pixel;
                Assert.That(rgb.r, Is.EqualTo(.2f).Within(.015f));
            }
            finally { foreach (var texture in owned) Object.DestroyImmediate(texture); Object.DestroyImmediate(source); Object.DestroyImmediate(mask); Object.DestroyImmediate(material); }
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
                var expected = srgb ? Mathf.GammaToLinearSpace(.5f) : .5f;
                if (NoGraphics) Assert.Ignore(Unread);
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
                if (NoGraphics) Assert.Ignore(Unread);
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
