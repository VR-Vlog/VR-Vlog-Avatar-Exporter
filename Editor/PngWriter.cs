using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
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
        // Below this many filtered bytes the rows are filtered on the calling thread.
        private const int ParallelMinimumBytes = 1 << 16;
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
        internal static byte[] Encode(Color32[] pixels, int width, int height) => Encode(pixels, width, height, true);

        // parallel: false filters every row on the calling thread; the output
        // bytes are the same either way (tests compare the two).
        internal static byte[] Encode(Color32[] pixels, int width, int height, bool parallel)
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

            // A row's filter depends only on its own raw bytes and the raw row
            // above, so bands of rows are filtered in parallel into one buffer.
            // The result does not depend on how the rows are split.
            var filtered = new byte[checked((rowBytes + 1) * height)];
            if (parallel && filtered.Length >= ParallelMinimumBytes)
            {
                var bands = Math.Min(height, Environment.ProcessorCount * 4);
                var rowsPerBand = (height + bands - 1) / bands;
                bands = (height + rowsPerBand - 1) / rowsPerBand;
                Parallel.For(0, bands, band => FilterRows(pixels, width, height, bpp, filtered, band * rowsPerBand, Math.Min(height, (band + 1) * rowsPerBand)));
            }
            else FilterRows(pixels, width, height, bpp, filtered, 0, height);

            var adler = parallel ? Task.Run(() => Adler32(filtered)) : null;
            var idat = new IdatWriter(output);
            idat.Write(ZlibHeader, 0, ZlibHeader.Length);
            using (var deflate = new DeflateStream(idat, System.IO.Compression.CompressionLevel.Optimal, true))
                deflate.Write(filtered, 0, filtered.Length);
            var checksum = new byte[4];
            WriteUInt32(checksum, 0, adler != null ? adler.Result : Adler32(filtered));
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

        // Filters PNG rows [from, to) into output (each row: filter type byte,
        // then the filtered bytes). Reads the raw row above `from` itself.
        private static void FilterRows(Color32[] pixels, int width, int height, int bpp, byte[] output, int from, int to)
        {
            var rowBytes = width * bpp;
            var previous = new byte[rowBytes];
            var current = new byte[rowBytes];
            // PNG row y is Unity row height - 1 - y.
            if (from > 0) FillRow(pixels, (height - from) * width, width, bpp, previous);
            for (var y = from; y < to; y++)
            {
                FillRow(pixels, (height - 1 - y) * width, width, bpp, current);
                var offset = y * (rowBytes + 1);
                var type = ChooseFilter(current, previous, bpp);
                output[offset] = (byte)type;
                ApplyFilter(type, current, previous, bpp, output, offset + 1);
                var swap = previous; previous = current; current = swap;
            }
        }

        // Returns the filter (None, Sub, Up, Average, Paeth) with the smallest sum
        // of the filtered bytes read as signed values (the libpng heuristic).
        // Ties keep the lower filter number.
        private static int ChooseFilter(byte[] row, byte[] prior, int bpp)
        {
            long sumNone = 0, sumSub = 0, sumUp = 0, sumAverage = 0, sumPaeth = 0;
            var length = row.Length;
            // First pixel: a and c are 0, so Sub is None and Paeth predicts b.
            for (var i = 0; i < bpp; i++)
            {
                int x = row[i], b = prior[i];
                var none = Magnitude(x);
                sumNone += none;
                sumSub += none;
                sumUp += Magnitude(x - b);
                sumAverage += Magnitude(x - (b >> 1));
                sumPaeth += Magnitude(x - b);
            }
            for (var i = bpp; i < length; i++)
            {
                int x = row[i], a = row[i - bpp], b = prior[i], c = prior[i - bpp];
                sumNone += Magnitude(x);
                sumSub += Magnitude(x - a);
                sumUp += Magnitude(x - b);
                sumAverage += Magnitude(x - ((a + b) >> 1));
                sumPaeth += Magnitude(x - Paeth(a, b, c));
            }
            var best = 0;
            var bestSum = sumNone;
            if (sumSub < bestSum) { best = 1; bestSum = sumSub; }
            if (sumUp < bestSum) { best = 2; bestSum = sumUp; }
            if (sumAverage < bestSum) { best = 3; bestSum = sumAverage; }
            if (sumPaeth < bestSum) best = 4;
            return best;
        }

        private static void ApplyFilter(int type, byte[] row, byte[] prior, int bpp, byte[] output, int offset)
        {
            var length = row.Length;
            switch (type)
            {
                case 0:
                    Buffer.BlockCopy(row, 0, output, offset, length);
                    break;
                case 1:
                    for (var i = 0; i < bpp; i++) output[offset + i] = row[i];
                    for (var i = bpp; i < length; i++) output[offset + i] = (byte)(row[i] - row[i - bpp]);
                    break;
                case 2:
                    for (var i = 0; i < length; i++) output[offset + i] = (byte)(row[i] - prior[i]);
                    break;
                case 3:
                    for (var i = 0; i < bpp; i++) output[offset + i] = (byte)(row[i] - (prior[i] >> 1));
                    for (var i = bpp; i < length; i++) output[offset + i] = (byte)(row[i] - ((row[i - bpp] + prior[i]) >> 1));
                    break;
                default:
                    for (var i = 0; i < bpp; i++) output[offset + i] = (byte)(row[i] - prior[i]);
                    for (var i = bpp; i < length; i++) output[offset + i] = (byte)(row[i] - Paeth(row[i - bpp], prior[i], prior[i - bpp]));
                    break;
            }
        }

        // |filtered byte| with the byte read as signed (0..128), without branches.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Magnitude(int difference)
        {
            int value = (sbyte)difference;
            var sign = value >> 31;
            return (value ^ sign) - sign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Abs(int value)
        {
            var sign = value >> 31;
            return (value ^ sign) - sign;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Paeth(int a, int b, int c)
        {
            // p = a + b - c; |p - a| = |b - c|, |p - b| = |a - c|, |p - c| = |a + b - 2c|.
            var pa = Abs(b - c);
            var pb = Abs(a - c);
            var pc = Abs(a + b - c - c);
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

        private static uint Adler32(byte[] data)
        {
            // 5552 is the largest n with 255n(n+1)/2 + (n+1)(65520) below 2^32 (zlib's NMAX),
            // so the sums are reduced once per block instead of once per byte.
            const int block = 5552;
            const uint modulus = 65521;
            uint a = 1, b = 0;
            var offset = 0;
            var count = data.Length;
            while (count > 0)
            {
                var n = Math.Min(count, block);
                count -= n;
                for (var end = offset + n; offset < end; offset++) { a += data[offset]; b += a; }
                a %= modulus;
                b %= modulus;
            }
            return (b << 16) | a;
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
