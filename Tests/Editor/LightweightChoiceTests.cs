using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 軽量化 as the board and the quick transfer share it: the avatar's saved
    // level, read and written through LightweightChoice.
    public sealed class LightweightChoiceTests
    {
        string storePath, previousPath;

        [SetUp]
        public void UseTemporaryStore()
        {
            previousPath = ExportSettingsStore.PathOverride;
            storePath = Path.Combine(Path.GetTempPath(), "vrvlog-lightweight-choice-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = storePath;
        }

        [TearDown]
        public void RestoreStore()
        {
            ExportSettingsStore.PathOverride = previousPath;
            ExportSettingsStore.Problem = null;
            if (File.Exists(storePath)) File.Delete(storePath);
        }

        [Test]
        public void AChosenLevelIsSavedForTheAvatarAndKeepsTheOtherChoices()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            ExportSettingsStore.Save(fixture.Source, new ExportSettings { Gaze = new GazeRangeOptions { Up = 25 } });
            Assert.That(LightweightChoice.Saved(fixture.Source), Is.EqualTo(LightweightLevel.Standard));
            foreach (var level in new[] { LightweightLevel.None, LightweightLevel.Max, LightweightLevel.Standard })
            {
                LightweightChoice.Choose(fixture.Source, level);
                Assert.That(LightweightChoice.Saved(fixture.Source), Is.EqualTo(level));
                Assert.That(ExportSettingsStore.Load(fixture.Source).Lightweight, Is.EqualTo(level));
                Assert.That(ExportSettingsStore.Load(fixture.Source).Gaze.Up, Is.EqualTo(25f), "Only the level changes.");
            }
            Assert.That(Regex.Replace(File.ReadAllText(storePath), @"\s", ""), Does.Contain("\"Lightweight\":" + (int)LightweightLevel.Standard));
        }

        [Test]
        public void TheChosenLevelOrAnUndefinedOneSavesNothing()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            LightweightChoice.Choose(fixture.Source, LightweightLevel.None);
            var revision = ExportSettingsStore.Revision;
            LightweightChoice.Choose(fixture.Source, LightweightLevel.None);
            LightweightChoice.Choose(fixture.Source, (LightweightLevel)7);
            LightweightChoice.Choose(null, LightweightLevel.Max);
            Assert.That(ExportSettingsStore.Revision, Is.EqualTo(revision));
            Assert.That(LightweightChoice.Saved(fixture.Source), Is.EqualTo(LightweightLevel.None));
            Assert.That(LightweightChoice.Saved(null), Is.EqualTo(LightweightLevel.Standard));
        }

        // The quick transfer reads the level again after a save from either screen.
        [Test]
        public void TheQuickTransferNoteFollowsTheSavedLevel()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var transfer = new AppTransferExport();
            transfer.SelectAvatar(fixture.Source);
            Assert.That(transfer.LightweightNote(null), Is.Empty);
            foreach (var level in new[] { LightweightLevel.Standard, LightweightLevel.None, LightweightLevel.Max })
            {
                ExportSettingsStore.Save(fixture.Source, new ExportSettings { Lightweight = level });
                Assert.That(transfer.LightweightNote(fixture.Source), Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightNote(level, false))));
            }
        }

        // Three buttons in the order of the levels, each with its level's note as the tooltip.
        [Test]
        public void TheButtonsAreTheThreeLevelsWithTheirNotes()
        {
            foreach (var optimizer in new[] { false, true })
            {
                var buttons = LightweightChoice.Buttons(optimizer, new GUIStyle());
                Assert.That(buttons.Length, Is.EqualTo(3));
                for (var i = 0; i < buttons.Length; i++)
                {
                    Assert.That(buttons[i].text, Is.EqualTo(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightLevelNames[i])));
                    Assert.That(buttons[i].tooltip, Does.StartWith(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightNote((LightweightLevel)i, optimizer))));
                }
                Assert.That(buttons[2].tooltip.Contains(ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightMaxTooltip)), Is.EqualTo(optimizer));
            }
            Assert.That(LightweightChoice.Width, Is.EqualTo(240f));
        }
    }
}
