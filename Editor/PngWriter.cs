using System;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // EncodeToPNG writes RGBA even when every pixel is opaque and filters and
    // compresses weakly. This writes the same pixels as 8-bit RGB (all opaque)
    // or RGBA with per-row adaptive filtering. Like EncodeToPNG it writes only
    // IHDR, IDAT and IEND, so readers decode the same pixels as before.
    internal static class PngWriter
    {
        private const int IdatChunkBytes = 1 << 20;
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly byte[] Ihdr = { (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        private static readonly byte[] Idat = { (byte)'I', (byte)'D', (byte)'A', (byte)'T' };
        private static readonly byte[] Iend = { (byte)'I', (byte)'E', (byte)'N', (byte)'D' };
        // zlib header: deflate, 32K window, default compression level, no dictionary.
        private static readonly byte[] ZlibHeader = { 0x78, 0x9C };
        private static readonly uint[] CrcTable = BuildCrcTable();

        // Textures in other formats (16-bit, half/float, single channel,
        // compressed) and unreadable ones keep going through EncodeToPNG.
        internal static byte[] Encode(Texture2D texture)
        {
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            return Supports(texture) ? Encode(texture.GetPixels32(), texture.width, texture.height) : ImageConversion.EncodeToPNG(texture);
        }

        internal static bool Supports(Texture2D texture) =>
            texture.isReadable && (texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.ARGB32
                || texture.format == TextureFormat.RGB24 || texture.format == TextureFormat.BGRA32);

        // Pixels in Unity order (row 0 is the bottom row). PNG rows run top-down.
        internal static byte[] Encode(Color32[] pixels, int width, int height)
        {
            if (pixels == null) throw new ArgumentNullException(nameof(pixels));
            if (width <= 0 || height <= 0 || pixels.Length != checked(width * height)) throw new ArgumentException("画像の大きさと画素の数が合いません。");
            var opaque = true;
            for (var i = 0; i < pixels.Length; i++)
                if (pixels[i].a != 255) { opaque = false; break; }
            var bpp = opaque ? 3 : 4;
            var rowBytes = checked(width * bpp);

            using var output = new MemoryStream();
            output.Write(Signature, 0, Signature.Length);
            var header = new byte[13];
            WriteUInt32(header, 0, (uint)width);
            WriteUInt32(header, 4, (uint)height);
            header[8] = 8;
            header[9] = (byte)(opaque ? 2 : 6);
            WriteChunk(output, Ihdr, header, 0, header.Length);

            var idat = new IdatWriter(output);
            idat.Write(ZlibHeader, 0, ZlibHeader.Length);
            var adler = new Adler32();
            using (var deflate = new DeflateStream(idat, System.IO.Compression.CompressionLevel.Optimal, true))
            {
                var previous = new byte[rowBytes];
                var current = new byte[rowBytes];
                var filtered = new byte[5][];
                for (var f = 0; f < filtered.Length; f++) { filtered[f] = new byte[rowBytes + 1]; filtered[f][0] = (byte)f; }
                for (var y = 0; y < height; y++)
                {
                    FillRow(pixels, (height - 1 - y) * width, width, bpp, current);
                    var best = filtered[Filter(current, previous, bpp, filtered)];
                    deflate.Write(best, 0, best.Length);
                    adler.Update(best, 0, best.Length);
                    var swap = previous; previous = current; current = swap;
                }
            }
            var checksum = new byte[4];
            WriteUInt32(checksum, 0, adler.Value);
            idat.Write(checksum, 0, checksum.Length);
            idat.Finish();

            WriteChunk(output, Iend, Array.Empty<byte>(), 0, 0);
            return output.ToArray();
        }

        private static void FillRow(Color32[] pixels, int start, int width, int bpp, byte[] row)
        {
            var o = 0;
            if (bpp == 3)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = pixels[start + x];
                    row[o] = p.r; row[o + 1] = p.g; row[o + 2] = p.b;
                    o += 3;
                }
            }
            else
            {
                for (var x = 0; x < width; x++)
                {
                    var p = pixels[start + x];
                    row[o] = p.r; row[o + 1] = p.g; row[o + 2] = p.b; row[o + 3] = p.a;
                    o += 4;
                }
            }
        }

        // Writes all five filters (None, Sub, Up, Average, Paeth) and returns the
        // one with the smallest sum of the filtered bytes read as signed values
        // (the libpng heuristic). Ties keep the lower filter number.
        private static int Filter(byte[] row, byte[] prior, int bpp, byte[][] filtered)
        {
            byte[] none = filtered[0], sub = filtered[1], up = filtered[2], average = filtered[3], paeth = filtered[4];
            long sumNone = 0, sumSub = 0, sumUp = 0, sumAverage = 0, sumPaeth = 0;
            for (var i = 0; i < row.Length; i++)
            {
                int x = row[i], b = prior[i];
                int a = i >= bpp ? row[i - bpp] : 0;
                int c = i >= bpp ? prior[i - bpp] : 0;
                var o = i + 1;
                none[o] = (byte)x;
                sub[o] = (byte)(x - a);
                up[o] = (byte)(x - b);
                average[o] = (byte)(x - ((a + b) >> 1));
                paeth[o] = (byte)(x - Paeth(a, b, c));
                sumNone += Magnitude(none[o]);
                sumSub += Magnitude(sub[o]);
                sumUp += Magnitude(up[o]);
                sumAverage += Magnitude(average[o]);
                sumPaeth += Magnitude(paeth[o]);
            }
            var best = 0;
            var bestSum = sumNone;
            if (sumSub < bestSum) { best = 1; bestSum = sumSub; }
            if (sumUp < bestSum) { best = 2; bestSum = sumUp; }
            if (sumAverage < bestSum) { best = 3; bestSum = sumAverage; }
            if (sumPaeth < bestSum) best = 4;
            return best;
        }

        private static int Magnitude(byte value) => value < 128 ? value : 256 - value;

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc) return a;
            return pb <= pc ? b : c;
        }

        private static void WriteChunk(Stream output, byte[] type, byte[] data, int offset, int count)
        {
            var length = new byte[4];
            WriteUInt32(length, 0, (uint)count);
            output.Write(length, 0, 4);
            output.Write(type, 0, 4);
            if (count > 0) output.Write(data, offset, count);
            var crc = UpdateCrc(UpdateCrc(0xFFFFFFFFu, type, 0, 4), data, offset, count) ^ 0xFFFFFFFFu;
            var trailer = new byte[4];
            WriteUInt32(trailer, 0, crc);
            output.Write(trailer, 0, 4);
        }

        private static uint UpdateCrc(uint crc, byte[] data, int offset, int count)
        {
            for (var i = offset; i < offset + count; i++) crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private sealed class Adler32
        {
            // Largest n with 255n(n+1)/2 + (n+1)(65520) below 2^32 (zlib's NMAX).
            private const int Block = 5552;
            private const uint Modulus = 65521;
            private uint a = 1, b;

            internal uint Value => (b << 16) | a;

            internal void Update(byte[] data, int offset, int count)
            {
                while (count > 0)
                {
                    var n = Math.Min(count, Block);
                    count -= n;
                    for (var end = offset + n; offset < end; offset++) { a += data[offset]; b += a; }
                    a %= Modulus;
                    b %= Modulus;
                }
            }
        }

        // Collects the zlib stream and writes it out as IDAT chunks of up to
        // IdatChunkBytes. Flush does not cut a chunk; Finish writes the rest.
        private sealed class IdatWriter : Stream
        {
            private readonly Stream output;
            private readonly byte[] buffer = new byte[IdatChunkBytes];
            private int count;

            internal IdatWriter(Stream output) { this.output = output; }

            public override void Write(byte[] data, int offset, int length)
            {
                while (length > 0)
                {
                    var n = Math.Min(length, buffer.Length - count);
                    Buffer.BlockCopy(data, offset, buffer, count, n);
                    count += n; offset += n; length -= n;
                    if (count == buffer.Length) WriteOut();
                }
            }

            internal void Finish()
            {
                if (count > 0) WriteOut();
            }

            private void WriteOut()
            {
                WriteChunk(output, Idat, buffer, 0, count);
                count = 0;
            }

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] data, int offset, int length) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
