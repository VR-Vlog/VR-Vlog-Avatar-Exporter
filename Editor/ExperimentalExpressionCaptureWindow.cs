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
        // Shown open: a license left at its defaults is easy to miss when folded.
        bool showLicenseSettings = true;
        readonly System.Collections.Generic.Dictionary<AnimationClip, string> clipErrors = new System.Collections.Generic.Dictionary<AnimationClip, string>();
        // Window-relative card rectangles, kept for tests that click a card.
        readonly System.Collections.Generic.Dictionary<AnimationClip, Rect> candidateRects = new System.Collections.Generic.Dictionary<AnimationClip, Rect>();
        // The card whose rename box opens on the next repaint, and where.
        (AnimationClip Clip, Rect At)? renameRequest;
        // The board face the preview shows, so a rename updates its title.
        AnimationClip previewClip;
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
        // The page shown: expressions, objects left out, then the VRM information
        // (in the transfer the third step is the next window). Moving off the
        // objects page applies its edits to the preview first.
        internal const int ExpressionsPage = 0, ObjectsPage = 1, InfoPage = 2;
        int page;
        bool applyingOnLeave;
        internal int Page => page;
        // The objects left out of the VRM, as edited here; null while they match the preview.
        System.Collections.Generic.List<GameObject> excludedObjectEdits;
        string excludedObjectError;
        // The VRChat gimmicks of the avatar, analyzed again only when the avatar,
        // the preview or the scene hierarchy changes. Choices save at once to the
        // avatar's settings and reach the preview with the object list.
        System.Collections.Generic.List<ExportGimmickFinding> gimmickFindings;
        System.Collections.Generic.List<(string Name, System.Collections.Generic.List<ExportGimmickFinding> Findings)> gimmickGroups =
            new System.Collections.Generic.List<(string Name, System.Collections.Generic.List<ExportGimmickFinding> Findings)>();
        System.Collections.Generic.HashSet<ExportGimmickFinding> gimmicksRemoved = new System.Collections.Generic.HashSet<ExportGimmickFinding>();
        // What each row holds (the dim column), worked out with the analysis.
        System.Collections.Generic.Dictionary<ExportGimmickFinding, string> gimmickContents = new System.Collections.Generic.Dictionary<ExportGimmickFinding, string>();
        ExperimentalExpressionCaptureSession gimmickSession;
        bool gimmicksStale = true;
        double nextGimmickScan;
        ExportSettings savedSettings;
        GameObject savedSettingsSource;
        int savedSettingsRevision = -1;

        // Board drag state. Cards carry the clip under a private key, so a
        // drag from the Project window is never mistaken for a card.
        const string DragKey = "VRVlog.LilToonExporter.ExpressionBoardCard";
        Vector2 pressStart;
        Place? dropHover, dropHoverNext;
        // The .anim drop area under a valid drag, keyed by its label.
        string clipDropHover, clipDropHoverNext;
        Vector2? dragMouse;
        // Window-relative slot rectangles, kept for tests and screenshots.
        readonly System.Collections.Generic.Dictionary<Place, Rect> slotRects = new System.Collections.Generic.Dictionary<Place, Rect>();
        // The step row as last drawn, kept for tests.
        StepIndicator.Layout stepLayout;
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
        GUIStyle cardText, cardTextDisabled, dimText, errorText, dimMini, dimMiniRight, dimMiniBold, errorMini, boldText, badgeText, closeText, accentButton, centeredDim, dropBox,
            nameCenter, stepMarkerText, stepMarkerDim, stepErrorBold;
        bool stylesForProSkin;
        Texture2D accentNormal, accentHover, accentActive, discTexture, ringTexture;
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
            // "戻る" keeps the transfer on the third page, and the board as it was.
            if (!window.LeaveTransferMode(window.TransferPanelTransferring)) window.Focus();
        }

        // "VRM書き出し" on this board: false when the user kept the transfer on its third page.
        internal bool LeaveTransferMode(bool transferring)
        {
            if (transferMode && !ConfirmLeavingTransfer(transferring)) return false;
            transferMode = false;
            titleContent = new GUIContent(ExporterLocalization.T("VRM書き出し"));
            minSize = new Vector2(600, 760);
            return true;
        }

        // "VRMを書き出す..." from an avatar's context menu: the window with that avatar
        // chosen, not yet prepared. A board prepared for another avatar asks first, as its
        // "変更" button does; keeping it leaves the board as it was.
        public static void OpenFor(GameObject avatar)
        {
            var window = GetWindow<ExperimentalExpressionCaptureWindow>();
            if (!window.ChooseForExport(avatar, window.TransferPanelTransferring)) window.Focus();
        }

        // Both questions come before anything changes. False when the user kept the transfer
        // on the third page or the prepared avatar: the board stays as it was.
        internal bool ChooseForExport(GameObject avatar, bool transferring)
        {
            var change = avatar != null && source != avatar && session != null;
            if (transferMode && !ConfirmLeavingTransfer(transferring)) return false;
            if (change && !ConfirmChangeAvatar()) return false;
            LeaveTransferMode(false);
            if (avatar == null || source == avatar) return true;
            if (change) Queue(() => { ChangeAvatar(); SelectSource(avatar); });
            else { SelectSource(avatar); Repaint(); }
            return true;
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
            if (!same && window.page == InfoPage && !window.ConfirmLeavingTransfer()) return false;
            if (!same && window.session != null && !window.ConfirmChangeAvatar()) return false;
            window.transferMode = true;
            window.titleContent = new GUIContent(ExporterLocalization.T("アプリに転送（通常）"));
            window.minSize = new Vector2(600, 760);
            // The third page is the transfer for the board's avatar; another avatar starts again from the objects.
            if (window.page > ObjectsPage && !same) window.SetPage(ObjectsPage);
            if (same || avatar == null) { window.Repaint(); return true; }
            window.Queue(() =>
            {
                window.PrepareForTransfer(avatar);
                if (!EditorApplication.isPlayingOrWillChangePlaymode) window.Prepare();
            });
            return true;
        }

        // The quick "アプリに転送": a board prepared for the avatar as opening
        // "通常" would, never shown. Its export is what "通常" exports when
        // nothing on the board is touched. The caller destroys it; a failed
        // or canceled preparation destroys it here and throws.
        internal static ExperimentalExpressionCaptureWindow PrepareHidden(GameObject avatar)
        {
            var window = CreateInstance<ExperimentalExpressionCaptureWindow>();
            try
            {
                window.PrepareForTransfer(avatar);
                window.Prepare();
                return window;
            }
            catch { DestroyImmediate(window); throw; }
        }

        // Everything both transfers set on a board before Prepare, in one place
        // so "かんたん" starts from the same defaults as "通常". A new setting
        // gets its default in ChangeAvatar or Prepare, never here per transfer.
        void PrepareForTransfer(GameObject avatar)
        {
            transferMode = true;
            ChangeAvatar();
            SelectSource(avatar);
        }

        // The open board prepared for this avatar, or null.
        internal static ExperimentalExpressionCaptureWindow ForTransfer(GameObject avatar) => avatar == null ? null :
            Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().FirstOrDefault(window => window != null && window.source == avatar && window.session != null);

        bool ConfirmChangeAvatar() => clipInputs.Count == 0 && (session == null || session.PoseOptions.Manual.Count == 0) ||
            EditorUtility.DisplayDialog(ExporterLocalization.T("アバターを変更"), ExporterLocalization.T("追加した表情・ポーズの一覧をリセットします。保存済みVRMは残ります。"), ExporterLocalization.T("変更する"), ExporterLocalization.T("戻る"));

        // Leaving the third page cancels its transfer. The board's own steps and closing do so
        // without asking, as the page says; a menu entry that takes the board away asks first.
        // The dialog: true cancels the transfer. Tests replace it.
        internal static Func<string, string, bool> ConfirmCancelTransfer = (title, body) =>
            EditorUtility.DisplayDialog(title, body, ExporterLocalization.T("中止"), ExporterLocalization.T("戻る"));

        bool ConfirmLeavingTransfer() => ConfirmLeavingTransfer(TransferPanelTransferring);

        // True when nothing is uploading or showing its QR, or the user chose "中止".
        internal static bool ConfirmLeavingTransfer(bool transferring) => !transferring ||
            ConfirmCancelTransfer(ExporterLocalization.T("転送を中止"), ExporterLocalization.T("進行中の転送を中止します。表示中のQRは使えなくなります。"));

        void OnEnable()
        {
            wantsMouseMove = true; EditorApplication.update += Advance; EditorApplication.projectChanged += CandidateAssetsChanged;
            EditorApplication.hierarchyChanged += GimmicksChanged;
        }

        void OnDisable()
        {
            EditorApplication.update -= Advance; EditorApplication.projectChanged -= CandidateAssetsChanged;
            EditorApplication.hierarchyChanged -= GimmicksChanged;
            // Closing the board cancels a transfer on its third page, as closing the transfer window does.
            transferPanel?.RemoveFromHierarchy(); transferPanel = null; transferPanelTried = false;
            pending = null; Cleanup(); ReleaseStyles();
        }

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

        // A queued action replaces any apply-on-leave still waiting, so its flag goes too.
        void Queue(Action action) { pending = action; applyingOnLeave = false; error = null; Repaint(); }

        internal static void AnimationAssetsReimported()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>()) window.CandidateAssetsChanged();
        }

        // A reimported file can draw a different face, so its thumbnail goes too.
        void CandidateAssetsChanged() { candidateValidationPending = true; ClearThumbnails(); Repaint(); }

        // The next layout analyzes the gimmicks again.
        void GimmicksChanged() { gimmicksStale = true; Repaint(); }

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
                if (problem != null) { excludedObjectEdits = current.ToList(); excludedObjectError = problem; }
            }
            clipErrors.Clear();
            foreach (var pair in session.ClipErrors((recommendations ?? new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>())
                .Select(item => item.Clip).Concat(clipInputs.Select(input => input.Clip)))) clipErrors[pair.Key] = pair.Value;
            foreach (var input in clipInputs)
            {
                input.Time = DefaultTime(input.Clip);
                // Only a deleted .anim leaves a null clip here; added files are never null.
                input.Error = input.Clip == null ? ExporterLocalization.T(DeletedClipReason) : clipErrors[input.Clip];
            }
            DropBrokenInputs();
            // An automatic value that stopped working is replaced, not left blocking the save.
            ApplyAutomaticPresets();
            Repaint();
        }

        // An added file that can no longer be exported (edited, or the avatar
        // changed) is hidden from the board, so it is removed like a file that
        // could not be added: it stops counting toward the limit and the
        // recorded route, a slot the user gave it is emptied (a gaze or neutral
        // slot is left open to be filled from names), and the reason
        // is shown. Automatic values are suggested again from the usable faces.
        // A file that is also one of the avatar's faces keeps its slot, as the
        // avatar's other unusable faces do. A deleted file is removed too; its
        // slot is kept, since the slot already shows the file as missing and
        // the save stops with that reason.
        void DropBrokenInputs()
        {
            var broken = clipInputs.Where(input => input.Error != null).ToArray();
            if (broken.Length == 0) return;
            var found = new System.Collections.Generic.HashSet<AnimationClip>((recommendations ?? new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>())
                .Select(item => item.Clip));
            var cleared = false;
            foreach (var input in broken)
            {
                clipInputs.Remove(input);
                if (board == null || input.Clip == null || found.Contains(input.Clip)) continue;
                foreach (var preset in ExperimentalExpressionCaptureSession.AssignablePresets)
                    if (board.SlotValue(preset) is var value && value != null && !value.Automatic && !value.IsNone && !value.IsShape && value.ResolveClip() == input.Clip)
                    { board.ClearSlot(preset); cleared = true; }
            }
            if (cleared) SaveChoices();
            status = RejectedMessage(broken.Select(input => (input.Name, input.Error)));
        }

        // A new preview of the same avatar that keeps the added files, the board,
        // the poses, the blink choice and the object list still being edited. With
        // objects that leave no preview, the previous objects are left out again and
        // the reason is returned; null when the preview has the objects asked for.
        string RebuildKeepingAssignments(GameObject[] objects, GameObject[] previous = null)
        {
            var inputs = clipInputs.ToArray(); var excluded = board?.Excluded.ToArray() ?? Array.Empty<AnimationClip>();
            var poses = session.PoseOptions.Copy(); var blink = configuredBlink?.Copy(); var automatic = automaticBlink; var previousSaved = saved;
            var presets = session.Presets.Values.Select(value => value.Copy()).ToArray();
            var edits = excludedObjectEdits; var editError = excludedObjectError;
            string problem = null;
            try { PrepareWith(objects); }
            catch (Exception exception) when (previous != null && !(exception is OperationCanceledException))
            {
                problem = exception.Message;
                PrepareWith(previous);
            }
            excludedObjectEdits = edits; excludedObjectError = editError;
            clipInputs.AddRange(inputs);
            board.Excluded.Clear(); board.Excluded.UnionWith(excluded);
            session.Presets.Clear();
            foreach (var preset in presets) session.SetPreset(ExperimentalExpressionCaptureSession.ParsePreset(preset.Preset), preset);
            // The poses kept replace the saved ones the preparation read again.
            session.PoseOptions.Manual.Clear(); session.PoseOptions.Excluded.Clear(); session.PoseOptions.Names.Clear();
            session.PoseOptions.Manual.AddRange(poses.Manual); session.PoseOptions.Excluded.UnionWith(poses.Excluded);
            foreach (var pair in poses.Names) session.PoseOptions.Names.Add(pair.Key, pair.Value);
            configuredBlink = blink; automaticBlink = automatic; saved = previousSaved;
            return problem;
        }

        void OnGUI()
        {
            EnsureStyles();
            var current = Event.current;
            var dragUpdate = current.type == EventType.DragUpdated;
            var dragMousePosition = current.mousePosition;
            if (dragUpdate) { dropHoverNext = null; clipDropHoverNext = null; }
            if (current.type == EventType.DragExited || current.type == EventType.DragPerform || current.type == EventType.MouseUp)
            { dropHover = null; clipDropHover = null; dragMouse = null; }
            if (current.type == EventType.MouseMove)
            {
                var hovered = closableTiles.FindIndex(tile => tile.Contains(current.mousePosition));
                if (hovered != hoverTile) { hoverTile = hovered; Repaint(); }
            }
            var repaint = current.type == EventType.Repaint;
            // Off the transfer's third page its panel leaves the window, which cancels a transfer in progress.
            var transferPage = transferMode && page == InfoPage && session != null && preview != null;
            if (!transferPage) transferPanel?.RemoveFromHierarchy();
            // Slots the fold hides drop out of slotRects until they are drawn again.
            if (repaint) { closableTiles.Clear(); thumbnailsShown.Clear(); slotRects.Clear(); boardDrawn = false; }
            ExporterWindowHeader.Layout(ExporterLocalization.T(transferMode ? "アプリに転送（通常）" : "VRMを書き出す"));
            using (new EditorGUILayout.VerticalScope(new GUIStyle { padding = new RectOffset(16, 16, 12, 12) }))
            {
                DrawSteps();
                EditorGUILayout.Space(12);
                var blocked = pending != null || applyingOnLeave || EditorApplication.isPlayingOrWillChangePlaymode;
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorGUILayout.HelpBox(ExporterLocalization.T("Playモードを終了すると編集できます。"), MessageType.Info);
                using (new EditorGUI.DisabledScope(blocked))
                {
                    if (session == null || page == ExpressionsPage)
                    {
                        EditorGUILayout.BeginHorizontal();
                        using (new EditorGUI.DisabledScope(session != null))
                            SelectSource((GameObject)EditorGUILayout.ObjectField(ExporterLocalization.T("アバター"), source, typeof(GameObject), true));
                        if (session != null && GUILayout.Button(ExporterLocalization.T("変更"), GUILayout.Width(50)) && ConfirmChangeAvatar())
                            Queue(ChangeAvatar);
                        EditorGUILayout.EndHorizontal();
                    }
                    if (session == null)
                    {
                        EditorGUILayout.Space(8);
                        EditorGUILayout.LabelField(ExporterLocalization.T("Hierarchyからアバターの一番上のオブジェクトを指定します。"), EditorStyles.wordWrappedLabel);
                        using (new EditorGUI.DisabledScope(source == null))
                            if (GUILayout.Button(ExporterLocalization.T("書き出しを準備"), GUILayout.Height(34))) Queue(Prepare);
                    }
                    else if (preview != null)
                    {
                        if (page == ExpressionsPage)
                        {
                            EditorGUILayout.Space(8);
                            DrawPreview();
                        }
                        EditorGUILayout.Space(8);
                        if (current.type == EventType.Layout) RebuildBoard();
                        // A card dragged near the top or bottom edge scrolls the page.
                        if (current.type == EventType.DragUpdated && DragAndDrop.GetGenericData(DragKey) is AnimationClip)
                        {
                            var step = DragScroll(pageView, current.mousePosition, DropTargetAt(current.mousePosition), pageScroll.y,
                                Mathf.Max(0, pageContentHeight - pageView.height));
                            if (step != 0) { pageScroll.y += step; Repaint(); }
                        }
                        if (transferPage)
                        {
                            DrawFeedback();
                            DrawTransferPanel(blocked);
                        }
                        else
                        {
                            pageScroll = EditorGUILayout.BeginScrollView(pageScroll);
                            var pageHidden = pageView.height > 0 ? HideOutside(new Rect(pageScroll, pageView.size)) : null;
                            if (page == ExpressionsPage)
                            {
                                DrawSlots();
                                EditorGUILayout.Space(8);
                                DrawPanels();
                                DrawAddRow();
                                EditorGUILayout.Space(12);
                                DrawBlink();
                                EditorGUILayout.Space(12);
                                DrawPoseInputs();
                            }
                            else if (page == ObjectsPage)
                            {
                                DrawGimmicks();
                                EditorGUILayout.Space(16);
                                DrawExcludedObjects();
                            }
                            else DrawInfo();
                            EditorGUILayout.Space(8);
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
                if (clipDropHover != clipDropHoverNext) { clipDropHover = clipDropHoverNext; Repaint(); }
                var dragging = DragAndDrop.GetGenericData(DragKey) is AnimationClip;
                dragMouse = dragging ? dragMousePosition : (Vector2?)null;
                if (dragging) Repaint();
            }
            DrawDraggedName();
            if (repaint && boardDrawn) PruneThumbnails();
            // The rename box opens from OnGUI, outside any scroll view, so its
            // place (window space, like candidateRects) needs no conversion.
            // A double-click first queues a preview of the face; the box waits for it.
            if (repaint && renameRequest is (AnimationClip clip, Rect at))
            {
                if (pending != null) Repaint();
                else
                {
                    renameRequest = null;
                    if (board != null && board.CanRename(clip)) OpenRename(clip, at);
                }
            }
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
            page = ExpressionsPage;
            try
            {
                try { PrepareWith(stored); }
                catch (Exception exception) when (stored.Length > 0 && !(exception is OperationCanceledException))
                {
                    PrepareWith(Array.Empty<GameObject>());
                    excludedObjectEdits = stored.ToList(); excludedObjectError = exception.Message;
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
                // So does the blink, as the blink settings window would set it.
                // Automatic (also what an avatar without saved settings reads)
                // leaves the board's own blink toggle as it is.
                var storedBlink = stored.Blink ?? new BlinkExportOptions();
                if (storedBlink.Mode == BlinkExportMode.Manual) { configuredBlink = storedBlink.Copy(); automaticBlink = true; }
                else if (storedBlink.Mode == BlinkExportMode.None) automaticBlink = false;
                session = new ExperimentalExpressionCaptureSession(source, replayInstalledDefaults: false, objects, stored.Gimmicks());
                // And the poses, as the export window writes them.
                var storedPoses = stored.Poses ?? new PoseExportOptions();
                session.PoseOptions.Manual.AddRange(storedPoses.Manual.Select(pose => pose.Copy()));
                session.PoseOptions.Excluded.UnionWith(storedPoses.Excluded);
                foreach (var pair in storedPoses.Names) session.PoseOptions.Names[pair.Key] = pair.Value;
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
            // Same caption row as the VRM expression slots below.
            var header = GUILayoutUtility.GetRect(100, 22, GUILayout.ExpandWidth(true));
            GUI.Label(new Rect(header.x, header.y + 2, header.width, 18), ExporterLocalization.T("プレビュー"), dimMini);
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
            GUI.Label(rect, new GUIContent(string.Empty, ExporterLocalization.T("ドラッグで回転・ホイールで拡大")));
            EditorGUILayout.LabelField(previewName, EditorStyles.boldLabel);
        }

        // ---- Expression board ----

        void RebuildBoard()
        {
            if (board == null) return;
            board.SetItems(clipInputs.Select(input => new ExpressionBoard.Item { Clip = input.Clip, Error = input.Error, Source = ExporterLocalization.T("追加した表情") }),
                (recommendations ?? new System.Collections.Generic.List<ExperimentalExpressionCaptureSession.ClipRecommendation>()).Select(item => new ExpressionBoard.Item
                    { Clip = item.Clip, Error = clipErrors.TryGetValue(item.Clip, out var reason) ? reason : null, Source = item.Source }));
        }

        // The transfer asks the author in the next window. Edits not yet in the
        // preview do not stop the button: the save applies them first.
        bool CanSave => (transferMode || ExportAuthor.IsValid(author)) && BoardBlockReason(includePending: false) == null;

        // What the next step asks to fix before any export, to a file or to the app.
        // It is the board part of BlockReason, so the two never disagree.
        internal bool BlocksExport => BoardBlockReason() != null;

        // Why the save button is off, shown in the bottom bar; null when it can
        // save. The same messages stop the save when it is called anyway.
        internal string BlockReason() =>
            BoardBlockReason() ?? (!transferMode && !ExportAuthor.IsValid(author) ? ExporterLocalization.T(ExportAuthor.Required) : null);

        // The reasons on the board itself, in the order the bottom bar names
        // them; the author is asked apart because the transfer asks it later.
        // Without the pending edits it is what the pages check: leaving the
        // objects page and saving apply those edits on their own.
        string BoardBlockReason(bool includePending = true)
        {
            if (includePending && excludedObjectEdits != null) return ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません");
            if (includePending && GimmicksPending) return ExporterLocalization.T(GimmicksPendingReason);
            foreach (var preset in ExperimentalExpressionCaptureSession.AssignablePresets)
                if (presetErrors.TryGetValue(preset, out var reason) && reason != null) return reason;
            // A reason kept for a preset outside the board order still stops it.
            var other = presetErrors.Values.FirstOrDefault(value => value != null);
            if (other != null) return other;
            if (board == null) return ExporterLocalization.T(AppTransferExport.BoardClosed);
            return null;
        }

        internal const string MenuShortRecordedFormat = "表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件を記録して書き出しました";
        internal const string MenuShortNotRecordedFormat = "表情メニューから書き出せた表情は{0}件でした・その他の表情{1}件は記録できませんでした";
        // The ordinary route kept the menu faces, fewer than the list; some list
        // faces are not in the VRM. Which ones and how many is not known: menu faces
        // and list clips share no key, and a non-FX layer can break menu faces the list has.
        internal const string MenuShortKeptMenuFormat = "表情メニューから{0}件を書き出しました・その他の表情の一覧{1}件のうち書き出していない表情があります";
        // The ordinary route kept menu faces the list does not show; nothing was left out.
        internal const string MenuExceedsListFormat = "表情メニューから{0}件を書き出しました・その他の表情の一覧は{1}件です";

        // What a save does when the menu gives fewer faces than the list. The
        // saved bytes, the list count it was decided with and the lines of the result.
        internal sealed class MenuShortResult
        {
            public byte[] Exported;
            public int ListedFaces;
            public string Recorded;
            public string Error;
            public string Kept;
        }

        // rebuildBoard is null once the board closed; otherwise it rebuilds the
        // board and gives its list count.
        // A failed recordList is undone by exportMenuAgain, whose bytes are saved.
        internal static MenuShortResult ResolveMenuShort(byte[] exported, int menuFaces, int listedFaces,
            Func<int> rebuildBoard, Func<byte[]> recordList, Func<byte[]> exportMenuAgain)
        {
            var result = new MenuShortResult { Exported = exported, ListedFaces = listedFaces };
            // A recovery attempt after the board closed has no list to record.
            if (rebuildBoard == null)
            {
                result.Kept = string.Format(ExporterLocalization.T(MenuShortNotRecordedFormat), menuFaces, listedFaces);
                return result;
            }
            // The board may have changed before a recovery attempt.
            var listed = rebuildBoard();
            result.ListedFaces = listed;
            try
            {
                result.Exported = recordList();
                result.Recorded = string.Format(ExporterLocalization.T(MenuShortRecordedFormat), menuFaces, listed);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                // The menu result saved before this change is saved again.
                result.Exported = exportMenuAgain();
                result.Error = exception.Message;
                result.Kept = string.Format(ExporterLocalization.T(MenuShortNotRecordedFormat), menuFaces, listed);
            }
            return result;
        }

        // ---- Pages ----

        // Circles joined by lines, each followed by the step name: a check for
        // done steps, the number filled with the accent for the current one and
        // in a ring for the rest; a step with an error is red with "!". The
        // places come from the row width only (StepIndicator), so the font size
        // cannot move them. Moving is by the bottom bar only.
        void DrawSteps()
        {
            var row = GUILayoutUtility.GetRect(100, StepIndicator.Height, GUILayout.ExpandWidth(true));
            var layout = StepIndicator.Compute(row);
            var names = StepIndicator.Names(transferMode);
            var shown = StepIndicator.Shown(session != null, page);
            var repaint = Event.current.type == EventType.Repaint;
            if (repaint) stepLayout = layout;
            for (var index = 0; index < names.Length; index++)
            {
                if (index > 0 && repaint) EditorGUI.DrawRect(layout.Lines[index - 1], CardBorderColor);
                var text = ExporterLocalization.T(names[index]);
                var (number, name) = StepIndicator.Split(text);
                var state = StepIndicator.StateOf(index, shown);
                var error = StepIndicator.HasError(index, excludedObjectError);
                if (repaint) DrawStepMarker(layout.Circles[index], StepIndicator.MarkerOf(state, error), number.Length > 0 ? number : (index + 1).ToString());
                var look = StepIndicator.LabelOf(state, error);
                var style = look.Error ? (look.Bold ? stepErrorBold : errorText) : look.Bold ? boldText : look.Dim ? dimText : cardText;
                var rect = layout.Labels[index];
                GUI.Label(rect, new GUIContent(Fit(name, style, rect.width), text), style);
            }
        }

        void DrawStepMarker(Rect circle, StepIndicator.Marker marker, string number)
        {
            var fill = marker == StepIndicator.Marker.Error ? ErrorColor : marker == StepIndicator.Marker.Ring ? DimColor : AccentButtonColor;
            GUI.DrawTexture(circle, Disc(marker == StepIndicator.Marker.Ring), ScaleMode.StretchToFill, true, 0, fill, 0, 0);
            if (marker == StepIndicator.Marker.Error) DrawStepGlyph(circle, "!", stepMarkerText);
            else if (marker == StepIndicator.Marker.Check)
            {
                var previous = Handles.color; Handles.color = Color.white;
                Handles.DrawAAPolyLine(2.4f, new Vector3(circle.x + circle.width * .28f, circle.y + circle.height * .52f),
                    new Vector3(circle.x + circle.width * .44f, circle.y + circle.height * .68f), new Vector3(circle.x + circle.width * .73f, circle.y + circle.height * .34f));
                Handles.color = previous;
            }
            else DrawStepGlyph(circle, number, marker == StepIndicator.Marker.Ring ? stepMarkerDim : stepMarkerText);
        }

        // A label centres the text's advance and line box, not the digit itself, and
        // snaps it to whole pixels, so a digit in the circle lands up to 2px off its
        // centre. Draw the glyph from the font instead, with its box on the centre.
        void DrawStepGlyph(Rect circle, string text, GUIStyle style)
        {
            var font = EditorStyles.miniBoldFont;
            if (text.Length != 1 || font == null || font.material == null) { GUI.Label(circle, text, style); return; }
            var scale = EditorGUIUtility.pixelsPerPoint;
            var size = Mathf.RoundToInt((style.fontSize > 0 ? style.fontSize : font.fontSize) * scale);
            font.RequestCharactersInTexture(text + "1", size);
            if (!font.GetCharacterInfo(text[0], out var glyph, size) || !font.GetCharacterInfo('1', out var one, size)) { GUI.Label(circle, text, style); return; }
            var rect = StepIndicator.GlyphRect(circle, glyph.minX, glyph.maxX, glyph.minY, glyph.maxY, one.minY, one.maxY, scale);
            if (!font.material.SetPass(0)) { GUI.Label(circle, text, style); return; }
            GL.Begin(GL.QUADS);
            GL.Color(style.normal.textColor * GUI.color * GUI.contentColor);
            GL.TexCoord(glyph.uvTopLeft); GL.Vertex3(rect.xMin, rect.yMin, 0);
            GL.TexCoord(glyph.uvTopRight); GL.Vertex3(rect.xMax, rect.yMin, 0);
            GL.TexCoord(glyph.uvBottomRight); GL.Vertex3(rect.xMax, rect.yMax, 0);
            GL.TexCoord(glyph.uvBottomLeft); GL.Vertex3(rect.xMin, rect.yMax, 0);
            GL.End();
        }

        // A white disc or ring, tinted when drawn; made larger than drawn so the edge stays smooth.
        Texture2D Disc(bool ring)
        {
            var texture = ring ? ringTexture : discTexture;
            if (texture != null) return texture;
            const int size = 96; const float edge = size / 2f, stroke = 6;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), new Vector2(edge, edge));
                    var outer = Mathf.Clamp01(edge - distance);
                    var alpha = ring ? Mathf.Min(outer, Mathf.Clamp01(distance - (edge - stroke))) : outer;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            texture.SetPixels32(pixels); texture.Apply();
            if (ring) ringTexture = texture; else discTexture = texture;
            return texture;
        }

        bool PreviewPending => excludedObjectEdits != null || GimmicksPending;

        // Leaving the objects page applies its edits; the other pages just move.
        internal void GoTo(int target)
        {
            if (page == ObjectsPage) ApplyThen(() => SetPage(target));
            else Queue(() => SetPage(target));
        }

        // Edits not in the preview yet (from the objects page, or gimmick choices
        // changed in the export window) are applied before `then`. A list the
        // preview cannot take keeps the edits and opens the objects page, where
        // the reason is shown; leaving or saving again tries again.
        void ApplyThen(Action then)
        {
            if (!PreviewPending) { Queue(then); return; }
            Queue(() =>
            {
                try { ApplyPreview(); }
                finally { applyingOnLeave = false; }
                if (!PreviewPending && excludedObjectError == null) Queue(then);
                else SetPage(ObjectsPage);
            });
            applyingOnLeave = true;
        }

        void SetPage(int target)
        {
            target = Mathf.Clamp(target, ExpressionsPage, InfoPage);
            // The status line reports what was just done on the page being left.
            // Staying (a failed apply) keeps the scroll, so the reason stays in view.
            if (target != page) { status = null; pageScroll = Vector2.zero; }
            page = target;
            GUIUtility.keyboardControl = 0;
            Repaint();
        }

        // The objects left out of the VRM by hand, shared with the export window
        // through the avatar's saved settings. Edits reach the preview when the page is left.
        void DrawExcludedObjects()
        {
            var shown = (System.Collections.Generic.IReadOnlyList<GameObject>)excludedObjectEdits ?? session.ExcludedObjects;
            var count = shown.Count(item => item != null);
            var header = GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true));
            var label = string.Format(ExporterLocalization.T("手動で除外するオブジェクト（{0}件）"), count);
            GUI.Label(header, new GUIContent(Fit(label, boldText, header.width), label), boldText);
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
            var add = ExporterLocalization.T("除外するオブジェクトを追加");
            if (GUILayout.Button(new GUIContent(Fit(add, GUI.skin.button, Mathf.Max(0, position.width - 48)), add))) Edits().Add(null);
            if (!string.IsNullOrEmpty(excludedObjectError)) EditorGUILayout.HelpBox(excludedObjectError, MessageType.Error);
        }

        void ApplyPreview()
        {
            if (session == null) return;
            // The rebuild reads the saved gimmick choices, so the object list
            // applies them as well.
            if (excludedObjectEdits != null) { ApplyExcludedObjects(); return; }
            if (!GimmicksPending) return;
            var current = session.ExcludedObjects.ToArray();
            // As in RefreshCandidateValidation: objects that leave no preview
            // wait in the list with the reason.
            var problem = current.Length > 0 ? RebuildKeepingAssignments(current, Array.Empty<GameObject>()) : RebuildKeepingAssignments(current);
            candidateValidationPending = true;
            if (problem != null) { excludedObjectEdits = current.ToList(); excludedObjectError = problem; }
        }

        // ---- VRChat gimmicks ----

        const string GimmicksPendingReason = "VRChat用のギミックがプレビューに反映されていません";

        // The avatar's saved settings, read again only after a save.
        ExportSettings SavedSettings()
        {
            if (savedSettings == null || savedSettingsSource != source || savedSettingsRevision != ExportSettingsStore.Revision)
            {
                savedSettings = ExportSettingsStore.Load(source);
                savedSettingsSource = source;
                savedSettingsRevision = ExportSettingsStore.Revision;
            }
            return savedSettings;
        }

        // Saved gimmick choices that the preview (and so the export) does not use yet.
        bool GimmicksPending => session != null && !ExportGimmickChoices.SameGimmicks(SavedSettings().Gimmicks(), session.GimmickOptions);

        // Runs on Layout only, so the rows stay the same until the next layout.
        void RefreshGimmicks()
        {
            if (Event.current.type != EventType.Layout || session == null) return;
            var changed = gimmickFindings == null || gimmickSession != session;
            if (!changed && gimmicksStale)
            {
                // A busy scene changes the hierarchy often; analyze at most once a second.
                if (EditorApplication.timeSinceStartup < nextGimmickScan) Repaint();
                else changed = true;
            }
            if (changed)
            {
                gimmickFindings = ExportGimmickChoices.Analyze(source, session.ExcludedObjects);
                gimmickGroups = ExportGimmickChoices.Groups(gimmickFindings);
                gimmickContents = gimmickGroups.SelectMany(group => group.Findings).Where(f => f.Target != null)
                    .ToDictionary(f => f, f => ExportGimmickChoices.Contents(f, ExporterLocalization.T));
                gimmickSession = session; gimmicksStale = false;
                nextGimmickScan = EditorApplication.timeSinceStartup + 1;
            }
            gimmicksRemoved = ExportGimmickChoices.Removed(gimmickFindings, SavedSettings().Gimmicks());
        }

        void DrawGimmicks()
        {
            RefreshGimmicks();
            var (removedCount, includedCount) = ExportGimmickChoices.Counts(gimmickGroups, gimmicksRemoved);
            var header = GUILayoutUtility.GetRect(100, 20, GUILayout.ExpandWidth(true));
            var label = ExportGimmickChoices.Header(removedCount, includedCount);
            GUI.Label(header, new GUIContent(Fit(label, boldText, header.width), label), boldText);
            var line = EditorGUIUtility.singleLineHeight;
            const float indent = 14, choiceWidth = 110, gap = 8;
            var note = ExporterLocalization.T("含めてもVRChatの中の動きは再現されません");
            var noteRect = GUILayoutUtility.GetRect(100, line, GUILayout.ExpandWidth(true));
            GUI.Label(noteRect, new GUIContent(Fit(note, dimMini, noteRect.width), note), dimMini);
            if (gimmickGroups.Count == 0)
            {
                var none = ExporterLocalization.T("見つかりませんでした");
                var noneRect = GUILayoutUtility.GetRect(100, line, GUILayout.ExpandWidth(true));
                noneRect.xMin += indent;
                GUI.Label(noneRect, new GUIContent(Fit(none, dimText, noneRect.width), none), dimText);
                return;
            }
            var settings = SavedSettings();
            var autoExclude = settings.AutoExcludeGimmicks;
            var choices = new[] { ExporterLocalization.T("除外"), ExporterLocalization.T("含める") };
            foreach (var (group, findings) in gimmickGroups)
            {
                EditorGUILayout.Space(4);
                var groupRect = GUILayoutUtility.GetRect(100, line, GUILayout.ExpandWidth(true));
                groupRect.xMin += indent;
                var groupName = ExporterLocalization.T(group);
                GUI.Label(groupRect, new GUIContent(Fit(groupName, dimMiniBold, groupRect.width), groupName), dimMiniBold);
                foreach (var finding in findings)
                {
                    var row = GUILayoutUtility.GetRect(100, line, GUILayout.ExpandWidth(true));
                    row.xMin += indent;
                    if (finding.Target == null) continue;
                    // A row kept by a kept parent sits under it and names it.
                    var parent = ExportGimmickChoices.InheritedFrom(finding, settings);
                    if (parent != null) row.xMin += indent;
                    var (nameRect, fieldRect, reasonRect, choiceRect) = GimmickRow(row, choiceWidth, gap);
                    var rowName = ExportGimmickChoices.RowName(finding);
                    var reason = ExporterLocalization.T(finding.Reason ?? "");
                    var path = ExportGimmickChoices.PathOf(finding, source);
                    EditorGUIUtility.AddCursorRect(nameRect, MouseCursor.Link);
                    if (GUI.Button(nameRect, new GUIContent(Fit(rowName, cardText, nameRect.width), reason.Length == 0 ? path : path + "\n" + reason), cardText))
                        EditorGUIUtility.PingObject(finding.Target);
                    // Read-only like an inspector's Script field: a click still pings it.
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUI.ObjectField(fieldRect, finding.Target, typeof(GameObject), true);
                    gimmickContents.TryGetValue(finding, out var contents);
                    contents ??= "";
                    if (parent != null)
                    {
                        // The choice is the parent's: its name and the result take the contents and choice columns.
                        var follows = string.Format(ExporterLocalization.T("{0}に従う：含める"), parent.name);
                        var followRect = new Rect(reasonRect.x, row.y, row.xMax - reasonRect.x, row.height);
                        GUI.Label(followRect, new GUIContent(Fit(follows, dimMiniRight, followRect.width), contents), dimMiniRight);
                        continue;
                    }
                    GUI.Label(reasonRect, new GUIContent(Fit(contents, dimText, reasonRect.width), reason), dimText);
                    var kept = ExportGimmickChoices.Kept(finding, settings);
                    var included = !gimmicksRemoved.Contains(finding);
                    // Kept by another part of the same gimmick: the choice is made there.
                    var editable = autoExclude && (kept || !included);
                    using (new EditorGUI.DisabledScope(!editable))
                    {
                        var choice = GUI.Toolbar(choiceRect, included ? 1 : 0, choices, EditorStyles.miniButton);
                        if (editable && choice != (included ? 1 : 0)) SetGimmick(finding, choice == 1);
                    }
                }
            }
        }

        // name | object field | contents | choice on one line. A narrow window
        // keeps the field readable and shortens the name and the contents first.
        internal static (Rect Name, Rect Field, Rect Contents, Rect Toggle) GimmickRow(Rect row, float toggleWidth, float gap)
        {
            var rest = Mathf.Max(0, row.width - toggleWidth - gap * 3);
            var field = Mathf.Max(rest * .35f, Mathf.Min(120, rest * .5f));
            var name = new Rect(row.x, row.y, (rest - field) * .55f, row.height);
            var fieldRect = new Rect(name.xMax + gap, row.y, field, row.height);
            var contents = new Rect(fieldRect.xMax + gap, row.y, rest - field - name.width, row.height);
            return (name, fieldRect, contents, new Rect(row.xMax - toggleWidth, row.y, toggleWidth, row.height));
        }

        // Saved at once, so the export window and the next export read it.
        void SetGimmick(ExportGimmickFinding finding, bool include)
        {
            var settings = ExportSettingsStore.Load(source);
            ExportGimmickChoices.SetKept(finding, settings, include);
            ExportSettingsStore.Save(source, settings);
            Repaint();
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
            var reset = ExporterLocalization.T("割り当てをリセット");
            var resetRect = new Rect(sizeLabel.x - 8 - 110, header.y + 2, 110, 18);
            if (GUI.Button(resetRect, new GUIContent(Fit(reset, EditorStyles.miniButton, resetRect.width - 8), reset), EditorStyles.miniButton) &&
                EditorUtility.DisplayDialog(reset, ExporterLocalization.T("手で割り当てた表情と書き出さない指定を消して名前から割り当て直します"),
                    ExporterLocalization.T("リセット"), ExporterLocalization.T("戻る")))
                Queue(ResetBoard);
            var right = resetRect.x - 8;
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
                DrawTile(tile, thumb, board.NameOf(clip), clip, Thumbnail(clip, null, null, size), assignment.Automatic, assignment.Automatic ? "自動" : null,
                    reason ?? AssetDatabase.GetAssetPath(clip), "その他の表情へ戻す", () => MoveTo(clip, Place.Other));
            else if (assignment != null && !assignment.IsNone)
            {
                // A BlendShape, or a file no longer on the board: it can only be cleared.
                var name = ExperimentalExpressionCaptureSession.PresetDisplayName(assignment);
                var face = assignment.IsShape ? Thumbnail(null, assignment.Path, assignment.Shape, size) : Thumbnail(clip, null, null, size);
                DrawTile(tile, thumb, assignment.IsShape ? assignment.Shape : name, null, face, assignment.Automatic, assignment.Automatic ? "自動" : null,
                    reason ?? name, "指定を外す", () => {
                        // Like a face leaving the slot, gaze and neutral are left open.
                        if (!ExperimentalExpressionCaptureSession.FilledFromNames(preset)) { ChoosePreset(preset, ExperimentalExpressionCaptureSession.PresetAssignment.None(preset)); return; }
                        session.SetPreset(preset, null); SaveChoices(); ApplyAutomaticPresets();
                    });
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
            // An open gaze or neutral slot is filled from names at export.
            if (assignment == null && ExperimentalExpressionCaptureSession.FilledFromNames(preset)) return ExporterLocalization.T("名前から埋める");
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
            DrawTile(tile, new Rect(tile.x + 7, tile.y + 4, size, size), board.NameOf(clip), clip, Thumbnail(clip, null, null, size), false,
                null, ItemTooltip(item), closeTooltip, close);
        }

        // count: the faces that are exported. Unsupported ones are not shown.
        internal static string OtherTitle(int count) => string.Format(ExporterLocalization.T("その他の表情 {0}件"), count);

        internal const string MenuLossFormat = "表情メニューの{0}項目はVRMに入らないことがあります";
        internal const string MenuLossListHeader = "ボードに無い表情メニューの項目";
        internal const string MenuLossMoreFormat = "ほか{0}項目";
        internal const string MenuUnreadable = "表情メニューを読み取れないためVRMに入らない項目を確認できません";
        const float MenuLossBandHeight = 22;
        const int MenuLossListed = 20;

        // The line above the custom list in the record route: the menu items
        // that route leaves out (outside: MenuEntriesOutsideBoard, null when
        // the menu could not be read). Null when nothing is shown.
        internal static (string Text, string Tooltip)? MenuLossBand(bool recordRoute, System.Collections.Generic.IReadOnlyList<string> outside)
        {
            if (!recordRoute) return null;
            if (outside == null) { var unreadable = ExporterLocalization.T(MenuUnreadable); return (unreadable, unreadable); }
            if (outside.Count == 0) return null;
            var text = string.Format(ExporterLocalization.T(MenuLossFormat), outside.Count);
            var lines = new System.Collections.Generic.List<string> { text, ExporterLocalization.T(MenuLossListHeader) };
            lines.AddRange(outside.Take(MenuLossListed));
            if (outside.Count > MenuLossListed) lines.Add(string.Format(ExporterLocalization.T(MenuLossMoreFormat), outside.Count - MenuLossListed));
            return (text, string.Join("\n", lines));
        }

        void DrawOtherPanel(Rect rect)
        {
            var items = board.ItemsIn(PlaceKind.Other).ToArray();
            var body = DrawPanelFrame(rect, Place.Other, OtherTitle(board.OtherCount),
                board.UsesRecordRoute ? ExporterLocalization.T("この一覧を記録して書き出し") : ExporterLocalization.T("アバターの表情メニューをそのまま書き出し"));
            // A fixed band, so the tiles move only when it appears or goes.
            var band = MenuLossBand(board.UsesRecordRoute, board.UsesRecordRoute ? session?.SourceMenuEntriesOutsideBoard : null);
            if (band != null)
            {
                var line = new Rect(body.x, body.y, body.width, MenuLossBandHeight);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(line, CardColor);
                    EditorGUI.DrawRect(new Rect(line.x, line.yMax - 1, line.width, 1), LineColor);
                }
                var icon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
                if (icon != null && Event.current.type == EventType.Repaint)
                    GUI.DrawTexture(new Rect(line.x + Gap, line.y + 3, 16, 16), icon, ScaleMode.ScaleToFit);
                var textRect = new Rect(line.x + Gap + 20, line.y, Mathf.Max(0, line.width - Gap * 2 - 20), line.height - 1);
                GUI.Label(textRect, new GUIContent(Fit(band.Value.Text, dimMini, textRect.width), band.Value.Tooltip), dimMini);
                // An empty label over the full line, so the icon and the gaps
                // show the tooltip too, not only the text.
                GUI.Label(line, new GUIContent("", band.Value.Tooltip), GUIStyle.none);
                body = new Rect(body.x, body.y + MenuLossBandHeight, body.width, Mathf.Max(0, body.height - MenuLossBandHeight));
            }
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

        // A renamed face also names its file; the last line is how to rename.
        string ItemTooltip(ExpressionBoard.Item item) =>
            (board.Renamed(item.Clip) ? board.NameOf(item.Clip) + "\n" + ExporterLocalization.T("ファイル名") + "  " + item.Clip.name + "\n" : "") +
            (item.Added ? item.Source : ExporterLocalization.T(item.Source ?? "")) + "\n" + AssetDatabase.GetAssetPath(item.Clip) +
            "\n" + ExporterLocalization.T("ダブルクリックで名前を変更");

        // The × of a tile, drawn only while the mouse is over the tile. The
        // button is called on every event, drawn empty when hidden, so it always
        // takes its control id and the controls after it keep theirs between
        // the repaint and the other events.
        internal static bool CloseButton(Rect tile, Rect button, GUIContent content, GUIStyle style)
        {
            var shown = Event.current.type != EventType.Repaint || tile.Contains(Event.current.mousePosition);
            return GUI.Button(button, shown ? content : GUIContent.none, shown ? style : GUIStyle.none);
        }

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
                if (CloseButton(tile, new Rect(tile.xMax - 18, tile.y + 2, 16, 16), new GUIContent("×", ExporterLocalization.T(closeTooltip)), closeText)) Queue(close);
            }
            if (clip == null) return;
            if (current.type == EventType.Repaint) candidateRects[clip] = ScreenRect(tile);
            var id = GUIUtility.GetControlID(FocusType.Passive);
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (!tile.Contains(current.mousePosition)) break;
                    // A double click on the card renames the face; the box opens under its name.
                    if (current.button == 0 && current.clickCount == 2)
                    { GUIUtility.hotControl = 0; RequestRename(clip, ScreenRect(nameRect)); current.Use(); }
                    else if (current.button == 0) { GUIUtility.hotControl = id; pressStart = current.mousePosition; current.Use(); }
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
            var text = Fit(board != null ? board.NameOf(clip) : clip.name, cardText, 200);
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
            menu.AddSeparator("");
            // A slot face is exported under the VRM preset name.
            if (board.CanRename(clip))
            {
                var at = candidateRects.TryGetValue(clip, out var tile) ? new Rect(tile.x, tile.yMax - ExpressionSlotLayout.Name, tile.width, ExpressionSlotLayout.Name)
                    : new Rect(Event.current.mousePosition, Vector2.zero);
                menu.AddItem(new GUIContent(ExporterLocalization.T("名前を変更…")), false, () => RequestRename(clip, at));
                if (board.Renamed(clip)) menu.AddItem(new GUIContent(ExporterLocalization.T("ファイル名に戻す")), false, () => Queue(() => RenameFace(clip, "")));
            }
            else if (place.Kind == PlaceKind.Slot)
                menu.AddDisabledItem(new GUIContent(ExporterLocalization.T("名前を変更（標準の表情は名前が決まっています）")), false);
            menu.ShowAsContext();
        }

        // at: the card's name in window space. A slot face only says why it keeps its name.
        void RequestRename(AnimationClip clip, Rect at)
        {
            if (board == null) return;
            if (board.CanRename(clip)) { renameRequest = (clip, at); Repaint(); }
            else if (board.PlaceOf(clip).Kind == PlaceKind.Slot) { status = ExporterLocalization.T("標準の表情は名前が決まっています"); Repaint(); }
        }

        void OpenRename(AnimationClip clip, Rect at) =>
            PopupWindow.Show(at, new ExpressionRenamePopup(board.NameOf(clip), clip.name, board.Renamed(clip), name =>
            {
                // The file name is always accepted: it is how a face gets its own name back.
                var key = board?.RenameError(clip, name);
                if (key == null || name.Trim() == clip.name.Trim()) return null;
                return string.Format(ExporterLocalization.T(key), name.Trim());
            }, name => Queue(() => RenameFace(clip, name))));

        void RenameFace(AnimationClip clip, string name)
        {
            RebuildBoard();
            if (!board.Rename(clip, name)) return;
            SaveChoices();
            if (previewClip == clip) ShowFace(board.NameOf(clip));
        }

        void PreviewBoardClip(AnimationClip clip) => Queue(() => {
            RefreshCandidateValidation(); var currentReason = session.ClipError(clip, DefaultTime(clip));
            if (currentReason != null) throw new InvalidOperationException(currentReason);
            session.PreviewClip(clip, DefaultTime(clip)); ShowFace(board.NameOf(clip)); previewClip = clip;
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

        void SaveChoices() => ExpressionPresetStore.Save(presetKey, session.Presets.Values, board?.Excluded, board?.Names);

        void DrawAddRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                clipToAdd = (AnimationClip)EditorGUILayout.ObjectField(ExporterLocalization.T("表情の.animを追加"), clipToAdd, typeof(AnimationClip), false);
                using (new EditorGUI.DisabledScope(clipToAdd == null))
                    if (GUILayout.Button(ExporterLocalization.T("追加"), GUILayout.Width(60))) { var clip = clipToAdd; Queue(() => AddClip(clip)); }
            }
            EditorGUILayout.Space(4);
            DropClips(ExporterLocalization.T("Projectから表情の.animをドロップして追加（複数可）"), AddClips);
            if (presetKey == null) EditorGUILayout.LabelField(ExporterLocalization.T("シーンが未保存のため指定は次回に残りません"), EditorStyles.wordWrappedMiniLabel);
            else if (presetSkipped > 0) EditorGUILayout.LabelField(ExporterLocalization.T("前回の指定のうち元のファイルがないものを外しました"), EditorStyles.wordWrappedMiniLabel);
        }

        // 戻る | the summary, or what stops the next step | the next step. The
        // bar stays below the scroll view, so the buttons keep their place.
        void DrawBottomBar()
        {
            EditorGUILayout.Space(6);
            // A narrow window puts the summary on its own row above the buttons.
            var stacked = position.width < 760;
            var bar = GUILayoutUtility.GetRect(100, stacked ? 60 : 38, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width, 1), LineColor);
            var nextWidth = Mathf.Min(220, bar.width * .4f); var backWidth = Mathf.Min(80, bar.width * .18f);
            var buttons = stacked ? bar.y + 29 : bar.y + 7;
            var nextRect = new Rect(bar.xMax - nextWidth, buttons, nextWidth, 28);
            // The first page has no 戻る; its place stays empty so nothing moves.
            var backRect = new Rect(bar.x, buttons + 3, backWidth, 22);
            var summaryRect = stacked ? new Rect(bar.x, bar.y + 5, bar.width, 20) : new Rect(backRect.xMax + 8, bar.y + 7, nextRect.x - 16 - backRect.xMax, 28);
            var (summary, summaryStyle) = Summary();
            GUI.Label(summaryRect, new GUIContent(Fit(summary, summaryStyle, summaryRect.width), summary), summaryStyle);
            var back = ExporterLocalization.T("戻る");
            if (page > ExpressionsPage && GUI.Button(backRect, new GUIContent(Fit(back, EditorStyles.miniButton, backRect.width - 8), back), EditorStyles.miniButton))
                GoTo(page - 1);
            string next; bool enabled; Action action;
            if (page == ExpressionsPage)
            { next = "次へ：除外するオブジェクト"; enabled = BoardBlockReason(includePending: false) == null; action = () => GoTo(ObjectsPage); }
            else if (page == ObjectsPage && transferMode)
            { next = "次へ：名前と利用条件"; enabled = BoardBlockReason(includePending: false) == null; action = () => ApplyThen(OpenTransferDetails); }
            else if (page == ObjectsPage)
            { next = "次へ：VRMの情報"; enabled = true; action = () => GoTo(InfoPage); }
            // The transfer page has its own "QRコードを作る"; the place stays empty so nothing moves.
            else if (transferMode) return;
            else
            { next = "VRMを保存"; enabled = CanSave; action = () => ApplyThen(Export); }
            next = ExporterLocalization.T(next);
            using (new EditorGUI.DisabledScope(!enabled))
                if (GUI.Button(nextRect, new GUIContent(Fit(next, accentButton, nextRect.width - 12), next), accentButton)) action();
        }

        // The line between the buttons: what stops the next step in red, the
        // state of the objects page while it waits or applies, or the counts.
        internal (string Text, GUIStyle Style) Summary()
        {
            var counts = string.Format(ExporterLocalization.T("VRMの表情 {0}/{1}・その他の表情 {2}・書き出さない表情 {3}"),
                board.SlotCount(MainPresets), MainPresets.Length, board.OtherCount, board.ExcludedCount);
            if (page == ObjectsPage)
            {
                if (applyingOnLeave) return (ExporterLocalization.T("除外の変更をプレビューに反映しています…"), dimText);
                if (!string.IsNullOrEmpty(excludedObjectError)) return (ExporterLocalization.T("プレビューに反映できませんでした・理由は一覧の下にあります"), errorText);
                if (transferMode && BoardBlockReason(includePending: false) is string stop) return (stop, errorText);
                if (PreviewPending) return (ExporterLocalization.T("プレビュー未反映　ページを移るときに反映します"), dimText);
                return (counts, dimText);
            }
            var reason = BoardBlockReason(includePending: false);
            // The transfer asks for its author in its own terms.
            if (reason == null && page == InfoPage && !transferMode && !ExportAuthor.IsValid(author)) reason = ExporterLocalization.T(ExportAuthor.Required);
            if (reason != null) return (reason, errorText);
            // Kept from opening when the saved list leaves no preview.
            if (page == ExpressionsPage && !string.IsNullOrEmpty(excludedObjectError))
                return (ExporterLocalization.T("除外するオブジェクトを反映できませんでした・次のページに理由があります"), errorText);
            if (page == InfoPage && PreviewPending) return (ExporterLocalization.T("プレビュー未反映　保存するときに反映します"), dimText);
            return (counts, dimText);
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
            dimMiniBold = Label(EditorStyles.miniBoldLabel, DimColor);
            errorMini = Label(EditorStyles.miniLabel, ErrorColor);
            nameCenter = Label(EditorStyles.label, TextColor, TextAnchor.MiddleCenter);
            // The badge sits on the thumbnail, so it is light on both skins.
            badgeText = Label(EditorStyles.miniLabel, Hex(0xd2d2d2), TextAnchor.MiddleCenter);
            closeText = Label(EditorStyles.label, DimColor, TextAnchor.MiddleCenter);
            closeText.hover.textColor = TextColor;
            centeredDim = Label(EditorStyles.miniLabel, DimColor, TextAnchor.MiddleCenter);
            // The step circles: white on the filled ones, dimmed in the ring.
            stepMarkerText = Label(EditorStyles.miniBoldLabel, Color.white, TextAnchor.MiddleCenter);
            stepMarkerDim = Label(EditorStyles.miniBoldLabel, DimColor, TextAnchor.MiddleCenter);
            stepErrorBold = Label(EditorStyles.boldLabel, ErrorColor);
            // Drop areas: the hint is centred in the box and readable on both skins.
            dropBox = Label(GUI.skin.box, DimColor, TextAnchor.MiddleCenter);
            dropBox.wordWrap = true;
            dropBox.padding = new RectOffset(6, 6, 2, 2);
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
            foreach (var texture in new[] { accentNormal, accentHover, accentActive, discTexture, ringTexture }) if (texture != null) DestroyImmediate(texture);
            accentNormal = accentHover = accentActive = discTexture = ringTexture = null;
        }

        void ReleaseStyles() { ReleaseTextures(); cardText = null; fitted.Clear(); }

        // ---- Save ----

        // The blink setting the export receives. Blink slots left empty take the
        // closed eyes found on the avatar; a face put in a slot comes first.
        // The names match the blink messages shared with the export window.
        void DrawBlink()
        {
            EditorGUILayout.LabelField(ExporterLocalization.T("瞬き"), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                const float buttonWidth = 100;
                var label = ExporterLocalization.T(configuredBlink?.Mode == BlinkExportMode.Manual ? "瞬きを書き出す" : "空いている瞬きの枠は閉じた目を自動で探す");
                var width = Mathf.Max(0, position.width - 32 - buttonWidth - 30);
                var blinkOn = EditorGUILayout.ToggleLeft(new GUIContent(Fit(label, EditorStyles.label, width), label), automaticBlink);
                // Blink candidates follow the blink setting the export receives.
                if (blinkOn != automaticBlink) { automaticBlink = blinkOn; Queue(ApplyAutomaticPresets); }
                if (GUILayout.Button(ExporterLocalization.T("確認・調整"), GUILayout.Width(buttonWidth))) Queue(ShowBlinkConfiguration);
            }
            var note = ExporterLocalization.T("枠に入れた表情が優先されます");
            var noteRect = GUILayoutUtility.GetRect(100, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            GUI.Label(noteRect, new GUIContent(Fit(note, dimMini, noteRect.width), note), dimMini);
        }

        // The third page of the file export; the transfer asks these in its next window.
        void DrawInfo()
        {
            author = EditorGUILayout.TextField(new GUIContent(ExporterLocalization.T("作者名"), ExporterLocalization.T("VRMファイルに記録される作者名です。")), author);
            // The line is always reserved so typing does not move the rows below.
            EditorGUILayout.LabelField(ExportAuthor.IsValid(author) ? "" : ExporterLocalization.T(ExportAuthor.Required), EditorStyles.miniLabel);
            if (licenseOptions == null) licenseOptions = new AvatarLicenseOptions();
            AvatarLicenseSettingsUi.Draw(ref showLicenseSettings, licenseOptions);
            EditorGUILayout.Space(8);
            DrawSavedResult();
        }

        void ShowFace(string name) { previewName = name; previewClip = null; Repaint(); }

        // Without exclusions or added files nothing is recorded and the
        // ordinary export keeps the avatar's own faces. Otherwise exactly the
        // usable faces of the custom list are recorded under their board names.
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
            foreach (var input in inputs) input.Name = board != null ? board.NameOf(input.Clip) : input.Clip.name;
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
            var usable = additions.Where(input => input.Error == null).ToArray();
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

        internal const string DeletedClipReason = "ファイルが削除されました。";

        // One line per file that was not added: its name, then the reason.
        internal static string RejectedMessage(System.Collections.Generic.IEnumerable<(string Name, string Reason)> rejected) =>
            string.Join("\n", rejected.Select(item => string.Format(ExporterLocalization.T("「{0}」は追加できません。{1}"), item.Name, item.Reason)));

        void DropClips(string label, Action<AnimationClip[]> accept)
        {
            var area = GUILayoutUtility.GetRect(100, 42, GUILayout.ExpandWidth(true));
            GUI.Box(area, label, dropBox);
            var current = Event.current;
            if (current.type == EventType.Repaint && clipDropHover == label) Outline(area, AccentColor);
            if (!area.Contains(current.mousePosition) || current.type != EventType.DragUpdated && current.type != EventType.DragPerform) return;
            var clips = DragAndDrop.objectReferences.OfType<AnimationClip>().Where(clip => clip != null).Distinct().ToArray();
            var valid = GUI.enabled && clips.Length > 0 && DragAndDrop.objectReferences.All(item => item is AnimationClip);
            DragAndDrop.visualMode = valid ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
            if (valid && current.type == EventType.DragUpdated) clipDropHoverNext = label;
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
            board.Names.Clear();
            var names = ExpressionPresetStore.LoadNames(presetKey, out var skippedNames);
            foreach (var pair in names) board.Names[pair.Key] = pair.Value;
            presetSkipped += skippedNames;
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
            // Gaze and neutral have no automatic suggestion; an open slot is
            // filled from names at export, which "none" stops.
            var named = ExperimentalExpressionCaptureSession.FilledFromNames(preset) && !session.HasAuthoredPreset(preset);
            Add(ExporterLocalization.T(session.HasAuthoredPreset(preset) ? "元のVRM設定" : "なし"), ExperimentalExpressionCaptureSession.PresetAssignment.None(preset),
                named ? current != null && current.IsNone : current == null || current.IsNone);
            if (named || current != null && !current.Automatic)
                menu.AddItem(new GUIContent(ExporterLocalization.T(named ? "名前から埋める" : "自動の候補に戻す")), named && current == null, () => Queue(() => {
                    session.SetPreset(preset, null); SaveChoices(); ApplyAutomaticPresets();
                }));
            menu.AddSeparator("");
            foreach (var item in board.Items.Where(ExpressionBoard.Usable))
            {
                var clip = item.Clip;
                var group = item.Added ? ExporterLocalization.T("追加した表情") : item.Source == "FaceEmoの登録" ? "FaceEmo" : ExporterLocalization.T("アバターのController");
                menu.AddItem(new GUIContent(group + "/" + Item(board.NameOf(clip))), current?.ResolveClip() == clip, () => Queue(() => MoveTo(clip, Place.Slot(preset))));
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
            RebuildBoard();
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

        internal ExportRecoverySession SaveVrm(string path, bool interactive = false)
        {
            RequireAuthor();
            return Save(path, AvatarName(), author, licenseOptions.Copy(), interactive, null);
        }

        // The detailed "アプリに転送" writes what this board shows, with the name,
        // author and license of the transfer window. Returns null when it saved
        // (saved has run) or was canceled, and the reason when it failed; the
        // recovery window then saves the same destination and runs saved.
        // interactive only changes what is asked or shown, never the bytes: the
        // quick transfer passes false, so what the export worked around is not
        // asked about before saving and goes to the Console instead, and a
        // board that blocks the export also says why.
        internal string TransferExport(string name, string author, AvatarLicenseOptions license, string destination, Action saved, bool interactive = true)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (session == null) return ExporterLocalization.T(AppTransferExport.BoardClosed);
            if (BlocksExport)
            {
                // Out of sight, the quick transfer also says what to fix on the board.
                var needsFix = ExporterLocalization.T(AppTransferExport.BoardNeedsFix);
                var reason = interactive ? null : BoardBlockReason();
                return string.IsNullOrEmpty(reason) ? needsFix : needsFix + "\n" + reason;
            }
            if (!ExportAuthor.IsValid(author)) return ExporterLocalization.T(ExportAuthor.Required);
            // The transfer window calls this outside Advance, so it opens the
            // batch the save button runs in: the board's records check the
            // source once, and Export hashes it again as it writes.
            ExportRecoverySession recovery;
            using (ExperimentalExpressionCaptureSession.Batch())
                recovery = Save(destination, name, author, (license ?? new AvatarLicenseOptions()).Copy(), interactive, saved);
            return recovery == null ? null : recovery.Failure?.Message ?? "";
        }

        // One save path for the file and the transfer. A transfer (written set)
        // leaves the saved file of this window as it was. interactive decides
        // only the dialogs and the Console text, not what is written.
        ExportRecoverySession Save(string path, string name, string targetAuthor, AvatarLicenseOptions license, bool interactive, Action written)
        {
            if (excludedObjectEdits != null) throw new InvalidOperationException(ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません"));
            if (GimmicksPending) throw new InvalidOperationException(ExporterLocalization.T(GimmicksPendingReason));
            RebuildBoard();
            RecordBoardClips();
            var target = session;
            ExportAuthor.Remember(target.Source, targetAuthor);
            var blink = ExportBlinkOptions();
            var expressions = target.Expressions.Select(ExperimentalExpressionCaptureSession.ClonePose).ToArray();
            var poses = target.PoseOptions.Copy();
            var presets = target.Presets.Values.Select(value => value.Copy()).ToArray();
            var expressionCount = ExperimentalExpressionCaptureSession.MergeIdentical(expressions).Length;
            // The record route replaces the menu faces even when nothing is left to record.
            var replaceCustomFaces = board.UsesRecordRoute;
            // The ordinary route evaluates the menu again and can lose faces the
            // board lists (a non-FX layer writing what a face reads). With no
            // menu face at all, the same save records the list instead; with
            // some, the menu faces (and their motion) are kept.
            var listedFaces = replaceCustomFaces ? 0 : board.OtherCount;
            var recordedList = false;
            // The report of the attempt that produced the bytes being saved.
            ExportFallbackReport savedReport = null;
            // The custom faces of those bytes: the recorded ones, or on the
            // ordinary route the menu faces registered in the VRM.
            var savedFaces = expressionCount;
            void Completed(byte[] bytes, System.Collections.Generic.IEnumerable<string> warnings)
            {
                var missingShaders = ExportResultList.MissingShaders(savedReport);
                // A plain field: still set when the recovery window saves after this board closed.
                LastMissingShaderCount = missingShaders;
                if (written != null)
                {
                    // For a transfer, the details of this export go to the Console.
                    var details = (warnings ?? Enumerable.Empty<string>()).ToArray();
                    // Nothing asked about what a non-interactive save worked around,
                    // so the whole result list goes there once.
                    if (!interactive && savedReport != null && savedReport.Count > 0) Debug.LogWarning(ExportResultList.Build(savedReport, details, path).Text());
                    else if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
                    written();
                    return;
                }
                if (this == null || !ReferenceEquals(session, target)) return;
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                var poseCount = extensions.TryGetValue(VRVlog.Poses.HumanoidPoseData.Extension, out var poses)
                    ? VRVlog.Poses.HumanoidPoseData.Read(poses).Count : 0;
                if (extensions.TryGetValue(VRVlog.Poses.HumanoidAnimationData.Extension, out var animations))
                    poseCount += VRVlog.Poses.HumanoidAnimationData.Read(animations).Count;
                saved = new SavedExpressionVrm(path, bytes, savedFaces, poseCount, missingShaders);
                status = (missingShaders > 0 ? string.Format(ExporterLocalization.T(LilToonExporterWindow.SavedWithMissingShadersFormat), missingShaders)
                    : ExporterLocalization.T("VRMを保存しました。")) + (warnings.Any() ? "\n" + string.Join("\n", warnings) : "");
                Repaint();
            }
            var recovery = LilToonExporterWindow.ExportAndSaveNormally(target.Source, path, (options, report, warnings) => {
                // A retry from the recovery window runs outside any batch; in
                // one attempt the standard faces use the hash Export has taken.
                using var batch = ExperimentalExpressionCaptureSession.Batch();
                // Both transfers let a failing stage follow the project-wide choice,
                // as the export window does, so "かんたん" and "通常" write the same
                // bytes. Saving the board's own VRM (no written) stays strict.
                if (written != null && report != null) report.ContinueOnStageFailure = ExportStageGuard.ContinueByDefault;
                foreach (var warning in target.Warnings) warnings.Add(warning);
                var exported = target.Export(name, targetAuthor, warnings, blink?.Copy(), license.Copy(), options, report, expressions, poses,
                    presets.Select(value => value.Copy()).ToArray(), replaceCustomFaces);
                var menuFaces = !replaceCustomFaces ? VrmMenuExpressions.CountRegistered(exported) : listedFaces;
                if (menuFaces == 0 && listedFaces > 0)
                {
                    var category = ExporterLocalization.T("その他の表情");
                    var resolved = ResolveMenuShort(exported, menuFaces, listedFaces,
                        this == null || !ReferenceEquals(session, target) ? null : (Func<int>)(() => {
                            RebuildBoard();
                            // Set here: a later attempt compares with this count even when this one throws.
                            listedFaces = board.OtherCount;
                            return listedFaces;
                        }),
                        () => {
                            // Set first: a canceled attempt still leaves the list recorded.
                            recordedList = true;
                            return ExportRecorded(board.ExportedCustom(), true);
                        },
                        () => {
                            var again = ExportRecorded(new System.Collections.Generic.List<AnimationClip>(), false);
                            recordedList = false;
                            return again;
                        });
                    exported = resolved.Exported;
                    listedFaces = resolved.ListedFaces;
                    if (resolved.Recorded != null)
                    {
                        warnings.Add(resolved.Recorded);
                        report?.Fallbacks.Record(ExportOutcome.Approximated, category, reason: resolved.Recorded, count: listedFaces);
                    }
                    if (resolved.Error != null) warnings.Add(resolved.Error);
                    if (resolved.Kept != null)
                    {
                        warnings.Add(resolved.Kept);
                        report?.Fallbacks.Record(ExportOutcome.Omitted, category, reason: resolved.Kept, count: Math.Max(0, listedFaces - menuFaces));
                    }
                }
                // Some menu faces but fewer than the list: recording the list would
                // replace the animated menu faces with still ones, so the menu
                // result is kept and the result says some list faces are not in it.
                // A listed clip written as an identical face of another is in it.
                else if (menuFaces + (report?.MergedMenuClips ?? 0) < listedFaces)
                {
                    var kept = string.Format(ExporterLocalization.T(MenuShortKeptMenuFormat), menuFaces, listedFaces);
                    warnings.Add(kept);
                    // One line without a count: listed minus menu is only a lower
                    // bound of the faces left out, so no number is claimed.
                    report?.Fallbacks.Record(ExportOutcome.Omitted, ExporterLocalization.T("その他の表情"), reason: kept);
                }
                // More menu faces than the list: the VRM keeps them all, so the
                // result only says how many it has.
                else if (menuFaces > listedFaces) warnings.Add(string.Format(ExporterLocalization.T(MenuExceedsListFormat), menuFaces, listedFaces));
                savedReport = report?.Fallbacks;
                // Recorded faces leave the menu out; without any the VRM has the menu faces.
                savedFaces = replaceCustomFaces || expressionCount > 0 ? expressionCount : menuFaces;
                return exported;

                // Records the clips on the board and exports this save again;
                // later recovery attempts of the save take the same route.
                byte[] ExportRecorded(System.Collections.Generic.List<AnimationClip> clips, bool replace)
                {
                    var shown = status;
                    RecordClips(clips);
                    status = shown;
                    expressions = target.Expressions.Select(ExperimentalExpressionCaptureSession.ClonePose).ToArray();
                    expressionCount = ExperimentalExpressionCaptureSession.MergeIdentical(expressions).Length;
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

        // The third step of the detailed transfer: this board's third page, where the
        // transfer panel exports from this board when its QR is made.
        void OpenTransferDetails()
        {
            if (excludedObjectEdits != null) throw new InvalidOperationException(ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません"));
            if (GimmicksPending) throw new InvalidOperationException(ExporterLocalization.T(GimmicksPendingReason));
            RebuildBoard();
            if (LilToonExporterWindow.EmbeddedTransferMethod() == null) throw new InvalidOperationException(ExporterLocalization.T("QR対応のパッケージを導入してください。"));
            SetPage(InfoPage);
        }

        // The transfer panel of the optional transfer module for the board's avatar, made once
        // per avatar so the name and consent typed there stay while the user goes back and forth.
        // Null in packages without the transfer.
        UnityEngine.UIElements.VisualElement transferPanel;
        GameObject transferPanelFor;
        bool transferPanelTried;

        // Whether the panel on the third page is uploading or showing its QR. Not when the
        // transfer module cannot be asked.
        bool TransferPanelTransferring
        {
            get
            {
                if (transferPanel?.panel == null) return false;
                try { return Equals(LilToonExporterWindow.EmbeddedTransferringMethod()?.Invoke(null, new object[] { transferPanel }), true); }
                catch (System.Reflection.TargetInvocationException exception) { Debug.LogException(exception.InnerException ?? exception); return false; }
            }
        }

        UnityEngine.UIElements.VisualElement TransferPanel()
        {
            if (transferPanelTried && transferPanelFor == source) return transferPanel;
            transferPanel?.RemoveFromHierarchy();
            transferPanel = null; transferPanelTried = true; transferPanelFor = source;
            try { transferPanel = LilToonExporterWindow.EmbeddedTransferMethod()?.Invoke(null, new object[] { source }) as UnityEngine.UIElements.VisualElement; }
            catch (System.Reflection.TargetInvocationException exception) { Debug.LogException(exception.InnerException ?? exception); }
            if (transferPanel != null) transferPanel.style.position = UnityEngine.UIElements.Position.Absolute;
            return transferPanel;
        }

        // The UI Toolkit panel sits over the area this layout leaves for it, between the
        // steps and the bottom bar; it is placed where the repaint lays the area out.
        void DrawTransferPanel(bool blocked)
        {
            var area = GUILayoutUtility.GetRect(0, 0, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var panel = TransferPanel();
            if (panel == null)
            {
                EditorGUI.HelpBox(new Rect(area.x, area.y, area.width, 40), ExporterLocalization.T("QR対応のパッケージを導入してください。"), MessageType.Error);
                return;
            }
            if (Event.current.type != EventType.Repaint) return;
            if (panel.parent == null) rootVisualElement.Add(panel);
            panel.style.left = area.x; panel.style.top = area.y;
            panel.style.width = area.width; panel.style.height = area.height;
            panel.SetEnabled(!blocked);
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
            // Another avatar starts from blink on, as a new board does; its saved blink applies on preparing.
            automaticBlink = true;
            licenseOptions = new AvatarLicenseOptions();
            status = null;
            page = ExpressionsPage;
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
            excludedObjectEdits = null; excludedObjectError = null;
            dropHover = dropHoverNext = null; clipDropHover = clipDropHoverNext = null; dragMouse = null;
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
