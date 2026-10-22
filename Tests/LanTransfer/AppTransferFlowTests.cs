using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // What the "アプリに転送" window shows and when, without UnityEditor. These
    // share the cloud fixture and its fake service with the session tests.
    public sealed partial class CloudTransferTests
    {
        [TestCase(false, true, "SampleAvatar", true, true, true, true, AppTransferText.NeedsAvatar)]
        [TestCase(true, true, "", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "   ", true, true, false, false, AppTransferText.NeedsName)]
        // Names the phone would list as empty once the extension and invisible characters go.
        [TestCase(true, true, ".vrm", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, " .VRM ", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "\u200B", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "\u200B.vrm", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, " \u200B .vrm", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "\u202E\u0000", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "\uDB40\uDC41", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "\u00A0", true, true, true, true, AppTransferText.NeedsName)]
        [TestCase(true, true, "a.vrm", true, true, true, true, null)]
        [TestCase(true, true, ".vrm.vrm", true, true, true, true, null)]
        [TestCase(true, true, "avatar", true, true, true, true, null)]
        [TestCase(true, true, "\u200Bavatar", true, true, true, true, null)]
        [TestCase(true, true, "😀", true, true, true, true, null)]
        [TestCase(true, true, "SampleAvatar", false, true, true, true, AppTransferText.NeedsShorterName)]
        [TestCase(true, true, "SampleAvatar", true, false, true, true, AppTransferText.NeedsAuthor)]
        [TestCase(true, true, "SampleAvatar", true, true, false, true, AppTransferText.NeedsConsent)]
        [TestCase(true, true, "SampleAvatar", true, true, false, false, AppTransferText.NeedsConsent)]
        [TestCase(true, true, "SampleAvatar", true, true, true, false, AppTransferText.NeedsPrivacyConsent)]
        [TestCase(true, true, "SampleAvatar", true, true, true, true, null)]
        // The detailed transfer exports from the expression board: without it nothing else matters yet.
        [TestCase(false, false, "SampleAvatar", true, true, true, true, AppTransferText.NeedsAvatar)]
        [TestCase(true, false, "", false, false, false, false, AppTransferText.NeedsBoard)]
        [TestCase(true, false, "SampleAvatar", true, true, true, true, AppTransferText.NeedsBoard)]
        public void PrimaryButtonNamesTheFirstMissingInput(bool hasAvatar, bool boardReady, string name, bool nameFits, bool hasAuthor, bool consented, bool privacyAccepted, string reason) =>
            Assert.That(AppTransferFlow.BlockReason(hasAvatar, boardReady, name, nameFits, hasAuthor, consented, privacyAccepted), Is.EqualTo(reason));

        // Cases an attribute cannot carry: a lone surrogate, and a name whose visible
        // character falls past the length bound so only spaces stay in the transfer name.
        [Test]
        public void NamesWithNothingVisibleLeftNeedAName()
        {
            Assert.That(AppTransferFlow.BlockReason(true, true, "\uD800", true, true, true, true), Is.EqualTo(AppTransferText.NeedsName));
            Assert.That(AppTransferFlow.BlockReason(true, true, "\u200B" + new string(' ', 252) + "a", true, true, true, true), Is.EqualTo(AppTransferText.NeedsName));
        }

        [TestCase("SampleAvatar_v1.2", "SampleAvatar_v1.2.vrm")]
        [TestCase("  サンプル v1.2+α  ", "サンプル v1.2+α.vrm")]
        [TestCase("avatar.VRM", "avatar.vrm")]
        [TestCase("衣装/夜\\昼\u0000‮", "衣装-夜-昼.vrm")]
        public void TransferNameIsTheTypedNameAsAVrmFileName(string typed, string expected) =>
            Assert.That(AppTransferFlow.TransferName(typed), Is.EqualTo(expected));

        [Test]
        public void TransferNameKeepsItsExtensionWithinTheServiceBound()
        {
            var japanese = AppTransferFlow.TransferName(new string('あ', 300));
            Assert.That(japanese, Is.EqualTo(new string('あ', 252) + ".vrm"));
            Assert.That(AppTransferFlow.TransferName(new string('あ', 252)), Is.EqualTo(japanese));
            Assert.That(AppTransferFlow.TransferName(new string('あ', 251)).Length, Is.EqualTo(255));
            // A surrogate pair that would straddle the bound is dropped whole.
            var emoji = AppTransferFlow.TransferName("a" + string.Concat(Enumerable.Repeat("😀", 130)));
            Assert.That(emoji, Is.EqualTo("a" + string.Concat(Enumerable.Repeat("😀", 125)) + ".vrm"));
            foreach (var name in new[] { japanese, emoji })
            {
                Assert.That(name.Length, Is.LessThanOrEqualTo(256));
                Assert.That(CloudTransferProtocol.DisplayName(name), Is.EqualTo(name));
            }
        }

        [Test]
        public void NamesThatCannotBecomeOneQrAreRefusedBeforeAnyExport()
        {
            Assert.That(AppTransferFlow.NameFits("SampleAvatar_v1.2"), Is.True);
            Assert.That(AppTransferFlow.NameFits(new string('a', 300)), Is.True);
            Assert.That(AppTransferFlow.NameFits(new string('あ', 200)), Is.True);
            // 252 three-byte characters stay under the link bound but exceed one QR symbol.
            var longest = AppTransferFlow.TransferName(new string('あ', 300));
            Assert.That(AppTransferFlow.PlaceholderLink(longest).Length, Is.LessThanOrEqualTo(CloudTransferProtocol.MaximumImportLinkLength));
            Assert.That(AppTransferFlow.NameFits(new string('あ', 300)), Is.False);
        }

        [Test]
        public void PlaceholderLinkIsNeverShorterThanARealLinkAndHoldsNoSecret()
        {
            var placeholder = AppTransferFlow.PlaceholderLink("サンプル.vrm");
            Assert.That(Link("サンプル.vrm").Length, Is.LessThanOrEqualTo(placeholder.Length));
            var fields = ImportLinkFields(placeholder, out _);
            Assert.That(fields["id"], Is.EqualTo(new string('0', 32)));
            Assert.That(fields["token"], Is.EqualTo(new string('A', 43)));
            Assert.That(fields["key"], Is.EqualTo(new string('A', 86)));
            Assert.That(fields["size"], Is.EqualTo(CloudTransferProtocol.MaximumSize.ToString()));
        }

        [Test]
        public void QrKeepsAFourModuleQuietZoneAndAtLeastThreeHundredPointsOfSymbol()
        {
            var link = Link("サンプル v1.2+α\"q\".vrm");
            var matrix = TransferQrCode.Encode(link);
            Assert.That(matrix.Width, Is.EqualTo(matrix.Height));
            // A reader gets the link back unchanged from the drawn modules.
            const int zoom = 4;
            var pixels = new byte[matrix.Width * zoom * matrix.Height * zoom];
            for (var y = 0; y < matrix.Height * zoom; y++)
                for (var x = 0; x < matrix.Width * zoom; x++)
                    pixels[y * matrix.Width * zoom + x] = matrix[x / zoom, y / zoom] ? (byte)0 : (byte)255;
            var image = new ZXing.RGBLuminanceSource(pixels, matrix.Width * zoom, matrix.Height * zoom, ZXing.RGBLuminanceSource.BitmapFormat.Gray8);
            Assert.That(new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new ZXing.Common.HybridBinarizer(image))).Text, Is.EqualTo(link));
            Assert.That((matrix.Width - 2 * TransferQrCode.QuietZone - 17) % 4, Is.EqualTo(0));
            for (var offset = 0; offset < matrix.Width; offset++)
                for (var border = 0; border < TransferQrCode.QuietZone; border++)
                    Assert.That(matrix[offset, border] || matrix[border, offset] || matrix[offset, matrix.Height - 1 - border] || matrix[matrix.Width - 1 - border, offset], Is.False);
            Assert.That(matrix[TransferQrCode.QuietZone, TransferQrCode.QuietZone], Is.True);
            foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                foreach (var side in new[] { 29, matrix.Width, 185 })
                {
                    var points = TransferQrCode.DisplaySide(side, scale);
                    var pixelsPerModule = points * scale / side;
                    Assert.That(Math.Abs(pixelsPerModule - Math.Round(pixelsPerModule)) < 0.001, Is.True);
                    Assert.That(points * (side - 2 * TransferQrCode.QuietZone) / side >= TransferQrCode.MinimumSymbolSide, Is.True);
                    // One pixel per module less would fall under the minimum: the QR is no larger than needed.
                    Assert.That((pixelsPerModule - 1) * (side - 2 * TransferQrCode.QuietZone) / scale < TransferQrCode.MinimumSymbolSide, Is.True);
                }
            Assert.Throws<ArgumentOutOfRangeException>(() => TransferQrCode.DisplaySide(2 * TransferQrCode.QuietZone));
        }

        [Test]
        public void QrFittedToAHeightKeepsWholePixelModulesAndTwoHundredPointsOfSymbol()
        {
            foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
                foreach (var side in new[] { 29, 57, 185 })
                {
                    var full = TransferQrCode.DisplaySide(side, scale);
                    Assert.That(TransferQrCode.FittedSide(side, full, scale), Is.EqualTo(full));
                    Assert.That(TransferQrCode.FittedSide(side, float.PositiveInfinity, scale), Is.EqualTo(full));
                    foreach (var room in new[] { 0f, 150f, 260f, 359f, full - 1f })
                    {
                        var points = TransferQrCode.FittedSide(side, room, scale);
                        var pixelsPerModule = points * scale / side;
                        Assert.That(Math.Abs(pixelsPerModule - Math.Round(pixelsPerModule)) < 0.001, Is.True);
                        Assert.That(points <= full, Is.True);
                        var symbol = points * (side - 2 * TransferQrCode.QuietZone) / side;
                        Assert.That(symbol >= TransferQrCode.SmallestSymbolSide - 0.001, Is.True);
                        // Larger than the room only where the room is under the smallest symbol.
                        if (points > room + 0.001)
                            Assert.That((pixelsPerModule - 1) * (side - 2 * TransferQrCode.QuietZone) / scale < TransferQrCode.SmallestSymbolSide, Is.True);
                        else
                            Assert.That((pixelsPerModule + 1) * side / scale > room, Is.True);
                    }
                }
        }

        [Test]
        public async Task FlowShowsTheLinkAfterTheUploadAndCountsDownToTheServiceDeadline()
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var run = new FlowRun(() => currentTime))
            {
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Form));
                run.Flow.BeginExport();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Exporting));
                Assert.That(run.Flow.Link == null, Is.True);
                // The upload takes a minute of the three the service allows.
                run.BeforePart = () => currentTime += 60;
                run.Flow.Upload(run.Path, AppTransferFlow.TransferName("サンプル v1.2"));
                Assert.That(run.Flow.Link == null, Is.True);
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                var fields = ImportLinkFields(run.Flow.Link, out _);
                Assert.That(fields["name"], Is.EqualTo("サンプル v1.2.vrm"));
                Assert.That(fields["exp"], Is.EqualTo((currentTime + 120).ToString()));
                Assert.That(run.Flow.Remaining, Is.EqualTo(120L));
                Assert.That(run.Flow.RemainingFraction, Is.EqualTo(1f));
                currentTime += 30;
                Assert.That(run.Flow.Remaining, Is.EqualTo(90L));
                Assert.That(run.Flow.RemainingFraction, Is.EqualTo(0.75f));
                currentTime += 89;
                run.Tick();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                Assert.That(run.Flow.Remaining, Is.EqualTo(1L));
                currentTime += 1;
                run.Tick();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Expired));
                Assert.That(run.Flow.Link == null, Is.True);
                Assert.That(run.Flow.Remaining, Is.EqualTo(0L));
                Assert.That(run.Flow.RemainingFraction, Is.EqualTo(0f));
                Assert.That(run.Flow.CanRegenerate, Is.True);
                Assert.That(File.Exists(run.Path), Is.True);
            }
        }

        [Test]
        public async Task RegeneratingUploadsAgainAsANewSessionFromTheSameExport()
        {
            var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            using (var run = new FlowRun(() => currentTime))
            {
                run.Flow.Upload(run.Path, "fixture.vrm");
                await run.Until(() => run.Flow.View == AppTransferView.Waiting);
                var first = run.Flow.Link;
                var firstTransport = run.Transport;
                currentTime += 180;
                run.Tick();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Expired));
                run.Flow.Regenerate();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                await run.Until(() => run.Flow.View == AppTransferView.Waiting);
                Assert.That(ReferenceEquals(run.Transport, firstTransport), Is.False);
                Assert.That(run.Transport.Parts.Count, Is.EqualTo(1));
                Assert.That(run.Flow.Link != first, Is.True);
                Assert.That(ImportLinkFields(run.Flow.Link, out _)["key"] != ImportLinkFields(first, out _)["key"], Is.True);
                Assert.That(run.Flow.Remaining, Is.EqualTo(180L));
                // While a QR is waiting, a new one cancels it first.
                var waiting = run.Transport;
                run.Flow.Regenerate();
                await run.Until(() => run.Flow.View == AppTransferView.Waiting && !ReferenceEquals(run.Transport, waiting));
                await run.Until(() => waiting.CancelCalls == 1);
                Assert.That(waiting.CancelCalls, Is.EqualTo(1));
            }
        }

        [TestCase("completed", "Completed")]
        [TestCase("cancelled", "Canceled")]
        [TestCase("failed", "Failed")]
        [TestCase("expired", "Expired")]
        public async Task FlowFollowsEveryEndTheServiceReports(string remote, string view)
        {
            var expected = (AppTransferView)Enum.Parse(typeof(AppTransferView), view);
            using (var run = new FlowRun())
            {
                run.Flow.Upload(run.Path, "fixture.vrm");
                await run.Until(() => run.Flow.View == AppTransferView.Waiting);
                run.Transport.RemoteState = remote;
                await run.Until(() => run.Flow.View != AppTransferView.Waiting);
                run.Tick();
                Assert.That(run.Flow.View, Is.EqualTo(expected));
                Assert.That(run.Flow.Link == null, Is.True);
                Assert.That(run.Flow.Remaining, Is.EqualTo(0L));
                Assert.That(run.Flow.RemainingFraction, Is.EqualTo(0f));
                if (expected == AppTransferView.Failed) Assert.That(run.Flow.FailureText(text => text).Length > 0, Is.True);
                // Once the phone has the avatar the exported copy is deleted; otherwise it stays for a new QR.
                Assert.That(File.Exists(run.Path), Is.EqualTo(expected != AppTransferView.Completed));
                Assert.That(run.Flow.CanRegenerate, Is.EqualTo(expected != AppTransferView.Completed));
            }
        }

        [Test]
        public async Task UploadFailureStaysOnScreenWithItsReasonUntilRetried()
        {
            using (var run = new FlowRun())
            {
                run.Failure = "quota";
                run.Flow.Upload(run.Path, "fixture.vrm");
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Failed));
                Assert.That(run.Flow.ExportDidFail, Is.False);
                Assert.That(run.Flow.FailureText(text => text).Contains("HTTP 429"), Is.True);
                Assert.That(run.Flow.Link == null, Is.True);
                Assert.That(run.Flow.CanRegenerate, Is.True);
                run.Failure = null;
                run.Flow.Regenerate();
                await run.Until(() => run.Flow.View != AppTransferView.Uploading);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
            }
        }

        [Test]
        public async Task UnreachableStatusIsShownWithoutEndingTheWait()
        {
            using (var run = new FlowRun())
            {
                run.Flow.Upload(run.Path, "fixture.vrm");
                await run.Until(() => run.Flow.View == AppTransferView.Waiting);
                run.Transport.Failure = "poll-failure";
                await run.Until(() => run.Flow.StatusUnknown);
                Assert.That(run.Flow.StatusUnknown, Is.True);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Waiting));
                Assert.That(run.Flow.Link != null, Is.True);
                run.Transport.Failure = null;
                await run.Until(() => !run.Flow.StatusUnknown);
                Assert.That(run.Flow.StatusUnknown, Is.False);
            }
        }

        [Test]
        public void ExportFailureAndCancellationNeverStartAnUpload()
        {
            using (var run = new FlowRun())
            {
                run.Flow.BeginExport();
                run.Flow.ExportCanceled();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Form));
                run.Flow.BeginExport();
                run.Flow.ExportFailed("理由");
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Failed));
                Assert.That(run.Flow.ExportDidFail, Is.True);
                Assert.That(run.Flow.FailureText(text => "[" + text + "]"), Is.EqualTo("[理由]"));
                Assert.That(run.Flow.CanRegenerate, Is.False);
                run.Flow.Regenerate();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Failed));
                run.Flow.BeginExport();
                run.Flow.ExportFailed(null);
                Assert.That(run.Flow.FailureText(text => "[" + text + "]"), Is.EqualTo(""));
                run.Flow.BeginExport();
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Exporting));
                Assert.That(run.Flow.ExportDidFail, Is.False);
                Assert.That(run.Transport == null, Is.True);
            }
        }

        [Test]
        public void SnapshotThatCannotBeTransferredFailsInPlaceAndIsDeleted()
        {
            // The source accepts 1 byte to 256 MiB; an empty file takes the same branch as one that is too large.
            using (var run = new FlowRun(length: 0))
            {
                run.Flow.Upload(run.Path, "fixture.vrm");
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Failed));
                Assert.That(run.Flow.FailureText(text => text), Is.EqualTo(AppTransferText.SnapshotTooLarge));
                Assert.That(File.Exists(run.Path), Is.False);
                Assert.That(run.Flow.CanRegenerate, Is.False);
                run.Flow.Upload(run.Path, "fixture.vrm");
                Assert.That(run.Flow.FailureText(text => text), Is.EqualTo(AppTransferText.SnapshotUnreadable));
                Assert.That(run.Transport == null, Is.True);
            }
        }

        [TestCase(false)][TestCase(true)]
        public async Task ClosingCancelsTheTransferAndDeletesTheExportedCopy(bool waiting)
        {
            using (var run = new FlowRun())
            {
                TaskCompletionSource<bool> held = null;
                if (!waiting) run.Configure = transport => transport.CreateRelease = held = new TaskCompletionSource<bool>();
                run.Flow.Upload(run.Path, "fixture.vrm");
                if (waiting) await run.Until(() => run.Flow.View == AppTransferView.Waiting);
                else await run.Until(() => run.Transport != null && run.Transport.CreateObserved.Task.IsCompleted);
                var transport = run.Transport;
                run.Flow.Stop();
                held?.SetResult(true);
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Form));
                Assert.That(run.Flow.Link == null, Is.True);
                Assert.That(File.Exists(run.Path), Is.False);
                await run.Until(() => transport.CancelCalls == 1);
                Assert.That(transport.CancelCalls, Is.EqualTo(1));
                Assert.That(transport.PublishCalls, Is.EqualTo(waiting ? 1 : 0));
            }
        }

        [Test]
        public void ConsentSentenceAndTermsPageStayExactlyAsAgreed()
        {
            Assert.That(AppTransferFlow.ConsentText, Is.EqualTo("このアバターを VR Vlog へ転送して利用することに同意します（利用規約）"));
            Assert.That(AppTransferFlow.TermsUrl, Is.EqualTo("https://vrvlog.fun/terms/"));
        }

        [TestCase("Waiting", true, true, true)]
        [TestCase("Waiting", true, false, false)]
        [TestCase("Waiting", false, true, false)]
        [TestCase("Failed", false, true, false)]
        [TestCase("Failed", true, true, false)]
        [TestCase("Canceled", false, true, false)]
        [TestCase("Expired", false, true, false)]
        [TestCase("Completed", false, true, false)]
        [TestCase("Uploading", false, true, false)]
        [TestCase("Form", false, true, false)]
        public void OnlyAQrOnScreenWithTheSoundOnBeeps(string view, bool qrShown, bool soundOn, bool beeps)
        {
            Assert.That(AppTransferFlow.ShouldBeepForQr((AppTransferView)Enum.Parse(typeof(AppTransferView), view), qrShown, soundOn), Is.EqualTo(beeps));
        }

        [Test]
        public void QrReadySoundIsOnByDefault()
        {
            Assert.That(AppTransferFlow.QrReadySoundByDefault, Is.True);
        }

        [Test]
        public void WindowTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
