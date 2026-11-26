using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 軽量化 as the expression board and the quick transfer both show it: three
    // buttons of one width and the note of the chosen level. The level is the
    // avatar's saved one (ExportSettingsStore), so a press on either screen is
    // what the other shows and what both export.
    internal static class LightweightChoice
    {
        internal const float ButtonWidth = 80;
        internal static float Width => ButtonWidth * ExperimentalExpressionCaptureWindow.LightweightLevelNames.Length;

        static readonly Dictionary<(string, GUIStyle, int), string> fitted = new Dictionary<(string, GUIStyle, int), string>();

        internal static LightweightLevel Saved(GameObject avatar) =>
            avatar == null ? LightweightLevel.Standard : ExportSettingsStore.Load(avatar).Lightweight;

        // Saves the level for the avatar; a press on the chosen level saves nothing.
        internal static void Choose(GameObject avatar, LightweightLevel level)
        {
            if (avatar == null || !LightweightLevels.IsDefined((int)level)) return;
            var settings = ExportSettingsStore.Load(avatar);
            if (settings.Lightweight == level) return;
            settings.Lightweight = level;
            ExportSettingsStore.Save(avatar, settings);
        }

        internal static string Note(LightweightLevel level, bool usesAvatarOptimizer) =>
            ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightNote(level, usesAvatarOptimizer));

        // The buttons, each name clipped to the button and the note of its level as the tooltip.
        internal static GUIContent[] Buttons(bool usesAvatarOptimizer, GUIStyle style)
        {
            var names = ExperimentalExpressionCaptureWindow.LightweightLevelNames;
            var levels = new GUIContent[names.Length];
            for (var i = 0; i < levels.Length; i++)
            {
                var level = (LightweightLevel)i;
                var tip = Note(level, usesAvatarOptimizer);
                if (level == LightweightLevel.Max && usesAvatarOptimizer) tip += "\n" + ExporterLocalization.T(ExperimentalExpressionCaptureWindow.LightweightMaxTooltip);
                levels[i] = new GUIContent(Fit(ExporterLocalization.T(names[i]), style, ButtonWidth - 12), tip);
            }
            return levels;
        }

        // The buttons in rect; a press saves the level for the avatar.
        internal static void Draw(Rect rect, GameObject avatar, LightweightLevel current, bool usesAvatarOptimizer)
        {
            var chosen = GUI.Toolbar(rect, (int)current, Buttons(usesAvatarOptimizer, GUI.skin.button), GUI.skin.button, GUI.ToolbarButtonSize.Fixed);
            if (chosen != (int)current && LightweightLevels.IsDefined(chosen)) Choose(avatar, (LightweightLevel)chosen);
        }

        // Text is clipped with an ellipsis so a long translation never widens a button.
        static string Fit(string text, GUIStyle style, float width)
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
            if (fitted.Count > 256) fitted.Clear();
            fitted[key] = value;
            return value;
        }
    }
}
