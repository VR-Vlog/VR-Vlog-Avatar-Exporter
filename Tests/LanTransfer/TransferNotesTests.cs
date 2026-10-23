using System.Linq;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    public sealed partial class CloudTransferTests
    {
        // The board's page after a transfer folds the notes by kind: the largest kind first, a reason
        // every target shares once, the same line once, and what the export did as asked after the notes.
        [Test]
        public void TransferNotesGroupByKindWithTheSharedReasonOnce()
        {
            var notes = TransferNotes.Build(new[]
            {
                "表情を省略: メニュー 01 — 動かない表情です",
                "除外: 小物 01",
                "表情を省略: メニュー 02 — 動かない表情です",
                "表情を省略: メニュー 01 — 動かない表情です",
                "マテリアルを置き換え: 素材 01 — 対応していないシェーダーです",
                "マテリアルを置き換え: 素材 02 — テクスチャがありません",
                "表情を省略: メニュー 03 — 動かない表情です",
                "ボーンが見つからないため、揺れものを書き出しませんでした。",
                "  ",
                null,
            });

            Assert.That(notes.Notes.Select(group => group.Kind), Is.EqualTo(new[] { "表情を省略", "マテリアルを置き換え" }));
            Assert.That(notes.NoteCount, Is.EqualTo(5));
            var faces = notes.Notes[0];
            Assert.That(faces.CommonReason, Is.EqualTo("動かない表情です"));
            Assert.That(faces.Items.Select(item => item.Target), Is.EqualTo(new[] { "メニュー 01", "メニュー 02", "メニュー 03" }));
            Assert.That(faces.Items.All(item => item.Reason == null), Is.True);
            // Different reasons stay with their targets.
            var materials = notes.Notes[1];
            Assert.That(materials.CommonReason == null, Is.True);
            Assert.That(materials.Items.Select(item => item.Reason), Is.EqualTo(new[] { "対応していないシェーダーです", "テクスチャがありません" }));

            Assert.That(notes.Records.Select(group => group.Kind), Is.EqualTo(new[] { "除外", null }));
            Assert.That(notes.Records[0].Items.Single().Target, Is.EqualTo("小物 01"));
            Assert.That(notes.Records[1].Items.Single().Target, Is.EqualTo("ボーンが見つからないため、揺れものを書き出しませんでした。"));
        }

        [Test]
        public void TransferNotesAreEmptyWithoutLines()
        {
            foreach (var notes in new[] { TransferNotes.Build(null), TransferNotes.Build(new string[0]) })
            {
                Assert.That(notes.Notes.Count, Is.EqualTo(0));
                Assert.That(notes.Records.Count, Is.EqualTo(0));
                Assert.That(notes.NoteCount, Is.EqualTo(0));
            }
        }

        // A single target keeps its reason on its own line.
        [Test]
        public void TransferNotesKeepTheReasonOfASingleTarget()
        {
            var group = TransferNotes.Build(new[] { "表情を省略: メニュー 01 — 動かない表情です" }).Notes.Single();
            Assert.That(group.CommonReason == null, Is.True);
            Assert.That(group.Items.Single().Reason, Is.EqualTo("動かない表情です"));
        }
    }
}
