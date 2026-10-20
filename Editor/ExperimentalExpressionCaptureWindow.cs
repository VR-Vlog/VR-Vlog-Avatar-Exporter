using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Place = VRVlog.LilToonExporter.ExpressionBoard.Place;
using PlaceKind = VRVlog.LilToonExporter.ExpressionBoard.PlaceKind;

namespace VRVlog.LilToonExporter
{
    internal sealed class ExperimentalExpressionCaptureWindow : EditorWindow
    {
        [SerializeField] GameObject source;
        [SerializeField] string author = "";
        SavedExpressionVrm saved;
        bool showPoses;
        readonly System.Collections.Generic.Dictionary<ExperimentalExpressionCaptureSession.ClipInput, ExperimentalExpressionCaptureSession.Pose> fileRecords =
            new System.Collections.Generic.Dictionary<ExperimentalExpressionCaptureSession.ClipInput, ExperimentalExpressionCaptureSession.Pose>();
        ExperimentalExpressionCaptureSession session;
        ExpressionBoard board;
        PreviewRenderUtility preview;
        Action pending;
        bool candidateValidationPending;
        string error, status;
        Vector2 pageScroll;
        Vector3 center;
        float distance = 1, yaw, zoom = 1;
        bool automaticBlink = true;
        BlinkExportOptions configuredBlink;
        BlinkConfigurationWindow blinkConfiguration;
        bool showLicenseSettings;
        readonly System.Collections.Generic.Dictionary<AnimationClip, string> clipErrors = new System.Collections.Generic.Dictionary<AnimationClip, string>();
        // Window-relative card rectangles, kept for tests that click a card.
        readonly System.Collections.Generic.Dictionary<AnimationClip, Rect> candidateRects = new System.Collections.Generic.Dictionary<AnimationClip, Rect>();
        // AssetDatabase-backed localization must run after ScriptableObject construction.
        string previewName = "基準の顔";
        Vector2 otherScroll, excludedScroll;
        AnimationClip clipToAdd;
        PoseReviewWindow poseReview;
        Rect expressionDropRect;
        readonly System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput> clipInputs =
            new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipInput>();
        System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation> recommendations;
        [SerializeField] AvatarLicenseOptions licenseOptions = new AvatarLicenseOptions();
        string presetKey;
        int presetSkipped;
        readonly System.Collections.Generic.Dictionary<UniVRM10.ExpressionPreset, string> presetErrors = new System.Collections.Generic.Dictionary<UniVRM10.ExpressionPreset, string>();
        // Opened from "アプリに転送（通常）": the next window asks the name, author and license.
        [SerializeField] bool transferMode;
        // The objects left out of the VRM, as edited here; null while they match the preview.
        const string ExcludedObjectsPreference = "VRVlog.ExpressionBoard.ShowExcludedObjects";
        System.Collections.Generic.List<GameObject> excludedObjectEdits;
        string excludedObjectError;
        bool? showExcludedObjects;

        // Board drag state. Cards carry the clip under a private key, so a
        // drag from the Project window is never mistaken for a card.
        const string DragKey = "VRVlog.LilToonExporter.ExpressionBoardCard";
        Vector2 pressStart;
        Place? dropHover, dropHoverNext;
        Vector2? dragMouse;

        // Fixed sizes: the board never follows the font size or the names.
        const float Gap = 6, SlotHeader = 22, SlotBody = 44, SlotFooter = 20, CardWidth = 150, CardHeight = 36,
            PanelHeight = 300, PanelHeader = 26, PanelFooter = 20, ScrollbarWidth = 14;
        static readonly Color LineColor = Hex(0x242424), PanelColor = Hex(0x2e2e2e), CardColor = Hex(0x383838), CardDisabledColor = Hex(0x313131),
            CardBorderColor = Hex(0x585858), DashColor = Hex(0x9a9a9a), TextColor = Hex(0xd2d2d2), DimColor = Hex(0x9a9a9a),
            AccentColor = Hex(0x7fa6c9), AccentButtonColor = Hex(0x2c5d87), ErrorColor = Hex(0xe0675f);
        GUIStyle cardText, cardTextDisabled, dimText, dimMini, errorMini, boldText, badgeText, closeText, accentButton, centeredDim;
        Texture2D accentNormal, accentHover, accentActive;
        Texture gridIcon, hiddenIcon;
        bool iconsLooked;
        readonly System.Collections.Generic.Dictionary<(string, GUIStyle, int), string> fitted = new System.Collections.Generic.Dictionary<(string, GUIStyle, int), string>();

        // The "アプリに転送" window starts from what this window shows: these
        // values live only in the open window and are not stored anywhere else.
        internal string Author => author;
        internal AvatarLicenseOptions LicenseFor(GameObject avatar) => avatar != null && source == avatar ? licenseOptions?.Copy() : null;
        internal bool TransferMode => transferMode;
        internal int SlotCount => board?.SlotCount ?? 0;
        internal int ExcludedObjectCount => session?.ExcludedObjects.Count ?? 0;

        public static void Open()
        {
            var window = GetWindow<ExperimentalExpressionCaptureWindow>();
            window.transferMode = false;
            window.titleContent = new GUIContent(ExporterLocalization.T("VRM書き出し"));
            window.minSize = new Vector2(600, 760);
        }

        // "アプリに転送（通常）": this board, prepared for the avatar, leads on to
        // the transfer window instead of saving a file. A board already prepared
        // for the avatar keeps its assignments. False when the user kept the
        // board's current avatar instead.
        public static bool OpenForTransfer(GameObject avatar)
        {
            var window = GetWindow<ExperimentalExpressionCaptureWindow>();
            window.Focus();
            var same = window.session != null && window.source == avatar;
            if (!same && window.session != null && !window.ConfirmChangeAvatar()) return false;
            window.transferMode = true;
            window.titleContent = new GUIContent(ExporterLocalization.T("アプリに転送（通常）"));
            window.minSize = new Vector2(600, 760);
            if (same || avatar == null) { window.Repaint(); return true; }
            window.Queue(() =>
            {
                window.ChangeAvatar();
                window.SelectSource(avatar);
                if (!EditorApplication.isPlayingOrWillChangePlaymode) window.Prepare();
            });
            return true;
        }

        // The open board prepared for this avatar, or null.
        internal static ExperimentalExpressionCaptureWindow ForTransfer(GameObject avatar) => avatar == null ? null :
            Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().FirstOrDefault(window => window != null && window.source == avatar && window.session != null);

        bool ConfirmChangeAvatar() => clipInputs.Count == 0 && (session == null || session.PoseOptions.Manual.Count == 0) ||
            EditorUtility.DisplayDialog(ExporterLocalization.T("アバターを変更"), ExporterLocalization.T("追加した表情・ポーズの一覧をリセットします。保存済みVRMは残ります。"), ExporterLocalization.T("変更する"), ExporterLocalization.T("戻る"));

        void OnEnable() { EditorApplication.update += Advance; EditorApplication.projectChanged += CandidateAssetsChanged; }
        void OnDisable() { EditorApplication.update -= Advance; EditorApplication.projectChanged -= CandidateAssetsChanged; pending = null; Cleanup(); ReleaseStyles(); }

        void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (pending == null && candidateValidationPending && session != null) pending = RefreshCandidateValidation;
            if (pending == null) return;
            var action = pending; pending = null;
            try { action(); error = null; }
            catch (OperationCanceledException) { status = ExporterLocalization.T("操作をキャンセルしました。"); }
            catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        void Queue(Action action) { pending = action; error = null; Repaint(); }

