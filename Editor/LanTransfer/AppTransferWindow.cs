using System;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // "アプリに転送": opened from the Hierarchy context menu of an avatar. One window takes the
    // name and the consent, exports, uploads the encrypted VRM and shows the QR of the
    // vrvlog://import#... link. AppTransferFlow decides what is shown and when; this class
    // builds the UI Toolkit view (AppTransferWindow.uxml/.uss) and connects it to the Editor.
    public sealed class AppTransferWindow : EditorWindow
    {
        internal const string MenuPath = "GameObject/VR Vlog/アプリに転送…";
        // The Hierarchy context menu lists the GameObject entries of the creation block,
        // which ends at priority 49. That value puts this entry last in it, above Properties.
        internal const int MenuPriority = 49;
        private const float HeaderHeight = 60f, Padding = 16f;
        // Everything of the QR screen except the header and the QR area itself.
        private const float TransferChrome = 206f;
        private static readonly Vector2 FormMinimumSize = new Vector2(420f, 400f);
        // The layout and styles next to this script, found by their asset GUIDs.
        private const string LayoutGuid = "db52aabb2abe4b37909abb5c737b7410", StyleGuid = "6f09c8d6325e47628b30efe33abe0b23";

        [SerializeField] private GameObject avatar;
        [SerializeField] private string avatarName = "";
        // Consent is for the avatar on screen, so it is asked again each time.
        [NonSerialized] private bool consented;
        // The privacy policy row stays once shown, so ticking it does not move the form.
        [NonSerialized] private bool showPrivacy;
        [NonSerialized] private bool? nameFits;
        [NonSerialized] private AppTransferFlow flow;
        [NonSerialized] private ExportEntry exporter;
        [NonSerialized] private bool exporterResolved;
        [NonSerialized] private string exporterProblem;
        [NonSerialized] private Texture2D qrTexture, icon;
        [NonSerialized] private string transferName;
        [NonSerialized] private int placeholderSide;
        [NonSerialized] private int exportAttempt;
        [NonSerialized] private bool exportPainted;
        [NonSerialized] private double exportDeadline;
        [NonSerialized] private double nextRepaint;
        [NonSerialized] private AppTransferView shownView, polledView;
        // Device pixels per point of the panel, read where IMGUI knows it.
        [NonSerialized] private float pixelsPerPoint;

        [NonSerialized] private bool built;
        [NonSerialized] private VisualElement content, form, formFields, privacyRow, transfer, slotRow, slot, slotMessage, slotUpload, warningRow, barFill;
        [NonSerialized] private HelpBox disabledBox, problemBox, playingBox;
        [NonSerialized] private Button diagnosticsButton, createButton, messageButton, regenerateButton, closeButton;
        [NonSerialized] private Label heading, subtitle, avatarLabel, nameLabel, nameHint, termsLabel, readTerms, consentText, privacyLink, privacyText,
            reasonLabel, createHint, messageTitle, messageBody, uploadTitle, scanLabel, warningText, statusLabel, remainingLabel, lifetimeLabel;
        [NonSerialized] private ObjectField avatarField;
        [NonSerialized] private TextField nameField;
        [NonSerialized] private Toggle consentToggle, privacyToggle;
        [NonSerialized] private Image qrImage;
        [NonSerialized] private IMGUIContainer uploadBar;

        [MenuItem(MenuPath, false, MenuPriority)]
        private static void OpenFromMenu(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            if (selected != null) Open(selected);
        }

        // Listed only in versions that transfer, and for one avatar root.
        [MenuItem(MenuPath, true)]
        private static bool CanOpenFromMenu() =>
            CloudTransferAvailability.Enabled && Selection.gameObjects.Length == 1 && IsAvatarRoot(Selection.gameObjects[0]);

        // The top of an avatar: it has an Animator and nothing above it has one.
        internal static bool IsAvatarRoot(GameObject target)
        {
            if (target == null || target.GetComponent<Animator>() == null) return false;
            for (var parent = target.transform.parent; parent != null; parent = parent.parent)
                if (parent.GetComponent<Animator>() != null) return false;
            return true;
        }

        public static void Open(GameObject target)
        {
            foreach (var open in Resources.FindObjectsOfTypeAll<AppTransferWindow>())
            {
                if (!open.Transferring) continue;
                // The same avatar keeps its transfer; only the window comes forward.
                if (open.avatar == target || !ConfirmEndingTransfer()) { open.Focus(); return; }
            }
            var window = GetWindow<AppTransferWindow>(false);
            window.Select(target, true);
            window.minSize = FormMinimumSize;
            window.Show();
        }

        private bool Transferring => flow != null && (flow.View == AppTransferView.Uploading || flow.View == AppTransferView.Waiting);

        // Another avatar cancels the upload or the QR on screen, so it is asked first.
        private static bool ConfirmEndingTransfer() => EditorUtility.DisplayDialog(T("アバターを変更"),
            T("進行中の転送を中止します。表示中のQRは使えなくなります。"), T("変更する"), T("戻る"));

        private static string T(string source) => ExporterLocalization.T(source);
        private Texture2D Icon => icon != null ? icon : icon = AppTransferIcon.Create();
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

        private void Select(GameObject target, bool opened)
        {
            // A recovery still pending for the previous avatar must not start an upload.
            exportAttempt++;
            flow?.Stop();
            ClearQr();
            // A name left over from an avatar that no longer exists is not the user's choice for this one.
            var untouched = opened || avatar == null || !AppTransferFlow.HasListedName(avatarName) || avatarName == avatar.name;
            avatar = target;
            if (untouched) avatarName = target != null ? target.name : "";
            consented = false;
            nameFits = null;
            Exporter?.SelectAvatar(target);
            minSize = FormMinimumSize;
            Refresh();
        }

        private void OnEnable()
        {
            EditorApplication.update += Poll;
            EditorApplication.quitting += StopAndClean;
            AssemblyReloadEvents.beforeAssemblyReload += StopAndClean;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Poll;
            EditorApplication.quitting -= StopAndClean;
            AssemblyReloadEvents.beforeAssemblyReload -= StopAndClean;
            StopAndClean();
            if (icon != null) DestroyImmediate(icon);
            icon = null;
        }

        // Closing cancels the transfer and deletes the exported copy. Nothing of
        // the link, key or token is written to a log, a preference or a file.
        private void StopAndClean()
        {
            exportAttempt++;
            flow?.Stop();
            ClearQr();
        }

        private void ClearQr()
        {
            if (qrImage != null) qrImage.image = null;
            if (qrTexture != null) DestroyImmediate(qrTexture);
            qrTexture = null;
        }

        private void Poll()
        {
            var time = EditorApplication.timeSinceStartup;
            // Until CreateGUI has run there is no flow; the form also refreshes every 0.2 s below,
            // so it follows the Editor (Play mode, the privacy choice shared with "スマホに送る").
            if (flow == null) return;
            if (!CloudTransferAvailability.Enabled) { StopAndClean(); return; }
            if (flow.View == AppTransferView.Exporting)
            {
                // Exporting blocks the Editor: start once the window has painted that state.
                // A hidden tab never paints, so do not wait for it longer than a moment.
                if (exportPainted || time >= exportDeadline) RunExport();
                return;
            }
            flow.Tick(time);
            var view = flow.View;
            if (view == AppTransferView.Waiting) { if (qrTexture == null) ShowQr(); }
            else ClearQr();
            view = flow.View;
            if (view == polledView && time < nextRepaint) return;
            polledView = view;
            nextRepaint = time + 0.2;
            Refresh();
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

        private string Reason()
        {
            if (nameFits == null) nameFits = AppTransferFlow.NameFits(avatarName);
            return AppTransferFlow.BlockReason(avatar != null, avatarName, nameFits.Value, Exporter?.HasAuthor() ?? false, consented,
                CloudVrmTransferWindow.PrivacyConsentAccepted);
        }

        private void Begin()
        {
            if (Exporter == null || EditorApplication.isPlayingOrWillChangePlaymode || Reason() != null) return;
            transferName = AppTransferFlow.TransferName(avatarName);
            placeholderSide = TransferQrCode.Encode(AppTransferFlow.PlaceholderLink(transferName)).Width;
            Flow.BeginExport();
            ExpectExportPaint();
            FitTransferSize();
            Refresh();
        }

        // A new QR uploads again from the exported copy. After a failed export there is none, so export again.
        private void Retry()
        {
            ClearQr();
            // The privacy policy choice is shared with "スマホに送る" and can be withdrawn there.
            // Exporting again needs an author; without one, return to the form where the field says so.
            if (!CloudVrmTransferWindow.PrivacyConsentAccepted || !flow.CanRegenerate && Exporter != null && !Exporter.HasAuthor())
            {
                flow.Stop();
                minSize = FormMinimumSize;
                Refresh();
                return;
            }
            if (flow.CanRegenerate) flow.Regenerate();
            else if (Exporter != null && !EditorApplication.isPlayingOrWillChangePlaymode) { flow.BeginExport(); ExpectExportPaint(); }
            Refresh();
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
            var path = AppTransferFlow.NewSnapshotPath();
            var target = avatar;
            var name = avatarName.Trim();
            var wireName = transferName;
            var saved = false;
            void Saved()
            {
                // The recovery window can save long after this attempt was replaced or the window closed.
                if (this == null || flow == null || attempt != exportAttempt) { AppTransferFlow.Discard(path); return; }
                saved = true;
                // One export feeds one upload: a second save of the same attempt is ignored.
                exportAttempt++;
                flow.Upload(path, wireName);
                Refresh();
            }
            string failure;
            try { failure = Exporter.Export(target, name, path, Saved); }
            catch (Exception exception) { failure = exception.Message ?? ""; AppTransferFlow.Discard(path); }
            finally { EditorUtility.ClearProgressBar(); }
            if (saved || attempt != exportAttempt) return;
            if (failure == null) { flow.ExportCanceled(); minSize = FormMinimumSize; }
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
                var scale = ScaleFactor;
                var smaller = side - 4 >= 2 * TransferQrCode.QuietZone + 21 ? TransferQrCode.DisplaySide(side - 4, scale) : 0f;
                return Mathf.Max(TransferQrCode.DisplaySide(side, scale), smaller);
            }
        }

        // The window is at least as large as the QR it is about to show.
        private void FitTransferSize()
        {
            var side = SlotSide;
            var size = new Vector2(Mathf.Max(FormMinimumSize.x, side + 2f * Padding), HeaderHeight + side + TransferChrome);
            minSize = size;
            if (docked) return;
            var rect = position;
            if (rect.width < size.x || rect.height < size.y)
                position = new Rect(rect.x, rect.y, Mathf.Max(rect.width, size.x), Mathf.Max(rect.height, size.y));
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
            var script = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));
            return string.IsNullOrEmpty(script) ? null : AssetDatabase.LoadAssetAtPath<TAsset>(Path.ChangeExtension(script, extension));
        }

        // The UXML holds the structure only; every text is set in Refresh through T.
        private void CreateGUI()
        {
            var tree = Load<VisualTreeAsset>(LayoutGuid, ".uxml");
            var sheet = Load<StyleSheet>(StyleGuid, ".uss");
            var root = rootVisualElement;
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

            disabledBox = root.Q<HelpBox>("disabled");
            content = root.Q("content");
            heading = root.Q<Label>("heading");
            subtitle = root.Q<Label>("subtitle");
            form = root.Q("form");
            problemBox = root.Q<HelpBox>("exporter-problem");
            diagnosticsButton = root.Q<Button>("diagnostics");
            playingBox = root.Q<HelpBox>("playing");
            formFields = root.Q("form-fields");
            avatarLabel = root.Q<Label>("avatar-label");
            avatarField = root.Q<ObjectField>("avatar-field");
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
            createHint = root.Q<Label>("create-hint");
            transfer = root.Q("transfer");
            slotRow = root.Q("slot-row");
            slot = root.Q("slot");
            qrImage = root.Q<Image>("qr");
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
            regenerateButton = root.Q<Button>("regenerate");
            closeButton = root.Q<Button>("close");

            var headerIcon = root.Q<Image>("header-icon");
            headerIcon.image = Icon;
            headerIcon.scaleMode = ScaleMode.ScaleToFit;
            var warningIcon = root.Q<Image>("warning-icon");
            warningIcon.image = EditorGUIUtility.IconContent("console.warnicon.sml").image;
            warningIcon.scaleMode = ScaleMode.ScaleToFit;
            qrImage.scaleMode = ScaleMode.StretchToFill;
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
            root.Q<IMGUIContainer>("terms").onGUIHandler = DrawTerms;
            diagnosticsButton.clicked += DependencyDiagnostics.OpenDiagnostics;
            createButton.clicked += Begin;
            messageButton.clicked += Retry;
            regenerateButton.clicked += Retry;
            closeButton.clicked += Close;
            uploadBar.onGUIHandler = DrawUploadBar;
            // The export starts once the panel has drawn the Exporting state.
            slot.generateVisualContent += _ => { if (shownView == AppTransferView.Exporting) exportPainted = true; };
            slotRow.RegisterCallback<GeometryChangedEvent>(_ => PlaceSlot());
            root.Q<ScrollView>("transfer-scroll").verticalScroller.valueChanged += _ => PlaceSlot();
            built = true;
            Refresh();
        }

        private void ProbeScale()
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            if (Mathf.Approximately(scale, pixelsPerPoint)) return;
            pixelsPerPoint = scale;
            rootVisualElement.schedule.Execute(Refresh);
        }

        private void DrawTerms()
        {
            var entry = exporter;
            if (entry == null) return;
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) entry.DrawTerms();
            // The author decides the reason line below.
            if (EditorGUI.EndChangeCheck()) rootVisualElement.schedule.Execute(Refresh);
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

        private void Refresh()
        {
            if (!built || this == null) return;
            var enabled = CloudTransferAvailability.Enabled;
            disabledBox.text = enabled ? "" : T(CloudTransferAvailability.DisabledMessage);
            Show(disabledBox, !enabled);
            Show(content, enabled);
            if (!enabled)
            {
                StopAndClean();
                return;
            }
            var title = avatar != null ? string.Format(T(AppTransferText.TitleWithAvatar), avatar.name) : T(AppTransferText.Title);
            if (titleContent.text != title || titleContent.image != Icon) titleContent = new GUIContent(title, Icon);
            var view = shownView = Flow.View;
            var sending = view != AppTransferView.Form;
            heading.text = T(AppTransferText.Heading);
            subtitle.text = sending ? string.Format(T(AppTransferText.Sending), avatarName.Trim()) : "";
            Show(subtitle, sending);
            Show(form, !sending);
            Show(transfer, sending);
            if (sending) RefreshTransfer(view);
            else RefreshForm();
        }

        private void RefreshForm()
        {
            var playing = EditorApplication.isPlayingOrWillChangePlaymode;
            var entry = Exporter;
            problemBox.text = entry == null ? T(exporterProblem) : "";
            Show(problemBox, entry == null);
            diagnosticsButton.text = T("VR Vlog 動作環境");
            Show(diagnosticsButton, entry == null);
            playingBox.text = T("Playモードを終了すると編集できます。");
            Show(playingBox, playing);
            formFields.SetEnabled(!playing && entry != null);

            avatarLabel.text = T("アバター");
            if (avatarField.value != avatar) avatarField.SetValueWithoutNotify(avatar);
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

            // One fixed line says why the button is disabled, so nothing below it moves.
            var reason = entry == null ? null : Reason();
            reasonLabel.text = reason == null ? "" : T(reason);
            createButton.text = T(AppTransferText.Create);
            createButton.SetEnabled(!playing && entry != null && reason == null);
            createHint.text = T(AppTransferText.CreateHint);
        }

        private void RefreshTransfer(AppTransferView view)
        {
            // The area keeps the size of the QR through every state, so the rows below never move.
            slotRow.style.height = SlotSide;
            var showQr = view == AppTransferView.Waiting && qrTexture != null;
            slot.EnableInClassList("slot--qr", showQr);
            qrImage.image = showQr ? qrTexture : null;
            Show(qrImage, showQr);

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

            var waiting = view == AppTransferView.Waiting;
            scanLabel.text = waiting ? T(AppTransferText.ScanWithCamera) : "";
            warningText.text = T(AppTransferText.DoNotShare);
            Reserve(warningRow, waiting);
            var counting = waiting || view == AppTransferView.Expired;
            statusLabel.text = waiting ? T(flow.StatusUnknown ? AppTransferText.StatusUnknown : AppTransferText.Waiting) : "";
            var seconds = waiting ? flow.Remaining : 0;
            remainingLabel.text = counting ? string.Format(T("残り {0}:{1:00}"), seconds / 60, seconds % 60) : "";
            remainingLabel.EnableInClassList("expired", view == AppTransferView.Expired);
            barFill.style.width = Length.Percent(waiting ? flow.RemainingFraction * 100f : 0f);
            lifetimeLabel.text = counting ? string.Format(T(AppTransferText.Lifetime), CloudTransferProtocol.LifetimeMinutes) : "";
            regenerateButton.text = T(AppTransferText.Regenerate);
            Show(regenerateButton, waiting);
            closeButton.text = T("閉じる");
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
            var qr = Mathf.Min(TransferQrCode.DisplaySide(qrTexture.width, ScaleFactor), side);
            var offset = Snap((side - qr) / 2f);
            qrImage.style.left = offset;
            qrImage.style.top = offset;
            qrImage.style.width = qr;
            qrImage.style.height = qr;
        }

        private sealed class ExportEntry
        {
            internal Action<GameObject> SelectAvatar;
            internal Func<bool> HasAuthor;
            internal Action DrawTerms;
            internal Func<GameObject, string, string, Action, string> Export;

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
                        DrawTerms = (Action)Delegate.CreateDelegate(typeof(Action), target, "DrawTerms"),
                        Export = (Func<GameObject, string, string, Action, string>)Delegate.CreateDelegate(typeof(Func<GameObject, string, string, Action, string>), target, "Export"),
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
