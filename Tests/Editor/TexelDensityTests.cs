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
        // twice the size halves it and twice the tiling doubles it. Of uneven
        // tiling only the smaller axis counts, so both axes keep the target.
        [TestCase(1f, 1f, 1f, 1024d)]
        [TestCase(2f, 1f, 1f, 512d)]
        [TestCase(1f, 2f, 2f, 2048d)]
        [TestCase(1f, 2f, 1f, 1024d)]
        [TestCase(1f, -4f, 2f, 2048d)]
        public void AQuadHasTheExactDensity(float scale, float tilingX, float tilingY, double expected)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Quad density" };
            using var fixture = new QuadFixture();
            try
            {
                fixture.Root.transform.localScale = Vector3.one * scale;
                fixture.Material.SetTexture("_MainTex", texture);
                fixture.Material.SetTextureScale("_MainTex", new Vector2(tilingX, tilingY));
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.True);
                Assert.That(plan.Density(texture), Is.EqualTo(expected).Within(1e-6));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // A skinned quad drawn as 1m × 1m has the density of the unscaled 1m
        // quad, whatever scale its renderer, the renderer's parent or its bone
        // carries (a mesh in centimetres under a renderer scaled 0.01 was
        // measured 100 times too dense and shrunk to the smallest size).
        [TestCase(1f, 1f, 1f, 1f, 1f)]
        [TestCase(.01f, .01f, .01f, 1f, 1f)]
        [TestCase(1f, 1f, 1f, .01f, 1f)]
        [TestCase(1f, 1f, 1f, 1f, .01f)]
        [TestCase(.02f, .005f, 1f, 1f, 1f)]
        [TestCase(.01f, .01f, .01f, 1f, 2f)]
        public void ASkinnedQuadOfOneSquareMetreHasTheUnscaledDensity(float x, float y, float z, float parent, float bone)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Skinned quad density" };
            using var fixture = new SkinnedQuadFixture(new Vector3(x, y, z), parent, bone);
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.True);
                Assert.That(plan.Density(texture), Is.EqualTo(1024d).Within(.01).Percent);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // The same on a MeshRenderer, scaled itself or through its parent.
        [TestCase(.01f, .01f, 1f)]
        [TestCase(.02f, .005f, 1f)]
        [TestCase(1f, 1f, .01f)]
        public void AMeshRendererQuadOfOneSquareMetreHasTheUnscaledDensity(float x, float y, float parent)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Scaled quad density" };
            using var fixture = new QuadFixture(width: 1 / (x * parent), height: 1 / (y * parent));
            var holder = new GameObject("Holder");
            try
            {
                holder.transform.SetPositionAndRotation(new Vector3(.3f, .5f, -.2f), Quaternion.Euler(20, 30, 40));
                holder.transform.localScale = Vector3.one * parent;
                fixture.Root.transform.SetParent(holder.transform, false);
                fixture.Root.transform.localScale = new Vector3(x, y, 1);
                fixture.Material.SetTexture("_MainTex", texture);
                var plan = TexelDensity.Measure(holder);
                Assert.That(plan.Measured(texture), Is.True);
                Assert.That(plan.Density(texture), Is.EqualTo(1024d).Within(.01).Percent);
            }
            finally
            {
                Object.DestroyImmediate(holder);
                Object.DestroyImmediate(texture);
            }
        }

        // On a mesh renderer, a measured size more than twice or less than half
        // the size of its bounds is not trusted: the textures keep their size, so
        // a wrong measurement costs memory, never looks. Tiled 4 times, the quad
        // has 4096 texels per metre and would be written at 512.
        [TestCase(1.5f, true)]
        [TestCase(.6f, true)]
        [TestCase(3f, false)]
        [TestCase(.3f, false)]
        public void AMeasurementFarFromTheRendererBoundsKeepsTheSize(float factor, bool measured)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Bounds rail" };
            using var fixture = new QuadFixture();
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                fixture.Material.SetTextureScale("_MainTex", new Vector2(4, 4));
                var bounds = fixture.Mesh.bounds;
                bounds.size *= factor;
                fixture.Mesh.bounds = bounds;
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.EqualTo(measured));
                Assert.That(plan.Maximum(texture, 1024), Is.EqualTo(measured ? 512 : 1024));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // On a skinned mesh the size to trust is where the bones alone place the
        // vertices, not the bounds, which many avatars widen on purpose. A blend
        // shape that scales the quad stands in for a wrong measurement: within
        // twice or half the bones' size the quad shrinks as measured (4096 texels
        // per metre over the size squared), beyond it the textures keep their
        // size. Bounds three times too large or small change nothing.
        [TestCase(1.5f, 1f, 512)]
        [TestCase(.6f, 1f, 256)]
        [TestCase(3f, 1f, 1024)]
        [TestCase(.3f, 1f, 1024)]
        [TestCase(1f, 3f, 512)]
        [TestCase(1f, .3f, 512)]
        public void ASkinnedMeasurementFarFromItsBonesKeepsTheSize(float shape, float bounds, int expected)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Bone rail" };
            using var fixture = new SkinnedQuadFixture(Vector3.one * .01f, shape: shape);
            try
            {
                fixture.Material.SetTexture("_MainTex", texture);
                fixture.Material.SetTextureScale("_MainTex", new Vector2(4, 4));
                fixture.Skin.localBounds = fixture.DrawnBounds(bounds);
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.EqualTo(expected != 1024));
                Assert.That(plan.Maximum(texture, 1024), Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // Pins how Unity bakes a skinned mesh under a scaled renderer. With the
        // scale, the vertices are in the renderer's own space and its whole
        // transform gives where they are drawn. Without it they are already
        // drawn size, in a frame with the renderer's position and rotation only,
        // so the whole transform scales them a second time. A scaled bone is in
        // the skinning either way.
        [TestCase(1f, 1f, 1f, 1f, 1f)]
        [TestCase(.01f, .01f, .01f, 1f, 1f)]
        [TestCase(1f, 1f, 1f, .01f, 1f)]
        [TestCase(1f, 1f, 1f, 1f, .01f)]
        [TestCase(.02f, .005f, 1f, 1f, 1f)]
        [TestCase(.01f, .01f, .01f, 1f, 2f)]
        public void BakeMeshWithTheScaleIsInTheRenderersOwnSpace(float x, float y, float z, float parent, float bone)
        {
            using var fixture = new SkinnedQuadFixture(new Vector3(x, y, z), parent, bone);
            var drawn = fixture.Drawn();
            var baked = new Mesh();
            try
            {
                var transform = fixture.Skin.transform;
                var whole = transform.localToWorldMatrix;
                var rigid = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
                double Error(Matrix4x4 matrix) => baked.vertices.Select((v, i) => (double)(matrix.MultiplyPoint3x4(v) - drawn[i]).magnitude).Max();
                var scaled = x * parent != 1 || y * parent != 1 || z * parent != 1;
                fixture.Skin.BakeMesh(baked, true);
                Assert.That(Error(whole), Is.LessThan(1e-4));
                if (scaled) Assert.That(Error(rigid), Is.GreaterThan(.5));
                fixture.Skin.BakeMesh(baked, false);
                Assert.That(Error(rigid), Is.LessThan(1e-4));
                if (scaled) Assert.That(Error(whole), Is.GreaterThan(.5));
            }
            finally { Object.DestroyImmediate(baked); }
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

        // The shader ignores the _ST of a slot without scale and offset, so a
        // stale tiling there does not count; the main tiling still does.
        [TestCase(1f, 1024d)]
        [TestCase(.5f, 512d)]
        public void AStaleTilingOfASlotWithoutScaleOffsetIsIgnored(float mainTiling, double expected)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "Shadow color" };
            using var fixture = new QuadFixture();
            try
            {
                var shader = fixture.Material.shader;
                var index = shader.FindPropertyIndex("_ShadowColorTex");
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                Assert.That(shader.GetPropertyFlags(index) & UnityEngine.Rendering.ShaderPropertyFlags.NoScaleOffset, Is.Not.Zero);
                fixture.Material.SetTexture("_ShadowColorTex", texture);
                fixture.Material.SetTextureScale("_ShadowColorTex", new Vector2(4, 4));
                fixture.Material.SetTextureScale("_MainTex", new Vector2(mainTiling, mainTiling));
                Assert.That(fixture.Material.GetTextureScale("_ShadowColorTex"), Is.EqualTo(new Vector2(4, 4)));
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.True);
                Assert.That(plan.Density(texture), Is.EqualTo(expected).Within(1e-6));
            }
            finally { Object.DestroyImmediate(texture); }
        }

        // Halving an MSDF texture breaks its edges.
        [TestCase("_Main2ndTex", 0f, true)]
        [TestCase("_Main2ndTex", 1f, false)]
        [TestCase("_Main3rdTex", 1f, false)]
        public void AnMsdfTextureNeverShrinks(string slot, float msdf, bool measured)
        {
            var texture = new Texture2D(1024, 1024, TextureFormat.RGBA32, false) { name = "MSDF " + slot };
            using var fixture = new QuadFixture();
            try
            {
                Assert.That(fixture.Material.HasProperty(slot + "IsMSDF"), Is.True, slot);
                fixture.Material.SetTexture(slot, texture);
                fixture.Material.SetFloat(slot + "IsMSDF", msdf);
                var plan = TexelDensity.Measure(fixture.Root);
                Assert.That(plan.Measured(texture), Is.EqualTo(measured));
                if (!measured) Assert.That(plan.Maximum(texture, 1024), Is.EqualTo(1024));
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
                Assert.That(TexelDensity.Plan.Megabytes(saved), Is.EqualTo("5.0"));
                Assert.That(TexelDensity.Plan.Megabytes(1024L * 1024 * 3 / 2), Is.EqualTo("1.5"));
                Assert.That(TexelDensity.Plan.Megabytes(1024L * 1024 * 8), Is.EqualTo("8.0"));
                Assert.That(TexelDensity.Plan.Megabytes(300L * 1024), Is.EqualTo("0.3"));
                Assert.That(TexelDensity.Plan.Megabytes(1), Is.EqualTo("0.1"), "Never 0MB.");
                Assert.That(string.Format(CultureInfo.InvariantCulture, ExportNoteText.TexturesLightened, 1, "5.3"),
                    Is.EqualTo("テクスチャ1枚を小さくしてメモリ使用量を5.3MB減らしました"));
            }
            finally { Object.DestroyImmediate(dense); Object.DestroyImmediate(sparse); }
        }

        // Off: nothing is measured and the export keeps today's size. On: the
        // fixture's triangle (5 texels per metre per texel) makes 512 into 256,
        // and 2048 also into 256. The default's resizing of 2048 to 1024 is noted
        // per texture with 1024 either way; the smaller size is in the result line only.
        [TestCase(false, 512)]
        [TestCase(true, 512)]
        [TestCase(false, 2048)]
        [TestCase(true, 2048)]
        public void TheExportShrinksOnlyWhenTheOptionIsOn(bool on, int source)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")) { name = "Lightweight material" };
            var main = new Texture2D(source, source, TextureFormat.RGBA32, true) { name = "Lightweight main" };
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                main.SetPixels32(Enumerable.Repeat(new Color32(90, 140, 200, 255), source * source).ToArray());
                main.Apply(true, false);
                material.SetTexture("_MainTex", main);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var measurements = TexelDensity.Measurements;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Lightweight", "Tests", warnings,
                    exporterVersion: "tests", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    autoLightweight: on);
                Assert.That(TexelDensity.Measurements - measurements, Is.EqualTo(on ? 1 : 0));
                var capped = Math.Min(source, 1024);
                var size = on ? 256 : capped;
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
                Assert.That(widths, Has.No.Member(on ? capped : 256));
                var line = TexelDensity.Plan.Line(1, TexelDensity.Bytes(capped, capped) - TexelDensity.Bytes(256, 256));
                Assert.That(warnings.Count(w => w == line), Is.EqualTo(on ? 1 : 0));
                var notes = source > capped
                    ? new[]
                    {
                        string.Format(ExportNoteText.LilToonTextureResized, main.name, source, source, capped, capped),
                        string.Format(ExportNoteText.OutputTextureResized, main.name, source, source, capped, capped),
                    }
                    : Array.Empty<string>();
                Assert.That(warnings.Where(w => w.Contains(main.name)), Is.EquivalentTo(notes));
                Assert.That(main.width, Is.EqualTo(source), "The source texture is not changed.");
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
            internal readonly Mesh Mesh;

            internal QuadFixture(bool uv = true, float width = 1, float height = 1)
            {
                var shader = Shader.Find("lilToon");
                Assert.That(shader, Is.Not.Null);
                Material = new Material(shader);
                Mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, new Vector3(width, 0, 0), new Vector3(0, height, 0), new Vector3(width, height, 0) },
                    triangles = new[] { 0, 2, 1, 1, 2, 3 },
                };
                if (uv) Mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                Root.AddComponent<MeshFilter>().sharedMesh = Mesh;
                Root.AddComponent<MeshRenderer>().sharedMaterial = Material;
            }

            public void Dispose()
            {
                if (Root != null) Object.DestroyImmediate(Root);
                Object.DestroyImmediate(Material);
                Object.DestroyImmediate(Mesh);
            }
        }

        // A skinned quad drawn as 1m × 1m with a lilToon material, UV 0..1. The
        // mesh units shrink by every scale on the way, like a mesh in centimetres
        // under a renderer scaled 0.01; the renderer may also sit under a scaled
        // parent, and the bone may be scaled after binding. A blend shape at full
        // weight may scale the quad by shape. The avatar is moved and turned so
        // no frame is the world by chance.
        sealed class SkinnedQuadFixture : IDisposable
        {
            internal readonly GameObject Root = new GameObject("Skinned texel density quad");
            internal readonly Material Material;
            internal readonly SkinnedMeshRenderer Skin;
            readonly Transform bone;
            readonly Mesh mesh;

            internal SkinnedQuadFixture(Vector3 rendererScale, float parentScale = 1, float boneScale = 1, float shape = 1)
            {
                var shader = Shader.Find("lilToon");
                Assert.That(shader, Is.Not.Null);
                Material = new Material(shader);
                Root.transform.SetPositionAndRotation(new Vector3(.3f, .5f, -.2f), Quaternion.Euler(20, 30, 40));
                bone = new GameObject("Bone").transform;
                bone.SetParent(Root.transform, false);
                var holder = new GameObject("Holder").transform;
                holder.SetParent(Root.transform, false);
                holder.localScale = Vector3.one * parentScale;
                var body = new GameObject("Body");
                body.transform.SetParent(holder, false);
                body.transform.localScale = rendererScale;
                var x = 1 / (rendererScale.x * parentScale * boneScale);
                var y = 1 / (rendererScale.y * parentScale * boneScale);
                mesh = new Mesh
                {
                    vertices = new[] { Vector3.zero, new Vector3(x, 0, 0), new Vector3(0, y, 0), new Vector3(x, y, 0) },
                    triangles = new[] { 0, 2, 1, 1, 2, 3 },
                    uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                    boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1 }, 4).ToArray(),
                    bindposes = new[] { bone.worldToLocalMatrix * body.transform.localToWorldMatrix },
                };
                if (shape != 1) mesh.AddBlendShapeFrame("Scale", 100, mesh.vertices.Select(v => v * (shape - 1)).ToArray(), null, null);
                Skin = body.AddComponent<SkinnedMeshRenderer>();
                Skin.sharedMesh = mesh;
                Skin.bones = new[] { bone };
                Skin.rootBone = bone;
                Skin.sharedMaterial = Material;
                if (shape != 1) Skin.SetBlendShapeWeight(0, 100);
                bone.localScale = Vector3.one * boneScale;
                // The bounds an importer stores: the drawn quad in the root bone's space.
                Skin.localBounds = DrawnBounds(1);
            }

            // Where skinning draws each vertex.
            internal Vector3[] Drawn() => mesh.vertices.Select(v => bone.localToWorldMatrix.MultiplyPoint3x4(mesh.bindposes[0].MultiplyPoint3x4(v))).ToArray();

            // The drawn quad in the root bone's space, its size times factor.
            internal Bounds DrawnBounds(float factor)
            {
                var drawn = Drawn().Select(bone.InverseTransformPoint).ToArray();
                var bounds = new Bounds(drawn[0], Vector3.zero);
                foreach (var point in drawn) bounds.Encapsulate(point);
                bounds.size *= factor;
                return bounds;
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