        internal static void AnimationAssetsReimported()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>()) window.CandidateAssetsChanged();
        }

        void CandidateAssetsChanged() { candidateValidationPending = true; Repaint(); }

        void RefreshCandidateValidation()
        {
            candidateValidationPending = false;
            if (session == null) return;
            if (!session.HasCurrentSource)
            {
                // A changed registered clip is also part of the guarded source.
                // Rebuild the owned preview rather than restamping stale geometry.
                // Retain the user's explicit files and settings; save rebuilds their records.
                if (session.Expressions.Any(pose => !fileRecords.Values.Contains(pose)))
                    throw new InvalidOperationException(ExporterLocalization.T("元のアバターが変更されました。「変更」からアバターを指定し直してください。"));
                RebuildKeepingAssignments();
            }
            clipErrors.Clear();
            foreach (var pair in session.ClipErrors((recommendations ?? new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>())
                .Select(item => item.Clip).Concat(clipInputs.Select(input => input.Clip)))) clipErrors[pair.Key] = pair.Value;
            foreach (var input in clipInputs)
            {
                input.Time = DefaultTime(input.Clip);
                input.Error = input.Clip == null ? ExporterLocalization.T("AnimationClipが指定されていません") : clipErrors[input.Clip];
            }
            // An automatic value that stopped working is replaced, not left blocking the save.
            ApplyAutomaticPresets();
            Repaint();
        }

        // A new preview of the same avatar that keeps the added files, the board,
        // the poses, the blink choice and the object list still being edited.
        void RebuildKeepingAssignments()
        {
            var inputs = clipInputs.ToArray(); var excluded = board?.Excluded.ToArray() ?? Array.Empty<AnimationClip>();
            var poses = session.PoseOptions.Copy(); var blink = configuredBlink?.Copy(); var previousSaved = saved;
            var presets = session.Presets.Values.Select(value => value.Copy()).ToArray();
            var edits = excludedObjectEdits; var editError = excludedObjectError; var showEdits = showExcludedObjects;
            Prepare();
            excludedObjectEdits = edits; excludedObjectError = editError; showExcludedObjects = showEdits;
            clipInputs.AddRange(inputs);
            board.Excluded.Clear(); board.Excluded.UnionWith(excluded);
            session.Presets.Clear();
            foreach (var preset in presets) session.SetPreset(ExperimentalExpressionCaptureSession.ParsePreset(preset.Preset), preset);
            session.PoseOptions.Manual.AddRange(poses.Manual); session.PoseOptions.Excluded.UnionWith(poses.Excluded);
            foreach (var pair in poses.Names) session.PoseOptions.Names.Add(pair.Key, pair.Value);
            configuredBlink = blink; saved = previousSaved;
        }

        void OnGUI()
        {
            EnsureStyles();
            var current = Event.current;
            var dragUpdate = current.type == EventType.DragUpdated;
            var dragMousePosition = current.mousePosition;
            if (dragUpdate) dropHoverNext = null;
            if (current.type == EventType.DragExited || current.type == EventType.DragPerform || current.type == EventType.MouseUp)
            { dropHover = null; dragMouse = null; }
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(16, 16, 12, 12) }))
            {
                EditorGUILayout.LabelField(ExporterLocalization.T(transferMode ? "アプリに転送（通常）" : "VRMを書き出す"), EditorStyles.boldLabel);
                if (transferMode) DrawTransferSteps();
                EditorGUILayout.Space(8);
                var blocked = pending != null || EditorApplication.isPlayingOrWillChangePlaymode;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorGUILayout.HelpBox(ExporterLocalization.T("Playモードを終了すると編集できます。"), MessageType.Info);
                using (new EditorGUI.DisabledScope(blocked))
                {
                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(session != null))
                        SelectSource((GameObject)EditorGUILayout.ObjectField(ExporterLocalization.T("アバター"), source, typeof(GameObject), true));
                    if (session != null && GUILayout.Button(ExporterLocalization.T("変更"), GUILayout.Width(50)) && ConfirmChangeAvatar())
                        Queue(ChangeAvatar);
                    EditorGUILayout.EndHorizontal();
                    if (session == null)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField(ExporterLocalization.T("Hierarchyからアバターの一番上のオブジェクトを指定します。"), EditorStyles.wordWrappedLabel);
                        using (new EditorGUI.DisabledScope(source == null))
                            if (GUILayout.Button(ExporterLocalization.T("書き出しを準備"), GUILayout.Height(34))) Queue(Prepare);
                    }
                    else if (preview != null)
                    {
                        EditorGUILayout.Space(8);
                        DrawPreview();
                        EditorGUILayout.Space(8);
                        if (current.type == EventType.Layout) RebuildBoard();
                        pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
                        DrawNextStep();
                        DrawSlots();
                        EditorGUILayout.Space(8);
                        DrawPanels();
                        EditorGUILayout.Space(6);
                        DrawAddRow();
                        EditorGUILayout.Space(12);
                        DrawExcludedObjects();
                        EditorGUILayout.Space(12);
                        DrawPoseInputs();
                        EditorGUILayout.Space(16);
                        DrawSave();
                        DrawFeedback();
                        EditorGUILayout.EndScrollView();
                        DrawBottomBar();
                    }
                }
                if (pending != null) EditorGUILayout.LabelField(ExporterLocalization.T("処理しています…"), EditorStyles.miniLabel);
                if (session == null) DrawFeedback();
            }
            if (dragUpdate)
            {
                if (!Nullable.Equals(dropHover, dropHoverNext)) { dropHover = dropHoverNext; Repaint(); }
                var dragging = DragAndDrop.GetGenericData(DragKey) is AnimationClip;
                dragMouse = dragging ? dragMousePosition : (Vector2?)null;
                if (dragging) Repaint();
            }
            DrawDraggedName();
        }

        void DrawFeedback()
        {
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            else if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
        }

        void Prepare()
        {
            Cleanup();
            try
            {
                // The objects and gimmicks left out follow the avatar's saved export settings.
                var stored = ExportSettingsStore.Load(source);
                session = new ExperimentalExpressionCaptureSession(source, replayInstalledDefaults: false, stored.Exclusions(), stored.Gimmicks());
                var target = session;
                board = new ExpressionBoard(target.Presets, target.SetPreset, clips => target.SuggestPresets(clips), target.HasAuthoredPreset);
                recommendations = session.RecommendClips();
                foreach (var item in session.ClipErrors(recommendations.Select(item => item.Clip))) clipErrors[item.Key] = item.Value;
                preview = new PreviewRenderUtility();
                NdmfExportPreparation.ShowOwnedCopyInPreview(session.Copy, preview);
                var renderers = ExportRendererSelection.Enumerate(session.Copy).ToArray();
                if (renderers.Length == 0) throw new InvalidOperationException(ExporterLocalization.T("表示するメッシュがありません。"));
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var animator = session.Copy.GetComponent<Animator>();
                var head = animator != null && animator.avatar != null && animator.avatar.isHuman
                    ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
                center = head != null ? head.position + Vector3.up * bounds.size.y * .065f : bounds.center;
                distance = head != null ? Mathf.Max(.35f, bounds.size.y * .65f) : Mathf.Max(.5f, bounds.size.magnitude * 1.2f);
                preview.camera.fieldOfView = 30;
                preview.camera.nearClipPlane = .01f; preview.camera.farClipPlane = 100;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(.18f, .18f, .2f);
                preview.lights[0].intensity = 1;
                preview.lights[0].transform.rotation = Quaternion.Euler(30, 150, 0);
                preview.lights[1].intensity = .5f; preview.ambientColor = Color.gray;
                yaw = 0; zoom = 1;
                previewName = ExporterLocalization.T("基準の顔");
                status = null;
                saved = null;
                candidateValidationPending = false;
                LoadPresetChoices();
            }
            catch { Cleanup(); throw; }
        }

        void DrawPreview()
        {
            var rect = GUILayoutUtility.GetRect(100, 165, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && rect.width > 0)
            {
                preview.BeginPreview(rect, GUIStyle.none);
                preview.camera.transform.position = center + Quaternion.AngleAxis(yaw, Vector3.up) * session.Copy.transform.forward * distance * zoom;
                preview.camera.transform.LookAt(center, Vector3.up);
                preview.Render();
                GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.ScaleToFit, false);
            }
            if (rect.Contains(Event.current.mousePosition))
            {
                if (Event.current.type == EventType.MouseDrag && Event.current.button == 0)
                { yaw = Mathf.Clamp(yaw + Event.current.delta.x, -90, 90); Event.current.Use(); Repaint(); }
                if (Event.current.type == EventType.ScrollWheel)
                { zoom = Mathf.Clamp(zoom + Event.current.delta.y * .04f, .25f, 2); Event.current.Use(); Repaint(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(previewName, EditorStyles.boldLabel);
                if (GUILayout.Button(new GUIContent(ExporterLocalization.T("元の顔"), ExporterLocalization.T("元の顔に戻す。画像をドラッグで回転、ホイールで拡大。")), GUILayout.Width(65)))
                    Queue(() => { session.RestoreBaseline(); previewName = ExporterLocalization.T("基準の顔"); });
            }
        }

        // ---- Expression board ----

        void RebuildBoard()
        {
            if (board == null) return;
            board.SetItems(clipInputs.Select(input => new ExpressionBoard.Item { Clip = input.Clip, Error = input.Error, Source = ExporterLocalization.T("追加した表情") }),
                (recommendations ?? new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>()).Select(item => new ExpressionBoard.Item
                    { Clip = item.Clip, Error = clipErrors.TryGetValue(item.Clip, out var reason) ? reason : null, Source = item.Source }));
        }

        // The transfer asks the author in the next window.
        bool CanSave => (transferMode || ExportAuthor.IsValid(author)) && excludedObjectEdits == null && !presetErrors.Values.Any(value => value != null) &&
            board != null && !board.ExceedsLimit && !board.HasDuplicateNames;

        // Exactly one next step, derived from the current state.
        string NextStep()
        {
            if (excludedObjectEdits != null) return ExporterLocalization.T("書き出さないオブジェクトをプレビューに反映する");
            foreach (var preset in ExperimentalExpressionCaptureSession.EmotionPresets)
                if (presetErrors.TryGetValue(preset, out var reason) && reason != null)
                    return string.Format(ExporterLocalization.T("{0}の割り当てを直す"), ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset)));
            if (board.ExceedsLimit) return string.Format(ExporterLocalization.T("その他の表情から{0}件を書き出さないへ移す"), board.OverLimitBy);
            if (board.HasDuplicateNames) return DuplicateNamesText(ExporterLocalization.T("同じ名前の表情{0}組の片方を書き出さないへ移す"));
            if (board.Items.Count == 0) return ExporterLocalization.T("表情の.animを追加する");
            if (board.AutomaticCount > 0) return string.Format(ExporterLocalization.T("名前で自動割り当てした{0}件を確認する"), board.AutomaticCount);
            if (transferMode) return ExporterLocalization.T("この割り当てで次の画面へ進む");
            if (!ExportAuthor.IsValid(author)) return ExporterLocalization.T("作者名を入力する");
            return ExporterLocalization.T("この割り当てでVRMを保存する");
        }

        // The two steps of the detailed transfer; this window is the first.
        void DrawTransferSteps()
        {
            var row = GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true));
            var first = ExporterLocalization.T("1 表情と書き出さないオブジェクト");
            var second = ExporterLocalization.T("2 名前と利用条件");
            const float separator = 20;
            var firstWidth = Mathf.Min(boldText.CalcSize(new GUIContent(first)).x, (row.width - separator) * .6f);
            GUI.Label(new Rect(row.x, row.y, firstWidth, row.height), new GUIContent(Fit(first, boldText, firstWidth), first), boldText);
            GUI.Label(new Rect(row.x + firstWidth, row.y, separator, row.height), "›", centeredDim);
            var secondRect = new Rect(row.x + firstWidth + separator, row.y, row.xMax - (row.x + firstWidth + separator), row.height);
            GUI.Label(secondRect, new GUIContent(Fit(second, dimText, secondRect.width), second), dimText);
        }

        // The objects left out of the VRM, shared with the export window through
        // the avatar's saved settings. Edits wait until "プレビューに反映".
        void DrawExcludedObjects()
        {
            var shown = (System.Collections.Generic.IReadOnlyList<GameObject>)excludedObjectEdits ?? session.ExcludedObjects;
            var count = shown.Count(item => item != null);
            if (showExcludedObjects == null)
                showExcludedObjects = EditorPrefs.HasKey(ExcludedObjectsPreference) ? EditorPrefs.GetBool(ExcludedObjectsPreference) : count > 0;
            var header = GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true));
            var label = string.Format(ExporterLocalization.T("書き出さないオブジェクト（{0}件）"), count);
            var open = EditorGUI.Foldout(header, showExcludedObjects.Value, new GUIContent(Fit(label, EditorStyles.foldout, header.width - 16), label), true);
            if (open != showExcludedObjects.Value) { showExcludedObjects = open; EditorPrefs.SetBool(ExcludedObjectsPreference, open); }
            if (!open) return;
            System.Collections.Generic.List<GameObject> Edits()
            {
                excludedObjectError = null;
                return excludedObjectEdits ?? (excludedObjectEdits = session.ExcludedObjects.ToList());
            }
            var release = ExporterLocalization.T("解除");
            var removed = -1;
            for (var index = 0; index < shown.Count; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var next = (GameObject)EditorGUILayout.ObjectField(shown[index], typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck()) Edits()[index] = next;
                    if (GUILayout.Button(new GUIContent(Fit(release, GUI.skin.button, 42), release), GUILayout.Width(50))) removed = index;
                }
            }
            if (removed >= 0) Edits().RemoveAt(removed);
            using (new EditorGUILayout.HorizontalScope())
            {
                var add = ExporterLocalization.T("除外するオブジェクトを追加");
                var apply = ExporterLocalization.T("プレビューに反映");
                if (GUILayout.Button(new GUIContent(Fit(add, GUI.skin.button, Mathf.Max(0, position.width - 200)), add))) Edits().Add(null);
                using (new EditorGUI.DisabledScope(excludedObjectEdits == null))
                    if (GUILayout.Button(new GUIContent(Fit(apply, GUI.skin.button, 112), apply), GUILayout.Width(120))) Queue(ApplyExcludedObjects);
            }
            if (!string.IsNullOrEmpty(excludedObjectError)) EditorGUILayout.HelpBox(excludedObjectError, MessageType.Error);
        }

        // Checks the edited list like the export does, saves it to the avatar's
        // settings and rebuilds the preview without them.
        void ApplyExcludedObjects()
        {
            if (excludedObjectEdits == null || session == null) return;
            var objects = excludedObjectEdits.Where(item => item != null).Distinct().ToArray();
            try { new ExportObjectExclusions(source, objects).Dispose(); }
            catch (InvalidOperationException exception) { excludedObjectError = ExporterLocalization.T(exception.Message); return; }
            var settings = ExportSettingsStore.Load(source);
            settings.ExcludedObjects.Clear();
            settings.ExcludedObjects.AddRange(objects);
            ExportSettingsStore.Save(source, settings);
            excludedObjectEdits = null; excludedObjectError = null;
            RebuildKeepingAssignments();
            // The rebuilt copy can change which faces work; check them on the next update.
            candidateValidationPending = true;
        }

        void DrawNextStep()
        {
            var row = GUILayoutUtility.GetRect(100, 24, GUILayout.ExpandWidth(true));
            var automatic = board.AutomaticCount;
            var buttonWidth = Mathf.Min(180, row.width * .32f);
            var buttonRect = new Rect(row.xMax - buttonWidth, row.y + 1, buttonWidth, 22);
            var prefix = ExporterLocalization.T("次にやること:");
            var prefixWidth = Mathf.Min(row.width * .25f, dimText.CalcSize(new GUIContent(prefix)).x);
            GUI.Label(new Rect(row.x, row.y, prefixWidth, row.height), Fit(prefix, dimText, prefixWidth), dimText);
            var stepRect = new Rect(row.x + prefixWidth + 4, row.y, buttonRect.x - 8 - (row.x + prefixWidth + 4), row.height);
            var step = NextStep();
            GUI.Label(stepRect, new GUIContent(Fit(step, boldText, stepRect.width), step), boldText);
            if (automatic > 0)
            {
                var label = string.Format(ExporterLocalization.T("自動の{0}件をすべて確定"), automatic);
                if (GUI.Button(buttonRect, new GUIContent(Fit(label, EditorStyles.miniButton, buttonRect.width - 8), label), EditorStyles.miniButton)) Queue(ConfirmAutomatic);
            }
            var line = GUILayoutUtility.GetRect(100, 9, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(line.x, line.y + 4, line.width, 1), LineColor);
        }

        void DrawSlots()
        {
            var caption = GUILayoutUtility.GetRect(100, 18, GUILayout.ExpandWidth(true));
            GUI.Label(caption, Fit(string.Format(ExporterLocalization.T("VRMの表情 {0}/5・ドロップで入れ替え"), board.SlotCount), dimMini, caption.width), dimMini);
            var row = GUILayoutUtility.GetRect(100, SlotHeader + SlotBody + SlotFooter, GUILayout.ExpandWidth(true));
            var presets = ExperimentalExpressionCaptureSession.EmotionPresets;
            var width = (row.width - Gap * (presets.Length - 1)) / presets.Length;
            for (var index = 0; index < presets.Length; index++)
                DrawSlot(presets[index], new Rect(row.x + index * (width + Gap), row.y, width, row.height));
        }

        void DrawSlot(UniVRM10.ExpressionPreset preset, Rect rect)
        {
            session.Presets.TryGetValue(preset, out var assignment);
            presetErrors.TryGetValue(preset, out var reason);
            var target = Place.Slot(preset);
            var repaint = Event.current.type == EventType.Repaint;
            if (repaint)
            {
                EditorGUI.DrawRect(rect, PanelColor);
                Outline(rect, Nullable.Equals(dropHover, target) ? AccentColor : LineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + SlotHeader, rect.width, 1), LineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - SlotFooter, rect.width, 1), LineColor);
            }
            var header = new Rect(rect.x, rect.y, rect.width, SlotHeader);
            var key = preset.ToString();
            var keyWidth = Mathf.Min(rect.width * .45f, dimMini.CalcSize(new GUIContent(key)).x);
            GUI.Label(new Rect(header.xMax - 6 - keyWidth, header.y + 2, keyWidth, header.height - 2), Fit(key, dimMini, keyWidth), dimMini);
            var labelRect = new Rect(header.x + 6, header.y, header.width - 16 - keyWidth, header.height);
            GUI.Label(labelRect, Fit(ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset)), cardText, labelRect.width), cardText);
            if (GUI.Button(header, new GUIContent("", ExporterLocalization.T("一覧から選ぶ")), GUIStyle.none)) ShowPresetMenu(preset, header);

            var card = new Rect(rect.x + 6, rect.y + SlotHeader + (SlotBody - CardHeight) / 2, rect.width - 12, CardHeight);
            var clip = assignment != null && !assignment.IsNone && !assignment.IsShape ? assignment.ResolveClip() : null;
            var item = board.Find(clip);
            if (item != null)
                DrawCard(card, clip.name, clip, true, assignment.Automatic, false, assignment.Automatic ? "自動" : null,
                    reason ?? AssetDatabase.GetAssetPath(clip), "その他の表情へ戻す", () => MoveTo(clip, Place.Other));
            else if (assignment != null && !assignment.IsNone)
                // A BlendShape, or a file no longer on the board: it can only be cleared.
                DrawCard(card, ExperimentalExpressionCaptureSession.PresetDisplayName(assignment), null, false, assignment.Automatic, false,
                    assignment.Automatic ? "自動" : null, reason, "指定を外す", () => ChoosePreset(preset, ExperimentalExpressionCaptureSession.PresetAssignment.None(preset)));

            var footer = new Rect(rect.x + 6, rect.yMax - SlotFooter + 1, rect.width - 12, SlotFooter - 1);
            var none = assignment == null || assignment.IsNone;
            var text = reason ?? ExporterLocalization.T(none ? session.HasAuthoredPreset(preset) ? "元のVRM設定" : "空のまま保存" : assignment.Automatic ? "自動の候補" : "手で割り当て");
            var style = reason != null ? errorMini : dimMini;
            GUI.Label(footer, new GUIContent(Fit(text, style, footer.width), reason), style);
            HandleDrop(rect, target);
        }

        void DrawPanels()
        {
            var area = GUILayoutUtility.GetRect(100, PanelHeight, GUILayout.ExpandWidth(true));
            var rightWidth = Mathf.Round(area.width * .25f);
            var left = new Rect(area.x, area.y, area.width - rightWidth - 8, area.height);
            var right = new Rect(left.xMax + 8, area.y, rightWidth, area.height);
            DrawOtherPanel(left);
            DrawExcludedPanel(right);
            if (Event.current.type == EventType.Repaint) expressionDropRect = ScreenRect(left);
        }

        Rect DrawPanelFrame(Rect rect, Place target, bool grid, string title, string caption, string footer)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, PanelColor);
                Outline(rect, Nullable.Equals(dropHover, target) ? AccentColor : LineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + PanelHeader, rect.width, 1), LineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - PanelFooter, rect.width, 1), LineColor);
            }
            var header = new Rect(rect.x + 8, rect.y, rect.width - 16, PanelHeader);
            DrawIcon(new Rect(header.x, header.y + (PanelHeader - 16) / 2, 16, 16), grid);
            var titleWidth = Mathf.Min(header.width - 20, cardText.CalcSize(new GUIContent(title)).x);
            GUI.Label(new Rect(header.x + 20, header.y, titleWidth, header.height), Fit(title, cardText, titleWidth), cardText);
            if (caption != null)
            {
                var captionRect = new Rect(header.x + 28 + titleWidth, header.y, header.xMax - (header.x + 28 + titleWidth), header.height);
                var fittedCaption = Fit(caption, dimMini, captionRect.width);
                var captionWidth = dimMini.CalcSize(new GUIContent(fittedCaption)).x;
                GUI.Label(new Rect(captionRect.xMax - captionWidth, captionRect.y + 1, captionWidth, captionRect.height), new GUIContent(fittedCaption, caption), dimMini);
            }
            var footerRect = new Rect(rect.x + 8, rect.yMax - PanelFooter + 1, rect.width - 16, PanelFooter - 1);
            GUI.Label(footerRect, new GUIContent(Fit(footer, dimMini, footerRect.width), footer), dimMini);
            return new Rect(rect.x + 1, rect.y + PanelHeader + 1, rect.width - 2, rect.height - PanelHeader - PanelFooter - 2);
        }

        void DrawOtherPanel(Rect rect)
        {
            var body = DrawPanelFrame(rect, Place.Other, true, string.Format(ExporterLocalization.T("その他の表情 {0}件"), board.OtherCount),
                board.UsesRecordRoute ? ExporterLocalization.T("この一覧を記録して書き出し") : ExporterLocalization.T("アバターの表情メニューをそのまま書き出し"),
                ExporterLocalization.T("見つかった表情は最初すべてここに入ります"));
            var items = board.ItemsIn(PlaceKind.Other).ToArray();
            var inner = body.width - ScrollbarWidth - Gap * 2;
            var columns = Mathf.Max(1, Mathf.FloorToInt((inner + Gap) / (CardWidth + Gap)));
            var rows = (items.Length + columns - 1) / columns;
            var view = new Rect(0, 0, body.width - ScrollbarWidth, Mathf.Max(body.height, Gap + rows * (CardHeight + Gap)));
            otherScroll = GUI.BeginScrollView(body, otherScroll, view, false, true);
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index]; var clip = item.Clip; var usable = ExpressionBoard.Usable(item);
                var card = new Rect(Gap + index % columns * (CardWidth + Gap), Gap + index / columns * (CardHeight + Gap), CardWidth, CardHeight);
                DrawCard(card, clip.name, clip, true, false, !usable, usable ? null : "対応外",
                    usable ? ItemTooltip(item) : item.Error, "書き出さないへ移す", () => MoveTo(clip, Place.Excluded));
            }
            if (items.Length == 0)
                GUI.Label(new Rect(Gap, Gap, view.width - Gap * 2, body.height - Gap * 2),
                    Fit(ExporterLocalization.T("Projectから表情の.animをドロップ（複数可）"), centeredDim, view.width - Gap * 2), centeredDim);
            GUI.EndScrollView();
            HandleDrop(rect, Place.Other, project: true);
        }

        void DrawExcludedPanel(Rect rect)
        {
            var body = DrawPanelFrame(rect, Place.Excluded, false, string.Format(ExporterLocalization.T("書き出さない {0}件"), board.ExcludedCount),
                null, ExporterLocalization.T("VRMに含めません"));
            var items = board.ItemsIn(PlaceKind.Excluded).ToArray();
            var width = body.width - ScrollbarWidth - Gap * 2;
            var listed = Gap + items.Length * (CardHeight + Gap);
            var zoneHeight = Mathf.Max(64, body.height - listed - Gap);
            var view = new Rect(0, 0, body.width - ScrollbarWidth, listed + zoneHeight + Gap);
            excludedScroll = GUI.BeginScrollView(body, excludedScroll, view, false, true);
            for (var index = 0; index < items.Length; index++)
            {
                var item = items[index]; var usable = ExpressionBoard.Usable(item);
                DrawCard(new Rect(Gap, Gap + index * (CardHeight + Gap), width, CardHeight), item.Clip.name, item.Clip, true, false, !usable,
                    usable ? null : "対応外", usable ? ItemTooltip(item) : item.Error, null, null);
            }
            var zone = new Rect(Gap, listed, width, zoneHeight);
            if (Event.current.type == EventType.Repaint) Dashed(zone, CardBorderColor);
            DrawIcon(new Rect(zone.center.x - 8, zone.center.y - 18, 16, 16), false);
            GUI.Label(new Rect(zone.x, zone.center.y, zone.width, 18), Fit(ExporterLocalization.T("ここへドロップ"), centeredDim, zone.width), centeredDim);
            GUI.EndScrollView();
            HandleDrop(rect, Place.Excluded);
        }

        static string ItemTooltip(ExpressionBoard.Item item) =>
            (item.Added ? item.Source : ExporterLocalization.T(item.Source ?? "")) + "\n" + AssetDatabase.GetAssetPath(item.Clip);

        // One card. A card with a clip can be clicked to preview, right-clicked
        // for the move menu and, when draggable, dragged onto another place.
        void DrawCard(Rect rect, string name, AnimationClip clip, bool draggable, bool automatic, bool unusable, string badge, string tooltip,
            string closeTooltip, Action close)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, unusable ? CardDisabledColor : CardColor);
                if (automatic) Dashed(rect, DashColor); else Outline(rect, CardBorderColor);
            }
            var closeRect = close != null ? new Rect(rect.xMax - 18, rect.y + 2, 16, 16) : Rect.zero;
            var right = close != null ? 20 : 6;
            const float badgeWidth = 44;
            // A narrow card (a slot in a narrow window) puts the badge under the name.
            var stacked = badge != null && rect.width < 140;
            var badgeRect = stacked ? new Rect(rect.x + 8, rect.yMax - 16, badgeWidth, 13)
                : new Rect(rect.xMax - right - badgeWidth, rect.center.y - 8, badgeWidth, 16);
            var nameRect = stacked ? new Rect(rect.x + 8, rect.y + 2, rect.width - 8 - right, 18)
                : new Rect(rect.x + 8, rect.y, rect.width - 8 - right - (badge != null ? badgeWidth + 4 : 0), rect.height);
            var style = unusable ? cardTextDisabled : cardText;
            GUI.Label(nameRect, new GUIContent(Fit(name, style, nameRect.width), tooltip), style);
            if (badge != null)
            {
                if (Event.current.type == EventType.Repaint) Outline(badgeRect, DimColor);
                GUI.Label(badgeRect, new GUIContent(Fit(ExporterLocalization.T(badge), badgeText, badgeRect.width), tooltip), badgeText);
            }
            if (close != null && GUI.Button(closeRect, new GUIContent("×", ExporterLocalization.T(closeTooltip)), closeText)) Queue(close);
            if (clip == null) return;
            if (Event.current.type == EventType.Repaint) candidateRects[clip] = ScreenRect(rect);
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!rect.Contains(current.mousePosition)) break;
                    if (current.button == 0) { GUIUtility.hotControl = id; pressStart = current.mousePosition; current.Use(); }
                    else if (current.button == 1) { ShowMoveMenu(clip); current.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if (draggable && (current.mousePosition - pressStart).sqrMagnitude > 16)
                    {
                        GUIUtility.hotControl = 0;
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = Array.Empty<UnityEngine.Object>();
                        DragAndDrop.SetGenericData(DragKey, clip);
                        DragAndDrop.StartDrag(clip.name);
                    }
                    current.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    GUIUtility.hotControl = 0;
                    if (rect.Contains(current.mousePosition)) PreviewBoardClip(clip);
                    current.Use();
                    break;
            }
        }

        void HandleDrop(Rect rect, Place target, bool project = false)
        {
            var current = Event.current;
            if (current.type != EventType.DragUpdated && current.type != EventType.DragPerform || !rect.Contains(current.mousePosition)) return;
            if (DragAndDrop.GetGenericData(DragKey) is AnimationClip clip)
            {
                var valid = GUI.enabled && board != null && board.CanMove(clip, target);
                DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Move : DragAndDropVisualMode.Rejected;
                if (valid) dropHoverNext = target;
                if (valid && current.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag(); DragAndDrop.SetGenericData(DragKey, null);
                    Queue(() => MoveTo(clip, target));
                }
                current.Use();
                return;
            }
            if (!project) return;
            // Files dropped from the Project window join the board as added files.
            var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().Where(item => item != null).Distinct().ToArray();
            var files = GUI.enabled && clips.Length > 0 && DragAndDrop.objectReferences.All(item => item is AnimationClip);
            DragAndDrop.visualMode = files ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (files) dropHoverNext = target;
            if (files && current.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Queue(() => AddClips(clips)); }
            current.Use();
        }

        void DrawDraggedName()
        {
            if (Event.current.type != EventType.Repaint || dragMouse == null || !(DragAndDrop.GetGenericData(DragKey) is AnimationClip clip)) return;
            var text = Fit(clip.name, cardText, 200);
            var rect = new Rect(dragMouse.Value.x + 14, dragMouse.Value.y + 6, Mathf.Min(216, cardText.CalcSize(new GUIContent(text)).x + 16), 22);
            EditorGUI.DrawRect(rect, CardColor); Outline(rect, AccentColor);
            GUI.Label(new Rect(rect.x + 8, rect.y, rect.width - 12, rect.height), text, cardText);
        }

        void ShowMoveMenu(AnimationClip clip)
        {
            var menu = new GenericMenu();
            var place = board.PlaceOf(clip);
            void Add(string label, Place target)
            {
                if (place.Equals(target)) menu.AddDisabledItem(new GUIContent(label), true);
                else if (board.CanMove(clip, target)) menu.AddItem(new GUIContent(label), false, () => Queue(() => MoveTo(clip, target)));
                else menu.AddDisabledItem(new GUIContent(label), false);
            }
            foreach (var preset in ExperimentalExpressionCaptureSession.EmotionPresets)
                Add(ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset)), Place.Slot(preset));
            menu.AddSeparator("");
            Add(ExporterLocalization.T("その他の表情"), Place.Other);
            Add(ExporterLocalization.T("書き出さない"), Place.Excluded);
            menu.ShowAsContext();
        }

        void PreviewBoardClip(AnimationClip clip) => Queue(() => {
            RefreshCandidateValidation(); var currentReason = session.ClipError(clip, DefaultTime(clip));
            if (currentReason != null) throw new InvalidOperationException(currentReason);
            session.PreviewClip(clip, DefaultTime(clip)); ShowFace(clip.name);
        });

        void MoveTo(AnimationClip clip, Place target)
        {
            RebuildBoard();
            if (!board.Move(clip, target)) return;
            ApplyAutomaticPresets();
            SaveChoices();
            if (target.Kind != PlaceKind.Slot) return;
            var preset = target.Preset;
            if (presetErrors.TryGetValue(preset, out var reason) && reason != null) return;
            session.PreviewPreset(preset, session.Presets[preset]);
            ShowFace(ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset)));
        }

        void ConfirmAutomatic()
        {
            RebuildBoard();
            board.ConfirmAutomatic();
            SaveChoices();
            RefreshPresetErrors();
        }

        void ResetBoard()
        {
            RebuildBoard();
            board.Reset();
            SaveChoices();
            RefreshPresetErrors();
            session.RestoreBaseline(); ShowFace(ExporterLocalization.T("基準の顔"));
        }

        void SaveChoices() => ExpressionPresetStore.Save(presetKey, session.Presets.Values, board?.Excluded);

        void DrawAddRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                clipToAdd = (AnimationClip)EditorGUILayout.ObjectField(ExporterLocalization.T("表情の.animを追加"), clipToAdd, typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(clipToAdd == null))
                    if (GUILayout.Button(ExporterLocalization.T("追加"), GUILayout.Width(60))) { var clip = clipToAdd; Queue(() => AddClip(clip)); }
            }
            if (presetKey == null) EditorGUILayout.LabelField(ExporterLocalization.T("シーンが未保存のため指定は次回に残りません"), EditorStyles.wordWrappedMiniLabel);
            else if (presetSkipped > 0) EditorGUILayout.LabelField(ExporterLocalization.T("前回の指定のうち元のファイルがないものを外しました"), EditorStyles.wordWrappedMiniLabel);
        }

        void DrawBottomBar()
        {
            EditorGUILayout.Space(6);
            // A narrow window puts the summary on its own row above the buttons.
            var stacked = position.width < 760;
            var bar = GUILayoutUtility.GetRect(100, stacked ? 60 : 38, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1), LineColor);
            var saveWidth = Mathf.Min(220, bar.width * .4f); var resetWidth = Mathf.Min(140, bar.width * .28f);
            var buttons = stacked ? bar.y + 29 : bar.y + 7;
            var saveRect = new Rect(bar.xMax - saveWidth, buttons, saveWidth, 28);
            var resetRect = new Rect(saveRect.x - 8 - resetWidth, buttons + 3, resetWidth, 22);
            var summaryRect = stacked ? new Rect(bar.x, bar.y + 5, bar.width, 20) : new Rect(bar.x, bar.y + 7, resetRect.x - 8 - bar.x, 28);
            var summary = string.Format(ExporterLocalization.T("VRMの表情 {0}/5・その他の表情 {1}・書き出さない {2}"), board.SlotCount, board.OtherCount, board.ExcludedCount);
            GUI.Label(summaryRect, new GUIContent(Fit(summary, dimText, summaryRect.width), summary), dimText);
            var reset = ExporterLocalization.T("割り当てをリセット");
            if (GUI.Button(resetRect, new GUIContent(Fit(reset, EditorStyles.miniButton, resetRect.width - 8), reset), EditorStyles.miniButton) &&
                EditorUtility.DisplayDialog(reset, ExporterLocalization.T("手で割り当てた表情と書き出さない指定を消して名前から割り当て直します"),
                    ExporterLocalization.T("リセット"), ExporterLocalization.T("戻る")))
                Queue(ResetBoard);
            var save = ExporterLocalization.T(transferMode ? "次へ：名前と利用条件" : "この割り当てでVRMを保存");
            using (new EditorGUI.DisabledScope(!CanSave))
                if (GUI.Button(saveRect, new GUIContent(Fit(save, accentButton, saveRect.width - 12), save), accentButton)) Queue(transferMode ? (Action)OpenTransferDetails : Export);
        }

        // ---- Drawing helpers ----

        static Color Hex(int value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);

        static void Outline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
        }

        static void Dashed(Rect rect, Color color)
        {
            for (var x = rect.x; x < rect.xMax; x += 6)
            {
                var length = Mathf.Min(3, rect.xMax - x);
                EditorGUI.DrawRect(new Rect(x, rect.y, length, 1), color);
                EditorGUI.DrawRect(new Rect(x, rect.yMax - 1, length, 1), color);
            }
            for (var y = rect.y; y < rect.yMax; y += 6)
            {
                var length = Mathf.Min(3, rect.yMax - y);
                EditorGUI.DrawRect(new Rect(rect.x, y, 1, length), color);
                EditorGUI.DrawRect(new Rect(rect.xMax - 1, y, 1, length), color);
            }
        }

        // Built-in icons when the editor has them, otherwise a plain glyph.
        void DrawIcon(Rect rect, bool grid)
        {
            if (!iconsLooked)
            {
                iconsLooked = true;
                gridIcon = EditorGUIUtility.FindTexture("GridLayoutGroup Icon");
                hiddenIcon = EditorGUIUtility.FindTexture("scenevis_hidden") ?? EditorGUIUtility.FindTexture("animationvisibilitytoggleoff");
            }
            if (Event.current.type != EventType.Repaint) return;
            var icon = grid ? gridIcon : hiddenIcon;
            if (icon != null) { GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit); return; }
            if (grid)
            {
                for (var x = 0; x < 2; x++)
                    for (var y = 0; y < 2; y++) Outline(new Rect(rect.x + 2 + x * 7, rect.y + 2 + y * 7, 5, 5), DimColor);
                return;
            }
            Outline(new Rect(rect.x + 1, rect.y + 4, 14, 8), DimColor);
            for (var step = 0; step < 12; step++) EditorGUI.DrawRect(new Rect(rect.x + 2 + step, rect.y + 13 - step, 1, 1), DimColor);
        }

        // Text is clipped with an ellipsis so long names never widen a row.
        string Fit(string text, GUIStyle style, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return "";
            var key = (text, style, Mathf.FloorToInt(width));
            if (fitted.TryGetValue(key, out var value)) return value;
            if (style.CalcSize(new GUIContent(text)).x <= width) value = text;
            else
            {
                int low = 0, high = text.Length;
                while (low < high)
                {
                    var middle = (low + high + 1) / 2;
                    if (style.CalcSize(new GUIContent(text.Substring(0, middle) + "…")).x <= width) low = middle; else high = middle - 1;
                }
                value = low > 0 ? text.Substring(0, low) + "…" : "…";
            }
            if (fitted.Count > 4096) fitted.Clear();
            fitted[key] = value;
            return value;
        }

        void EnsureStyles()
        {
            if (cardText != null && accentNormal != null) return;
            GUIStyle Label(GUIStyle basis, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
            {
                var style = new GUIStyle(basis) { alignment = anchor, clipping = TextClipping.Clip, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
                return style;
            }
            cardText = Label(EditorStyles.label, TextColor);
            cardTextDisabled = Label(EditorStyles.label, Hex(0x6e6e6e));
            dimText = Label(EditorStyles.label, DimColor);
            boldText = Label(EditorStyles.boldLabel, TextColor);
            dimMini = Label(EditorStyles.miniLabel, DimColor);
            errorMini = Label(EditorStyles.miniLabel, ErrorColor);
            badgeText = Label(EditorStyles.miniLabel, DimColor, TextAnchor.MiddleCenter);
            closeText = Label(EditorStyles.label, DimColor, TextAnchor.MiddleCenter);
            closeText.hover.textColor = TextColor;
            centeredDim = Label(EditorStyles.miniLabel, DimColor, TextAnchor.MiddleCenter);
            Texture2D Solid(Color color)
            {
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                texture.SetPixel(0, 0, color); texture.Apply();
                return texture;
            }
            ReleaseTextures();
            accentNormal = Solid(AccentButtonColor); accentHover = Solid(Hex(0x3569a0)); accentActive = Solid(Hex(0x24507a));
            accentButton = new GUIStyle(GUI.skin.button) { border = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip, wordWrap = false };
            void State(GUIStyleState state, Texture2D background)
            {
                state.background = background; state.scaledBackgrounds = Array.Empty<Texture2D>(); state.textColor = Color.white;
            }
            State(accentButton.normal, accentNormal); State(accentButton.hover, accentHover); State(accentButton.active, accentActive); State(accentButton.focused, accentNormal);
            fitted.Clear();
        }

        void ReleaseTextures()
        {
            foreach (var texture in new[] { accentNormal, accentHover, accentActive }) if (texture != null) DestroyImmediate(texture);
            accentNormal = accentHover = accentActive = null;
        }

        void ReleaseStyles() { ReleaseTextures(); cardText = null; fitted.Clear(); }

        // ---- Save ----

        void DrawSave()
        {
            EditorGUILayout.LabelField(ExporterLocalization.T("保存"), EditorStyles.boldLabel);
            EditorGUILayout.Space(6);
            if (transferMode)
            {
                // The transfer window asks these; they are not asked twice.
                var line = GUILayoutUtility.GetRect(100, 18, GUILayout.ExpandWidth(true));
                var later = ExporterLocalization.T("作者名と利用条件は次の画面で入力します");
                GUI.Label(line, new GUIContent(Fit(later, dimText, line.width), later), dimText);
            }
            else
            {
                author = EditorGUILayout.TextField(new GUIContent(ExporterLocalization.T("作者名"), ExporterLocalization.T("VRMファイルに記録される作者名です。")), author);
                // The line is always reserved so typing does not move the rows below.
                EditorGUILayout.LabelField(ExportAuthor.IsValid(author) ? "" : ExporterLocalization.T(ExportAuthor.Required), EditorStyles.miniLabel);
                if (licenseOptions == null) licenseOptions = new AvatarLicenseOptions();
                AvatarLicenseSettingsUi.Draw(ref showLicenseSettings, licenseOptions);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                automaticBlink = EditorGUILayout.Toggle(ExporterLocalization.T(configuredBlink?.Mode == BlinkExportMode.Manual ? "瞬き" : "瞬きを自動設定する"), automaticBlink);
                if (GUILayout.Button(ExporterLocalization.T("確認・調整"), GUILayout.Width(100))) Queue(ShowBlinkConfiguration);
            }
            EditorGUILayout.Space(8);
            if (!transferMode) DrawSavedResult();
        }

        void ShowFace(string name) { previewName = name; Repaint(); }

        // Without exclusions or added files nothing is recorded and the
        // ordinary export keeps the avatar's own faces. Otherwise exactly the
        // usable faces of the custom list are recorded under their file names.
        void RecordBoardClips()
        {
            RebuildBoard();
            var exported = board.UsesRecordRoute ? board.ExportedCustom() : new System.Collections.Generic.List<AnimationClip>();
            foreach (var input in clipInputs) input.Time = DefaultTime(input.Clip);
            var inputs = exported.Select(clip => clipInputs.FirstOrDefault(input => input.Clip == clip) is var added && added != null
                ? added : new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Time = DefaultTime(clip) }).ToArray();
            foreach (var input in inputs) input.Name = input.Clip.name;
            var poses = session.ReplaceClipRecords(inputs, fileRecords.Values);
            fileRecords.Clear();
            for (var index = 0; index < inputs.Length; index++) fileRecords.Add(inputs[index], poses[index]);
            if (poses.Length > 0) status = ExporterLocalization.T("表情を記録・更新しました: ") + poses.Length + ExporterLocalization.T("件");
        }

        void DrawSavedResult()
        {
            if (saved == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(ExporterLocalization.T("VRMを保存しました"), EditorStyles.boldLabel);
                EditorGUILayout.LabelField(Path.GetFileName(saved.Path) + ExporterLocalization.T("　/　表情 ") + saved.Expressions + ExporterLocalization.T("件・ポーズ ") + saved.Poses + ExporterLocalization.T("件"), EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(ExporterLocalization.T("下の転送は最後に保存したVRMを送ります。編集した内容を送るには、もう一度保存してください。"), EditorStyles.wordWrappedMiniLabel);
                var transfer = LilToonExporterWindow.SavedVrmTransferMethod();
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(transfer == null || !File.Exists(saved.Path)))
                        if (GUILayout.Button(ExporterLocalization.T("QRコードでスマホに送る"), GUILayout.Height(32))) Queue(OpenSavedTransfer);
                    using (new EditorGUI.DisabledScope(!File.Exists(saved.Path)))
                        if (GUILayout.Button(ExporterLocalization.T("保存先を開く"), GUILayout.Height(32))) EditorUtility.RevealInFinder(saved.Path);
                }
                if (!File.Exists(saved.Path)) EditorGUILayout.HelpBox(ExporterLocalization.T("保存したVRMが見つかりません。もう一度保存してください。"), MessageType.Warning);
                else if (transfer == null) EditorGUILayout.HelpBox(ExporterLocalization.T("このパッケージにはQR転送がありません。QR対応の試験用パッケージを導入してください。"), MessageType.Info);
                else EditorGUILayout.LabelField(ExporterLocalization.T("次の画面でアップロードを開始します。QR受信対応のiPhone版VR Vlogを使ってください。"), EditorStyles.wordWrappedMiniLabel);
            }
        }

        Rect ScreenRect(Rect rect) => new Rect(GUIUtility.GUIToScreenPoint(rect.position) - position.position, rect.size);
        internal static float DefaultTime(AnimationClip clip) => clip == null ? 0 : clip.length;

        void PreviewClipInput(ExperimentalExpressionCaptureSession.ClipInput input)
        {
            input.Time = DefaultTime(input.Clip);
            input.Error = session.ClipError(input.Clip, input.Time);
            if (input.Error != null) throw new InvalidOperationException(input.Error);
            session.PreviewClip(input.Clip, input.Time);
            ShowFace(input.Name);
        }

        void AddClips(AnimationClip[] clips)
        {
            var additions = clips.Where(clip => clip != null && !clipInputs.Any(input => input.Clip == clip)).Distinct().ToArray();
            if (clipInputs.Count + additions.Length > ExperimentalExpressionCaptureSession.MaximumExpressions)
                throw new InvalidOperationException(ExporterLocalization.T("表情は64件までです。選択を減らして追加してください。"));
            foreach (var clip in additions) AddClipCore(clip, false);
            if (additions.Length > 0) status = string.Format(ExporterLocalization.T("{0}件の表情を追加しました"), additions.Length);
            ApplyAutomaticPresets();
        }

        void AddClip(AnimationClip clip) => AddClipCore(clip, true);

        void AddClipCore(AnimationClip clip, bool suggest)
        {
            if (clip == null || clipInputs.Any(input => input.Clip == clip)) return;
            if (clipInputs.Count >= ExperimentalExpressionCaptureSession.MaximumExpressions)
                throw new InvalidOperationException(ExporterLocalization.T("表情は64件までです。"));
            var input = new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = clip.name, Time = DefaultTime(clip) };
            input.Error = session.ClipError(clip, input.Time);
            clipInputs.Add(input);
            if (input.Error == null) { session.PreviewClip(clip, input.Time); ShowFace(input.Name); }
            status = input.Error == null ? ExporterLocalization.T("表情を追加しました") : ExporterLocalization.T("対応外のファイルはその他の表情に灰色で表示します");
            if (suggest) ApplyAutomaticPresets();
        }

        void DropClips(string label, Action<AnimationClip[]> accept)
        {
            var area = GUILayoutUtility.GetRect(100, 42, GUILayout.ExpandWidth(true));
            GUI.Box(area, label);
            var current = Event.current;
            if (!area.Contains(current.mousePosition) || current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return;
            var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().Where(clip => clip != null).Distinct().ToArray();
            var valid = GUI.enabled && clips.Length > 0 && DragAndDrop.objectReferences.All(item => item is AnimationClip);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (valid && current.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); Queue(() => accept(clips)); }
            current.Use();
        }

        void LoadPresetChoices()
        {
            presetKey = ExpressionPresetStore.KeyFor(source);
            session.PresetShapeOptions(); // Build the BlendShape list once, outside OnGUI.
            session.Presets.Clear();
            foreach (var assignment in ExpressionPresetStore.Load(presetKey, out presetSkipped))
                session.SetPreset(ExperimentalExpressionCaptureSession.ParsePreset(assignment.Preset), assignment);
            board.Excluded.Clear();
            board.Excluded.UnionWith(ExpressionPresetStore.LoadExcluded(presetKey, out var skippedExclusions));
            presetSkipped += skippedExclusions;
            ApplyAutomaticPresets();
        }

        // Suggestions follow the current faces and are recomputed when they
        // change. The user's own choices, including "none", are never replaced.
        void ApplyAutomaticPresets()
        {
            if (session == null) return;
            RebuildBoard();
            board.ApplyAutomatic();
            RefreshPresetErrors();
        }

        void RefreshPresetErrors()
        {
            presetErrors.Clear();
            if (session == null) return;
            foreach (var pair in session.Presets) presetErrors[pair.Key] = session.PresetError(pair.Key, pair.Value);
        }

        void ChoosePreset(UniVRM10.ExpressionPreset preset, ExperimentalExpressionCaptureSession.PresetAssignment assignment)
        {
            session.SetPreset(preset, assignment);
            SaveChoices();
            presetErrors[preset] = session.PresetError(preset, assignment);
            if (assignment.IsNone) { session.RestoreBaseline(); ShowFace(ExporterLocalization.T("基準の顔")); return; }
            if (presetErrors[preset] != null) throw new InvalidOperationException(presetErrors[preset]);
            session.PreviewPreset(preset, assignment);
            ShowFace(ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset)));
        }

        // Faces on the board move through the board, so the face leaves its
        // previous place; BlendShapes and "none" are set on the slot directly.
        void ShowPresetMenu(UniVRM10.ExpressionPreset preset, Rect rect)
        {
            var menu = new GenericMenu();
            session.Presets.TryGetValue(preset, out var current);
            string Item(string value) => value.Replace("/", "／");
            void Add(string label, ExperimentalExpressionCaptureSession.PresetAssignment assignment, bool on) =>
                menu.AddItem(new GUIContent(label), on, () => Queue(() => ChoosePreset(preset, assignment)));
            Add(ExporterLocalization.T(session.HasAuthoredPreset(preset) ? "元のVRM設定" : "なし"), ExperimentalExpressionCaptureSession.PresetAssignment.None(preset), current == null || current.IsNone);
            if (current != null && !current.Automatic)
                menu.AddItem(new GUIContent(ExporterLocalization.T("自動の候補に戻す")), false, () => Queue(() => {
                    session.SetPreset(preset, null); SaveChoices(); ApplyAutomaticPresets();
                }));
            menu.AddSeparator("");
            foreach (var item in board.Items.Where(ExpressionBoard.Usable))
            {
                var clip = item.Clip;
                var group = item.Added ? ExporterLocalization.T("追加した表情") : item.Source == "FaceEmoの登録" ? "FaceEmo" : ExporterLocalization.T("アバターのController");
                menu.AddItem(new GUIContent(group + "/" + Item(clip.name)), current?.ResolveClip() == clip, () => Queue(() => MoveTo(clip, Place.Slot(preset))));
            }
            System.Collections.Generic.IReadOnlyList<(string Path, string Shape)> options;
            try { options = session.PresetShapeOptions(); }
            catch (InvalidOperationException exception) { error = exception.Message; options = Array.Empty<(string, string)>(); }
            foreach (var option in options)
                Add("BlendShape/" + Item(string.IsNullOrEmpty(option.Path) ? "(root)" : option.Path) + "/" + Item(option.Shape),
                    ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(preset, option.Path, option.Shape),
                    current != null && current.IsShape && current.Path == option.Path && current.Shape == option.Shape);
            menu.DropDown(rect);
        }

        void DrawPoseInputs()
        {
            EditorGUILayout.Space();
            showPoses = EditorGUILayout.Foldout(showPoses, ExporterLocalization.T("全身ポーズを追加（任意・") + session.PoseOptions.Manual.Count + ExporterLocalization.T("件）"), true);
            if (!showPoses) return;
            DropClips(ExporterLocalization.T("UnityのProjectからポーズの.animをここへドロップ"), clips => {
                foreach (var clip in clips)
                    if (!session.PoseOptions.Manual.Any(pose => pose.Clip == clip))
                        session.PoseOptions.Manual.Add(new ManualPose { Clip = clip, Name = clip.name });
            });
            foreach (var pose in session.PoseOptions.Manual.ToArray())
            {
                EditorGUILayout.BeginHorizontal();
                pose.Name = EditorGUILayout.TextField(pose.Name);
                if (GUILayout.Button(ExporterLocalization.T("削除"), GUILayout.Width(50))) Queue(() => session.PoseOptions.Manual.Remove(pose));
                EditorGUILayout.EndHorizontal();
            }
            if (session.PoseOptions.Manual.Count > 0 && GUILayout.Button(ExporterLocalization.T("ポーズを確認"))) Queue(() => {
                poseReview?.Close();
                poseReview = PoseReviewWindow.Show(source, session.PoseOptions, Array.Empty<GameObject>(), new ExportGimmickOptions { AutoExclude = false }, manualOnly: true);
            });
            EditorGUILayout.LabelField(ExporterLocalization.T("ポーズと表情は別々に選べるデータとして同じVRMへ保存します。"), EditorStyles.wordWrappedMiniLabel);
        }

        void Export()
        {
            RequireAuthor();
            RequireWithinLimit();
            var path = EditorUtility.SaveFilePanel(ExporterLocalization.T("VRMの保存先"), "", AvatarName() + "-expressions", "vrm");
            if (string.IsNullOrEmpty(path)) return;
            SaveVrm(path, interactive: true);
        }

        // The disabled button is the normal guard; this also stops a call from
        // elsewhere before any record, file or recovery session is started.
        void RequireAuthor()
        {
            if (!ExportAuthor.IsValid(author)) throw new InvalidOperationException(ExporterLocalization.T(ExportAuthor.Required));
        }

        void RequireWithinLimit()
        {
            RebuildBoard();
            if (board != null && board.ExceedsLimit)
                throw new InvalidOperationException(string.Format(ExporterLocalization.T("その他の表情が64件を超えています・{0}件を書き出さないへ移すまで保存しません"), board.OverLimitBy));
            if (board != null && board.HasDuplicateNames)
                throw new InvalidOperationException(DuplicateNamesText(ExporterLocalization.T("同じ名前の表情が{0}組あります・片方を書き出さないへ移すまで保存しません")));
        }

        string DuplicateNamesText(string format)
        {
            var names = board.DuplicateNames();
            return string.Format(format, names.Length) + ": " + string.Join(" / ", names);
        }

        internal ExportRecoverySession SaveVrm(string path, bool interactive = false)
        {
            RequireAuthor();
            return Save(path, AvatarName(), author, licenseOptions.Copy(), interactive, null);
        }

        // The detailed "アプリに転送" writes what this board shows, with the name,
        // author and license of the transfer window. Returns null when it saved
        // (saved has run) or was canceled, and the reason when it failed; the
        // recovery window then saves the same destination and runs saved.
        internal string TransferExport(string name, string author, AvatarLicenseOptions license, string destination, Action saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (session == null) return ExporterLocalization.T(AppTransferExport.BoardClosed);
            if (!ExportAuthor.IsValid(author)) return ExporterLocalization.T(ExportAuthor.Required);
            var recovery = Save(destination, name, author, (license ?? new AvatarLicenseOptions()).Copy(), true, saved);
            return recovery == null ? null : recovery.Failure?.Message ?? "";
        }

        // One save path for the file and the transfer. A transfer (written set)
        // leaves the saved file of this window as it was.
        ExportRecoverySession Save(string path, string name, string targetAuthor, AvatarLicenseOptions license, bool interactive, Action written)
        {
            if (excludedObjectEdits != null) throw new InvalidOperationException(ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません"));
            RequireWithinLimit();
            RecordBoardClips();
            var target = session;
            ExportAuthor.Remember(target.Source, targetAuthor);
            var blink = automaticBlink ? configuredBlink?.Copy() : new BlinkExportOptions { Mode = BlinkExportMode.None };
            var expressions = target.Expressions.Select(ExperimentalExpressionCaptureSession.ClonePose).ToArray();
            var poses = target.PoseOptions.Copy();
            var presets = target.Presets.Values.Select(value => value.Copy()).ToArray();
            var expressionCount = expressions.Length;
            // The record route replaces the menu faces even when nothing is left to record.
            var replaceCustomFaces = board.UsesRecordRoute;
            void Completed(byte[] bytes, System.Collections.Generic.IEnumerable<string> warnings)
            {
                if (written != null)
                {
                    // Like the quick transfer, the details of this export go to the Console.
                    var details = (warnings ?? Enumerable.Empty<string>()).ToArray();
                    if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
                    written();
                    return;
                }
                if (this == null || !ReferenceEquals(session, target)) return;
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var poseCount = extensions.TryGetValue(VRVlog.Poses.HumanoidPoseData.Extension, out var poses)
                    ? VRVlog.Poses.HumanoidPoseData.Read(poses).Count : 0;
                if (extensions.TryGetValue(VRVlog.Poses.HumanoidAnimationData.Extension, out var animations))
                    poseCount += VRVlog.Poses.HumanoidAnimationData.Read(animations).Count;
                saved = new SavedExpressionVrm(path, bytes, expressionCount, poseCount);
                status = ExporterLocalization.T("VRMを保存しました。") + (warnings.Any() ? "\n" + string.Join("\n", warnings) : "");
                Repaint();
            }
            var recovery = LilToonExporterWindow.ExportAndSaveNormally(target.Source, path, (options, report, warnings) => {
                foreach (var warning in target.Warnings) warnings.Add(warning);
                return target.Export(name, targetAuthor, warnings, blink?.Copy(), license.Copy(), options, report, expressions, poses,
                    presets.Select(value => value.Copy()).ToArray(), replaceCustomFaces);
            }, Completed, previewExcludedObjects: target.ExcludedObjects, previewGimmicks: target.GimmickOptions, interactive: interactive,
                openBlinkSettings: () => { if (this != null && ReferenceEquals(session, target)) ShowBlinkConfiguration(); });
            if (recovery != null)
            {
                recovery.RetainInput(target.RetainForRecovery());
                try { ExportFailureWindow.Show(recovery, () => Completed(recovery.LastSuccess.Bytes, recovery.LastSuccess.Warnings)); }
                catch { recovery.ReleaseInput(); throw; }
            }
            return recovery;
        }

        internal void OpenSavedTransfer()
        {
            if (saved == null) throw new InvalidOperationException(ExporterLocalization.T("先にVRMを保存してください。"));
            var entry = LilToonExporterWindow.SavedVrmTransferMethod();
            if (entry == null) throw new InvalidOperationException(ExporterLocalization.T("QR対応のパッケージを導入してください。"));
            try { entry.Invoke(null, new object[] { saved.VerifiedPath() }); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.Data["VRVlog.ShowMessageAsIs"] is bool asIs && asIs)
            { throw new InvalidOperationException(ExporterLocalization.T(exception.InnerException.Message)); }
            catch (System.Reflection.TargetInvocationException)
            { throw new InvalidOperationException(ExporterLocalization.T("転送用のコピーを準備できませんでした。256 MiB以下の保存済みVRMを指定してください。")); }
        }

        // The second step of the detailed transfer: the transfer window for this
        // avatar, which exports from this board when its QR is made.
        void OpenTransferDetails()
        {
            if (excludedObjectEdits != null) throw new InvalidOperationException(ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません"));
            RequireWithinLimit();
            var entry = LilToonExporterWindow.DetailedTransferMethod();
            if (entry == null) throw new InvalidOperationException(ExporterLocalization.T("QR対応のパッケージを導入してください。"));
            try { entry.Invoke(null, new object[] { source }); }
            catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException != null)
            { throw new InvalidOperationException(exception.InnerException.Message, exception.InnerException); }
        }

        void ShowBlinkConfiguration()
        {
            blinkConfiguration?.Close();
            var options = automaticBlink ? configuredBlink?.Copy() ?? new BlinkExportOptions() : new BlinkExportOptions { Mode = BlinkExportMode.None };
            blinkConfiguration = BlinkConfigurationWindow.Show(source, options, selected => {
                configuredBlink = selected.Mode == BlinkExportMode.Manual ? selected.Copy() : null;
                automaticBlink = selected.Mode != BlinkExportMode.None;
                Repaint();
            });
        }

        internal static void AtomicWrite(string path, byte[] bytes)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        string AvatarName() => source != null && !string.IsNullOrWhiteSpace(source.name) ? source.name.Trim() : "avatar";

        void SelectSource(GameObject next)
        {
            if (source == next) return;
            if (next != null) author = ExportAuthor.Replace(author, ExportAuthor.ForSelection(ExportAuthor.Remembered(next), author));
            source = next;
            licenseOptions = new AvatarLicenseOptions();
        }

        void ChangeAvatar()
        {
            Cleanup();
            licenseOptions = new AvatarLicenseOptions();
            status = null;
        }

        void Cleanup()
        {
            blinkConfiguration?.Close(); blinkConfiguration = null; configuredBlink = null;
            // The utility owns its lights/camera; session owns the avatar and assets.
            if (session?.RetainPreviewForRecovery(preview) == true) preview = null;
            preview?.Cleanup(); preview = null;
            session?.Dispose(); session = null; board = null;
            poseReview?.Close(); poseReview = null;
            clipErrors.Clear(); candidateRects.Clear(); clipInputs.Clear(); fileRecords.Clear(); recommendations = null; saved = null;
            presetErrors.Clear(); presetKey = null; presetSkipped = 0;
            excludedObjectEdits = null; excludedObjectError = null; showExcludedObjects = null;
            dropHover = dropHoverNext = null; dragMouse = null;
        }
    }
#if UNITY_5_3_OR_NEWER
    // projectChanged does not fire for every in-place .anim reimport.
    internal sealed class ExpressionCaptureAssetPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] previousPaths)
        {
            if (imported.Concat(deleted).Concat(moved).Concat(previousPaths).Any(path =>
                path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".overrideController", StringComparison.OrdinalIgnoreCase)))
                ExperimentalExpressionCaptureWindow.AnimationAssetsReimported();
        }
    }
#endif

}
