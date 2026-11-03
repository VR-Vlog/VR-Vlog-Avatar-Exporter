using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using VRVlog.AvatarFormats;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class JsonAccessTests
    {
        [Test]
        public void LenientReadersReturnNullForAMissingOwnerKeyOrType()
        {
            var inner = new Dictionary<string, object>();
            var list = new List<object>();
            var owner = new Dictionary<string, object> { ["object"] = inner, ["list"] = list, ["text"] = "a" };
            Assert.That(JsonAccess.Object(owner, "object"), Is.SameAs(inner));
            Assert.That(JsonAccess.List(owner, "list"), Is.SameAs(list));
            Assert.That(JsonAccess.Get(owner, "text"), Is.EqualTo("a"));
            Assert.That(JsonAccess.Get(null, "text"), Is.Null);
            Assert.That(JsonAccess.Get(owner, "missing"), Is.Null);
            Assert.That(JsonAccess.Object(null, "object"), Is.Null);
            Assert.That(JsonAccess.Object(owner, "list"), Is.Null);
            Assert.That(JsonAccess.List(null, "list"), Is.Null);
            Assert.That(JsonAccess.List(owner, "object"), Is.Null);
        }

        // The callers pass their own exception, so each format keeps its type and text.
        [Test]
        public void StrictReadersThrowTheCallersException()
        {
            var inner = new Dictionary<string, object>();
            var list = new List<object> { 1L };
            Assert.That(JsonAccess.Object(inner, Invalid), Is.SameAs(inner));
            Assert.That(JsonAccess.List(list, Invalid), Is.SameAs(list));
            Assert.That(JsonAccess.List(list, 1, 1, Invalid), Is.SameAs(list));
            Assert.That(JsonAccess.Text("ab", 2, Invalid), Is.EqualTo("ab"));

            Assert.That(() => JsonAccess.Object(list, () => new InvalidDataException("VRM のノードまたはスキンが不正です。")),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("VRM のノードまたはスキンが不正です。"));
            Assert.That(() => JsonAccess.Object(null, () => new InvalidOperationException("追跡表情のVRM情報が不正です: custom")),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("追跡表情のVRM情報が不正です: custom"));
            Assert.That(() => JsonAccess.List(JsonAccess.Get(null, "meshes"), () => new InvalidOperationException("追跡表情のVRM配列がありません: meshes")),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("追跡表情のVRM配列がありません: meshes"));
            Assert.That(() => JsonAccess.List(inner, Invalid), Throws.TypeOf<FormatException>().With.Message.EqualTo("invalid"));
            Assert.That(() => JsonAccess.List(list, 2, 3, Invalid), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonAccess.List(list, 0, 0, Invalid), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonAccess.Text("", 2, Invalid), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonAccess.Text("abc", 2, Invalid), Throws.TypeOf<FormatException>());
            Assert.That(() => JsonAccess.Text(1L, 2, Invalid), Throws.TypeOf<FormatException>());
        }

        [Test]
        public void SkinRepairKeepsItsMessages()
        {
            var noNodes = Glb(new Dictionary<string, object> { ["skins"] = new List<object> { new Dictionary<string, object>() } });
            Assert.That(() => ExportSkinRoots.Repair(noNodes),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("スキンの参照先ノードがありません。"));
            var badNode = Glb(new Dictionary<string, object>
            {
                ["skins"] = new List<object> { new Dictionary<string, object>() },
                ["nodes"] = new List<object> { "node" }
            });
            Assert.That(() => ExportSkinRoots.Repair(badNode),
                Throws.TypeOf<InvalidDataException>().With.Message.EqualTo("VRM のノードまたはスキンが不正です。"));
            var noSkins = Glb(new Dictionary<string, object>());
            Assert.That(ExportSkinRoots.Repair(noSkins), Is.SameAs(noSkins));
        }

        [Test]
        public void FormatRequirementsKeepTheirMessages()
        {
            var noAsset = GlbDocument.Create(new Dictionary<string, object>(), null).Write();
            Assert.That(() => AvatarFormatRequirements.Write(noAsset),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("glTF asset is missing."));
            var undeclared = Glb(new Dictionary<string, object> { ["extensionsUsed"] = new List<object> { "VRVLOG_missing" } });
            Assert.That(() => AvatarFormatRequirements.Write(undeclared),
                Throws.TypeOf<InvalidOperationException>().With.Message.EqualTo("Declared extension is missing: VRVLOG_missing"));
            var plain = Glb(new Dictionary<string, object>());
            Assert.That(AvatarFormatRequirements.Write(plain), Is.SameAs(plain));
        }

        static Exception Invalid() => new FormatException("invalid");

        static byte[] Glb(Dictionary<string, object> json)
        {
            json["asset"] = new Dictionary<string, object> { ["version"] = "2.0" };
            return GlbDocument.Create(json, null).Write();
        }
    }
}
