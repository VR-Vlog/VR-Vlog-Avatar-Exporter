using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class EmissionMaskBakeTests
    {
        [Test]
        public void MaskLimitsStandardMToonEmissionToMaskedArea()
        {
            var material = Create();
            var emission = Solid(new Color(.6f, .6f, .6f, 1));
            var mask = Image(Color.black, Color.white, Color.black, Color.white);
            var main = Solid(Color.white);
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
                Assert.That(Linear(textures[0].GetPixel(0, 0)).maxColorComponent, Is.EqualTo(0f).Within(.015f), "Black mask must not emit.");
                Assert.That(Linear(textures[0].GetPixel(1, 0)).r, Is.EqualTo(.6f).Within(.015f), "White mask keeps the emission image.");
                Assert.That(Linear(textures[0].GetPixel(1, 1)).g, Is.EqualTo(.6f).Within(.015f));
                Assert.IsFalse(warnings.Any(warning => warning.Contains("発光のマスク")), "A baked mask is not an approximation.");
                Assert.AreSame(emission, material.GetTexture("_EmissionMap"));
                Assert.AreSame(mask, material.GetTexture("_EmissionBlendMask"));
            }
            finally { Destroy(material, created, textures, emission, mask, main); }
        }

        [Test]
        public void MainStrengthTintsEmissionByMainTexture()
        {
            var material = Create();
            var emission = Solid(new Color(.6f, .6f, .6f, 1));
            var main = Solid(new Color(.5f, .25f, 1, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_EmissionMap", emission);
                material.SetFloat("_EmissionMainStrength", 1);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.That(textures, Has.Count.EqualTo(1));
                var pixel = Linear(((Texture2D)fallback.GetTexture("_EmissionMap")).GetPixel(0, 0));
                Assert.That(pixel.r, Is.EqualTo(.3f).Within(.015f));
                Assert.That(pixel.g, Is.EqualTo(.15f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(.6f).Within(.015f));
            }
            finally { Destroy(material, created, textures, emission, main); }
        }

        [Test]
        public void WithoutMaskOrMainStrengthEmissionImageIsUnchanged()
        {
            var material = Create();
            var emission = Solid(new Color(.6f, .6f, .6f, 1));
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
            finally { Destroy(material, created, textures, emission); }
        }

        private static Material Create()
        {
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var material = new Material(shader);
            material.SetFloat("_UseEmission", 1);
            return material;
        }

        private static Texture2D Solid(Color color) => Image(color, color, color, color);

        private static Texture2D Image(params Color[] pixels)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Color Linear(Color pixel) => QualitySettings.activeColorSpace == ColorSpace.Linear ? pixel.linear : pixel;

        private static void Destroy(Material material, List<Material> created, List<Texture2D> textures, params Texture2D[] images)
        {
            foreach (var item in created) Object.DestroyImmediate(item);
            foreach (var texture in textures) Object.DestroyImmediate(texture);
            foreach (var image in images) Object.DestroyImmediate(image);
            Object.DestroyImmediate(material);
        }
    }
}
