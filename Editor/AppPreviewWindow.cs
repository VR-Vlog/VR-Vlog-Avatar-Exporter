using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Shows an exported VRM through the VR Vlog app's own reader package, with
    // the official lilToon Built-in shaders standing in for the app's URP ones.
    // The package is reached only through AppPreviewEntry.Backend; without it
    // the window says so and opens nothing.
    // Every row has a fixed height and the left pane a fixed width, so a longer
    // translation or a larger font clips text instead of moving the panes.
    internal sealed class AppPreviewWindow : EditorWindow
    {
        const string MenuPath = "VR Vlog/アプリでの見え方を確認";
        const float PaneWidth = 280, MaxStep = 1f / 15;
        const int ExpressionRows = 6, AnimationRows = 4;
        const string StateKey = "VRVlog.LilToonExporter.AppPreview.";
        const string PinnedLilToon = "2.3.4";
        // The sway: ±0.25 m sideways as a 0.8 s sine, two cycles.
        const float SwayDistance = .25f, SwayPeriod = .8f, SwayCycles = 2;
        // A blink test signal for this window only; the app does not use it.
        const float BlinkInterval = 4, BlinkClose = .1f, BlinkHold = .05f, BlinkOpen = .15f;

        static string exporterVersion;
        static GUIStyle clipped, centered, right, heading, foldout, radio, warning;

        IAppPreviewSession load;
        PreviewRenderUtility preview;
        bool loadedStandardMToon;
        // The VRM stays in memory only. A script reload drops it and keeps the
        // file's name, path and error line, so OnEnable can offer to load again.
        [NonSerialized] byte[] bytes;
        [SerializeField] string fileName, filePath;
        [SerializeField] string errorText, errorDetail;
        string lilToonVersion;
        int revision;
        bool loading, visible = true, autoBlink, springsEnabled = true, stepFailed;
        double loadStarted, lastTick, lastLoadingRepaint;
        CancellationTokenSource cancel;
        string expression, playingAnimation;
        float blinkAmount, blinkClock, swayTime = -1;
        Vector3 origin, center;
        float distance = 1, zoom = 1, yaw, paneHeight = 1000;
        Vector2 paneScroll, expressionScroll, animationScroll;

        internal IAppPreviewSession Load => load;
        internal string Error => errorText;
        internal Task LastLoad { get; private set; } = Task.CompletedTask;

        static string T(string source) => ExporterLocalization.T(source);

        [MenuItem(MenuPath, true)]
        internal static bool CanOpen() => AppPreviewEntry.Backend != null;

        [MenuItem(MenuPath)]
        static void OpenFromMenu()
        {
            if (!CanOpen()) return;
            var path = EditorUtility.OpenFilePanel(T("VRM ファイルを開く"), "", "vrm");
            if (!string.IsNullOrEmpty(path)) Window().LoadFile(path);
        }

        // The path, when known, lets the window read the file again after a script reload.
        internal static AppPreviewWindow Open(byte[] data, string name, string path = null)
        {
            var window = Window();
            window.LastLoad = window.LoadAsync(data, name, path);
            return window;
        }

        static AppPreviewWindow Window()
        {
            var window = GetWindow<AppPreviewWindow>();
            window.titleContent = new GUIContent(T("アプリでの見え方"));
            window.minSize = new Vector2(900, 600);
            window.Show();
            return window;
        }

        // False shows the app's materials, true only the VRM's MToon.
        static bool StandardMToon
        {
            get => SessionState.GetInt(StateKey + "MaterialMode", 0) == 1;
            set => SessionState.SetInt(StateKey + "MaterialMode", value ? 1 : 0);
        }

        void LoadFile(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception exception)
            {
                Release();
                bytes = null; fileName = Path.GetFileName(path); filePath = null;
                errorText = T("ファイルを読めません"); errorDetail = exception.Message;
                Repaint();
                return;
            }
            LastLoad = LoadAsync(data, Path.GetFileName(path), path);
        }

        internal bool CanReload => bytes != null || filePath != null;

        // The bytes in memory, or the file again after a script reload dropped them.
        internal void Reload()
        {
            if (bytes != null) LastLoad = LoadAsync(bytes, fileName, filePath);
            else if (filePath != null) LoadFile(filePath);
        }

        // Never throws: a failure becomes the error state.
        internal async Task LoadAsync(byte[] data, string name, string path = null)
        {
            Release();
            var mine = revision;
            bytes = data; fileName = name; filePath = path;
            errorText = errorDetail = null;
            var backend = AppPreviewEntry.Backend;
            if (backend == null)
            {
                errorText = T("VR Vlog アプリの読み込みパッケージがありません");
                Repaint();
                return;
            }
            cancel = new CancellationTokenSource();
            loading = true; loadStarted = EditorApplication.timeSinceStartup;
            loadedStandardMToon = StandardMToon;
            lilToonVersion = LilToonExporterWindow.FindLilToonPackage()?.version;
            IAppPreviewSession result = null;
            string failure = null, detail = null;
            try
            {
                var started = backend.LoadAsync(data, name, loadedStandardMToon, cancel.Token);
                Repaint();
                result = await started;
            }
            catch (Exception exception) { failure = T("VRM として読み込めません"); detail = exception.Message; }
            // Closed, cancelled or replaced while loading: release what arrived late.
            if (this == null || mine != revision) { result?.Dispose(); return; }
            loading = false;
            if (failure == null && result == null) failure = T("VRM として読み込めません");
            else if (failure == null && result.Root == null)
            {
                failure = result.MaterialStatus == "failed-memory" ? T("メモリが足りず読み込めません") : ErrorText(result.ErrorCode);
                detail = string.Join(" ", result.ErrorCode, result.ErrorMessage).Trim();
                result.Dispose();
            }
            else if (failure == null)
            {
                try { Attach(result); }
                catch (Exception exception)
                {
                    Release();
                    failure = exception is InvalidOperationException ? T("表示するメッシュがありません") : T("VRM として読み込めません");
                    detail = exception.Message;
                }
            }
            errorText = failure; errorDetail = detail;
            Repaint();
        }

        void Attach(IAppPreviewSession result)
        {
            load = result;
            var root = result.Root;
            root.hideFlags = HideFlags.HideAndDontSave;
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.updateWhenOffscreen = true;
                skin.forceMatrixRecalculationPerRender = true;
            }
            origin = Vector3.zero;
            root.transform.position = origin;
            var bounds = ExportRecoveryComparisonWindow.VisibleBounds(root);
            center = bounds.center;
            distance = Mathf.Max(.5f, bounds.size.magnitude * 1.8f);
            preview = ExportRecoveryComparisonWindow.CreateRenderer(root);
            lastTick = 0; blinkClock = 0; stepFailed = false;
            load.SetSpringBonesEnabled(springsEnabled);
            if (!autoBlink && blinkAmount > 0) load.SetEyeBlink(blinkAmount, blinkAmount);
        }

        void Cancel()
        {
            Release();
            errorText = T("読み込んでいません"); errorDetail = null;
        }

        void Release()
        {
            revision++;
            loading = false;
            cancel?.Cancel(); cancel?.Dispose(); cancel = null;
            // The load owns Root. PreviewRenderUtility.Cleanup closes the preview
            // scene, which would destroy Root before the load releases the rest.
            load?.Dispose(); load = null;
            preview?.Cleanup(); preview = null;
            expression = playingAnimation = null;
            swayTime = -1;
        }

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            lilToonVersion = LilToonExporterWindow.FindLilToonPackage()?.version;
            // After a script reload OnDisable has released the load; say so
            // instead of showing only the open button.
            if (fileName != null && load == null && !loading && errorText == null)
            {
                errorText = T("読み込んでいません"); errorDetail = null;
            }
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Release();
        }

        void OnBecameVisible() { visible = true; lastTick = 0; }
        void OnBecameInvisible() => visible = false;

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode && change != PlayModeStateChange.ExitingPlayMode) return;
            var hadFile = loading || load != null;
            Release();
            if (hadFile) { errorText = T("読み込んでいません"); errorDetail = null; }
            Repaint();
        }

        void OnEditorUpdate()
        {
            var now = EditorApplication.timeSinceStartup;
            if (loading)
            {
                if (now - lastLoadingRepaint > .25) { lastLoadingRepaint = now; Repaint(); }
                return;
            }
            if (load == null || load.Root == null || preview == null || stepFailed || !visible || position.width < 1 || position.height < 1)
            {
                lastTick = 0;
                return;
            }
            if (lastTick <= 0) { lastTick = now; return; }
            var deltaTime = Mathf.Min((float)(now - lastTick), MaxStep);
            lastTick = now;
            if (deltaTime <= 0) return;
            try { Step(deltaTime); }
            catch (Exception exception)
            {
                // Stop stepping instead of logging the same failure every frame.
                stepFailed = true;
                Debug.LogException(exception);
            }
            Repaint();
        }

        internal void Step(float deltaTime)
        {
            if (swayTime >= 0)
            {
                swayTime += deltaTime;
                var done = swayTime >= SwayPeriod * SwayCycles;
                load.Root.transform.position = done ? origin
                    : origin + Vector3.right * (SwayDistance * Mathf.Sin(2 * Mathf.PI * swayTime / SwayPeriod));
                if (done) swayTime = -1;
            }
            if (autoBlink)
            {
                blinkClock = (blinkClock + deltaTime) % BlinkInterval;
                var closure = BlinkClosure(blinkClock);
                load.SetEyeBlink(closure, closure);
            }
            // Expressions and their animations advance every frame; spring bones
            // only move while enabled (SetSpringBonesEnabled). A zero step is never sent.
            load.Tick(deltaTime);
            // A single play ends on its own, and the button turns back to play.
            var playing = load.PlayingExpressionAnimation;
            if (playing != playingAnimation)
            {
                playingAnimation = playing;
                Repaint();
            }
        }

        internal static float BlinkClosure(float time)
        {
            if (time < BlinkClose) return time / BlinkClose;
            time -= BlinkClose;
            if (time < BlinkHold) return 1;
            time -= BlinkHold;
            return time < BlinkOpen ? 1 - time / BlinkOpen : 0;
        }

        void ReturnToOrigin()
        {
            swayTime = -1;
            if (load?.Root == null) return;
            load.Root.transform.position = origin;
            load.ResetSpringBones();
        }

        internal void SelectExpression(string name)
        {
            load.ClearExpressions();
            if (name != null) load.SetExpression(name, 1);
            expression = name;
            // Clearing also stops the expression animation.
            playingAnimation = null;
            // In VRM 1.0 the blink is an expression too, so clearing can open
            // eyes the slider still shows closed. Auto blink sets it every frame.
            if (!autoBlink) load.SetEyeBlink(blinkAmount, blinkAmount);
        }

        internal void SetBlinkAmount(float amount)
        {
            blinkAmount = amount;
            load.SetEyeBlink(amount, amount);
        }

        internal void PlayAnimation(string name)
        {
            load.PlayExpressionAnimation(name);
            playingAnimation = name;
        }

        void OnGUI()
        {
            CreateStyles();
            ExporterWindowHeader.Draw(new Rect(0, 0, position.width, ExporterWindowHeader.Height), T("アプリでの見え方"));
            var toolbarHeight = EditorGUIUtility.singleLineHeight + 3;
            DrawToolbar(new Rect(0, ExporterWindowHeader.Height, position.width, toolbarHeight));
            var top = ExporterWindowHeader.Height + toolbarHeight;
            var height = Mathf.Max(0, position.height - top);
            DrawPane(new Rect(0, top, PaneWidth, height));
            EditorGUI.DrawRect(new Rect(PaneWidth - 1, top, 1, height), new Color(0, 0, 0, .25f));
            DrawPreview(new Rect(PaneWidth, top, Mathf.Max(0, position.width - PaneWidth), height));
        }

        static void CreateStyles()
        {
            if (clipped != null) return;
            clipped = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip, wordWrap = false };
            centered = new GUIStyle(clipped) { alignment = TextAnchor.MiddleCenter };
            right = new GUIStyle(clipped) { alignment = TextAnchor.MiddleRight };
            heading = new GUIStyle(clipped) { fontStyle = FontStyle.Bold };
            foldout = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, wordWrap = false };
            radio = new GUIStyle(EditorStyles.radioButton) { clipping = TextClipping.Clip, wordWrap = false };
            warning = new GUIStyle(clipped);
            warning.normal.textColor = EditorGUIUtility.isProSkin ? new Color(1f, .75f, .3f) : new Color(.6f, .35f, 0f);
        }

        void DrawToolbar(Rect rect)
        {
            GUI.Box(rect, GUIContent.none, EditorStyles.toolbar);
            const float versionWidth = 96;
            var inner = new Rect(rect.x, rect.y + 1, rect.width, rect.height - 2);
            GUI.Label(new Rect(6, inner.y, PaneWidth - 12, inner.height), new GUIContent(fileName ?? "", fileName), clipped);
            GUI.Label(new Rect(PaneWidth + 6, inner.y, Mathf.Max(0, rect.width - PaneWidth - versionWidth - 18), inner.height), ToolbarResult(), clipped);
            exporterVersion ??= LilToonExporterWindow.PackageVersion();
            GUI.Label(new Rect(rect.xMax - versionWidth - 6, inner.y, versionWidth, inner.height), "v" + exporterVersion, right);
        }

        GUIContent ToolbarResult()
        {
            if (loading) return GUIContent.none;
            if (load != null) return new GUIContent(MaterialSummary(load.MaterialStatus), load.MaterialStatus);
            return errorText != null ? new GUIContent(errorText, errorDetail) : GUIContent.none;
        }

        void DrawPane(Rect rect)
        {
            var line = EditorGUIUtility.singleLineHeight;
            var width = rect.width - 16;
            paneScroll = GUI.BeginScrollView(rect, paneScroll, new Rect(0, 0, width, paneHeight));
            var y = 4f;
            var rowWidth = width - 12;
            Rect Row(int rows = 1)
            {
                var value = new Rect(8, y, rowWidth, rows * line + (rows - 1) * 2);
                y += rows * (line + 2);
                return value;
            }
            bool Section(string key, string title)
            {
                var open = SessionState.GetBool(StateKey + key, true);
                var next = EditorGUI.Foldout(new Rect(4, y, width - 8, line), open, title, true, foldout);
                y += line + 2;
                if (next != open) SessionState.SetBool(StateKey + key, next);
                return next;
            }
            var ready = load != null && !loading;

            if (Section("Material", T("材質")))
            {
                using (new EditorGUI.DisabledScope(loading))
                {
                    var mtoonOnly = StandardMToon;
                    var app = GUI.Toggle(Row(), !mtoonOnly, T("アプリと同じ"), radio);
                    var mtoon = GUI.Toggle(Row(), mtoonOnly, T("MToon のみ"), radio);
                    var selected = app && mtoonOnly ? false : mtoon && !mtoonOnly ? true : mtoonOnly;
                    if (selected != mtoonOnly)
                    {
                        StandardMToon = selected;
                        if (CanReload) Reload();
                    }
                }
                var status = Row();
                if (load != null) GUI.Label(status, new GUIContent(MaterialDetail(load.MaterialStatus, loadedStandardMToon), load.MaterialStatus), clipped);
                GUI.Label(Row(), lilToonVersion == null ? T("lilToon がありません") : "lilToon " + lilToonVersion, clipped);
                var mismatch = Row();
                if (lilToonVersion != null && lilToonVersion != PinnedLilToon) GUI.Label(mismatch, T("lilToon 2.3.4 とバージョンが違います"), warning);
            }
            y += 6;

            if (Section("Expression", T("表情")))
            {
                using (new EditorGUI.DisabledScope(!ready))
                {
                    var names = load?.Expressions ?? Array.Empty<string>();
                    var area = Row(ExpressionRows);
                    if (load != null && names.Count == 0) GUI.Label(new Rect(area.x, area.y, area.width, line), T("表情はありません"), clipped);
                    else if (load != null)
                    {
                        var content = new Rect(0, 0, area.width - 16, (names.Count + 1) * (line + 2));
                        expressionScroll = GUI.BeginScrollView(area, expressionScroll, content);
                        for (var i = 0; i <= names.Count; i++)
                        {
                            var name = i == 0 ? null : names[i - 1];
                            var label = name == null ? new GUIContent(T("なし")) : new GUIContent(name, name);
                            var on = expression == name;
                            if (GUI.Toggle(new Rect(2, i * (line + 2), content.width - 4, line), on, label, radio) && !on) SelectExpression(name);
                        }
                        GUI.EndScrollView();
                    }
                    GUI.Label(Row(), T("表情アニメーション"), heading);
                    var animations = load?.ExpressionAnimations ?? Array.Empty<string>();
                    area = Row(AnimationRows);
                    if (load != null && animations.Count == 0) GUI.Label(new Rect(area.x, area.y, area.width, line), T("表情アニメーションはありません"), clipped);
                    else if (load != null)
                    {
                        const float buttonWidth = 56;
                        var content = new Rect(0, 0, area.width - 16, animations.Count * (line + 2));
                        animationScroll = GUI.BeginScrollView(area, animationScroll, content);
                        for (var i = 0; i < animations.Count; i++)
                        {
                            var name = animations[i];
                            var rowY = i * (line + 2);
                            GUI.Label(new Rect(2, rowY, content.width - buttonWidth - 8, line), new GUIContent(name, name), clipped);
                            var playing = playingAnimation == name;
                            if (GUI.Button(new Rect(content.width - buttonWidth - 2, rowY, buttonWidth, line), playing ? T("停止") : T("再生")))
                                PlayAnimation(playing ? null : name);
                        }
                        GUI.EndScrollView();
                    }
                }
            }
            y += 6;

            if (Section("Blink", T("まばたき")))
            {
                using (new EditorGUI.DisabledScope(!ready))
                {
                    var automatic = GUI.Toggle(Row(), autoBlink, T("自動でまばたき"));
                    if (automatic != autoBlink)
                    {
                        autoBlink = automatic; blinkClock = 0;
                        if (!automatic) load.SetEyeBlink(blinkAmount, blinkAmount);
                    }
                    using (new EditorGUI.DisabledScope(autoBlink))
                    {
                        var labelWidth = EditorGUIUtility.labelWidth;
                        EditorGUIUtility.labelWidth = 110;
                        var amount = EditorGUI.Slider(Row(), T("目の閉じ具合"), blinkAmount, 0, 1);
                        EditorGUIUtility.labelWidth = labelWidth;
                        if (!Mathf.Approximately(amount, blinkAmount)) SetBlinkAmount(amount);
                    }
                }
            }
            y += 6;

            if (Section("Spring", T("揺れもの")))
            {
                var hasSprings = load != null && load.HasSpringBones;
                using (new EditorGUI.DisabledScope(!ready || !hasSprings))
                {
                    var enabled = GUI.Toggle(Row(), springsEnabled, T("揺れものを動かす"));
                    if (enabled != springsEnabled)
                    {
                        springsEnabled = enabled;
                        load.SetSpringBonesEnabled(enabled);
                        lastTick = 0;
                        if (!enabled) ReturnToOrigin();
                    }
                    var buttons = Row();
                    var half = (buttons.width - 4) / 2;
                    using (new EditorGUI.DisabledScope(!springsEnabled))
                        if (GUI.Button(new Rect(buttons.x, buttons.y, half, buttons.height), T("左右に揺らす"))) swayTime = 0;
                    if (GUI.Button(new Rect(buttons.x + half + 4, buttons.y, half, buttons.height), T("元の位置に戻す"))) ReturnToOrigin();
                }
                var note = Row();
                if (load != null && !hasSprings) GUI.Label(note, T("揺れものはありません"), clipped);
            }
            paneHeight = y + 4;
            GUI.EndScrollView();
        }

        void DrawPreview(Rect rect)
        {
            if (rect.width < 1 || rect.height < 1) return;
            if (load != null && preview != null)
            {
                HandleInput(rect);
                if (Event.current.type == EventType.Repaint) Render(rect);
                return;
            }
            var line = EditorGUIUtility.singleLineHeight;
            var middle = rect.center.y;
            if (loading)
            {
                const float labelWidth = 200, buttonWidth = 80;
                var x = rect.center.x - (labelWidth + 8 + buttonWidth) / 2;
                var elapsed = (int)(EditorApplication.timeSinceStartup - loadStarted);
                GUI.Label(new Rect(x, middle - line / 2, labelWidth, line), string.Format(T("読み込み中 {0} 秒"), elapsed), centered);
                if (GUI.Button(new Rect(x + labelWidth + 8, middle - line / 2, buttonWidth, line), T("中止"))) Cancel();
                return;
            }
            if (AppPreviewEntry.Backend == null)
            {
                GUI.Label(new Rect(rect.x + 20, middle - line / 2, rect.width - 40, line), T("VR Vlog アプリの読み込みパッケージがありません"), centered);
                return;
            }
            if (errorText != null)
            {
                GUI.Label(new Rect(rect.x + 20, middle - line - 4, rect.width - 40, line), new GUIContent(errorText, errorDetail), centered);
                var button = new Rect(rect.center.x - 100, middle + 4, 200, line);
                if (CanReload) { if (GUI.Button(button, T("もう一度読み込む"))) Reload(); }
                else if (GUI.Button(button, T("VRM ファイルを開く"))) OpenFromMenu();
                return;
            }
            if (GUI.Button(new Rect(rect.center.x - 110, middle - line, 220, line * 2), T("VRM ファイルを開く"))) OpenFromMenu();
        }

        void HandleInput(Rect rect)
        {
            var current = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);
            switch (current.GetTypeForControl(id))
            {
                case EventType.MouseDown when current.button == 0 && rect.Contains(current.mousePosition):
                    GUIUtility.hotControl = id;
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    yaw += current.delta.x * .5f;
                    current.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
                case EventType.ScrollWheel when rect.Contains(current.mousePosition):
                    zoom = Mathf.Clamp(zoom * (1 + current.delta.y * .05f), .2f, 4);
                    current.Use();
                    Repaint();
                    break;
            }
        }

        void Render(Rect rect)
        {
            var cameraDistance = distance * zoom;
            preview.BeginPreview(rect, GUIStyle.none);
            preview.camera.farClipPlane = Mathf.Max(100, cameraDistance * 3);
            preview.camera.transform.position = center + Quaternion.Euler(0, yaw, 0) * Vector3.forward * cameraDistance;
            preview.camera.transform.LookAt(center, Vector3.up);
            preview.Render();
            GUI.DrawTexture(rect, preview.EndPreview(), ScaleMode.ScaleToFit, false);
        }

        static bool IsFullLilToon(string status) =>
            status.StartsWith("full-liltoon:", StringComparison.Ordinal);

        static int Count(string status, string key)
        {
            var start = status.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return 0;
            start += key.Length;
            var end = start;
            while (end < status.Length && char.IsDigit(status[end])) end++;
            return int.TryParse(status.Substring(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;
        }

        // The toolbar's words for the reader's material result.
        internal static string MaterialSummary(string status)
        {
            status = status ?? "";
            if (IsFullLilToon(status)) return Count(status, "material-fallbacks:") > 0 ? T("一部を MToon で表示") : T("lilToon で表示");
            if (status.StartsWith("approximated-mtoon:", StringComparison.Ordinal)) return T("MToon で近似表示");
            if (status == "failed-memory") return T("メモリが足りず読み込めません");
            if (status == "standard-mtoon" || status.Contains("low-power") || status.Contains("vrm0") ||
                status.StartsWith("fallback", StringComparison.Ordinal)) return T("MToon で表示");
            return status.Length == 0 || status == "detecting-materials" || status == "enabled-pending" ? T("材質を判定できません") : status;
        }

        // The left pane's line; the raw code is the tooltip.
        internal static string MaterialDetail(string status, bool standardMToon)
        {
            status = status ?? "";
            if (IsFullLilToon(status))
            {
                var kept = Count(status, "material-fallbacks:");
                return kept > 0 ? string.Format(T("{0} 件を MToon で表示しています"), kept) : T("lilToon で表示しています");
            }
            if (status.StartsWith("approximated-mtoon:", StringComparison.Ordinal)) return T("lilToon の設定を MToon で近似しています");
            if (status == "failed-memory") return T("メモリが足りず読み込めません");
            if (status.Contains("low-power")) return T("省電力モードのため MToon で表示しています");
            if (status == "standard-mtoon")
                return standardMToon ? T("VRM の MToon で表示しています") : T("lilToon の追加データがありません");
            if (status.Contains("vrm0")) return T("VRM 0.x のため MToon で表示しています");
            if (status.StartsWith("fallback", StringComparison.Ordinal)) return T("lilToon を適用できず MToon で表示しています");
            return status.Length == 0 || status == "detecting-materials" || status == "enabled-pending" ? T("材質を判定できません") : status;
        }

        internal static string ErrorText(string code)
        {
            code = code ?? "";
            if (code == "file-missing") return T("ファイルが見つかりません");
            if (code == "VRM-json-unsafe") return T("VRM の JSON を読めません");
            if (code.StartsWith("VRM10-", StringComparison.Ordinal)) return T("VRM 1.0 として読み込めません");
            if (code.StartsWith("VRM0-", StringComparison.Ordinal)) return T("VRM 0.x として読み込めません");
            return T("VRM として読み込めません");
        }
    }
}
