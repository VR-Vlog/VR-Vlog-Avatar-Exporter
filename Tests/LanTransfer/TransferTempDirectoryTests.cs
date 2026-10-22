using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    public sealed class TransferTempDirectoryTests
    {
        string scratch;
        long requiredFreeBytes;

        [SetUp]
        public void SetUp()
        {
            scratch = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-temp-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            requiredFreeBytes = TransferTempDirectory.RequiredFreeBytes;
        }

        [TearDown]
        public void TearDown()
        {
            TransferTempDirectory.TestRoots = null;
            TransferTempDirectory.RequiredFreeBytes = requiredFreeBytes;
            if (Directory.Exists(scratch)) Directory.Delete(scratch, true);
        }

        // A root that is a file cannot hold the transfer folder.
        string Unusable()
        {
            var file = Path.Combine(scratch, "not-a-folder-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(file, new byte[] { 1 });
            return file;
        }

        string Usable(string name)
        {
            var directory = Path.Combine(scratch, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        [Test]
        public void AnUnusableFolderIsSkippedForTheNextOne()
        {
            var good = Usable("second");
            TransferTempDirectory.TestRoots = () => new[] { null, "", "bad|<name>", Unusable(), good };
            var path = TransferTempDirectory.NewFile("VRVlogCloudTransfers", ".vrm");
            Assert.That(Path.GetDirectoryName(path), Is.EqualTo(Path.Combine(good, "VRVlogCloudTransfers")));
            Assert.That(Path.GetFileName(path), Does.Match("^[0-9a-f]{32}\\.vrm$"));
            Assert.That(File.Exists(path), Is.False, "Only the name is chosen; the caller writes the file.");
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(path)), Is.Empty, "The write test leaves nothing behind.");
        }

        [Test]
        public void TheTransferGivesUpOnlyWhenNoFolderWorks()
        {
            TransferTempDirectory.TestRoots = () => new[] { Unusable(), Unusable() };
            var failure = Assert.Throws<InvalidOperationException>(() => TransferTempDirectory.NewFile("VRVlogCloudTransfers", ".vrm"));
            Assert.That(failure.Message, Is.EqualTo(TransferTempDirectory.NoWritableFolder));
            Assert.That(failure.Data[TransferTempDirectory.ShownAsIs], Is.EqualTo(true));
        }

        [Test]
        public void AFolderWithoutRoomIsUsedOnlyWhenNothingElseWorks()
        {
            var first = Usable("first");
            TransferTempDirectory.RequiredFreeBytes = long.MaxValue;
            TransferTempDirectory.TestRoots = () => new[] { Unusable(), first, Usable("second") };
            Assert.That(Path.GetDirectoryName(TransferTempDirectory.NewFile("VRVlogLanTransfers", ".vrm")),
                Is.EqualTo(Path.Combine(first, "VRVlogLanTransfers")));
        }

        [Test]
        public void OnlyStaleCopiesNamedByTheExporterAreSwept()
        {
            var directory = Path.Combine(Usable("sweep"), "VRVlogEncryptedTransfers");
            Directory.CreateDirectory(directory);
            string Make(string name, TimeSpan age)
            {
                var path = Path.Combine(directory, name);
                File.WriteAllBytes(path, new byte[] { 1 });
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow - age);
                return path;
            }
            var stale = Make(Guid.NewGuid().ToString("N") + ".bin", TimeSpan.FromDays(2));
            var staleVrm = Make(Guid.NewGuid().ToString("N") + ".vrm", TimeSpan.FromDays(2));
            var fresh = Make(Guid.NewGuid().ToString("N") + ".bin", TimeSpan.FromMinutes(5));
            var foreign = Make("keep-me.vrm", TimeSpan.FromDays(30));
            TransferTempDirectory.TestRoots = () => new[] { Path.GetDirectoryName(directory) };
            TransferTempDirectory.NewFile("VRVlogEncryptedTransfers", ".bin");
            Assert.That(File.Exists(stale) || File.Exists(staleVrm), Is.False);
            Assert.That(File.Exists(fresh) && File.Exists(foreign), Is.True);
        }

        [Test]
        public void ASavedVrmCopyUsesTheNextFolderWhenTheFirstFails()
        {
            var original = Path.Combine(scratch, "saved.vrm");
            var bytes = Enumerable.Range(0, 4096).Select(index => (byte)index).ToArray();
            File.WriteAllBytes(original, bytes);
            var good = Usable("copy");
            TransferTempDirectory.TestRoots = () => new[] { Unusable(), good };
            string copied;
            using (var snapshot = CloudDevelopmentSnapshot.CopySavedVrm(original))
            {
                copied = snapshot.SnapshotPath;
                Assert.That(copied, Does.StartWith(Path.Combine(good, "VRVlogCloudTransfers")));
                Assert.That(File.ReadAllBytes(copied), Is.EqualTo(bytes));
            }
            Assert.That(File.Exists(copied), Is.False);
            Assert.That(File.ReadAllBytes(original), Is.EqualTo(bytes));
        }

#if !VRVLOG_LAN_TRANSFER_CLI
        // The app transfer window deletes the VRM it exported for the transfer
        // when the export is canceled or fails, and reports a missing folder
        // instead of throwing.
        [TestCase("canceled")][TestCase("failed")][TestCase("thrown")][TestCase("no folder")]
        public void TheAppTransferDeletesItsCopyWhenNothingIsUploaded(string outcome)
        {
            const BindingFlags field = BindingFlags.NonPublic | BindingFlags.Instance;
            var good = Usable("app");
            TransferTempDirectory.TestRoots = outcome == "no folder" ? (Func<System.Collections.Generic.IEnumerable<string>>)(() => new[] { Unusable() }) : () => new[] { good };
            var avatar = new UnityEngine.GameObject("vrvlog-app-transfer-temp", typeof(UnityEngine.Animator));
            // Not shown: the window logic runs without an OS window, which batch runs may not create.
            var window = new AppTransferPanel();
            string written = null;
            try
            {
                var entryType = typeof(AppTransferPanel).GetNestedType("ExportEntry", BindingFlags.NonPublic);
                var entry = Activator.CreateInstance(entryType, true);
                Func<UnityEngine.GameObject, string, string, Action, string> export = (target, name, destination, saved) =>
                {
                    written = destination;
                    File.WriteAllBytes(destination, new byte[] { 1, 2, 3 });
                    if (outcome == "thrown") throw new IOException("fixture");
                    return outcome == "canceled" ? null : "fixture failure";
                };
                entryType.GetField("Export", field | BindingFlags.Public).SetValue(entry, export);
                entryType.GetField("SelectAvatar", field | BindingFlags.Public).SetValue(entry, (Action<UnityEngine.GameObject>)(_ => { }));
                entryType.GetField("HasAuthor", field | BindingFlags.Public).SetValue(entry, (Func<bool>)(() => true));
                entryType.GetField("TermsKey", field | BindingFlags.Public).SetValue(entry, (Func<string>)(() => "fixture terms"));
                typeof(AppTransferPanel).GetField("exporter", field).SetValue(window, entry);
                typeof(AppTransferPanel).GetField("exporterResolved", field).SetValue(window, true);
                typeof(AppTransferPanel).GetField("avatar", field).SetValue(window, avatar);
                typeof(AppTransferPanel).GetField("avatarName", field).SetValue(window, "SampleAvatar");
                var flow = new AppTransferFlow();
                typeof(AppTransferPanel).GetField("flow", field).SetValue(window, flow);
                flow.BeginExport();
                Assert.DoesNotThrow(() => typeof(AppTransferPanel).GetMethod("RunExport", field).Invoke(window, null));
                if (outcome == "no folder") Assert.That(written, Is.Null, "Nothing is exported without a folder for it.");
                else
                {
                    Assert.That(written, Does.StartWith(Path.Combine(good, "VRVlogCloudTransfers")));
                    Assert.That(File.Exists(written), Is.False);
                }
                Assert.That(flow.View, Is.EqualTo(outcome == "canceled" ? AppTransferView.Form : AppTransferView.Failed));
                if (outcome == "no folder") Assert.That(flow.FailureText(text => text), Is.EqualTo(TransferTempDirectory.NoWritableFolder));
            }
            finally
            {
                typeof(AppTransferPanel).GetField("flow", field).SetValue(window, null);
                UnityEngine.Object.DestroyImmediate(avatar);
            }
        }
#endif
    }
}
