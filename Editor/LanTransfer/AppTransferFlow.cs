using System;
using System.Collections.Generic;
using System.IO;
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
        internal const string Heading = "VR Vlogへ転送";
        internal const string Sending = "{0}を送る";
        internal const string Name = "名前";
        internal const string NameHint = "アプリのアバター一覧での名前";
        internal const string Terms = "規約";
        internal const string ReadTerms = "利用規約を読む";
        internal const string Create = "QRコードを作る";
        internal const string CreateHint = "スマホのカメラでQRを読み取るとVR Vlogが開いて取り込みが始まります";
        internal const string NeedsAvatar = "アバターを指定すると作れます";
        internal const string NeedsName = "名前を入れると作れます";
        internal const string NeedsShorterName = "名前を短くすると作れます";
        internal const string NeedsAuthor = "作者名を入れると作れます";
        internal const string NeedsConsent = "利用規約に同意すると作れます";
        internal const string NeedsPrivacyConsent = "プライバシーポリシーに同意すると作れます";
        internal const string Exporting = "VRMを書き出しています";
        internal const string ExportFailed = "書き出せませんでした";
        internal const string SnapshotTooLarge = "書き出したVRMが256MiBを超えています";
        internal const string SnapshotUnreadable = "書き出したVRMを開けません";
        internal const string ScanWithCamera = "スマホのカメラで読み取る";
        internal const string Waiting = "読み取り待ち";
        internal const string StatusUnknown = "状態を確認できません";
        internal const string Lifetime = "有効期限{0}分";
        internal const string Regenerate = "QRを作り直す";
        internal const string Retry = "もう一度";
        internal const string Expired = "期限が切れました";
        internal const string Completed = "取り込みが終わりました";
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
    }

    internal enum AppTransferView { Form, Exporting, Uploading, Waiting, Completed, Expired, Canceled, Failed }

    // The "アプリに転送" window without UnityEditor, so the host checks cover what
    // it shows and when. The window draws View and forwards the buttons.
    internal sealed class AppTransferFlow : IDisposable
    {
        // オーナーの確定待ち。法的な文言なので、確定するまで一字も変えず、翻訳もしない。
        internal const string ConsentText = "このアバターを VR Vlog へ転送して利用することに同意します（利用規約）";
        // オーナーの確定待ち。VR Vlogアプリが利用規約として開くページと同じ。
        internal const string TermsUrl = "https://vrvlog.fun/terms/";
        internal const string Extension = ".vrm";
        private const int MaximumNameLength = 256;

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
        internal static string BlockReason(bool hasAvatar, string name, bool nameFits, bool hasAuthor, bool consented, bool privacyAccepted) =>
            !hasAvatar ? AppTransferText.NeedsAvatar
            : string.IsNullOrWhiteSpace(name) ? AppTransferText.NeedsName
            : !nameFits ? AppTransferText.NeedsShorterName
            : !hasAuthor ? AppTransferText.NeedsAuthor
            : !consented ? AppTransferText.NeedsConsent
            : privacyAccepted ? null : AppTransferText.NeedsPrivacyConsent;

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

        internal static string NewSnapshotPath() => Path.Combine(Path.GetTempPath(), "VRVlogCloudTransfers", Guid.NewGuid().ToString("N") + Extension);
        internal static void Discard(string snapshotPath) => LanVrmTransferServer.TryDelete(snapshotPath);

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
            // The phone has the avatar: the exported copy is no longer needed.
            if (session?.State == CloudTransferState.Completed) { source?.Dispose(); source = null; }
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
