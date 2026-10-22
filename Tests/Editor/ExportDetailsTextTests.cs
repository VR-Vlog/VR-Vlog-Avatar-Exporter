using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public class ExportDetailsTextTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "VRVlog-details-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(directory, true);

        private static Exception Failure()
        {
            try { throw new IOException("合成エラー /synthetic/project/avatar.png"); }
            catch (IOException inner) { return new InvalidOperationException("書き出し失敗", inner); }
        }

        [Test]
        public void AllDiagnosticFieldsAndTheExactNestedExceptionSurviveLargeReports()
        {
            var error = Failure();
            var issues = Enumerable.Range(0, 120).Select(index => new ExportRecoveryDiagnostic
            {
                Stage = "工程" + index, Target = "対象" + index,
                Reason = "原因" + index + new string('あ', 2048),
                Remedy = "対処" + index, LostEffect = "影響" + index
            }).ToArray();
            var text = ExportDetailsText.Failure(error.Message, error.ToString(), issues);
            foreach (var issue in issues)
                foreach (var field in new[] { issue.Stage, issue.Target, issue.Reason, issue.Remedy, issue.LostEffect })
                    Assert.That(text, Does.Contain(field));
            Assert.That(text, Does.EndWith(error.ToString()));
            Assert.That(text, Does.Contain(nameof(IOException)));
            Assert.That(text, Does.Contain(nameof(Failure)));
        }

        [Test]
        public void NativeClipboardAndUtf8FileContainTheSameCompleteUnicodeText()
        {
            var details = new string('文', 180000) + "\n最後のエラー 👁️\r\n最終行\t保存終了";
            var previous = EditorGUIUtility.systemCopyBuffer;
            try
            {
                ExportDetailsText.Copy(details);
                Assert.That(EditorGUIUtility.systemCopyBuffer, Is.EqualTo(details));
                var path = Path.Combine(directory, "全文.txt");
                Assert.That(ExportDetailsText.Save(details, () => path, out var error), Is.True, error);
                Assert.That(File.ReadAllText(path, Encoding.UTF8), Is.EqualTo(EditorGUIUtility.systemCopyBuffer));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new UTF8Encoding(false).GetBytes(details)));
            }
            finally { EditorGUIUtility.systemCopyBuffer = previous; }
        }

        [Test]
        public void CancelDoesNotTouchAnExistingFileOrLeaveTemporaryFiles()
        {
            var path = Path.Combine(directory, "existing.txt");
            File.WriteAllText(path, "元の内容");
            Assert.That(ExportDetailsText.Save("新しい内容", () => "", out var error), Is.False);
            Assert.That(error, Is.Null);
            Assert.That(File.ReadAllText(path), Is.EqualTo("元の内容"));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void ConfirmedExistingFileIsReplacedWithCompleteTextWithoutTemporaryFiles()
        {
            var path = Path.Combine(directory, "existing.txt");
            File.WriteAllText(path, "元の内容");
            Assert.That(ExportDetailsText.Save("新しい全文\n終わり", () => path, out var error), Is.True, error);
            Assert.That(File.ReadAllText(path), Is.EqualTo("新しい全文\n終わり"));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void WriteFailureIsReportedAndPreservesOtherFiles()
        {
            var existing = Path.Combine(directory, "existing.txt");
            File.WriteAllText(existing, "保持する内容");
            var path = Path.Combine(directory, "missing", "details.txt");
            Assert.That(ExportDetailsText.Save("全文", () => path, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(existing), Is.EqualTo("保持する内容"));
            Assert.That(Directory.GetFiles(directory), Has.Length.EqualTo(1));
        }

        [Test]
        public void FailedReplacementOfDirectoryCleansOnlyItsOwnTemporaryFile()
        {
            var target = Path.Combine(directory, "folder.txt");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "保持");
            Assert.That(ExportDetailsText.Save("全文", () => target, out var error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(File.ReadAllText(Path.Combine(target, "keep.txt")), Is.EqualTo("保持"));
            Assert.That(Directory.GetFiles(directory), Is.Empty);
        }

        // The window headers, the full text and the shared diagnostic all name the version in
        // package.json, which the release bump changes, so a report says which version it came from.
        [Test]
        public void HeaderFailureTextAndSharedDiagnosticNameThePackageVersion()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ExporterWindowHeader).Assembly);
            Assert.That(package, Is.Not.Null, "The tests run with the exporter installed as a package.");
            var json = File.ReadAllText(Path.Combine(package.resolvedPath, "package.json"));
            var version = System.Text.RegularExpressions.Regex.Match(json, "\"version\"\\s*:\\s*\"([^\"]+)\"").Groups[1].Value;
            Assert.That(version, Is.Not.Empty);
            Assert.That(ExporterWindowHeader.Version, Is.EqualTo("v" + version));
            Assert.That(LilToonExporterWindow.PackageVersion(), Is.EqualTo(version));

            var error = Failure();
            var text = ExportDetailsText.Failure(error.Message, error.ToString(), Array.Empty<ExportRecoveryDiagnostic>(), version);
            Assert.That(text, Does.StartWith("VR Vlog Exporter " + version + "\n").Or.StartWith("VR Vlog Exporter " + version + "\r\n"));
            Assert.That(text, Does.EndWith(error.ToString()));
            Assert.That(ExportDetailsText.Failure(error.Message, error.ToString(), null), Does.Not.Contain("VR Vlog Exporter"));

            var support = ExportRecoveryReport.FromException(null, error).BuildSupportText(version);
            Assert.That(support, Does.Contain("Exporter: " + version));
            Assert.That(support, Does.StartWith("VR Vlog exporter recovery report v1\n"));
        }
    }
}
