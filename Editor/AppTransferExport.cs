using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Export side of the "アプリに転送" window. That window ships with the optional
    // QR module and binds these members by name where it uses them, so the
    // exporter keeps compiling and working in packages without the module.
    internal sealed class AppTransferExport
    {
        // Only the open/closed state of the section. The author is remembered
        // per avatar through ExportAuthor, like the export windows.
        private const string TermsFoldoutPreference = "VRVlog.AppTransfer.ShowVrmTerms";
        private string author = "";
        // The avatar's remembered export settings, the same ones the export
        // window shows and saves. Only the license is edited here.
        private ExportSettings settings = new ExportSettings();
        private AvatarLicenseOptions license { get => settings.License; set => settings.License = value; }
        private bool showTerms = EditorPrefs.GetBool(TermsFoldoutPreference, false);
        private GUIStyle summaryStyle;

        internal delegate ExportRecoverySession SaveNormallyHandler(GameObject source, string destination,
            Func<ExportRecoveryOptions, ExportRecoveryReport, ICollection<string>, byte[]> create,
            Action<byte[], IEnumerable<string>> completed, IEnumerable<GameObject> previewExcludedObjects,
            ExportGimmickOptions previewGimmicks, bool interactive, Action openBlinkSettings);

        // The save path of the export windows. Tests replace it to see how it is called.
        internal static SaveNormallyHandler SaveNormally = LilToonExporterWindow.ExportAndSaveNormally;

        // The author is the avatar's remembered one, else what this window
        // already holds, else the author of an open export window. The license
        // comes from an export window showing this avatar, else the avatar's
        // remembered settings; an avatar without any starts from the default.
        public void SelectAvatar(GameObject avatar)
        {
            settings = ExportSettingsStore.Load(avatar);
            if (avatar != null) author = ExportAuthor.Replace(author, ExportAuthor.ForSelection(ExportAuthor.Remembered(avatar), author));
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>())
            {
                if (!HasAuthor()) author = ExportAuthor.Replace(author, window.Author ?? "");
                var shown = window.LicenseFor(avatar);
                if (shown != null) license = shown;
            }
        }

        public bool HasAuthor() => ExportAuthor.IsValid(author);

        // The VRM meta fields of the export window, under this window's own foldout.
        public void DrawTerms()
        {
            if (summaryStyle == null) summaryStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
            // A required field that is still empty is never left folded away.
            if (!HasAuthor()) showTerms = true;
            var open = EditorGUILayout.Foldout(showTerms, ExporterLocalization.T("VRMの利用条件"), true);
            if (open != showTerms && HasAuthor())
            {
                showTerms = open;
                EditorPrefs.SetBool(TermsFoldoutPreference, open);
            }
            if (!showTerms)
            {
                // Folded, the section still says what the VRM will carry.
                var rect = EditorGUILayout.GetControlRect(false, 16f);
                rect.xMin += 14f;
                GUI.Label(rect, author.Trim() + " / " + AvatarLicenseSettingsUi.Summary(license), summaryStyle);
                return;
            }
            EditorGUILayout.Space(4f);
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 164f;
            try
            {
                author = EditorGUILayout.TextField(new GUIContent(ExporterLocalization.T("作者名"), ExporterLocalization.T("VRMファイルに記録される作者名です。")), author);
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            // The line is always reserved so typing does not move the rows below.
            EditorGUILayout.LabelField(HasAuthor() ? "" : ExporterLocalization.T(ExportAuthor.Required), EditorStyles.miniLabel);
            AvatarLicenseSettingsUi.DrawFields(license);
        }

        // Writes the VRM to destination through the entry the export windows use,
        // with the avatar's remembered export settings (exclusions, gimmicks,
        // blink, poses) and the license shown here.
        // Returns null when it saved (saved has run) or the user canceled, and the
        // reason when it failed. After a failure the recovery window is open;
        // saving from there writes the same destination and then runs saved.
        public string Export(GameObject avatar, string name, string destination, Action saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            // The window keeps its button disabled without an author; a retry or
            // another caller stops here before any file or recovery session starts.
            if (!HasAuthor()) return ExporterLocalization.T(ExportAuthor.Required);
            var targetAuthor = author;
            ExportAuthor.Remember(avatar, targetAuthor);
            // Choices saved by the export window since this avatar was selected
            // apply too; the license edited here is remembered for the next export.
            var targetSettings = ExportSettingsStore.Load(avatar);
            targetSettings.License = (license ?? new AvatarLicenseOptions()).Copy();
            ExportSettingsStore.Save(avatar, targetSettings);
            void Completed(byte[] bytes, IEnumerable<string> warnings)
            {
                // Like the export windows, the details of this export go to the Console.
                var details = (warnings ?? Array.Empty<string>()).ToArray();
                if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
                saved();
            }
            // Interactive like the export window: an export that had to work around
            // something asks before it is saved, so nothing degraded is uploaded unasked.
            // "設定を変更する" selects the objects concerned and cancels this transfer.
            var recovery = SaveNormally(avatar, destination, targetSettings.Creator(avatar, name, targetAuthor), Completed,
                targetSettings.Exclusions(), targetSettings.Gimmicks(), true, null);
            if (recovery == null) return null;
            ExportFailureWindow.Show(recovery, () => Completed(recovery.LastSuccess.Bytes, recovery.LastSuccess.Warnings));
            return recovery.Failure?.Message ?? "";
        }
    }
}
