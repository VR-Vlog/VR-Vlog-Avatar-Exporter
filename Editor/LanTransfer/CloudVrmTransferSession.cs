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
                if (Size < 1 || Size > CloudTransferProtocol.MaximumSize) throw new InvalidOperationException("スマホへの転送は256MB以下のVRMに対応しています。");
                using (var hash = SHA256.Create()) FileHash = CloudTransferProtocol.Hex(hash.ComputeHash(master));
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
        // One file sent as one service transfer. The normal file is always one;
        // the lightweight file, when asked for, is a second transfer with its own
        // key and tokens that the session drops without stopping the normal one.
        private sealed class Leg
        {
            internal readonly CloudVrmTransferSource Source;
            internal CloudEncryptedSnapshot Encrypted;
            internal string Id, ReadToken, UploadToken;
            internal FileStream Reader;
            internal long Transferred;
            internal Leg(CloudVrmTransferSource source) { Source = source; }
            internal string Path => "/v3/transfers/" + Id;
        }

        // The lightweight upload ends this long before the normal file's upload
        // deadline, so both publications still start within it.
        internal const int LightPublicationReserveSeconds = 60;

        private readonly object gate = new object();
        private readonly Leg normal;
        private Leg light;
        private readonly bool lightRequested;
        private readonly ICloudTransferTransport transport;
        private readonly Func<long> now;
        private readonly Func<TimeSpan, CancellationToken, Task> delay;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private CloudTransferState state = CloudTransferState.Preparing;
        private string qr, importLink, failureStage, stage;
        private SafeTransferFailure failure;
        private long expiresAt, uploadSize;
        private bool started, disposed, extendedUpload, confirmingPublication, lightDropped;
        private long uploadDeadline;
        internal CloudTransferState State { get { lock (gate) return state; } }
        internal string Qr { get { lock (gate) return qr; } }
        // vrvlog://import#... for the phone's own camera. The in-app scanner keeps reading Qr.
        // With the lightweight file it is version 4; Qr always carries the normal file only.
        internal string ImportLink { get { lock (gate) return importLink; } }
        // Both files together, so the progress is the whole of what is sent.
        internal long Transferred { get { lock (gate) return normal.Transferred + (light?.Transferred ?? 0); } }
        internal long UploadSize { get { lock (gate) return uploadSize; } }
        internal bool HasEncryptionKey { get { lock (gate) return normal.Encrypted?.HasKey == true; } }
        internal long ExpiresAt { get { lock (gate) return expiresAt; } }
        internal bool LightRequested => lightRequested;
        // The lightweight file was asked for and is no longer sent: the normal file goes alone.
        internal bool LightFailed { get { lock (gate) return lightDropped; } }
        internal bool Terminal => State >= CloudTransferState.Completed;
        internal string Message => State == CloudTransferState.Preparing ? "転送用のアバターを暗号化しています…"
            : State == CloudTransferState.Uploading ? "暗号化したアバターを一時アップロードしています。"
            : State == CloudTransferState.Ready ? "スマホのVR Vlogで「PCから受け取る」を開き、QRを読み取ってください。"
            : State == CloudTransferState.Completed ? "スマホへの保存が完了しました。クラウドの転送用コピーは削除対象になりました。"
            : State == CloudTransferState.Canceled ? "転送を中止しました。"
            : State == CloudTransferState.Expired ? "受取期限が切れました。新しいQRを作成できます。"
            : FailureMessage;
        private string FailureMessage { get { lock (gate) return failure == null
            ? "転送サービスでエラーが発生しました\n新しいQRを作る"
            : failureStage + "で停止しました\n" + failure.Message; } }

        // Called by the window on the Editor thread. Upload tasks never access
        // AssetDatabase-backed localization or expose uncontrolled exception text.
        internal string DisplayMessage(Func<string, string> translate)
        {
            lock (gate)
            {
                if (state == CloudTransferState.Failed && failure != null)
                    return string.Format(translate("{0}で停止しました\n{1}"), translate(failureStage),
                        string.Format(translate(failure.Reason), failure.StatusCode));
                return translate(Message);
            }
        }

        // The session borrows lightSource as it borrows source; the caller disposes both.
        internal CloudVrmTransferSession(CloudVrmTransferSource source, ICloudTransferTransport transport = null, Func<long> now = null, Func<TimeSpan, CancellationToken, Task> delay = null, CloudVrmTransferSource lightSource = null)
        {
            normal = new Leg(source ?? throw new ArgumentNullException(nameof(source)));
            if (lightSource != null) { light = new Leg(lightSource); lightRequested = true; }
            this.transport = transport ?? new CloudTransferTransport();
            this.now = now ?? (() => DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            this.delay = delay ?? ((duration, token) => Task.Delay(duration, token));
        }

        // The normal file is created and uploaded first, then the lightweight one;
        // both are published back to back and the link ends with the earlier receipt.
        internal async Task UploadAsync(CancellationToken cancellation = default)
        {
            lock (gate) { if (started || disposed) throw new InvalidOperationException("転送は開始済みです。"); started = true; }
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation))
            {
                var ct = linked.Token;
                stage = "暗号化の準備";
                try
                {
                    var protectedSnapshot = await CloudEncryptedSnapshot.CreateAsync(normal.Source, ct).ConfigureAwait(false);
                    lock (gate)
                    {
                        if (ct.IsCancellationRequested || disposed) { protectedSnapshot.Dispose(); ct.ThrowIfCancellationRequested(); throw new ObjectDisposedException(nameof(CloudVrmTransferSession)); }
                        normal.Encrypted = protectedSnapshot; uploadSize = protectedSnapshot.Size;
                    }
                    await Light(ct, async leg =>
                    {
                        var lightSnapshot = await CloudEncryptedSnapshot.CreateAsync(leg.Source, ct).ConfigureAwait(false);
                        lock (gate)
                        {
                            if (light != leg || disposed) { lightSnapshot.Dispose(); throw new ObjectDisposedException(nameof(CloudVrmTransferSession)); }
                            leg.Encrypted = lightSnapshot; uploadSize += lightSnapshot.Size;
                        }
                    }).ConfigureAwait(false);
                    stage = "転送の作成";
                    extendedUpload = await SupportsExtendedUpload(ct).ConfigureAwait(false);
                    // A service without the longer upload lifetime gives each transfer
                    // three minutes from its creation, which two files cannot share.
                    if (!extendedUpload) await DropLight().ConfigureAwait(false);
                    var expiry = await Create(normal, ct).ConfigureAwait(false);
                    lock (gate) { expiresAt = expiry; uploadDeadline = expiry; if (!disposed && state != CloudTransferState.Canceled) state = CloudTransferState.Uploading; }
                    ct.ThrowIfCancellationRequested();
                    stage = "暗号化ファイルの読み取り";
                    await UploadParts(normal, ct, 0).ConfigureAwait(false);
                    long lightDeadline = 0;
                    await Light(ct, async leg =>
                    {
                        var cutoff = uploadDeadline - LightPublicationReserveSeconds;
                        if (now() >= cutoff) throw new TimeoutException();
                        lightDeadline = await Create(leg, ct, cutoff).ConfigureAwait(false);
                        await UploadParts(leg, ct, cutoff).ConfigureAwait(false);
                    }).ConfigureAwait(false);
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
                    var receiveExpiry = await Publish(normal, ct, uploadDeadline).ConfigureAwait(false);
                    if (!extendedUpload && receiveExpiry != expiresAt) throw CloudTransferProtocol.Invalid();
                    lock (gate) { ct.ThrowIfCancellationRequested(); expiresAt = receiveExpiry; confirmingPublication = false; }
                    // Right after the normal file, so the two receipts end close together.
                    await Light(ct, async leg =>
                    {
                        var lightExpiry = await Publish(leg, ct, lightDeadline).ConfigureAwait(false);
                        lock (gate) expiresAt = Math.Min(expiresAt, lightExpiry);
                    }).ConfigureAwait(false);
                    var linkWithoutLight = false;
                    lock (gate)
                    {
                        ct.ThrowIfCancellationRequested();
                        var key = protectedSnapshot.KeyForQr();
                        var source = normal.Source;
                        qr = CloudTransferProtocol.Qr(normal.Id, normal.ReadToken, source.Name, source.Size, source.FileHash, expiresAt, key);
                        // The link is an additional representation. A name it cannot carry
                        // leaves it null and never fails the QR the in-app scanner reads.
                        importLink = null;
                        if (light != null)
                        {
                            try { importLink = CloudTransferProtocol.ImportLink(normal.Id, normal.ReadToken, source.Name, source.Size, source.FileHash, expiresAt, key, light.Id, light.ReadToken, light.Source.Size, light.Source.FileHash, light.Encrypted.KeyForQr()); }
                            catch (InvalidOperationException) { linkWithoutLight = true; }
                        }
                        if (importLink == null)
                        {
                            try { importLink = CloudTransferProtocol.ImportLink(normal.Id, normal.ReadToken, source.Name, source.Size, source.FileHash, expiresAt, key); }
                            catch (InvalidOperationException) { importLink = null; }
                        }
                        state = CloudTransferState.Ready;
                        protectedSnapshot.ReleaseFile();
                        if (!linkWithoutLight) light?.Encrypted.ReleaseFile();
                    }
                    if (linkWithoutLight) await DropLight().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    lock (gate) { failureStage = stage; failure = SafeFailure(exception); }
                    lock (gate) { qr = null; importLink = null; ForgetFiles(); if (state != CloudTransferState.Canceled && state != CloudTransferState.Expired) state = CloudTransferState.Failed; }
                    await DeleteRemotes().ConfigureAwait(false);
                    throw new InvalidOperationException(Message); // Never disclose service bodies, credentials or paths.
                }
            }
        }

        // Runs one step of the lightweight file. Whatever stops that step and not
        // the session drops the lightweight file and leaves the normal one going.
        private async Task Light(CancellationToken ct, Func<Leg, Task> step)
        {
            Leg leg;
            lock (gate) leg = light;
            if (leg == null) return;
            try { await step(leg).ConfigureAwait(false); }
            catch (Exception) when (!ct.IsCancellationRequested && !SessionStopped) { await DropLight().ConfigureAwait(false); }
        }
        private bool SessionStopped
        {
            get { lock (gate) return disposed || state == CloudTransferState.Canceled || state == CloudTransferState.Expired || state == CloudTransferState.Failed; }
        }
        private async Task DropLight()
        {
            Leg dropped;
            lock (gate)
            {
                dropped = light;
                if (dropped == null) return;
                light = null; lightDropped = true;
                dropped.Reader?.Dispose(); dropped.Reader = null;
                if (dropped.Encrypted != null) { uploadSize -= dropped.Encrypted.Size; dropped.Encrypted.Dispose(); dropped.Encrypted = null; }
            }
            await DeleteRemote(dropped).ConfigureAwait(false);
        }

        // Returns the upload deadline the service gave the new transfer.
        private async Task<long> Create(Leg leg, CancellationToken ct, long limit = 0)
        {
            var snapshot = leg.Encrypted;
            var fields = await Send("POST", extendedUpload ? "/v3/uploads" : "/v3/transfers", null, Encoding.UTF8.GetBytes(CloudTransferProtocol.CreateBody(CloudEncryptedSnapshot.WireName, snapshot.Size, snapshot.FileHash)), "application/json", ct, limit: limit).ConfigureAwait(false);
            fields.Exact("v", "id", "token", "uploadToken", "expiresAt", "partSize");
            var newId = fields.Text("id"); var token = fields.Text("token"); var owner = fields.Text("uploadToken"); var expiry = fields.Number("expiresAt");
            var currentTime = now();
            if (!CloudTransferProtocol.IsHex(newId, 32) || !CloudTransferProtocol.IsToken(owner)) throw CloudTransferProtocol.Invalid();
            // A valid create response has already reserved server storage.
            // Retain authenticated cleanup credentials even when its
            // lifetime is rejected before any VRM bytes are uploaded.
            lock (gate) { leg.Id = newId; leg.UploadToken = owner; }
            if (fields.Number("v") != 3 || !CloudTransferProtocol.IsToken(token) || token == owner || fields.Number("partSize") != CloudTransferProtocol.PartSize) throw CloudTransferProtocol.Invalid();
            // The second transfer is a transfer of its own, never the first one again.
            if (leg != normal && (newId == normal.Id || token == normal.ReadToken || owner == normal.UploadToken)) throw CloudTransferProtocol.Invalid();
            if (expiry <= currentTime || expiry > currentTime + (extendedUpload ? CloudTransferProtocol.UploadLifetimeSeconds + 60 : (CloudTransferProtocol.LifetimeMinutes + 1) * 60)) throw new SafeTransferFailure("PCの日時が転送サービスと合っていません\nOSの日時の自動設定を確認する");
            lock (gate) leg.ReadToken = token;
            return expiry;
        }

        private async Task UploadParts(Leg leg, CancellationToken ct, long limit)
        {
            var snapshot = leg.Encrypted;
            using (var reader = snapshot.OpenRead())
            using (var hash = SHA256.Create())
            {
                lock (gate) { ct.ThrowIfCancellationRequested(); if (leg.Encrypted == null) throw new ObjectDisposedException(nameof(CloudVrmTransferSession)); leg.Reader = reader; }
                var remaining = snapshot.Size; var partNumber = 1;
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
                    if (leg == normal) stage = "暗号化ファイルのアップロード";
                    await Send("PUT", leg.Path + "/parts/" + partNumber.ToString(CultureInfo.InvariantCulture), leg.UploadToken, part, "application/octet-stream", ct, false, retry: true, limit: limit).ConfigureAwait(false);
                    lock (gate) leg.Transferred += part.Length;
                    remaining -= part.Length; partNumber++;
                }
                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                if (reader.ReadByte() != -1 || CloudTransferProtocol.Hex(hash.Hash) != snapshot.FileHash) throw CloudTransferProtocol.Invalid();
            }
            lock (gate) leg.Reader = null;
        }

        // Returns until when the phone may receive the transfer.
        private async Task<long> Publish(Leg leg, CancellationToken ct, long legUploadDeadline)
        {
            var snapshot = leg.Encrypted;
            var published = await Send("POST", leg.Path + "/publish", leg.UploadToken, Array.Empty<byte>(), "application/octet-stream", ct, retry: true).ConfigureAwait(false);
            published.Exact("v", "id", "token", "name", "size", "sha256", "expiresAt");
            if (published.Number("v") != 3 || published.Text("id") != leg.Id || published.Text("token") != leg.ReadToken || published.Text("name") != CloudEncryptedSnapshot.WireName
                || published.Number("size") != snapshot.Size || published.Text("sha256") != snapshot.FileHash) throw CloudTransferProtocol.Invalid();
            CheckExpiry(); // Never expose a receipt beyond the finite confirmation bound.
            var receiveExpiry = published.Number("expiresAt");
            if (extendedUpload)
            {
                var current = now();
                if (receiveExpiry <= current || receiveExpiry > current + (CloudTransferProtocol.LifetimeMinutes + 1) * 60
                    || receiveExpiry > legUploadDeadline + CloudTransferProtocol.LifetimeMinutes * 60) throw CloudTransferProtocol.Invalid();
            }
            return receiveExpiry;
        }

        internal async Task RefreshAsync(CancellationToken cancellation = default)
        {
            if (State != CloudTransferState.Ready) return;
            CheckExpiry();
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, cancellation))
            {
                // The state of the normal file is the transfer's; the phone always receives it.
                var fields = await Send("GET", normal.Path, normal.UploadToken, null, null, linked.Token, diagnose: false).ConfigureAwait(false);
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
                    qr = null; importLink = null; ForgetFiles();
                }
            }
        }
        internal bool ExpireIfDue()
        {
            lock (gate)
            {
                if (state >= CloudTransferState.Completed || expiresAt == 0 || now() < OperationDeadline) return false;
                state = CloudTransferState.Expired; qr = null; importLink = null;
                stop.Cancel(); ForgetFiles();
                return true;
            }
        }
        private void ForgetFiles()
        {
            lock (gate)
                foreach (var leg in light == null ? new[] { normal } : new[] { normal, light })
                { leg.Reader?.Dispose(); leg.Reader = null; leg.Encrypted?.Dispose(); leg.Encrypted = null; }
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
            if (response.StatusCode != 200) throw new SafeTransferFailure("転送サービスが応答できませんでした（HTTP {0}）\n時間を置いて新しいQRを作る", response.StatusCode);
            var fields = CloudTransferProtocol.Fields.Read(response.Body);
            fields.Exact("v", "uploadLifetimeSeconds");
            if (fields.Number("v") != 3) throw CloudTransferProtocol.Invalid();
            var seconds = fields.Number("uploadLifetimeSeconds");
            if (seconds == 0) return false;
            if (seconds != CloudTransferProtocol.UploadLifetimeSeconds) throw CloudTransferProtocol.Invalid();
            return true;
        }

        // A limit, when given, ends the request and its retries before the transfer's own deadline.
        private async Task<CloudTransferProtocol.Fields> Send(string method, string path, string token, byte[] body, string type, CancellationToken ct, bool parse = true, bool diagnose = true, bool retry = false, long limit = 0)
        {
            CloudTransferResponse response = null;
            for (var attempt = 0; ; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if (retry) CheckExpiry();
                if (limit > 0 && now() >= limit) throw new TimeoutException();
                response = null;
                try
                {
                    var seconds = extendedUpload && method == "PUT" ? CloudTransferProtocol.UploadPartTimeoutSeconds : 90;
                    var expiry = Deadline(limit);
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
                var remaining = TimeSpan.FromSeconds(Math.Max(0, Deadline(limit) - now()));
                // A server's requested cooldown must not be shortened into a
                // premature retry or extend the transfer's original lifetime.
                if (wait >= remaining && response != null) break;
                await delay(wait < remaining ? wait : remaining, ct).ConfigureAwait(false);
            }
            if (response.StatusCode < 200 || response.StatusCode >= 300)
            {
                if (!diagnose) throw CloudTransferProtocol.Invalid();
                throw new SafeTransferFailure(response.StatusCode == 429
                    ? "転送サービスの利用上限か混雑で送信できません（HTTP 429）\n時間を置いてもう一度試す"
                    : response.StatusCode == 413
                    ? "転送サイズがサービスの上限を超えています（HTTP 413）\nサイズを減らす"
                    : response.StatusCode == 401 || response.StatusCode == 403
                    ? "転送の認証が拒否されました（HTTP {0}）\n新しいQRを作る"
                    : "転送サービスが応答できませんでした（HTTP {0}）\n時間を置いて新しいQRを作る", response.StatusCode);
            }
            return parse ? CloudTransferProtocol.Fields.Read(response.Body) : null;
        }
        private long Deadline(long limit)
        {
            var expiry = OperationDeadline;
            return limit > 0 && (expiry == 0 || limit < expiry) ? limit : expiry;
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
            var reason = exception is UnauthorizedAccessException ? "一時ファイルへのアクセスが拒否されました\nOSのアクセス権を確認する"
                : exception is InvalidDataException ? "転送サービスの応答を確認できませんでした\n最新版の試験用パッケージで再試行する"
                : exception is IOException ? "一時ファイルを読み書きできませんでした\n空き容量やファイルを使用中のソフトを確認する"
                : exception is HttpRequestException ? "転送サービスに接続できません\nインターネット接続・VPN・プロキシを確認する"
                : exception is OperationCanceledException ? "通信が中断または時間切れになりました\n接続を確認して新しいQRを作る"
                : "転送データを確認できませんでした\n最新版の試験用パッケージで再試行する";
            return new SafeTransferFailure(reason);
        }

        private async Task DeleteRemote(Leg leg)
        {
            string transferId, owner;
            lock (gate) { transferId = leg.Id; owner = leg.UploadToken; }
            if (transferId == null || owner == null) return;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    await transport.SendAsync("POST", "/v3/transfers/" + transferId + "/cancel", owner, Array.Empty<byte>(), "application/octet-stream", timeout.Token).ConfigureAwait(false);
            }
            catch { /* The fixed server expiry also cleans up when notification cannot reach it. */ }
        }
        // Stopping the transfer stops both files. Once the phone has received the
        // normal file it may still be fetching the lightweight one, which the
        // service then removes at its own expiry.
        private Task DeleteRemotes()
        {
            Leg second;
            lock (gate) second = state == CloudTransferState.Completed ? null : light;
            return second == null ? DeleteRemote(normal) : Task.WhenAll(DeleteRemote(normal), DeleteRemote(second));
        }
        internal async Task CancelAsync()
        {
            lock (gate) { if (Terminal) return; state = CloudTransferState.Canceled; qr = null; importLink = null; stop.Cancel(); ForgetFiles(); }
            await DeleteRemotes().ConfigureAwait(false);
        }
        public void Dispose()
        {
            lock (gate) { if (disposed) return; disposed = true; stop.Cancel(); if (!Terminal) state = CloudTransferState.Canceled; qr = null; importLink = null; ForgetFiles(); }
            _ = DeleteRemotes();
        }
    }
}
