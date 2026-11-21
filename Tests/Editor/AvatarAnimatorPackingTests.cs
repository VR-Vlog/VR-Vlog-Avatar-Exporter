using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VRVlog.AvatarAnimator;

namespace VRVlog.LilToonExporter.Tests
{
    // VRVLOG_avatar_animator in the GLB (schema 2): the document packed into
    // a bufferView of its own, read back, and removed with its bytes.
    public sealed class AvatarAnimatorPackingTests
    {
        // A GLB of two views (an image's and one more) and 10 bytes of binary.
        static GlbDocument Glb()
        {
            var json = (Dictionary<string, object>)JsonDom.Parse(
                "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"byteLength\":10}]," +
                "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":6},{\"buffer\":0,\"byteOffset\":8,\"byteLength\":2}]," +
                "\"images\":[{\"bufferView\":1,\"mimeType\":\"image/png\"}],\"extensionsUsed\":[\"VRMC_vrm\"],\"extensions\":{\"VRMC_vrm\":{}}}");
            return GlbDocument.Create(json, new byte[] { 1, 2, 3, 4, 5, 6, 0, 0, 7, 8 });
        }

        static Dictionary<string, object> Document() => new Dictionary<string, object>
        {
            ["schemaVersion"] = (long)AvatarAnimatorData.SchemaVersion,
            ["clips"] = new List<object> { new Dictionary<string, object> { ["name"] = "Hat", ["tracks"] = new List<object>() } }
        };

        [Test]
        public void PlaceWritesTheDocumentIntoABufferViewOfItsOwn()
        {
            var glb = Glb();
            AvatarAnimatorPacking.Place(glb, Document());
            var read = GlbDocument.Read(glb.Write());

            Assert.That(JsonDom.Serialize(read.Find("extensions")[AvatarAnimatorData.Extension]), Is.EqualTo("{\"schemaVersion\":2,\"bufferView\":2}"));
            Assert.That(((List<object>)read.Json["extensionsUsed"]).Last(), Is.EqualTo(AvatarAnimatorData.Extension));
            var view = (Dictionary<string, object>)((List<object>)read.Json["bufferViews"])[2];
            Assert.That((Convert.ToInt64(view["buffer"]), Convert.ToInt64(view["byteOffset"])), Is.EqualTo((0L, 12L)), "After the binary, on four bytes.");
            Assert.That(((Dictionary<string, object>)((List<object>)read.Json["buffers"])[0])["byteLength"], Is.EqualTo((long)glb.Binary.Length));
            Assert.That(JsonDom.Serialize(AvatarAnimatorPacking.Document(read)), Is.EqualTo(JsonDom.Serialize(Document())));
        }

        [Test]
        public void DocumentReadsAnExtensionWrittenInTheJsonAsItIs()
        {
            var glb = Glb();
            Assert.That(AvatarAnimatorPacking.Document(glb), Is.Null);
            glb.PlaceExtension(AvatarAnimatorData.Extension, Document());
            Assert.That(AvatarAnimatorPacking.Document(glb), Is.SameAs(glb.Find("extensions")[AvatarAnimatorData.Extension]));
        }

        [Test]
        public void DocumentRejectsAViewOutsideTheBinary()
        {
            var glb = Glb();
            AvatarAnimatorPacking.Place(glb, Document());
            var view = (Dictionary<string, object>)((List<object>)glb.Json["bufferViews"])[2];
            view["byteLength"] = (long)view["byteLength"] + 1;
            Assert.Throws<InvalidDataException>(() => AvatarAnimatorPacking.Document(glb));
            view["byteLength"] = (long)view["byteLength"] - 2;
            Assert.Throws<InvalidOperationException>(() => AvatarAnimatorPacking.Document(glb), "A cut document is not one.");
        }

        // Removed, the file is the one written without it, also when a later
        // stage appended a view after it: that view and what names it move down.
        [Test]
        public void RemoveTakesTheViewAndItsBytes()
        {
            var without = Glb();
            var with = Glb();
            AvatarAnimatorPacking.Place(with, Document());
            foreach (var glb in new[] { without, with })
            {
                var offset = glb.AppendBinaryBatch(new[] { new byte[] { 9, 9, 9 } })[0];
                var views = (List<object>)glb.Json["bufferViews"];
                views.Add(new Dictionary<string, object> { ["buffer"] = 0L, ["byteOffset"] = (long)offset, ["byteLength"] = 3L });
                ((List<object>)glb.Json["images"]).Add(new Dictionary<string, object> { ["bufferView"] = (long)(views.Count - 1), ["mimeType"] = "image/png" });
            }
            var read = GlbDocument.Read(with.Write());

            Assert.That(AvatarAnimatorPacking.Remove(read), Is.True);
            Assert.That(JsonDom.Serialize(read.Json), Is.EqualTo(JsonDom.Serialize(GlbDocument.Read(without.Write()).Json)));
            Assert.That(read.Binary, Is.EqualTo(without.Binary));
            Assert.That(AvatarAnimatorPacking.Remove(read), Is.False, "Nothing is left to remove.");
        }

        [Test]
        public void RemoveTakesAnExtensionWrittenInTheJson()
        {
            var glb = Glb();
            var before = JsonDom.Serialize(glb.Json);
            glb.PlaceExtension(AvatarAnimatorData.Extension, Document());
            Assert.That(AvatarAnimatorPacking.Remove(glb), Is.True);
            Assert.That(JsonDom.Serialize(glb.Json), Is.EqualTo(before));
            Assert.That(glb.Binary.Length, Is.EqualTo(10));
        }

        // A view another key also names stays, and so do bytes another view reaches into.
        [Test]
        public void RemoveKeepsAViewOrBytesSomethingElseUses()
        {
            var named = Glb();
            AvatarAnimatorPacking.Place(named, Document());
            ((List<object>)named.Json["images"]).Add(new Dictionary<string, object> { ["bufferView"] = 2L, ["mimeType"] = "image/png" });
            var binary = named.Binary;
            AvatarAnimatorPacking.Remove(named);
            Assert.That(((List<object>)named.Json["bufferViews"]).Count, Is.EqualTo(3));
            Assert.That(named.Binary, Is.SameAs(binary));

            var overlapped = Glb();
            AvatarAnimatorPacking.Place(overlapped, Document());
            var views = (List<object>)overlapped.Json["bufferViews"];
            var packed = (Dictionary<string, object>)views[2];
            views.Add(new Dictionary<string, object> { ["buffer"] = 0L, ["byteOffset"] = packed["byteOffset"], ["byteLength"] = 4L });
            ((List<object>)overlapped.Json["images"]).Add(new Dictionary<string, object> { ["bufferView"] = 3L, ["mimeType"] = "image/png" });
            binary = overlapped.Binary;
            AvatarAnimatorPacking.Remove(overlapped);
            Assert.That(((List<object>)overlapped.Json["bufferViews"]).Count, Is.EqualTo(3));
            Assert.That(((Dictionary<string, object>)((List<object>)overlapped.Json["images"])[1])["bufferView"], Is.EqualTo(2L));
            Assert.That(overlapped.Binary, Is.SameAs(binary));
        }
    }
}
