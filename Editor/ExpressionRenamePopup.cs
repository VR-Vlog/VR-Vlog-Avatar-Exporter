using System;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The small box under a board card that renames its face. Its size is
    // fixed: long names and messages are cut with "…" and shown in full on hover.
    internal sealed class ExpressionRenamePopup : PopupWindowContent
    {
        const string Field = "VRVlogExpressionName";
        const float Width = 320, Height = 124, Pad = 8;
        readonly string fileName;
        readonly Func<string, string> problem;
        readonly Action<string> apply;
        readonly bool renamed;
        string text;
        bool focused;
        GUIStyle error;

        // problem returns why a name cannot be used, already localized, or null.
        internal ExpressionRenamePopup(string current, string fileName, bool renamed, Func<string, string> problem, Action<string> apply)
        {
            text = current ?? "";
            this.fileName = fileName ?? "";
            this.renamed = renamed;
            this.problem = problem ?? throw new ArgumentNullException(nameof(problem));
            this.apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public override Vector2 GetWindowSize() => new Vector2(Width, Height);

        public override void OnGUI(Rect rect)
        {
            if (error == null)
            {
                error = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, wordWrap = false };
                error.normal.textColor = EditorGUIUtility.isProSkin ? new Color32(0xe0, 0x67, 0x5f, 0xff) : new Color32(0xb3, 0x26, 0x1e, 0xff);
            }
            var current = Event.current;
            // Enter and Esc are read before the text field takes the key.
            if (current.type == EventType.KeyDown && (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            { if (problem(text) == null) Apply(text); current.Use(); return; }
            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape) { editorWindow.Close(); current.Use(); return; }

            var inner = new Rect(rect.x + Pad, rect.y + Pad, rect.width - Pad * 2, rect.height - Pad * 2);
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 18), ExporterLocalization.T("表情名を変更"), EditorStyles.boldLabel);
            GUI.SetNextControlName(Field);
            text = EditorGUI.TextField(new Rect(inner.x, inner.y + 22, inner.width, 20), text);
            if (text.Length > 256) text = text.Substring(0, 256);
            if (!focused) { EditorGUI.FocusTextInControl(Field); focused = true; }
            var file = ExporterLocalization.T("ファイル名") + "  " + fileName;
            Line(new Rect(inner.x, inner.y + 46, inner.width, 16), file, EditorStyles.miniLabel);
            var reason = problem(text);
            if (reason != null) Line(new Rect(inner.x, inner.y + 62, inner.width, 16), reason, error);

            var buttons = inner.yMax - 20;
            var back = ExporterLocalization.T("ファイル名に戻す");
            using (new EditorGUI.DisabledScope(!renamed))
                if (GUI.Button(new Rect(inner.x, buttons, 120, 20), new GUIContent(back, back), EditorStyles.miniButton)) Apply("");
            var ok = ExporterLocalization.T("変更");
            var cancel = ExporterLocalization.T("キャンセル");
            using (new EditorGUI.DisabledScope(reason != null))
                if (GUI.Button(new Rect(inner.xMax - 72, buttons, 72, 20), new GUIContent(ok, ok))) Apply(text);
            if (GUI.Button(new Rect(inner.xMax - 72 - 6 - 82, buttons, 82, 20), new GUIContent(cancel, cancel))) editorWindow.Close();
        }

        void Apply(string name)
        {
            apply(name);
            editorWindow.Close();
        }

        static void Line(Rect rect, string value, GUIStyle style) =>
            GUI.Label(rect, new GUIContent(Cut(value, style, rect.width), value), style);

        static string Cut(string value, GUIStyle style, float width)
        {
            if (style.CalcSize(new GUIContent(value)).x <= width) return value;
            var length = value.Length;
            while (length > 1 && style.CalcSize(new GUIContent(value.Substring(0, length) + "…")).x > width) length--;
            return value.Substring(0, length) + "…";
        }
    }
}
