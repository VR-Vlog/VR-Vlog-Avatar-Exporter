using System.Linq;
using VRVlog.LilToonExporter;
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

        // A name before ": " without a target and a reason is not a kind: material and object
        // notes are sentences of their own, and two of them on one material stay two lines.
        [Test]
        public void TransferNotesKeepNamedSentencesAsLinesOfTheirOwn()
        {
            var notes = TransferNotes.Build(new[]
            {
                "Body: 影の色を近似しました。",
                "Body: 透明部分を切り抜きに変えました。",
                "除外: 小物 01",
            });
            Assert.That(notes.Notes.Count, Is.EqualTo(0));
            Assert.That(notes.NoteCount, Is.EqualTo(0));
            Assert.That(notes.Records.Select(group => group.Kind), Is.EqualTo(new[] { "除外", null }));
            Assert.That(notes.Records[1].Items.Select(item => item.Target), Is.EqualTo(new[]
            {
                "Body: 影の色を近似しました。",
                "Body: 透明部分を切り抜きに変えました。",
            }));
        }

        // A single target keeps its reason on its own line.
        [Test]
        public void TransferNotesKeepTheReasonOfASingleTarget()
        {
            var group = TransferNotes.Build(new[] { "表情を省略: メニュー 01 — 動かない表情です" }).Notes.Single();
            Assert.That(group.CommonReason == null, Is.True);
            Assert.That(group.Items.Single().Reason, Is.EqualTo("動かない表情です"));
        }

        // What the conversion changed is read back by kind and leaves the lines: the lilToon versions,
        // the expressions, each texture with its sizes, and each PhysBone sentence once with its PhysBones.
        [Test]
        public void TransferNotesReadTheConversionByKind()
        {
            var notes = TransferNotes.Build(new[]
            {
                string.Format(ExportNoteText.LilToonRendered, "2.4.1", "2.3.4"),
                string.Format(ExportNoteText.ExpressionsAdded, 13),
                ExportNoteText.FxRestSkipped,
                string.Format(ExportNoteText.LilToonTextureResized, "Body", 4096, 2048, 1024, 512),
                string.Format(ExportNoteText.MainTextureResized, "Face", 512, 512),
                ExportNoteText.Spring("Hair", ExportNoteText.SpringForces),
                ExportNoteText.Spring("Hair", ExportNoteText.SpringStretch),
                ExportNoteText.Spring("Hair", ExportNoteText.SpringNotConverted),
                ExportNoteText.Spring("Tail", ExportNoteText.SpringForces),
                ExportNoteText.Spring("Tail", ExportNoteText.SpringNotConverted),
                "除外: 小物 01",
            });

            Assert.That(notes.LilToonInstalled, Is.EqualTo("2.4.1"));
            Assert.That(notes.LilToonDrawn, Is.EqualTo("2.3.4"));
            Assert.That(notes.Expressions, Is.EqualTo(13));
            Assert.That(notes.FxRestSkipped, Is.True);
            Assert.That(notes.Textures.Select(texture => (texture.Name, texture.FromWidth, texture.FromHeight, texture.Width, texture.Height)),
                Is.EqualTo(new[] { ("Body", 4096, 2048, 1024, 512), ("Face", 0, 0, 512, 512) }));
            Assert.That(notes.SpringBones, Is.EqualTo(new[] { "Hair", "Tail" }));
            Assert.That(notes.Springs.Select(spring => spring.Sentence),
                Is.EqualTo(new[] { ExportNoteText.SpringForces, ExportNoteText.SpringStretch, ExportNoteText.SpringNotConverted }));
            Assert.That(notes.Springs[1].Bones, Is.EqualTo(new[] { "Hair" }));
            Assert.That(notes.Springs[2].Bones, Is.EqualTo(new[] { "Hair", "Tail" }));
            Assert.That(notes.HasConversion, Is.True);
            // Only the record is left as a line.
            Assert.That(notes.Records.Select(group => group.Kind), Is.EqualTo(new[] { "除外" }));
            Assert.That(notes.NoteCount, Is.EqualTo(0));
        }

        // Without expressions the note says none could be added; without conversion notes nothing is read.
        [Test]
        public void TransferNotesReadNoExpressions()
        {
            Assert.That(TransferNotes.Build(new[] { ExportNoteText.ExpressionsNone }).Expressions, Is.EqualTo(0));
            var none = TransferNotes.Build(new[] { "除外: 小物 01" });
            Assert.That(none.HasConversion, Is.False);
            Assert.That(none.Expressions == null, Is.True);
        }

#if !VRVLOG_LAN_TRANSFER_CLI
        // Every PhysBone sentence has a short label for the rows (the rows are drawn in Unity only).
        [Test]
        public void TransferNotesHaveALabelForEachSpringSentence()
        {
            Assert.That(ConversionText.SpringLabels.Length, Is.EqualTo(ExportNoteText.SpringSentences.Length));
        }
#endif
    }
}
