using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The export result list. Before saving it replaces the old yes/no dialog;
    // after saving the same list opens from "書き出しの詳細". Rows and columns
    // have fixed sizes so long names never move the layout.
    internal sealed class ExportResultWindow : EditorWindow
    {
        internal const string WindowTitle = "VR Vlog 書き出し結果";
        internal const string PendingTitle = "まだ保存していません";
        internal const string SavedTitle = "VRMを保存しました";
        // Saved, but a material's shader is missing: the file does not look like the avatar.
        internal const string SavedWithWarningsTitle = "VRMを保存しました（警告あり）";
        internal const string SaveButton = "保存する";
        internal const string ChangeSettingsButton = "設定を変更する";
        internal const string CloseButton = "閉じる";
        internal const string AllTab = "すべて";
        internal const string SearchPlaceholder = "名前で絞り込み";
        internal const string EmptyList = "変えた項目はありません";
        internal const string NoMatch = "条件に合う項目はありません";
        internal const string ReloadedList = "スクリプトの再読み込みで一覧が消えました";
        internal const string ReloadedPending = "保存していません もう一度書き出します";
        internal const string SelectButton = "選択";
        internal const string MissingTarget = "アバターに見つかりません";
        internal const string BlinkButton = "瞬きを設定";

        const float HeaderHeight = 32f, ToolbarHeight = 22f, ColumnHeight = 18f, FooterHeight = 40f;
        const float GroupHeight = 22f, RowHeight = 20f, LineHeight = 18f;
        const float Indent = 24f, OutcomeWidth = 58f, TargetWidth = 150f, ArrowWidth = 18f, LabelWidth = 72f, TabWidth = 78f;
        const string CollapsedKey = "VRVlog.LilToonExporter.ExportResultWindow.Collapsed";

        ExportResultList result;
        GameObject avatar;
        Action save, changeSettings, openBlinkSettings;
        [SerializeField] bool pending, saved;
        [SerializeField] string destination = "";
        [SerializeField] int filter = -1;
        [SerializeField] string query = "";
        [SerializeField] Vector2 scroll;
        [SerializeField] string feedback;
        [SerializeField] bool feedbackFailed;
        readonly HashSet<ExportResultRow> expanded = new HashSet<ExportResultRow>();
        GUIStyle titleStyle, mutedStyle, groupStyle, cellStyle, rightMutedStyle, wrappedStyle;

        internal bool IsPending => pending && !saved;

        internal static ExportResultWindow ShowPending(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings)
        {
            var window = Open(result, avatar, openBlinkSettings);
            window.pending = true; window.saved = false;
            window.save = save; window.changeSettings = changeSettings;
            return window;
        }

        internal static ExportResultWindow ShowSaved(ExportResultList result, GameObject avatar, Action openBlinkSettings)
        {
            var window = Open(result, avatar, openBlinkSettings);
            window.pending = false; window.saved = true;
            window.save = null; window.changeSettings = null;
            return window;
        }

        static ExportResultWindow Open(ExportResultList result, GameObject avatar, Action openBlinkSettings)
        {
            var window = GetWindow<ExportResultWindow>(true, ExporterLocalization.T(WindowTitle), true);
            window.minSize = new Vector2(560f, 420f);
            window.result = result ?? new ExportResultList();
            window.avatar = avatar;
            window.destination = window.result.Destination;
            window.openBlinkSettings = openBlinkSettings;
            window.filter = -1; window.query = ""; window.scroll = Vector2.zero;
            window.feedback = null; window.feedbackFailed = false;
            window.expanded.Clear();
            window.Show();
            window.Focus();
            return window;
        }

        // Writes the file. A failed write keeps the list open with the error.
        internal void Save()
        {
            if (!IsPending || save == null) return;
            try
            {
                save();
                saved = true; pending = false; save = null; changeSettings = null;
                feedback = null; feedbackFailed = false;
            }
            catch (Exception error)
            {
                feedback = ExporterLocalization.T("保存できませんでした") + " " + error.Message;
                feedbackFailed = true;
            }
            Repaint();
        }

        internal void ChangeSettings()
        {
            var back = changeSettings;
            save = null; changeSettings = null; pending = false;
            Close();
            back?.Invoke();
        }

        // Closing with the title bar answers neither; say so instead of dropping the export silently.
        void OnDestroy()
        {
            if (IsPending && save != null) Debug.LogWarning(ExporterLocalization.T(ReloadedPending));
        }

        static HashSet<string> Collapsed() => new HashSet<string>(SessionState.GetString(CollapsedKey, "")
            .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));

        static void SetCollapsed(string group, bool collapsed)
        {
            var names = Collapsed();
            if (collapsed) names.Add(group); else names.Remove(group);
            SessionState.SetString(CollapsedKey, string.Join("\n", names));
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, clipping = TextClipping.Clip };
            mutedStyle = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip, normal = { textColor = Muted } };
            rightMutedStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleRight };
            groupStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            cellStyle = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            wrappedStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
        }

        static Color Muted => EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);
        static Color Hairline => EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.15f);
        static Color GroupBackground => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.04f) : new Color(0f, 0f, 0f, 0.05f);
        static Color OpenBackground => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.07f) : new Color(0f, 0f, 0f, 0.07f);

        // Only omitted and approximated lines carry a colour; the rest stay neutral.
        internal static Color OutcomeColor(ExportOutcome? outcome)
        {
            var pro = EditorGUIUtility.isProSkin;
            if (outcome == ExportOutcome.Omitted) return pro ? new Color(0.93f, 0.66f, 0.34f) : new Color(0.66f, 0.36f, 0.04f);
            if (outcome == ExportOutcome.Approximated) return pro ? new Color(0.48f, 0.70f, 0.95f) : new Color(0.12f, 0.36f, 0.68f);
            return Muted;
        }

        void OnGUI()
        {
            EnsureStyles();
            var width = position.width;
            var header = ExporterWindowHeader.Height;
            ExporterWindowHeader.Draw(new Rect(0f, 0f, width, header), ExporterLocalization.T("書き出し結果"));
            DrawHeader(new Rect(0f, header, width, HeaderHeight));
            DrawToolbar(new Rect(0f, header + HeaderHeight, width, ToolbarHeight));
            DrawColumns(new Rect(0f, header + HeaderHeight + ToolbarHeight, width, ColumnHeight));
            var top = header + HeaderHeight + ToolbarHeight + ColumnHeight;
            DrawList(new Rect(0f, top, width, Mathf.Max(0f, position.height - top - FooterHeight)));
            DrawFooter(new Rect(0f, position.height - FooterHeight, width, FooterHeight));
        }

        // The heading source text. Before saving the rows already show a missing shader.
        internal static string Title(bool saved, ExportResultList result) =>
            !saved ? PendingTitle : result != null && result.MissingShaderCount > 0 ? SavedWithWarningsTitle : SavedTitle;

        void DrawHeader(Rect area)
        {
            var title = ExporterLocalization.T(Title(saved, result));
            var titleWidth = titleStyle.CalcSize(new GUIContent(title)).x;
            var line = new Rect(area.x + 10f, area.y + 6f, area.width - 20f, 20f);
            var right = saved && File.Exists(destination) ? 104f : 0f;
            var titleRect = new Rect(line.x, line.y, Mathf.Min(titleWidth, line.width - right), line.height);
            GUI.Label(titleRect, title, titleStyle);
            var file = Path.GetFileName(destination ?? "");
            if (saved && File.Exists(destination))
                file += "・" + EditorUtility.FormatBytes(new FileInfo(destination).Length);
            GUI.Label(new Rect(titleRect.xMax + 8f, line.y, Mathf.Max(0f, line.width - titleRect.width - 8f - right), line.height),
                new GUIContent(file, destination), mutedStyle);
            if (right > 0f && GUI.Button(new Rect(line.xMax - 100f, line.y, 100f, line.height), ExporterLocalization.T("保存先を開く")))
                EditorUtility.RevealInFinder(destination);
        }

        void DrawToolbar(Rect area)
        {
            GUI.Box(area, GUIContent.none, EditorStyles.toolbar);
            var x = area.x + 6f;
            bool Tab(string label, int value)
            {
                var rect = new Rect(x, area.y, TabWidth, area.height);
                x += TabWidth;
                return GUI.Toggle(rect, filter == value, label, EditorStyles.toolbarButton) && filter != value;
            }
            var total = result == null ? 0 : ExportResultList.Order.Sum(outcome => result.Summary[outcome]) + result.NoticeCount;
            if (Tab(ExporterLocalization.T(AllTab) + " " + total, -1)) filter = -1;
            foreach (var outcome in ExportResultList.Order)
                if (Tab(ExporterLocalization.T(ExportResultList.Label(outcome)) + " " + (result?.Summary[outcome] ?? 0), (int)outcome)) filter = (int)outcome;
            var searchWidth = Mathf.Min(200f, Mathf.Max(80f, area.xMax - x - 12f));
            var search = new Rect(area.xMax - searchWidth - 6f, area.y + 2f, searchWidth, area.height - 4f);
            query = EditorGUI.TextField(search, query ?? "", EditorStyles.toolbarSearchField);
            if (string.IsNullOrEmpty(query) && Event.current.type == EventType.Repaint)
                mutedStyle.Draw(new Rect(search.x + 16f, search.y - 1f, search.width - 20f, search.height + 2f),
                    ExporterLocalization.T(SearchPlaceholder), false, false, false, false);
        }

        float ContentWidth(float width) => Mathf.Max(60f, width - Indent - OutcomeWidth - TargetWidth - ArrowWidth - 16f);

        void DrawColumns(Rect area)
        {
            GUI.Label(new Rect(area.x + Indent, area.y, OutcomeWidth, area.height), ExporterLocalization.T("種類"), mutedStyle);
            GUI.Label(new Rect(area.x + Indent + OutcomeWidth, area.y, ContentWidth(area.width), area.height), ExporterLocalization.T("内容"), mutedStyle);
            GUI.Label(new Rect(area.xMax - TargetWidth - ArrowWidth - 16f, area.y, TargetWidth, area.height), ExporterLocalization.T("対象"), mutedStyle);
            EditorGUI.DrawRect(new Rect(area.x, area.yMax - 1f, area.width, 1f), Hairline);
        }

        IEnumerable<ExportResultRow> Visible(ExportResultGroup group) => group.Rows.Where(row =>
            (filter < 0 || row.Outcome.HasValue && (int)row.Outcome.Value == filter) && row.Matches(query));

        // Lines shown under an opened row. Each is one fixed-height line except
        // the reason, which wraps so no part of the sentence is hidden.
        float DetailHeight(ExportResultRow row, float width)
        {
            var height = 6f;
            if (!string.IsNullOrEmpty(row.Reason)) height += ReasonHeight(row.Reason, width);
            if (ShowsFullText(row, width)) height += ReasonHeight(row.Content, width);
            foreach (var issue in row.Diagnostics) if (issue != null) height += LineHeight * 2f;
            height += row.Targets.Count * LineHeight;
            if (row.Blink && openBlinkSettings != null) height += LineHeight + 4f;
            return height + 6f;
        }

        // Notices always repeat their sentence; other rows only when the line cuts it off.
        bool ShowsFullText(ExportResultRow row, float width) =>
            row.Outcome == null || cellStyle.CalcSize(new GUIContent(row.Content)).x > ContentWidth(width) - 8f;

        float ReasonHeight(string text, float width) =>
            Mathf.Max(LineHeight, wrappedStyle.CalcHeight(new GUIContent(text), Mathf.Max(60f, width - Indent - OutcomeWidth - LabelWidth - 16f)));

        void DrawList(Rect area)
        {
            if (result == null)
            {
                GUI.Label(new Rect(area.x + 10f, area.y + 10f, area.width - 20f, LineHeight),
                    ExporterLocalization.T(pending ? ReloadedPending : ReloadedList), mutedStyle);
                return;
            }
            var collapsed = Collapsed();
            var groups = result.Groups.Select(group => (group, rows: Visible(group).ToArray())).Where(pair => pair.rows.Length > 0).ToArray();
            var inner = area.width - 14f;
            var total = groups.Sum(pair => GroupHeight + (collapsed.Contains(pair.group.Name) ? 0f :
                pair.rows.Sum(row => RowHeight + (expanded.Contains(row) ? DetailHeight(row, inner) : 0f))));
            if (groups.Length == 0)
            {
                var empty = result.IsEmpty ? EmptyList : NoMatch;
                GUI.Label(new Rect(area.x + 10f, area.y + 10f, area.width - 20f, LineHeight), ExporterLocalization.T(empty), mutedStyle);
                return;
            }
            scroll = GUI.BeginScrollView(area, scroll, new Rect(0f, 0f, inner, total));
            var y = 0f;
            foreach (var (group, rows) in groups)
            {
                var header = new Rect(0f, y, inner, GroupHeight);
                EditorGUI.DrawRect(header, GroupBackground);
                EditorGUI.DrawRect(new Rect(0f, header.yMax - 1f, inner, 1f), Hairline);
                var isCollapsed = collapsed.Contains(group.Name);
                var counts = ExportResultList.Counts(group.Rows);
                if (GUI.Button(header, GUIContent.none, GUIStyle.none)) SetCollapsed(group.Name, !isCollapsed);
                GUI.Label(new Rect(6f, y + 2f, 14f, GroupHeight - 4f), isCollapsed ? "▶" : "▼", mutedStyle);
                GUI.Label(new Rect(Indent, y + 2f, inner - Indent - 168f, GroupHeight - 4f), group.Name, groupStyle);
                GUI.Label(new Rect(inner - 166f, y + 2f, 160f, GroupHeight - 4f), counts, rightMutedStyle);
                y += GroupHeight;
                if (isCollapsed) continue;
                foreach (var row in rows)
                {
                    var open = expanded.Contains(row);
                    var line = new Rect(0f, y, inner, RowHeight);
                    if (open) EditorGUI.DrawRect(new Rect(0f, y, inner, RowHeight + DetailHeight(row, inner)), OpenBackground);
                    if (GUI.Button(line, GUIContent.none, GUIStyle.none)) { if (open) expanded.Remove(row); else expanded.Add(row); }
                    DrawRow(line, row, open);
                    y += RowHeight;
                    if (open)
                    {
                        var detail = DetailHeight(row, inner);
                        DrawDetail(new Rect(0f, y, inner, detail), row);
                        y += detail;
                    }
                    EditorGUI.DrawRect(new Rect(Indent, y - 1f, inner - Indent, 1f), Hairline);
                }
            }
            GUI.EndScrollView();
        }

        void DrawRow(Rect line, ExportResultRow row, bool open)
        {
            var outcome = new GUIStyle(cellStyle) { normal = { textColor = OutcomeColor(row.Outcome) } };
            if (row.Outcome == ExportOutcome.Omitted || row.Outcome == ExportOutcome.Approximated) outcome.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(Indent, line.y + 1f, OutcomeWidth, line.height - 2f), ExporterLocalization.T(ExportResultList.Label(row.Outcome)), outcome);
            var content = ContentWidth(line.width);
            GUI.Label(new Rect(Indent + OutcomeWidth, line.y + 1f, content - 8f, line.height - 2f), new GUIContent(row.Content, row.Content), cellStyle);
            var target = row.TargetLabel;
            GUI.Label(new Rect(line.xMax - TargetWidth - ArrowWidth - 6f, line.y + 1f, TargetWidth, line.height - 2f),
                new GUIContent(target, string.Join("\n", row.Targets.Select(item => item.Name))), cellStyle);
            GUI.Label(new Rect(line.xMax - ArrowWidth - 2f, line.y + 1f, ArrowWidth, line.height - 2f), open ? "▼" : "▶", mutedStyle);
        }

        void DrawDetail(Rect area, ExportResultRow row)
        {
            var x = Indent + OutcomeWidth;
            var width = area.width - x - 10f;
            var y = area.y + 6f;
            void Labelled(string label, string text, bool wrap)
            {
                var height = wrap ? ReasonHeight(text, area.width) : LineHeight;
                GUI.Label(new Rect(x, y, LabelWidth, LineHeight), ExporterLocalization.T(label), mutedStyle);
                GUI.Label(new Rect(x + LabelWidth, y, width - LabelWidth, height), new GUIContent(text, text), wrap ? wrappedStyle : cellStyle);
                y += height;
            }
            if (ShowsFullText(row, area.width)) Labelled("全文", row.Content, true);
            if (!string.IsNullOrEmpty(row.Reason)) Labelled("理由", row.Reason, true);
            foreach (var issue in row.Diagnostics)
            {
                if (issue == null) continue;
                Labelled("対処", ExporterLocalization.T(issue.Remedy), false);
                Labelled("変わる点", ExporterLocalization.T(issue.LostEffect), false);
            }
            foreach (var target in row.Targets)
            {
                var found = Find(target);
                // Without a path the name has the whole line.
                var nameWidth = target.Path == null ? width : Mathf.Min(220f, (width - 64f) * 0.45f);
                GUI.Label(new Rect(x, y, nameWidth, LineHeight), new GUIContent(target.Name, target.Name), cellStyle);
                if (target.Path != null)
                {
                    GUI.Label(new Rect(x + nameWidth + 8f, y, width - nameWidth - 72f, LineHeight),
                        new GUIContent(target.Path, target.Path), mutedStyle);
                    using (new EditorGUI.DisabledScope(found == null))
                        if (GUI.Button(new Rect(x + width - 56f, y + 1f, 56f, LineHeight - 2f),
                                new GUIContent(ExporterLocalization.T(SelectButton), found == null ? ExporterLocalization.T(MissingTarget) : target.Path), EditorStyles.miniButton))
                        {
                            Selection.activeGameObject = found;
                            EditorGUIUtility.PingObject(found);
                        }
                }
                y += LineHeight;
            }
            if (row.Blink && openBlinkSettings != null)
            {
                if (GUI.Button(new Rect(x, y + 2f, 96f, LineHeight), ExporterLocalization.T(BlinkButton), EditorStyles.miniButton)) openBlinkSettings();
            }
        }

        // The avatar's own object for a path recorded on the export copy.
        internal GameObject Find(ExportResultTarget target)
        {
            if (avatar == null || target?.Path == null) return null;
            var found = target.Path.Length == 0 ? avatar.transform : avatar.transform.Find(target.Path);
            return found != null ? found.gameObject : null;
        }

        void DrawFooter(Rect area)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), Hairline);
            var y = area.y + 8f;
            var x = area.x + 10f;
            bool Left(string label, float buttonWidth)
            {
                var rect = new Rect(x, y, buttonWidth, 24f);
                x += buttonWidth + 6f;
                return GUI.Button(rect, ExporterLocalization.T(label));
            }
            if (IsPending && result != null)
            {
                if (Left(ChangeSettingsButton, 120f)) { ChangeSettings(); GUIUtility.ExitGUI(); }
                if (Left("全文をコピー", 104f)) Copy();
            }
            else if (result != null)
            {
                if (Left("全文をコピー", 104f)) Copy();
                if (Left("テキスト保存", 104f)) SaveText();
            }
            var primary = IsPending && result != null;
            var buttonWidth = primary ? 112f : 88f;
            var feedbackRect = new Rect(x + 4f, y + 3f, Mathf.Max(0f, area.xMax - 10f - buttonWidth - 8f - x - 4f), 18f);
            if (!string.IsNullOrEmpty(feedback))
            {
                var style = new GUIStyle(mutedStyle);
                if (feedbackFailed) style.normal.textColor = OutcomeColor(ExportOutcome.Omitted);
                GUI.Label(feedbackRect, new GUIContent(feedback, feedback), style);
            }
            var button = new Rect(area.xMax - 10f - buttonWidth, y, buttonWidth, 24f);
            if (primary)
            {
                var background = GUI.backgroundColor;
                GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.6f, 1f) : new Color(0.55f, 0.75f, 1f);
                if (GUI.Button(button, ExporterLocalization.T(SaveButton))) Save();
                GUI.backgroundColor = background;
            }
            else if (GUI.Button(button, ExporterLocalization.T(CloseButton))) { Close(); GUIUtility.ExitGUI(); }
        }

        void Copy()
        {
            ExportDetailsText.Copy(result?.Text(LilToonExporterWindow.PackageVersion()));
            feedback = ExporterLocalization.T("全文をコピーしました");
            feedbackFailed = false;
        }

        void SaveText()
        {
            var text = result?.Text(LilToonExporterWindow.PackageVersion());
            var done = ExportDetailsText.Save(text, () => EditorUtility.SaveFilePanel(
                ExporterLocalization.T("書き出し詳細を保存"), "", "VRVlog-export-details", "txt"), out var error);
            if (done || error != null)
            {
                feedbackFailed = !done;
                feedback = done ? ExporterLocalization.T("テキストを保存しました") : ExporterLocalization.T("テキストを保存できませんでした") + " " + error;
            }
            GUIUtility.ExitGUI();
        }
    }
}
