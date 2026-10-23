using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // The content of "アプリに転送": the name and the consent, the export, the upload of the
    // encrypted VRM and the QR of the vrvlog://import#... link. AppTransferFlow decides what is
    // shown and when; this class builds the UI Toolkit view (AppTransferWindow.uxml/.uss) and
    // connects it to the Editor. AppTransferWindow shows it on its own for the quick transfer;
    // the expression board shows it as its third page for the detailed one (CreateEmbedded),
    // where the board's own header, avatar and steps replace the panel's.
    [Serializable]
    public sealed class AppTransferPanel
    {
        private const float HeaderHeight = 60f, Padding = 16f;
        // Everything of the QR screen except the header and the QR area itself. The QR notice wraps at
        // twice the default font size: at the minimum width of 420 every translation takes 2 lines (65 at
        // most, zh-Hans) where one 18-high line used to be, so 47 more than before needs no scrolling.
        // The buttons and the save result line under both screens, and the QR screen's hairline above
        // them (AppTransferWindow.uss).
        private const float FooterHeight = 69f;
        private const float TransferChrome = 208f + FooterHeight;
        internal static readonly Vector2 FormMinimumSize = new Vector2(420f, 400f);
        // The QR notice's size in the window (AppTransferWindow.uss), and the smallest it is made on the board.
        private const float WarningFontSize = 24f, SmallestWarningFontSize = 12f;
        // Above the QR once the export is saved, when materials lost their shader. Kept out of
        // AppTransferText: that list holds one-line texts without punctuation or requests.
        internal const string MissingShadersText = "シェーダーが見つからないマテリアルが{0}個あり、見た目を正しく再現できていません。アバターに必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出してください";
        // The height that warning adds to the QR screen: at the minimum width of 420 it wraps to 3 lines
        // in every translation, plus the box's padding and its 8 bottom margin.
        private const float MissingShadersHeight = 64f;
        // The layout and styles next to AppTransferWindow.cs, found by their asset GUIDs.
        private const string LayoutGuid = "db52aabb2abe4b37909abb5c737b7410", StyleGuid = "6f09c8d6325e47628b30efe33abe0b23";
        // The start dialog names the transfer it stops. Sentences, so kept out of AppTransferText.
        internal const string StopUploadText = "「{0}」のアップロードを中止します";
        internal const string StopQrText = "「{0}」で表示中のQRは使えなくなります";
        // The button next to "QRコードを作る" on both screens, and the line under the buttons once it saved.
        internal const string SaveCopyText = "VRMを保存";
        internal const string SavedCopyText = "{0}を保存しました";
        internal const string SavedCopyMissingShadersText = "{0}を保存しました・シェーダーが見つからないマテリアル{1}個";
        // On the form once "QRコードを作る" sends the saved file instead of exporting again.
        internal const string SavedCopyReusedText = "{0}を保存しました・このファイルを送ります";
        internal const string SaveFailedText = "保存できませんでした";

        // Every panel on screen, in a window or on the board. The Editor transfers one avatar at a time:
        // starting one stops the others, after asking.
        internal static readonly List<AppTransferPanel> Live = new List<AppTransferPanel>();
        // The start dialog: true stops the other transfer and starts this one. Tests replace it.
        internal static Func<string, string, bool> ConfirmStart = (title, body) =>
            EditorUtility.DisplayDialog(title, body, T(AppTransferText.StopAndStart), T("戻る"));

        [SerializeField] private GameObject avatar;
        [SerializeField] private string avatarName = "";
        // Opened from the expression board: the export is what that board shows.
        [SerializeField] private bool detailed;
        // The window showing this panel; null on the expression board.
        [NonSerialized] internal AppTransferWindow Window;
        // On the expression board, which already shows the header and the avatar.
        [NonSerialized] private bool embedded;
        // On the board: its "戻る" to the objects page, which asks first while this transfers.
        [NonSerialized] private Action back;
        // On the board: whether "VRMを保存" is the blue button instead of "QRコードを作る". The board
        // opened to save a VRM says so; opened to transfer, and in the window, the QR is the blue one.
        // Read on every refresh, as the board can change between the two.
        [NonSerialized] private Func<bool> savePrimary;
        // Consent is for the avatar on screen, so it is asked again each time.
        [NonSerialized] private bool consented;
        // The privacy policy row stays once shown, so ticking it does not move the form.
        [NonSerialized] private bool showPrivacy;
        [NonSerialized] private bool? nameFits;
        [NonSerialized] private AppTransferFlow flow;
        [NonSerialized] private ExportEntry exporter;
        [NonSerialized] private bool exporterResolved;
        [NonSerialized] private string exporterProblem;
        [NonSerialized] private Texture2D qrTexture;
        [NonSerialized] private string transferName;
        [NonSerialized] private int placeholderSide;
        [NonSerialized] private int exportAttempt;
        // Materials whose shader was missing in the export the current upload comes from.
        [NonSerialized] private int missingShaders;
        [NonSerialized] private bool exportPainted;
        [NonSerialized] private double exportDeadline;
        [NonSerialized] private double nextRepaint;
        [NonSerialized] private AppTransferView shownView, polledView;
        // Device pixels per point of the panel, read where IMGUI knows it.
        [NonSerialized] private float pixelsPerPoint;
        // On the board, the most the QR area may take of the page's height (FitToArea).
        [NonSerialized] private float embeddedRoom = float.PositiveInfinity;
        // The last "VRMを保存" of this panel for this avatar: the file written, or why nothing was.
        [NonSerialized] private string savedCopy, saveProblem;
        [NonSerialized] private int savedCopyMissingShaders;
        // What the saved file and the transfer's exported copy were written from, so pressing the other
        // button reuses the file instead of exporting again. Null once it may no longer match.
        [NonSerialized] private WrittenVrm savedWritten, sentWritten;

        [NonSerialized] private bool built;
        // "QRコードを作る" was pressed while something it needs was missing; the missing labels turn red.
        [NonSerialized] private bool pressedWhileBlocked, authorMarked;
        [NonSerialized] private IMGUIContainer termsView;
        [NonSerialized] private VisualElement root, content, form, formFields, consentFields, privacyRow, transfer, slotRow, slot, slotMessage, slotUpload, revealArea, warningRow, barFill;
        [NonSerialized] private HelpBox disabledBox, problemBox, playingBox, missingShadersBox;
        [NonSerialized] private Button diagnosticsButton, summaryButton, backButton, createButton, messageButton, showQrButton, hideQrButton, regenerateButton, closeButton, saveButton;
        [NonSerialized] private Label heading, subtitle, avatarLabel, summaryText, nameLabel, nameHint, termsLabel, readTerms, consentText, privacyLink, privacyText,
            reasonLabel, messageTitle, messageBody, uploadTitle, scanLabel, warningText, statusLabel, remainingLabel, lifetimeLabel, leaveNote,
            saveResult, saveOpen;
        [NonSerialized] private ObjectField avatarField;
        [NonSerialized] private TextField nameField;
        [NonSerialized] private Toggle consentToggle, privacyToggle, soundToggle;
        [NonSerialized] private Image qrImage, qrCover, warningIcon;
        [NonSerialized] private ScrollView formScroll, transferScroll;
        [NonSerialized] private VisualElement transferBody;
        [NonSerialized] private IMGUIContainer uploadBar;

        // The third page of the expression board, for the board's avatar. Resolved by the board by
        // name, as the exporter is here. It lives while it is on the board's page: leaving the page
        // or closing the board cancels the transfer and deletes the copy, as closing the window does.
        public static VisualElement CreateEmbedded(GameObject avatar) => CreateEmbedded(avatar, null, null);

        // The same with the board's "戻る" next to the buttons, and which of "VRMを保存" and
        // "QRコードを作る" is the blue one (savePrimary, read on every refresh).
        public static VisualElement CreateEmbedded(GameObject avatar, Action back, Func<bool> savePrimary)
        {
            var panel = new AppTransferPanel { detailed = true, embedded = true, back = back, savePrimary = savePrimary };
            var element = new VisualElement();
            panel.Build(element);
            panel.Select(avatar, true);
            element.RegisterCallback<AttachToPanelEvent>(_ => { panel.Enable(); panel.Refresh(); });
            element.RegisterCallback<DetachFromPanelEvent>(_ => panel.Disable());
            return element;
        }

        // Whether the board's panel (CreateEmbedded) is uploading or showing its QR. The board asks
        // before a menu entry takes it off its page. Resolved by name, as CreateEmbedded is.
        public static bool EmbeddedTransferring(VisualElement element) => Live.Any(panel => panel.root == element && panel.Transferring);

        internal GameObject Avatar => avatar;
        internal bool Detailed { get => detailed; set => detailed = value; }
        internal bool Transferring => flow != null && (flow.View == AppTransferView.Uploading || flow.View == AppTransferView.Waiting);
        internal bool AtForm => flow == null || flow.View == AppTransferView.Form;

        // Another avatar cancels the upload or the QR on screen, so it is asked first.
        internal static bool ConfirmEndingTransfer() => EditorUtility.DisplayDialog(T("アバターを変更"),
            T("このQRコードは使えなくなります"), T("変更する"), T("転送を続ける"));

        internal static bool ConfirmRedoingTransfer() => EditorUtility.DisplayDialog(T("QRを作り直す"),
            T("このQRコードは使えなくなります"), T("作り直す"), T("転送を続ける"));

        private static string T(string source) => ExporterLocalization.T(source);
        private AppTransferFlow Flow => flow ?? (flow = new AppTransferFlow());

        // The exporter assembly compiles only with a supported UniVRM. Bind it by
        // name where it is used, as the exporter does for this module.
        private ExportEntry Exporter
        {
            get
            {
                if (exporterResolved) return exporter;
                exporterResolved = true;
                // The exporter assembly also loads with an unsupported lilToon; like the
                // export windows, do not export until the installed versions are supported.
                var installed = DependencyDiagnostics.Installed();
                exporter = DependencyDiagnostics.Error(installed) == null ? ExportEntry.Resolve() : null;
                if (exporter != null) exporter.SelectAvatar(avatar);
                else exporterProblem = DependencyDiagnostics.StartupError(installed)
                    ?? "書き出し処理の準備が完了していません。「再読み込み」を押してください。";
                return exporter;
            }
        }

        // The name of the avatar's last successful transfer, or null.
        private string RememberedName(GameObject target)
        {
            var remembered = target != null ? Exporter?.RememberedName(target) : null;
            return string.IsNullOrEmpty(remembered) ? null : remembered;
        }

        // Whether the expression board this panel exports from is open and prepared.
        private bool BoardReady => !detailed || avatar == null || Exporter?.DetailedProblem(avatar) == null;
        // The open board has an error its next step names, such as a face that does not work.
        private bool BoardNeedsFix => detailed && avatar != null && BoardReady && (Exporter?.DetailedNeedsFix(avatar) ?? false);

        internal void Select(GameObject target, bool opened)
        {
            // A recovery still pending for the previous avatar must not start an upload.
            exportAttempt++;
            flow?.Stop();
            ClearQr();
            missingShaders = 0;
            savedCopy = saveProblem = null;
            ForgetSavedForReuse();
            // A name left over from an avatar that no longer exists is not the user's choice for this one.
            var untouched = opened || avatar == null || !AppTransferFlow.HasListedName(avatarName) || avatarName == avatar.name
                || avatarName == RememberedName(avatar);
            avatar = target;
            // Opening starts from the name of the avatar's last transfer.
            if (untouched) avatarName = target == null ? "" : opened ? RememberedName(target) ?? target.name : target.name;
            consented = false;
            nameFits = null;
            pressedWhileBlocked = false;
            Exporter?.SelectAvatar(target);
            SetMinimumSize(FormMinimumSize);
            Refresh();
        }

        internal void Enable()
        {
            if (!Live.Contains(this)) Live.Add(this);
            EditorApplication.update += Poll;
            EditorApplication.quitting += StopAndClean;
            AssemblyReloadEvents.beforeAssemblyReload += StopAndClean;
            ObjectChangeEvents.changesPublished += SceneChanged;
            Undo.undoRedoPerformed += SceneChanged;
        }

        internal void Disable()
        {
            // Away from the screen the board can change, so the saved file may no longer be what it shows.
            ForgetSavedForReuse();
            Live.Remove(this);
            EditorApplication.update -= Poll;
            EditorApplication.quitting -= StopAndClean;
            AssemblyReloadEvents.beforeAssemblyReload -= StopAndClean;
            ObjectChangeEvents.changesPublished -= SceneChanged;
            Undo.undoRedoPerformed -= SceneChanged;
            StopAndClean();
        }

        // Closing cancels the transfer and deletes the exported copy. Nothing of
        // the link, key or token is written to a log, a preference or a file.
        private void StopAndClean()
        {
            exportAttempt++;
            flow?.Stop();
            ClearQr();
        }

        // Before a transfer starts here: another panel uploading or showing its QR is named in a
        // dialog and stopped when the user goes on. "戻る" only closes the dialog.
        private bool MayStart()
        {
            var others = Live.Where(other => other != this && other.Transferring).ToArray();
            if (others.Length == 0) return true;
            var first = others[0];
            var heading = T(first.detailed ? AppTransferText.HeadingDetailed : AppTransferText.HeadingQuick);
            var body = string.Format(T(first.flow.View == AppTransferView.Uploading ? StopUploadText : StopQrText), heading);
            if (!ConfirmStart(T(AppTransferText.StartTransfer), body)) return false;
            foreach (var other in others) other.StopTransfer();
            return true;
        }

        // Another transfer goes on: this one stops as closing would, back at its form.
        internal void StopTransfer()
        {
            StopAndClean();
            SetMinimumSize(FormMinimumSize);
            Refresh();
        }

        // The window this panel is shown in: its own, or the expression board.
        internal EditorWindow Host => Window != null ? (EditorWindow)Window : root == null ? null
            : Resources.FindObjectsOfTypeAll<EditorWindow>().FirstOrDefault(window => window != null && window.rootVisualElement.Contains(root));

        private void ClearQr()
        {
            if (qrImage != null) qrImage.image = null;
            if (qrTexture != null) UnityEngine.Object.DestroyImmediate(qrTexture);
            qrTexture = null;
        }

        // The board sizes its page itself; only the window grows for the QR.
        private void SetMinimumSize(Vector2 size)
        {
            if (Window != null) Window.minSize = size;
        }

        private void Poll()
        {
            var time = EditorApplication.timeSinceStartup;
            // Until the view is built there is no flow; the form also refreshes every 0.2 s below,
            // so it follows the Editor (Play mode, the privacy choice shared with "スマホに送る").
            if (flow == null) return;
            if (!CloudTransferAvailability.Enabled) { StopAndClean(); return; }
            if (flow.View == AppTransferView.Exporting)
            {
                // Exporting blocks the Editor: start once the panel has painted that state.
                // A hidden tab never paints, so do not wait for it longer than a moment.
                if (exportPainted || time >= exportDeadline) RunExport();
                return;
            }
            flow.Tick(time);
            SyncQr();
            var view = flow.View;
            if (view == polledView && time < nextRepaint) return;
            polledView = view;
            nextRepaint = time + 0.2;
            Refresh();
        }

        // The OS notification sound, once per QR that is ready behind "QRを表示".
        // The real QR exists only while it is shown.
        private void SyncQr()
        {
            if (AppTransferFlow.ShouldBeepForQr(flow.View, flow.TakeQrReady(), AppTransferWindow.QrReadySound)) EditorApplication.Beep();
            if (flow.QrRevealed) { if (qrTexture == null) ShowQr(); }
            else ClearQr();
        }

        private void ShowQr()
        {
            // A status request can end the wait between the view check and this read.
            var link = flow.Link;
            if (link == null) return;
            ClearQr();
            try { qrTexture = CloudVrmTransferWindow.RenderQr(link); }
            catch { flow.Fail(AppTransferText.NeedsShorterName); }
        }

        // "QRを表示": the QR is drawn now, not when it became ready.
        private void RevealQr()
        {
            flow.RevealQr();
            if (flow.QrRevealed && qrTexture == null) ShowQr();
            Refresh();
        }

        // "QRを隠す": back to the cover. The QR stays valid until its deadline.
        private void HideQr()
        {
            flow.HideQr();
            ClearQr();
            Refresh();
        }

        private string Reason()
        {
            if (nameFits == null) nameFits = AppTransferFlow.NameFits(avatarName);
            if (BoardNeedsFix) return AppTransferText.NeedsBoardFix;
            return AppTransferFlow.BlockReason(avatar != null, BoardReady, avatarName, nameFits.Value, Exporter?.HasAuthor() ?? false, consented,
                CloudVrmTransferWindow.PrivacyConsentAccepted);
        }

        // The inputs to show in red, by the same checks as Reason.
        private RequiredInput MarkedInputs(bool pressed)
        {
            if (nameFits == null) nameFits = AppTransferFlow.NameFits(avatarName);
            return AppTransferFlow.MarkedInputs(pressed, avatar != null, avatarName, nameFits.Value, Exporter?.HasAuthor() ?? false, consented,
                CloudVrmTransferWindow.PrivacyConsentAccepted);
        }

        // The first red field above the footer, scrolled into view when it is outside the form's scroll.
        private void RevealFirstMarked()
        {
            var marked = MarkedInputs(true);
            var target = (marked & RequiredInput.Avatar) != 0 ? (VisualElement)avatarLabel
                : (marked & RequiredInput.Name) != 0 ? (VisualElement)nameLabel
                : (marked & RequiredInput.Author) != 0 ? termsView : null;
            if (target != null && formScroll != null) formScroll.ScrollTo(target);
        }

        private void Begin()
        {
            if (Exporter == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var reason = Reason();
            if (AppTransferFlow.BoardBlocks(reason)) return;
            if (reason != null)
            {
                pressedWhileBlocked = true;
                Refresh();
                RevealFirstMarked();
                return;
            }
            pressedWhileBlocked = false;
            if (!MayStart()) return;
            transferName = AppTransferFlow.TransferName(avatarName);
            placeholderSide = TransferQrCode.Encode(AppTransferFlow.PlaceholderLink(transferName)).Width;
            Flow.BeginExport();
            ExpectExportPaint();
            FitTransferSize();
            Refresh();
        }

        // The expression board in its transfer mode, for the avatar on screen. From the quick
        // transfer this switches to the detailed one, so the window closes.
        private void EditDetails()
        {
            var entry = Exporter;
            if (entry == null || avatar == null || Transferring) return;
            // Declining the board's change of avatar keeps the window.
            if (entry.EditDetails(avatar) && !detailed && Window != null) Window.Close();
        }

        // A new QR uploads again from the exported copy. After a failed export there is none, so export again.
        private void Retry()
        {
            // The privacy policy choice is shared with "スマホに送る" and can be withdrawn there.
            // Exporting again needs an author; without one, return to the form where the field says so.
            var backToForm = !CloudVrmTransferWindow.PrivacyConsentAccepted || !flow.CanRegenerate && Exporter != null && !Exporter.HasAuthor();
            // Declined, this QR or message stays as it is.
            if (!backToForm && !MayStart()) return;
            ClearQr();
            if (backToForm)
            {
                flow.Stop();
                SetMinimumSize(FormMinimumSize);
                Refresh();
                return;
            }
            if (flow.CanRegenerate) flow.Regenerate();
            else if (Exporter != null && !EditorApplication.isPlayingOrWillChangePlaymode) { flow.BeginExport(); ExpectExportPaint(); }
            Refresh();
        }

        // Saving needs what the export needs, not the consent to the transfer service. Not while
        // the transfer exports, which holds the Editor until it has saved its own copy.
        private bool CanSaveCopy => Exporter != null && avatar != null && !EditorApplication.isPlayingOrWillChangePlaymode && BoardReady
            && !BoardNeedsFix && Exporter.HasAuthor() && Flow.View != AppTransferView.Exporting;

        // "VRMを保存": once this transfer has exported, the very file it sends, copied where the user picks.
        // Before that, the VRM it would send, saved as the board's own save does, with the same destination
        // dialog, result list and recovery window; "QRコードを作る" then sends that file. The transfer on screen
        // goes on. Without a name, the avatar's, as the board's own save names it.
        private void SaveCopy()
        {
            if (!CanSaveCopy) return;
            if (flow?.SnapshotHash != null && sentWritten != null) { SaveSentCopy(); return; }
            var name = string.IsNullOrWhiteSpace(avatarName) ? avatar.name.Trim() : avatarName.Trim();
            if (name.Length == 0) name = "avatar";
            var source = avatar;
            // Taken now: the result list or the recovery window can write the file much later.
            var key = SaveKey(name);
            string failure;
            try
            {
                failure = Exporter.SaveCopy(avatar, name, detailed, (path, missing) =>
                {
                    // Also later, when the result list or recovery window saves; by then the panel may
                    // show another avatar, whose row must not claim this file.
                    if (avatar != source) return;
                    SavedCopy(path, missing, Written(AppTransferFlow.FileHashOf(path), key, source));
                });
            }
            catch (Exception exception) { failure = exception.Message ?? ""; }
            finally { EditorUtility.ClearProgressBar(); }
            if (failure != null) { savedCopy = null; saveProblem = failure; savedWritten = null; }
            Refresh();
        }

        // The exported copy of the transfer on screen, copied byte for byte. Nothing is exported again,
        // so the saved file is the one the phone gets.
        private void SaveSentCopy()
        {
            var destination = EditorUtility.SaveFilePanel(T("VRMの保存先"), "", Exporter.SaveFileName(avatar), "vrm");
            if (string.IsNullOrEmpty(destination)) return;
            var sent = sentWritten;
            string copied = null, failure = null;
            try
            {
                using (var from = sent == null ? null : flow.OpenSnapshot())
                    if (from != null) copied = AppTransferFlow.CopyTo(from, destination, sent.Hash);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException
                || exception is NotSupportedException || exception is ObjectDisposedException)
            { failure = exception.Message ?? ""; }
            // The transfer ended before the copy, or the copy is not what was sent: CopyTo placed nothing,
            // so a file the user chose to replace is still there.
            if (failure == null && copied == null) failure = T(SaveFailedText);
            if (failure != null) { savedCopy = null; saveProblem = failure; savedWritten = null; }
            else SavedCopy(destination, missingShaders, sent);
            Refresh();
        }

        private void SavedCopy(string path, int missing, WrittenVrm written)
        {
            savedCopy = path;
            savedCopyMissingShaders = missing;
            saveProblem = null;
            savedWritten = written;
            Refresh();
        }

        private void ForgetSavedForReuse() => savedWritten = sentWritten = null;

        // A change in a scene or an undo may have changed the avatar: the files are checked again before reuse.
        private void SceneChanged(ref ObjectChangeEventStream stream) => SceneChanged();

        private void SceneChanged()
        {
            if (savedWritten != null) savedWritten.SceneChanged = true;
            if (sentWritten != null) sentWritten.SceneChanged = true;
        }

        // The name, author and VRM terms a file is written with.
        private string SaveKey(string name) => name + "\n" + (Exporter?.TermsKey() ?? "");

        // Null when the file cannot be read back: it is then never reused.
        private WrittenVrm Written(string hash, string key, GameObject source) =>
            hash == null || Exporter == null ? null : new WrittenVrm(hash, key, Exporter.SourceStamp(source, detailed));

        // "QRコードを作る" after "VRMを保存" sends the saved file while the name, author and VRM terms are
        // the ones it was written with and nothing it was made from has changed. Cheap, for the result line:
        // after a change in a scene it says nothing. RunExport checks the avatar itself on every press, as
        // a file reimported on disk changes it without any change in a scene.
        private bool MayReuseSavedCopy(bool checkAvatar)
        {
            var written = savedWritten;
            if (savedCopy == null || saveProblem != null || written == null || Exporter == null) return false;
            if (written.Terms != SaveKey(avatarName.Trim()) || !Exporter.ChoicesUnchanged(written.Stamp)) return false;
            if (!checkAvatar) return !written.SceneChanged;
            if (!Exporter.SourceUnchanged(written.Stamp, avatar)) return false;
            written.SceneChanged = false;
            return true;
        }

        // Copies the saved file to the transfer's own copy. False, with nothing left behind, when the file
        // changed or cannot be read; the transfer then exports as it would without it.
        private bool ReuseSavedCopy(string snapshotPath)
        {
            string copied = null;
            try
            {
                using (var from = new FileStream(savedCopy, FileMode.Open, FileAccess.Read, FileShare.Read))
                    copied = AppTransferFlow.CopyTo(from, snapshotPath, savedWritten.Hash);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException
                || exception is NotSupportedException) { }
            if (copied != null) return true;
            AppTransferFlow.Discard(snapshotPath);
            savedWritten = null;
            return false;
        }

        // A VRM file this panel wrote or sent: its SHA-256, the name and terms, and what it was made from.
        private sealed class WrittenVrm
        {
            internal readonly string Hash, Terms;
            internal readonly object Stamp;
            internal bool SceneChanged;
            internal WrittenVrm(string hash, string key, object stamp) { Hash = hash; Terms = key; Stamp = stamp; }
        }

        private void RefreshSaveRow()
        {
            saveButton.text = T(SaveCopyText);
            saveButton.SetEnabled(CanSaveCopy);
            string result = "", detail = "";
            var warn = false;
            if (saveProblem != null)
            {
                // The recovery window, when open, has the details; the first line fits the row.
                var first = saveProblem.Split('\n')[0].Trim();
                result = first.Length > 0 ? first : T(AppTransferText.ExportFailed);
                detail = saveProblem;
                warn = true;
            }
            else if (savedCopy != null)
            {
                var file = Path.GetFileName(savedCopy);
                result = savedCopyMissingShaders > 0 ? string.Format(T(SavedCopyMissingShadersText), file, savedCopyMissingShaders)
                    : string.Format(T(Flow.View == AppTransferView.Form && MayReuseSavedCopy(false) ? SavedCopyReusedText : SavedCopyText), file);
                warn = savedCopyMissingShaders > 0;
                // The row has room for the count only; the tooltip says what to do, as the QR screen's warning does.
                detail = warn ? savedCopy + "\n" + string.Format(T(MissingShadersText), savedCopyMissingShaders) : savedCopy;
            }
            saveResult.text = result;
            saveResult.tooltip = detail;
            saveResult.EnableInClassList("save-result--warn", warn);
            saveOpen.text = T("保存先を開く");
            Show(saveOpen, saveProblem == null && savedCopy != null && File.Exists(savedCopy));
        }

        private void ExpectExportPaint()
        {
            exportPainted = false;
            exportDeadline = EditorApplication.timeSinceStartup + 0.5;
        }

        private void RunExport()
        {
            exportPainted = false;
            var attempt = ++exportAttempt;
            // A new export replaces the one the warning was about.
            missingShaders = 0;
            string path;
            // Every folder that could hold the transfer copy refused it: give up before exporting.
            try { path = AppTransferFlow.NewSnapshotPath(); }
            catch (InvalidOperationException exception)
            {
                flow.ExportFailed(exception.Message ?? "");
                Refresh();
                return;
            }
            var target = avatar;
            var name = avatarName.Trim();
            var wireName = transferName;
            var fromBoard = detailed;
            // After "VRMを保存", the saved file is sent; nothing is exported a second time.
            sentWritten = null;
            if (MayReuseSavedCopy(true) && ReuseSavedCopy(path))
            {
                exportAttempt++;
                missingShaders = savedCopyMissingShaders;
                if (missingShaders > 0) FitTransferSize();
                Exporter?.RememberTransferName(target, name);
                sentWritten = savedWritten;
                flow.Upload(path, wireName);
                Refresh();
                return;
            }
            var key = SaveKey(name);
            var saved = false;
            void Saved()
            {
                // The recovery window can save long after this attempt was replaced or the panel closed
                // (closing counts as a new attempt).
                if (flow == null || attempt != exportAttempt) { AppTransferFlow.Discard(path); return; }
                // A late save from the recovery window, after another panel started its transfer, asks again.
                if (!MayStart())
                {
                    exportAttempt++;
                    AppTransferFlow.Discard(path);
                    flow.ExportCanceled();
                    SetMinimumSize(FormMinimumSize);
                    Refresh();
                    return;
                }
                saved = true;
                missingShaders = Exporter?.LastMissingShaderCount() ?? 0;
                // Saving it later copies this file. A file that left out a failing stage is not what
                // "VRMを保存" writes: saving then exports again.
                var complete = (Exporter?.LastOmittedStageCount() ?? 0) == 0;
                // Grow for the warning now, while the upload is shown, so the QR never moves once it is on screen.
                if (missingShaders > 0) FitTransferSize();
                // One export feeds one upload: a second save of the same attempt is ignored.
                exportAttempt++;
                flow.Upload(path, wireName);
                // The stamp is taken now, as the file is written; the upload hashed the snapshot already.
                sentWritten = complete ? Written(flow.SnapshotHash, key, target) : null;
                Refresh();
            }
            string failure;
            try { failure = fromBoard ? Exporter.ExportDetailed(target, name, path, Saved) : Exporter.Export(target, name, path, Saved); }
            catch (Exception exception) { failure = exception.Message ?? ""; AppTransferFlow.Discard(path); }
            finally { EditorUtility.ClearProgressBar(); }
            if (saved || attempt != exportAttempt) return;
            // Nothing was handed to the upload. Delete whatever this attempt left;
            // a later save from the recovery window writes the file again.
            AppTransferFlow.Discard(path);
            if (failure == null) { flow.ExportCanceled(); SetMinimumSize(FormMinimumSize); }
            else flow.ExportFailed(failure);
            Refresh();
        }

        private float ScaleFactor => pixelsPerPoint > 0f ? pixelsPerPoint : EditorGUIUtility.pixelsPerPoint;

        // The placeholder has the longest numbers a link can have, so the real QR is the same
        // version or one smaller. Reserve the larger of the two drawn sizes for whole-pixel modules.
        private float SlotSide
        {
            get
            {
                var side = Mathf.Max(placeholderSide, 2 * TransferQrCode.QuietZone + 21);
                var smaller = side - 4 >= 2 * TransferQrCode.QuietZone + 21 ? QrSide(side - 4) : 0f;
                return Mathf.Max(QrSide(side), smaller);
            }
        }

        // On the board the QR takes what the page's height leaves it (FitToArea); in the window, its full size.
        private float QrSide(int matrixSide) => embedded
            ? TransferQrCode.FittedSide(matrixSide, embeddedRoom, ScaleFactor)
            : TransferQrCode.DisplaySide(matrixSide, ScaleFactor);

        // The height the board's page leaves for the QR area: the scroll's visible height less every
        // other row of the QR screen. Those rows have fixed heights, so the text size does not change it,
        // and resizing the QR area changes the rows' total by as much, so it settles at once.
        private void FitToArea()
        {
            if (!embedded || !built || transfer.resolvedStyle.display == DisplayStyle.None) return;
            var visible = transferScroll.contentViewport.layout.height;
            var rows = transferBody.layout.height - slotRow.layout.height;
            if (float.IsNaN(visible) || float.IsNaN(rows) || visible <= 0f) return;
            var room = Mathf.Floor(visible - rows);
            if (Mathf.Abs(room - embeddedRoom) < 0.5f) return;
            embeddedRoom = room;
            slotRow.style.height = SlotSide;
            PlaceSlot();
        }

        // On the board the QR notice is one line: the largest size up to its own that fits the width.
        // Too narrow even at the smallest size, it wraps as in the window.
        private void FitWarning()
        {
            if (!embedded || !built) return;
            var room = warningRow.contentRect.width - warningIcon.layout.width - warningIcon.resolvedStyle.marginLeft - warningIcon.resolvedStyle.marginRight;
            var current = warningText.resolvedStyle.fontSize;
            if (float.IsNaN(room) || room <= 0f || current <= 0f || string.IsNullOrEmpty(warningText.text)) return;
            var width = warningText.MeasureTextSize(warningText.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined).x;
            if (width <= 0f) return;
            // Two points spare, and one size up is not taken: text width is not quite linear in its size,
            // so the line stays inside the row and does not flip between two sizes on every refresh.
            var fitted = Mathf.Min(WarningFontSize, Mathf.Floor(current * (room - 2f) / width));
            var line = fitted >= SmallestWarningFontSize;
            warningRow.EnableInClassList("warning--line", line);
            if (!line) warningText.style.fontSize = StyleKeyword.Null;
            else if (fitted < current || fitted >= current + 2f || fitted == WarningFontSize) warningText.style.fontSize = fitted;
        }

        // The window is at least as large as the QR it is about to show. On a short screen (1080p at 150%)
        // it is no taller than the Unity window, so the buttons stay on screen and the body scrolls instead.
        // On the board the page keeps its size and the QR screen scrolls.
        private void FitTransferSize()
        {
            var window = Window;
            if (window == null) return;
            var side = SlotSide;
            var height = HeaderHeight + side + TransferChrome + (missingShaders > 0 ? MissingShadersHeight : 0f);
            var screen = EditorGUIUtility.GetMainWindowPosition().height;
            if (screen > 0f) height = Mathf.Min(height, Mathf.Max(FormMinimumSize.y, screen));
            var size = new Vector2(Mathf.Max(FormMinimumSize.x, side + 2f * Padding), height);
            window.minSize = size;
            if (window.docked) return;
            var rect = window.position;
            if (rect.width < size.x || rect.height < size.y)
                window.position = new Rect(rect.x, rect.y, Mathf.Max(rect.width, size.x), Mathf.Max(rect.height, size.y));
        }

        private float Snap(float value)
        {
            var scale = ScaleFactor;
            return Mathf.Round(value * scale) / scale;
        }

        private TAsset Load<TAsset>(string guid, string extension) where TAsset : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<TAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null) return asset;
            var script = Window != null ? AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(Window))
                : AssetDatabase.FindAssets("AppTransferWindow t:MonoScript").Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass() == typeof(AppTransferWindow));
            return string.IsNullOrEmpty(script) ? null : AssetDatabase.LoadAssetAtPath<TAsset>(Path.ChangeExtension(script, extension));
        }

        // The UXML holds the structure only; every text is set in Refresh through T.
        internal void Build(VisualElement container)
        {
            root = container;
            var tree = Load<VisualTreeAsset>(LayoutGuid, ".uxml");
            var sheet = Load<StyleSheet>(StyleGuid, ".uss");
            if (tree == null || sheet == null)
            {
                // A package imported without these files would otherwise open an empty window.
                root.Clear();
                root.Add(new HelpBox(T(AppTransferText.LayoutMissing), HelpBoxMessageType.Error) { style = { marginTop = 8f, marginBottom = 8f, marginLeft = 8f, marginRight = 8f } });
                Debug.LogError("VR Vlog: AppTransferWindow.uxml or AppTransferWindow.uss was not found next to AppTransferWindow.cs. Reimport the package.");
                return;
            }
            root.Clear();
            tree.CloneTree(root);
            root.styleSheets.Add(sheet);
            root.AddToClassList("app-transfer");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "app-transfer--dark" : "app-transfer--light");
            var font = AppTransferWindow.TextFont();
            if (font != null) root.style.unityFontDefinition = FontDefinition.FromSDFFont(font);

            disabledBox = root.Q<HelpBox>("disabled");
            content = root.Q("content");
            heading = root.Q<Label>("heading");
            subtitle = root.Q<Label>("subtitle");
            form = root.Q("form");
            problemBox = root.Q<HelpBox>("exporter-problem");
            diagnosticsButton = root.Q<Button>("diagnostics");
            playingBox = root.Q<HelpBox>("playing");
            missingShadersBox = root.Q<HelpBox>("missing-shaders");
            leaveNote = root.Q<Label>("leave-note");
            formFields = root.Q("form-fields");
            consentFields = root.Q("consent");
            avatarLabel = root.Q<Label>("avatar-label");
            avatarField = root.Q<ObjectField>("avatar-field");
            summaryText = root.Q<Label>("summary-text");
            summaryButton = root.Q<Button>("summary-button");
            nameLabel = root.Q<Label>("name-label");
            nameField = root.Q<TextField>("name-field");
            nameHint = root.Q<Label>("name-hint");
            termsLabel = root.Q<Label>("terms-label");
            readTerms = root.Q<Label>("read-terms");
            consentToggle = root.Q<Toggle>("consent-toggle");
            consentText = root.Q<Label>("consent-text");
            privacyRow = root.Q("privacy");
            privacyLink = root.Q<Label>("privacy-link");
            privacyToggle = root.Q<Toggle>("privacy-toggle");
            privacyText = root.Q<Label>("privacy-text");
            reasonLabel = root.Q<Label>("reason");
            createButton = root.Q<Button>("create");
            transfer = root.Q("transfer");
            slotRow = root.Q("slot-row");
            slot = root.Q("slot");
            qrImage = root.Q<Image>("qr");
            qrCover = root.Q<Image>("qr-cover");
            revealArea = root.Q("reveal");
            showQrButton = root.Q<Button>("show-qr");
            hideQrButton = root.Q<Button>("hide-qr");
            slotMessage = root.Q("message");
            messageTitle = root.Q<Label>("message-title");
            messageBody = root.Q<Label>("message-body");
            messageButton = root.Q<Button>("message-button");
            slotUpload = root.Q("upload");
            uploadTitle = root.Q<Label>("upload-title");
            uploadBar = root.Q<IMGUIContainer>("upload-bar");
            scanLabel = root.Q<Label>("scan");
            warningRow = root.Q("warning");
            warningText = root.Q<Label>("warning-text");
            statusLabel = root.Q<Label>("status");
            remainingLabel = root.Q<Label>("remaining");
            barFill = root.Q("bar-fill");
            lifetimeLabel = root.Q<Label>("lifetime");
            soundToggle = root.Q<Toggle>("sound");
            backButton = root.Q<Button>("back");
            regenerateButton = root.Q<Button>("regenerate");
            closeButton = root.Q<Button>("close");
            saveButton = root.Q<Button>("save");
            saveResult = root.Q<Label>("save-result");
            saveOpen = root.Q<Label>("save-open");

            // The board above shows the header and the avatar. Its "戻る" to the objects page sits at
            // the left of the buttons and leaves the QR screen, so the panel has no "閉じる" there
            // (RefreshTransfer). The window has neither "戻る" nor another page to go back to.
            if (embedded)
            {
                Show(root.Q("header"), false);
                Show(root.Q("header-line"), false);
                Show(root.Q("avatar-row"), false);
                Show(root.Q("summary"), false);
            }
            Show(backButton, embedded && back != null);
            // Only the board's page cancels the transfer when it is left; the window says nothing new.
            Show(leaveNote, embedded);

            var headerIcon = root.Q<Image>("header-icon");
            headerIcon.image = ExporterWindowIcon.Texture;
            headerIcon.scaleMode = ScaleMode.ScaleToFit;
            var versionLabel = root.Q<Label>("version");
            versionLabel.text = ExporterWindowHeader.Version;
            Show(versionLabel, ExporterWindowHeader.Version.Length > 0);
            warningIcon = root.Q<Image>("warning-icon");
            warningIcon.image = EditorGUIUtility.IconContent("console.warnicon").image;
            warningIcon.scaleMode = ScaleMode.ScaleToFit;
            qrImage.scaleMode = ScaleMode.StretchToFill;
            qrCover.image = QrCover.Texture;
            qrCover.scaleMode = ScaleMode.StretchToFill;
            // Only "QRを表示" takes the click, not the area around it.
            qrCover.pickingMode = PickingMode.Ignore;
            revealArea.pickingMode = PickingMode.Ignore;
            var probe = root.Q<IMGUIContainer>("scale-probe");
            probe.onGUIHandler = ProbeScale;
            // Only measures the display scale: no Tab stop, no clicks.
            probe.focusable = false;
            probe.pickingMode = PickingMode.Ignore;

            avatarField.objectType = typeof(GameObject);
            avatarField.allowSceneObjects = true;
            avatarField.RegisterValueChangedCallback(changed =>
            {
                var selected = changed.newValue as GameObject;
                if (selected != avatar && (!Transferring || ConfirmEndingTransfer())) Select(selected, false);
                // Kept from the IMGUI version: the form is hidden while transferring, but a declined change must not stay in the field.
                avatarField.SetValueWithoutNotify(avatar);
                Refresh();
            });
            nameField.RegisterValueChangedCallback(changed =>
            {
                avatarName = changed.newValue ?? "";
                nameFits = null;
                Refresh();
            });
            consentToggle.RegisterValueChangedCallback(changed => { consented = changed.newValue; Refresh(); });
            consentText.RegisterCallback<ClickEvent>(_ => consentToggle.value = !consentToggle.value);
            privacyToggle.RegisterValueChangedCallback(changed =>
            {
                // The same saved choice as "スマホに送る".
                if (changed.newValue != CloudVrmTransferWindow.PrivacyConsentAccepted) CloudVrmTransferWindow.PrivacyConsentAccepted = changed.newValue;
                Refresh();
            });
            privacyText.RegisterCallback<ClickEvent>(_ => privacyToggle.value = !privacyToggle.value);
            readTerms.RegisterCallback<ClickEvent>(_ => Application.OpenURL(AppTransferFlow.TermsUrl));
            privacyLink.RegisterCallback<ClickEvent>(_ => Application.OpenURL(CloudVrmTransferWindow.PrivacyPolicyUrl));
            // The VRM terms are the IMGUI section shared with the export window.
            termsView = root.Q<IMGUIContainer>("terms");
            termsView.onGUIHandler = DrawTerms;
            diagnosticsButton.clicked += DependencyDiagnostics.OpenDiagnostics;
            summaryButton.clicked += EditDetails;
            backButton.clicked += () => back?.Invoke();
            createButton.clicked += Begin;
            messageButton.clicked += Retry;
            regenerateButton.clicked += Retry;
            showQrButton.clicked += RevealQr;
            hideQrButton.clicked += HideQr;
            closeButton.clicked += () => { if (Window != null) Window.Close(); };
            saveButton.clicked += SaveCopy;
            saveOpen.RegisterCallback<ClickEvent>(_ => { if (savedCopy != null && File.Exists(savedCopy)) EditorUtility.RevealInFinder(savedCopy); });
            soundToggle.RegisterValueChangedCallback(changed => AppTransferWindow.QrReadySound = changed.newValue);
            uploadBar.onGUIHandler = DrawUploadBar;
            // The export starts once the panel has drawn the Exporting state.
            slot.generateVisualContent += _ => { if (shownView == AppTransferView.Exporting) exportPainted = true; };
            slotRow.RegisterCallback<GeometryChangedEvent>(_ => PlaceSlot());
            formScroll = root.Q<ScrollView>("form-scroll");
            transferScroll = root.Q<ScrollView>("transfer-scroll");
            transferBody = transferScroll.Q(className: "body");
            transferScroll.verticalScroller.valueChanged += _ => PlaceSlot();
            if (embedded)
            {
                transferScroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => FitToArea());
                transferBody.RegisterCallback<GeometryChangedEvent>(_ => FitToArea());
                warningRow.RegisterCallback<GeometryChangedEvent>(_ => FitWarning());
            }
            built = true;
            Refresh();
        }

        private void ProbeScale()
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            if (Mathf.Approximately(scale, pixelsPerPoint)) return;
            pixelsPerPoint = scale;
            root.schedule.Execute(Refresh);
        }

        private void DrawTerms()
        {
            var entry = exporter;
            if (entry == null) return;
            EditorGUI.BeginChangeCheck();
            var markAuthor = (MarkedInputs(pressedWhileBlocked) & RequiredInput.Author) != 0;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) entry.DrawTerms(markAuthor);
            authorMarked = markAuthor;
            // The author decides the reason line below.
            if (EditorGUI.EndChangeCheck()) root.schedule.Execute(Refresh);
        }

        private void DrawUploadBar()
        {
            if (flow == null || flow.UploadSize <= 0) return;
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true)), (float)flow.Transferred / flow.UploadSize,
                string.Format(T("{0:N0} / {1:N0} バイト（暗号化済み）"), flow.Transferred, flow.UploadSize));
        }

        private static void Show(VisualElement element, bool visible) => element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        // Hidden but still taking its height, so the rows below never move.
        private static void Reserve(VisualElement element, bool visible) => element.style.visibility = visible ? Visibility.Visible : Visibility.Hidden;

        internal void Refresh()
        {
            if (!built) return;
            var enabled = CloudTransferAvailability.Enabled;
            disabledBox.text = enabled ? "" : T(CloudTransferAvailability.DisabledMessage);
            Show(disabledBox, !enabled);
            Show(content, enabled);
            if (!enabled)
            {
                StopAndClean();
                return;
            }
            if (Window != null)
            {
                var title = avatar != null ? string.Format(T(AppTransferText.TitleWithAvatar), avatar.name) : T(AppTransferText.Title);
                if (Window.titleContent.text != title || Window.titleContent.image != null) Window.titleContent = new GUIContent(title);
            }
            var view = shownView = Flow.View;
            var sending = view != AppTransferView.Form;
            heading.text = T(detailed ? AppTransferText.HeadingDetailed : AppTransferText.HeadingQuick);
            subtitle.text = sending ? string.Format(T(AppTransferText.Sending), avatarName.Trim()) : "";
            Show(subtitle, sending);
            Show(form, !sending);
            Show(transfer, sending);
            if (sending) RefreshTransfer(view);
            else RefreshForm();
            RefreshSaveRow();
            RefreshActions(sending);
        }

        // One row of buttons under both screens: "戻る" at the left on the board, then at the right
        // "VRMを保存" and "QRコードを作る", which the QR screen turns into "QRを作り直す" of the same width,
        // so "VRMを保存" stays where it was. The window's QR screen adds "閉じる" at the end.
        private void RefreshActions(bool sending)
        {
            backButton.text = T("戻る");
            Show(createButton, !sending);
            Show(regenerateButton, sending);
            Show(closeButton, sending && !embedded);
            // The order stays; only the blue moves, to one button by where this opened: "VRMを保存" on the
            // board opened from "VRMを書き出す", "QRコードを作る" from "アプリに転送" and in the window.
            // Re-read on every refresh.
            var saveFirst = savePrimary != null && savePrimary();
            saveButton.EnableInClassList("primary", saveFirst);
            createButton.EnableInClassList("primary", !saveFirst);
        }

        private void RefreshForm()
        {
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;
            var entry = Exporter;
            problemBox.text = entry == null ? T(exporterProblem) : "";
            Show(problemBox, entry == null);
            diagnosticsButton.text = T("VR Vlog 動作環境");
            Show(diagnosticsButton, entry == null);
            playingBox.text = T("Playモードを終了すると編集できます");
            // The board says this above its pages.
            Show(playingBox, playing && !embedded);
            formFields.SetEnabled(!playing && entry != null);
            // The terms and the consent sit above the button, outside the fields, and lock with them.
            consentFields.SetEnabled(!playing && entry != null);

            avatarLabel.text = T(AppTransferText.Avatar);
            if (avatarField.value != avatar) avatarField.SetValueWithoutNotify(avatar);
            // What the export takes from elsewhere, and where to change it.
            summaryText.text = entry != null ? entry.SettingsSummary(avatar, detailed) : "";
            summaryButton.text = T(!detailed ? AppTransferText.EditDetails : BoardReady ? AppTransferText.BackToBoard : AppTransferText.OpenBoard);
            summaryButton.SetEnabled(avatar != null && !Transferring);
            nameLabel.text = T(AppTransferText.Name);
            if (nameField.value != avatarName) nameField.SetValueWithoutNotify(avatarName);
            nameHint.text = T(AppTransferText.NameHint);
            termsLabel.text = T(AppTransferText.Terms);
            readTerms.text = T(AppTransferText.ReadTerms);
            consentText.text = AppTransferFlow.ConsentText;
            consentToggle.SetValueWithoutNotify(consented);
            // The same saved choice, link and wording as "スマホに送る".
            if (!CloudVrmTransferWindow.PrivacyConsentAccepted) showPrivacy = true;
            Show(privacyRow, showPrivacy);
            privacyLink.text = T("プライバシーポリシーを開く");
            privacyText.text = T("プライバシーポリシーに同意する");
            privacyToggle.SetValueWithoutNotify(CloudVrmTransferWindow.PrivacyConsentAccepted);

            // Only the board's reasons get a line; a missing input turns red instead, so the line is gone otherwise.
            var reason = entry == null ? null : Reason();
            reasonLabel.text = AppTransferFlow.ReasonText(reason, T);
            reasonLabel.tooltip = reasonLabel.text;
            Show(reasonLabel, reasonLabel.text.Length > 0);
            var marked = entry == null ? RequiredInput.None : MarkedInputs(pressedWhileBlocked);
            avatarLabel.EnableInClassList("missing", (marked & RequiredInput.Avatar) != 0);
            nameLabel.EnableInClassList("missing", (marked & RequiredInput.Name) != 0);
            nameHint.EnableInClassList("missing", (marked & RequiredInput.Name) != 0);
            consentText.EnableInClassList("missing", (marked & RequiredInput.Consent) != 0);
            privacyText.EnableInClassList("missing", (marked & RequiredInput.PrivacyConsent) != 0);
            // The author is the IMGUI section, which draws its red itself once asked to draw again.
            if (authorMarked != ((marked & RequiredInput.Author) != 0)) { authorMarked = !authorMarked; termsView.MarkDirtyRepaint(); }
            createButton.text = T(AppTransferText.Create);
            // Pressed while an input is missing, it marks that input; only the board's own problems keep it disabled.
            createButton.SetEnabled(!playing && entry != null && !AppTransferFlow.BoardBlocks(reason) && (reason == null || MarkedInputs(true) != RequiredInput.None));
        }

        private void RefreshTransfer(AppTransferView view)
        {
            // The saved export does not look like the avatar. Shown once per export, from its upload on,
            // so it does not appear above a QR that is already on screen.
            var warnShaders = missingShaders > 0 && view != AppTransferView.Exporting;
            missingShadersBox.text = warnShaders ? string.Format(T(MissingShadersText), missingShaders) : "";
            Show(missingShadersBox, warnShaders);
            // While there is a transfer to lose. The line keeps its height in the other states.
            leaveNote.text = T(AppTransferText.LeavingCancels);
            Reserve(leaveNote, view == AppTransferView.Uploading || view == AppTransferView.Waiting);
            // The area keeps the size of the QR through every state, so the rows below never move.
            slotRow.style.height = SlotSide;
            var waiting = view == AppTransferView.Waiting;
            var showQr = waiting && qrTexture != null;
            // A ready QR waits behind a blurred picture that is not made from it, with "QRを表示" on top.
            var covered = waiting && !showQr;
            slot.EnableInClassList("slot--qr", showQr || covered);
            qrImage.image = showQr ? qrTexture : null;
            Show(qrImage, showQr);
            Show(qrCover, covered);
            Show(revealArea, covered);
            showQrButton.text = T(AppTransferText.ShowQr);

            // A failure or an ended transfer says why in place, with the one thing to do next.
            string title = null, body = null, button = null;
            switch (view)
            {
                case AppTransferView.Exporting:
                    title = T(AppTransferText.Exporting);
                    break;
                case AppTransferView.Expired:
                    // The grey area keeps the QR's size; nothing readable is drawn after the deadline.
                    title = T(AppTransferText.Expired); button = T(AppTransferText.Regenerate);
                    break;
                case AppTransferView.Canceled:
                    title = T("転送を中止しました"); button = T(AppTransferText.Regenerate);
                    break;
                case AppTransferView.Completed:
                    title = T(AppTransferText.Completed);
                    break;
                case AppTransferView.Failed:
                    title = T(flow.ExportDidFail ? AppTransferText.ExportFailed : "転送できませんでした");
                    body = flow.FailureText(T); button = T(AppTransferText.Retry);
                    break;
            }
            Show(slotMessage, title != null);
            messageTitle.text = title ?? "";
            messageBody.text = body ?? "";
            Show(messageBody, !string.IsNullOrEmpty(body));
            messageButton.text = button ?? "";
            Show(messageButton, button != null);
            var uploading = view == AppTransferView.Uploading;
            Show(slotUpload, uploading);
            if (uploading)
            {
                uploadTitle.text = T(flow.Encrypting ? "転送を準備しています" : "アップロード中");
                Reserve(uploadBar, flow.UploadSize > 0);
                uploadBar.MarkDirtyRepaint();
            }
            if (view == AppTransferView.Exporting) slot.MarkDirtyRepaint();
            PlaceSlot();

            scanLabel.text = showQr ? T(AppTransferText.ScanWithCamera) : "";
            hideQrButton.text = T(AppTransferText.HideQr);
            Show(hideQrButton, showQr);
            warningText.text = T(AppTransferText.DoNotShare);
            FitWarning();
            // Its place is kept in every state, so it does not move when the QR appears.
            Reserve(warningRow, AppTransferFlow.ShowsDoNotShare(view));
            var counting = waiting || view == AppTransferView.Expired;
            statusLabel.text = waiting ? T(flow.StatusUnknown ? AppTransferText.StatusUnknown : showQr ? AppTransferText.Waiting : AppTransferText.QrHidden) : "";
            var seconds = waiting ? flow.Remaining : 0;
            remainingLabel.text = counting ? string.Format(T("残り {0}:{1:00}"), seconds / 60, seconds % 60) : "";
            remainingLabel.EnableInClassList("expired", view == AppTransferView.Expired);
            barFill.style.width = Length.Percent(waiting ? flow.RemainingFraction * 100f : 0f);
            lifetimeLabel.text = counting ? string.Format(T(AppTransferText.Lifetime), CloudTransferProtocol.LifetimeMinutes) : "";
            regenerateButton.text = T(AppTransferText.Regenerate);
            // Its place stays while it is hidden, so "VRMを保存" next to it does not move.
            Reserve(regenerateButton, waiting);
            closeButton.text = T("閉じる");
            soundToggle.text = soundToggle.tooltip = T(AppTransferText.QrReadySound);
            soundToggle.SetValueWithoutNotify(AppTransferWindow.QrReadySound);
        }

        // UI Toolkit can centre on half a device pixel; the QR's modules need whole ones.
        private void PlaceSlot()
        {
            if (!built || shownView == AppTransferView.Form) return;
            var side = SlotSide;
            var row = slotRow.worldBound;
            if (float.IsNaN(row.width) || row.width <= 0f) return;
            slot.style.left = Snap(row.x + (row.width - side) / 2f) - row.x;
            slot.style.top = Snap(row.y) - row.y;
            slot.style.width = side;
            slot.style.height = side;
            if (qrTexture == null) return;
            var qr = Mathf.Min(QrSide(qrTexture.width), side);
            var offset = Snap((side - qr) / 2f);
            qrImage.style.left = offset;
            qrImage.style.top = offset;
            qrImage.style.width = qr;
            qrImage.style.height = qr;
        }

        internal sealed class ExportEntry
        {
            internal Action<GameObject> SelectAvatar;
            internal Func<bool> HasAuthor;
            internal Action<bool> DrawTerms;
            internal Func<GameObject, string, string, Action, string> Export;
            internal Func<GameObject, string> RememberedName;
            internal Func<GameObject, bool, string> SettingsSummary;
            internal Func<GameObject, string> DetailedProblem;
            internal Func<GameObject, bool> DetailedNeedsFix;
            internal Func<GameObject, bool> EditDetails;
            internal Func<GameObject, string, string, Action, string> ExportDetailed;
            internal Func<int> LastMissingShaderCount;
            internal Func<int> LastOmittedStageCount;
            internal Func<GameObject, string, bool, Action<string, int>, string> SaveCopy;
            internal Func<GameObject, string> SaveFileName;
            internal Func<string> TermsKey;
            internal Action<GameObject, string> RememberTransferName;
            internal Func<GameObject, bool, object> SourceStamp;
            internal Func<object, GameObject, bool> SourceUnchanged;
            internal Func<object, bool> ChoicesUnchanged;

            internal static ExportEntry Resolve()
            {
                try
                {
                    var type = Type.GetType("VRVlog.LilToonExporter.AppTransferExport, VRVlog.LilToonExporter.Editor", false);
                    if (type == null) return null;
                    var target = Activator.CreateInstance(type, true);
                    return new ExportEntry
                    {
                        SelectAvatar = (Action<GameObject>)Delegate.CreateDelegate(typeof(Action<GameObject>), target, "SelectAvatar"),
                        HasAuthor = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), target, "HasAuthor"),
                        DrawTerms = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), target, "DrawTerms"),
                        Export = (Func<GameObject, string, string, Action, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string, string, Action, string>), target, "Export"),
                        RememberedName = (Func<GameObject, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string>), target, "RememberedName"),
                        SettingsSummary = (Func<GameObject, bool, string>)Delegate.CreateDelegate(typeof(Func<GameObject, bool, string>), target, "SettingsSummary"),
                        DetailedProblem = (Func<GameObject, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string>), target, "DetailedProblem"),
                        DetailedNeedsFix = (Func<GameObject, bool>)Delegate.CreateDelegate(typeof(Func<GameObject, bool>), target, "DetailedNeedsFix"),
                        EditDetails = (Func<GameObject, bool>)Delegate.CreateDelegate(typeof(Func<GameObject, bool>), target, "EditDetails"),
                        ExportDetailed = (Func<GameObject, string, string, Action, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string, string, Action, string>), target, "ExportDetailed"),
                        LastMissingShaderCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), target, "LastMissingShaderCount"),
                        LastOmittedStageCount = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), target, "LastOmittedStageCount"),
                        SaveCopy = (Func<GameObject, string, bool, Action<string, int>, string>)Delegate.CreateDelegate(
                            typeof(Func<GameObject, string, bool, Action<string, int>, string>), target, "SaveCopy"),
                        SaveFileName = (Func<GameObject, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string>), target, "SaveFileName"),
                        TermsKey = (Func<string>)Delegate.CreateDelegate(typeof(Func<string>), target, "TermsKey"),
                        RememberTransferName = (Action<GameObject, string>)Delegate.CreateDelegate(typeof(Action<GameObject, string>), target, "RememberTransferName"),
                        SourceStamp = (Func<GameObject, bool, object>)Delegate.CreateDelegate(typeof(Func<GameObject, bool, object>), target, "SourceStamp"),
                        SourceUnchanged = (Func<object, GameObject, bool>)Delegate.CreateDelegate(typeof(Func<object, GameObject, bool>), target, "SourceUnchanged"),
                        ChoicesUnchanged = (Func<object, bool>)Delegate.CreateDelegate(typeof(Func<object, bool>), target, "ChoicesUnchanged"),
                    };
                }
                catch (Exception exception) when (exception is TypeLoadException || exception is FileNotFoundException || exception is FileLoadException
                    || exception is BadImageFormatException || exception is ArgumentException || exception is MissingMemberException
                    || exception is System.Reflection.TargetInvocationException)
                {
                    return null;
                }
            }
        }
    }
}
