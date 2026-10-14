using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // "アプリに転送": opened from the Hierarchy context menu of an avatar. One window takes the
    // name and the consent, exports, uploads the encrypted VRM and shows the QR of the
    // vrvlog://import#... link. AppTransferFlow decides what is shown and when; this class
    // draws it and connects it to the Editor.
    public sealed class AppTransferWindow : EditorWindow
    {
        internal const string MenuPath = "GameObject/VR Vlog/アプリに転送…";
        // The Hierarchy context menu lists the GameObject entries of the creation block,
        // which ends at priority 49. That value puts this entry last in it, above Properties.
        internal const int MenuPriority = 49;
        private const float HeaderHeight = 60f, LabelWidth = 64f, Padding = 16f;
        // Everything of the QR screen except the header and the QR area itself.
        private const float TransferChrome = 184f;
        private static readonly Vector2 FormMinimumSize = new Vector2(420f, 400f);
        // The one accent: the primary button and the remaining-time bar.
        private static readonly Color Accent = new Color(0.24f, 0.49f, 0.85f);
        private static readonly Color AccentTint = new Color(0.5f, 0.74f, 1f);

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
        [NonSerialized] private bool qrIsPlaceholder;
        [NonSerialized] private string transferName;
        [NonSerialized] private int placeholderSide;
        [NonSerialized] private int exportAttempt;
        [NonSerialized] private bool exportPainted;
        [NonSerialized] private double exportDeadline;
        [NonSerialized] private double nextRepaint;
        [NonSerialized] private AppTransferView drawnView, polledView;
        [NonSerialized] private Vector2 scroll;
        [NonSerialized] private GUIStyle bodyStyle, footerStyle, headingStyle, subStyle, hintStyle, consentStyle, linkStyle, reasonStyle,
            centerStyle, centerBoldStyle, statusStyle, remainingStyle, expiredStyle, noteStyle, primaryStyle;

        [MenuItem(MenuPath, false, MenuPriority)]
        private static void OpenFromMenu(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            if (selected != null) Open(selected);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanOpenFromMenu() => Selection.gameObjects.Length == 1;

        public static void Open(GameObject target)
        {
            var window = GetWindow<AppTransferWindow>(false);
            window.Select(target, true);
            window.minSize = FormMinimumSize;
            window.Show();
        }

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
                exporter = ExportEntry.Resolve();
                if (exporter != null) exporter.SelectAvatar(avatar);
                else exporterProblem = DependencyDiagnostics.StartupError(DependencyDiagnostics.Installed())
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
            var untouched = opened || avatar == null || string.IsNullOrWhiteSpace(avatarName) || avatarName == avatar.name;
            avatar = target;
            if (untouched) avatarName = target != null ? target.name : "";
            consented = false;
            nameFits = null;
            Exporter?.SelectAvatar(target);
            minSize = FormMinimumSize;
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
            if (qrTexture != null) DestroyImmediate(qrTexture);
            qrTexture = null;
            qrIsPlaceholder = false;
        }

        private void Poll()
        {
            if (flow == null) return;
            if (!CloudTransferAvailability.Enabled) { StopAndClean(); return; }
            var time = EditorApplication.timeSinceStartup;
            if (flow.View == AppTransferView.Exporting)
            {
                // Exporting blocks the Editor: start once the window has painted that state.
                // A hidden tab never paints, so do not wait for it longer than a moment.
                if (exportPainted || time >= exportDeadline) RunExport();
                return;
            }
            flow.Tick(time);
            var view = flow.View;
            if (view == AppTransferView.Waiting) { if (qrTexture == null || qrIsPlaceholder) ShowQr(); }
            else if (view == AppTransferView.Expired) { if (qrTexture == null || !qrIsPlaceholder) ShowExpiredQr(); }
            else ClearQr();
            view = flow.View;
            if (view == polledView && (view == AppTransferView.Form || time < nextRepaint)) return;
            polledView = view;
            nextRepaint = time + 0.2;
            Repaint();
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

        // An expired QR is replaced by one of the same size that holds no key or token.
        private void ShowExpiredQr()
        {
            ClearQr();
            try { qrTexture = CloudVrmTransferWindow.RenderQr(AppTransferFlow.PlaceholderLink(transferName)); qrIsPlaceholder = true; }
            catch { qrTexture = null; }
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
            Repaint();
        }

        // A new QR uploads again from the exported copy. After a failed export there is none, so export again.
        private void Retry()
        {
            ClearQr();
            // The privacy policy choice is shared with "スマホに送る" and can be withdrawn there.
            if (!CloudVrmTransferWindow.PrivacyConsentAccepted)
            {
                flow.Stop();
                minSize = FormMinimumSize;
                Repaint();
                return;
            }
            if (flow.CanRegenerate) flow.Regenerate();
            else if (Exporter != null && !EditorApplication.isPlayingOrWillChangePlaymode) { flow.BeginExport(); ExpectExportPaint(); }
            Repaint();
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
                Repaint();
            }
            string failure;
            try { failure = Exporter.Export(target, name, path, Saved); }
            catch (Exception exception) { failure = exception.Message ?? ""; AppTransferFlow.Discard(path); }
            finally { EditorUtility.ClearProgressBar(); }
            if (saved || attempt != exportAttempt) return;
            if (failure == null) { flow.ExportCanceled(); minSize = FormMinimumSize; }
            else flow.ExportFailed(failure);
            Repaint();
        }

        // The placeholder has the longest numbers a link can have, so the real QR is the same
        // version or one smaller. Reserve the larger of the two drawn sizes for whole-pixel modules.
        private float SlotSide
        {
            get
            {
                var side = Mathf.Max(placeholderSide, 2 * TransferQrCode.QuietZone + 21);
                var scale = EditorGUIUtility.pixelsPerPoint;
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

        private void OnGUI()
        {
            if (!CloudTransferAvailability.Enabled)
            {
                StopAndClean();
                EditorGUILayout.HelpBox(T(CloudTransferAvailability.DisabledMessage), MessageType.Info);
                return;
            }
            EnsureStyles();
            var title = avatar != null ? string.Format(T(AppTransferText.TitleWithAvatar), avatar.name) : T(AppTransferText.Title);
            if (titleContent.text != title || titleContent.image != Icon) titleContent = new GUIContent(title, Icon);
            // The update callback can change the view between the layout and repaint passes of one frame.
            if (Event.current.type == EventType.Layout) drawnView = Flow.View;
            var view = drawnView;
            DrawHeader(view != AppTransferView.Form);
            if (view == AppTransferView.Form) DrawForm();
            else DrawTransfer(view);
            if (view == AppTransferView.Exporting && Event.current.type == EventType.Repaint) exportPainted = true;
        }

        private void EnsureStyles()
        {
            if (bodyStyle != null) return;
            var grey = EditorStyles.centeredGreyMiniLabel.normal.textColor;
            var error = EditorGUIUtility.isProSkin ? new Color(0.96f, 0.42f, 0.4f) : new Color(0.72f, 0.14f, 0.12f);
            bodyStyle = new GUIStyle { padding = new RectOffset((int)Padding, (int)Padding, (int)Padding, (int)Padding) };
            footerStyle = new GUIStyle { padding = new RectOffset((int)Padding, (int)Padding, 10, 10) };
            headingStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            subStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = grey } };
            hintStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = grey } };
            consentStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
            linkStyle = new GUIStyle(EditorStyles.linkLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            reasonStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            centerStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            centerBoldStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, wordWrap = true, fontSize = 13 };
            statusStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            remainingStyle = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip, wordWrap = false };
            expiredStyle = new GUIStyle(remainingStyle) { normal = { textColor = error } };
            noteStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip, wordWrap = false, normal = { textColor = grey } };
            primaryStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };
        }

        private static Color Hairline => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.14f) : new Color(0f, 0f, 0f, 0.18f);
        private static Color Neutral => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0.07f);

        private static void DrawHairline() => EditorGUI.DrawRect(GUILayoutUtility.GetRect(0f, 1f, GUILayout.ExpandWidth(true)), Hairline);

        private void DrawHeader(bool sending)
        {
            var rect = GUILayoutUtility.GetRect(0f, HeaderHeight, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(new Rect(rect.x + Padding, rect.y + 12f, 36f, 36f), Icon, ScaleMode.ScaleToFit);
            var text = new Rect(rect.x + Padding + 46f, rect.y, Mathf.Max(0f, rect.width - 2f * Padding - 46f), 22f);
            text.y = rect.y + (sending ? 10f : 19f);
            GUI.Label(text, T(AppTransferText.Heading), headingStyle);
            if (sending) GUI.Label(new Rect(text.x, rect.y + 34f, text.width, 16f), string.Format(T(AppTransferText.Sending), avatarName.Trim()), subStyle);
            DrawHairline();
        }

        // Label column and field column have fixed positions, whatever the label says.
        private static Rect Field(Rect row) => new Rect(row.x + LabelWidth, row.y, Mathf.Max(0f, row.width - LabelWidth), row.height);
        private static Rect Label(Rect row) => new Rect(row.x, row.y, LabelWidth - 4f, row.height);

        private void DrawForm()
        {
            using (var scrolling = new EditorGUILayout.ScrollViewScope(scroll))
            using (new EditorGUILayout.VerticalScope(bodyStyle))
            {
                scroll = scrolling.scrollPosition;
                var playing = EditorApplication.isPlayingOrWillChangePlaymode;
                var entry = Exporter;
                if (entry == null)
                {
                    EditorGUILayout.HelpBox(T(exporterProblem), MessageType.Warning);
                    if (GUILayout.Button(T("VR Vlog 動作環境"))) DependencyDiagnostics.OpenDiagnostics();
                    EditorGUILayout.Space(8f);
                }
                if (playing) EditorGUILayout.HelpBox(T("Playモードを終了すると編集できます。"), MessageType.Info);
                using (new EditorGUI.DisabledScope(playing || entry == null))
                {
                    var row = EditorGUILayout.GetControlRect(false, 22f);
                    GUI.Label(Label(row), T("アバター"), EditorStyles.label);
                    var selected = EditorGUI.ObjectField(Field(row), avatar, typeof(GameObject), true) as GameObject;
                    if (selected != avatar) Select(selected, false);

                    EditorGUILayout.Space(6f);
                    row = EditorGUILayout.GetControlRect(false, 22f);
                    GUI.Label(Label(row), T(AppTransferText.Name), EditorStyles.boldLabel);
                    EditorGUI.BeginChangeCheck();
                    avatarName = EditorGUI.TextField(Field(row), avatarName);
                    if (EditorGUI.EndChangeCheck()) nameFits = null;
                    GUI.Label(Field(EditorGUILayout.GetControlRect(false, 16f)), T(AppTransferText.NameHint), subStyle);

                    EditorGUILayout.Space(8f);
                    DrawHairline();
                    EditorGUILayout.Space(8f);
                    row = EditorGUILayout.GetControlRect(false, 20f);
                    GUI.Label(Label(row), T(AppTransferText.Terms), EditorStyles.boldLabel);
                    var link = Field(row);
                    link.width = Mathf.Min(link.width, linkStyle.CalcSize(new GUIContent(T(AppTransferText.ReadTerms))).x);
                    EditorGUIUtility.AddCursorRect(link, MouseCursor.Link);
                    if (GUI.Button(link, T(AppTransferText.ReadTerms), linkStyle)) Application.OpenURL(AppTransferFlow.TermsUrl);
                    // The consent sentence wraps inside its own width and is never shortened.
                    var consentHeight = Mathf.Max(EditorGUIUtility.singleLineHeight,
                        consentStyle.CalcHeight(new GUIContent(AppTransferFlow.ConsentText), Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 2f * Padding - 34f)));
                    consented = EditorGUILayout.ToggleLeft(AppTransferFlow.ConsentText, consented, consentStyle, GUILayout.Height(consentHeight));
                    // The same saved choice, link and wording as "スマホに送る".
                    if (!CloudVrmTransferWindow.PrivacyConsentAccepted) showPrivacy = true;
                    if (showPrivacy)
                    {
                        EditorGUILayout.Space(6f);
                        var policy = Field(EditorGUILayout.GetControlRect(false, 20f));
                        policy.width = Mathf.Min(policy.width, linkStyle.CalcSize(new GUIContent(T("プライバシーポリシーを開く"))).x);
                        EditorGUIUtility.AddCursorRect(policy, MouseCursor.Link);
                        if (GUI.Button(policy, T("プライバシーポリシーを開く"), linkStyle)) Application.OpenURL(CloudVrmTransferWindow.PrivacyPolicyUrl);
                        var privacy = T("プライバシーポリシーに同意する");
                        var privacyHeight = Mathf.Max(EditorGUIUtility.singleLineHeight,
                            consentStyle.CalcHeight(new GUIContent(privacy), Mathf.Max(100f, EditorGUIUtility.currentViewWidth - 2f * Padding - 34f)));
                        var accepted = EditorGUILayout.ToggleLeft(privacy, CloudVrmTransferWindow.PrivacyConsentAccepted, consentStyle, GUILayout.Height(privacyHeight));
                        if (accepted != CloudVrmTransferWindow.PrivacyConsentAccepted) CloudVrmTransferWindow.PrivacyConsentAccepted = accepted;
                    }

                    EditorGUILayout.Space(8f);
                    entry?.DrawTerms();
                    EditorGUILayout.Space(8f);
                    DrawHairline();
                    EditorGUILayout.Space(8f);
                }
                // One fixed line says why the button is disabled, so nothing below it moves.
                var reason = entry == null ? null : Reason();
                GUI.Label(EditorGUILayout.GetControlRect(false, 18f), reason == null ? "" : T(reason), reasonStyle);
                using (new EditorGUI.DisabledScope(playing || entry == null || reason != null))
                    if (PrimaryButton(EditorGUILayout.GetControlRect(false, 34f), T(AppTransferText.Create))) Begin();
                EditorGUILayout.Space(6f);
                GUILayout.Label(T(AppTransferText.CreateHint), hintStyle);
            }
        }

        private bool PrimaryButton(Rect rect, string text)
        {
            var previous = GUI.backgroundColor;
            if (GUI.enabled) GUI.backgroundColor = AccentTint;
            var pressed = GUI.Button(rect, text, primaryStyle);
            GUI.backgroundColor = previous;
            return pressed;
        }

        private void DrawTransfer(AppTransferView view)
        {
            using (var scrolling = new EditorGUILayout.ScrollViewScope(scroll))
            using (new EditorGUILayout.VerticalScope(bodyStyle))
            {
                scroll = scrolling.scrollPosition;
                // The area keeps the size of the QR through every state, so the rows below never move.
                var side = SlotSide;
                var area = GUILayoutUtility.GetRect(side, side, GUILayout.ExpandWidth(true));
                DrawSlot(new Rect(Snap(area.x + (area.width - side) / 2f), Snap(area.y), side, side), view);
                EditorGUILayout.Space(8f);
                GUI.Label(EditorGUILayout.GetControlRect(false, 18f), view == AppTransferView.Waiting ? T(AppTransferText.ScanWithCamera) : "", centerStyle);
                EditorGUILayout.Space(12f);

                var counting = view == AppTransferView.Waiting || view == AppTransferView.Expired;
                var row = EditorGUILayout.GetControlRect(false, 18f);
                var state = view == AppTransferView.Waiting ? T(flow.StatusUnknown ? AppTransferText.StatusUnknown : AppTransferText.Waiting) : "";
                GUI.Label(new Rect(row.x, row.y, Mathf.Max(0f, row.width - 120f), row.height), state, statusStyle);
                if (counting)
                {
                    var seconds = view == AppTransferView.Waiting ? flow.Remaining : 0;
                    GUI.Label(new Rect(row.xMax - 120f, row.y, 120f, row.height), string.Format(T("残り {0}:{1:00}"), seconds / 60, seconds % 60),
                        view == AppTransferView.Expired ? expiredStyle : remainingStyle);
                }
                var bar = EditorGUILayout.GetControlRect(false, 4f);
                EditorGUI.DrawRect(bar, Neutral);
                // Filled from the left by what remains, so it shrinks toward the deadline.
                if (view == AppTransferView.Waiting) EditorGUI.DrawRect(new Rect(bar.x, bar.y, Mathf.Round(bar.width * flow.RemainingFraction), bar.height), Accent);
                GUI.Label(EditorGUILayout.GetControlRect(false, 16f), counting ? string.Format(T(AppTransferText.Lifetime), CloudTransferProtocol.LifetimeMinutes) : "", noteStyle);
            }
            DrawHairline();
            using (new EditorGUILayout.HorizontalScope(footerStyle))
            {
                if (view == AppTransferView.Waiting && GUILayout.Button(T(AppTransferText.Regenerate), GUILayout.Width(140f), GUILayout.Height(24f))) Retry();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(T("閉じる"), GUILayout.Width(88f), GUILayout.Height(24f)))
                {
                    Close();
                    GUIUtility.ExitGUI();
                }
            }
        }

        private static float Snap(float value)
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            return Mathf.Round(value * scale) / scale;
        }

        private void DrawSlot(Rect slot, AppTransferView view)
        {
            if (view == AppTransferView.Waiting && qrTexture != null && !qrIsPlaceholder)
            {
                EditorGUI.DrawRect(slot, Color.white);
                DrawQr(slot, 1f);
                return;
            }
            EditorGUI.DrawRect(slot, Neutral);
            var inner = new Rect(slot.x + 20f, slot.y + 12f, slot.width - 40f, slot.height - 24f);
            switch (view)
            {
                case AppTransferView.Exporting:
                    DrawMessage(inner, T(AppTransferText.Exporting), null, null);
                    break;
                case AppTransferView.Expired:
                    if (qrTexture != null) DrawQr(slot, 0.16f);
                    DrawMessage(inner, T(AppTransferText.Expired), null, T(AppTransferText.Regenerate));
                    break;
                case AppTransferView.Canceled:
                    DrawMessage(inner, T("転送を中止しました"), null, T(AppTransferText.Regenerate));
                    break;
                case AppTransferView.Completed:
                    DrawMessage(inner, T(AppTransferText.Completed), null, null);
                    break;
                case AppTransferView.Failed:
                    DrawMessage(inner, T(flow.ExportDidFail ? AppTransferText.ExportFailed : "転送できませんでした"), flow.FailureText(T), T(AppTransferText.Retry));
                    break;
                case AppTransferView.Uploading:
                    DrawUpload(inner);
                    break;
            }
        }

        private void DrawQr(Rect slot, float alpha)
        {
            var side = Mathf.Min(TransferQrCode.DisplaySide(qrTexture.width, EditorGUIUtility.pixelsPerPoint), slot.width);
            var rect = new Rect(Snap(slot.x + (slot.width - side) / 2f), Snap(slot.y + (slot.height - side) / 2f), side, side);
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(rect, qrTexture, ScaleMode.StretchToFill);
            GUI.color = previous;
        }

        // A failure or an ended transfer says why in place, with the one thing to do next.
        private void DrawMessage(Rect area, string title, string body, string button)
        {
            const float titleHeight = 22f, buttonHeight = 30f;
            var hasBody = !string.IsNullOrEmpty(body);
            var buttonBlock = button == null ? 0f : buttonHeight + 12f;
            var bodyHeight = hasBody ? Mathf.Min(centerStyle.CalcHeight(new GUIContent(body), area.width), Mathf.Max(0f, area.height - titleHeight - buttonBlock - 8f)) : 0f;
            var y = area.y + Mathf.Max(0f, (area.height - titleHeight - (hasBody ? bodyHeight + 8f : 0f) - buttonBlock) / 2f);
            GUI.Label(new Rect(area.x, y, area.width, titleHeight), title, centerBoldStyle);
            y += titleHeight;
            if (hasBody)
            {
                GUI.Label(new Rect(area.x, y + 8f, area.width, bodyHeight), body, centerStyle);
                y += bodyHeight + 8f;
            }
            if (button == null) return;
            var width = Mathf.Min(160f, area.width);
            if (PrimaryButton(new Rect(area.x + (area.width - width) / 2f, y + 12f, width, buttonHeight), button)) Retry();
        }

        private void DrawUpload(Rect area)
        {
            var y = area.y + area.height / 2f - 28f;
            GUI.Label(new Rect(area.x, y, area.width, 22f), T(flow.Encrypting ? "転送を準備しています" : "アップロード中"), centerBoldStyle);
            if (flow.UploadSize <= 0) return;
            EditorGUI.ProgressBar(new Rect(area.x, y + 32f, area.width, 20f), (float)flow.Transferred / flow.UploadSize,
                string.Format(T("{0:N0} / {1:N0} バイト（暗号化済み）"), flow.Transferred, flow.UploadSize));
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
