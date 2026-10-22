using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    // The header at the top of the exporter's IMGUI windows: the VR Vlog logo and
    // the screen name, with a hairline below. Sizes and colours are those of the
    // header of アプリに転送 (AppTransferWindow.uss: .header, .header-icon,
    // .heading, .version, .hairline), so every window starts the same way. Windows call only
    // Layout or Draw, so the header can be changed here for all of them at once.
    public static class ExporterWindowHeader
    {
        private const float RowHeight = 60f, Padding = 16f, IconSize = 36f, IconGap = 10f;

        // The version at the right end has a fixed width, so the title does not move with its length.
        public const float VersionWidth = 110f, VersionGap = 8f;

        // The row plus the 1px hairline.
        public const float Height = RowHeight + 1f;

        private static GUIStyle headingStyle, versionStyle;
        private static string version;

        // "v" and the version in this package's package.json, so a screenshot or a report
        // shows which version made it. Empty when the exporter is not installed as a package.
        public static string Version
        {
            get
            {
                if (version == null)
                {
                    var number = PackageInfo.FindForAssembly(typeof(ExporterWindowHeader).Assembly)?.version;
                    version = string.IsNullOrWhiteSpace(number) ? "" : "v" + number;
                }
                return version;
            }
        }

        // For windows laid out with GUILayout: call first in OnGUI, outside any padded scope.
        public static void Layout(string title) =>
            Draw(GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(Height), GUILayout.ExpandWidth(true)), title);

        // For windows that place their parts with rects: draw at the top and start the rest at Height.
        public static void Draw(Rect area, string title)
        {
            if (Event.current.type != EventType.Repaint) return;
            headingStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip, wordWrap = false };
            versionStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleRight, clipping = TextClipping.Clip, wordWrap = false,
                normal = { textColor = EditorGUIUtility.isProSkin ? new Color32(153, 153, 153, 255) : new Color32(110, 110, 110, 255) },
            };
            var icon = new Rect(area.x + Padding, area.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
            GUI.DrawTexture(icon, ExporterWindowIcon.Texture, ScaleMode.ScaleToFit);
            var textX = icon.xMax + IconGap;
            var textEnd = area.xMax - Padding;
            if (Version.Length > 0)
            {
                GUI.Label(new Rect(textEnd - VersionWidth, area.y, VersionWidth, RowHeight), Version, versionStyle);
                textEnd -= VersionWidth + VersionGap;
            }
            GUI.Label(new Rect(textX, area.y, Mathf.Max(0f, textEnd - textX), RowHeight), title, headingStyle);
            EditorGUI.DrawRect(new Rect(area.x, area.y + RowHeight, area.width, 1f),
                EditorGUIUtility.isProSkin ? new Color(1f, 1f, 1f, .14f) : new Color(0f, 0f, 0f, .18f));
        }
    }
}
