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

        // The notes of a left-out stage, lost BlendShapes, a substituted shader or material and a
        // skipped expression setup are written in the language of the Editor too, and an English
        // summary used to drop them.
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
                // As UniVrmOneClickExporter, NdmfExportPreparation, UnsupportedShaderSubstitution and
                // VrmExpressionBindings write them.
                var notes = new[]
                {
                    ExporterLocalization.T(UniVrmOneClickExporter.StageOmittedWarning) + ": Expressions — reason",
                    ExporterLocalization.T(NdmfExportPreparation.LostGeneratedMorphs) + "Body/Smile",
                    ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Hair",
                    ExporterLocalization.T(UnsupportedShaderSubstitution.Failed) + "Skirt",
                    ExporterLocalization.Format(UniVrmOneClickExporter.LostMorphOmittedWarning, "Smile", "mouth_a"),
                    ExporterLocalization.Format(UniVrmOneClickExporter.LostExpressionOmittedWarning, "Smile"),
                    ExporterLocalization.T(UniVrmOneClickExporter.LostTrackingWarning) + " (detail)",
                    ExporterLocalization.Format(VrmExpressionBindings.DuplicateExpressionFormat, "Happy", 2),
                    ExporterLocalization.T(UniVrmOneClickExporter.MaterialApproximatedWarning) + "Body — " +
                        ExporterLocalization.T(UniVrmOneClickExporter.ApproximatedLilToonReason),
                };
                var others = new[]
                {
                    ExporterLocalization.T(UnsupportedShaderSubstitution.KeptForExpressions) + "Face",
                    ExporterLocalization.T(UniVrmOneClickExporter.MaterialApproximatedWarning) + "Face — " +
                        ExporterLocalization.T(UniVrmOneClickExporter.UnofficialLilToonReason),
                };
                Assert.That(ExportAppearanceReport.Changes(notes.Concat(others)), Is.EqualTo(notes), locale);
                // Chinese shares 省略 and 近似 with Japanese; in English and Korean only the translated
                // sentence can find a note.
                if (locale == "en" || locale == "ko")
                {
                    var words = new[] { "省略", "近似", "縮小", "調整して", "自動除外せず保持" };
                    foreach (var note in notes) Assert.That(words.Any(word => note.Contains(word)), Is.False, locale + ": " + note);
                }
                // A note that holds such a sentence after another one is listed in every language,
                // as the word in it lists it in Japanese.
                var quoting = ExporterLocalization.T(UnsupportedShaderSubstitution.KeptForExpressions) + ExporterLocalization.T(UnsupportedShaderSubstitution.Warning);
                Assert.That(ExportAppearanceReport.Changes(new[] { quoting }), Is.EqualTo(new[] { quoting }), locale);
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }

        // Any note of the locale table is found the same way, not only the ones listed by hand
        // before: of the lilToon structure notes, written in the language of the Editor, the one
        // that writes a material with MToon is listed and the one that leaves a property out is not.
        [TestCase("ja")]
        [TestCase("en")]
        [TestCase("ko")]
        [TestCase("zh-Hans")]
        [TestCase("zh-Hant")]
        public void StructureNotesInTheEditorLanguageAreListedByWhatTheyChange(string locale)
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
                string Note(LilToonStructureCheck.Kind kind) => LilToonStructureCheck.Warnings(
                    new[] { new LilToonStructureCheck.Difference(kind, "Body", "_Example") }, ExporterLocalization.T).First();
                var approximated = Note(LilToonStructureCheck.Kind.MissingProperty);
                var leftOut = Note(LilToonStructureCheck.Kind.UnknownProperty);
                if (locale == "en" || locale == "ko") Assert.That(approximated, Does.Not.Contain("近似"), "The note is translated.");
                Assert.That(ExportAppearanceReport.Changes(new[] { approximated, leftOut }), Is.EqualTo(new[] { approximated }), locale);
            }
            finally
            {
                localeField.SetValue(null, previousLocale);
                messagesField.SetValue(null, previousMessages);
            }
        }
    }
}
