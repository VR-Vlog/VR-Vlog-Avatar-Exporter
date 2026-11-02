using System.Globalization;
using System.Threading;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    public sealed partial class CloudTransferTests
    {
        [Test]
        public void UploadSizeUsesTheUnitOfTheTotalForBothNumbers()
        {
            Assert.That(TransferSizeText.Progress(25165824, 26195432), Is.EqualTo("24.0 / 25.0 MB"));
            Assert.That(TransferSizeText.Progress(0, 26195432), Is.EqualTo("0.0 / 25.0 MB"));
            // A small part of a large total stays in the total's unit.
            Assert.That(TransferSizeText.Progress(512, 26195432), Is.EqualTo("0.0 / 25.0 MB"));
            Assert.That(TransferSizeText.Progress(1536, 2048), Is.EqualTo("1.5 / 2.0 KB"));
            Assert.That(TransferSizeText.Progress(1073741824, 3221225472), Is.EqualTo("1.0 / 3.0 GB"));
        }

        [Test]
        public void UploadSizeSwitchesUnitWhereTheRoundedNumberReaches1024()
        {
            Assert.That(TransferSizeText.Progress(0, 0), Is.EqualTo("0 / 0 B"));
            Assert.That(TransferSizeText.Progress(512, 1023), Is.EqualTo("512 / 1023 B"));
            Assert.That(TransferSizeText.Progress(512, 1024), Is.EqualTo("0.5 / 1.0 KB"));
            Assert.That(TransferSizeText.Progress(0, 1048524), Is.EqualTo("0.0 / 1023.9 KB"));
            Assert.That(TransferSizeText.Progress(0, 1048570), Is.EqualTo("0.0 / 1.0 MB"));
            // There is no unit above GB.
            Assert.That(TransferSizeText.Progress(0, 2199023255552), Is.EqualTo("0.0 / 2048.0 GB"));
        }

        [Test]
        public void UploadSizeDoesNotFollowTheSystemNumberFormat()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                Assert.That(TransferSizeText.Progress(25165824, 26195432), Is.EqualTo("24.0 / 25.0 MB"));
            }
            finally { Thread.CurrentThread.CurrentCulture = previous; }
        }

        [Test]
        public void UploadSizeTreatsANegativeCountAsZero()
        {
            Assert.That(TransferSizeText.Progress(-1, 2048), Is.EqualTo("0.0 / 2.0 KB"));
        }
    }
}
