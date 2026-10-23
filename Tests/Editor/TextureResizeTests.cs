using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public class TextureResizeTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void ConvertedFourKTexturePreservesColorAlphaAndSource(bool srgb)
        {
            var source = Image(4096, 8, !srgb, _ => new Color32(102, 51, 204, 128));
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, !srgb);
            try
            {
                var original = source.GetPixels32();
                var warnings = new List<string>();
                var bytes = new MobileTextureSerializer(warnings).ExportBytesWithMime(source, srgb ? VRVlog.LilToonExporter.Vendor.UniGLTF.ColorSpace.sRGB : VRVlog.LilToonExporter.Vendor.UniGLTF.ColorSpace.Linear);
                Assert.That(bytes.mime, Is.EqualTo("image/png"));
                Assert.That(ImageConversion.LoadImage(decoded, bytes.bytes, false), Is.True);
                Assert.That(decoded.width, Is.EqualTo(1024));
                Assert.That(decoded.height, Is.EqualTo(2));
                Assert.That(decoded.GetPixels32().All(p => p.r == 102 && p.g == 51 && p.b == 204 && p.a == 128), Is.True);
                Assert.That(source.width, Is.EqualTo(4096));
                Assert.That(source.GetPixels32(), Is.EqualTo(original));
                Assert.That(warnings.Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(decoded); }
        }

        [Test]
        public void FourKNormalMapIsConvertedBeforeResizeWithoutReimportingSource()
        {
            var path = "Assets/__VRVlogResizeNormal_" + Guid.NewGuid().ToString("N") + ".png";
            var originalImage = Image(4096, 8, true, _ => new Color32(128, 128, 255, 255));
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(originalImage));
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.NormalMap;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.maxTextureSize = 4096;
                importer.isReadable = false;
                importer.SaveAndReimport();
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                var settingsBefore = EditorJsonUtility.ToJson(importer);
                var fileBefore = File.ReadAllBytes(path);
                var serializer = new MobileTextureSerializer(null);
                using (var exporter = new TextureExporter(serializer))
                {
                    exporter.RegisterExportingAsNormal(source);
                    var converted = exporter.Export().Single();
                    Assert.That(converted.Item2, Is.EqualTo(VRVlog.LilToonExporter.Vendor.UniGLTF.ColorSpace.Linear));
                    var bytes = serializer.ExportBytesWithMime(converted.Item1, converted.Item2);
                    Assert.That(ImageConversion.LoadImage(decoded, bytes.bytes, false), Is.True);
                }
                Assert.That(decoded.width, Is.EqualTo(1024));
                Assert.That(decoded.height, Is.EqualTo(2));
                var pixel = decoded.GetPixels32()[0];
                Assert.That((int)pixel.r, Is.EqualTo(128).Within(4));
                Assert.That((int)pixel.g, Is.EqualTo(128).Within(4));
                Assert.That((int)pixel.b, Is.GreaterThanOrEqualTo(251));
                Assert.That(source.width, Is.EqualTo(4096));
                Assert.That(EditorJsonUtility.ToJson(importer), Is.EqualTo(settingsBefore));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(fileBefore));
            }
            finally
            {
                AssetDatabase.DeleteAsset(path);
                Object.DestroyImmediate(originalImage);
                Object.DestroyImmediate(decoded);
            }
        }

        [Test]
        public void FourKOutlineWidthMaskIsResizedOnItsExportCopy()
        {
            var source = Image(4096, 8, true, _ => new Color32(64, 200, 100, 255));
            var owned = new List<Texture2D>();
            try
            {
                var copy = OutlineMaskTexture.Create(source, owned);
                Assert.That(copy.width, Is.EqualTo(1024));
                Assert.That(copy.height, Is.EqualTo(2));
                Assert.That(copy.GetPixel(0,0).g, Is.EqualTo(64 / 255f).Within(.01));
                Assert.That(source.width, Is.EqualTo(4096));
                Assert.That(source.GetPixels32()[0].g, Is.EqualTo(200));
                Assert.That(copy, Is.Not.SameAs(source));
            }
            finally { foreach (var texture in owned) Object.DestroyImmediate(texture); Object.DestroyImmediate(source); }
        }

        private static Texture2D Image(int width, int height, bool linear, Func<int,Color32> pixel)
        {
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
            image.SetPixels32(Enumerable.Range(0, width * height).Select(pixel).ToArray());
            image.Apply(false, false);
            return image;
        }
    }
}
