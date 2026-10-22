using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using NUnit.Framework;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public class PngWriterTests
    {
        [TestCase(8, 8, "opaque", TextureFormat.RGBA32, false)]
        [TestCase(8, 8, "alpha", TextureFormat.RGBA32, true)]
        [TestCase(1, 1, "alpha", TextureFormat.RGBA32, false)]
        [TestCase(17, 9, "opaque", TextureFormat.RGBA32, true)]
        [TestCase(64, 48, "gradient", TextureFormat.RGBA32, false)]
        [TestCase(64, 48, "gradientAlpha", TextureFormat.RGBA32, false)]
        [TestCase(17, 9, "opaque", TextureFormat.RGB24, false)]
        [TestCase(17, 9, "alpha", TextureFormat.ARGB32, false)]
        [TestCase(17, 9, "alpha", TextureFormat.BGRA32, true)]
        public void DecodesToTheSamePixelsAsSourceAndEncodeToPng(int width, int height, string pattern, TextureFormat format, bool linear)
        {
            var source = Image(width, height, format, linear, Pattern(pattern, width));
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            var decodedUnity = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear);
            try
            {
                var expected = source.GetPixels32();
                var managed = PngWriter.Encode(source);
                var unity = ImageConversion.EncodeToPNG(source);
                Assert.That(ImageConversion.LoadImage(decoded, managed, false), Is.True);
                Assert.That(ImageConversion.LoadImage(decodedUnity, unity, false), Is.True);
                Assert.That(decoded.width, Is.EqualTo(width));
                Assert.That(decoded.height, Is.EqualTo(height));
                AssertSamePixels(expected, decoded.GetPixels32());
                AssertSamePixels(decodedUnity.GetPixels32(), decoded.GetPixels32());
                var opaque = Array.TrueForAll(expected, p => p.a == 255);
                Assert.That(managed[25], Is.EqualTo(opaque ? 2 : 6), "IHDR color type");
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(decoded); Object.DestroyImmediate(decodedUnity); }
        }

        [TestCase(17, 9, "alpha")]
        [TestCase(300, 200, "gradient")]
        public void WritesValidChunksAndZlibStream(int width, int height, string pattern)
        {
            var source = Image(width, height, TextureFormat.RGBA32, false, Pattern(pattern, width));
            try
            {
                var png = PngWriter.Encode(source);
                var chunks = Chunks(png);
                Assert.That(chunks[0].Type, Is.EqualTo("IHDR"));
                Assert.That(chunks[chunks.Count - 1].Type, Is.EqualTo("IEND"));
                Assert.That(chunks[chunks.Count - 1].Data.Length, Is.EqualTo(0));
                for (var i = 1; i < chunks.Count - 1; i++) Assert.That(chunks[i].Type, Is.EqualTo("IDAT"));
                Assert.That(chunks.Count, Is.GreaterThanOrEqualTo(3));

                var header = chunks[0].Data;
                var opaque = pattern != "alpha";
                Assert.That(header.Length, Is.EqualTo(13));
                Assert.That(ReadUInt32(header, 0), Is.EqualTo((uint)width));
                Assert.That(ReadUInt32(header, 4), Is.EqualTo((uint)height));
                Assert.That(header[8], Is.EqualTo(8), "bit depth");
                Assert.That(header[9], Is.EqualTo(opaque ? 2 : 6), "color type");
                Assert.That(header[10], Is.EqualTo(0), "compression method");
                Assert.That(header[11], Is.EqualTo(0), "filter method");
                Assert.That(header[12], Is.EqualTo(0), "interlace method");

                var zlib = new MemoryStream();
                for (var i = 1; i < chunks.Count - 1; i++) zlib.Write(chunks[i].Data, 0, chunks[i].Data.Length);
                var stream = zlib.ToArray();
                Assert.That(stream[0], Is.EqualTo(0x78));
                Assert.That((stream[0] * 256 + stream[1]) % 31, Is.EqualTo(0), "zlib header check bits");
                Assert.That(stream[1] & 0x20, Is.EqualTo(0), "no preset dictionary");
                var inflated = Inflate(stream, 2, stream.Length - 6);
                var rowBytes = width * (opaque ? 3 : 4);
                Assert.That(inflated.Length, Is.EqualTo(height * (rowBytes + 1)));
                for (var y = 0; y < height; y++) Assert.That(inflated[y * (rowBytes + 1)], Is.LessThanOrEqualTo(4), "filter type of row " + y);
                Assert.That(ReadUInt32(stream, stream.Length - 4), Is.EqualTo(Adler32(inflated)), "Adler-32");
            }
            finally { Object.DestroyImmediate(source); }
        }

        // Every case has at least 64 KiB of filtered bytes, so the parallel path
        // is used. 4096x5 RGBA (81,925 bytes) has one row per band.
        [TestCase(512, 512, "noisyGradient")]
        [TestCase(257, 129, "alpha")]
        [TestCase(4096, 5, "alpha")]
        public void ParallelFilteringWritesTheSameBytesAsSequential(int width, int height, string pattern)
        {
            var pixels = new Color32[width * height];
            var pixel = Pattern(pattern, width);
            for (var i = 0; i < pixels.Length; i++) pixels[i] = pixel(i);
            var parallel = PngWriter.Encode(pixels, width, height, true);
            var sequential = PngWriter.Encode(pixels, width, height, false);
            Assert.That(parallel, Is.EqualTo(sequential));
        }

        [Test]
        public void WritesTheSameChunkTypesAsEncodeToPng()
        {
            var source = Image(17, 9, TextureFormat.RGBA32, false, Pattern("alpha", 17));
            try
            {
                var managed = new SortedSet<string>();
                foreach (var chunk in Chunks(PngWriter.Encode(source))) managed.Add(chunk.Type);
                var unity = new SortedSet<string>();
                foreach (var chunk in Chunks(ImageConversion.EncodeToPNG(source))) unity.Add(chunk.Type);
                Assert.That(managed, Is.EqualTo(unity));
                Assert.That(unity, Is.EquivalentTo(new[] { "IDAT", "IEND", "IHDR" }));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void HalfFloatTextureKeepsEncodeToPngOutput()
        {
            var source = new Texture2D(5, 3, TextureFormat.RGBAHalf, false, true);
            try
            {
                var colors = new Color[15];
                for (var i = 0; i < colors.Length; i++) colors[i] = new Color(i / 15f, 0.25f, 1f - i / 15f, i % 2 == 0 ? 1f : 0.5f);
                source.SetPixels(colors);
                source.Apply(false, false);
                Assert.That(PngWriter.Supports(source), Is.False);
                Assert.That(PngWriter.Encode(source), Is.EqualTo(ImageConversion.EncodeToPNG(source)));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void ConvertedOpaqueTextureIsWrittenAsRgb()
        {
            var source = Image(17, 9, TextureFormat.RGBA32, false, Pattern("opaque", 17));
            try
            {
                var png = MobileTextureEncoder.EncodeConverted(source, true);
                Assert.That(png[25], Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void SmoothOpaqueGradientIsSmallerThanEncodeToPng()
        {
            var source = Image(512, 512, TextureFormat.RGBA32, false, Pattern("gradient", 512));
            try
            {
                var managed = PngWriter.Encode(source);
                var unity = ImageConversion.EncodeToPNG(source);
                Debug.Log("PngWriter 512x512 gradient: " + managed.Length + " bytes, EncodeToPNG: " + unity.Length + " bytes");
                Assert.That(managed.Length, Is.LessThan(unity.Length));
            }
            finally { Object.DestroyImmediate(source); }
        }

        [Test]
        public void LogsTimeAndSizeFor2048Texture()
        {
            var source = Image(2048, 2048, TextureFormat.RGBA32, false, Pattern("noisyGradient", 2048));
            try
            {
                var managedTimer = Stopwatch.StartNew();
                var managed = PngWriter.Encode(source);
                managedTimer.Stop();
                var unityTimer = Stopwatch.StartNew();
                var unity = ImageConversion.EncodeToPNG(source);
                unityTimer.Stop();
                Debug.Log("PngWriter 2048x2048: " + managedTimer.ElapsedMilliseconds + " ms, " + managed.Length + " bytes; EncodeToPNG: "
                    + unityTimer.ElapsedMilliseconds + " ms, " + unity.Length + " bytes");
                Assert.That(managed.Length, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(source); }
        }

        private static Func<int, Color32> Pattern(string name, int width)
        {
            var random = new System.Random(1234);
            switch (name)
            {
                case "opaque": return _ => new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
                case "alpha": return _ => new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
                case "gradient": return i => Gradient(i % width, i / width, 255);
                case "gradientAlpha": return i => Gradient(i % width, i / width, (byte)(i / width * 5));
                case "noisyGradient": return i =>
                {
                    var c = Gradient(i % width, i / width, 255);
                    var n = random.Next(4);
                    return new Color32((byte)Math.Min(255, c.r + n), (byte)Math.Min(255, c.g + n), c.b, 255);
                };
                default: throw new ArgumentException(name);
            }
        }

        private static Color32 Gradient(int x, int y, byte alpha) => new Color32((byte)(x / 2), (byte)(y / 2), (byte)((x + y) / 4), alpha);

        private static Texture2D Image(int width, int height, TextureFormat format, bool linear, Func<int, Color32> pixel)
        {
            var texture = new Texture2D(width, height, format, false, linear);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = pixel(i);
            if (format == TextureFormat.RGBA32) texture.SetPixels32(pixels);
            else
            {
                // Color32 -> Color -> 8-bit is exact; SetPixels covers every format used here.
                var colors = new Color[pixels.Length];
                for (var i = 0; i < pixels.Length; i++) colors[i] = pixels[i];
                texture.SetPixels(colors);
            }
            texture.Apply(false, false);
            return texture;
        }

        private static void AssertSamePixels(Color32[] expected, Color32[] actual)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (var i = 0; i < expected.Length; i++)
            {
                var e = expected[i];
                var a = actual[i];
                if (e.r != a.r || e.g != a.g || e.b != a.b || e.a != a.a)
                    Assert.Fail("pixel " + i + ": expected " + e + " but was " + a);
            }
        }

        private struct Chunk
        {
            internal string Type;
            internal byte[] Data;
        }

        // Parses the PNG and checks the signature and every chunk's CRC-32.
        private static List<Chunk> Chunks(byte[] png)
        {
            var signature = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            for (var i = 0; i < signature.Length; i++) Assert.That(png[i], Is.EqualTo(signature[i]), "PNG signature");
            var chunks = new List<Chunk>();
            var position = 8;
            while (position < png.Length)
            {
                var length = (int)ReadUInt32(png, position);
                var type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
                var data = new byte[length];
                Buffer.BlockCopy(png, position + 8, data, 0, length);
                var crc = Crc32(png, position + 4, length + 4);
                Assert.That(ReadUInt32(png, position + 8 + length), Is.EqualTo(crc), "CRC of " + type);
                chunks.Add(new Chunk { Type = type, Data = data });
                position += 12 + length;
            }
            Assert.That(position, Is.EqualTo(png.Length));
            return chunks;
        }

        private static uint ReadUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

        // Bitwise CRC-32 (no table) so the test does not share the writer's table.
        private static uint Crc32(byte[] data, int offset, int count)
        {
            var crc = 0xFFFFFFFFu;
            for (var i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var value in data)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        private static byte[] Inflate(byte[] data, int offset, int count)
        {
            using var input = new MemoryStream(data, offset, count);
            using var inflater = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            inflater.CopyTo(output);
            return output.ToArray();
        }
    }
}
