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
        private AvatarLicenseOptions license = new AvatarLicenseOptions();
        private bool showTerms = EditorPrefs.GetBool(TermsFoldoutPreference, false);
        private GUIStyle summaryStyle;

        // The avatar's remembered author comes first. Otherwise start from the
        // author and license settings of an open export window; like those
        // windows, another avatar starts from the default license.
        public void SelectAvatar(GameObject avatar)
        {
            license = new AvatarLicenseOptions();
            if (avatar != null) author = ExportAuthor.ForSelection(ExportAuthor.Remembered(avatar), author);
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>())
            {
                if (string.IsNullOrWhiteSpace(author)) author = window.Author ?? "";
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
        // with the settings an export window has before anything is adjusted.
        // Returns null when it saved (saved has run) or the user canceled, and the
        // reason when it failed. After a failure the recovery window is open;
        // saving from there writes the same destination and then runs saved.
        public string Export(GameObject avatar, string name, string destination, Action saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            var targetAuthor = author;
            var targetLicense = license.Copy();
            ExportAuthor.Remember(avatar, targetAuthor);
            void Completed(byte[] bytes, IEnumerable<string> warnings)
            {
                // Like the export windows, the details of this export go to the Console.
                var details = (warnings ?? Array.Empty<string>()).ToArray();
                if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
                saved();
            }
            var recovery = LilToonExporterWindow.ExportAndSaveNormally(avatar, destination, (options, report, warnings) =>
            {
                if (avatar == null) throw new InvalidOperationException(ExporterLocalization.T("この書き出しで選んだアバターが見つかりません。アバターを指定し直してください。"));
                return UniVrmOneClickExporter.Export(avatar, name, targetAuthor, warnings,
                    exporterVersion: LilToonExporterWindow.PackageVersion(), lilToonVersion: Compatibility.DependencyPolicy.LilToonVersion,
                    poseOptions: new PoseExportOptions(), licenseOptions: targetLicense, disableAudioLink: true,
                    recoveryOptions: options, recoveryReport: report);
            }, Completed, previewGimmicks: new ExportGimmickOptions());
            if (recovery == null) return null;
            ExportFailureWindow.Show(recovery, () => Completed(recovery.LastSuccess.Bytes, recovery.LastSuccess.Warnings));
            return recovery.Failure?.Message ?? "";
        }
    }
}
