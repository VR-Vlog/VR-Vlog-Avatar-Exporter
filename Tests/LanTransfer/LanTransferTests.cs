using System;
using System.IO;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // The old LAN transfer is retired (Enabled is false). Only the guards that keep it
    // off remain; the server and protocol tests were removed with the feature.
    public sealed class LanTransferTests
    {
        [Test]
        public void DisabledProductionTransferRejectsBeforeOpeningAMissingSnapshot()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-disabled-" + Guid.NewGuid().ToString("N"), "missing.vrm");
            Assert.That(LanTransferAvailability.Enabled, Is.False);
            var exception = Assert.Throws<NotSupportedException>(() => LanVrmTransferServer.Start(path, "fixture.vrm",
                System.Net.IPAddress.Parse("192.168.1.2"), TimeSpan.FromMinutes(1)));
            Assert.That(exception.Message, Is.EqualTo(LanTransferAvailability.DisabledMessage));
            Assert.That(Directory.Exists(Path.GetDirectoryName(path)), Is.False);
        }

        [TestCase(false)][TestCase(true)]
        public void DisabledProductionTransferKeepsTheCallerSnapshotAndOwnership(bool supplyOpenSnapshot)
        {
            var data = FixtureBytes();
            var path = NewSnapshot(data);
            FileStream snapshot = null;
            try
            {
                if (supplyOpenSnapshot) snapshot = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Assert.Throws<NotSupportedException>(() => LanVrmTransferServer.Start(path, "fixture.vrm",
                    System.Net.IPAddress.Parse("192.168.1.2"), TimeSpan.FromMinutes(1), snapshot));
                if (snapshot != null) Assert.That(snapshot.CanRead, Is.True, "A disabled operation never takes ownership of the caller's file.");
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(data));
            }
            finally { snapshot?.Dispose(); if (File.Exists(path)) File.Delete(path); }
        }

        private static byte[] FixtureBytes() { var bytes = new byte[128 * 1024]; for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i % 251); return bytes; }
        private static string NewSnapshot(byte[] data) { var path = Path.Combine(Path.GetTempPath(), "vrvlog-lan-test-" + Guid.NewGuid().ToString("N") + ".vrm"); File.WriteAllBytes(path, data); return path; }
    }
}
