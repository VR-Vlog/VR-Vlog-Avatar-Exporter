using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class MatcapApproximationTests
    {
        [Test]
        public void MaskLimitsTheAddedMatcapToTheMaskedArea()
        {
            var material = Create(1);
            var mask = TestTextures.Image(Color.black, Color.white, Color.black, Color.white);
            var main = TestTextures.Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapBlendMask", mask);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.That(textures, Has.Count.EqualTo(1));
                var weight = (Texture2D)fallback.GetTexture("_RimTex");
                Assert.AreSame(textures[0], weight);
                Assert.AreSame(main, fallback.GetTexture("_MainTex"), "An added MatCap does not change the main image.");
                Assert.AreSame(mask, material.GetTexture("_MatCapBlendMask"));
                // Without a MatCap image there is nothing to weigh.
                material.SetTexture("_MatCapTex", null);
                Assert.IsNull(UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures).GetTexture("_RimTex"));
                Assert.That(textures, Has.Count.EqualTo(1));
                NoGraphicsDevice.IgnorePixelChecks();
                Assert.That(weight.GetPixel(0, 0).linear.maxColorComponent, Is.EqualTo(0f).Within(.015f), "Black mask must not add the MatCap.");
                Assert.That(weight.GetPixel(1, 0).linear.r, Is.EqualTo(1f).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, mask, main); }
        }

        [TestCase("_UseRim", "_RimColor")]
        [TestCase("_UseBacklight", "_BacklightColor")]
        public void MaskStaysWhenARimLightIsOn(string toggle, string color)
        {
            var material = Create(1);
            var mask = TestTextures.Image(Color.black, Color.white, Color.black, Color.white);
            var rim = TestTextures.Solid(Color.white);
            var main = TestTextures.Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapBlendMask", mask);
                material.SetFloat(toggle, 1);
                material.SetColor(color, Color.white);
                material.SetTexture("_RimColorTex", rim);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.That(textures, Has.Count.EqualTo(1));
                Assert.That(fallback.GetColor("_RimColor").maxColorComponent, Is.GreaterThan(0f));
                var weight = (Texture2D)fallback.GetTexture("_RimTex");
                Assert.AreSame(textures[0], weight, "The mask replaces the rim image.");
                // Without a mask the MatCap covers the mesh in lilToon too, so
                // the rim keeps its own image.
                material.SetTexture("_MatCapBlendMask", null);
                material.SetFloat("_MatCapMainStrength", 1);
                var unmasked = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures).GetTexture("_RimTex");
                if (toggle == "_UseRim") Assert.AreSame(rim, unmasked);
                else Assert.IsNull(unmasked);
                Assert.That(textures, Has.Count.EqualTo(1));
                NoGraphicsDevice.IgnorePixelChecks();
                Assert.That(weight.GetPixel(0, 0).linear.maxColorComponent, Is.EqualTo(0f).Within(.015f));
                Assert.That(weight.GetPixel(1, 0).linear.r, Is.EqualTo(1f).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, mask, rim, main); }
        }

        [Test]
        public void MultiplyMatcapDarkensTheMainImageWhereMasked()
        {
            var material = Create(3);
            var matcap = TestTextures.Solid(new Color(.5f, .25f, 1, 1));
            var mask = TestTextures.Image(Color.black, Color.white, Color.black, Color.white);
            var main = TestTextures.Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapTex", matcap);
                material.SetTexture("_MatCapBlendMask", mask);
                material.SetFloat("_UseShadow", 1);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.That(textures, Has.Count.EqualTo(1));
                var baked = (Texture2D)fallback.GetTexture("_MainTex");
                Assert.AreSame(textures[0], baked);
                Assert.AreSame(baked, fallback.GetTexture("_ShadeTex"), "The shade image is the main image.");
                Assert.IsNull(fallback.GetTexture("_MatcapTex"), "MToon would add a multiply MatCap.");
                Assert.AreSame(main, material.GetTexture("_MainTex"));
                NoGraphicsDevice.IgnorePixelChecks();
                Assert.That(baked.GetPixel(0, 0).linear.r, Is.EqualTo(1f).Within(.015f), "Black mask keeps the main image.");
                var pixel = baked.GetPixel(1, 0).linear;
                Assert.That(pixel.r, Is.EqualTo(.5f).Within(.015f));
                Assert.That(pixel.g, Is.EqualTo(.25f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(1f).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, matcap, mask, main); }
        }

        // The bakes are sRGB images that the app reads in linear values, so a
        // Gamma project bakes the same pixels as a Linear one: it decodes the
        // sRGB main image and mask, darkens in linear values and encodes.
        [Test]
        public void ANormalMatcapDarkensAnSrgbMainImageInLinearValues()
        {
            var material = Create(0);
            var main = Srgb(new Color(.5f, .5f, .5f, 1));
            var mask = Srgb(new Color(.5f, .5f, .5f, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapBlendMask", mask);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                var baked = (Texture2D)fallback.GetTexture("_MainTex");
                Assert.That(textures, Does.Contain(baked));
                NoGraphicsDevice.IgnorePixelChecks();
                // lilToon draws lerp(main, MatCap, mask); seen from every side the main image keeps 1 - mask.
                var gray = Mathf.GammaToLinearSpace(.5f);
                Assert.That(baked.GetPixel(0, 0).linear.r, Is.EqualTo(gray * (1 - gray)).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, mask, main); }
        }

        [Test]
        public void AMultiplyMatcapAveragesAnSrgbImageInLinearValues()
        {
            NoGraphicsDevice.AllowErrorLogs();
            var material = Create(3);
            // Half the disk is mid gray, half white.
            var matcap = Srgb(new Color(.5f, .5f, .5f, 1), Color.white, new Color(.5f, .5f, .5f, 1), Color.white);
            var main = TestTextures.Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapTex", matcap);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                var baked = (Texture2D)fallback.GetTexture("_MainTex");
                Assert.That(textures, Does.Contain(baked));
                NoGraphicsDevice.IgnorePixelChecks();
                var mean = (Mathf.GammaToLinearSpace(.5f) + 1) / 2;
                Assert.That(baked.GetPixel(0, 0).linear.r, Is.EqualTo(mean).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, matcap, main); }
        }

        // A screen MatCap adds src * (1 - dst); the weight image holds 1 - albedo
        // in linear values.
        [Test]
        public void AScreenMatcapWeighsByTheLinearAlbedo()
        {
            var material = Create(2);
            var main = Srgb(new Color(.5f, .5f, .5f, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                var weight = (Texture2D)fallback.GetTexture("_RimTex");
                Assert.That(textures, Does.Contain(weight));
                NoGraphicsDevice.IgnorePixelChecks();
                Assert.That(weight.GetPixel(0, 0).linear.r, Is.EqualTo(1 - Mathf.GammaToLinearSpace(.5f)).Within(.015f));
            }
            finally { TestObjects.Destroy(material, created, textures, main); }
        }

        // A 2x2 sRGB image with point sampling, pixels from the bottom left.
        static Texture2D Srgb(params Color[] pixels)
        {
            if (pixels.Length == 1) pixels = new[] { pixels[0], pixels[0], pixels[0], pixels[0] };
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // 55 materials need 111 images without the bakes and 166 with them.
        [TestCase(55, false)]
        [TestCase(4, true)]
        public void BakesThatPassTheImageLimitAreLeftOut(int count, bool baked)
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new AttachmentConnectionTests.Fixture();
            var quad = new Mesh { name = "MatCap quad" };
            var matcap = TestTextures.Solid(Color.white);
            var mask = TestTextures.Image(Color.black, Color.white, Color.black, Color.white);
            var materials = new List<Material>();
            var images = new List<Texture2D> { matcap, mask };
            try
            {
                quad.vertices = new[] { new Vector3(0, 1, 0), new Vector3(.1f, 1, 0), new Vector3(0, 1.1f, 0) };
                quad.triangles = new[] { 0, 1, 2 };
                quad.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
                quad.normals = Enumerable.Repeat(Vector3.back, 3).ToArray();
                for (var i = 0; i < count; i++)
                {
                    // Distinct colors keep the images from being merged.
                    var main = TestTextures.Solid(new Color(i / 64f, .5f, 1 - i / 64f, 1));
                    var normal = TestTextures.Solid(new Color(.5f, .5f, i / 64f, 1));
                    images.Add(main);
                    images.Add(normal);
                    var material = Create(0);
                    material.name = "MatCap " + i;
                    material.SetTexture("_MainTex", main);
                    material.SetTexture("_MatCapTex", matcap);
                    material.SetTexture("_MatCapBlendMask", mask);
                    material.SetFloat("_MatCapMainStrength", 1);
                    material.SetFloat("_UseBumpMap", 1);
                    material.SetTexture("_BumpMap", normal);
                    materials.Add(material);
                    var part = new GameObject("Part " + i);
                    part.transform.SetParent(fixture.Source.transform, false);
                    part.AddComponent<MeshFilter>().sharedMesh = quad;
                    part.AddComponent<MeshRenderer>().sharedMaterial = material;
                }
                // The export made again keeps one copy of each warning.
                var warnings = new List<string> { "Earlier" };
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "MatCap limit", "Tests", warnings: warnings, exporterVersion: "0.10.0", lilToonVersion: "2.3.4",
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(warnings, Is.Unique);
                Assert.That(warnings, Does.Contain("Earlier"));
                var glb = GlbDocument.Read(bytes);
                var extensions = glb.Json.TryGetValue("extensions", out var raw) ? raw as Dictionary<string, object> : null;
                Assert.That(extensions != null && extensions.ContainsKey(LilToonMobileProfile.ExtensionName), Is.True, "The bakes must not cost the lilToon extension.");
                var textures = LilToonGlbExtension.StandardCounts(bytes).Textures;
                Assert.That(textures, Is.LessThanOrEqualTo(LilToonMobileProfile.MaximumTextures));
                if (baked) Assert.That(textures, Is.GreaterThan(2 * count + 1), "Under the limit the bakes stay.");
                else Assert.That(textures, Is.EqualTo(2 * count + 1), "Over the limit the export is made again without them.");
            }
            finally
            {
                foreach (var material in materials) Object.DestroyImmediate(material);
                foreach (var image in images) Object.DestroyImmediate(image);
                Object.DestroyImmediate(quad);
            }
        }

        private static Material Create(int mode)
        {
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null, "Install supported lilToon.");
            var material = new Material(shader);
            material.SetFloat("_UseMatCap", 1);
            material.SetFloat("_MatCapBlendMode", mode);
            material.SetColor("_MatCapColor", Color.white);
            material.SetFloat("_MatCapBlend", 1);
            material.SetTexture("_MatCapTex", Texture2D.whiteTexture);
            return material;
        }

    }
}
