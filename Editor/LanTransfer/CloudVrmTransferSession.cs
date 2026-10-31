using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace VRVlog.LilToonExporter.LanTransfer
{
    internal sealed class CloudTransferResponse
    {
        internal int StatusCode { get; }
        internal string Body { get; }
        internal TimeSpan? RetryAfter { get; }
        internal CloudTransferResponse(int statusCode, string body, TimeSpan? retryAfter = null)
        { StatusCode = statusCode; Body = body; RetryAfter = retryAfter; }
    }

    internal interface ICloudTransferTransport
    {
        Task<CloudTransferResponse> SendAsync(string method, string path, string token, byte[] body, string contentType, CancellationToken cancellation, int timeoutSeconds = 90);
    }

    internal sealed class CloudTransferTransport : ICloudTransferTransport
    {
        private static readonly HttpClient Client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        public async Task<CloudTransferResponse> SendAsync(string method, string path, string token, byte[] body, string contentType, CancellationToken cancellation, int timeoutSeconds = 90)
        {
            if (timeoutSeconds < 1 || timeoutSeconds > CloudTransferProtocol.UploadPartTimeoutSeconds) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            // Every production request goes to the fixed service using the OS
            // certificate trust. Redirects and QR-supplied hosts are forbidden.
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (var request = new HttpRequestMessage(new HttpMethod(method), CloudTransferProtocol.ServiceUrl + path))
            {
                // ResponseHeadersRead does not extend HttpClient.Timeout to
                // body reads. Bound the entire exchange, including the body.
                deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
                if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (body != null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                    request.Content.Headers.ContentLength = body.Length;
                }
                using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[1024]; int count;
                    while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) != 0)
                    {
                        if (output.Length + count > 4096) throw CloudTransferProtocol.Invalid();
                        output.Write(buffer, 0, count);
                    }
                    var after = response.Headers.RetryAfter;
                    var retryAfter = after?.Delta ?? (after?.Date.HasValue == true ? after.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
                    return new CloudTransferResponse((int)response.StatusCode, new UTF8Encoding(false, true).GetString(output.ToArray()), retryAfter);
                }
            }
        }
    }

    // This is only the explicitly generated transfer snapshot, never an
    // ordinary user-selected export. Sessions borrow it; closing the window
    // releases the immutable master and deletes it, including after retries.
    internal sealed class CloudVrmTransferSource : IDisposable
    {
        private readonly object gate = new object();
        private readonly List<FileStream> readers = new List<FileStream>();
        private readonly string path;
        private FileStream master;
        internal string Name { get; }
        internal long Size { get; }
        internal string FileHash { get; }
        internal CloudVrmTransferSource(string ownedSnapshotPath, string name)
        {
            path = ownedSnapshotPath;
            Name = CloudTransferProtocol.DisplayName(name);
            try
            {
                master = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                Size = master.Length;
                if (Size < 1 || Size > CloudTransferProtocol.MaximumSize) throw new InvalidOperationException("スマホへの転送は256 MiB以下のVRMに対応しています。");
                using (var hash = SHA256.Create()) FileHash = LanTransferProtocol.Hex(hash.ComputeHash(master));
                master.Position = 0;
            }
            catch { Dispose(); throw; }
        }
        internal FileStream OpenRead()
        {
            lock (gate)
            {
                if (master == null) throw new ObjectDisposedException(nameof(CloudVrmTransferSource));
                // Readers already closed (an upload that ended, a saved copy) need nothing more.
                readers.RemoveAll(closed => !closed.CanRead);
                var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
                readers.Add(reader);
                return reader;
            }
        }
        public void Dispose()
        {
            lock (gate)
            {
                // A close/reload can race with startup before the session has
                // registered its active reader. Close every borrowed handle.
                foreach (var reader in readers) reader.Dispose();
                readers.Clear(); master?.Dispose(); master = null;
                TransferTempDirectory.TryDelete(path);
            }
        }
    }

    internal enum CloudTransferState { Preparing, Uploading, Ready, Completed, Canceled, Expired, Failed }

    internal sealed class CloudVrmTransferSession : IDisposable
    {
        private readonly object gate = new object();
        private readonly CloudVrmTransferSource source;
        private readonly ICloudTransferTransport transport;
        private readonly Func<long> now;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private string id, readToken, uploadToken;
        private FileStream activeReader;
        private CloudEncryptedSnapshot encrypted;
        private CloudTransferState state = CloudTransferState.Preparing;
        private string qr, importLink, failureStage;
        private SafeTransferFailure failure;
        private long transferred, expiresAt, uploadSize;
        private bool started, disposed, extendedUpload, confirmingPublication;
        private long uploadDeadline;
        internal CloudTransferState State { get { lock (gate) return state; } }
        internal string Qr { get { lock (gate) return qr; } }
        // vrvlog://import#... for the phone's own camera. The in-app scanner keeps reading Qr.
        internal string ImportLink { get { lock (gate) return importLink; } }
        internal long Transferred { get { lock (gate) return transferred; } }
        internal long UploadSize { get { lock (gate) return uploadSize; } }
        internal bool HasEncryptionKey { get { lock (gate) return encrypted?.HasKey == true; } }
        internal long ExpiresAt { get { lock (gate) return expiresAt; } }
        internal bool Terminal => State >= CloudTransferState.Completed;
        internal string Message => State == CloudTransferState.Preparing ? "転送用のアバターを暗号化しています…"
            : State == CloudTransferState.Uploading ? "暗号化したアバターを一時アップロードしています。"
            : State == CloudTransferState.Ready ? "スマホのVR Vlogで「PCから受け取る」を開き、QRを読み取ってください。"
            : State == CloudTransferState.Completed ? "スマホへの保存が完了しました。クラウドの転送用コピーは削除対象になりました。"
            : State == CloudTransferState.Canceled ? "転送を中止しました。"
            : State == CloudTransferState.Expired ? "受取期限が切れました。新しいQRを作成できます。"
            : FailureMessage;
        private string FailureMessage { get { lock (gate) return failure == null
            ? "転送サービスでエラーが発生しました。新しいQRを作成してください。"
            : failureStage + "で停止しました。\n" + failure.Message; } }

        // Called by the window on the Editor thread. Upload tasks never access
        // AssetDatabase-backed localization or expose uncontrolled exception text.
        internal string DisplayMessage(Func<string, string> translate)
        {
            lock (gate)
            {
                if (state == CloudTransferState.Failed && failure != null)
                    return string.Format(translate("{0}で停止しました。\n{1}"), translate(failureStage),
                        string.Format(translate(failure.Reason), failure.StatusCode));
                return translate(Message);
            }
        }

        internal CloudVrmTransferSession(CloudVrmTransferSource source, ICloudTransferTransport transport = null, Func<long> now = null, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.source = source ?? throw new ArgumentNullException(nameof(source));
            this.transport = transport ?? new CloudTransferTransport();
            this.now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            this.delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        }

        internal async Task UploadAsync(CancellationToken cancellation = default)
        {
            lock (gate) { if (started || disposed) throw new InvalidOperationException("転送は開始済みです。"); started = true; }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation))
            {
                var ct = linked.Token;
                var stage = "暗号化の準備";
                try
                {
                    var protectedSnapshot = await CloudEncryptedSnapshot.CreateAsync(source, ct).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (ct.IsCancellationRequested || disposed) { protectedSnapshot.Dispose(); ct.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(CloudVrmTransferSession)); }
                        encrypted = protectedSnapshot; uploadSize = protectedSnapshot.Size;
                    }
                    stage = "転送の作成";
                    extendedUpload = await SupportsExtendedUpload(ct).ConfigureAwait(false);
                    var fields = await Send("POST", extendedUpload ? "/v3/uploads" : "/v3/transfers", null, Encoding.UTF8.GetBytes(CloudTransferProtocol.CreateBody(CloudEncryptedSnapshot.WireName, protectedSnapshot.Size, protectedSnapshot.FileHash)), "application/json", ct).ConfigureAwait(false);
                    fields.Exact("v", "id", "token", "uploadToken", "expiresAt", "partSize");
                    var newId = fields.Text("id"); var token = fields.Text("token"); var owner = fields.Text("uploadToken"); var expiry = fields.Number("expiresAt");
                    var currentTime = now();
                    if (!CloudTransferProtocol.IsHex(newId, 32) || !CloudTransferProtocol.IsToken(owner)) throw CloudTransferProtocol.Invalid();
                    // A valid create response has already reserved server storage.
                    // Retain authenticated cleanup credentials even when its
                    // lifetime is rejected before any VRM bytes are uploaded.
                    lock (gate) { id = newId; uploadToken = owner; }
                    if (fields.Number("v") != 3 || !CloudTransferProtocol.IsToken(token) || token == owner || fields.Number("partSize") != CloudTransferProtocol.PartSize) throw CloudTransferProtocol.Invalid();
                    if (expiry <= currentTime || expiry > currentTime + (extendedUpload ? CloudTransferProtocol.UploadLifetimeSeconds + 60 : (CloudTransferProtocol.LifetimeMinutes + 1) * 60)) throw new SafeTransferFailure("PCの日時が転送サービスと合っていません。OSの日時の自動設定を確認してください。");
                    lock (gate) { readToken = token; expiresAt = expiry; uploadDeadline = expiry; if (!disposed && state != CloudTransferState.Canceled) state = CloudTransferState.Uploading; }
                    ct.ThrowIfCancellationRequested();
                    stage = "暗号化ファイルの読み取り";
                    using (var reader = protectedSnapshot.OpenRead())
                    using (var hash = SHA256.Create())
                    {
                        lock (gate) { ct.ThrowIfCancellationRequested(); activeReader = reader; }
                        var remaining = protectedSnapshot.Size; var partNumber = 1;
                        while (remaining > 0)
                        {
                            ct.ThrowIfCancellationRequested(); CheckExpiry();
                            var part = new byte[(int)Math.Min(CloudTransferProtocol.PartSize, remaining)]; var offset = 0;
                            while (offset < part.Length)
                            {
                                var count = await reader.ReadAsync(part, offset, part.Length - offset, ct).ConfigureAwait(false);
                                if (count == 0) throw CloudTransferProtocol.Invalid(); offset += count;
                            }
                            hash.TransformBlock(part, 0, part.Length, null, 0);
                            stage = "暗号化ファイルのアップロード";
                            await Send("PUT", Path + "/parts/" + partNumber.ToString(CultureInfo.InvariantCulture), uploadToken, part, "application/octet-stream", ct, false, retry: true).ConfigureAwait(false);
                            lock (gate) transferred += part.Length;
                            remaining -= part.Length; partNumber++;
                        }
                        hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                        if (reader.ReadByte() != -1 || LanTransferProtocol.Hex(hash.Hash) != protectedSnapshot.FileHash) throw CloudTransferProtocol.Invalid();
                    }
                    lock (gate) activeReader = null;
                    CheckExpiry();
                    stage = "QRの発行";
                    lock (gate)
                    {
                        ct.ThrowIfCancellationRequested();
                        CheckExpiry();
                        // Only publication started within the upload deadline
                        // may confirm a durable receipt until the absolute bound.
                        confirmingPublication = extendedUpload;
                    }
                    var published = await Send("POST", Path + "/publish", uploadToken, Array.Empty<byte>(), "application/octet-stream", ct, retry: true).ConfigureAwait(false);
                    published.Exact("v", "id", "token", "name", "size", "sha256", "expiresAt");
                    if (published.Number("v") != 3 || published.Text("id") != id || published.Text("token") != readToken || published.Text("name") != CloudEncryptedSnapshot.WireName
                        || published.Number("size") != protectedSnapshot.Size || published.Text("sha256") != protectedSnapshot.FileHash) throw CloudTransferProtocol.Invalid();
                    CheckExpiry(); // Never expose a receipt beyond the finite confirmation bound.
                    var receiveExpiry = published.Number("expiresAt");
                    if (extendedUpload)
                    {
                        var current = now();
                        if (receiveExpiry <= current || receiveExpiry > current + (CloudTransferProtocol.LifetimeMinutes + 1) * 60
                            || receiveExpiry > uploadDeadline + CloudTransferProtocol.LifetimeMinutes * 60) throw CloudTransferProtocol.Invalid();
                    }
                    else if (receiveExpiry != expiresAt) throw CloudTransferProtocol.Invalid();
                    lock (gate)
                    {
                        ct.ThrowIfCancellationRequested();
                        expiresAt = receiveExpiry; confirmingPublication = false;
                        var key = protectedSnapshot.KeyForQr();
                        qr = CloudTransferProtocol.Qr(id, readToken, source.Name, source.Size, source.FileHash, expiresAt, key);
                        // The link is an additional representation. A name it cannot carry
                        // leaves it null and never fails the QR the in-app scanner reads.
                        try { importLink = CloudTransferProtocol.ImportLink(id, readToken, source.Name, source.Size, source.FileHash, expiresAt, key); }
                        catch (InvalidOperationException) { importLink = null; }
                        state = CloudTransferState.Ready;
                        protectedSnapshot.ReleaseFile();
                    }
                }
                catch (Exception exception)
                {
                    lock (gate) { failureStage = stage; failure = SafeFailure(exception); }
                    lock (gate) { activeReader = null; qr = null; importLink = null; encrypted?.Dispose(); encrypted = null; if (state != CloudTransferState.Canceled && state != CloudTransferState.Expired) state = CloudTransferState.Failed; }
                    await DeleteRemote().ConfigureAwait(false);
                    throw new InvalidOperationException(Message); // Never disclose service bodies, credentials or paths.
                }
            }
        }

        internal async Task RefreshAsync(CancellationToken cancellation = default)
        {
            if (State != CloudTransferState.Ready) return;
            CheckExpiry();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation))
            {
                var fields = await Send("GET", Path, uploadToken, null, null, linked.Token, diagnose: false).ConfigureAwait(false);
                var remote = fields.Text("state");
                lock (gate)
                {
                    if (state != CloudTransferState.Ready) return;
                    switch (remote)
                    {
                        case "ready": return;
                        case "completed": state = CloudTransferState.Completed; break;
                        case "cancelled": state = CloudTransferState.Canceled; break;
                        case "expired": state = CloudTransferState.Expired; break;
                        case "failed": state = CloudTransferState.Failed; break;
                        default: throw CloudTransferProtocol.Invalid();
                    }
                    qr = null; importLink = null; encrypted?.Dispose(); encrypted = null;
                }
            }
        }
        private string Path => "/v3/transfers/" + id;
        internal bool ExpireIfDue()
        {
            lock (gate)
            {
                if (state >= CloudTransferState.Completed || expiresAt == 0 || now() < OperationDeadline) return false;
                state = CloudTransferState.Expired; qr = null; importLink = null;
                stop.Cancel(); activeReader?.Dispose(); activeReader = null;
                encrypted?.Dispose(); encrypted = null;
                return true;
            }
        }
        private long OperationDeadline
        {
            get { lock (gate) return confirmingPublication && extendedUpload
                ? uploadDeadline + CloudTransferProtocol.LifetimeMinutes * 60 : expiresAt; }
        }
        private void CheckExpiry()
        {
            ExpireIfDue();
            if (State == CloudTransferState.Expired) throw new InvalidOperationException(Message);
        }
        private async Task<bool> SupportsExtendedUpload(CancellationToken ct)
        {
            // Read-only capability negotiation precedes the sole create POST.
            // A missing endpoint is legacy; never replay create on another route.
            var response = await transport.SendAsync("GET", "/v3/capabilities", null, null, null, ct, 10).ConfigureAwait(false);
            if (response.StatusCode == 404) return false;
            if (response.StatusCode != 200) throw new SafeTransferFailure("転送サービスが応答できませんでした（HTTP {0}）。時間を置いて新しいQRを作成してください。", response.StatusCode);
            var fields = CloudTransferProtocol.Fields.Read(response.Body);
            fields.Exact("v", "uploadLifetimeSeconds");
            if (fields.Number("v") != 3) throw CloudTransferProtocol.Invalid();
            var seconds = fields.Number("uploadLifetimeSeconds");
            if (seconds == 0) return false;
            if (seconds != CloudTransferProtocol.UploadLifetimeSeconds) throw CloudTransferProtocol.Invalid();
            return true;
        }

        private async Task<CloudTransferProtocol.Fields> Send(string method, string path, string token, byte[] body, string type, CancellationToken ct, bool parse = true, bool diagnose = true, bool retry = false)
        {
            CloudTransferResponse response = null;
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (retry) CheckExpiry();
                response = null;
                try
                {
                    var seconds = extendedUpload && method == "PUT" ? CloudTransferProtocol.UploadPartTimeoutSeconds : 90;
                    var expiry = OperationDeadline;
                    if (expiry > 0) seconds = (int)Math.Min(seconds, Math.Max(1, expiry - now()));
                    response = await transport.SendAsync(method, path, token, body, type, ct, seconds).ConfigureAwait(false);
                    if (!retry || attempt >= 2 || !TransientStatus(response.StatusCode)) break;
                }
                catch (HttpRequestException) when (retry && attempt < 2 && !ct.IsCancellationRequested) { }
                catch (OperationCanceledException) when (retry && attempt < 2 && !ct.IsCancellationRequested) { }
                // Only authenticated part PUT and publish opt in. Their server
                // routes replay committed results after a lost response. Never
                // retry create, extend expiry, or exceed the server's 3 attempts.
                ct.ThrowIfCancellationRequested();
                CheckExpiry();
                var wait = TimeSpan.FromSeconds(attempt + 1);
                if (response?.RetryAfter > wait) wait = response.RetryAfter.Value;
                var remaining = TimeSpan.FromSeconds(Math.Max(0, OperationDeadline - now()));
                // A server's requested cooldown must not be shortened into a
                // premature retry or extend the transfer's original lifetime.
                if (wait >= remaining && response != null) break;
                await delay(wait < remaining ? wait : remaining, ct).ConfigureAwait(false);
            }
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                if (!diagnose) throw CloudTransferProtocol.Invalid();
                throw new SafeTransferFailure(response.StatusCode == 429
                    ? "転送サービスの利用上限または混雑により送信できません（HTTP 429）。時間を置いて試してください。"
                    : response.StatusCode == 413
                    ? "転送サイズがサービスの上限を超えています（HTTP 413）。サイズを減らしてください。"
                    : response.StatusCode == 401 || response.StatusCode == 403
                    ? "転送の認証が拒否されました（HTTP {0}）。新しいQRを作成してください。"
                    : "転送サービスが応答できませんでした（HTTP {0}）。時間を置いて新しいQRを作成してください。", response.StatusCode);
            }
            return parse ? CloudTransferProtocol.Fields.Read(response.Body) : null;
        }
        private static bool TransientStatus(int status) => status == 408 || status == 500 || status == 502 || status == 503 || status == 504;

        // Only controlled text and a status code reach the UI. Exception messages,
        // paths, server bodies, QR contents and credentials never do.
        private sealed class SafeTransferFailure : Exception
        {
            internal readonly string Reason;
            internal readonly int StatusCode;
            internal SafeTransferFailure(string reason, int statusCode = 0) : base(string.Format(reason, statusCode))
            { Reason = reason; StatusCode = statusCode; }
        }
        private static SafeTransferFailure SafeFailure(Exception exception)
        {
            if (exception is SafeTransferFailure safe) return safe;
            var reason = exception is UnauthorizedAccessException ? "一時ファイルへのアクセスが拒否されました。OSのアクセス権を確認してください。"
                : exception is InvalidDataException ? "転送サービスの応答を確認できませんでした。最新版の試験用パッケージで再試行してください。"
                : exception is IOException ? "一時ファイルを読み書きできませんでした。空き容量やファイルを使用中のソフトを確認してください。"
                : exception is HttpRequestException ? "転送サービスに接続できません。インターネット接続・VPN・プロキシを確認してください。"
                : exception is OperationCanceledException ? "通信が中断または時間切れになりました。接続を確認して新しいQRを作成してください。"
                : "転送データを確認できませんでした。最新版の試験用パッケージで再試行してください。";
            return new SafeTransferFailure(reason);
        }

        private async Task DeleteRemote()
        {
            string transferId, owner;
            lock (gate) { transferId = id; owner = uploadToken; }
            if (transferId == null || owner == null) return;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await transport.SendAsync("POST", "/v3/transfers/" + transferId + "/cancel", owner, Array.Empty<byte>(), "application/octet-stream", timeout.Token).ConfigureAwait(false);
            }
            catch { /* The fixed server expiry also cleans up when notification cannot reach it. */ }
        }
        internal async Task CancelAsync()
        {
            lock (gate) { if (Terminal) return; state = CloudTransferState.Canceled; qr = null; importLink = null; stop.Cancel(); activeReader?.Dispose(); activeReader = null; encrypted?.Dispose(); encrypted = null; }
            await DeleteRemote().ConfigureAwait(false);
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; stop.Cancel(); activeReader?.Dispose(); activeReader = null; if (!Terminal) state = CloudTransferState.Canceled; qr = null; importLink = null; encrypted?.Dispose(); encrypted = null; }
            _ = DeleteRemote();
        }
    }
}
