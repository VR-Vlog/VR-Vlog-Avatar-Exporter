using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    public sealed class LilToonExporterWindow : EditorWindow
    {
        // The saved message when materials lost their shader. Also used by the expression board.
        internal const string SavedWithMissingShadersFormat = "VRMを保存しましたが、シェーダーが見つからないマテリアルが{0}個あり、見た目を正しく再現できていません。アバターに必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出してください。";

        private GameObject avatar;
        private string author = "";
        private string outputPath = "";
        private string lastSavedPath, lastSavedSummary;
        private string[] lastSavedWarnings = Array.Empty<string>();
        // The result list of the last saved export. Lost on a script reload;
        // the warnings above then still give a list without the report lines.
        [NonSerialized] private ExportResultList lastSavedResult;
        private bool showAppearanceOptions;
        [SerializeField] private bool showLicenseSettings;
        private bool showEnvironment;
        private bool showBlink;
        // The avatar's remembered choices; ExportSettingsStore saves them as
        // they change, so selecting the avatar again brings them back.
        // ExportSettings is not serialized, so a script reload resets it while
        // the avatar stays; OnEnable loads it again. Until then savedSignature
        // is null and nothing is saved, so the defaults never replace the entry.
        private ExportSettings settings = new ExportSettings();
        [NonSerialized] private string savedSignature;
        [NonSerialized] private int loadedRevision;
        private AvatarLicenseOptions licenseOptions => settings.License;
        private BlinkExportOptions blinkOptions { get => settings.Blink; set => settings.Blink = value; }
        private PoseExportOptions poseOptions => settings.Poses;
        private List<GameObject> excludedObjects => settings.ExcludedObjects;
        private bool autoExcludeGimmicks { get => settings.AutoExcludeGimmicks; set => settings.AutoExcludeGimmicks = value; }
        private List<GameObject> includedGimmicks => settings.IncludedGimmicks;
        private List<Component> includedGimmickComponents => settings.IncludedGimmickComponents;
        private List<ExportGimmickFinding> gimmickFindings = new List<ExportGimmickFinding>();
        private double nextGimmickScan;
        private readonly BlinkStatusCache blinkStatus = new BlinkStatusCache();
        private Vector2 scrollPosition;
        private GUIStyle requiredLabelStyle;
        private GUIStyle hintStyle;
        private GUIStyle centeredHintStyle;
        private GUIStyle placeholderStyle;
        private const string AuthorControlName = "VRVlogExporterAuthor";

        // IMGUI runs for layout, repaint and input. Only the status label may
        // reuse a scan; export and preview still resolve the live avatar.
        internal sealed class BlinkStatusCache
        {
            internal BlinkExportSession Resolved { get; private set; }
            internal string Error { get; private set; }
            private double nextScan = double.NegativeInfinity;

            internal void Refresh(double now, Func<BlinkExportSession> resolve)
            {
                if (now < nextScan) return;
                Invalidate();
                nextScan = now + 1;
                try { Resolved = resolve(); }
                catch (Exception exception) { Error = exception.Message; }
            }

            internal void Invalidate()
            {
                Resolved?.Dispose();
                Resolved = null;
                Error = null;
                nextScan = double.NegativeInfinity;
            }
        }

        private void OnEnable()
        {
            if (avatar != null) LoadSettings(avatar, reportSkipped: false);
        }

        private void OnDisable()
        {
            SaveSettings();
            blinkStatus.Invalidate();
        }

        // Saves what changed (also from the pose window, which edits the same
        // options), and otherwise picks up a save made by another window.
        private void OnInspectorUpdate()
        {
            if (avatar == null || savedSignature == null || GUIUtility.hotControl != 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (settings.Signature() != savedSignature) SaveSettings();
            else if (loadedRevision != ExportSettingsStore.Revision) ReloadSettings();
        }

        // Another window saved. The settings object is kept and only its
        // contents are replaced, because the pose window edits settings.Poses
        // by reference; nothing changes when this avatar's settings are the same.
        private void ReloadSettings()
        {
            var loaded = ExportSettingsStore.Load(avatar);
            loadedRevision = ExportSettingsStore.Revision;
            if (loaded.Signature() == settings.Signature()) return;
            settings.Assign(loaded);
            savedSignature = settings.Signature();
            InvalidateAvatarScan();
            Repaint();
        }

        private void SaveSettings()
        {
            if (avatar == null || savedSignature == null) return;
            var signature = settings.Signature();
            if (signature == savedSignature) return;
            ExportSettingsStore.Save(avatar, settings);
            savedSignature = signature;
            loadedRevision = ExportSettingsStore.Revision;
        }

        // The skipped targets were already reported when the avatar was
        // selected, so a script reload loads them again without a warning.
        private void LoadSettings(GameObject target, bool reportSkipped = true)
        {
            settings = ExportSettingsStore.Load(target, out var skipped);
            savedSignature = settings.Signature();
            loadedRevision = ExportSettingsStore.Revision;
            if (skipped > 0 && reportSkipped) Debug.LogWarning(string.Format(ExporterLocalization.T("保存していた書き出し設定のうち{0}件の対象が見つからないため外しました。"), skipped), target);
            InvalidateAvatarScan();
            Repaint();
        }

        private void InvalidateAvatarScan()
        {
            nextGimmickScan = 0;
            blinkStatus.Invalidate();
        }

        public static void Open()
        {
            var window = GetWindow<LilToonExporterWindow>(true, ExporterLocalization.T("VR Vlog VRM書き出し"));
            window.minSize = new Vector2(430f, 430f);
        }

        private void OnGUI()
        {
            ExporterWindowHeader.Layout(ExporterLocalization.T("VRM書き出し"));
            using (var scroll = new EditorGUILayout.ScrollViewScope(scrollPosition))
            {
                scrollPosition = scroll.scrollPosition;
                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
                {
                    EditorGUILayout.Space(16f);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(16f);
                        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true))) DrawWindow();
                        GUILayout.Space(16f);
                    }
                    EditorGUILayout.Space(16f);
                }
            }
        }

        private void DrawWindow()
        {
            EnsureStyles();
            if (GUILayout.Button(ExporterLocalization.T("表情ファイル・ポーズを指定して書き出す")))
                ExperimentalExpressionCaptureWindow.Open();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(ExporterLocalization.T("保存先を選ぶと、VRMの書き出しと保存まで自動で進みます。"), EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(18f);
            DrawRequiredLabel(ExporterLocalization.T("アバター"), ExporterLocalization.T("Hierarchyにあるアバターの一番上のオブジェクトを指定します。"));
            var selectedAvatar = (GameObject)EditorGUILayout.ObjectField(
                avatar,
                typeof(GameObject),
                true,
                GUILayout.Height(24f));
            if (selectedAvatar != avatar)
            {
                SaveSettings();
                showBlink = false;
                gimmickFindings.Clear();
                LoadSettings(selectedAvatar);
                if (selectedAvatar != null) author = ExportAuthor.Replace(author, ExportAuthor.ForSelection(ExportAuthor.Remembered(selectedAvatar), author));
            }
            avatar = selectedAvatar;
            EditorGUILayout.LabelField(ExporterLocalization.T("Hierarchyからアバターを指定"), hintStyle);

            EditorGUILayout.Space(14f);
            DrawRequiredLabel(ExporterLocalization.T("作者名"), ExporterLocalization.T("VRMファイルに記録される作者名です。"));
            var authorRect = EditorGUILayout.GetControlRect(false, 24f);
            GUI.SetNextControlName(AuthorControlName);
            author = EditorGUI.TextField(authorRect, author);
            if (string.IsNullOrEmpty(author) && GUI.GetNameOfFocusedControl() != AuthorControlName)
                GUI.Label(authorRect, ExporterLocalization.T("作者名を入力"), placeholderStyle);
            // The line is always reserved so typing does not move the rows below.
            EditorGUILayout.LabelField(ExportAuthor.IsValid(author) ? "" : ExporterLocalization.T(ExportAuthor.Required), hintStyle);

            EditorGUILayout.Space(12f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUI.DisabledScope(avatar == null))
                    if (GUILayout.Button(ExporterLocalization.T("ポーズを確認・調整"), GUILayout.Width(160f), GUILayout.Height(26f)))
                        PoseReviewWindow.Show(avatar, poseOptions, excludedObjects.ToArray(),
                            new ExportGimmickOptions { AutoExclude = autoExcludeGimmicks, IncludedObjects = includedGimmicks.ToArray(), IncludedComponents = includedGimmickComponents.ToArray() });
            }

            DrawSeparator();
            showAppearanceOptions = EditorGUILayout.Foldout(showAppearanceOptions, ExporterLocalization.T("書き出し設定"), true);
            if (showAppearanceOptions)
            {
                EditorGUILayout.Space(6f);
                DrawBlink();
                EditorGUILayout.Space(6f);
                DrawGimmicks();
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField(ExporterLocalization.T("書き出さないオブジェクト（ペット・ギミックなど）"));
                EditorGUILayout.HelpBox(ExporterLocalization.T("除外したい子オブジェクトを指定します。ワンクリック書き出しに適用され、Unityの元アバターは変更しません。"), MessageType.None);
                EditorGUI.BeginChangeCheck();
                for (var index = 0; index < excludedObjects.Count; index++)
                {
                    EditorGUILayout.BeginHorizontal();
                    excludedObjects[index] = (GameObject)EditorGUILayout.ObjectField(excludedObjects[index], typeof(GameObject), true);
                    if (GUILayout.Button(ExporterLocalization.T("削除"), GUILayout.Width(48))) { excludedObjects.RemoveAt(index); index--; InvalidateAvatarScan(); }
                    EditorGUILayout.EndHorizontal();
                }
                if (EditorGUI.EndChangeCheck()) InvalidateAvatarScan();
                if (GUILayout.Button(ExporterLocalization.T("除外するオブジェクトを追加"))) { excludedObjects.Add(null); InvalidateAvatarScan(); }
            }

            EditorGUILayout.Space(8f);
            if (settings.License == null) settings.License = new AvatarLicenseOptions();
            AvatarLicenseSettingsUi.Draw(ref showLicenseSettings, licenseOptions);

            EditorGUILayout.Space(14f);
            var canExport = avatar != null && ExportAuthor.IsValid(author);
            using (new EditorGUI.DisabledScope(!canExport))
                if (GUILayout.Button(ExporterLocalization.T("保存先を選んでVRMを書き出す"), GUILayout.Height(40f))) ExportOneClick();
            // Each missing input is named once, under its own field.

            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                // A material without its shader is saved but does not look like the avatar; the saved state says so.
                var missingShaders = lastSavedResult?.MissingShaderCount ?? 0;
                EditorGUILayout.HelpBox((missingShaders > 0 ? string.Format(ExporterLocalization.T(SavedWithMissingShadersFormat), missingShaders) : ExporterLocalization.T("VRMを保存しました。")) +
                    "\n" + System.IO.Path.GetFileName(lastSavedPath) + "\n" + lastSavedSummary, missingShaders > 0 ? MessageType.Warning : MessageType.Info);
                using (new EditorGUILayout.HorizontalScope())
                {
                    var sendSavedVrm = SavedVrmTransferMethod();
                    if (sendSavedVrm != null && GUILayout.Button(ExporterLocalization.T("スマホに送る")))
                    {
                        try { sendSavedVrm.Invoke(null, new object[] { lastSavedPath }); }
                        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException?.Data["VRVlog.ShowMessageAsIs"] is bool asIs && asIs)
                        { EditorUtility.DisplayDialog(ExporterLocalization.T("スマホに送る"), ExporterLocalization.T(exception.InnerException.Message), ExporterLocalization.T("閉じる")); }
                        catch { EditorUtility.DisplayDialog(ExporterLocalization.T("スマホに送る"), ExporterLocalization.T("転送用のコピーを準備できません"), ExporterLocalization.T("閉じる")); }
                    }
                    if (GUILayout.Button(ExporterLocalization.T("保存先を開く"))) EditorUtility.RevealInFinder(lastSavedPath);
                    if ((lastSavedWarnings.Length > 0 || lastSavedResult != null && !lastSavedResult.IsEmpty) &&
                        GUILayout.Button(ExporterLocalization.T("書き出しの詳細")))
                        ExportResultWindow.ShowSaved(lastSavedResult ?? ExportResultList.Build(null, lastSavedWarnings, lastSavedPath), avatar, OpenBlinkSettings);
                    if (AppPreviewEntry.Backend != null && GUILayout.Button(ExporterLocalization.T("アプリでの見え方を確認"))) OpenAppPreview(lastSavedPath);
                }
            }

            DrawSeparator();
            showEnvironment = EditorGUILayout.Foldout(showEnvironment, ExporterLocalization.T("動作環境"), true);
            if (showEnvironment)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("lilToon", ExporterLocalization.T(InstalledLilToonStatus()));
                // The bundled copy the exporter writes with; the project's UniVRM is not used.
                EditorGUILayout.LabelField("UniVRM", string.Format(ExporterLocalization.T("{0}（内蔵）"),
                    VRVlog.LilToonExporter.Vendor.UniGLTF.PackageVersion.VERSION));
            }
        }

        private void EnsureStyles()
        {
            if (requiredLabelStyle != null) return;
            requiredLabelStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(5, 5, 1, 1),
                margin = new RectOffset(6, 0, 0, 0)
            };
            hintStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
                normal = { textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor }
            };
            centeredHintStyle = new GUIStyle(hintStyle) { alignment = TextAnchor.MiddleCenter };
            placeholderStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 5, 0, 0),
                normal = { textColor = EditorStyles.centeredGreyMiniLabel.normal.textColor }
            };
        }

        private void DrawRequiredLabel(string label, string tooltip)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(new GUIContent(label, tooltip), EditorStyles.boldLabel, GUILayout.ExpandWidth(false));
                GUILayout.Label(ExporterLocalization.T("必須"), requiredLabelStyle, GUILayout.ExpandWidth(false));
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.Space(3f);
        }

        private static void DrawSeparator()
        {
            EditorGUILayout.Space(12f);
            var rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.14f)
                : new Color(0f, 0f, 0f, 0.18f));
            EditorGUILayout.Space(10f);
        }

        private BlinkExportSession ResolveBlink()
        {
            using var manual = new ExportObjectExclusions(avatar, excludedObjects);
            var options = new ExportGimmickOptions { AutoExclude = autoExcludeGimmicks, IncludedObjects = includedGimmicks.ToArray(), IncludedComponents = includedGimmickComponents.ToArray() };
            var findings = autoExcludeGimmicks ? ExportGimmickDetection.Analyze(avatar, manual.Contains) : new List<ExportGimmickFinding>();
            using var exclusions = new ExportObjectExclusions(avatar, excludedObjects.Concat(ExportGimmickDetection.AutomaticRoots(findings, options)));
            return BlinkExportSession.Resolve(avatar, blinkOptions, exclusions.Contains);
        }

        private void DrawBlink()
        {
            if (avatar == null) return;
            blinkStatus.Refresh(EditorApplication.timeSinceStartup, ResolveBlink);
            var resolved = blinkStatus.Resolved;
            // An automatic setup that fell back to no blink keeps the old error
            // visible, so the manual choice is still one click away.
            var error = blinkStatus.Error ?? (resolved?.UnresolvedAutomatic == true ? resolved.UnresolvedReason : null);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(ExporterLocalization.T("瞬き"), ExporterLocalization.T(resolved?.Description) ?? ExporterLocalization.T("設定が必要です"));
            if (GUILayout.Button(showBlink ? ExporterLocalization.T("設定を閉じる") : ExporterLocalization.T("確認・調整"), GUILayout.Width(100))) showBlink = !showBlink;
            EditorGUILayout.EndHorizontal();
            if (!showBlink && error == null) return;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var mode = (BlinkExportMode)EditorGUILayout.Popup(ExporterLocalization.T("設定"), (int)blinkOptions.Mode,
                    new[] { ExporterLocalization.T("自動"), ExporterLocalization.T("手動"), ExporterLocalization.T("瞬きなし") });
                if (mode != blinkOptions.Mode)
                {
                    blinkOptions.SelectMode(mode, resolved);
                    blinkStatus.Invalidate();
                    Repaint();
                }
                if (blinkOptions.Mode == BlinkExportMode.Auto && error != null)
                {
                    EditorGUILayout.HelpBox(ExporterLocalization.T(error), MessageType.Warning);
                    EditorGUILayout.LabelField(ExporterLocalization.T("自動では閉眼用の変形を確定できません。手動でメッシュと変形を選択してください。"), EditorStyles.wordWrappedLabel);
                    if (GUILayout.Button(ExporterLocalization.T("手動で閉眼を設定")))
                    {
                        blinkOptions.SelectMode(BlinkExportMode.Manual);
                        showBlink = true;
                        blinkStatus.Invalidate();
                        Repaint();
                    }
                }
                if (blinkOptions.Mode == BlinkExportMode.Manual)
                {
                    EditorGUILayout.LabelField(ExporterLocalization.T("メッシュと閉眼用の変形を選び、適用量を調整してください。"), EditorStyles.wordWrappedLabel);
                    DrawBlinkBindings(ExporterLocalization.T("両目"), blinkOptions.Both);
                    var individual = blinkOptions.Left.Count > 0 || blinkOptions.Right.Count > 0;
                    var next = EditorGUILayout.ToggleLeft(ExporterLocalization.T("左右を個別に設定"), individual);
                    if (next && !individual) { blinkOptions.Left.Add(new BlinkShapeBinding()); blinkOptions.Right.Add(new BlinkShapeBinding()); blinkStatus.Invalidate(); }
                    if (!next && individual) { blinkOptions.Left.Clear(); blinkOptions.Right.Clear(); blinkStatus.Invalidate(); }
                    if (next) { DrawBlinkBindings(ExporterLocalization.T("左目"), blinkOptions.Left); DrawBlinkBindings(ExporterLocalization.T("右目"), blinkOptions.Right); }
                }
                if (error != null && blinkOptions.Mode != BlinkExportMode.Auto) EditorGUILayout.HelpBox(ExporterLocalization.T(error), MessageType.Warning);
                using (new EditorGUI.DisabledScope(error != null))
                    if (GUILayout.Button(ExporterLocalization.T("開閉をプレビュー")))
                        try
                        {
                            BlinkPreviewWindow.Show(avatar, blinkOptions.Copy(), excludedObjects.ToArray(),
                                new ExportGimmickOptions { AutoExclude = autoExcludeGimmicks, IncludedObjects = includedGimmicks.ToArray(), IncludedComponents = includedGimmickComponents.ToArray() });
                        }
                        catch (Exception exception) { ExportFailureWindow.Show(exception); }
            }
        }

        private void DrawBlinkBindings(string label, List<BlinkShapeBinding> bindings)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            var renderers = ExportRendererSelection.Enumerate(avatar).OfType<SkinnedMeshRenderer>()
                .Where(renderer => renderer.sharedMesh != null && renderer.sharedMesh.blendShapeCount > 0).ToArray();
            var rendererChoices = new[] { ExporterLocalization.T("メッシュを選択") }.Concat(renderers.Select(renderer =>
                AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform))).ToArray();
            for (var i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    var renderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(ExporterLocalization.T("メッシュ"), binding.Renderer, typeof(SkinnedMeshRenderer), true);
                    if (renderer != binding.Renderer) { binding.Renderer = renderer; binding.Shape = ""; }
                    if (GUILayout.Button(ExporterLocalization.T("削除"), GUILayout.Width(48))) { bindings.RemoveAt(i--); blinkStatus.Invalidate(); continue; }
                }
                var selectedRenderer = Array.IndexOf(renderers, binding.Renderer);
                var nextRenderer = EditorGUILayout.Popup(ExporterLocalization.T("アバター内のメッシュ"), selectedRenderer + 1, rendererChoices);
                if (nextRenderer > 0 && renderers[nextRenderer - 1] != binding.Renderer)
                {
                    binding.Renderer = renderers[nextRenderer - 1];
                    binding.Shape = "";
                }
                var mesh = binding.Renderer != null ? binding.Renderer.sharedMesh : null;
                if (mesh == null) continue;
                var names = Enumerable.Range(0, mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray();
                var selected = Array.IndexOf(names, binding.Shape);
                var choices = new[] { string.IsNullOrEmpty(binding.Shape) ? ExporterLocalization.T("変形を選択") : ExporterLocalization.T("見つかりません: ") + binding.Shape }.Concat(names).ToArray();
                var next = EditorGUILayout.Popup(ExporterLocalization.T("閉眼用の変形"), selected + 1, choices);
                if (next > 0) binding.Shape = names[next - 1];
                binding.Weight = EditorGUILayout.Slider(ExporterLocalization.T("適用量 (%)"), binding.Weight, 0, 100);
            }
            if (EditorGUI.EndChangeCheck()) blinkStatus.Invalidate();
            if (GUILayout.Button(label + ExporterLocalization.T("の変形を追加"))) { bindings.Add(new BlinkShapeBinding()); blinkStatus.Invalidate(); }
        }

        private void DrawGimmicks()
        {
            EditorGUI.BeginChangeCheck();
            autoExcludeGimmicks = EditorGUILayout.Toggle(ExporterLocalization.T("補助ギミックを自動除外"), autoExcludeGimmicks);
            if (EditorGUI.EndChangeCheck()) InvalidateAvatarScan();
            EditorGUILayout.HelpBox(ExporterLocalization.T("ワンクリック書き出しで、確認できた補助ギミックを省略します。衣装や表情は保持し、Unityの元アバターは変更しません。判定は書き出すたびに更新します。"), MessageType.None);
            if (avatar == null) return;
            if (GUILayout.Button(ExporterLocalization.T("検出一覧を更新"))) InvalidateAvatarScan();
            if (Event.current.type == EventType.Layout && EditorApplication.timeSinceStartup >= nextGimmickScan)
            {
                gimmickFindings = ExportGimmickChoices.Analyze(avatar, excludedObjects);
                nextGimmickScan = EditorApplication.timeSinceStartup + 1;
            }
            foreach (var finding in ExportGimmickChoices.Listed(gimmickFindings))
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(finding.Target, typeof(GameObject), true);
                EditorGUILayout.LabelField(ExporterLocalization.T(finding.Reason), EditorStyles.wordWrappedLabel);
                if (finding.Unit == GimmickExclusionUnit.Review)
                {
                    if (GUILayout.Button("手動の除外一覧に追加") && !excludedObjects.Contains(finding.Target))
                    {
                        excludedObjects.Add(finding.Target);
                        InvalidateAvatarScan();
                    }
                }
                else
                {
                    var inherited = ExportGimmickChoices.Inherited(finding, settings);
                    using (new EditorGUI.DisabledScope(!autoExcludeGimmicks || inherited))
                    {
                        var keep = ExportGimmickChoices.Kept(finding, settings);
                        var selected = EditorGUILayout.ToggleLeft(inherited ? ExporterLocalization.T("親の指定でVRMに入れる") : ExporterLocalization.T("VRMに入れる"), keep || inherited);
                        if (!inherited && selected != keep)
                        {
                            ExportGimmickChoices.SetKept(finding, settings, selected);
                            blinkStatus.Invalidate();
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }
            // Keep exceptions editable if an asset changes and is no longer a
            // candidate, or a formerly safe hierarchy becomes a review item.
            for (var i = includedGimmicks.Count - 1; i >= 0; i--)
            {
                var go = includedGimmicks[i];
                if (go == null) { includedGimmicks.RemoveAt(i); blinkStatus.Invalidate(); continue; }
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(ExporterLocalization.T("含める指定: ") + go.name);
                if (GUILayout.Button(ExporterLocalization.T("解除"), GUILayout.Width(48))) { includedGimmicks.RemoveAt(i); blinkStatus.Invalidate(); }
                EditorGUILayout.EndHorizontal();
            }
            for (var i = includedGimmickComponents.Count - 1; i >= 0; i--)
            {
                var component = includedGimmickComponents[i];
                if (component == null) { includedGimmickComponents.RemoveAt(i); blinkStatus.Invalidate(); continue; }
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(ExporterLocalization.T("含める指定: ") + component.name + " (" + component.GetType().Name + ")");
                if (GUILayout.Button(ExporterLocalization.T("解除"), GUILayout.Width(48))) { includedGimmickComponents.RemoveAt(i); blinkStatus.Invalidate(); }
                EditorGUILayout.EndHorizontal();
            }
        }

        private void ExportOneClick()
        {
            // The disabled button is the normal guard; nothing starts without an author.
            if (!ExportAuthor.IsValid(author)) return;
            try { using var resolved = ResolveBlink(); }
            catch (Exception)
            {
                blinkStatus.Invalidate();
                showAppearanceOptions = true;
                showBlink = true;
                Repaint();
                return;
            }
            outputPath = EditorUtility.SaveFilePanel(ExporterLocalization.T("VRMの保存先"), "", DefaultFileName(), "vrm");
            if (string.IsNullOrEmpty(outputPath)) return;
            lastSavedPath = null;
            lastSavedSummary = null;
            lastSavedWarnings = Array.Empty<string>();
            lastSavedResult = null;
            // A nonmodal failure window may remain open while the user changes
            // this window. Retry exactly the avatar, destination and options
            // they chose for this attempt; revalidate the live avatar each time.
            var targetAvatar = avatar;
            var targetAuthor = author;
            var targetOutput = outputPath;
            SaveSettings();
            var create = settings.Creator(targetAvatar, AvatarName(), targetAuthor);
            var targetExclusions = settings.Exclusions();
            var targetGimmicks = settings.Gimmicks();
            if (File.Exists(targetOutput) && !EditorUtility.DisplayDialog(
                ExporterLocalization.T("ファイルを上書きしますか？"), targetOutput,
                ExporterLocalization.T("上書き"), ExporterLocalization.T("キャンセル"))) return;
            ExportAuthor.Remember(targetAvatar, targetAuthor);
            ExportRecoverySession session = null;
            // The report of the attempt that produced the bytes being saved.
            ExportFallbackReport savedReport = null;
            void ExportCompleted(byte[] bytes, IEnumerable<string> warnings)
            {
                ShowExportCompletion(bytes, warnings, targetOutput, savedReport);
            }
            void Completed() => ExportCompleted(session.LastSuccess.Bytes, session.LastSuccess.Warnings);
            try
            {
                session = ExportAndSaveNormally(targetAvatar, targetOutput, (options, report, warnings) =>
                {
                    var exported = create(options, report, warnings);
                    savedReport = report?.Fallbacks;
                    return exported;
                }, ExportCompleted, targetExclusions, targetGimmicks, interactive: true, openBlinkSettings: OpenBlinkSettings);
                if (session != null) ExportFailureWindow.Show(session, Completed);
            }
            catch (Exception exception) { ExportFailureWindow.Show(exception); }
        }

        // A fresh export has no old diagnosis or preview to invalidate. The
        // exporter works on its own copy; only an actual export failure starts
        // a guarded recovery session. Successful normal exports save directly.
        internal static ExportRecoverySession ExportAndSaveNormally(GameObject source, string destination,
            Func<ExportRecoveryOptions, ExportRecoveryReport, ICollection<string>, byte[]> create,
            Action<byte[], IEnumerable<string>> completed = null,
            IEnumerable<GameObject> previewExcludedObjects = null, ExportGimmickOptions previewGimmicks = null,
            bool interactive = false, Action openBlinkSettings = null)
        {
            if (create == null) throw new ArgumentNullException(nameof(create));
            var warnings = new List<string>();
            var report = new ExportRecoveryReport();
            byte[] bytes;
            // Poses over the app's limits are asked once here; a later retry
            // from the recovery window reuses the choice saved for the avatar.
            try
            {
                using (new PoseLimitChooser.Scope(source, interactive && !ExportFallbackConfirmation.IsBatchMode()))
                    bytes = create(new ExportRecoveryOptions(), report, warnings);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception exception)
            {
                return ExportRecoverySession.FromFailedExport(source, destination, create, exception, report,
                    previewExcludedObjects, previewGimmicks);
            }
            // The export already worked around what it could not set up. Show the
            // result list once, before anything is written, and save from there.
            ExportFallbackConfirmation.Confirm(report.Fallbacks, warnings, destination, source, interactive, () =>
            {
                ExportOutputWriter.Write(destination, bytes);
                completed?.Invoke(bytes, warnings);
            }, openBlinkSettings);
            return null;
        }

        internal static bool ShouldShowFailureAfterFailedAttempt(ExportRecoverySession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            return session.IsInvalidated || !session.WasCanceled;
        }

        private string AvatarName()
        {
            return avatar != null && !string.IsNullOrWhiteSpace(avatar.name) ? avatar.name.Trim() : "avatar";
        }

        private string DefaultFileName()
        {
            var name = AvatarName();
            foreach (var invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '-');
            return name + "-liltoon.vrm";
        }

        // The blink choices live in this window; open their section.
        private void OpenBlinkSettings()
        {
            if (this == null) return;
            showAppearanceOptions = true; showBlink = true;
            Focus(); Repaint();
        }

        private void ShowExportCompletion(byte[] bytes, IEnumerable<string> warnings, string destination, ExportFallbackReport report = null)
        {
            var details = (warnings ?? Array.Empty<string>()).ToArray();
            if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
            var resultWindow = this != null ? this : GetWindow<LilToonExporterWindow>(true, ExporterLocalization.T("VR Vlog VRM書き出し"));
            resultWindow.lastSavedPath = destination;
            resultWindow.lastSavedSummary = string.Format(ExporterLocalization.T("{0:N0}バイト・VRChat表情 {1}件"), bytes.Length, VrmMenuExpressions.CountRegistered(bytes));
            resultWindow.lastSavedWarnings = details;
            resultWindow.lastSavedResult = ExportResultList.Build(report, details, destination);
            resultWindow.Repaint();
        }

        internal static string PackageVersion()
        {
            var info = PackageManagerPackageInfo.FindForAssembly(typeof(LilToonExporterWindow).Assembly);
            return info != null && !string.IsNullOrWhiteSpace(info.version) ? info.version : "0.12.0-beta.7";
        }

        // Stable packages omit this assembly. Resolve only its public optional
        // entry so exporting also works in packages without the transfer module.
        internal static System.Reflection.MethodInfo SavedVrmTransferMethod() =>
            TransferMethod(type => type, "OpenSavedVrmForDevelopment", typeof(string));

        // The second step of the detailed "アプリに転送", in the same optional
        // assembly and namespace as the entry above.
        internal static System.Reflection.MethodInfo DetailedTransferMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferWindow", false), "OpenDetailed", typeof(GameObject));

        // The same step as the expression board's third page: returns the panel as a VisualElement, given
        // the board's "戻る" and whether "VRMを保存" is the blue button. Null without the module, or while
        // it is turned off, and the board then shows its own page without the QR.
        internal static System.Reflection.MethodInfo EmbeddedTransferMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferPanel", false), "CreateEmbedded",
                typeof(GameObject), typeof(Action), typeof(Func<bool>));

        // Whether that panel is uploading or showing its QR, asked before the board leaves its page.
        internal static System.Reflection.MethodInfo EmbeddedTransferringMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferPanel", false), "EmbeddedTransferring", typeof(UnityEngine.UIElements.VisualElement));

        private static System.Reflection.MethodInfo TransferMethod(Func<Type, Type> owner, string name, params Type[] parameters)
        {
            try
            {
                var type = Type.GetType("VRVlog.LilToonExporter.LanTransfer.CloudVrmTransferWindow, VRVlog.LanTransfer.Editor", false);
                if (type == null || !Equals(type.GetProperty("IsAvailable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null), true)) return null;
                return owner(type)?.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, parameters, null);
            }
            catch { return null; }
        }

        // Reads the saved file on click so the export result does not keep the
        // VRM bytes in memory while the window stays open.
        private static void OpenAppPreview(string path)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception)
            {
                EditorUtility.DisplayDialog(ExporterLocalization.T("アプリでの見え方を確認"), ExporterLocalization.T("ファイルを読めません"), ExporterLocalization.T("閉じる"));
                return;
            }
            AppPreviewWindow.Open(bytes, Path.GetFileName(path), path);
        }

        private static string InstalledLilToonStatus()
        {
            var lilToon = Compatibility.LilToonInstallation.Find();
            if (lilToon == null) return ExporterLocalization.T("未インストール（VCC／ALCOMで追加）");
            if (Compatibility.DependencyPolicy.ClassifyLilToon(lilToon.Version) == Compatibility.DependencyPolicy.LilToonSupport.Catalogue)
                return lilToon.Describe(ExporterLocalization.T);
            var version = Compatibility.DependencyPolicy.DisplayLilToon(lilToon.Version, ExporterLocalization.T);
            return lilToon.AssetsFolder == null
                ? string.Format(ExporterLocalization.T("{0}（アプリでは{1}として描画）"), version, Compatibility.DependencyPolicy.LilToonVersion)
                : string.Format(ExporterLocalization.T("{0}（Assetsに導入: {1}・アプリでは{2}として描画）"), version, lilToon.AssetsFolder, Compatibility.DependencyPolicy.LilToonVersion);
        }

        // Stops only without lilToon, or when lilToon's baker that the avatar
        // needs is missing or changed. Any other installed version exports:
        // warnings lists how it differs from the catalogue and what the export
        // leaves out. Returns the installed version for asset.extras;
        // sourceLilToonVersion is always the catalogue version.
        internal static string RequireLilToon(GameObject avatar, ICollection<string> warnings)
        {
            var version = InstalledLilToonVersion();
            var error = Compatibility.DependencyPolicy.LilToonError(version, ExporterLocalization.T);
            if (error != null) throw new InvalidOperationException(error);
            var warning = Compatibility.DependencyPolicy.LilToonWarning(version, ExporterLocalization.T);
            if (warning != null) warnings?.Add(warning);
            LilToonStructureCheck.Inspect(avatar, Compatibility.DependencyPolicy.DisplayLilToon(version, ExporterLocalization.T), warnings);
            return version;
        }

        // The package version, LilToonUnknownVersion for lilToon under Assets
        // without one, or null without lilToon.
        internal static string InstalledLilToonVersion() => Compatibility.LilToonInstallation.Find()?.Version;
    }
}
