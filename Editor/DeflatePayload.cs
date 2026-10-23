using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace VRVlog.LilToonExporter
{
    internal sealed class DeflatePayload
    {
        // Compressing the snapshot's images took most of its time and needs no
        // Unity object, so each payload compresses on the thread pool while the
        // next image is read back. Only a few run at once, so the uncompressed
        // copies they hold stay few; the bytes are the same as compressing here.
        static readonly SemaphoreSlim running = new SemaphoreSlim(Math.Max(1, Math.Min(Environment.ProcessorCount - 1, 8)));
        readonly Task<byte[]> encoding;

        internal byte[] Encoded => encoding.GetAwaiter().GetResult();
        internal int DecodedBytes { get; }

        // The caller must not change bytes afterwards.
        internal DeflatePayload(byte[] bytes)
        {
            DecodedBytes = bytes.Length;
            running.Wait();
            encoding = Task.Run(() =>
            {
                try { return Compress(bytes); }
                finally { running.Release(); }
            });
        }

        static byte[] Compress(byte[] bytes)
        {
            using var compressed = new MemoryStream();
            using (var encoder = new DeflateStream(compressed, CompressionLevel.Optimal, true))
                encoder.Write(bytes, 0, bytes.Length);
            return compressed.ToArray();
        }
    }
}
