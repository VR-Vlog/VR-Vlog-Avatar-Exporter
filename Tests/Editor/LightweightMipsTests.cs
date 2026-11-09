using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: a colour texture of an opaque material is written as mip 0
    // alone and the app builds its mips when it loads
    // (Documentation~/decisions/2026-11-09-lightweight-mips.md).
    public sealed class LightweightMipsTests
    {
        static readonly Color32 MainColor = new Color32(73, 119, 181, 255);

        // On: the opaque main texture loses its stored mips; the normal map, the
        // reflection cube and the cutout material's texture keep theirs. Off:
        // every texture keeps its mips. The preview builds the left-out chain
        // the way the app does, so it samples every level either way.
        [TestCase(false)]
        [TestCase(true)]
        public async Task OnlyTheOptionLeavesOutTheMipsOfOpaqueColourTextures(bool on)
        {
            using var f = new ExportVrmLilToonPreviewTests.Fixture();
            var mainPath = ImportedImage(64);
            var main = AssetDatabase.LoadAssetAtPath<Texture2D>(mainPath);
            var normal = f.Own(Image("Lightweight mips normal", 16, new Color32(128, 128, 255, 255), true));
            var cutoutMain = f.Own(Image("Lightweight mips cutout main", 32, new Color32(200, 90, 40, 128), false));
            var cube = f.Own(new Cubemap(4, TextureFormat.RGBA32, true) { name = "Lightweight mips cube" });
            for (var face = 0; face < 6; face++) for (var mip = 0; mip < 3; mip++)
                cube.SetPixels(Enumerable.Repeat(Color.gray, Math.Max(1, 4 >> mip) * Math.Max(1, 4 >> mip)).ToArray(), (CubemapFace)face, mip);
            cube.Apply(false, false);
            f.First.SetTexture("_MainTex", main);
            f.First.SetFloat("_UseBumpMap", 1); f.First.SetTexture("_BumpMap", normal);
            f.First.SetFloat("_UseReflection", 1); f.First.SetTexture("_ReflectionCubeTex", cube);
            var cutout = Shader.Find("Hidden/lilToonCutout");
            Assert.That(cutout, Is.Not.Null);
            f.Second.shader = cutout; f.Second.SetTexture("_MainTex", cutoutMain);

            try
            {
                var bytes = f.Export(on ? new TexelDensity.Plan(null) : null);
                var textures = F.List(F.Root(GlbDocument.Read(bytes).Json), "textures").Select(F.Object).ToList();
                int Mips(Texture source) => F.Int(textures.Single(t => F.Text(t, "name") == source.name), "mips");
                Assert.That(Mips(main), Is.EqualTo(on ? 1 : 7));
                Assert.That(Mips(normal), Is.EqualTo(5), "Normal maps keep their mips");
                Assert.That(Mips(cube), Is.EqualTo(3), "Cube maps keep their mips");
                Assert.That(Mips(cutoutMain), Is.EqualTo(6), "Textures of non-opaque materials keep their mips");

                await f.Import(bytes);
                using (ExportVrmLilToonPreview.Apply(bytes, f.Imported.gameObject))
                {
                    var image = (Texture2D)f.ImportedFront.sharedMaterial.GetTexture("_MainTex");
                    Assert.That(image.mipmapCount, Is.EqualTo(7));
                    Assert.That(image.isReadable, Is.False);
                    foreach (var mip in new[] { 0, 3, 6 })
                        AssertRgba(LilToonFullTexture.Read(image, mip, 0, false, true, false), MainColor, mip);
                    Assert.That(f.ImportedFront.sharedMaterial.GetTexture("_BumpMap").mipmapCount, Is.EqualTo(5));
                }
            }
            finally { AssetDatabase.DeleteAsset(mainPath); }
        }

        // A non-opaque lilToon material keeps the mips of every texture it reads,
        // also one an opaque material shares. Other shaders add nothing.
        [Test]
        public void TexturesOfNonOpaqueMaterialsKeepTheirMips()
        {
            var opaque = new Material(Shader.Find("lilToon"));
            var cutout = new Material(Shader.Find("Hidden/lilToonCutout"));
            var standard = new Material(Shader.Find("Standard"));
            var shared = new Texture2D(4, 4); var opaqueOnly = new Texture2D(4, 4); var mask = new Texture2D(4, 4); var other = new Texture2D(4, 4);
            try
            {
                opaque.SetTexture("_MainTex", shared); opaque.SetTexture("_ShadowColorTex", opaqueOnly);
                cutout.SetTexture("_MainTex", shared); cutout.SetTexture("_AlphaMask", mask);
                standard.SetTexture("_MainTex", other);
                Assert.That(LilToonFullSnapshot.KeepMips(new[] { opaque, cutout, standard, null }), Is.EquivalentTo(new[] { shared, mask }));
            }
            finally
            {
                foreach (var item in new Object[] { opaque, cutout, standard, shared, opaqueOnly, mask, other }) Object.DestroyImmediate(item);
            }
        }

        // A point-filtered image keeps its chain: the app builds none for it.
        [Test]
        public void APointFilteredTextureKeepsItsMips()
        {
            var path = ImportedImage(8);
            try
            {
                var image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.That(LilToonFullSnapshot.KeepsStoredMips(image), Is.False);
                image.filterMode = FilterMode.Point;
                Assert.That(LilToonFullSnapshot.KeepsStoredMips(image), Is.True);
            }
            finally { AssetDatabase.DeleteAsset(path); }
        }

        // A chain the importer filters in its own way stays as exported; the
        // app's box filter would make other levels.
        [TestCase("default", false)]
        [TestCase("fadeout", true)]
        [TestCase("coverage", true)]
        [TestCase("border", true)]
        [TestCase("kaiser", true)]
        public void AnImporterMipFilterOtherThanTheBoxFilterKeepsTheMips(string setting, bool kept)
        {
            var path = "Assets/__VRVlogLightweightMips_" + Guid.NewGuid().ToString("N") + ".png";
            var original = Image("Lightweight mips importer", 8, MainColor, false);
            try
            {
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(original));
                AssetDatabase.ImportAsset(path);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.mipmapEnabled = true;
                if (setting == "fadeout") importer.fadeout = true;
                if (setting == "coverage") importer.mipMapsPreserveCoverage = true;
                if (setting == "border") importer.borderMipmap = true;
                if (setting == "kaiser") importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
                importer.SaveAndReimport();
                Assert.That(LilToonFullSnapshot.OwnMipFilter(AssetDatabase.LoadAssetAtPath<Texture2D>(path)), Is.EqualTo(kept));
            }
            finally { AssetDatabase.DeleteAsset(path); Object.DestroyImmediate(original); }
        }

        // Levels written by hand stay: the app's box filter cannot rebuild them.
        [Test]
        public void ATextureWithoutAnImporterKeepsItsMips()
        {
            var inMemory = Image("Lightweight mips in memory", 8, MainColor, false);
            var asset = Image("Lightweight mips asset", 8, MainColor, false);
            var path = "Assets/__VRVlogLightweightMips_" + Guid.NewGuid().ToString("N") + ".asset";
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                Assert.That(LilToonFullSnapshot.OwnMipFilter(inMemory), Is.True);
                Assert.That(LilToonFullSnapshot.OwnMipFilter(AssetDatabase.LoadAssetAtPath<Texture2D>(path)), Is.True);
            }
            finally { AssetDatabase.DeleteAsset(path); Object.DestroyImmediate(inMemory); }
        }

        // An uncompressed asset with the importer's default box mips.
        static string ImportedImage(int size)
        {
            var path = "Assets/__VRVlogLightweightMips_" + Guid.NewGuid().ToString("N") + ".png";
            var image = Image("Lightweight mips source", size, MainColor, false);
            try { File.WriteAllBytes(path, ImageConversion.EncodeToPNG(image)); }
            finally { Object.DestroyImmediate(image); }
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return path;
        }

        static Texture2D Image(string name, int size, Color32 color, bool linear)
        {
            var image = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { name = name };
            image.SetPixels32(Enumerable.Repeat(color, size * size).ToArray());
            image.Apply(true, false);
            return image;
        }

        static void AssertRgba(byte[] pixels, Color32 expected, int mip)
        {
            Assert.That(pixels.Length, Is.GreaterThan(0));
            var components = new[] { expected.r, expected.g, expected.b, expected.a };
            for (var i = 0; i < pixels.Length; i++)
                Assert.That((int)pixels[i], Is.EqualTo((int)components[i % 4]).Within(1), "mip " + mip + " component " + i);
        }
    }
}
