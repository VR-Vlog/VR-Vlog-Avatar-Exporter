#if !VRVLOG_LAN_TRANSFER_CLI
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    public sealed class TransferNotesLanguageTests
    {
        // The notes of a downscaled image are written in the language of the Editor, so they read in
        // that language on the result list, and the page after a transfer still reads them back.
        [TestCase("en")]
        [TestCase("ko")]
        [TestCase("zh-Hans")]
        [TestCase("zh-Hant")]
        public void TextureNotesWrittenInTheEditorLanguageAreReadBack(string locale)
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Static;
            var localeField = typeof(ExporterLocalization).GetField("_locale", flags);
            var messagesField = typeof(ExporterLocalization).GetField("_messages", flags);
            var previousLocale = localeField.GetValue(null);
            var previousMessages = messagesField.GetValue(null);
            try
            {
                localeField.SetValue(null, locale);
                messagesField.SetValue(null, null);
                foreach (var template in new[]
                {
                    ExportNoteText.LilToonTextureResized, ExportNoteText.OutputTextureResized,
                    ExportNoteText.MainTextureResized, ExportNoteText.AlphaMaskResized,
                })
                    Assert.That(ExporterLocalization.T(template), Is.Not.EqualTo(template), locale + ": no translation of " + template);

                // As LilToonFullSnapshot, MobileTextureEncoder, LilToonMainTextureBaker and AlphaMaskBaker write them.
                var lines = new[]
                {
                    ExportNoteText.Format(ExportNoteText.LilToonTextureResized, "Body", 4096, 2048, 1024, 512),
                    ExportNoteText.Format(ExportNoteText.OutputTextureResized, "Hair", 2048, 2048, 1024, 1024),
                    ExportNoteText.Format(ExportNoteText.MainTextureResized, "Face", 512, 256),
                    ExportNoteText.Format(ExportNoteText.AlphaMaskResized, "Skirt", 256, 128),
                };
                var notes = TransferNotes.Build(lines.Concat(new[] { "除外: 小物 01" }));
                Assert.That(notes.Textures.Select(texture => (texture.Name, texture.FromWidth, texture.FromHeight, texture.Width, texture.Height)),
                    Is.EqualTo(new[] { ("Body", 4096, 2048, 1024, 512), ("Hair", 2048, 2048, 1024, 1024), ("Face", 0, 0, 512, 256), ("Skirt", 0, 0, 256, 128) }));
                Assert.That(notes.HasConversion, Is.True);
                Assert.That(notes.NoteCount, Is.EqualTo(0));
                Assert.That(notes.Records.Select(group => group.Kind), Is.EqualTo(new[] { "除外" }));

                // In Japanese only the Japanese templates are tried, so none of the translated lines is read:
                // the reads above went through the translated templates.
                localeField.SetValue(null, "ja");
                Assert.That(TransferNotes.Build(lines).Textures, Is.Empty);
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }
    }
}
#endif
