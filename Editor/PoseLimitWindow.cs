using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Asked once during an export whose poses exceed the app's limits. The
    // list starts at the saved choice or the default; writing is possible only
    // within the limits. Rows and columns have fixed sizes so long names and
    // other fonts never move the layout.
    internal sealed class PoseLimitWindow : EditorWindow
    {
        internal const string WindowTitle = "アプリの上限を超えた項目";
        internal const string Heading = "アプリの上限に合わせて調整します";
        internal const string OverLimit = "ポーズが上限を超えています";
        internal const string Kind = "ポーズ";
        internal const string LimitFormat = "上限 {0}件";
        internal const string DropMoreFormat = "あと{0}件外す";
        internal const string DropsFormat = "{0}件を外します";
        internal const string FramesShortFormat = "動くポーズをあと{0}フレーム減らす";
        internal const string AnimationBytesShortFormat = "動くポーズのデータをあと{0}MB減らす";
        internal const string PoseBytesShortFormat = "静止ポーズのデータをあと{0}MB減らす";
        internal const string FramesLabel = "動くポーズのフレーム合計";
        internal const string AnimationBytesLabel = "動くポーズのデータ量";
        internal const string PoseBytesLabel = "静止ポーズのデータ量";
        internal const string DefaultNote = "既定ではリストの末尾から外します";
        internal const string DefaultButton = "既定に戻す";
        internal const string KeepColumn = "保存";
        internal const string NameColumn = "名前";
        internal const string CategoryColumn = "カテゴリ";
        internal const string KindColumn = "種類";
        internal const string SourceColumn = "登録元";
        internal const string Still = "静止";
        internal const string MovingFormat = "動く {0}秒 {1}フレーム";
        internal const string OverFrameLimitText = "1本の上限8,192フレームを超えています";
        internal const string TooLargeText = "1本でデータの上限16MBを超えています";
        // Short forms for the type cell; the sentences above stay in the tooltip.
        internal const string OverFrameLimitShort = "8,192フレーム超え";
        internal const string TooLargeShort = "16MB超え";
        internal const string CancelButton = "書き出しをやめる";
        internal const string ConfirmButton = "この内容で書き出す";
        internal const string SavedNote = "選んだ内容はこのアバターの書き出し設定に保存します";

        const float TitleHeight = 36f, FooterHeight = 48f, Margin = 12f, CardWidth = 240f, CardHeight = 112f;
        const float SummaryHeight = 92f, ColumnHeight = 22f, RowHeight = 22f;
        const float KeepWidth = 72f, CategoryWidth = 80f, SourceWidth = 100f, ButtonWidth = 168f, DefaultWidth = 136f;
        const double Megabyte = 1024d * 1024d;

        PoseLimitPlan plan;
        bool[] defaults;
        string avatarName = "";
        bool confirmed;
        Vector2 scroll;
        GUIStyle titleStyle, boldStyle, cellStyle, mutedStyle, rightStyle, rightMutedStyle;

        // Blocks until the user answers. Closing the window stops the export.
        internal static bool Ask(PoseLimitPlan plan, GameObject avatar)
        {
            var window = CreateInstance<PoseLimitWindow>();
            window.titleContent = new GUIContent(ExporterLocalization.T(WindowTitle));
            window.Open(plan, avatar != null ? avatar.name : "");
            var main = EditorGUIUtility.GetMainWindowPosition();
            var size = new Vector2(960f, 620f);
            window.minSize = new Vector2(820f, 480f);
            window.position = new Rect(main.center - size / 2f, size);
            window.ShowModal();
            return window.confirmed;
        }

        internal void Open(PoseLimitPlan target, string avatar)
        {
            plan = target;
            avatarName = avatar ?? "";
            var keep = plan.Rows.Select(row => row.Keep).ToArray();
            plan.Default();
            defaults = plan.Rows.Select(row => row.Keep).ToArray();
            for (var i = 0; i < keep.Length; i++) plan.Rows[i].Keep = keep[i];
            // Opens on the rows left out, which sit at the end by default.
            var first = plan.Rows.FindIndex(row => !row.Keep);
            scroll = new Vector2(0f, Mathf.Max(0f, (first - 10) * RowHeight));
        }

        void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, clipping = TextClipping.Clip };
            boldStyle = new GUIStyle(EditorStyles.boldLabel) { clipping = TextClipping.Clip };
            cellStyle = new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            mutedStyle = new GUIStyle(cellStyle) { normal = { textColor = Muted } };
            rightStyle = new GUIStyle(cellStyle) { alignment = TextAnchor.MiddleRight };
            rightMutedStyle = new GUIStyle(mutedStyle) { alignment = TextAnchor.MiddleRight };
        }

        static Color Muted => EditorGUIUtility.isProSkin ? new Color(0.62f, 0.62f, 0.62f) : new Color(0.38f, 0.38f, 0.38f);
        static Color Hairline => EditorGUIUtility.isProSkin ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.15f);
        static Color Stripe => EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, 0.03f) : new Color(0f, 0f, 0f, 0.03f);
        static Color Selected => EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.68f, 0.80f, 0.93f);
        static Color Over => EditorGUIUtility.isProSkin ? new Color(0.94f, 0.42f, 0.36f) : new Color(0.70f, 0.14f, 0.10f);
        static Color Fits => EditorGUIUtility.isProSkin ? new Color(0.50f, 0.78f, 0.45f) : new Color(0.16f, 0.48f, 0.16f);
        // Rows the user changed from the default.
        static Color Changed => EditorGUIUtility.isProSkin ? new Color(0.95f, 0.78f, 0.36f) : new Color(0.60f, 0.42f, 0.02f);

        GUIStyle Colored(GUIStyle style, Color color) => new GUIStyle(style) { normal = { textColor = color } };

        static string T(string text) => ExporterLocalization.T(text);
        static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
        static string Megabytes(long bytes) => (bytes / Megabyte).ToString("0.0", CultureInfo.InvariantCulture);
        static string MegabytesUp(long bytes) => (Math.Ceiling(bytes / Megabyte * 10d) / 10d).ToString("0.0", CultureInfo.InvariantCulture);

        void OnGUI()
        {
            if (plan == null) { Close(); return; }
            EnsureStyles();
            var width = position.width;
            var header = ExporterWindowHeader.Height;
            ExporterWindowHeader.Draw(new Rect(0f, 0f, width, header), T(WindowTitle));
            DrawTitle(new Rect(Margin, header, width - Margin * 2f, TitleHeight));
            var top = header + TitleHeight;
            var height = Mathf.Max(0f, position.height - top - FooterHeight);
            DrawCard(new Rect(Margin, top, CardWidth, height - Margin));
            DrawDetail(new Rect(Margin * 2f + CardWidth, top, Mathf.Max(0f, width - Margin * 3f - CardWidth), height - Margin));
            DrawFooter(new Rect(0f, position.height - FooterHeight, width, FooterHeight));
        }

        void DrawTitle(Rect area)
        {
            var line = new Rect(area.x, area.y + 8f, area.width, 20f);
            var nameWidth = 160f;
            var noticeWidth = 220f;
            GUI.Label(new Rect(line.x, line.y, line.width - nameWidth - noticeWidth - 16f, line.height), T(Heading), titleStyle);
            GUI.Label(new Rect(line.xMax - nameWidth - noticeWidth - 8f, line.y, noticeWidth, line.height), T(OverLimit), plan.Within ? rightMutedStyle : Colored(rightStyle, Over));
            GUI.Label(new Rect(line.xMax - nameWidth, line.y, nameWidth, line.height), new GUIContent(avatarName, avatarName), rightMutedStyle);
        }

        // The kind on the left, as in the other limits the app may add later.
        void DrawCard(Rect area)
        {
            EditorGUI.DrawRect(area, Stripe);
            var card = new Rect(area.x, area.y, area.width, CardHeight);
            EditorGUI.DrawRect(card, Selected);
            var inner = new Rect(card.x + 12f, card.y + 10f, card.width - 24f, 20f);
            var over = plan.CountOver;
            GUI.Label(new Rect(inner.x, inner.y, 80f, inner.height), T(Kind), boldStyle);
            GUI.Label(new Rect(inner.x + 80f, inner.y, inner.width - 80f, inner.height), Number(plan.Rows.Count) + " → " + Number(plan.KeptCount), rightStyle);
            GUI.Label(new Rect(inner.x, inner.y + 22f, inner.width, inner.height),
                string.Format(T(LimitFormat), VRVlog.Poses.HumanoidPoseData.MaximumPoses), rightStyle);
            var bar = new Rect(inner.x, inner.y + 50f, inner.width, 4f);
            var scale = (float)Math.Max(plan.Rows.Count, VRVlog.Poses.HumanoidPoseData.MaximumPoses);
            EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.35f));
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, bar.width * plan.KeptCount / scale, bar.height), over ? Over : Fits);
            EditorGUI.DrawRect(new Rect(bar.x + bar.width * VRVlog.Poses.HumanoidPoseData.MaximumPoses / scale - 1f, bar.y - 4f, 2f, bar.height + 8f), Color.white);
            var status = string.Format(T(DropsFormat), plan.Rows.Count - plan.KeptCount);
            GUI.Label(new Rect(inner.x, inner.y + 64f, inner.width, inner.height), new GUIContent(status, status), cellStyle);
        }

        // The first limit still exceeded, as the next thing to do.
        internal string Shortfall()
        {
            if (plan.CountOver) return string.Format(T(DropMoreFormat), plan.KeptCount - VRVlog.Poses.HumanoidPoseData.MaximumPoses);
            if (plan.FramesOver) return string.Format(T(FramesShortFormat), Number(plan.AnimationFrames - VRVlog.Poses.HumanoidAnimationData.MaximumTotalFrames));
            if (plan.AnimationBytesOver) return string.Format(T(AnimationBytesShortFormat), MegabytesUp(plan.AnimationBytes - VRVlog.Poses.HumanoidAnimationData.MaximumBytes));
            if (plan.PoseBytesOver) return string.Format(T(PoseBytesShortFormat), MegabytesUp(plan.PoseBytes - VRVlog.Poses.HumanoidPoseData.MaximumBytes));
            return "";
        }

        void DrawDetail(Rect area)
        {
            var line = new Rect(area.x, area.y + 4f, area.width, 20f);
            GUI.Label(new Rect(line.x, line.y, 60f, line.height), T(Kind), boldStyle);
            GUI.Label(new Rect(line.x + 60f, line.y, 110f, line.height), Number(plan.Rows.Count) + " → " + Number(plan.KeptCount),
                Colored(cellStyle, plan.CountOver ? Over : Fits));
            GUI.Label(new Rect(line.x + 170f, line.y, 140f, line.height), string.Format(T(LimitFormat), VRVlog.Poses.HumanoidPoseData.MaximumPoses), mutedStyle);
            using (new EditorGUI.DisabledScope(plan.IsDefault))
                if (GUI.Button(new Rect(line.xMax - DefaultWidth, line.y, DefaultWidth, line.height), T(DefaultButton))) plan.Default();

            var third = area.width / 3f;
            var stats = new Rect(area.x, line.y + 26f, third, 38f);
            Stat(stats, FramesLabel, Number(plan.AnimationFrames) + " / " + Number(VRVlog.Poses.HumanoidAnimationData.MaximumTotalFrames), plan.FramesOver);
            stats.x += third;
            Stat(stats, AnimationBytesLabel, Megabytes(plan.AnimationBytes) + " / 16MB", plan.AnimationBytesOver);
            stats.x += third;
            Stat(stats, PoseBytesLabel, Megabytes(plan.PoseBytes) + " / 2MB", plan.PoseBytesOver);
            GUI.Label(new Rect(area.x, line.y + 66f, area.width, 18f), T(DefaultNote), mutedStyle);

            var table = new Rect(area.x, area.y + SummaryHeight, area.width, Mathf.Max(0f, area.height - SummaryHeight));
            DrawTable(table);
        }

        void Stat(Rect rect, string label, string value, bool over)
        {
            var text = T(label);
            // Label over value, so a long label in any language never pushes the value out.
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 12f, 18f), new GUIContent(text, text), mutedStyle);
            GUI.Label(new Rect(rect.x, rect.y + 18f, rect.width - 12f, 18f), value, Colored(cellStyle, over ? Over : Fits));
        }

        void DrawTable(Rect area)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), Hairline);
            var columns = new Rect(area.x, area.y + 1f, area.width, ColumnHeight);
            // Name and kind share what the fixed columns leave.
            var rest = Mathf.Max(160f, area.width - 16f - KeepWidth - CategoryWidth - SourceWidth);
            var nameWidth = Mathf.Floor(rest * 0.42f);
            var kindWidth = rest - nameWidth;
            var selectable = plan.Rows.Where(row => row.Selectable).ToList();
            var all = selectable.Count > 0 && selectable.All(row => row.Keep);
            EditorGUI.showMixedValue = !all && selectable.Any(row => row.Keep);
            var next = EditorGUI.Toggle(new Rect(columns.x + 14f, columns.y + 2f, 18f, 18f), all);
            EditorGUI.showMixedValue = false;
            GUI.Label(new Rect(columns.x + 34f, columns.y + 2f, KeepWidth - 40f, 18f), T(KeepColumn), mutedStyle);
            if (next != all) foreach (var row in selectable) row.Keep = next;
            var x = columns.x + KeepWidth;
            void Header(string label, float columnWidth) { GUI.Label(new Rect(x, columns.y + 2f, columnWidth - 8f, 18f), T(label), mutedStyle); x += columnWidth; }
            Header(NameColumn, nameWidth);
            Header(CategoryColumn, CategoryWidth);
            Header(KindColumn, kindWidth);
            Header(SourceColumn, SourceWidth);
            EditorGUI.DrawRect(new Rect(area.x, columns.yMax, area.width, 1f), Hairline);

            var list = new Rect(area.x, columns.yMax + 1f, area.width, Mathf.Max(0f, area.yMax - columns.yMax - 1f));
            var content = new Rect(0f, 0f, list.width - 16f, plan.Rows.Count * RowHeight);
            scroll = GUI.BeginScrollView(list, scroll, content);
            var first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight));
            var last = Mathf.Min(plan.Rows.Count, Mathf.CeilToInt((scroll.y + list.height) / RowHeight) + 1);
            for (var i = first; i < last; i++) DrawRow(i, new Rect(0f, i * RowHeight, content.width, RowHeight), nameWidth, kindWidth);
            GUI.EndScrollView();
        }

        void DrawRow(int index, Rect rect, float nameWidth, float kindWidth)
        {
            var row = plan.Rows[index];
            if (index % 2 == 1) EditorGUI.DrawRect(rect, Stripe);
            var dim = !row.Keep;
            using (new EditorGUI.DisabledScope(!row.Selectable))
            {
                var keep = GUI.Toggle(new Rect(rect.x + 14f, rect.y + 2f, 18f, 18f), row.Keep, GUIContent.none);
                if (row.Selectable) row.Keep = keep;
            }
            var text = dim ? mutedStyle : cellStyle;
            var changed = row.Selectable && defaults != null && index < defaults.Length && row.Keep != defaults[index];
            var x = rect.x + KeepWidth;
            GUI.Label(new Rect(x, rect.y + 2f, nameWidth - 8f, 18f), new GUIContent(row.Name, row.Name), changed ? Colored(cellStyle, Changed) : text);
            x += nameWidth;
            GUI.Label(new Rect(x, rect.y + 2f, CategoryWidth - 8f, 18f), new GUIContent(row.Category, row.Category), text);
            x += CategoryWidth;
            var kind = KindText(row);
            GUI.Label(new Rect(x, rect.y + 2f, kindWidth - 8f, 18f), new GUIContent(kind, KindTip(row)), row.Selectable ? text : Colored(cellStyle, Over));
            x += kindWidth;
            var source = T(row.Source);
            GUI.Label(new Rect(x, rect.y + 2f, SourceWidth - 8f, 18f), new GUIContent(source, source), text);
        }

        internal static string KindText(PoseLimitPlan.Row row)
        {
            if (row.OverFrameLimit) return T(OverFrameLimitShort);
            if (row.TooLarge) return T(TooLargeShort);
            if (!row.Animated) return T(Still);
            return string.Format(T(MovingFormat), row.Duration.ToString("0.0", CultureInfo.InvariantCulture), Number(row.Frames));
        }

        static string KindTip(PoseLimitPlan.Row row)
        {
            if (row.OverFrameLimit) return T(OverFrameLimitText);
            if (row.TooLarge) return T(TooLargeText);
            return KindText(row);
        }

        void DrawFooter(Rect area)
        {
            EditorGUI.DrawRect(new Rect(area.x, area.y, area.width, 1f), Hairline);
            var y = area.y + 10f;
            if (GUI.Button(new Rect(area.x + Margin, y, 140f, 26f), T(CancelButton))) { confirmed = false; Close(); GUIUtility.ExitGUI(); }
            var button = new Rect(area.xMax - Margin - ButtonWidth, y, ButtonWidth, 26f);
            var noteRect = new Rect(area.x + Margin + 152f, y + 4f, Mathf.Max(0f, button.x - 12f - area.x - Margin - 152f), 18f);
            var within = plan.Within;
            var note = within ? T(SavedNote) : Shortfall();
            GUI.Label(noteRect, new GUIContent(note, note), within ? rightMutedStyle : Colored(rightStyle, Over));
            using (new EditorGUI.DisabledScope(!within))
            {
                var background = GUI.backgroundColor;
                GUI.backgroundColor = EditorGUIUtility.isProSkin ? new Color(0.35f, 0.6f, 1f) : new Color(0.55f, 0.75f, 1f);
                if (GUI.Button(button, T(ConfirmButton))) { confirmed = true; Close(); GUIUtility.ExitGUI(); }
                GUI.backgroundColor = background;
            }
        }
    }
}