#if VRVLOG_LAN_TRANSFER_CLI
            var root = Directory.GetCurrentDirectory();
#else
            var root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CloudVrmTransferSession).Assembly).resolvedPath;
#endif
            var texts = typeof(AppTransferText).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                .Where(field => field.IsLiteral).Select(field => (string)field.GetRawConstantValue()).ToArray();
            Assert.That(texts.Length, Is.EqualTo(40));
            Assert.That(texts.Distinct().Count(), Is.EqualTo(texts.Length));
            foreach (var text in texts)
            {
                // One line, no punctuation, no request or ability phrasing, no exclamation.
                // The QR notice alone asks politely, as the owner chose for that line.
                var request = text == AppTransferText.DoNotShare ? "" : "|ください";
                Assert.That(Regex.IsMatch(text, "[。、！!\n]" + request + "|できます|しまいました"), Is.False);
                // This package writes Japanese and Latin text without a space between them.
                Assert.That(Regex.IsMatch(text, "[ぁ-んァ-ヶ一-龠] [A-Za-z0-9]|[A-Za-z0-9] [ぁ-んァ-ヶ一-龠]"), Is.False);
            }
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var table = File.ReadAllText(System.IO.Path.Combine(root, "Editor/Locales/ExporterLocale_" + locale + ".json"));
                foreach (var text in texts)
                {
                    var entry = Regex.Match(table, Regex.Escape("{\"source\":" + LanTransferProtocol.JsonString(text) + ",\"translated\":\"") + "((?:[^\"\\\\]|\\\\.)+)\"\\}");
                    Assert.That(entry.Success, Is.True);
                    var translated = entry.Groups[1].Value;
                    Assert.That(Regex.IsMatch(translated, "[ぁ-んァ-ヶ]"), Is.False);
                    Assert.That(Regex.Matches(translated, @"\{\d").Count, Is.EqualTo(Regex.Matches(text, @"\{\d").Count));
                    // No closing period, no "please" and no exclamation in any language.
                    Assert.That(Regex.IsMatch(translated, @"[.。！!]$|\bPlease\b|주세요|请|請", RegexOptions.IgnoreCase), Is.False);
                }
            }
        }

