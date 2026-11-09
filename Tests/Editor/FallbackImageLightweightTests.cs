using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: the MToon images only the lilToon extension's materials
    // use are written at a long side of 256, 512 for the face.
    public sealed class FallbackImageLightweightTests
    {
        const int Width = 1024, Height = 512;

        // Image 0: base colour of a material only bound nodes draw. Image 1:
        // base colour of the face material (the expressions move its mesh).
        // Image 2: of a material a node without a binding draws too. Image 3:
        // also the thumbnail. Image 4: colour in one slot, data in another.
        // Image 5: normal map of the bound material.
        [Test]
        public void OnlyImagesNothingElseUsesBecomeSmaller()
        {
            var source = Build();
            var before = GlbDocument.Read(source);
            var result = FallbackImageLightweight.Apply(source, null, out var count, out var saved);
            Assert.That(count, Is.EqualTo(3));
            Assert.That(saved, Is.EqualTo(source.Length - result.Length).And.GreaterThan(0));
            var after = GlbDocument.Read(result);
            Assert.That(Size(after, 0), Is.EqualTo((256, 128)));
            Assert.That(Size(after, 1), Is.EqualTo((512, 256)));
            Assert.That(Size(after, 5), Is.EqualTo((256, 128)));
            foreach (var kept in new[] { 2, 3, 4 }) Assert.That(Png(after, kept), Is.EqualTo(Png(before, kept)), "image " + kept);
            // The constant channels keep their value, the colour image after
            // sRGB averaging and the normal map after linear averaging.
            foreach (var image in new[] { 0, 1, 5 })
                foreach (var pixel in Pixels(after, image))
                {
                    Assert.That(pixel.r, Is.EqualTo(200).Within(1), "image " + image);
                    Assert.That(pixel.b, Is.EqualTo(50).Within(1), "image " + image);
                    Assert.That(pixel.a, Is.EqualTo(255), "image " + image);
                }
            var chunk = F.Object(F.List(F.Object(F.Object(after.Json["extensions"])[F.ExtensionName]), "chunks")[0]);
            Assert.That(View(after, F.Int(chunk, "bufferView")), Is.EqualTo(new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 }));
            Assert.That(F.List(after.Json, "bufferViews").Count, Is.EqualTo(F.List(before.Json, "bufferViews").Count));
        }

        // The materials follow the app: a record, and no node without a
        // binding draws it.
        [Test]
        public void TheMaterialsFollowTheAppsFullyBoundRule()
        {
            var json = GlbDocument.Read(Build()).Json;
            Assert.That(FallbackImageLightweight.FullyBoundMaterials(json), Is.EquivalentTo(new[] { 0, 1, 3 }));
            Assert.That(FallbackImageLightweight.FaceMaterials(json), Is.EquivalentTo(new[] { 1 }));
            var plan = FallbackImageLightweight.Plan(json);
            Assert.That(plan.Keys, Is.EquivalentTo(new[] { 0, 1, 5 }));
            Assert.That(plan[0], Is.EqualTo((true, 256)));
            Assert.That(plan[1], Is.EqualTo((true, 512)));
            Assert.That(plan[5], Is.EqualTo((false, 256)));
        }

        // A file without the full lilToon extension, or with an extension
        // this step has not checked, is kept.
        [Test]
        public void AFileWithoutTheExtensionOrWithAnUnknownOneIsKept()
        {
            var glb = GlbDocument.Read(Build());
            F.Object(glb.Json["extensions"]).Remove(F.ExtensionName);
            var without = glb.Write();
            Assert.That(FallbackImageLightweight.Apply(without, null, out var count, out var saved), Is.SameAs(without));
            Assert.That(count, Is.Zero);
            glb = GlbDocument.Read(Build());
            glb.Json["extensionsUsed"] = new List<object> { F.ExtensionName, "EXT_unknown" };
            var unknown = glb.Write();
            var warnings = new List<string>();
            Assert.That(FallbackImageLightweight.Apply(unknown, warnings, out count, out saved), Is.SameAs(unknown));
            Assert.That(warnings, Is.EqualTo(new[] { string.Format(FallbackImageLightweight.UnknownExtensionWarning, "EXT_unknown") }));
        }

        // Through the exporter and back through the vendored importer. Off,
        // the MToon image keeps the export's size; on, it is at most 512 and
        // the extension still has its own copy. The result list has the line
        // only when on.
        [TestCase(false)]
        [TestCase(true)]
        public async Task TheExportShrinksTheImagesOnlyWhenTheOptionIsOn(bool on)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var shader = Shader.Find("lilToon");
            Assert.That(shader, Is.Not.Null);
            var material = new Material(shader) { name = "Fallback image material" };
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Fallback image" };
            Vrm10Instance imported = null;
            try
            {
                texture.SetPixels32(Noise(Width, Height));
                texture.Apply(false);
                material.SetTexture("_MainTex", texture);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Fallback images", "Tests", warnings: warnings,
                    exporterVersion: "tests", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None },
                    autoLightweight: on);
                var glb = GlbDocument.Read(bytes);
                var images = F.List(glb.Json, "images").Count;
                var sizes = Enumerable.Range(0, images).Select(i => Size(glb, i)).ToList();
                Assert.That(sizes.Any(s => s == (Width, Height)), Is.EqualTo(!on), string.Join(", ", sizes));
                if (on) Assert.That(sizes.Max(s => Math.Max(s.Item1, s.Item2)), Is.LessThanOrEqualTo(FallbackImageLightweight.FaceMaximum));
                var extension = F.Object(F.Object(glb.Json["extensions"])[F.ExtensionName]);
                Assert.That(F.List(extension, "textures"), Is.Not.Empty);
                var prefix = ExportNoteText.FallbackImagesLightened.Substring(0, ExportNoteText.FallbackImagesLightened.IndexOf("{0}", StringComparison.Ordinal));
                Assert.That(warnings.Count(w => w.StartsWith(prefix, StringComparison.Ordinal)), Is.EqualTo(on ? 1 : 0));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller());
                Assert.That(imported, Is.Not.Null);
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var text = ExportNoteText.FallbackImagesLightened;
            Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S"), Is.False, text);
            Assert.That(FallbackImageLightweight.ResultLine(0, 0), Is.Null);
            Assert.That(string.Format(CultureInfo.InvariantCulture, text, 3, TexelDensity.Plan.Megabytes(1)),
                Is.EqualTo("他のVRMアプリ向けの画像3枚を小さくしてVRMを0.1MB小さくしました"));
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
                Assert.That(File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json")),
                    Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale);
        }

        // Red 200, blue 50, opaque, green from a fixed sequence, so the PNG
        // does not compress away and the averaged red and blue are known.
        static Color32[] Noise(int width, int height)
        {
            var pixels = new Color32[width * height];
            var state = 12345u;
            for (var i = 0; i < pixels.Length; i++)
            {
                state = state * 1664525u + 1013904223u;
                pixels[i] = new Color32(200, (byte)(state >> 24), 50, 255);
            }
            return pixels;
        }

        // Nodes 0, 1 and 3 have bindings; node 2 has none and draws mesh 2
        // (material 2). Mesh 1 is the face: an expression moves it.
        static byte[] Build()
        {
            var png = PngWriter.Encode(Noise(Width, Height), Width, Height);
            var parts = new List<byte[]> { Floats(0, 0, 0, 1, 0, 0, 0, 1, 0), new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 } };
            for (var i = 0; i < 6; i++) parts.Add(png);
            var binary = new List<byte>();
            var views = new List<object>();
            foreach (var part in parts)
            {
                views.Add(new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", (long)binary.Count }, { "byteLength", (long)part.Length } });
                binary.AddRange(part);
                while (binary.Count % 4 != 0) binary.Add(0);
            }
            var images = Enumerable.Range(0, 6).Select(i => (object)new Dictionary<string, object> { { "bufferView", (long)(i + 2) }, { "mimeType", "image/png" } }).ToList();
            var textures = Enumerable.Range(0, 6).Select(i => (object)new Dictionary<string, object> { { "source", (long)i } }).ToList();
            Dictionary<string, object> Info(int texture) => new Dictionary<string, object> { { "index", (long)texture } };
            Dictionary<string, object> Material(int baseColor, params (string Key, int Texture)[] mtoon)
            {
                var extension = mtoon.ToDictionary(m => m.Key, m => (object)Info(m.Texture));
                return new Dictionary<string, object>
                {
                    { "pbrMetallicRoughness", new Dictionary<string, object> { { "baseColorTexture", Info(baseColor) } } },
                    { "extensions", new Dictionary<string, object> { { "VRMC_materials_mtoon", extension } } },
                };
            }
            var m0 = Material(0, ("shadeMultiplyTexture", 0));
            m0["normalTexture"] = Info(5);
            var materials = new List<object> { m0, Material(1), Material(2), Material(3, ("shadeMultiplyTexture", 4)) };
            ((Dictionary<string, object>)materials[3])["emissiveTexture"] = Info(3);
            ((Dictionary<string, object>)materials[3])["occlusionTexture"] = Info(4);
            var accessors = new List<object>
            {
                new Dictionary<string, object>
                {
                    { "bufferView", 0L }, { "componentType", 5126L }, { "type", "VEC3" }, { "count", 3L },
                    { "min", new List<object> { 0d, 0d, 0d } }, { "max", new List<object> { 1d, 1d, 0d } },
                },
            };
            Dictionary<string, object> Mesh(int material) => new Dictionary<string, object>
            {
                { "primitives", new List<object> { new Dictionary<string, object> { { "attributes", new Dictionary<string, object> { { "POSITION", 0L } } }, { "material", (long)material } } } },
            };
            var meshes = new List<object> { Mesh(0), Mesh(1), Mesh(2), Mesh(3) };
            // Node 2 is unbound and also draws material 2.
            var nodes = Enumerable.Range(0, 4).Select(i => (object)new Dictionary<string, object> { { "mesh", (long)i } }).ToList();
            var records = new[] { 0, 1, 2, 3 }.Select(i => (object)new Dictionary<string, object> { { "materialIndex", (long)i } }).ToList();
            var bindings = new[] { 0, 1, 3 }.Select(i => (object)new Dictionary<string, object> { { "node", (long)i }, { "mesh", (long)i } }).ToList();
            var json = new Dictionary<string, object>
            {
                { "asset", new Dictionary<string, object> { { "version", "2.0" } } },
                { "extensionsUsed", new List<object> { "VRMC_vrm", "VRMC_materials_mtoon", F.ExtensionName } },
                { "buffers", new List<object> { new Dictionary<string, object> { { "byteLength", (long)binary.Count } } } },
                { "bufferViews", views }, { "accessors", accessors }, { "meshes", meshes }, { "nodes", nodes },
                { "materials", materials }, { "textures", textures }, { "images", images },
                { "extensions", new Dictionary<string, object>
                {
                    { "VRMC_vrm", new Dictionary<string, object>
                    {
                        { "meta", new Dictionary<string, object> { { "thumbnailImage", 3L } } },
                        { "expressions", new Dictionary<string, object> { { "preset", new Dictionary<string, object> { { "happy", new Dictionary<string, object>
                        {
                            { "morphTargetBinds", new List<object> { new Dictionary<string, object> { { "node", 1L }, { "index", 0L }, { "weight", 1d } } } },
                        } } } } } },
                    } },
                    { F.ExtensionName, new Dictionary<string, object>
                    {
                        { "schemaMajor", 2L }, { "materials", records }, { "bindings", bindings },
                        { "chunks", new List<object> { new Dictionary<string, object> { { "bufferView", 1L }, { "decodedBytes", 8L }, { "codec", "deflate" } } } },
                    } },
                } },
            };
            return GlbDocument.Create(json, binary.ToArray()).Write();
        }

        static byte[] View(GlbDocument glb, int index)
        {
            var view = F.Object(F.List(glb.Json, "bufferViews")[index]);
            var start = view.TryGetValue("byteOffset", out var at) ? Convert.ToInt32(at) : 0;
            return glb.Binary.Skip(start).Take(F.Int(view, "byteLength")).ToArray();
        }

        static byte[] Png(GlbDocument glb, int image) => View(glb, F.Int(F.Object(F.List(glb.Json, "images")[image]), "bufferView"));

        static (int, int) Size(GlbDocument glb, int image)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.That(texture.LoadImage(Png(glb, image), false), Is.True);
                return (texture.width, texture.height);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static Color32[] Pixels(GlbDocument glb, int image)
        {
            var texture = new Texture2D(2, 2);
            try
            {
                Assert.That(texture.LoadImage(Png(glb, image), false), Is.True);
                return texture.GetPixels32();
            }
            finally { Object.DestroyImmediate(texture); }
        }

        static byte[] Floats(params float[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    }
}
