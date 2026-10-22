using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // An 8-bit normal is stored as rgba32 only when its quantized RG decode
    // stays within the fixed angle limits at the strongest material scale.
    public sealed class NormalStorageTests
    {
        // A moderate tilt whose canonical red sits half a step between two
        // bytes: about 0.2 degrees at scale 1, about 7 degrees at scale 9.6.
        const float Moderate = 140.5f / 255f, Centre = 128f / 255f;

        [Test]
        public void SmoothNormalsFitRgba32AndQuantizeToValidBytes()
        {
            var random = new System.Random(1);
            float Tilt() => (float)(random.NextDouble() * .8 - .4) * .5f + .5f;
            var chain = new List<byte[]> { HalfPixels(Enumerable.Range(0, 1024).Select(_ => (Tilt(), Tilt()))), HalfPixels(Enumerable.Range(0, 256).Select(_ => (Tilt(), Tilt()))) };
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(chain, 1), Is.True);
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(chain, .25f), Is.True, "A weaker scale decodes as scale 1.");
            foreach (var mip in chain)
            {
                var bytes = LilToonFullSnapshot.NormalToRgba32(mip);
                Assert.That(bytes.Length * 2, Is.EqualTo(mip.Length));
                Assert.DoesNotThrow(() => F.ValidatePixels(bytes, "rgba32"));
            }
        }

        [Test]
        public void QuantizationRoundsHalfDataToTheNearestByte()
        {
            var bytes = LilToonFullSnapshot.NormalToRgba32(HalfPixels(new[] { (.5f, 1.2f), (-.1f, 254.6f / 255f) }));
            Assert.That(bytes, Is.EqualTo(new byte[] { 128, 255, 255, 255, 0, 255, 255, 255 }));
        }

        [Test]
        public void ANearHorizonPixelAboveTheMaximumKeepsHalf()
        {
            // One pixel in 1001 is within the 1% the percentile allows, so
            // only the maximum rejects it (about 4.4 degrees at scale 1).
            var pixels = Enumerable.Repeat((Centre, Centre), 1000).Append((254.6f / 255f, Centre));
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(new[] { HalfPixels(pixels) }, 1), Is.False);
        }

        [TestCase(1f, true)]
        [TestCase(8f, false)]
        [TestCase(-9.6f, false)]
        public void AModerateTiltFitsOnlyAtAWeakScale(float scale, bool fits)
        {
            var pixels = HalfPixels(Enumerable.Repeat((Moderate, Centre), 64));
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(new[] { pixels }, scale), Is.EqualTo(fits));
        }

        [TestCase(2, true)]
        [TestCase(3, false)]
        public void ThePercentileAllowsOnlyOnePercentAboveHalfADegree(int outliers, bool fits)
        {
            // Each outlier turns about 0.85 degrees: above the percentile
            // limit, below the maximum. 200 flat pixels allow 2 of them.
            var pixels = Enumerable.Repeat((Centre, Centre), 200).Concat(Enumerable.Repeat((250.5f / 255f, Centre), outliers));
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(new[] { HalfPixels(pixels) }, 1), Is.EqualTo(fits));
        }

        [TestCase(float.NaN)]
        public void AnUnknownScaleKeepsHalf(float scale)
        {
            var pixels = HalfPixels(Enumerable.Repeat((Centre, Centre), 16));
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(new[] { pixels }, scale), Is.False);
        }

        [TestCase(new[] { 1f }, "rgba32")]
        [TestCase(new[] { 9.6f, 1f }, "rgbaHalf")]
        [TestCase(new[] { 1f, -9.6f }, "rgbaHalf")]
        public void TheStrongestReferenceDecidesASharedNormal(float[] scales, string expected)
        {
            var source = ModerateNormal();
            var materials = scales.Select(scale => Bumped(source, scale)).ToList();
            try
            {
                var snapshot = new LilToonFullSnapshot();
                foreach (var material in materials) ReadMaterial(snapshot, material);
                AssertStored(snapshot, source, expected);
            }
            finally
            {
                foreach (var material in materials) Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AReferenceWithoutADecodeScaleKeepsHalf(bool unknownFirst)
        {
            var source = ModerateNormal();
            var bumped = Bumped(source, 1);
            var tangent = new Material(Shader.Find("lilToon"));
            try
            {
                var shader = tangent.shader;
                var property = shader.FindPropertyIndex("_AnisotropyTangentMap");
                Assert.That(property, Is.GreaterThanOrEqualTo(0));
                Assert.That(shader.GetPropertyFlags(property) & ShaderPropertyFlags.Normal, Is.EqualTo(ShaderPropertyFlags.Normal));
                tangent.SetTexture("_AnisotropyTangentMap", source);
                var snapshot = new LilToonFullSnapshot();
                foreach (var material in unknownFirst ? new[] { tangent, bumped } : new[] { bumped, tangent }) ReadMaterial(snapshot, material);
                AssertStored(snapshot, source, "rgbaHalf");
            }
            finally
            {
                Object.DestroyImmediate(tangent);
                Object.DestroyImmediate(bumped);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CompleteExportStoresAnEightBitNormalByItsStrongestScale(bool strongerSecondMaterial)
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var source = ModerateNormal();
            var front = Bumped(source, 1);
            var back = Bumped(source, strongerSecondMaterial ? 9.6f : 1);
            try
            {
                var skins = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.That(skins.Length, Is.EqualTo(2));
                skins[0].sharedMaterial = front;
                skins[1].sharedMaterial = back;
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Normal storage", "Tests", new List<string>(),
                    exporterVersion: "0.10.0-preview.1", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                var glb = GlbDocument.Read(bytes);
                Assert.DoesNotThrow(() => F.Validate(glb.Json, bytes.LongLength, glb.Binary.Length));
                var root = F.Root(glb.Json);
                var record = F.List(root, "textures").Select(F.Object).Single(t => F.Text(t, "name") == source.name);
                var format = strongerSecondMaterial ? "rgbaHalf" : "rgba32";
                Assert.That(F.Text(record, "format"), Is.EqualTo(format));
                Assert.That(F.Bool(record, "normal"), Is.True);
                Assert.That(F.Int(record, "mips"), Is.EqualTo(source.mipmapCount));
                Assert.That(F.List(record, "chunks").Count, Is.EqualTo(source.mipmapCount));
                for (var mip = 0; mip < source.mipmapCount; mip++)
                {
                    var chunk = F.Object(F.At(F.List(root, "chunks"), F.Integer(F.List(record, "chunks")[mip])));
                    var size = Math.Max(1, source.width >> mip) * Math.Max(1, source.height >> mip);
                    Assert.That(F.Int(chunk, "decodedBytes"), Is.EqualTo(size * (format == "rgba32" ? 4 : 8)), "mip " + mip);
                }
            }
            finally
            {
                Object.DestroyImmediate(front);
                Object.DestroyImmediate(back);
                Object.DestroyImmediate(source);
            }
        }

        // Every pixel has the same moderate tilt. The alpha of 254 makes the
        // canonical red (red times alpha) fall between two bytes.
        static Texture2D ModerateNormal()
        {
            var source = new Texture2D(16, 16, TextureFormat.RGBA32, true, true) { name = "Normal storage moderate tilt", filterMode = FilterMode.Bilinear };
            for (var mip = 0; mip < source.mipmapCount; mip++)
            {
                var size = Math.Max(1, 16 >> mip);
                source.SetPixels32(Enumerable.Repeat(new Color32(141, 128, 255, 254), size * size).ToArray(), mip);
            }
            source.Apply(false, false);
            var halfReads = Enumerable.Range(0, source.mipmapCount).Select(mip => LilToonFullTexture.Read(source, mip, 0, true, false, true, true)).ToList();
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(halfReads, 1), Is.True, "Precondition: the canonical copy passes at scale 1.");
            Assert.That(LilToonFullSnapshot.NormalFitsRgba32(halfReads, 9.6f), Is.False, "Precondition: the canonical copy fails at scale 9.6.");
            return source;
        }

        static Material Bumped(Texture source, float scale)
        {
            var material = new Material(Shader.Find("lilToon")) { name = "Normal storage scale " + scale };
            material.SetFloat("_UseBumpMap", 1);
            material.SetTexture("_BumpMap", source);
            material.SetFloat("_BumpScale", scale);
            return material;
        }

        static void ReadMaterial(LilToonFullSnapshot snapshot, Material material)
            => typeof(LilToonFullSnapshot).GetMethod("ReadMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(snapshot, new object[] { material });

        // The stored chain matches the GPU's canonical Half copy, quantized
        // only for rgba32, and a re-decision leaves no orphaned payloads.
        static void AssertStored(LilToonFullSnapshot snapshot, Texture2D source, string format)
        {
            var hidden = BindingFlags.Instance | BindingFlags.NonPublic;
            var textures = (List<object>)typeof(LilToonFullSnapshot).GetField("textures", hidden).GetValue(snapshot);
            var payloads = (List<DeflatePayload>)typeof(LilToonFullSnapshot).GetField("payloads", hidden).GetValue(snapshot);
            var record = textures.Select(F.Object).Single(t => F.Text(t, "name") == source.name);
            Assert.That(F.Text(record, "format"), Is.EqualTo(format));
            Assert.That(F.Bool(record, "normal"), Is.True);
            Assert.That(textures.Select(F.Object).Sum(t => F.List(t, "chunks").Count), Is.EqualTo(payloads.Count));
            for (var mip = 0; mip < source.mipmapCount; mip++)
            {
                var payload = payloads[F.Integer(F.List(record, "chunks")[mip])];
                using var encoded = new MemoryStream(payload.Encoded, false);
                using var decoder = new DeflateStream(encoded, CompressionMode.Decompress);
                using var decoded = new MemoryStream();
                decoder.CopyTo(decoded);
                var half = LilToonFullTexture.Read(source, mip, 0, true, false, true, true);
                Assert.That(decoded.ToArray(), Is.EqualTo(format == "rgba32" ? LilToonFullSnapshot.NormalToRgba32(half) : half), "mip " + mip);
            }
        }

        // Canonical RG with B and A of 1, as the GPU copy writes them.
        static byte[] HalfPixels(IEnumerable<(float r, float g)> pixels)
        {
            var list = pixels.ToList();
            var bytes = new byte[list.Count * 8];
            for (var i = 0; i < list.Count; i++)
            {
                var values = new[] { list[i].r, list[i].g, 1f, 1f };
                for (var c = 0; c < 4; c++)
                {
                    var bits = Mathf.FloatToHalf(values[c]);
                    bytes[i * 8 + c * 2] = (byte)bits;
                    bytes[i * 8 + c * 2 + 1] = (byte)(bits >> 8);
                }
            }
            return bytes;
        }
    }
}
