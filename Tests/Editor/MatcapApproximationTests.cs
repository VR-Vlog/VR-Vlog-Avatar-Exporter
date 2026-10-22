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
            var mask = Image(Color.black, Color.white, Color.black, Color.white);
            var main = Solid(Color.white);
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
                Assert.That(Linear(weight.GetPixel(0, 0)).maxColorComponent, Is.EqualTo(0f).Within(.015f), "Black mask must not add the MatCap.");
                Assert.That(Linear(weight.GetPixel(1, 0)).r, Is.EqualTo(1f).Within(.015f));
                Assert.AreSame(main, fallback.GetTexture("_MainTex"), "An added MatCap does not change the main image.");
                Assert.AreSame(mask, material.GetTexture("_MatCapBlendMask"));
                // Without a MatCap image there is nothing to weigh.
                material.SetTexture("_MatCapTex", null);
                Assert.IsNull(UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures).GetTexture("_RimTex"));
                Assert.That(textures, Has.Count.EqualTo(1));
            }
            finally { Destroy(material, created, textures, mask, main); }
        }

        [Test]
        public void MainStrengthAndScreenScaleTheAddedMatcap()
        {
            var material = Create(2);
            var main = Solid(new Color(.5f, .25f, 1, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetFloat("_MatCapMainStrength", 1);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                var pixel = Linear(((Texture2D)fallback.GetTexture("_RimTex")).GetPixel(0, 0));
                // albedo * (1 - albedo) for main strength 1 and screen.
                Assert.That(pixel.r, Is.EqualTo(.25f).Within(.015f));
                Assert.That(pixel.g, Is.EqualTo(.1875f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(0f).Within(.015f));
            }
            finally { Destroy(material, created, textures, main); }
        }

        [Test]
        public void UnmaskedAddedMatcapAndRimLightKeepTheirImages()
        {
            var material = Create(1);
            var mask = Solid(Color.black);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                foreach (var unmasked in new Texture[] { null, Texture2D.whiteTexture })
                {
                    material.SetTexture("_MatCapBlendMask", unmasked);
                    var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                    Assert.IsNull(fallback.GetTexture("_RimTex"));
                    Assert.That(textures, Is.Empty);
                }
                // The rim image also scales MToon's rim light; keep it for that.
                material.SetTexture("_MatCapBlendMask", mask);
                material.SetFloat("_UseRim", 1);
                UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.That(textures, Is.Empty);
            }
            finally { Destroy(material, created, textures, mask); }
        }

        [Test]
        public void MultiplyMatcapDarkensTheMainImageWhereMasked()
        {
            var material = Create(3);
            var matcap = Solid(new Color(.5f, .25f, 1, 1));
            var mask = Image(Color.black, Color.white, Color.black, Color.white);
            var main = Solid(Color.white);
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
                Assert.That(Linear(baked.GetPixel(0, 0)).r, Is.EqualTo(1f).Within(.015f), "Black mask keeps the main image.");
                var pixel = Linear(baked.GetPixel(1, 0));
                Assert.That(pixel.r, Is.EqualTo(.5f).Within(.015f));
                Assert.That(pixel.g, Is.EqualTo(.25f).Within(.015f));
                Assert.That(pixel.b, Is.EqualTo(1f).Within(.015f));
                Assert.IsNull(fallback.GetTexture("_MatcapTex"), "MToon would add a multiply MatCap.");
                Assert.AreSame(main, material.GetTexture("_MainTex"));
            }
            finally { Destroy(material, created, textures, matcap, mask, main); }
        }

        [Test]
        public void NormalMatcapDarkensByItsAlphaAndIsAdded()
        {
            var material = Create(0);
            var matcap = Solid(new Color(1, 1, 1, .5f));
            var main = Solid(new Color(.8f, .8f, .8f, 1));
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapTex", matcap);
                material.SetFloat("_MatCapBlend", .5f);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                // lerp(dst, src, blend * a) keeps 1 - .5 * .5 of dst.
                var pixel = Linear(((Texture2D)fallback.GetTexture("_MainTex")).GetPixel(0, 0));
                Assert.That(pixel.r, Is.EqualTo(.6f).Within(.015f));
                Assert.That(Linear(((Texture2D)fallback.GetTexture("_MainTex")).GetPixel(0, 0)).a, Is.EqualTo(1f).Within(.015f));
                Assert.AreSame(matcap, fallback.GetTexture("_MatcapTex"));
            }
            finally { Destroy(material, created, textures, matcap, main); }
        }

        [Test]
        public void WhiteMultiplyMatcapLeavesTheMainImage()
        {
            var material = Create(3);
            var main = Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapTex", Texture2D.whiteTexture);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                Assert.AreSame(main, fallback.GetTexture("_MainTex"));
                Assert.That(textures, Is.Empty);
            }
            finally { Destroy(material, created, textures, main); }
        }

        [Test]
        public void NormalMatcapWithoutAnImageLeavesTheMainImage()
        {
            var material = Create(0);
            var main = Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                material.SetTexture("_MainTex", main);
                material.SetTexture("_MatCapTex", null);
                var fallback = UniVrmOneClickExporter.CreateMToonFallback(material, created, null, false, textures);
                // MToon adds nothing without the image, so darkening would only lose light.
                Assert.AreSame(main, fallback.GetTexture("_MainTex"));
                Assert.That(textures, Is.Empty);
            }
            finally { Destroy(material, created, textures, main); }
        }

        [Test]
        public void MaterialsWithTheSameInputsShareOneBake()
        {
            var first = Create(3);
            var second = Create(3);
            var matcap = Solid(new Color(.5f, .25f, 1, 1));
            var main = Solid(Color.white);
            var created = new List<Material>();
            var textures = new List<Texture2D>();
            try
            {
                foreach (var material in new[] { first, second })
                {
                    material.SetTexture("_MainTex", main);
                    material.SetTexture("_MatCapTex", matcap);
                }
                var bakes = new MatcapApproximation.Bakes(true);
                var a = UniVrmOneClickExporter.CreateMToonFallback(first, created, null, false, textures, matcapBakes: bakes);
                var b = UniVrmOneClickExporter.CreateMToonFallback(second, created, null, false, textures, matcapBakes: bakes);
                Assert.That(textures, Has.Count.EqualTo(1));
                Assert.AreSame(a.GetTexture("_MainTex"), b.GetTexture("_MainTex"));
                second.SetColor("_Color", new Color(.5f, .5f, .5f, 1));
                var c = UniVrmOneClickExporter.CreateMToonFallback(second, created, null, false, textures, matcapBakes: bakes);
                Assert.That(textures, Has.Count.EqualTo(2), "A different main color is its own bake.");
                Assert.AreNotSame(a.GetTexture("_MainTex"), c.GetTexture("_MainTex"));
                // Disabled bakes keep the source images.
                var d = UniVrmOneClickExporter.CreateMToonFallback(first, created, null, false, textures, matcapBakes: new MatcapApproximation.Bakes(false));
                Assert.AreSame(main, d.GetTexture("_MainTex"));
                Assert.That(textures, Has.Count.EqualTo(2));
            }
            finally { Object.DestroyImmediate(second); Destroy(first, created, textures, matcap, main); }
        }

        // 55 materials need 111 images without the bakes and 166 with them.
        [TestCase(55, false, false)]
        [TestCase(55, false, true)]
        [TestCase(4, true, false)]
        public void BakesThatPassTheImageLimitAreLeftOut(int count, bool baked, bool lenient)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var quad = new Mesh { name = "MatCap quad" };
            var matcap = Solid(Color.white);
            var mask = Image(Color.black, Color.white, Color.black, Color.white);
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
                    var main = Solid(new Color(i / 64f, .5f, 1 - i / 64f, 1));
                    var normal = Solid(new Color(.5f, .5f, i / 64f, 1));
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
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "MatCap limit", "Tests", warnings, exporterVersion: "0.10.0", lilToonVersion: "2.3.4",
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    recoveryReport: new ExportRecoveryReport { ContinueOnStageFailure = lenient });
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
