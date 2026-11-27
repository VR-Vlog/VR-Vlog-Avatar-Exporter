using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportProgressTests
    {
        Func<string, float, bool> display;
        Material material;
        readonly List<(string Text, float Fraction)> shown = new List<(string, float)>();

        [SetUp]
        public void Record()
        {
            display = ExportProgress.Display;
            shown.Clear();
            ExportProgress.Display = (text, fraction) => { shown.Add((text, fraction)); return false; };
        }

        [TearDown]
        public void Restore()
        {
            ExportProgress.Display = display;
            Object.DestroyImmediate(material);
        }

        [Test]
        public void AStageShowsItsNameOnlyDuringAnExport()
        {
            var guard = new ExportStageGuard(true);
            guard.Run(ExportStage.PhysBone, () => { });
            Assert.That(shown, Is.Empty, "Outside an export nothing is drawn.");

            using (ExportProgress.Scope())
            {
                guard.Run(ExportStage.PhysBone, () => { });
                Assert.That(ExportProgress.Active, Is.True);
            }
            Assert.That(shown.Select(step => step.Text), Is.EqualTo(new[] { ExporterLocalization.T(ExportStageGuard.Label(ExportStage.PhysBone)) }));
            Assert.That(ExportProgress.Active, Is.False);
        }

        [Test]
        public void AnExportNamesItsStagesAndTheBarOnlyGrows()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = Avatar();
            var bytes = Export(fixture.Source);

            Assert.That(bytes, Is.Not.Null.And.Not.Empty);
            var texts = shown.Select(step => step.Text).ToList();
            Assert.That(texts, Does.Contain(ExporterLocalization.T(ExportStageGuard.Label(ExportStage.BuildProcess))),
                "The build of Modular Avatar and NDMF, the longest step on a large avatar, must be named.");
            Assert.That(texts, Does.Contain(ExporterLocalization.T("VRM変換")), "The steps without a stage are named as the report names them.");
            var fractions = shown.Select(step => step.Fraction).ToList();
            Assert.That(fractions, Is.Ordered, "The bar must not move back.");
            Assert.That(fractions.Last(), Is.InRange(0f, 1f));
            Assert.That(ExportProgress.Active, Is.False, "The export clears its bar.");
        }

        [Test]
        public void CancelStopsTheExportAndRemovesItsCopy()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = Avatar();
            var build = ExporterLocalization.T(ExportStageGuard.Label(ExportStage.BuildProcess));
            ExportProgress.Display = (text, fraction) => { shown.Add((text, fraction)); return text == build; };
            var roots = SceneManager.GetActiveScene().rootCount;
            var report = new ExportRecoveryReport { ContinueOnStageFailure = true };

            Assert.Throws<OperationCanceledException>(() => Export(fixture.Source, report));
            Assert.That(shown.Last().Text, Is.EqualTo(build), "Nothing after the canceled stage runs.");
            Assert.That(report.Fallbacks.Results, Is.Empty, "A cancel is not a failed stage left out.");
            Assert.That(SceneManager.GetActiveScene().rootCount, Is.EqualTo(roots), "The export copy must be removed.");
            Assert.That(ExportProgress.Active, Is.False);

            shown.Clear();
            ExportProgress.Display = (text, fraction) => { shown.Add((text, fraction)); return false; };
            Assert.That(Export(fixture.Source), Is.Not.Null.And.Not.Empty, "The next export starts without the cancel.");
        }

        [Test]
        public void AFinishedExportKeepsItsFileWhenCancelIsPressedLate()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = Avatar();
            var done = ExporterLocalization.T("完了");
            ExportProgress.Display = (text, fraction) => { shown.Add((text, fraction)); return text == done; };

            Assert.That(Export(fixture.Source), Is.Not.Null.And.Not.Empty, "A cancel after the file is made must not throw the file away.");
            Assert.That(shown.Select(step => step.Text), Has.None.EqualTo(done));
        }

        [Test]
        public void ACanceledSaveWritesNoFileAndOpensNoRecovery()
        {
            NoGraphicsDevice.AllowErrorLogs();
            using var fixture = Avatar();
            ExportProgress.Display = (text, fraction) => true;
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-progress-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var completed = false;
                var recovery = ExportEntry.ExportAndSaveNormally(fixture.Source, path,
                    (options, report, warnings) => Export(fixture.Source, report), (bytes, warnings) => completed = true);

                Assert.That(recovery, Is.Null, "A cancel returns like the user's own cancel: no recovery window.");
                Assert.That(completed, Is.False);
                Assert.That(File.Exists(path), Is.False);
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        // The fixture with lilToon materials, as an avatar the exporter accepts.
        AttachmentConnectionTests.Fixture Avatar()
        {
            var fixture = new AttachmentConnectionTests.Fixture();
            material = new Material(Shader.Find("lilToon")) { name = "Progress" };
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
            return fixture;
        }

        static byte[] Export(GameObject source, ExportRecoveryReport report = null) =>
            UniVrmOneClickExporter.Export(source, "Progress", "Tests", warnings: new List<string>(),
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);
    }
}
