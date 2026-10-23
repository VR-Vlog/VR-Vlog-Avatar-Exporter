using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // Japanese sources of the "アプリに転送" window. Editor/Locales carries the
    // other languages; the host checks require every entry in all of them.
    internal static class AppTransferText
    {
        internal const string Title = "アプリに転送";
        internal const string TitleWithAvatar = "アプリに転送 — {0}";
        internal const string HeadingQuick = "アプリに転送（かんたん）";
        internal const string HeadingDetailed = "アプリに転送（通常）";
        internal const string EditDetails = "表情とオブジェクトを選ぶ";
        internal const string BackToBoard = "戻って編集";
        internal const string OpenBoard = "割り当て画面を開く";
        internal const string Sending = "{0}を送る";
        internal const string Name = "名前";
        internal const string NameHint = "アプリのアバター一覧での名前";
        internal const string Terms = "規約";
        internal const string ReadTerms = "利用規約を読む";
        internal const string Create = "QRコードを作る";
        internal const string NeedsAvatar = "アバターを指定すると作れます";
        internal const string NeedsBoard = "表情の割り当て画面が開いていません";
        internal const string NeedsBoardFix = "表情の割り当てを直すと作れます";
        internal const string NeedsName = "名前を入れると作れます";
        internal const string NeedsShorterName = "名前を短くすると作れます";
        internal const string NeedsAuthor = "作者名を入れると作れます";
        internal const string NeedsConsent = "利用規約に同意すると作れます";
        internal const string NeedsPrivacyConsent = "プライバシーポリシーに同意すると作れます";
        internal const string Avatar = "アバター";
        internal const string Exporting = "VRMを書き出しています";
        internal const string ExportFailed = "書き出せませんでした";
        internal const string SnapshotTooLarge = "書き出したVRMが256MiBを超えています";
        internal const string SnapshotUnreadable = "書き出したVRMを開けません";
        internal const string ScanWithCamera = "スマホのカメラで読み取る";
        internal const string DoNotShare = "QRを他の人に見せたり配信に映したりしないでください";
        internal const string Waiting = "読み取り待ち";
        internal const string StatusUnknown = "状態を確認できません";
        internal const string Lifetime = "有効期限{0}分";
        internal const string Regenerate = "QRを作り直す";
        internal const string QrReadySound = "QRができたら音を鳴らす";
        internal const string Retry = "もう一度";
        internal const string Expired = "期限が切れました";
        internal const string Completed = "取り込みが終わりました";
        internal const string LayoutMissing = "画面のファイルを読み込めません（パッケージを再インポートすると直ります）";
        internal const string StartTransfer = "転送を始める";
        internal const string StopAndStart = "中止して始める";
        internal const string LeavingCancels = "ページを移るかボードを閉じると転送を中止します";
    }

    // One encoder for both transfer windows, so their QR codes cannot drift.
    internal static class TransferQrCode
    {
        internal const int QuietZone = 4;
        internal const int MinimumSymbolSide = 300;

        internal static BitMatrix Encode(string payload) => new QRCodeWriter().encode(payload, BarcodeFormat.QR_CODE, 0, 0,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M, [EncodeHintType.MARGIN] = QuietZone, [EncodeHintType.CHARACTER_SET] = "UTF-8" });

        // Side in GUI points for a matrix that includes its quiet zone. Every
        // module covers a whole number of screen pixels and the symbol inside
        // the quiet zone is at least MinimumSymbolSide points wide.
        internal static float DisplaySide(int matrixSide, float pixelsPerPoint = 1f)
        {
            if (matrixSide <= 2 * QuietZone) throw new ArgumentOutOfRangeException(nameof(matrixSide));
            var scale = pixelsPerPoint > 0f ? pixelsPerPoint : 1f;
            var pixelsPerModule = (int)Math.Ceiling(MinimumSymbolSide * scale / (matrixSide - 2 * QuietZone));
            return matrixSide * pixelsPerModule / scale;
        }

        // On the expression board the QR shares a page of fixed height with the rows around it.
        internal const int SmallestSymbolSide = 200;

        // DisplaySide, made smaller to fit within maxSide points: whole pixels per module still, and
        // never under SmallestSymbolSide points of symbol, so a short area scrolls rather than an unreadable QR.
        internal static float FittedSide(int matrixSide, float maxSide, float pixelsPerPoint = 1f)
        {
            var full = DisplaySide(matrixSide, pixelsPerPoint);
            if (!(maxSide < full)) return full;
            var scale = pixelsPerPoint > 0f ? pixelsPerPoint : 1f;
            var fitting = (int)Math.Floor(maxSide * scale / matrixSide);
            var smallest = (int)Math.Ceiling(SmallestSymbolSide * scale / (matrixSide - 2 * QuietZone));
            return matrixSide * Math.Max(fitting, smallest) / scale;
        }
    }

    internal enum AppTransferView { Form, Exporting, Uploading, Waiting, Completed, Expired, Canceled, Failed }

    // The inputs "QRコードを作る" needs, whose labels turn red when it is pressed without them.
    [Flags]
    internal enum RequiredInput { None = 0, Avatar = 1, Name = 2, Author = 4, Consent = 8, PrivacyConsent = 16 }

    // The "アプリに転送" window without UnityEditor, so the host checks cover what
    // it shows and when. The window draws View and forwards the buttons.
    internal sealed class AppTransferFlow : IDisposable
    {
        // 2026-10-14にオーナーの指示でモックの文言のまま採用した。法的な文言なので、オーナーの承認なしに一字も変えず、翻訳もしない。
        internal const string ConsentText = "このアバターを VR Vlog へ転送して利用することに同意します（利用規約）";
        // 2026-10-14にオーナーの指示で採用した。VR Vlogアプリが利用規約として開くページと同じで、オーナーの承認なしに変えない。
        internal const string TermsUrl = "https://vrvlog.fun/terms/";
        internal const string Extension = ".vrm";
        private const int MaximumNameLength = 256;
        // "QRができたら音を鳴らす" is on until the user turns it off.
        internal const bool QrReadySoundByDefault = true;

        private readonly Func<CloudVrmTransferSource, CloudVrmTransferSession> createSession;
        private readonly Func<long> now;
        private CloudVrmTransferSource source;
        private CloudVrmTransferSession session;
        private Task uploading, polling;
        private double nextPoll;
        private long shownRemaining;
        private bool exporting;
        // Null unless something failed outside the session; empty when there is no reason to add.
        private string failure;

        internal AppTransferFlow(Func<CloudVrmTransferSource, CloudVrmTransferSession> createSession = null, Func<long> now = null)
        {
            this.now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            this.createSession = createSession ?? (owned => new CloudVrmTransferSession(owned, null, this.now));
        }

        // Why the primary button is disabled, or null when it can be pressed.
        // privacyAccepted is the saved privacy policy choice shared with "スマホに送る".
        // boardReady is false only for the detailed transfer while its expression board is not open.
        internal static string BlockReason(bool hasAvatar, bool boardReady, string name, bool nameFits, bool hasAuthor, bool consented, bool privacyAccepted) =>
            !hasAvatar ? AppTransferText.NeedsAvatar
            : !boardReady ? AppTransferText.NeedsBoard
            : !HasListedName(name) ? AppTransferText.NeedsName
            : !nameFits ? AppTransferText.NeedsShorterName
            : !hasAuthor ? AppTransferText.NeedsAuthor
            : !consented ? AppTransferText.NeedsConsent
            : privacyAccepted ? null : AppTransferText.NeedsPrivacyConsent;

        // The inputs to show in red: none until "QRコードを作る" has been pressed without them, then each
        // one still missing, so a field turns back as soon as it is filled in.
        internal static RequiredInput MarkedInputs(bool pressed, bool hasAvatar, string name, bool nameFits, bool hasAuthor, bool consented, bool privacyAccepted)
        {
            if (!pressed) return RequiredInput.None;
            var missing = RequiredInput.None;
            if (!hasAvatar) missing |= RequiredInput.Avatar;
            if (!HasListedName(name) || !nameFits) missing |= RequiredInput.Name;
            if (!hasAuthor) missing |= RequiredInput.Author;
            if (!consented) missing |= RequiredInput.Consent;
            if (!privacyAccepted) missing |= RequiredInput.PrivacyConsent;
            return missing;
        }

        // Filling the fields does not fix the board, so these reasons keep "QRコードを作る" disabled.
        internal static bool BoardBlocks(string reason) =>
            reason == AppTransferText.NeedsBoard || reason == AppTransferText.NeedsBoardFix;

        // The line above "QRコードを作る" shows only the board's reasons, which keep the button disabled;
        // a missing input is shown by turning its field red once the button is pressed.
        internal static string ReasonText(string reason, Func<string, string> translate) =>
            BoardBlocks(reason) ? translate(reason) : "";

        // The OS notification sound plays only when a QR has just appeared on screen; a QR
        // that could not be drawn, a failed or canceled transfer and an ended wait stay silent.
        internal static bool ShouldBeepForQr(AppTransferView view, bool qrShown, bool soundOn) =>
            soundOn && qrShown && view == AppTransferView.Waiting;

        // "QRを他の人に見せたり…" is read before the QR appears: from the export, through the upload,
        // and under the QR. Once there is no QR coming (done, failed, canceled, expired) it goes.
        internal static bool ShowsDoNotShare(AppTransferView view) =>
            view == AppTransferView.Exporting || view == AppTransferView.Uploading || view == AppTransferView.Waiting;

        // The phone stores the transfer under this name and lists the avatar
        // without the extension. This string is also the QR and AAD name.
        internal static string TransferName(string name)
        {
            var text = CloudTransferProtocol.DisplayName((name ?? "").Trim());
            if (text.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) text = text.Substring(0, text.Length - Extension.Length);
            var limit = MaximumNameLength - Extension.Length;
            if (text.Length > limit) text = text.Substring(0, char.IsHighSurrogate(text[limit - 1]) ? limit - 1 : limit);
            return text + Extension;
        }

        // Whether the phone lists a visible name. TransferName removes control and
        // format characters and the extension, so ".vrm" or a zero width space before ".vrm" leaves
        // nothing, and a name it empties entirely falls back to "avatar".
        internal static bool HasListedName(string name)
        {
            var text = name ?? "";
            var visible = false;
            for (var i = 0; i < text.Length && !visible; i++)
            {
                if (char.IsLowSurrogate(text[i])) continue;
                switch (CharUnicodeInfo.GetUnicodeCategory(text, i))
                {
                    case UnicodeCategory.Control:
                    case UnicodeCategory.Format:
                    case UnicodeCategory.Surrogate:
                    case UnicodeCategory.SpaceSeparator:
                    case UnicodeCategory.LineSeparator:
                    case UnicodeCategory.ParagraphSeparator:
                        continue;
                    default:
                        visible = true;
                        break;
                }
            }
            var listed = TransferName(name);
            return visible && !string.IsNullOrWhiteSpace(listed.Substring(0, listed.Length - Extension.Length));
        }

        // The link with the longest values a transfer can have and no secret.
        // It sizes the QR area before the upload; nothing draws it as a QR.
        internal static string PlaceholderLink(string transferName) => CloudTransferProtocol.ImportLink(new string('0', 32), new string('A', 43),
            transferName, CloudTransferProtocol.MaximumSize, new string('0', 64), 9999999999L, new string('A', 86));

        // Whether a QR can exist for this name: the link bound, then what one QR symbol holds.
        internal static bool NameFits(string name)
        {
            try { TransferQrCode.Encode(PlaceholderLink(TransferName(name))); return true; }
            catch (InvalidOperationException) { return false; }
            catch (WriterException) { return false; }
        }

        internal static string NewSnapshotPath() => TransferTempDirectory.NewFile("VRVlogCloudTransfers", Extension);
        internal static void Discard(string snapshotPath) => TransferTempDirectory.TryDelete(snapshotPath);

        // Writes what from reads to destination and returns the SHA-256 of the bytes written. The bytes go to
        // a temporary file next to destination first, so a failed copy leaves whatever was there before.
        // Bytes whose SHA-256 is not expected are not placed either: null, and destination is untouched.
        internal static string CopyTo(Stream from, string destination, string expected)
        {
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string hash;
                using (var sha = SHA256.Create())
                {
                    using (var to = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var hashing = new CryptoStream(to, sha, CryptoStreamMode.Write))
                        from.CopyTo(hashing);
                    hash = LanTransferProtocol.Hex(sha.Hash);
                }
                if (hash != expected) return null;
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                return hash;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        // The SHA-256 of a file, as CloudVrmTransferSource computes it, or null when it cannot be read.
        internal static string FileHashOf(string path)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create()) return LanTransferProtocol.Hex(sha.ComputeHash(file));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException
                || exception is NotSupportedException) { return null; }
        }

        internal AppTransferView View
        {
            get
            {
                if (exporting) return AppTransferView.Exporting;
                if (failure != null) return AppTransferView.Failed;
                if (session == null) return AppTransferView.Form;
                switch (session.State)
                {
                    case CloudTransferState.Preparing:
                    case CloudTransferState.Uploading: return AppTransferView.Uploading;
                    // The QR appears once Tick has seen the upload finish and fixed the bar's full length.
                    case CloudTransferState.Ready: return uploading == null && shownRemaining > 0 ? AppTransferView.Waiting : AppTransferView.Uploading;
                    case CloudTransferState.Completed: return AppTransferView.Completed;
                    case CloudTransferState.Canceled: return AppTransferView.Canceled;
                    case CloudTransferState.Expired: return AppTransferView.Expired;
                    default: return AppTransferView.Failed;
                }
            }
        }

        internal string Link => View == AppTransferView.Waiting ? session.ImportLink : null;
        internal bool Encrypting => session?.State == CloudTransferState.Preparing;
        internal long Transferred => session?.Transferred ?? 0;
        internal long UploadSize => session?.UploadSize ?? 0;
        internal bool StatusUnknown { get; private set; }
        // The export failed, as opposed to the upload: retrying exports again.
        internal bool ExportDidFail { get; private set; }
        // A new QR is a new session uploaded from the same exported snapshot. An ended
        // session may still be telling the service; that does not hold up the next one.
        internal bool CanRegenerate => source != null && !exporting && (session == null || session.Terminal || session.State == CloudTransferState.Ready);
        // The exported copy this transfer sends, kept until the flow stops (a new QR from another export,
        // another avatar, leaving the page or closing), so "VRMを保存" can save the very file sent, also
        // after the phone has it. Null while exporting and when there is none.
        internal string SnapshotHash => exporting ? null : source?.FileHash;
        internal FileStream OpenSnapshot() => exporting || source == null ? null : source.OpenRead();

        // Seconds until the service deadline. The deadline starts when the
        // transfer is created, so the upload has already used part of it.
        internal long Remaining => session == null || session.Terminal || session.ExpiresAt == 0 ? 0 : Math.Max(0, session.ExpiresAt - now());
        // The bar is full when the QR appears and empties toward the deadline.
        internal float RemainingFraction => View == AppTransferView.Waiting ? Math.Min(1f, (float)Remaining / shownRemaining) : 0f;

        internal string FailureText(Func<string, string> translate) =>
            failure != null ? failure.Length == 0 ? "" : translate(failure) : session != null ? session.DisplayMessage(translate) : "";

        internal void BeginExport() { Stop(); exporting = true; }
        internal void ExportCanceled() => exporting = false;
        internal void ExportFailed(string reason)
        {
            exporting = false; ExportDidFail = true;
            failure = reason ?? "";
        }

        // A failure the session does not know about, such as a QR that cannot be drawn.
        internal void Fail(string reason)
        {
            Stop();
            failure = reason ?? "";
        }

        // Adopts the exported snapshot: from here this flow deletes it.
        internal void Upload(string ownedSnapshotPath, string transferName)
        {
            Stop();
            try { source = new CloudVrmTransferSource(ownedSnapshotPath, transferName); }
            catch (InvalidOperationException) { failure = AppTransferText.SnapshotTooLarge; }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) { failure = AppTransferText.SnapshotUnreadable; }
            if (source == null) { Discard(ownedSnapshotPath); return; }
            StartSession();
        }

        internal void Regenerate()
        {
            if (CanRegenerate) StartSession();
        }

        private void StartSession()
        {
            session?.Dispose();
            Abandon(ref uploading); Abandon(ref polling);
            failure = null; StatusUnknown = false; shownRemaining = 0;
            session = createSession(source);
            // File I/O and the multipart upload run away from the Editor thread.
            var current = session;
            uploading = Task.Run(() => current.UploadAsync());
        }

        // Called from the Editor update with its clock. Expiry never waits for a status request.
        internal void Tick(double time)
        {
            session?.ExpireIfDue();
            if (uploading?.IsCompleted == true)
            {
                if (uploading.IsFaulted) _ = uploading.Exception;
                uploading = null;
                if (session?.State == CloudTransferState.Ready)
                {
                    if (session.ImportLink == null) { session.Dispose(); failure = AppTransferText.NeedsShorterName; }
                    else { shownRemaining = Math.Max(1, session.ExpiresAt - now()); nextPoll = time; }
                }
            }
            if (polling?.IsCompleted == true)
            {
                if (polling.IsFaulted) _ = polling.Exception;
                StatusUnknown = polling.IsFaulted && session?.Terminal != true;
                polling = null;
            }
            if (session?.State == CloudTransferState.Ready && uploading == null && polling == null && time >= nextPoll)
            {
                nextPoll = time + 5;
                polling = session.RefreshAsync();
            }
        }

        // Cancels a transfer in progress and deletes the exported snapshot.
        internal void Stop()
        {
            session?.Dispose(); session = null;
            Abandon(ref uploading); Abandon(ref polling);
            source?.Dispose(); source = null;
            exporting = false; failure = null; StatusUnknown = false; ExportDidFail = false; shownRemaining = 0;
        }

        // Observe abandoned task failures without logging transport details.
        private static void Abandon(ref Task task)
        {
            if (task != null) _ = task.ContinueWith(done => { _ = done.Exception; }, TaskScheduler.Default);
            task = null;
        }

        public void Dispose() => Stop();
    }
}
