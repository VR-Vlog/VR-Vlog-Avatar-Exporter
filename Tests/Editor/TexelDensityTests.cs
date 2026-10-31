using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: texture sizes chosen from the texels per metre of the model's surface.
    public sealed class TexelDensityTests
    {
        // A 1m square showing the whole 1024 texture has 1024 texels per metre;
        // twice the size halves it and twice the tiling doubles it.
        [TestCase(1f, 1f, 1024d)]
        [TestCase(2f, 1f, 512d)]
        [TestCase(1f, 2f, 2048d)]
        public void AQuadHasTheExactDensity(float scale, float tiling, double expected)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Quad density" };
            using var fixture = new QuadFixture();
            try
            {
                fixture.Root.transform.localScale = Vector3.one * scale;
                fixture.Material.SetTexture("_MainTex", texture);
                fixture.Material.SetTextureScale("_MainTex", new Vector2(tiling, 1));
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.True);
                Assert.That(plan.Density(texture), Is.EqualTo(expected).Within(1e-6));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void OneTriangleHasTheExactDensityAndADegenerateOneHasNone()
        {
            // UV area .5, world area .125: sqrt(4) = 2 texels per metre per texel.
            var density = TexelDensity.Triangle(Vector2.zero, Vector2.right, Vector2.up,
                Vector3.zero, new Vector3(.5f, 0, 0), new Vector3(0, .5f, 0), 256, 256);
            Assert.That(density, Is.EqualTo(512d).Within(1e-9));
            Assert.That(TexelDensity.Triangle(Vector2.zero, Vector2.right, Vector2.up, Vector3.zero, Vector3.right, Vector3.right * 2, 256, 256), Is.Null);
            Assert.That(TexelDensity.Triangle(Vector2.zero, Vector2.zero, Vector2.up, Vector3.zero, Vector3.right, Vector3.up, 256, 256), Is.Null);
        }

        [Test]
        public void TheDensityOfASubmeshIsTheAreaWeightedNinetiethPercentile()
        {
            Assert.That(TexelDensity.WeightedPercentile(new[] { (100d, 9d), (5000d, 1d) }), Is.EqualTo(100));
            Assert.That(TexelDensity.WeightedPercentile(new[] { (100d, 8d), (5000d, 2d) }), Is.EqualTo(5000));
            Assert.That(double.IsNaN(TexelDensity.WeightedPercentile(Array.Empty<(double, double)>())), Is.True);
        }

        // Halving starts from the size already written and stops when the half
        // would fall below the target or the smallest size.
        [TestCase(4800d, 1024, 1024, 256)]
        [TestCase(2400d, 1024, 1024, 512)]
        [TestCase(2399d, 1024, 1024, 1024)]
        [TestCase(4800d, 4096, 1024, 1024)]
        [TestCase(1e9, 1024, 1024, 32)]
        [TestCase(1e9, 48, 48, 48)]
        [TestCase(double.NaN, 1024, 1024, 1024)]
        public void HalvingStopsAtTheTargetAndTheSmallestSize(double density, int source, int written, int expected)
        {
            Assert.That(TexelDensity.ReducedSize(density, source, written), Is.EqualTo(expected));
        }

        [Test]
        public void SavedBytesCountEveryMipLevel()
        {
            Assert.That(TexelDensity.Bytes(1, 1), Is.EqualTo(4));
            Assert.That(TexelDensity.Bytes(2, 2), Is.EqualTo(20));
            Assert.That(TexelDensity.Bytes(4, 1), Is.EqualTo(28));
            Assert.That(TexelDensity.Bytes(512, 512) - TexelDensity.Bytes(256, 256), Is.EqualTo(1024L * 1024));
        }

        [TestCase("_MatCapTex")]
        [TestCase("_RimColorTex")]
        [TestCase("_MainGradationTex")]
        [TestCase("_DitherTex")]
        public void ASlotWithoutTheSurfaceUvNeverShrinks(string slot)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Never shrink " + slot };
            using var fixture = new QuadFixture();
            try
            {
                Assert.That(fixture.Material.HasProperty(slot), Is.True, slot);
                Assert.That(TexelDensity.NeverShrinks(slot), Is.True);
                fixture.Material.SetTexture(slot, texture);
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.False);
                Assert.That(plan.Maximum(texture, 1024), Is.EqualTo(1024));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(0f, true)]
        [TestCase(1f, false)]
        public void ASlotOnAnotherUvNeverShrinks(float uvMode, bool measured)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Emission UV mode" };
            using var fixture = new QuadFixture();
            try
            {
                Assert.That(fixture.Material.HasProperty("_EmissionMap_UVMode"), Is.True);
                fixture.Material.SetTexture("_EmissionMap", texture);
                fixture.Material.SetFloat("_EmissionMap_UVMode", uvMode);
                Assert.That(TexelDensity.Measure(fixture.Root).Measured(texture), Is.EqualTo(measured));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [TestCase(TextureFormat.RGBAHalf)]
        [TestCase(TextureFormat.RGBAFloat)]
        public void AnHdrTextureNeverShrinks(TextureFormat format)
        {
            var texture = new Texture2D(1024, 1024, format, false, true) { name = "HDR " + format };
            using var fixture = new QuadFixture();
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                Assert.That(TexelDensity.Measure(fixture.Root).Measured(texture), Is.False);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void ATextureSharedWithANeverShrinkSlotDoesNotShrink()
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Shared main and MatCap" };
            using var fixture = new QuadFixture();
            using var other = new QuadFixture();
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                other.Root.transform.SetParent(fixture.Root.transform, false);
                Assert.That(TexelDensity.Measure(fixture.Root).Measured(texture), Is.True, "Alone the main slot is measured.");
                other.Material.SetTexture("_MatCapTex", texture);
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.False);
                Assert.That(plan.Maximum(texture, 1024), Is.EqualTo(1024));
                Assert.That(plan.ResultLine(), Is.Null);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        [Test]
        public void AMeshWithoutUvHasNoMeasurableUser()
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "No UV" };
            using var fixture = new QuadFixture(uv: false);
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                Assert.That(TexelDensity.Measure(fixture.Root).Measured(texture), Is.False);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // One line for the whole export: each texture counts once, however often it is written.
        [Test]
        public void TheResultLineCountsEachSmallerTextureOnce()
        {
            var dense = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Dense" };
            var sparse = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Sparse" };
            try
            {
                var plan = new TexelDensity.Plan(new Dictionary<Texture, double> { [dense] = 4800, [sparse] = 600 });
                Assert.That(plan.ResultLine(), Is.Null);
                Assert.That(plan.Maximum(dense, 1024), Is.EqualTo(256));
                Assert.That(plan.Maximum(dense, 1024), Is.EqualTo(256));
                Assert.That(plan.Maximum(sparse, 1024), Is.EqualTo(1024));
                Assert.That(plan.Count, Is.EqualTo(1));
                var saved = TexelDensity.Bytes(1024, 1024) - TexelDensity.Bytes(256, 256);
                Assert.That(plan.SavedBytes, Is.EqualTo(saved));
                Assert.That(plan.ResultLine(), Is.EqualTo(TexelDensity.Plan.Line(1, saved)));
                Assert.That(TexelDensity.Plan.Megabytes(saved), Is.EqualTo("5"));
                Assert.That(TexelDensity.Plan.Megabytes(1024L * 1024 * 3 / 2), Is.EqualTo("1.5"));
                Assert.That(string.Format(CultureInfo.InvariantCulture, ExportNoteText.TexturesLightened, 1, "5.3"),
                    Is.EqualTo("テクスチャ1枚を小さくしてメモリを5.3MB減らしました"));
            }
            finally { Object.DestroyImmediate(dense); Object.DestroyImmediate(sparse); }
        }

        // Off: nothing is measured and the export keeps today's size. On: the
        // fixture's triangle (5 texels per metre per texel) makes 512 into 256.
        [TestCase(false)]
        [TestCase(true)]
        public void TheExportShrinksOnlyWhenTheOptionIsOn(bool on)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")) { name = "Lightweight material" };
            var main = new Texture2D(512, 512, TextureFormat.RGBA32, true) { name = "Lightweight main" };
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                main.SetPixels32(Enumerable.Repeat(new Color32(90, 140, 200, 255), 512 * 512).ToArray());
                main.Apply(true, false);
                material.SetTexture("_MainTex", main);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var measurements = TexelDensity.Measurements;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Lightweight", "Tests", warnings,
                    exporterVersion: "tests", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    autoLightweight: on);
                Assert.That(TexelDensity.Measurements - measurements, Is.EqualTo(on ? 1 : 0));
                var size = on ? 256 : 512;
                var glb = GlbDocument.Read(bytes);
                var record = F.List(F.Root(glb.Json), "textures").Select(F.Object).Single(t => F.Text(t, "name") == main.name);
                Assert.That(F.Int(record, "width"), Is.EqualTo(size));
                Assert.That(F.Int(record, "height"), Is.EqualTo(size));
                var widths = F.List(glb.Json, "images").Select(F.Object).Select(image =>
                {
                    var view = F.Object(F.At(F.List(glb.Json, "bufferViews"), F.Int(image, "bufferView")));
                    var data = new byte[F.Int(view, "byteLength")];
                    Buffer.BlockCopy(glb.Binary, view.ContainsKey("byteOffset") ? F.Int(view, "byteOffset") : 0, data, 0, data.Length);
                    Assert.That(ImageConversion.LoadImage(decoded, data, false), Is.True);
                    return decoded.width;
                }).ToArray();
                Assert.That(widths, Does.Contain(size), "The MToon image of the main texture follows the same size.");
                Assert.That(widths, Does.Not.Contain(on ? 512 : 256));
                var line = TexelDensity.Plan.Line(1, TexelDensity.Bytes(512, 512) - TexelDensity.Bytes(256, 256));
                Assert.That(warnings.Count(w => w == line), Is.EqualTo(on ? 1 : 0));
                Assert.That(main.width, Is.EqualTo(512), "The source texture is not changed.");
                Assert.That(material.GetTexture("_MainTex"), Is.SameAs(main));
            }
            finally
            {
                Object.DestroyImmediate(decoded);
                Object.DestroyImmediate(main);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var texts = new[]
            {
                ExperimentalExpressionCaptureWindow.AutoLightweightLabel, ExperimentalExpressionCaptureWindow.AutoLightweightNote,
                ExportNoteText.TexturesLightened,
            };
            foreach (var text in texts)
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S"), Is.False, text);
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var table = File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var text in texts)
                    Assert.That(table, Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale + ": " + text);
            }
        }

        // A 1m square on a MeshRenderer with a lilToon material, UV 0..1.
        sealed class QuadFixture : IDisposable
        {
            internal readonly GameObject Root = new GameObject("Texel density quad");
            internal readonly Material Material;
            readonly Mesh mesh;

            internal QuadFixture(bool uv = true)
            {
                var shader = Shader.Find("lilToon");
                Assert.That(shader, Is.Not.Null);
                Material = new Material(shader);
                mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, Vector3.right, Vector3.up, new Vector3(1, 1, 0) },
                    triangles = new[] { 0, 2, 1, 1, 2, 3 },
                };
                if (uv) mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                Root.AddComponent<MeshFilter>().sharedMesh = mesh;
                Root.AddComponent<MeshRenderer>().sharedMaterial = Material;
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Material);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
