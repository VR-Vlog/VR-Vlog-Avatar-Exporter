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
        // Materials whose shader was missing in the last save, the file or the transfer. Read by AppTransferExport.
        internal int LastMissingShaderCount { get; private set; }
        bool showPoses;
        readonly System.Collections.Generic.Dictionary<ExperimentalExpressionCaptureSession.ClipInput, ExperimentalExpressionCaptureSession.Pose> fileRecords =
            new System.Collections.Generic.Dictionary<ExperimentalExpressionCaptureSession.ClipInput, ExperimentalExpressionCaptureSession.Pose>();
        ExperimentalExpressionCaptureSession session;
        ExpressionBoard board;
        PreviewRenderUtility preview;
        Action pending;
        bool candidateValidationPending, opening;
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
        // Eye slots whose error is the closing shape shared with the other eye.
        readonly System.Collections.Generic.HashSet<UniVRM10.ExpressionPreset> eyeOverlaps = new System.Collections.Generic.HashSet<UniVRM10.ExpressionPreset>();
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
        // Window-relative slot rectangles, kept for tests and screenshots.
        readonly System.Collections.Generic.Dictionary<Place, Rect> slotRects = new System.Collections.Generic.Dictionary<Place, Rect>();
        // Tiles with a × button, which shows only under the mouse; the
        // window repaints when the mouse moves onto or off one of them.
        readonly System.Collections.Generic.List<Rect> closableTiles = new System.Collections.Generic.List<Rect>();
        int hoverTile = -1;

        // Slot area: thumbnail size and the fold of gaze and neutral, kept per user.
        const string LargeThumbnailsPreference = "VRVlog.ExpressionBoard.LargeThumbnails";
        const string ExtraPresetsPreference = "VRVlog.ExpressionBoard.ShowExtraPresets";
        bool? largeThumbnails, showExtraPresets;
        // Height of the other/excluded panels, dragged at their bottom edge.
        const string PanelHeightPreference = "VRVlog.ExpressionBoard.PanelHeight";
        float? panelHeight;
        float panelDragStart, panelDragHeight;
        // The page view in window coordinates, from the last repaint.
        Rect pageView;
        // How tall the page is inside the view, from the last repaint.
        float pageContentHeight;
        // The excluded panel in window coordinates, beside expressionDropRect.
        Rect excludedDropRect;
        // The width the slots were laid out for, from the last repaint, so every
        // event of a frame places the slots the same way.
        float slotAreaWidth;

        // Thumbnails are drawn a few per update on the preview copy and kept
        // until the faces can change; null marks a face that could not be drawn.
        sealed class ThumbnailRequest { public AnimationClip Clip; public string Path, Shape; public float Size; }
        readonly System.Collections.Generic.Dictionary<string, Texture2D> thumbnails = new System.Collections.Generic.Dictionary<string, Texture2D>();
        readonly System.Collections.Generic.Dictionary<string, ThumbnailRequest> thumbnailRequests = new System.Collections.Generic.Dictionary<string, ThumbnailRequest>();
        readonly System.Collections.Generic.HashSet<string> thumbnailsShown = new System.Collections.Generic.HashSet<string>();
        // Faces that failed to draw stay plain until the files or the preview
        // change, so a broken face is tried and reported once, not every frame.
        readonly System.Collections.Generic.HashSet<string> thumbnailFailures = new System.Collections.Generic.HashSet<string>();
        bool boardDrawn;
        const int ThumbnailsPerUpdate = 4;
        const float ThumbnailDistance = .6f;
        // What an unchosen blink slot inherits; resolving it reads the avatar, so
        // it is computed once per change outside OnGUI.
        string blinkFallback;
        bool blinkFallbackStale = true;

        // Fixed sizes: the board never follows the font size or the names.
        const float Gap = ExpressionSlotLayout.Gap, PanelHeader = 26;
        internal const float PanelHeight = 300, MinPanelHeight = 160, MaxPanelHeight = 1000, PanelHandle = 6;
        // The skin's own scrollbar, read while drawing, not a fixed guess.
        static float ScrollbarWidth => ExpressionSlotLayout.ScrollbarWidth(GUI.skin.verticalScrollbar);
        // The mock colors are the dark skin; the light skin gets the same roles.
        static Color Skin(int dark, int light) => Hex(EditorGUIUtility.isProSkin ? dark : light);
        static Color LineColor => Skin(0x242424, 0x999999);
        static Color PanelColor => Skin(0x2e2e2e, 0xd6d6d6);
        static Color CardColor => Skin(0x383838, 0xe4e4e4);
        static Color CardBorderColor => Skin(0x585858, 0x9a9a9a);
        static Color DashColor => Skin(0x9a9a9a, 0x6a6a6a);
        static Color TextColor => Skin(0xd2d2d2, 0x1e1e1e);
        static Color DimColor => Skin(0x9a9a9a, 0x5a5a5a);
        static Color AccentColor => Skin(0x7fa6c9, 0x3a72b0);
        static Color ErrorColor => Skin(0xe0675f, 0xb3261e);
        static Color ThumbEmpty => Skin(0x333333, 0xcfcfcf);
        static readonly Color AccentButtonColor = Hex(0x2c5d87), BadgeColor = new Color(.13f, .13f, .13f, .85f);
        GUIStyle cardText, cardTextDisabled, dimText, errorText, dimMini, dimMiniRight, errorMini, boldText, badgeText, closeText, accentButton, centeredDim,
            nameCenter;
        bool stylesForProSkin;
        Texture2D accentNormal, accentHover, accentActive;
        readonly System.Collections.Generic.Dictionary<(string, GUIStyle, int), string> fitted = new System.Collections.Generic.Dictionary<(string, GUIStyle, int), string>();

        // The "アプリに転送" window starts from what this window shows: these
        // values live only in the open window and are not stored anywhere else.
        internal string Author => author;
        internal AvatarLicenseOptions LicenseFor(GameObject avatar) => avatar != null && source == avatar ? licenseOptions?.Copy() : null;
        internal bool TransferMode => transferMode;
        // The board header counts the same 13 slots ("割当x/13").
        internal int SlotCount => board?.SlotCount(MainPresets) ?? 0;
        internal static int MainSlotCount => MainPresets.Length;
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

        void OnEnable() { wantsMouseMove = true; EditorApplication.update += Advance; EditorApplication.projectChanged += CandidateAssetsChanged; }
        void OnDisable() { EditorApplication.update -= Advance; EditorApplication.projectChanged -= CandidateAssetsChanged; pending = null; Cleanup(); ReleaseStyles(); }

        void Advance()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            if (pending == null && candidateValidationPending && session != null) pending = RefreshCandidateValidation;
            if (pending == null) { AdvanceIdle(); return; }
            var action = pending; pending = null;
            try { using (ExperimentalExpressionCaptureSession.Batch()) action(); error = null; }
            catch (OperationCanceledException) { status = ExporterLocalization.T("操作をキャンセルしました。"); }
            catch (Exception exception) { error = exception.Message; Debug.LogException(exception); }
            finally { EditorUtility.ClearProgressBar(); Repaint(); }
        }

        void Queue(Action action) { pending = action; error = null; Repaint(); }

        internal static void AnimationAssetsReimported()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>()) window.CandidateAssetsChanged();
        }

        // A reimported file can draw a different face, so its thumbnail goes too.
        void CandidateAssetsChanged() { candidateValidationPending = true; ClearThumbnails(); Repaint(); }

        // Work that waits for an update with nothing queued: the blink line of
        // unchosen blink slots and a few thumbnails.
        void AdvanceIdle()
        {
            if (session == null || preview == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            using var batch = ExperimentalExpressionCaptureSession.Batch();
            RefreshBlinkFallback();
            if (thumbnailRequests.Count > 0) using (ExpressionBoardTimings.Step("thumbnails")) RenderThumbnails();
        }

        void RefreshBlinkFallback()
        {
            if (!blinkFallbackStale || session == null) return;
            blinkFallbackStale = false;
            using var timing = ExpressionBoardTimings.Step("blinkFallback");
            try { blinkFallback = session.BlinkFallbackDescription(ExportBlinkOptions()); }
            catch (Exception exception) when (!(exception is OperationCanceledException)) { blinkFallback = exception.Message; }
            Repaint();
        }

        // The key holds the drawn size in pixels, so a new size or display scale
        // draws again instead of stretching an old texture.
        Texture2D Thumbnail(AnimationClip clip, string path, string shape, float size)
        {
            if (clip == null && shape == null) return null;
            var pixels = Mathf.RoundToInt(size * EditorGUIUtility.pixelsPerPoint);
            var key = (clip != null ? "c" + clip.GetInstanceID() : "s" + path + "/" + shape) + "@" + pixels;
            if (Event.current != null && Event.current.type == EventType.Repaint) thumbnailsShown.Add(key);
            if (thumbnails.TryGetValue(key, out var texture)) return texture;
            if (thumbnailFailures.Contains(key)) return null;
            if (!thumbnailRequests.ContainsKey(key)) thumbnailRequests[key] = new ThumbnailRequest { Clip = clip, Path = path, Shape = shape, Size = size };
            return null;
        }

        // Each request leaves the queue whatever happens to it: drawn, or
        // remembered as failed. When the preview's own face cannot be read or
        // put back, the rest of the queue fails too, so nothing is retried
        // every update.
        void RenderThumbnails()
        {
            ExperimentalExpressionCaptureSession.Pose original;
            try { original = session.CurrentFace(); }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                FailThumbnails(thumbnailRequests.Keys.ToArray(), exception);
                Repaint();
                return;
            }
            try
            {
                foreach (var key in thumbnailRequests.Keys.Take(ThumbnailsPerUpdate).ToArray())
                {
                    var request = thumbnailRequests[key];
                    thumbnailRequests.Remove(key);
                    Texture2D texture;
                    try { texture = RenderThumbnail(request); }
                    catch (Exception exception) when (!(exception is OperationCanceledException)) { FailThumbnails(new[] { key }, exception); continue; }
                    if (texture != null) thumbnails[key] = texture;
                    else FailThumbnails(new[] { key }, null);
                }
            }
            finally
            {
                // The preview shows the face it showed before.
                try { session.ShowFace(original); preview.camera.ResetAspect(); }
                catch (Exception exception) when (!(exception is OperationCanceledException)) { FailThumbnails(thumbnailRequests.Keys.ToArray(), exception); }
            }
            Repaint();
        }

        // Failed faces stay plain squares; each is reported once in the Console.
        void FailThumbnails(string[] keys, Exception exception)
        {
            foreach (var key in keys)
            {
                thumbnailRequests.Remove(key);
                if (thumbnailFailures.Add(key) && exception != null)
                    Debug.LogWarning(string.Format(ExporterLocalization.T("表情のサムネイルを描けませんでした: {0}"), exception.Message));
            }
        }

        // A face whose pose cannot be built returns null and stays a plain
        // square; a failed render is thrown to RenderThumbnails.
        Texture2D RenderThumbnail(ThumbnailRequest request)
        {
            try
            {
                session.ShowFace(request.Clip != null ? session.ThumbnailFace(request.Clip) : session.ThumbnailFace(request.Path, request.Shape));
            }
            catch (Exception exception) when (!(exception is OperationCanceledException)) { return null; }
            preview.BeginStaticPreview(new Rect(0, 0, request.Size, request.Size));
            try
            {
                // Straight at the face, as the preview starts.
                preview.camera.aspect = 1;
                preview.camera.transform.position = center + session.Copy.transform.forward * distance * ThumbnailDistance;
                preview.camera.transform.LookAt(center, Vector3.up);
                preview.Render();
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                var failed = preview.EndStaticPreview();
                if (failed != null) DestroyImmediate(failed);
                throw;
            }
            var texture = preview.EndStaticPreview();
            if (texture != null) texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        void ClearThumbnails()
        {
            foreach (var texture in thumbnails.Values) if (texture != null) DestroyImmediate(texture);
            thumbnails.Clear(); thumbnailRequests.Clear(); thumbnailsShown.Clear(); thumbnailFailures.Clear();
        }

        // After a repaint, faces no tile showed (a removed file, the other size)
        // are let go.
        void PruneThumbnails()
        {
            foreach (var key in thumbnails.Keys.Where(key => !thumbnailsShown.Contains(key)).ToArray())
            {
                if (thumbnails[key] != null) DestroyImmediate(thumbnails[key]);
                thumbnails.Remove(key);
            }
            foreach (var key in thumbnailRequests.Keys.Where(key => !thumbnailsShown.Contains(key)).ToArray()) thumbnailRequests.Remove(key);
        }

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
                // An edit can leave the objects outside the avatar or make them hide
                // every mesh; the preview then opens with every object and the list
                // waits with the reason, as when the window opens.
                var current = session.ExcludedObjects.ToArray();
                var problem = current.Length > 0 ? RebuildKeepingAssignments(current, Array.Empty<GameObject>()) : RebuildKeepingAssignments(current);
                if (problem != null) { excludedObjectEdits = current.ToList(); excludedObjectError = problem; showExcludedObjects = true; }
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
        // the poses, the blink choice and the object list still being edited. With
        // objects that leave no preview, the previous objects are left out again and
        // the reason is returned; null when the preview has the objects asked for.
        string RebuildKeepingAssignments(GameObject[] objects, GameObject[] previous = null)
        {
            var inputs = clipInputs.ToArray(); var excluded = board?.Excluded.ToArray() ?? Array.Empty<AnimationClip>();
            var poses = session.PoseOptions.Copy(); var blink = configuredBlink?.Copy(); var previousSaved = saved;
            var presets = session.Presets.Values.Select(value => value.Copy()).ToArray();
            var edits = excludedObjectEdits; var editError = excludedObjectError; var showEdits = showExcludedObjects;
            string problem = null;
            try { PrepareWith(objects); }
            catch (Exception exception) when (previous != null && !(exception is OperationCanceledException))
            {
                problem = exception.Message;
                PrepareWith(previous);
            }
            excludedObjectEdits = edits; excludedObjectError = editError; showExcludedObjects = showEdits;
            clipInputs.AddRange(inputs);
            board.Excluded.Clear(); board.Excluded.UnionWith(excluded);
            session.Presets.Clear();
            foreach (var preset in presets) session.SetPreset(ExperimentalExpressionCaptureSession.ParsePreset(preset.Preset), preset);
            session.PoseOptions.Manual.AddRange(poses.Manual); session.PoseOptions.Excluded.UnionWith(poses.Excluded);
            foreach (var pair in poses.Names) session.PoseOptions.Names.Add(pair.Key, pair.Value);
            configuredBlink = blink; saved = previousSaved;
            return problem;
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
            if (current.type == EventType.MouseMove)
            {
                var hovered = closableTiles.FindIndex(tile => tile.Contains(current.mousePosition));
                if (hovered != hoverTile) { hoverTile = hovered; Repaint(); }
            }
            var repaint = current.type == EventType.Repaint;
            // Slots the fold hides drop out of slotRects until they are drawn again.
            if (repaint) { closableTiles.Clear(); thumbnailsShown.Clear(); slotRects.Clear(); boardDrawn = false; }
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
                        // A card dragged near the top or bottom edge scrolls the page.
                        if (current.type == EventType.DragUpdated && DragAndDrop.GetGenericData(DragKey) is AnimationClip)
                        {
                            var step = DragScroll(pageView, current.mousePosition, DropTargetAt(current.mousePosition), pageScroll.y,
                                Mathf.Max(0, pageContentHeight - pageView.height));
                            if (step != 0) { pageScroll.y += step; Repaint(); }
                        }
                        pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
                        var pageHidden = pageView.height > 0 ? HideOutside(new Rect(pageScroll, pageView.size)) : null;
                        DrawSlots();
                        EditorGUILayout.Space(8);
                        DrawPanels();
                        DrawAddRow();
                        EditorGUILayout.Space(12);
                        DrawExcludedObjects();
                        EditorGUILayout.Space(12);
                        DrawPoseInputs();
                        EditorGUILayout.Space(16);
                        DrawSave();
                        DrawFeedback();
                        if (current.type == EventType.Repaint) pageContentHeight = GUILayoutUtility.GetLastRect().yMax;
                        RestoreEvent(pageHidden);
                        EditorGUILayout.EndScrollView();
                        if (current.type == EventType.Repaint)
                        {
                            // The panel height is clamped to the view: draw again when the view changed.
                            var view = GUILayoutUtility.GetLastRect();
                            if (view.height != pageView.height) Repaint();
                            pageView = view;
                        }
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
            if (repaint && boardDrawn) PruneThumbnails();
        }

        void DrawFeedback()
        {
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            else if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
        }

        // The objects left out follow the avatar's saved export settings. When the
        // preview cannot be built without them, it opens with every object and the
        // saved list waits in the object list with the reason, to be fixed here.
        void Prepare()
        {
            ExpressionBoardTimings.Clear();
            using var timing = ExpressionBoardTimings.Step("prepare");
            var stored = ExportSettingsStore.Load(source).Exclusions();
            using var batch = ExperimentalExpressionCaptureSession.Batch();
            opening = true;
            try
            {
                try { PrepareWith(stored); }
                catch (Exception exception) when (stored.Length > 0 && !(exception is OperationCanceledException))
                {
                    PrepareWith(Array.Empty<GameObject>());
                    excludedObjectEdits = stored.ToList(); excludedObjectError = exception.Message; showExcludedObjects = true;
                }
                // Inside this batch the session's check of the source is already
                // done; on the next update the blink line would hash the avatar again.
                RefreshBlinkFallback();
            }
            finally { opening = false; EditorUtility.ClearProgressBar(); }
        }

        // Preparing holds the editor for a few seconds on a large avatar. Only
        // opening can be cancelled: a rebuild would lose the files and
        // assignments already on the board.
        void PrepareProgress(string step, float progress)
        {
            var title = ExporterLocalization.T("書き出しを準備中");
            if (!opening) EditorUtility.DisplayProgressBar(title, step, progress);
            else if (EditorUtility.DisplayCancelableProgressBar(title, step, progress)) throw new OperationCanceledException();
        }

        void PrepareWith(GameObject[] objects)
        {
            Cleanup();
            try
            {
                using var steps = ExpressionBoardTimings.Sequence();
                steps.Next("session");
                PrepareProgress(ExporterLocalization.T("アバターを読み込み"), 0);
                // The gimmicks left out follow the avatar's saved export settings.
                var stored = ExportSettingsStore.Load(source);
                session = new ExperimentalExpressionCaptureSession(source, replayInstalledDefaults: false, objects, stored.Gimmicks());
                var target = session;
                board = new ExpressionBoard(target.Presets, target.SetPreset, clips => target.SuggestPresets(clips, ExportBlinkOptions()), target.HasAuthoredPreset);
                steps.Next("recommend");
                PrepareProgress(ExporterLocalization.T("表情ファイルを確認"), .45f);
                recommendations = session.RecommendClips();
                steps.Next("clipErrors");
                foreach (var item in session.ClipErrors(recommendations.Select(item => item.Clip))) clipErrors[item.Key] = item.Value;
                steps.Next("preview");
                PrepareProgress(ExporterLocalization.T("プレビューを用意"), .7f);
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
                steps.Next("presets");
                PrepareProgress(ExporterLocalization.T("割り当てを決める"), .75f);
                LoadPresetChoices();
                PrepareProgress(ExporterLocalization.T("割り当てを決める"), 1);
            }
            catch { Cleanup(); throw; }
            finally { if (!opening) EditorUtility.ClearProgressBar(); }
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
        bool CanSave => (transferMode || ExportAuthor.IsValid(author)) && !BlocksExport;

        // What the next step asks to fix before any export, to a file or to the app.
        // It is the board part of BlockReason, so the two never disagree.
        internal bool BlocksExport => BoardBlockReason() != null;

        // Why the save button is off, shown in the bottom bar; null when it can
        // save. The same messages stop the save when it is called anyway.
        internal string BlockReason() =>
            BoardBlockReason() ?? (!transferMode && !ExportAuthor.IsValid(author) ? ExporterLocalization.T(ExportAuthor.Required) : null);

        // The reasons on the board itself, in the order the bottom bar names
        // them; the author is asked apart because the transfer asks it later.
        string BoardBlockReason()
        {
            if (excludedObjectEdits != null) return ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません");
            foreach (var preset in ExperimentalExpressionCaptureSession.AssignablePresets)
                if (presetErrors.TryGetValue(preset, out var reason) && reason != null) return reason;
            // A reason kept for a preset outside the board order still stops it.
            var other = presetErrors.Values.FirstOrDefault(value => value != null);
            if (other != null) return other;
            if (board == null) return ExporterLocalization.T(AppTransferExport.BoardClosed);
            if (board.ExceedsLimit) return string.Format(ExporterLocalization.T(OverLimitFormat), board.OverLimitBy);
            return null;
        }

        const string OverLimitFormat = "その他の表情が64件を超えています・{0}件を書き出さない表情へ移すまで保存しません";
        internal const string MenuShortRecordedFormat = "表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件を記録して書き出しました";
        internal const string MenuShortKeptFormat = "表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件のとおりに書き出すには64件以下にする";
        internal const string MenuShortNotRecordedFormat = "表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件は記録できませんでした";

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

        // Checks the edited list like the export does, rebuilds the preview without
        // them and saves them to the avatar's settings. A list that leaves no preview
        // stays unsaved and on screen with the reason; the previous preview comes back.
        void ApplyExcludedObjects()
        {
            if (excludedObjectEdits == null || session == null) return;
            var objects = excludedObjectEdits.Where(item => item != null).Distinct().ToArray();
            try { new ExportObjectExclusions(source, objects).Dispose(); }
            catch (InvalidOperationException exception) { excludedObjectError = ExporterLocalization.T(exception.Message); return; }
            var edits = excludedObjectEdits;
            excludedObjectEdits = null; excludedObjectError = null;
            var problem = RebuildKeepingAssignments(objects, session.ExcludedObjects.ToArray());
            // The rebuilt copy can change which faces work; check them on the next update.
            candidateValidationPending = true;
            if (problem != null) { excludedObjectEdits = edits; excludedObjectError = problem; return; }
            var settings = ExportSettingsStore.Load(source);
            settings.ExcludedObjects.Clear();
            settings.ExcludedObjects.AddRange(objects);
            ExportSettingsStore.Save(source, settings);
        }

        // ---- Preset slots ----

        // Emotions, blink and mouth: the 13 slots always shown.
        static readonly UniVRM10.ExpressionPreset[] MainPresets = ExperimentalExpressionCaptureSession.PresetGroups
            .Take(ExpressionSlotLayout.MainGroups).SelectMany(group => group.Presets).ToArray();

        // The slots on screen: the 13, and gaze and neutral while the fold is
        // open. The header's confirm button counts and confirms these only, so
        // its number matches the dashed slots the user can see.
        internal static UniVRM10.ExpressionPreset[] ShownPresets(bool extraOpen) => extraOpen
            ? ExperimentalExpressionCaptureSession.PresetGroups.SelectMany(group => group.Presets).ToArray()
            : MainPresets;

        bool LargeThumbnails => largeThumbnails ??= EditorPrefs.GetBool(LargeThumbnailsPreference, false);
        bool ShowExtraPresets => showExtraPresets ??= EditorPrefs.GetBool(ExtraPresetsPreference, false);

        // "割当3/5・未確認2": filled slots, and automatic ones not yet confirmed.
        string Count(UniVRM10.ExpressionPreset[] presets)
        {
            var text = string.Format(ExporterLocalization.T("割当{0}/{1}"), board.SlotCount(presets), presets.Length);
            var automatic = board.AutomaticCount(presets);
            return automatic > 0 ? text + string.Format(ExporterLocalization.T("・未確認{0}"), automatic) : text;
        }

        void DrawSlots()
        {
            var current = Event.current;
            if (current.type == EventType.Repaint) boardDrawn = true;
            var large = LargeThumbnails;
            var header = GUILayoutUtility.GetRect(100, 22, GUILayout.ExpandWidth(true));
            var toolbar = new Rect(header.xMax - 80, header.y + 2, 80, 18);
            var sizeLabel = new Rect(toolbar.x - 70, header.y + 2, 66, 18);
            GUI.Label(sizeLabel, Fit(ExporterLocalization.T("サムネイル"), dimMiniRight, sizeLabel.width), dimMiniRight);
            var size = GUI.Toolbar(toolbar, large ? 1 : 0, new[] { ExporterLocalization.T("小"), ExporterLocalization.T("大") }, EditorStyles.miniButton);
            if (size != (large ? 1 : 0)) { largeThumbnails = large = size == 1; EditorPrefs.SetBool(LargeThumbnailsPreference, large); }
            var right = sizeLabel.x - 8;
            // The confirm button follows the caption text, not the right edge.
            var caption = string.Format(ExporterLocalization.T("VRMの表情 {0}・ドロップで入れ替え"), Count(MainPresets));
            // The button counts and confirms the same slots: the ones on screen.
            var shown = ShownPresets(ShowExtraPresets);
            var automatic = board.AutomaticCount(shown);
            var buttonWidth = automatic > 0 ? Mathf.Min(150, Mathf.Max(0, right - header.x) * .5f) : 0;
            ExpressionSlotLayout.CaptionAndButton(header.x, right, dimMini.CalcSize(new GUIContent(caption)).x, buttonWidth, out var captionWidth, out var buttonX);
            var captionRect = new Rect(header.x, header.y + 2, captionWidth, 18);
            GUI.Label(captionRect, new GUIContent(Fit(caption, dimMini, captionRect.width), caption), dimMini);
            if (automatic > 0)
            {
                var label = string.Format(ExporterLocalization.T("自動の候補{0}件を確定"), automatic);
                var button = new Rect(buttonX, header.y + 2, buttonWidth, 18);
                if (GUI.Button(button, new GUIContent(Fit(label, EditorStyles.miniButton, buttonWidth - 8), label), EditorStyles.miniButton)) Queue(() => ConfirmAutomatic(shown));
            }

            // The slots are placed for the width of the last repaint, so every
            // event of a frame sees the same rectangles. The page scrollbar is
            // left out of the width either way, so it showing up cannot move them.
            var groups = ExperimentalExpressionCaptureSession.PresetGroups;
            if (slotAreaWidth <= 0) slotAreaWidth = position.width - 32;
            var open = ShowExtraPresets;
            var width = Mathf.Min(slotAreaWidth, position.width - 32 - ScrollbarWidth);
            var layout = ExpressionSlotLayout.Compute(width, large, open, groups.Select(group => group.Presets.Length).ToArray());
            var area = GUILayoutUtility.GetRect(100, layout.Height, GUILayout.ExpandWidth(true));
            if (current.type == EventType.Repaint && area.width > 1 && !Mathf.Approximately(area.width, slotAreaWidth)) { slotAreaWidth = area.width; Repaint(); }
            GUI.BeginGroup(area);
            foreach (var group in layout.Groups)
            {
                var presets = groups[group.Index].Presets;
                DrawGroupCaption(group.Caption, groups[group.Index].Label, presets);
                for (var index = 0; index < presets.Length; index++) DrawSlot(presets[index], group.Slots[index], large);
            }
            DrawFoldRow(layout.Fold, open);
            GUI.EndGroup();
        }

        void DrawGroupCaption(Rect rect, string label, UniVRM10.ExpressionPreset[] presets)
        {
            var title = ExporterLocalization.T(label) + "　" + Count(presets);
            GUI.Label(rect, new GUIContent(Fit(title, cardText, rect.width), title), cardText);
            if (presets != ExperimentalExpressionCaptureSession.BlinkPresets) return;
            var note = ExporterLocalization.T("左右はアバターから見て");
            var used = cardText.CalcSize(new GUIContent(title)).x + 10;
            if (used + dimMini.CalcSize(new GUIContent(note)).x <= rect.width)
                GUI.Label(new Rect(rect.x + used, rect.y, rect.width - used, rect.height), note, dimMini);
        }

        // Gaze and neutral sit under this row; closed, it shows their counts.
        void DrawFoldRow(Rect rect, bool open)
        {
            var label = ExporterLocalization.T("追加のプリセット");
            var foldWidth = Mathf.Min(150, rect.width);
            var next = EditorGUI.Foldout(new Rect(rect.x, rect.y + 1, foldWidth, 18), open, new GUIContent(Fit(label, EditorStyles.foldout, foldWidth - 16), label), true);
            if (next != open) { showExtraPresets = next; EditorPrefs.SetBool(ExtraPresetsPreference, next); }
            if (open) return;
            var extra = ExperimentalExpressionCaptureSession.PresetGroups.Skip(ExpressionSlotLayout.MainGroups).ToArray();
            var summary = extra.All(group => board.SlotCount(group.Presets) == 0)
                ? string.Join("・", extra.Select(group => ExporterLocalization.T(group.Label) + " " + group.Presets.Length)) + ExporterLocalization.T("（どれも空）")
                : string.Join("・", extra.Select(group => ExporterLocalization.T(group.Label) + " " + Count(group.Presets)));
            var summaryRect = new Rect(rect.x + foldWidth + 6, rect.y + 1, Mathf.Max(0, rect.width - foldWidth - 6), 18);
            GUI.Label(summaryRect, new GUIContent(Fit(summary, dimMini, summaryRect.width), summary), dimMini);
        }

        void DrawSlot(UniVRM10.ExpressionPreset preset, Rect rect, bool large)
        {
            session.Presets.TryGetValue(preset, out var assignment);
            presetErrors.TryGetValue(preset, out var reason);
            if (reason != null && eyeOverlaps.Contains(preset)) reason = ExporterLocalization.T("左右の目で同じ変形が重なっています");
            var target = Place.Slot(preset);
            var repaint = Event.current.type == EventType.Repaint;
            if (repaint) slotRects[target] = ScreenRect(rect);
            var labelRect = new Rect(rect.x + 1, rect.y, rect.width - 2, ExpressionSlotLayout.Label);
            var label = ExporterLocalization.T(ExperimentalExpressionCaptureSession.PresetLabel(preset));
            GUI.Label(labelRect, Fit(label, cardText, labelRect.width), cardText);
            var key = preset.ToString();
            if (cardText.CalcSize(new GUIContent(label)).x + dimMiniRight.CalcSize(new GUIContent(key)).x + 6 <= labelRect.width)
                GUI.Label(labelRect, key, dimMiniRight);
            if (GUI.Button(labelRect, new GUIContent("", ExporterLocalization.T("一覧から選ぶ")), GUIStyle.none)) ShowPresetMenu(preset, labelRect);

            var size = ExpressionSlotLayout.Thumb(large);
            var tile = new Rect(rect.x, rect.y + ExpressionSlotLayout.Label + 2, rect.width, ExpressionSlotLayout.Tile(large));
            var thumb = new Rect(tile.x + 7, tile.y + 4, size, size);
            var clip = assignment != null && !assignment.IsNone && !assignment.IsShape ? assignment.ResolveClip() : null;
            if (board.Find(clip) != null)
                DrawTile(tile, thumb, clip.name, clip, Thumbnail(clip, null, null, size), assignment.Automatic, assignment.Automatic ? "自動" : null,
                    reason ?? AssetDatabase.GetAssetPath(clip), "その他の表情へ戻す", () => MoveTo(clip, Place.Other));
            else if (assignment != null && !assignment.IsNone)
            {
                // A BlendShape, or a file no longer on the board: it can only be cleared.
                var name = ExperimentalExpressionCaptureSession.PresetDisplayName(assignment);
                var face = assignment.IsShape ? Thumbnail(null, assignment.Path, assignment.Shape, size) : Thumbnail(clip, null, null, size);
                DrawTile(tile, thumb, assignment.IsShape ? assignment.Shape : name, null, face, assignment.Automatic, assignment.Automatic ? "自動" : null,
                    reason ?? name, "指定を外す", () => ChoosePreset(preset, ExperimentalExpressionCaptureSession.PresetAssignment.None(preset)));
            }
            else
            {
                if (repaint) Dashed(thumb, CardBorderColor);
                GUI.Label(thumb, Fit(ExporterLocalization.T("ここへドロップ"), centeredDim, thumb.width), centeredDim);
                // An empty slot can also be filled from the list, without a drag.
                if (GUI.Button(thumb, new GUIContent("", ExporterLocalization.T("一覧から選ぶ")), GUIStyle.none)) ShowPresetMenu(preset, thumb);
            }
            if (repaint && Nullable.Equals(dropHover, target)) Outline(tile, AccentColor);

            var stateRect = new Rect(rect.x + 1, tile.yMax + 1, rect.width - 2, ExpressionSlotLayout.State);
            var state = reason ?? SlotState(preset, assignment);
            // The blink setting's description is too long for a slot; the slot
            // shows its head and the tooltip all of it.
            var shown = reason == null && state == blinkFallback ? StateHead(state) : state;
            var style = reason != null ? errorMini : dimMini;
            GUI.Label(stateRect, new GUIContent(Fit(shown, style, stateRect.width), state), style);
            HandleDrop(rect, target);
        }

        // The part before the first bracket or ": " of a description.
        internal static string StateHead(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var end = text.Length;
            foreach (var mark in new[] { "（", " (", ": " })
            {
                var index = text.IndexOf(mark, StringComparison.Ordinal);
                if (index > 0 && index < end) end = index;
            }
            return text.Substring(0, end);
        }

        // What saving does with the slot.
        string SlotState(UniVRM10.ExpressionPreset preset, ExperimentalExpressionCaptureSession.PresetAssignment assignment)
        {
            if (assignment != null && !assignment.IsNone) return ExporterLocalization.T(assignment.Automatic ? "自動の候補" : "確定");
            if (session.HasAuthoredPreset(preset)) return ExporterLocalization.T("元のVRM設定");
            // A blink slot nobody chose follows the blink setting; "none" chosen
            // on it leaves it empty.
            if ((assignment == null || assignment.Automatic) && ExperimentalExpressionCaptureSession.BlinkPresets.Contains(preset)) return blinkFallback ?? "";
            return ExporterLocalization.T("空のまま保存");
        }

        // A control in a scroll view still sees the mouse where the view hides
        // it, so mouse and drop events outside the visible part are hidden from
        // the controls inside. A pressed control keeps its events.
        EventType? HideOutside(Rect visible)
        {
            var current = Event.current; var type = current.type;
            var pointer = current.isMouse || type == EventType.DragUpdated || type == EventType.DragPerform || type == EventType.ContextClick || type == EventType.ScrollWheel;
            if (!pointer || GUIUtility.hotControl != 0 || visible.Contains(current.mousePosition)) return null;
            current.type = EventType.Ignore;
            return type;
        }

        static void RestoreEvent(EventType? hidden)
        {
            if (hidden != null && Event.current.type == EventType.Ignore) Event.current.type = hidden.Value;
        }

        void DrawPanels()
        {
            var height = ShownPanelHeight(panelHeight ??= EditorPrefs.GetFloat(PanelHeightPreference, PanelHeight), pageView.height);
            var area = GUILayoutUtility.GetRect(100, height, GUILayout.ExpandWidth(true));
            var rightWidth = Mathf.Round(area.width * .25f);
            var left = new Rect(area.x, area.y, area.width - rightWidth - 8, area.height);
            var right = new Rect(left.xMax + 8, area.y, rightWidth, area.height);
            DrawOtherPanel(left);
            DrawExcludedPanel(right);
            if (Event.current.type == EventType.Repaint) { expressionDropRect = ScreenRect(left); excludedDropRect = ScreenRect(right); }
            DrawPanelHandle(GUILayoutUtility.GetRect(100, PanelHandle, GUILayout.ExpandWidth(true)), height);
        }

        // The panels never grow past the page view, so their bottom edge stays
        // reachable in a small window; the saved height is kept for a larger one.
        // pageHeight: 0 before the first repaint.
        internal static float ShownPanelHeight(float saved, float pageHeight)
        {
            var max = pageHeight > 0 ? Mathf.Clamp(pageHeight - PanelHandle - 30, MinPanelHeight, MaxPanelHeight) : MaxPanelHeight;
            return Mathf.Clamp(saved, MinPanelHeight, max);
        }

        // The gap under the panels: dragging it sets their height.
        void DrawPanelHandle(Rect handle, float shown)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive);
            var current = Event.current;
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeVertical);
            // A short line in the middle, so the gap does not read as the edge of the footer.
            if (current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(Mathf.Round(handle.center.x - 16), Mathf.Round(handle.center.y - 1), 32, 2),
                    GUIUtility.hotControl == id ? AccentColor : CardBorderColor);
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && handle.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    panelDragStart = current.mousePosition.y;
                    panelDragHeight = shown;
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    // The same limit as the drawn height, so the saved height is the one on screen.
                    panelHeight = ShownPanelHeight(panelDragHeight + current.mousePosition.y - panelDragStart, pageView.height);
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    EditorPrefs.SetFloat(PanelHeightPreference, panelHeight ?? shown);
                    current.Use();
                    break;
            }
        }

        internal const float DragScrollBand = 24, DragScrollStep = 8;

        // How far a card held at pointer scrolls the page on one drag event.
        // The page scrolls only inside the band at the top or bottom edge and
        // stops at either end. Over a drop target it scrolls only until that
        // target is whole in the view: the target moves toward the middle and
        // stays under the pointer, instead of running away from it.
        // target: the slot or panel under the pointer, null when none.
        internal static float DragScroll(Rect view, Vector2 pointer, Rect? target, float scroll, float maxScroll)
        {
            if (view.height <= 0 || !view.Contains(pointer)) return 0;
            var step = pointer.y < view.y + DragScrollBand ? -DragScrollStep : pointer.y > view.yMax - DragScrollBand ? DragScrollStep : 0;
            if (step == 0) return 0;
            if (target is Rect rect && (step < 0 ? rect.yMin >= view.yMin : rect.yMax <= view.yMax)) return 0;
            return Mathf.Clamp(scroll + step, 0, Mathf.Max(0, maxScroll)) - scroll;
        }

        // The slot or panel under a window position, from the last repaint.
        Rect? DropTargetAt(Vector2 point)
        {
            foreach (var rect in slotRects.Values) if (rect.Contains(point)) return rect;
            if (expressionDropRect.Contains(point)) return expressionDropRect;
            if (excludedDropRect.Contains(point)) return excludedDropRect;
            return null;
        }

        Rect DrawPanelFrame(Rect rect, Place target, string title, string caption)
        {
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, PanelColor);
                Outline(rect, Nullable.Equals(dropHover, target) ? AccentColor : LineColor);
                EditorGUI.DrawRect(new Rect(rect.x, rect.y + PanelHeader, rect.width, 1), LineColor);
            }
            var header = new Rect(rect.x + 8, rect.y, rect.width - 16, PanelHeader);
            var titleWidth = Mathf.Min(header.width, cardText.CalcSize(new GUIContent(title)).x);
            GUI.Label(new Rect(header.x, header.y, titleWidth, header.height), Fit(title, cardText, titleWidth), cardText);
            if (caption != null)
            {
                var captionRect = new Rect(header.x + titleWidth + 8, header.y + 1, Mathf.Max(0, header.width - titleWidth - 8), header.height);
                GUI.Label(captionRect, new GUIContent(Fit(caption, dimMiniRight, captionRect.width), caption), dimMiniRight);
            }
            return new Rect(rect.x + 1, rect.y + PanelHeader + 1, rect.width - 2, rect.height - PanelHeader - 2);
        }

        // Panel cards follow the slots' thumbnail size.
        static Rect PanelTile(int index, int columns, float size) => new Rect(Gap + index % columns * (size + 14 + Gap),
            Gap + index / columns * (size + 8 + ExpressionSlotLayout.Name + Gap), size + 14, size + 8 + ExpressionSlotLayout.Name);

        static int PanelColumns(Rect body, float size) =>
            Mathf.Max(1, Mathf.FloorToInt((body.width - ScrollbarWidth - Gap * 2 + Gap) / (size + 14 + Gap)));

        static float PanelRows(int count, int columns, float size) => Gap + (count + columns - 1) / columns * (size + 8 + ExpressionSlotLayout.Name + Gap);

        void DrawPanelTile(Rect tile, float size, ExpressionBoard.Item item, string closeTooltip, Action close)
        {
            var clip = item.Clip;
            DrawTile(tile, new Rect(tile.x + 7, tile.y + 4, size, size), clip.name, clip, Thumbnail(clip, null, null, size), false,
                null, ItemTooltip(item), closeTooltip, close);
        }

        // count: the faces that are exported. Unsupported ones are not shown.
        internal static string OtherTitle(int count) => string.Format(ExporterLocalization.T("その他の表情 {0}件"), count);

        void DrawOtherPanel(Rect rect)
        {
            var items = board.ItemsIn(PlaceKind.Other).ToArray();
            var body = DrawPanelFrame(rect, Place.Other, OtherTitle(board.OtherCount),
                board.UsesRecordRoute ? ExporterLocalization.T("この一覧を記録して書き出し") : ExporterLocalization.T("アバターの表情メニューをそのまま書き出し"));
            var size = ExpressionSlotLayout.Thumb(LargeThumbnails);
            var columns = PanelColumns(body, size);
            var view = new Rect(0, 0, body.width - ScrollbarWidth, Mathf.Max(body.height, PanelRows(items.Length, columns, size)));
            var hidden = HideOutside(body);
            otherScroll = GUI.BeginScrollView(body, otherScroll, view, false, true);
            for (var index = 0; index < items.Length; index++)
            {
                var clip = items[index].Clip;
                DrawPanelTile(PanelTile(index, columns, size), size, items[index], "書き出さない表情へ移す", () => MoveTo(clip, Place.Excluded));
            }
            if (items.Length == 0)
                GUI.Label(new Rect(Gap, Gap, view.width - Gap * 2, body.height - Gap * 2),
                    Fit(ExporterLocalization.T("Projectから表情の.animをドロップ（複数可）"), centeredDim, view.width - Gap * 2), centeredDim);
            GUI.EndScrollView();
            RestoreEvent(hidden);
            HandleDrop(rect, Place.Other, project: true);
        }

        void DrawExcludedPanel(Rect rect)
        {
            var body = DrawPanelFrame(rect, Place.Excluded, string.Format(ExporterLocalization.T("書き出さない表情 {0}件"), board.ExcludedCount), null);
            var items = board.ItemsIn(PlaceKind.Excluded).ToArray();
            var size = ExpressionSlotLayout.Thumb(LargeThumbnails);
            var columns = PanelColumns(body, size);
            var listed = PanelRows(items.Length, columns, size);
            var zoneHeight = Mathf.Max(40, body.height - listed - Gap);
            var view = new Rect(0, 0, body.width - ScrollbarWidth, listed + zoneHeight + Gap);
            var hidden = HideOutside(body);
            excludedScroll = GUI.BeginScrollView(body, excludedScroll, view, false, true);
            for (var index = 0; index < items.Length; index++) DrawPanelTile(PanelTile(index, columns, size), size, items[index], null, null);
            var zone = new Rect(Gap, listed, view.width - Gap * 2, zoneHeight);
            if (Event.current.type == EventType.Repaint) Dashed(zone, CardBorderColor);
            GUI.Label(zone, Fit(ExporterLocalization.T("ここへドロップ"), centeredDim, zone.width), centeredDim);
            GUI.EndScrollView();
            RestoreEvent(hidden);
            HandleDrop(rect, Place.Excluded);
        }

        static string ItemTooltip(ExpressionBoard.Item item) =>
            (item.Added ? item.Source : ExporterLocalization.T(item.Source ?? "")) + "\n" + AssetDatabase.GetAssetPath(item.Clip);

        // One tile: the thumbnail with the name under it. A tile with a clip can
        // be clicked to preview, right-clicked for the move menu and dragged
        // onto another place. Its × shows while the mouse is over it.
        void DrawTile(Rect tile, Rect thumb, string name, AnimationClip clip, Texture2D face, bool automatic, string badge, string tooltip,
            string closeTooltip, Action close)
        {
            var current = Event.current;
            if (current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(tile, CardColor);
                if (automatic) Dashed(tile, DashColor); else Outline(tile, CardBorderColor);
                if (face != null) GUI.DrawTexture(thumb, face, ScaleMode.ScaleToFit, false);
                else EditorGUI.DrawRect(thumb, ThumbEmpty);
            }
            GUI.Label(tile, new GUIContent("", tooltip), GUIStyle.none);
            if (badge != null)
            {
                var mark = new Rect(thumb.x + 2, thumb.y + 2, 30, 14);
                if (current.type == EventType.Repaint) EditorGUI.DrawRect(mark, BadgeColor);
                GUI.Label(mark, new GUIContent(Fit(ExporterLocalization.T(badge), badgeText, mark.width - 2), tooltip), badgeText);
            }
            var nameRect = new Rect(tile.x + 2, thumb.yMax, tile.width - 4, ExpressionSlotLayout.Name);
            var style = nameCenter;
            GUI.Label(nameRect, new GUIContent(Fit(name, style, nameRect.width), tooltip), style);
            if (close != null)
            {
                if (current.type == EventType.Repaint) closableTiles.Add(ScreenRect(tile));
                // Drawn only under the mouse; every other event keeps the button
                // so the controls after it keep their ids.
                if (current.type != EventType.Repaint || tile.Contains(current.mousePosition))
                    if (GUI.Button(new Rect(tile.xMax - 18, tile.y + 2, 16, 16), new GUIContent("×", ExporterLocalization.T(closeTooltip)), closeText)) Queue(close);
            }
            if (clip == null) return;
            if (current.type == EventType.Repaint) candidateRects[clip] = ScreenRect(tile);
            var id = GUIUtility.GetControlID(FocusType.Passive);
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!tile.Contains(current.mousePosition)) break;
                    if (current.button == 0) { GUIUtility.hotControl = id; pressStart = current.mousePosition; current.Use(); }
                    else if (current.button == 1) { ShowMoveMenu(clip); current.Use(); }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    if ((current.mousePosition - pressStart).sqrMagnitude > 16)
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
                    if (tile.Contains(current.mousePosition)) PreviewBoardClip(clip);
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
            foreach (var group in ExperimentalExpressionCaptureSession.PresetGroups)
            {
                foreach (var preset in group.Presets) Add(ExperimentalExpressionCaptureSession.PresetFullLabel(preset), Place.Slot(preset));
                menu.AddSeparator("");
            }
            Add(ExporterLocalization.T("その他の表情"), Place.Other);
            Add(ExporterLocalization.T("書き出さない表情"), Place.Excluded);
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

        void ConfirmAutomatic(UniVRM10.ExpressionPreset[] presets)
        {
            RebuildBoard();
            board.ConfirmAutomatic(presets);
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
            // What stops the save takes the place of the summary.
            var reason = BlockReason();
            var summary = reason ?? string.Format(ExporterLocalization.T("VRMの表情 {0}/{1}・その他の表情 {2}・書き出さない表情 {3}"),
                board.SlotCount(MainPresets), MainPresets.Length, board.OtherCount, board.ExcludedCount);
            var summaryStyle = reason != null ? errorText : dimText;
            GUI.Label(summaryRect, new GUIContent(Fit(summary, summaryStyle, summaryRect.width), summary), summaryStyle);
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
            if (cardText != null && accentNormal != null && stylesForProSkin == EditorGUIUtility.isProSkin) return;
            stylesForProSkin = EditorGUIUtility.isProSkin;
            GUIStyle Label(GUIStyle basis, Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
            {
                var style = new GUIStyle(basis) { alignment = anchor, clipping = TextClipping.Clip, wordWrap = false, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0) };
                style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
                return style;
            }
            cardText = Label(EditorStyles.label, TextColor);
            cardTextDisabled = Label(EditorStyles.label, Hex(0x6e6e6e));
            dimText = Label(EditorStyles.label, DimColor);
            errorText = Label(EditorStyles.label, ErrorColor);
            boldText = Label(EditorStyles.boldLabel, TextColor);
            dimMini = Label(EditorStyles.miniLabel, DimColor);
            dimMiniRight = Label(EditorStyles.miniLabel, DimColor, TextAnchor.MiddleRight);
            errorMini = Label(EditorStyles.miniLabel, ErrorColor);
            nameCenter = Label(EditorStyles.label, TextColor, TextAnchor.MiddleCenter);
            // The badge sits on the thumbnail, so it is light on both skins.
            badgeText = Label(EditorStyles.miniLabel, Hex(0xd2d2d2), TextAnchor.MiddleCenter);
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
                var blinkOn = EditorGUILayout.Toggle(ExporterLocalization.T(configuredBlink?.Mode == BlinkExportMode.Manual ? "瞬き" : "瞬きを自動設定する"), automaticBlink);
                // Blink candidates follow the blink setting the export receives.
                if (blinkOn != automaticBlink) { automaticBlink = blinkOn; Queue(ApplyAutomaticPresets); }
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
            RecordClips(board.UsesRecordRoute ? board.ExportedCustom() : new System.Collections.Generic.List<AnimationClip>());
        }

        void RecordClips(System.Collections.Generic.List<AnimationClip> exported)
        {
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
                EditorGUILayout.LabelField(ExporterLocalization.T(saved.MissingShaders > 0 ? ExportResultWindow.SavedWithWarningsTitle : ExportResultWindow.SavedTitle), EditorStyles.boldLabel);
                if (saved.MissingShaders > 0)
                    EditorGUILayout.HelpBox(string.Format(ExporterLocalization.T(LilToonExporterWindow.SavedWithMissingShadersFormat), saved.MissingShaders), MessageType.Warning);
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
            var additions = clips.Where(clip => clip != null && !clipInputs.Any(input => input.Clip == clip)).Distinct().Select(Checked).ToArray();
            // Only the files that are added count toward the limit.
            var usable = additions.Where(input => input.Error == null).ToArray();
            if (clipInputs.Count + usable.Length > ExperimentalExpressionCaptureSession.MaximumExpressions)
                throw new InvalidOperationException(ExporterLocalization.T("表情は64件までです。選択を減らして追加してください。"));
            foreach (var input in usable) Keep(input);
            if (usable.Length > 0) status = string.Format(ExporterLocalization.T("{0}件の表情を追加しました"), usable.Length);
            ApplyAutomaticPresets();
            var rejected = additions.Where(input => input.Error != null).Select(input => (input.Name, input.Error)).ToArray();
            // The error replaces the status line, so it also says what was added.
            if (rejected.Length > 0) throw new InvalidOperationException(usable.Length > 0 ? status + "\n" + RejectedMessage(rejected) : RejectedMessage(rejected));
        }

        void AddClip(AnimationClip clip)
        {
            if (clip == null || clipInputs.Any(input => input.Clip == clip)) return;
            var input = Checked(clip);
            if (input.Error != null) throw new InvalidOperationException(RejectedMessage(new[] { (input.Name, input.Error) }));
            if (clipInputs.Count >= ExperimentalExpressionCaptureSession.MaximumExpressions)
                throw new InvalidOperationException(ExporterLocalization.T("表情は64件までです。"));
            Keep(input);
            status = ExporterLocalization.T("表情を追加しました");
            ApplyAutomaticPresets();
        }

        // Error is the reason the export cannot use the file; such a file is not added.
        ExperimentalExpressionCaptureSession.ClipInput Checked(AnimationClip clip)
        {
            var input = new ExperimentalExpressionCaptureSession.ClipInput { Clip = clip, Name = clip.name, Time = DefaultTime(clip) };
            input.Error = session.ClipError(clip, input.Time);
            return input;
        }

        void Keep(ExperimentalExpressionCaptureSession.ClipInput input)
        {
            clipInputs.Add(input);
            session.PreviewClip(input.Clip, input.Time); ShowFace(input.Name);
        }

        // One line per file that was not added: its name, then the reason.
        internal static string RejectedMessage(System.Collections.Generic.IEnumerable<(string Name, string Reason)> rejected) =>
            string.Join("\n", rejected.Select(item => string.Format(ExporterLocalization.T("「{0}」は追加できません。{1}"), item.Name, item.Reason)));

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
            using var steps = ExpressionBoardTimings.Sequence();
            steps.Next("shapeOptions");
            session.PresetShapeOptions(); // Build the BlendShape list once, outside OnGUI.
            steps.Next("stored");
            session.Presets.Clear();
            foreach (var assignment in ExpressionPresetStore.Load(presetKey, out presetSkipped))
                session.SetPreset(ExperimentalExpressionCaptureSession.ParsePreset(assignment.Preset), assignment);
            board.Excluded.Clear();
            board.Excluded.UnionWith(ExpressionPresetStore.LoadExcluded(presetKey, out var skippedExclusions));
            presetSkipped += skippedExclusions;
            steps.Next("automatic");
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
            blinkFallbackStale = true;
        }

        void RefreshPresetErrors()
        {
            presetErrors.Clear(); eyeOverlaps.Clear();
            if (session == null) return;
            foreach (var pair in session.Presets) CheckPreset(pair.Key, pair.Value);
        }

        void CheckPreset(UniVRM10.ExpressionPreset preset, ExperimentalExpressionCaptureSession.PresetAssignment assignment)
        {
            presetErrors[preset] = session.PresetError(preset, assignment);
            if (presetErrors[preset] != null && session.BlinkSidesOverlap(preset, assignment)) eyeOverlaps.Add(preset);
            else eyeOverlaps.Remove(preset);
        }

        void ChoosePreset(UniVRM10.ExpressionPreset preset, ExperimentalExpressionCaptureSession.PresetAssignment assignment)
        {
            session.SetPreset(preset, assignment);
            SaveChoices();
            CheckPreset(preset, assignment);
            // The left and right eye slots check each other for a shared closing shape.
            var otherEye = preset == UniVRM10.ExpressionPreset.blinkLeft ? UniVRM10.ExpressionPreset.blinkRight
                : preset == UniVRM10.ExpressionPreset.blinkRight ? UniVRM10.ExpressionPreset.blinkLeft : (UniVRM10.ExpressionPreset?)null;
            if (otherEye is UniVRM10.ExpressionPreset eye && session.Presets.TryGetValue(eye, out var eyeValue)) CheckPreset(eye, eyeValue);
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
                throw new InvalidOperationException(string.Format(ExporterLocalization.T(OverLimitFormat), board.OverLimitBy));
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
            if (BlocksExport) return ExporterLocalization.T(AppTransferExport.BoardNeedsFix);
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
            var blink = ExportBlinkOptions();
            var expressions = target.Expressions.Select(ExperimentalExpressionCaptureSession.ClonePose).ToArray();
            var poses = target.PoseOptions.Copy();
            var presets = target.Presets.Values.Select(value => value.Copy()).ToArray();
            var expressionCount = expressions.Length;
            // The record route replaces the menu faces even when nothing is left to record.
            var replaceCustomFaces = board.UsesRecordRoute;
            // The ordinary route evaluates the menu again and can lose faces the
            // board lists (a non-FX layer writing what a face reads). With fewer
            // menu faces than the list, the same save records the list instead.
            var listedFaces = replaceCustomFaces ? 0 : board.OtherCount;
            var recordedList = false;
            // The report of the attempt that produced the bytes being saved.
            ExportFallbackReport savedReport = null;
            void Completed(byte[] bytes, System.Collections.Generic.IEnumerable<string> warnings)
            {
                var missingShaders = ExportResultList.MissingShaders(savedReport);
                // A plain field: still set when the recovery window saves after this board closed.
                LastMissingShaderCount = missingShaders;
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
                saved = new SavedExpressionVrm(path, bytes, expressionCount, poseCount, missingShaders);
                status = (missingShaders > 0 ? string.Format(ExporterLocalization.T(LilToonExporterWindow.SavedWithMissingShadersFormat), missingShaders)
                    : ExporterLocalization.T("VRMを保存しました。")) + (warnings.Any() ? "\n" + string.Join("\n", warnings) : "");
                Repaint();
            }
            var recovery = LilToonExporterWindow.ExportAndSaveNormally(target.Source, path, (options, report, warnings) => {
                foreach (var warning in target.Warnings) warnings.Add(warning);
                var exported = target.Export(name, targetAuthor, warnings, blink?.Copy(), license.Copy(), options, report, expressions, poses,
                    presets.Select(value => value.Copy()).ToArray(), replaceCustomFaces);
                var menuFaces = !replaceCustomFaces && listedFaces > 0 ? VrmMenuExpressions.CountRegistered(exported) : listedFaces;
                if (menuFaces < listedFaces)
                {
                    var category = ExporterLocalization.T("その他の表情");
                    string kept = null;
                    // A recovery attempt after the board closed has no list to record.
                    if (this == null || !ReferenceEquals(session, target))
                        kept = string.Format(ExporterLocalization.T(MenuShortNotRecordedFormat), menuFaces, listedFaces);
                    else
                    {
                        // The board may have changed before a recovery attempt.
                        RebuildBoard();
                        listedFaces = board.OtherCount;
                        if (!board.CanRecordList) kept = string.Format(ExporterLocalization.T(MenuShortKeptFormat), menuFaces, listedFaces);
                        else
                        {
                            // Set first: a canceled attempt still leaves the list recorded.
                            recordedList = true;
                            try
                            {
                                exported = ExportRecorded(board.ExportedCustom(), true);
                                var recorded = string.Format(ExporterLocalization.T(MenuShortRecordedFormat), menuFaces, listedFaces);
                                warnings.Add(recorded);
                                report?.Fallbacks.Record(ExportOutcome.Approximated, category, reason: recorded, count: listedFaces);
                            }
                            catch (Exception exception) when (!(exception is OperationCanceledException))
                            {
                                // The menu result saved before this change is saved again.
                                exported = ExportRecorded(new System.Collections.Generic.List<AnimationClip>(), false);
                                recordedList = false;
                                warnings.Add(exception.Message);
                                kept = string.Format(ExporterLocalization.T(MenuShortNotRecordedFormat), menuFaces, listedFaces);
                            }
                        }
                    }
                    if (kept != null)
                    {
                        warnings.Add(kept);
                        report?.Fallbacks.Record(ExportOutcome.Omitted, category, reason: kept, count: Math.Max(0, listedFaces - menuFaces));
                    }
                }
                savedReport = report?.Fallbacks;
                return exported;

                // Records the clips on the board and exports this save again;
                // later recovery attempts of the save take the same route.
                byte[] ExportRecorded(System.Collections.Generic.List<AnimationClip> clips, bool replace)
                {
                    var shown = status;
                    RecordClips(clips);
                    status = shown;
                    expressions = target.Expressions.Select(ExperimentalExpressionCaptureSession.ClonePose).ToArray();
                    expressionCount = expressions.Length;
                    replaceCustomFaces = replace;
                    // The earlier result is replaced, its warnings with it.
                    warnings.Clear();
                    foreach (var warning in target.Warnings) warnings.Add(warning);
                    return target.Export(name, targetAuthor, warnings, blink?.Copy(), license.Copy(), options, report, expressions, poses,
                        presets.Select(value => value.Copy()).ToArray(), replaceCustomFaces);
                }
            }, Completed, previewExcludedObjects: target.ExcludedObjects, previewGimmicks: target.GimmickOptions, interactive: interactive,
                openBlinkSettings: () => { if (this != null && ReferenceEquals(session, target)) ShowBlinkConfiguration(); });
            // The bytes are made by now; the board keeps its own records again,
            // whether the save finished, waits on the result list or stopped.
            if (recovery == null && recordedList && this != null && ReferenceEquals(session, target)) RecordBoardClips();
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
                Queue(ApplyAutomaticPresets);
            });
        }

        // The blink setting the export receives (null is automatic).
        BlinkExportOptions ExportBlinkOptions() => automaticBlink ? configuredBlink?.Copy() : new BlinkExportOptions { Mode = BlinkExportMode.None };

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
            presetErrors.Clear(); eyeOverlaps.Clear(); presetKey = null; presetSkipped = 0;
            excludedObjectEdits = null; excludedObjectError = null; showExcludedObjects = null;
            dropHover = dropHoverNext = null; dragMouse = null;
            ClearThumbnails(); slotRects.Clear(); closableTiles.Clear(); hoverTile = -1;
            blinkFallback = null; blinkFallbackStale = true;
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
