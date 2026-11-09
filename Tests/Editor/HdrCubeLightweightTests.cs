using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 自動で軽量化する: an HDR cubemap (lilToon's reflection cube) whose values
    // all fit in half is stored as half, with the same size and mips.
    public sealed class HdrCubeLightweightTests
    {
        [TestCase(65504f, true)]
        [TestCase(-65504f, true)]
        [TestCase(65505f, false)]
        [TestCase(float.PositiveInfinity, false)]
        [TestCase(float.NaN, false)]
        public void FitsHalfAcceptsOnlyFiniteValuesHalfHolds(float value, bool fits)
        {
            var chunk = new[] { 1f, 0f, value, 1f }.SelectMany(BitConverter.GetBytes).ToArray();
            Assert.That(LilToonFullSnapshot.FitsHalf(new[] { new byte[16], chunk }), Is.EqualTo(fits));
        }

        // A float cube becomes half only when the option is on and every face
        // and mip fits; the chunks then hold what reading it as half gives.
        // A float 2D texture stays float.
        [TestCase(true, 4f, true, "rgbaHalf")]
        [TestCase(false, 4f, true, "rgbaFloat")]
        [TestCase(true, 70000f, true, "rgbaFloat")]
        [TestCase(true, 4f, false, "rgbaFloat")]
        public void AFloatCubeIsStoredAsHalfOnlyWhenOnAndItFits(bool on, float largest, bool cube, string expected)
        {
            const int size = 8;
            Texture source = cube ? new Cubemap(size, TextureFormat.RGBAFloat, true) : (Texture)new Texture2D(size, size, TextureFormat.RGBAFloat, true, true);
            source.filterMode = FilterMode.Point;
            source.name = "Reflection cube";
            try
            {
                var faces = cube ? 6 : 1;
                for (var face = 0; face < faces; face++)
                    for (var mip = 0; mip < source.mipmapCount; mip++)
                    {
                        var side = Math.Max(1, size >> mip);
                        var pixels = Enumerable.Range(0, side * side).Select(i => new Color(.25f + face, mip + .5f, i == 0 && face == 5 && mip == 0 ? largest : 2, 1)).ToArray();
                        if (source is Cubemap c) c.SetPixels(pixels, (CubemapFace)face, mip);
                        else ((Texture2D)source).SetPixels(pixels, mip);
                    }
                if (source is Cubemap applied) applied.Apply(false);
                else ((Texture2D)source).Apply(false);
                var snapshot = new LilToonFullSnapshot();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                if (on) typeof(LilToonFullSnapshot).GetField("lightweight", flags).SetValue(snapshot, new TexelDensity.Plan(new Dictionary<Texture, double>()));
                var texture = typeof(LilToonFullSnapshot).GetMethod("Texture", flags, null, new[] { typeof(Texture), typeof(bool), typeof(float) }, null);
                // Without a GPU (-nographics) the copy shader cannot read the
                // texture back, so the snapshot stops with the same error as
                // without the option.
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                {
                    var error = Assert.Throws<TargetInvocationException>(() => texture.Invoke(snapshot, new object[] { source, false, 1f }));
                    Assert.That(error.InnerException, Is.InstanceOf<InvalidOperationException>());
                    return;
                }
                var index = (int)texture.Invoke(snapshot, new object[] { source, false, 1f });
                var record = (Dictionary<string, object>)((List<object>)typeof(LilToonFullSnapshot).GetField("textures", flags).GetValue(snapshot))[index];
                Assert.That(record["format"], Is.EqualTo(expected));
                Assert.That(record["width"], Is.EqualTo(size));
                Assert.That(record["mips"], Is.EqualTo(source.mipmapCount));
                Assert.That(record["faces"], Is.EqualTo(faces));
                if (expected != "rgbaHalf") return;
                var payloads = (List<DeflatePayload>)typeof(LilToonFullSnapshot).GetField("payloads", flags).GetValue(snapshot);
                var chunks = (List<object>)record["chunks"];
                Assert.That(chunks.Count, Is.EqualTo(6 * source.mipmapCount));
                var i = 0;
                for (var face = 0; face < 6; face++)
                    for (var mip = 0; mip < source.mipmapCount; mip++, i++)
                    {
                        var side = Math.Max(1, size >> mip);
                        var stored = Inflate(payloads[(int)chunks[i]].Encoded);
                        Assert.That(stored.Length, Is.EqualTo(side * side * 8));
                        Assert.That(stored, Is.EqualTo(LilToonFullTexture.Read(source, mip, face, false, false, true, true)), "face " + face + " mip " + mip);
                    }
            }
            finally { Object.DestroyImmediate(source); }
        }

        static byte[] Inflate(byte[] compressed)
        {
            using var input = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
    }
}
