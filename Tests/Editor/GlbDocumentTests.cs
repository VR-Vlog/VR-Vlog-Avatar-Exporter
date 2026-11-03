using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class GlbDocumentTests
    {
        [Test]
        public void RoundTripPreservesJsonAndBinary()
        {
            var json = new Dictionary<string, object> { { "asset", new Dictionary<string, object> { { "version", "2.0" } } }, { "name", "日本語" } };
            var source = Build(json, new byte[] { 1, 2, 3, 4 });
            var document = GlbDocument.Read(source);
            var output = document.Write();
            var roundTrip = GlbDocument.Read(output);
            Assert.AreEqual("日本語", roundTrip.Json["name"]);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, roundTrip.Binary);
        }

        [Test]
        public void AppendBinaryAlignsPayloadAndUpdatesDeclaredLength()
        {
            var json = new Dictionary<string, object>
            {
                { "asset", new Dictionary<string, object> { { "version", "2.0" } } },
                { "buffers", new List<object> { new Dictionary<string, object> { { "byteLength", 3L } } } }
            };
            var document = GlbDocument.Read(Build(json, new byte[] { 1, 2, 3 }));

            var offset = document.AppendBinaryBatch(new[] { new byte[] { 4, 5 } })[0];
            var roundTrip = GlbDocument.Read(document.Write());
            var buffer = (Dictionary<string, object>)((List<object>)roundTrip.Json["buffers"])[0];

            Assert.AreEqual(4, offset);
            Assert.AreEqual(6L, buffer["byteLength"]);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 0, 4, 5, 0, 0 }, roundTrip.Binary);
        }

        [TestCase(0x46546C66u)] [TestCase(0x46546C67u)]
        public void RejectsInvalidHeader(uint magic)
        {
            var bytes = Build(new Dictionary<string, object>{{"asset",new Dictionary<string, object>{{"version","2.0"}}}}, Array.Empty<byte>());
            bytes[0]=(byte)magic; bytes[1]=(byte)(magic>>8); bytes[2]=(byte)(magic>>16); bytes[3]=(byte)(magic>>24);
            if (magic == 0x46546C67u) Assert.DoesNotThrow(()=>GlbDocument.Read(bytes)); else Assert.Throws<System.IO.InvalidDataException>(()=>GlbDocument.Read(bytes));
        }

        [Test]
        public void PlaceExtensionListsTheNameOnceAndKeepsTheKeyOrder()
        {
            var document = GlbDocument.Create(new Dictionary<string, object>(), null);
            document.PlaceExtension("B", 1L);
            document.PlaceExtension("A", 2L);
            document.PlaceExtension("B", 3L);
            var extensions = (Dictionary<string, object>)document.Json["extensions"];
            CollectionAssert.AreEqual(new[] { "B", "A" }, extensions.Keys);
            Assert.AreEqual(3L, extensions["B"]);
            CollectionAssert.AreEqual(new object[] { "B", "A" }, (List<object>)document.Json["extensionsUsed"]);

            Assert.That(() => document.PlaceExtension("A", 4L, "already there"),
                Throws.InvalidOperationException.With.Message.EqualTo("already there"));
            Assert.AreEqual(2L, extensions["A"]);
            document.PlaceExtension("C", 5L, "already there");
            CollectionAssert.AreEqual(new object[] { "B", "A", "C" }, (List<object>)document.Json["extensionsUsed"]);
        }

        [Test]
        public void FindWalksObjectsByKey()
        {
            var custom = new Dictionary<string, object>();
            var json = new Dictionary<string, object>
            {
                ["extensions"] = new Dictionary<string, object> { ["VRMC_vrm"] = new Dictionary<string, object> { ["expressions"] = new Dictionary<string, object> { ["custom"] = custom } } },
                ["meshes"] = new List<object>()
            };
            var document = GlbDocument.Create(json, null);
            Assert.That(document.Find("extensions", "VRMC_vrm", "expressions", "custom"), Is.SameAs(custom));
            Assert.That(document.Find(), Is.SameAs(json));
            Assert.That(document.Find("extensions", "missing", "custom"), Is.Null);
            Assert.That(document.Find("meshes"), Is.Null);
        }

        private static byte[] Build(Dictionary<string, object> json, byte[] binary)
        {
            return GlbDocument.Create(json, binary).Write();
        }
    }
}
