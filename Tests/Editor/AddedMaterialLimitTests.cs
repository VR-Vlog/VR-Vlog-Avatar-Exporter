using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class AddedMaterialLimitTests
    {
        // Material 0 is drawn with and uses texture 0. The added materials 1
        // and 2 use texture 1 and 2 alone; material 3 (added) uses only texture
        // 0, which the drawn material needs anyway.
        static GlbDocument File()
        {
            Dictionary<string, object> Info(int texture) => new Dictionary<string, object> { { "index", (long)texture } };
            Dictionary<string, object> View(int offset) => new Dictionary<string, object> { { "buffer", 0L }, { "byteOffset", (long)offset }, { "byteLength", 4L } };
            var json = new Dictionary<string, object>
            {
                { "asset", new Dictionary<string, object> { { "version", "2.0" } } },
                { "buffers", new List<object> { new Dictionary<string, object> { { "byteLength", 16L } } } },
                { "bufferViews", new List<object> { View(0), View(4), View(8), View(12) } },
                { "accessors", new List<object> { new Dictionary<string, object> { { "bufferView", 0L }, { "componentType", 5126L }, { "count", 1L }, { "type", "SCALAR" } } } },
                { "images", new List<object> {
                    new Dictionary<string, object> { { "bufferView", 1L }, { "mimeType", "image/png" } },
                    new Dictionary<string, object> { { "bufferView", 2L }, { "mimeType", "image/png" } },
                    new Dictionary<string, object> { { "bufferView", 3L }, { "mimeType", "image/png" } } } },
                { "textures", new List<object> {
                    new Dictionary<string, object> { { "source", 0L } },
                    new Dictionary<string, object> { { "source", 1L } },
                    new Dictionary<string, object> { { "source", 2L } } } },
                { "materials", new List<object> {
                    new Dictionary<string, object> { { "pbrMetallicRoughness", new Dictionary<string, object> { { "baseColorTexture", Info(0) } } } },
                    new Dictionary<string, object> { { "extensions", new Dictionary<string, object> { { "VRMC_materials_mtoon",
                        new Dictionary<string, object> { { "shadeMultiplyTexture", Info(1) } } } } } },
                    new Dictionary<string, object> { { "pbrMetallicRoughness", new Dictionary<string, object> { { "baseColorTexture", Info(2) } } } },
                    new Dictionary<string, object> { { "pbrMetallicRoughness", new Dictionary<string, object> { { "baseColorTexture", Info(0) } } } } } },
                { "meshes", new List<object> { new Dictionary<string, object> { { "primitives", new List<object> {
                    new Dictionary<string, object> { { "attributes", new Dictionary<string, object> { { "POSITION", 0L } } }, { "material", 0L } } } } } } },
            };
            return GlbDocument.Create(json, Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
        }

        [Test]
        public void AddedMaterialsFitInTheOrderGivenWhileBothLimitsHold()
        {
            var json = File().Json;
            Assert.That(AddedMaterialLimit.Fitting(json, 1), Is.EqualTo(3));
            // Texture 1 would be the second texture: none fits, and 3 after it is not tried.
            Assert.That(AddedMaterialLimit.Fitting(json, 1, maximumTextures: 1), Is.Zero);
            Assert.That(AddedMaterialLimit.Fitting(json, 1, maximumTextures: 2), Is.EqualTo(1));
            Assert.That(AddedMaterialLimit.Fitting(json, 1, maximumMaterials: 3), Is.EqualTo(2));
            // Already over with the drawn material alone: nothing is added.
            Assert.That(AddedMaterialLimit.Fitting(json, 1, maximumMaterials: 0), Is.Zero);
        }

        [Test]
        public void TrimLeavesOutTheMaterialsAndWhatOnlyTheyUsed()
        {
            var bytes = File().Write();
            var trimmed = GlbDocument.Read(AddedMaterialLimit.Trim(bytes, 2));
            var json = trimmed.Json;
            Assert.That(((List<object>)json["materials"]).Count, Is.EqualTo(2));
            // Texture 2, image 2 and bufferView 3 were only material 2's.
            Assert.That(((List<object>)json["textures"]).Cast<Dictionary<string, object>>().Select(t => Convert.ToInt32(t["source"])), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(((List<object>)json["images"]).Cast<Dictionary<string, object>>().Select(i => Convert.ToInt32(i["bufferView"])), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(((List<object>)json["bufferViews"]).Count, Is.EqualTo(3));
            Assert.That(trimmed.Binary.Length, Is.LessThan(16));

            // Leaving out material 1 as well renumbers nothing a kept material uses.
            json = GlbDocument.Read(AddedMaterialLimit.Trim(bytes, 1)).Json;
            Assert.That(((List<object>)json["textures"]).Count, Is.EqualTo(1));
            Assert.That(((List<object>)json["bufferViews"]).Count, Is.EqualTo(2));
            var info = (Dictionary<string, object>)((Dictionary<string, object>)((Dictionary<string, object>)((List<object>)json["materials"])[0])["pbrMetallicRoughness"])["baseColorTexture"];
            Assert.That(Convert.ToInt32(info["index"]), Is.Zero);

            Assert.That(AddedMaterialLimit.Trim(bytes, 4), Is.SameAs(bytes));
            Assert.Throws<InvalidOperationException>(() => AddedMaterialLimit.Trim(bytes, 0));
        }
    }
}
