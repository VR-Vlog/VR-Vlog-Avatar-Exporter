#if EXPORTER_BEHAVIOR_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter;

public static class ExporterTextureResizeBehaviorTests
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception("Texture resize: " + message); }
    private static void Equal<T>(T actual, T expected, string message) => Check(EqualityComparer<T>.Default.Equals(actual, expected), message + ": " + actual + " != " + expected);
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { count++; return; } throw new Exception("Expected " + typeof(T).Name); }
    public static void Run()
    {
        count = 0;
        foreach (var dimensions in new[] { (4096,4096,1024,1024), (4096,2048,1024,512), (1024,4096,256,1024), (1,4096,1,1024), (512,256,512,256), (2048,3,1024,2) })
        {
            var size = TextureResizePolicy.Size(dimensions.Item1, dimensions.Item2);
            Equal(size.Item1, dimensions.Item3, "Width"); Equal(size.Item2, dimensions.Item4, "Height");
        }
        Throws<ArgumentOutOfRangeException>(() => TextureResizePolicy.Size(0, 1024));
        Throws<ArgumentOutOfRangeException>(() => TextureResizePolicy.Size(1, 1, 0));
        Throws<ArgumentException>(() => TextureResizePolicy.Resize(new byte[3], 1, 1, true));
        var opaque = new byte[] { 0,0,0,255, 255,255,255,255 };
        var before = (byte[])opaque.Clone();
        var color = TextureResizePolicy.Resize(opaque, 2, 1, true, 1);
        var numeric = TextureResizePolicy.Resize(opaque, 2, 1, false, 1);
        Equal(color[0], (byte)188, "Color average is linear light then encoded once");
        Equal(numeric[0], (byte)128, "Numeric channels are not gamma corrected");
        Equal(color[3], (byte)255, "Opaque alpha retained");
        Check(before.SequenceEqual(opaque), "Source pixels immutable");
        var transparent = new byte[] { 255,0,0,255, 0,255,0,0 };
        var alphaColor = TextureResizePolicy.Resize(transparent, 2, 1, true, 1);
        Equal(alphaColor[0], (byte)255, "Transparent RGB does not darken visible color");
        Equal(alphaColor[1], (byte)0, "Transparent green does not create a fringe");
        Equal(alphaColor[3], (byte)128, "Coverage averaged independently");
        var alphaData = TextureResizePolicy.Resize(transparent, 2, 1, false, 1);
        Equal(alphaData[0], (byte)128, "Numeric red is independent of alpha");
        Equal(alphaData[1], (byte)128, "Numeric green is independent of alpha");
        var fractional = TextureResizePolicy.Resize(new byte[] { 0,0,0,255, 90,0,0,255, 180,0,0,255 }, 3, 1, false, 2);
        Equal(fractional[0], (byte)30, "Area filter accounts for fractional first pixel coverage");
        Equal(fractional[4], (byte)150, "Area filter accounts for fractional last pixel coverage");
        var retained = TextureResizePolicy.Resize(transparent, 2, 1, true);
        Check(retained.SequenceEqual(transparent) && !ReferenceEquals(retained, transparent), "Small images keep all channels and get an independent result");
        var fourK = Enumerable.Range(0, 4096 * 8).SelectMany(_ => new byte[] { 102, 51, 204, 128 }).ToArray();
        var fourKOutput = TextureResizePolicy.Resize(fourK, 4096, 8, true);
        Equal(fourKOutput.Length, 1024 * 2 * 4, "Actual 4K RGBA array resized to expected aspect ratio");
        Check(Enumerable.Range(0, fourKOutput.Length).All(i => fourKOutput[i] == new byte[] {102,51,204,128}[i%4]), "Constant color and alpha survive downsize exactly");
        var sourceTexture = new UnityEngine.Texture2D { width = 4096, height = 4096, name = "source" };
        var serializer = new MobileTextureSerializer(null);
        Check(!serializer.CanExportAsEditorAssetFile(sourceTexture, VRVlog.LilToonExporter.Vendor.UniGLTF.ColorSpace.sRGB), "Serializer always requests UniVRM semantic conversion");
        serializer.ModifyTextureAssetBeforeExporting(sourceTexture);
        Equal(sourceTexture.width, 4096, "Serializer preparation does not modify source");
        Console.WriteLine("Texture resize checks passed (" + count + " assertions): actual CPU area filtering; Unity codecs/GPU not simulated.");
    }
}
#endif
