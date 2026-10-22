using System;
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
        // The board's next step names what to fix, as it does before saving a file.
        internal const string BoardNeedsFix = "表情の割り当てを直すと作れます";
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
        // Materials whose shader was missing in the last saved export. The window warns under its QR.
        private int lastMissingShaderCount;

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

        // The number of materials whose shader was missing in the export last saved
        // by Export or ExportDetailed. 0 before a save and after a clean one.
        public int LastMissingShaderCount() => lastMissingShaderCount;

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
                // The quick transfer prepares the board as "通常" opens it: the
                // avatar's last assignment, automatic where there is none.
                return string.Format(ExporterLocalization.T("表情は前回の割り当て（なければ自動）・書き出さないオブジェクト{0}件"), excludedCount);
            }
            var board = ExperimentalExpressionCaptureWindow.ForTransfer(avatar);
            return board == null ? "" : string.Format(ExporterLocalization.T("VRMの表情{0}/{1}・書き出さないオブジェクト{2}件"),
                board.SlotCount, ExperimentalExpressionCaptureWindow.MainSlotCount, board.ExcludedObjectCount);
        }

        // Null when the detailed transfer can export this avatar.
        public string DetailedProblem(GameObject avatar) =>
            ExperimentalExpressionCaptureWindow.ForTransfer(avatar) == null ? ExporterLocalization.T(BoardClosed) : null;

        // True while the open board has something to fix before it exports.
        public bool DetailedNeedsFix(GameObject avatar) => ExperimentalExpressionCaptureWindow.ForTransfer(avatar)?.BlocksExport ?? false;

        // Opens the expression board for this avatar in the transfer mode. False
        // when the board kept another avatar.
        public bool EditDetails(GameObject avatar) => ExperimentalExpressionCaptureWindow.OpenForTransfer(avatar);

        // The VRM meta fields of the export window. The author is required and
        // always shown; the license is optional and starts folded, like the export
        // windows, and this window remembers whether it was opened.
        public void DrawTerms()
        {
            if (summaryStyle == null) summaryStyle = new GUIStyle(EditorStyles.miniLabel) { wordWrap = false, clipping = TextClipping.Clip };
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 164f;
            try
            {
                author = EditorGUILayout.TextField(new GUIContent(ExporterLocalization.T("作者名"), ExporterLocalization.T("VRMファイルに記録される作者名です。")), author);
            }
            finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
            // The line is always reserved so typing does not move the rows below.
            EditorGUILayout.LabelField(HasAuthor() ? "" : ExporterLocalization.T(ExportAuthor.Required), EditorStyles.miniLabel);
            var open = EditorGUILayout.Foldout(showTerms, ExporterLocalization.T("VRMの利用条件"), true);
            if (open != showTerms)
            {
                showTerms = open;
                EditorPrefs.SetBool(TermsFoldoutPreference, open);
            }
            if (!showTerms)
            {
                // Folded, the section still says what the VRM will carry.
                var rect = EditorGUILayout.GetControlRect(false, 16f);
                rect.xMin += 14f;
                GUI.Label(rect, AvatarLicenseSettingsUi.Summary(license), summaryStyle);
                return;
            }
            AvatarLicenseSettingsUi.DrawFields(license);
        }

        // Writes what "通常" writes for this avatar when nothing on the expression
        // board is touched: a board is prepared out of sight, as opening "通常"
        // prepares it, and exports with the name, author and license of this
        // window. Only the result list is not asked about before saving.
        // Returns null when it saved (saved has run) or the user canceled, and the
        // reason when it failed. After a failure the recovery window is open;
        // saving from there writes the same destination and then runs saved.
        public string Export(GameObject avatar, string name, string destination, Action saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            // The window keeps its button disabled without an author; a retry or
            // another caller stops here before any file or recovery session starts.
            if (!HasAuthor()) return ExporterLocalization.T(ExportAuthor.Required);
            lastMissingShaderCount = 0;
            var targetAuthor = author;
            var targetSettings = RememberTerms(avatar, targetAuthor);
            ExperimentalExpressionCaptureWindow board;
            try { board = ExperimentalExpressionCaptureWindow.PrepareHidden(avatar); }
            catch (OperationCanceledException) { return null; }
            try { return ExportFrom(board, avatar, name, targetAuthor, targetSettings, destination, saved, false); }
            finally { UnityEngine.Object.DestroyImmediate(board); }
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
            lastMissingShaderCount = 0;
            var targetAuthor = author;
            var targetSettings = RememberTerms(avatar, targetAuthor);
            return ExportFrom(board, avatar, name, targetAuthor, targetSettings, destination, saved, true);
        }

        // "VRMをPCに保存" on the transfer screen: what the transfer would send (detailed: the open board,
        // quick: a board prepared out of sight), saved as "VRMを保存" saves, to a file the user picks.
        // saved gets the path and the missing shader count once the file is written. Returns null when
        // it saved or the user canceled, and the reason when it failed.
        public string SaveCopy(GameObject avatar, string name, bool detailed, Action<string, int> saved)
        {
            if (saved == null) throw new ArgumentNullException(nameof(saved));
            if (!HasAuthor()) return ExporterLocalization.T(ExportAuthor.Required);
            var open = detailed ? ExperimentalExpressionCaptureWindow.ForTransfer(avatar) : null;
            if (detailed && open == null) return ExporterLocalization.T(BoardClosed);
            var destination = EditorUtility.SaveFilePanel(ExporterLocalization.T("VRMの保存先"), "",
                ExperimentalExpressionCaptureWindow.SaveFileName(avatar), "vrm");
            if (string.IsNullOrEmpty(destination)) return null;
            var targetAuthor = author;
            var license = RememberTerms(avatar, targetAuthor).License.Copy();
            if (open != null) return open.SaveTransferCopy(name, targetAuthor, license, destination, saved);
            ExperimentalExpressionCaptureWindow board;
            try { board = ExperimentalExpressionCaptureWindow.PrepareHidden(avatar); }
            catch (OperationCanceledException) { return null; }
            try { return board.SaveTransferCopy(name, targetAuthor, license, destination, saved); }
            finally { UnityEngine.Object.DestroyImmediate(board); }
        }

        // The one export of both transfers: the board writes the VRM; a saved
        // transfer remembers its name for the avatar.
        private string ExportFrom(ExperimentalExpressionCaptureWindow board, GameObject avatar, string name, string targetAuthor,
            ExportSettings targetSettings, string destination, Action saved, bool interactive)
        {
            return board.TransferExport(name, targetAuthor, targetSettings.License.Copy(), destination, () =>
            {
                // The board sets the count before it calls back. A plain field:
                // still read when the recovery window saves after the board closed.
                lastMissingShaderCount = board.LastMissingShaderCount;
                ExportSettingsStore.SaveTransferName(avatar, name);
                saved();
            }, interactive);
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
