using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportAppearanceReportLanguageTests
    {
        // The downscale notes are written in the language of the Editor, and the "見た目の変更" summary
        // after an export still lists them.
        [TestCase("ja")]
        [TestCase("en")]
        [TestCase("ko")]
        [TestCase("zh-Hans")]
        [TestCase("zh-Hant")]
        public void DownscaleNotesInTheEditorLanguageAreAppearanceChanges(string locale)
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
                // As LilToonFullSnapshot, MobileTextureEncoder, LilToonMainTextureBaker and AlphaMaskBaker write them.
                var notes = new[]
                {
                    ExporterLocalization.Format(ExportNoteText.LilToonTextureResized, "Body", 4096, 2048, 1024, 512),
                    ExporterLocalization.Format(ExportNoteText.OutputTextureResized, "Hair", 2048, 2048, 1024, 1024),
                    ExporterLocalization.Format(ExportNoteText.MainTextureResized, "Face", 512, 256),
                    ExporterLocalization.Format(ExportNoteText.AlphaMaskResized, "Skirt", 256, 128),
                };
                Assert.That(ExportAppearanceReport.Changes(notes), Is.EqualTo(notes), locale);
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }

        // The notes of a left-out stage, lost BlendShapes and a substituted shader are written in the
        // language of the Editor too, and an English summary used to drop them.
        [TestCase("ja")]
        [TestCase("en")]
        [TestCase("ko")]
        [TestCase("zh-Hans")]
        [TestCase("zh-Hant")]
        public void OmissionAndApproximationNotesInTheEditorLanguageAreAppearanceChanges(string locale)
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
                // As UniVrmOneClickExporter.RecordOmissions, NdmfExportPreparation and UnsupportedShaderSubstitution write them.
                var notes = new[]
                {
                    ExporterLocalization.T("書き出しの一部を省略しました") + ": Expressions — reason",
                    ExporterLocalization.T(NdmfExportPreparation.LostGeneratedMorphs) + "Body/Smile",
                    ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Hair",
                    ExporterLocalization.T(UnsupportedShaderSubstitution.Failed) + "Skirt",
                };
                var other = ExporterLocalization.T(UnsupportedShaderSubstitution.KeptForExpressions) + "Face";
                Assert.That(ExportAppearanceReport.Changes(notes.Append(other)), Is.EqualTo(notes), locale);
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }
    }
}
