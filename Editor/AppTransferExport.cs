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
        // The detailed transfer exports what the expression board shows; without it there is nothing to export.
        internal const string BoardClosed = "表情の割り当て画面が開いていません";
        private string author = "";
        // The avatar's remembered export settings, the same ones the export
        // window shows and saves. Only the license is edited here.
        private ExportSettings settings = new ExportSettings();
        private AvatarLicenseOptions license { get => settings.License; set => settings.License = value; }
        private bool showTerms = EditorPrefs.GetBool(TermsFoldoutPreference, false);
        private GUIStyle summaryStyle;
        // The window asks for the summary several times a second; the store is read again only after a save.
        private GameObject countedAvatar;
        private int countedRevision = -1, excludedCount;

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
                // A board opened for the transfer hides its author and license; they are asked here.
                if (window.TransferMode) continue;
                if (!HasAuthor()) author = ExportAuthor.Replace(author, window.Author ?? "");
                var shown = window.LicenseFor(avatar);
                if (shown != null) license = shown;
            }
        }

        public bool HasAuthor() => ExportAuthor.IsValid(author);

        // The name of the avatar's last successful transfer, or null.
        public string RememberedName(GameObject avatar) => ExportSettingsStore.LoadTransferName(avatar);

        // The line above the form: what the export takes from elsewhere.
        // Detailed without a board says nothing here; the reason line says why.
        public string SettingsSummary(GameObject avatar, bool detailed)
        {
            if (avatar == null) return "";
            if (!detailed)
            {
                if (avatar != countedAvatar || countedRevision != ExportSettingsStore.Revision)
                {
                    countedAvatar = avatar;
                    countedRevision = ExportSettingsStore.Revision;
                    excludedCount = ExportSettingsStore.Load(avatar).Exclusions().Length;
                }
                return string.Format(ExporterLocalization.T("表情は名前から自動・書き出さないオブジェクト{0}件"), excludedCount);
            }
            var board = ExperimentalExpressionCaptureWindow.ForTransfer(avatar);
            return board == null ? "" : string.Format(ExporterLocalization.T("VRMの表情{0}/5・書き出さないオブジェクト{1}件"), board.SlotCount, board.ExcludedObjectCount);
        }

        // Null when the detailed transfer can export this avatar.
        public string DetailedProblem(GameObject avatar) =>
            ExperimentalExpressionCaptureWindow.ForTransfer(avatar) == null ? ExporterLocalization.T(BoardClosed) : null;

        // Opens the expression board for this avatar in the transfer mode. False
        // when the board kept another avatar.
        public bool EditDetails(GameObject avatar) => ExperimentalExpressionCaptureWindow.OpenForTransfer(avatar);

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
            var targetSettings = RememberTerms(avatar, targetAuthor);
            // The report of the attempt that produced the bytes being saved.
            ExportFallbackReport fallbacks = null;
            var create = targetSettings.Creator(avatar, name, targetAuthor);
            void Completed(byte[] bytes, IEnumerable<string> warnings)
            {
                // Like the export windows, the details of this export go to the Console.
                // Nothing asked about what the export worked around, so the whole result list goes there once.
                var details = (warnings ?? Array.Empty<string>()).ToArray();
                if (fallbacks != null && fallbacks.Count > 0) Debug.LogWarning(ExportResultList.Build(fallbacks, details, destination).Text());
                else if (details.Length > 0) Debug.Log(ExporterLocalization.T("VR Vlog 書き出し詳細\n・") + string.Join("\n・", details));
                ExportSettingsStore.SaveTransferName(avatar, name);
                saved();
            }
            // The quick transfer does not stop to ask about what the export worked
            // around; an export that cannot be saved still fails with its reason.
            var recovery = SaveNormally(avatar, destination, (options, report, warnings) =>
            {
                var exported = create(options, report, warnings);
                fallbacks = report?.Fallbacks;
                return exported;
            }, Completed, targetSettings.Exclusions(), targetSettings.Gimmicks(), false, null);
            if (recovery == null) return null;
            ExportFailureWindow.Show(recovery, () => Completed(recovery.LastSuccess.Bytes, recovery.LastSuccess.Warnings));
            return recovery.Failure?.Message ?? "";
        }

        // Writes what the expression board for this avatar shows, with the name,
        // author and license of this window. Returns like Export; without that
        // board it returns why and writes nothing.
        public string ExportDetailed(GameObject avatar, string name, string destination, Action saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            var board = ExperimentalExpressionCaptureWindow.ForTransfer(avatar);
            if (board == null) return ExporterLocalization.T(BoardClosed);
            if (!HasAuthor()) return ExporterLocalization.T(ExportAuthor.Required);
            var targetAuthor = author;
            var targetSettings = RememberTerms(avatar, targetAuthor);
            return board.TransferExport(name, targetAuthor, targetSettings.License.Copy(), destination, () =>
            {
                ExportSettingsStore.SaveTransferName(avatar, name);
                saved();
            });
        }

        // The author and the license edited here are remembered for the next
        // export. Choices saved by the export window since this avatar was
        // selected apply too.
        private ExportSettings RememberTerms(GameObject avatar, string targetAuthor)
        {
            ExportAuthor.Remember(avatar, targetAuthor);
            var targetSettings = ExportSettingsStore.Load(avatar);
            targetSettings.License = (license ?? new AvatarLicenseOptions()).Copy();
            ExportSettingsStore.Save(avatar, targetSettings);
            return targetSettings;
        }
    }
}
