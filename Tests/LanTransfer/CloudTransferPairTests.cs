using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // The normal and the lightweight file of one avatar sent as two transfers behind one link.
    public sealed partial class CloudTransferTests
    {
        [Test]
        public async Task BothFilesAreUploadedFirstAndThenPublishedBackToBack()
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                await session.UploadAsync();
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Ready));
                Assert.That(pair.Transport.Log, Is.EqualTo(new[]
                {
                    "capabilities", "create 0", "part 0/1", "part 0/2", "create 1", "part 1/1", "publish 0", "publish 1",
                }));
                var link = PairLinkFields(session.ImportLink);
                Assert.That(link["v"], Is.EqualTo("4"));
                Assert.That(link["id"], Is.EqualTo(pair.Transport.Transfers[0].Id));
                Assert.That(link["lid"], Is.EqualTo(pair.Transport.Transfers[1].Id));
                Assert.That(link["token"], Is.EqualTo(pair.Transport.Transfers[0].ReadToken));
                Assert.That(link["ltoken"], Is.EqualTo(pair.Transport.Transfers[1].ReadToken));
                Assert.That(link["size"], Is.EqualTo(pair.Normal.Size.ToString()));
                Assert.That(link["sha256"], Is.EqualTo(pair.Normal.FileHash));
                Assert.That(link["lsize"], Is.EqualTo(pair.Light.Size.ToString()));
                Assert.That(link["lsha256"], Is.EqualTo(pair.Light.FileHash));
                // Each file has its own key, and neither upload token is in the link.
                Assert.That(link["key"] == link["lkey"], Is.False);
                Assert.That(session.ImportLink.Contains(pair.Transport.Transfers[0].UploadToken) || session.ImportLink.Contains(pair.Transport.Transfers[1].UploadToken), Is.False);
                // The in-app scanner's QR stays the version 3 normal file.
                var qr = CloudTransferProtocol.Fields.Read(session.Qr.Substring(CloudTransferProtocol.QrPrefix.Length));
                Assert.That(qr.Number("v"), Is.EqualTo(3L));
                Assert.That(qr.Text("id"), Is.EqualTo(pair.Transport.Transfers[0].Id));
                Assert.That(qr.Text("key"), Is.EqualTo(link["key"]));
                // The progress counts both files.
                var wire = CloudEncryptedSnapshot.WireSize(pair.Normal.Size) + CloudEncryptedSnapshot.WireSize(pair.Light.Size);
                Assert.That(session.UploadSize, Is.EqualTo(wire));
                Assert.That(session.Transferred, Is.EqualTo(wire));
                Assert.That(session.LightFailed, Is.False);
                Assert.That(pair.Transport.Cancelled.Count, Is.EqualTo(0));
            }
        }

        [TestCase(120, 170)]
        [TestCase(170, 120)]
        public async Task TheLinkEndsWithTheEarlierOfTheTwoReceipts(int normalLifetime, int lightLifetime)
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                pair.Transport.PublicationLifetimes = new[] { normalLifetime, lightLifetime };
                await session.UploadAsync();
                var link = PairLinkFields(session.ImportLink);
                var earlier = Math.Min(pair.Transport.Transfers[0].ReceiveExpiry, pair.Transport.Transfers[1].ReceiveExpiry);
                Assert.That(link["exp"], Is.EqualTo(earlier.ToString()));
                Assert.That(session.ExpiresAt, Is.EqualTo(earlier));
            }
        }

        [TestCase("light-create")]
        [TestCase("light-part")]
        [TestCase("light-publish")]
        [TestCase("light-publish-mismatch")]
        public async Task ALightweightFailureSendsTheNormalFileWithAVersion3Link(string failure)
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                pair.Transport.Failure = failure;
                await session.UploadAsync();
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Ready));
                Assert.That(session.LightFailed, Is.True);
                var link = ImportLinkFields(session.ImportLink, out _);
                Assert.That(link["v"], Is.EqualTo("3"));
                Assert.That(link["id"], Is.EqualTo(pair.Transport.Transfers[0].Id));
                Assert.That(link["size"], Is.EqualTo(pair.Normal.Size.ToString()));
                // The lightweight transfer that was created is removed at once.
                Assert.That(pair.Transport.Cancelled, Is.EqualTo(failure == "light-create" ? new string[0] : new[] { pair.Transport.Transfers[1].Id }));
                Assert.That(session.UploadSize, Is.EqualTo(CloudEncryptedSnapshot.WireSize(pair.Normal.Size)));
                Assert.That(session.Transferred, Is.EqualTo(session.UploadSize));
            }
        }

        [Test]
        public async Task AServiceWithoutTheLongerUploadSendsOnlyTheNormalFile()
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                pair.Transport.ExtendedUploads = false;
                await session.UploadAsync();
                Assert.That(session.LightFailed, Is.True);
                Assert.That(ImportLinkFields(session.ImportLink, out _)["v"], Is.EqualTo("3"));
                Assert.That(pair.Transport.Transfers.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ALightweightUploadThatWouldDelayTheNormalPublicationIsDropped()
        {
            using (var pair = new PairFixture())
            {
                var current = 1795000000L;
                pair.Transport.Now = () => current;
                // The normal file takes all but the reserve of its upload lifetime.
                pair.Transport.BeforeFirstLightCreate = () => current += CloudTransferProtocol.UploadLifetimeSeconds - CloudVrmTransferSession.LightPublicationReserveSeconds;
                using (var session = pair.Session(() => current))
                {
                    await session.UploadAsync();
                    Assert.That(session.State, Is.EqualTo(CloudTransferState.Ready));
                    Assert.That(session.LightFailed, Is.True);
                    Assert.That(ImportLinkFields(session.ImportLink, out _)["v"], Is.EqualTo("3"));
                    Assert.That(pair.Transport.Log.Count(entry => entry.StartsWith("publish", StringComparison.Ordinal)), Is.EqualTo(1));
                }
            }
        }

        [Test]
        public async Task ANormalFailureStopsAndRemovesBothTransfers()
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                pair.Transport.Failure = "normal-publish";
                Assert.That(await Fails(session.UploadAsync()), Is.True);
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Failed));
                Assert.That(session.ImportLink == null && session.Qr == null, Is.True);
                Assert.That(pair.Transport.Cancelled.Distinct().OrderBy(id => id), Is.EqualTo(pair.Transport.Transfers.Select(transfer => transfer.Id).OrderBy(id => id)));
            }
        }

        [Test]
        public async Task CancelingStopsBothTransfers()
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                await session.UploadAsync();
                await session.CancelAsync();
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Canceled));
                Assert.That(pair.Transport.Cancelled.Distinct().OrderBy(id => id), Is.EqualTo(pair.Transport.Transfers.Select(transfer => transfer.Id).OrderBy(id => id)));
                Assert.That(session.ImportLink == null, Is.True);
            }
        }

        [Test]
        public async Task CancelingDuringTheLightweightUploadStopsBothTransfers()
        {
            using (var pair = new PairFixture())
            using (var session = pair.Session())
            {
                var release = new TaskCompletionSource<bool>();
                pair.Transport.LightPartRelease = release;
                var upload = session.UploadAsync();
                await pair.Transport.LightPartObserved.Task;
                await session.CancelAsync();
                release.TrySetResult(true);
                Assert.That(await Fails(upload), Is.True);
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Canceled));
                Assert.That(session.LightFailed, Is.False);
                Assert.That(pair.Transport.Cancelled.Distinct().OrderBy(id => id), Is.EqualTo(pair.Transport.Transfers.Select(transfer => transfer.Id).OrderBy(id => id)));
                Assert.That(pair.Transport.Log.Any(entry => entry.StartsWith("publish", StringComparison.Ordinal)), Is.False);
            }
        }

        [Test]
        public async Task AfterThePhoneHasTheNormalFileClosingLeavesTheLightweightOne()
        {
            using (var pair = new PairFixture())
            {
                using (var session = pair.Session())
                {
                    await session.UploadAsync();
                    pair.Transport.RemoteState = "completed";
                    await session.RefreshAsync();
                    Assert.That(session.State, Is.EqualTo(CloudTransferState.Completed));
                }
                Assert.That(pair.Transport.Cancelled.Contains(pair.Transport.Transfers[1].Id), Is.False);
                // Only the normal transfer is asked for its state.
                Assert.That(pair.Transport.Log.Where(entry => entry.StartsWith("state", StringComparison.Ordinal)), Is.EqualTo(new[] { "state 0" }));
            }
        }

        [Test]
        public async Task WithoutALightweightSourceTheSessionSendsOneFileAsBefore()
        {
            using (var pair = new PairFixture())
            using (var session = new CloudVrmTransferSession(pair.Normal, pair.Transport))
            {
                await session.UploadAsync();
                Assert.That(session.LightRequested || session.LightFailed, Is.False);
                Assert.That(pair.Transport.Log, Is.EqualTo(new[] { "capabilities", "create 0", "part 0/1", "part 0/2", "publish 0" }));
                Assert.That(ImportLinkFields(session.ImportLink, out _)["v"], Is.EqualTo("3"));
            }
        }

        [Test]
        public async Task AFlowAskedForTheLightweightFileShowsTheVersion4LinkAndDeletesBothFilesWhenItStops()
        {
            using (var run = new PairFlowRun())
            {
                run.Flow.BeginExport();
                run.Flow.Upload(run.NormalPath, "サンプル.vrm", true, run.LightPath);
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                Assert.That(PairLinkFields(run.Flow.Link)["v"], Is.EqualTo("4"));
                Assert.That(run.Flow.LightWanted, Is.True);
                Assert.That(run.Flow.LightFailed, Is.False);
                Assert.That(run.Transport.Transfers.Count, Is.EqualTo(2));
                run.Flow.Stop();
                Assert.That(File.Exists(run.NormalPath) || File.Exists(run.LightPath), Is.False);
                Assert.That(run.Flow.LightWanted, Is.False);
            }
        }

        [Test]
        public async Task AFlowWhoseLightweightFileWasNotWrittenSendsTheNormalOneAndSaysSo()
        {
            using (var run = new PairFlowRun())
            {
                run.Flow.BeginExport();
                run.Flow.Upload(run.NormalPath, "サンプル.vrm", true, null);
                Assert.That(run.Flow.LightFailed, Is.True);
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                Assert.That(ImportLinkFields(run.Flow.Link, out _)["v"], Is.EqualTo("3"));
                Assert.That(run.Transport.Transfers.Count, Is.EqualTo(1));
                Assert.That(run.Flow.LightFailed, Is.True);
            }
        }

        [Test]
        public async Task AFlowWhoseLightweightUploadFailsSendsTheNormalOneAndSaysSo()
        {
            using (var run = new PairFlowRun { Failure = "light-part" })
            {
                run.Flow.BeginExport();
                run.Flow.Upload(run.NormalPath, "サンプル.vrm", true, run.LightPath);
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                Assert.That(ImportLinkFields(run.Flow.Link, out _)["v"], Is.EqualTo("3"));
                Assert.That(run.Flow.LightFailed, Is.True);
            }
        }

        [Test]
        public async Task AFlowNotAskedForTheLightweightFileDeletesItAndSendsOne()
        {
            using (var run = new PairFlowRun())
            {
                run.Flow.BeginExport();
                run.Flow.Upload(run.NormalPath, "サンプル.vrm", false, run.LightPath);
                Assert.That(File.Exists(run.LightPath), Is.False);
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(ImportLinkFields(run.Flow.Link, out _)["v"], Is.EqualTo("3"));
                Assert.That(run.Flow.LightWanted || run.Flow.LightFailed, Is.False);
            }
        }

        [Test]
        public void Version4PlaceholderIsNeverShorterThanARealLinkAndHoldsNoSecret()
        {
            var placeholder = AppTransferFlow.PlaceholderLink("サンプル.vrm", true);
            Assert.That(PairLink("サンプル.vrm").Length, Is.LessThanOrEqualTo(placeholder.Length));
            Assert.That(AppTransferFlow.PlaceholderLink("サンプル.vrm").Length < placeholder.Length, Is.True);
            var fields = PairLinkFields(placeholder);
            Assert.That(fields["v"], Is.EqualTo("4"));
            Assert.That(fields["lid"], Is.EqualTo(new string('1', 32)));
            Assert.That(fields["ltoken"], Is.EqualTo(new string('A', 43)));
            Assert.That(fields["lkey"], Is.EqualTo(new string('A', 86)));
            Assert.That(fields["lsize"], Is.EqualTo(CloudTransferProtocol.MaximumSize.ToString()));
        }

        // The version 4 link is longer, so fewer characters of a name fit one QR, marked or not.
        [Test]
        public void LongNamesAreCheckedAgainstTheVersion4LinkWhenTheLightweightFileGoes()
        {
            Assert.That(AppTransferFlow.NameFits("SampleAvatar_v1.2", true), Is.True);
            Assert.That(AppTransferFlow.NameFits(new string('a', 300), true), Is.True);
            Assert.That(AppTransferFlow.NameFits(new string('あ', 300), true), Is.False);
            var longest = "";
            while (AppTransferFlow.NameFits(longest + "あ", true)) longest += "あ";
            Assert.That(longest.Length >= 150, Is.True);
            Assert.That(AppTransferFlow.NameFits(longest + "あ"), Is.True);
            Assert.That(AppTransferFlow.PlaceholderLink(AppTransferFlow.TransferName(longest), true).Length, Is.LessThanOrEqualTo(CloudTransferProtocol.MaximumImportLinkLength));
            TransferQrCode.Encode(PairLink(AppTransferFlow.TransferName(longest)));
            // The icon goes on a version 4 QR while the name is short enough, as on a version 3 one.
            var longestMarked = "";
            while (TransferQrCode.CanMark(AppTransferFlow.PlaceholderLink(AppTransferFlow.TransferName(longestMarked + "あ"), true))) longestMarked += "あ";
            Assert.That(longestMarked.Length >= 64, Is.True);
            foreach (var name in new[] { "SampleAvatar_v1.2", longestMarked })
                Assert.That(TransferQrCode.CanMark(PairLink(AppTransferFlow.TransferName(name))), Is.True);
            Assert.That(TransferQrCode.CanMark(AppTransferFlow.PlaceholderLink(AppTransferFlow.TransferName(longestMarked + "あ"), true)), Is.False);
        }

        // A marked version 4 QR reads with the icon's square blank, for a short name and the longest marked one.
        [Test]
        public void MarkedVersion4QrReadsWithTheCentreCovered()
        {
            var longestMarked = "";
            while (TransferQrCode.CanMark(AppTransferFlow.PlaceholderLink(AppTransferFlow.TransferName(longestMarked + "あ"), true))) longestMarked += "あ";
            foreach (var name in new[] { "SampleAvatar_v1.2", longestMarked })
            {
                var link = PairLink(AppTransferFlow.TransferName(name));
                var matrix = TransferQrCode.Encode(link, true);
                var blank = (int)Math.Ceiling(matrix.Width * TransferQrCode.MarkShare) + 2;
                var from = (matrix.Width - blank) / 2;
                const int zoom = 4;
                var pixels = new byte[matrix.Width * zoom * matrix.Height * zoom];
                for (var y = 0; y < matrix.Height * zoom; y++)
                    for (var x = 0; x < matrix.Width * zoom; x++)
                    {
                        int mx = x / zoom, my = y / zoom;
                        var covered = mx >= from && mx < from + blank && my >= from && my < from + blank;
                        pixels[y * matrix.Width * zoom + x] = matrix[mx, my] && !covered ? (byte)0 : (byte)255;
                    }
                var image = new ZXing.RGBLuminanceSource(pixels, matrix.Width * zoom, matrix.Height * zoom, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
                Assert.That(new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(image))).Text == link, Is.True);
            }
        }

        private static async Task<bool> Fails(Task upload)
        {
            try { await upload; return false; }
            catch (InvalidOperationException) { return true; }
        }

        private static Dictionary<string, string> PairLinkFields(string link)
        {
            Assert.That(link.StartsWith(CloudTransferProtocol.ImportLinkPrefix, StringComparison.Ordinal), Is.True);
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in link.Substring(CloudTransferProtocol.ImportLinkPrefix.Length).Split('&'))
                fields.Add(pair.Substring(0, pair.IndexOf('=')), pair.Substring(pair.IndexOf('=') + 1));
            Assert.That(fields.Count, Is.EqualTo(13));
            return fields;
        }

        // The window's flow over the two-transfer fake service.
        private sealed class PairFlowRun : IDisposable
        {
            internal readonly string NormalPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrvlog-app-transfer-pair-" + Guid.NewGuid().ToString("N") + ".vrm");
            internal readonly string LightPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrvlog-app-transfer-pair-" + Guid.NewGuid().ToString("N") + ".vrm");
            internal readonly AppTransferFlow Flow;
            internal PairTransport Transport;
            internal string Failure;
            private double time;

            internal PairFlowRun()
            {
                File.WriteAllBytes(NormalPath, Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray());
                File.WriteAllBytes(LightPath, Enumerable.Range(0, 2048).Select(i => (byte)(i % 241)).ToArray());
                Flow = new AppTransferFlow(createPair: (normal, light) =>
                {
                    Transport = new PairTransport(light == null ? new[] { normal } : new[] { normal, light }) { Failure = Failure };
                    return new CloudVrmTransferSession(normal, Transport, () => Transport.Now(), NoWait, light);
                });
            }

            internal async Task Until(Func<bool> reached)
            {
                for (var attempt = 0; attempt < 1000 && !reached(); attempt++)
                {
                    Flow.Tick(time += 10);
                    if (!reached()) await Task.Delay(5);
                }
                Assert.That(reached(), Is.True);
            }

            public void Dispose()
            {
                Flow.Dispose();
                foreach (var path in new[] { NormalPath, LightPath }) if (File.Exists(path)) File.Delete(path);
            }
        }

        private sealed class PairFixture : IDisposable
        {
            private readonly string normalPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrvlog-cloud-pair-" + Guid.NewGuid().ToString("N") + ".vrm");
            private readonly string lightPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrvlog-cloud-pair-" + Guid.NewGuid().ToString("N") + ".vrm");
            internal readonly CloudVrmTransferSource Normal, Light;
            internal readonly PairTransport Transport;
            internal PairFixture()
            {
                // Two parts for the normal file, one for the lightweight one.
                File.WriteAllBytes(normalPath, Enumerable.Range(0, CloudTransferProtocol.PartSize + 4096).Select(i => (byte)(i % 251)).ToArray());
                File.WriteAllBytes(lightPath, Enumerable.Range(0, 4096).Select(i => (byte)(i % 241)).ToArray());
                Normal = new CloudVrmTransferSource(normalPath, "テストアバター.vrm");
                Light = new CloudVrmTransferSource(lightPath, "テストアバター.vrm");
                Transport = new PairTransport(new[] { Normal, Light });
            }
            internal CloudVrmTransferSession Session(Func<long> now = null) => new CloudVrmTransferSession(Normal, Transport, now ?? Transport.Now, NoWait, Light);
            public void Dispose() { Normal.Dispose(); Light.Dispose(); }
        }

        // A service holding several transfers. Each create gets its own id and tokens.
        private sealed class PairTransport : ICloudTransferTransport
        {
            internal sealed class Transfer
            {
                internal string Id, ReadToken, UploadToken, WireHash;
                internal long WireSize, UploadExpiry, ReceiveExpiry;
                internal readonly List<byte[]> Parts = new List<byte[]>();
            }
            private readonly CloudVrmTransferSource[] sources;
            internal readonly List<Transfer> Transfers = new List<Transfer>();
            internal readonly List<string> Log = new List<string>();
            internal readonly List<string> Cancelled = new List<string>();
            internal Func<long> Now = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            internal bool ExtendedUploads = true;
            internal int[] PublicationLifetimes = { 180, 180 };
            internal string Failure, RemoteState = "ready";
            internal Action BeforeFirstLightCreate;
            internal TaskCompletionSource<bool> LightPartRelease;
            internal readonly TaskCompletionSource<bool> LightPartObserved = new TaskCompletionSource<bool>();
            internal PairTransport(CloudVrmTransferSource[] sources) { this.sources = sources; }

            private static string Token(int transfer, int kind) => CloudTransferProtocol.Base64Url(Enumerable.Repeat((byte)(transfer * 16 + kind + 1), 32).ToArray());

            public async Task<CloudTransferResponse> SendAsync(string method, string path, string token, byte[] body, string contentType, CancellationToken cancellation, int timeoutSeconds = 90)
            {
                if (path == "/v3/capabilities")
                {
                    Log.Add("capabilities");
                    return ExtendedUploads ? new CloudTransferResponse(200, "{\"v\":3,\"uploadLifetimeSeconds\":900}") : new CloudTransferResponse(404, "{}");
                }
                if (path == "/v3/uploads" || path == "/v3/transfers")
                {
                    var index = Transfers.Count;
                    if (index == 1) BeforeFirstLightCreate?.Invoke();
                    Log.Add("create " + index);
                    Assert.That(token == null, Is.True);
                    if (Failure == "light-create" && index == 1) return new CloudTransferResponse(503, "{}");
                    var request = CloudTransferProtocol.Fields.Read(Encoding.UTF8.GetString(body));
                    var created = new Transfer
                    {
                        Id = (index + 1).ToString("x32"), ReadToken = Token(index, 0), UploadToken = Token(index, 1),
                        WireSize = request.Number("size"), WireHash = request.Text("sha256"),
                        UploadExpiry = Now() + (path == "/v3/uploads" ? 900 : 180),
                    };
                    Assert.That(created.WireSize, Is.EqualTo(CloudEncryptedSnapshot.WireSize(sources[index].Size)));
                    Transfers.Add(created);
                    return new CloudTransferResponse(201, "{\"v\":3,\"id\":\"" + created.Id + "\",\"token\":\"" + created.ReadToken + "\",\"uploadToken\":\"" + created.UploadToken + "\",\"expiresAt\":" + created.UploadExpiry + ",\"partSize\":8388608}");
                }
                var transfer = Transfers.Single(candidate => path.StartsWith("/v3/transfers/" + candidate.Id, StringComparison.Ordinal));
                var number = Transfers.IndexOf(transfer);
                Assert.That(token, Is.EqualTo(transfer.UploadToken));
                if (path.EndsWith("/cancel", StringComparison.Ordinal)) { Cancelled.Add(transfer.Id); return new CloudTransferResponse(200, "{}"); }
                cancellation.ThrowIfCancellationRequested();
                if (path.Contains("/parts/"))
                {
                    var part = int.Parse(path.Substring(path.LastIndexOf('/') + 1));
                    Log.Add("part " + number + "/" + part);
                    if (number == 1)
                    {
                        if (Failure == "light-part") return new CloudTransferResponse(500, "{}");
                        LightPartObserved.TrySetResult(true);
                        if (LightPartRelease != null) await LightPartRelease.Task;
                        cancellation.ThrowIfCancellationRequested();
                    }
                    if (part > transfer.Parts.Count) transfer.Parts.Add((byte[])body.Clone());
                    return new CloudTransferResponse(200, "{}");
                }
                if (path.EndsWith("/publish", StringComparison.Ordinal))
                {
                    Log.Add("publish " + number);
                    if (Failure == "light-publish" && number == 1 || Failure == "normal-publish" && number == 0) return new CloudTransferResponse(503, "{}");
                    using (var digest = SHA256.Create()) Assert.That(CloudTransferProtocol.Hex(digest.ComputeHash(transfer.Parts.SelectMany(bytes => bytes).ToArray())), Is.EqualTo(transfer.WireHash));
                    transfer.ReceiveExpiry = ExtendedUploads ? Now() + PublicationLifetimes[number] : transfer.UploadExpiry;
                    var hash = Failure == "light-publish-mismatch" && number == 1 ? new string('f', 64) : transfer.WireHash;
                    return new CloudTransferResponse(200, "{\"v\":3,\"id\":\"" + transfer.Id + "\",\"token\":\"" + transfer.ReadToken + "\",\"name\":\"encrypted-avatar.bin\",\"size\":" + transfer.WireSize + ",\"sha256\":\"" + hash + "\",\"expiresAt\":" + transfer.ReceiveExpiry + "}");
                }
                Log.Add("state " + number);
                return new CloudTransferResponse(200, "{\"state\":\"" + RemoteState + "\"}");
            }
        }
    }
}