#if !VRVLOG_LAN_TRANSFER_CLI
        [Test]
        public void OpeningTheSameAvatarDuringATransferOnlyFocusesTheWindow()
        {
            const BindingFlags field = BindingFlags.NonPublic | BindingFlags.Instance;
            using (var run = new FlowRun())
            {
                // Until a tick sees the upload end, the flow stays in Uploading.
                run.Flow.Upload(run.Path, "fixture.vrm");
                Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                var avatar = new UnityEngine.GameObject("vrvlog-app-transfer-reopen", typeof(UnityEngine.Animator));
                var window = UnityEditor.EditorWindow.GetWindow<AppTransferWindow>(false);
                var panel = typeof(AppTransferWindow).GetField("panel", field).GetValue(window);
                try
                {
                    typeof(AppTransferPanel).GetField("avatar", field).SetValue(panel, avatar);
                    typeof(AppTransferPanel).GetField("avatarName", field).SetValue(panel, "SampleAvatar");
                    typeof(AppTransferPanel).GetField("flow", field).SetValue(panel, run.Flow);
                    AppTransferWindow.Open(avatar);
                    Assert.That(UnityEngine.Resources.FindObjectsOfTypeAll<AppTransferWindow>().Length, Is.EqualTo(1));
                    Assert.That(typeof(AppTransferPanel).GetField("flow", field).GetValue(panel), Is.SameAs(run.Flow));
                    Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                    Assert.That(typeof(AppTransferPanel).GetField("avatarName", field).GetValue(panel), Is.EqualTo("SampleAvatar"));
                    Assert.That(File.Exists(run.Path), Is.True);
                }
                finally
                {
                    typeof(AppTransferPanel).GetField("flow", field).SetValue(panel, null);
                    window.Close();
                    UnityEngine.Object.DestroyImmediate(avatar);
                }
            }
        }

        [Test]
        public async Task DecliningTheStartDialogKeepsTheOtherUploadAndStartsNothing()
        {
            using (var running = new FlowRun())
            using (var next = new FlowRun())
            {
                // Until a tick sees the upload end, the flow stays in Uploading.
                running.Flow.Upload(running.Path, "fixture.vrm");
                await FailOnce(next);
                var other = PanelWith(running.Flow, detailed: true);
                var panel = PanelWith(next.Flow, detailed: false);
                var asked = new System.Collections.Generic.List<(string Title, string Body)>();
                WithStartDialog(other, panel, (title, body) => { asked.Add((title, body)); return false; }, () =>
                {
                    Retry(panel);
                    Assert.That(asked.Count, Is.EqualTo(1));
                    Assert.That(asked[0].Title, Is.EqualTo(Localized(AppTransferText.StartTransfer)));
                    Assert.That(asked[0].Body, Is.EqualTo(string.Format(Localized(AppTransferPanel.StopUploadText), Localized(AppTransferText.HeadingDetailed))));
                    // "戻る" leaves both as they were.
                    Assert.That(running.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                    Assert.That(File.Exists(running.Path), Is.True);
                    Assert.That(next.Flow.View, Is.EqualTo(AppTransferView.Failed));
                });
            }
        }

        [Test]
        public async Task AcceptingTheStartDialogStopsTheOtherQrBeforeStarting()
        {
            using (var running = new FlowRun())
            using (var next = new FlowRun())
            {
                running.Flow.Upload(running.Path, "fixture.vrm");
                await running.Until(() => running.Flow.View == AppTransferView.Waiting);
                await FailOnce(next);
                var other = PanelWith(running.Flow, detailed: false);
                var panel = PanelWith(next.Flow, detailed: true);
                var asked = new System.Collections.Generic.List<string>();
                WithStartDialog(other, panel, (title, body) => { asked.Add(body); return true; }, () =>
                {
                    Retry(panel);
                    Assert.That(asked, Is.EqualTo(new[] { string.Format(Localized(AppTransferPanel.StopQrText), Localized(AppTransferText.HeadingQuick)) }));
                    // The other panel is back at its form with no QR, and this one uploads again.
                    Assert.That(running.Flow.View, Is.EqualTo(AppTransferView.Form));
                    Assert.That(running.Flow.Link == null, Is.True);
                    Assert.That(next.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                });
                await next.Until(() => next.Flow.View == AppTransferView.Waiting);
            }
        }

        // Opening the window for another avatar while the board's page shows its QR: "変更する"
        // stops that transfer and deletes its copy, so no second dialog follows.
        [Test]
        public async Task AcceptingTheWindowDialogStopsTheOtherTransfer()
        {
            using (var running = new FlowRun())
            {
                running.Flow.Upload(running.Path, "fixture.vrm");
                await running.Until(() => running.Flow.View == AppTransferView.Waiting);
                var other = PanelWith(running.Flow, detailed: true);
                var target = new UnityEngine.GameObject("vrvlog-app-transfer-other", typeof(UnityEngine.Animator));
                var asked = new System.Collections.Generic.List<bool>();
                try
                {
                    AppTransferPanel.Live.Add(other);
                    Assert.That(AppTransferWindow.MayOpen(target, false, same => { asked.Add(same); return true; }), Is.True);
                    Assert.That(asked, Is.EqualTo(new[] { false }));
                    Assert.That(other.Transferring, Is.False);
                    Assert.That(running.Flow.View, Is.EqualTo(AppTransferView.Form));
                    Assert.That(running.Flow.Link == null, Is.True);
                    Assert.That(File.Exists(running.Path), Is.False);
                }
                finally
                {
                    AppTransferPanel.Live.Remove(other);
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        [Test]
        public void DecliningTheWindowDialogKeepsTheOtherTransfer()
        {
            using (var running = new FlowRun())
            {
                // Until a tick sees the upload end, the flow stays in Uploading.
                running.Flow.Upload(running.Path, "fixture.vrm");
                var other = PanelWith(running.Flow, detailed: true);
                var target = new UnityEngine.GameObject("vrvlog-app-transfer-other", typeof(UnityEngine.Animator));
                var asked = 0;
                try
                {
                    AppTransferPanel.Live.Add(other);
                    Assert.That(AppTransferWindow.MayOpen(target, false, same => { asked++; return false; }), Is.False);
                    Assert.That(asked, Is.EqualTo(1));
                    Assert.That(other.Transferring, Is.True);
                    Assert.That(running.Flow.View, Is.EqualTo(AppTransferView.Uploading));
                    Assert.That(File.Exists(running.Path), Is.True);
                }
                finally
                {
                    AppTransferPanel.Live.Remove(other);
                    UnityEngine.Object.DestroyImmediate(target);
                }
            }
        }

        // The board asks the panel it shows by its element, which is the panel's root.
        [Test]
        public void EmbeddedTransferringIsTrueOnlyForTheBoardPanelOnScreenWhileItTransfers()
        {
            using (var running = new FlowRun())
            {
                var other = PanelWith(running.Flow, detailed: true);
                var element = new UnityEngine.UIElements.VisualElement();
                typeof(AppTransferPanel).GetField("root", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(other, element);
                running.Flow.Upload(running.Path, "fixture.vrm");
                // Not on screen.
                Assert.That(AppTransferPanel.EmbeddedTransferring(element), Is.False);
                try
                {
                    AppTransferPanel.Live.Add(other);
                    Assert.That(AppTransferPanel.EmbeddedTransferring(element), Is.True);
                    Assert.That(AppTransferPanel.EmbeddedTransferring(new UnityEngine.UIElements.VisualElement()), Is.False);
                    Assert.That(AppTransferPanel.EmbeddedTransferring(null), Is.False);
                    other.StopTransfer();
                    Assert.That(AppTransferPanel.EmbeddedTransferring(element), Is.False);
                }
                finally { AppTransferPanel.Live.Remove(other); }
            }
        }

        // The start dialog's two sentences are not AppTransferText, which the wording test covers.
        [Test]
        public void StartDialogTextExistsInEveryLocale()
        {
            var root = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CloudVrmTransferSession).Assembly).resolvedPath;
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var table = File.ReadAllText(System.IO.Path.Combine(root, "Editor/Locales/ExporterLocale_" + locale + ".json"));
                foreach (var text in new[] { AppTransferPanel.StopUploadText, AppTransferPanel.StopQrText })
                {
                    var entry = Regex.Match(table, Regex.Escape("{\"source\":" + LanTransferProtocol.JsonString(text) + ",\"translated\":\"") + "((?:[^\"\\\\]|\\\\.)+)\"\\}");
                    Assert.That(entry.Success, Is.True, locale);
                    Assert.That(Regex.Matches(entry.Groups[1].Value, @"\{\d").Count, Is.EqualTo(1), locale);
                }
            }
        }

        // A refused upload, whose "もう一度" starts the transfer again from the same copy.
        private static async Task FailOnce(FlowRun run)
        {
            run.Failure = "quota";
            run.Flow.Upload(run.Path, "fixture.vrm");
            await run.Until(() => run.Flow.View != AppTransferView.Uploading);
            Assert.That(run.Flow.View, Is.EqualTo(AppTransferView.Failed));
            Assert.That(run.Flow.CanRegenerate, Is.True);
            run.Failure = null;
        }

        private static string Localized(string source) =>
            (string)typeof(AppTransferPanel).GetMethod("T", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { source });

        private static AppTransferPanel PanelWith(AppTransferFlow flow, bool detailed)
        {
            var panel = new AppTransferPanel { Detailed = detailed };
            typeof(AppTransferPanel).GetField("flow", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(panel, flow);
            return panel;
        }

        private static void Retry(AppTransferPanel panel) =>
            typeof(AppTransferPanel).GetMethod("Retry", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);

        // Both panels on screen without their Editor polling, the dialog answered by the test,
        // and the shared privacy choice accepted so "もう一度" does not return to the form.
        private static void WithStartDialog(AppTransferPanel other, AppTransferPanel panel, Func<string, string, bool> answer, Action body)
        {
            const string key = CloudVrmTransferWindow.PrivacyConsentPreference;
            var hadPrivacy = UnityEditor.EditorPrefs.HasKey(key);
            var privacy = UnityEditor.EditorPrefs.GetBool(key, false);
            var dialog = AppTransferPanel.ConfirmStart;
            try
            {
                CloudVrmTransferWindow.PrivacyConsentAccepted = true;
                AppTransferPanel.Live.Add(other);
                AppTransferPanel.Live.Add(panel);
                AppTransferPanel.ConfirmStart = answer;
                body();
            }
            finally
            {
                AppTransferPanel.ConfirmStart = dialog;
                AppTransferPanel.Live.Remove(other);
                AppTransferPanel.Live.Remove(panel);
                if (hadPrivacy) UnityEditor.EditorPrefs.SetBool(key, privacy);
                else UnityEditor.EditorPrefs.DeleteKey(key);
            }
        }
#endif

        // One exported snapshot and the flow that adopts it, against the fake service.
        private sealed class FlowRun : IDisposable
        {
            internal readonly string Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vrvlog-app-transfer-test-" + Guid.NewGuid().ToString("N") + ".vrm");
            internal readonly AppTransferFlow Flow;
            internal FakeTransport Transport;
            internal Action BeforePart;
            internal string Failure;
            internal Action<FakeTransport> Configure;
            private double time;

            internal FlowRun(Func<long> now = null, int length = 47)
            {
                var clock = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                var bytes = new byte[length];
                for (var index = 0; index < length; index++) bytes[index] = (byte)(index % 251);
                File.WriteAllBytes(Path, bytes);
                Flow = new AppTransferFlow(source =>
                {
                    Transport = new FakeTransport(source) { Now = clock, BeforePart = BeforePart, Failure = Failure };
                    Configure?.Invoke(Transport);
                    return new CloudVrmTransferSession(source, Transport, clock);
                }, clock);
            }

            // Each tick is later than the status interval, so every one may ask the service again.
            internal void Tick() => Flow.Tick(time += 10);

            internal async Task Until(Func<bool> reached)
            {
                for (var attempt = 0; attempt < 1000 && !reached(); attempt++)
                {
                    Tick();
                    if (!reached()) await Task.Delay(5);
                }
                Assert.That(reached(), Is.True);
            }

            public void Dispose()
            {
                Flow.Dispose();
                if (File.Exists(Path)) File.Delete(Path);
            }
        }
    }
}
