using System;
using UnityEngine;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    // The exporter for tests of the transfer panel: each member calls the delegate of its name,
    // which the test sets. The result accessors give null while unset; the others need theirs.
    internal sealed class StubAppTransferExport : IAppTransferExport
    {
        internal Action<GameObject> SelectAvatar;
        internal Func<bool> HasAuthor;
        internal Func<int> LastMissingShaderCount;
        internal Func<int> LastOmittedStageCount;
        internal Func<string[]> LastNotes;
        internal Func<Func<string>> LastDiagnostics;
        internal Func<Func<string>> LastNamedDiagnostics;
        internal Func<Action<string, string>> LastResult;
        internal Func<GameObject, string> RememberedName;
        internal Func<GameObject, bool, string> SettingsSummary;
        internal Func<GameObject, string> DetailedProblem;
        internal Func<GameObject, bool> DetailedNeedsFix;
        internal Func<GameObject, bool> EditDetails;
        internal Action<bool> DrawTerms;
        internal Func<GameObject, string, string, Action, string> Export;
        internal Func<GameObject, string, string, Action, string> ExportDetailed;
        internal Func<GameObject, string, bool, Action<string, int>, string> SaveCopy;
        internal Func<GameObject, string> SaveFileName;
        internal Func<string> TermsKey;
        internal Action<GameObject, string> RememberTransferName;
        internal Func<GameObject, bool, object> SourceStamp;
        internal Func<object, GameObject, bool> SourceUnchanged;
        internal Func<object, bool> ChoicesUnchanged;
        internal Func<GameObject, Rect, bool> DrawQuickPreview;
        internal Func<GameObject, bool> QuickPreviewChanged;
        internal Func<bool> QuickPreviewFailed;
        internal Action ReleaseQuickPreview;

        void IAppTransferExport.SelectAvatar(GameObject avatar) => SelectAvatar(avatar);
        bool IAppTransferExport.HasAuthor() => HasAuthor();
        int IAppTransferExport.LastMissingShaderCount() => LastMissingShaderCount();
        int IAppTransferExport.LastOmittedStageCount() => LastOmittedStageCount();
        string[] IAppTransferExport.LastNotes() => LastNotes();
        Func<string> IAppTransferExport.LastDiagnostics() => LastDiagnostics?.Invoke();
        Func<string> IAppTransferExport.LastNamedDiagnostics() => LastNamedDiagnostics?.Invoke();
        Action<string, string> IAppTransferExport.LastResult() => LastResult?.Invoke();
        string IAppTransferExport.RememberedName(GameObject avatar) => RememberedName(avatar);
        string IAppTransferExport.SettingsSummary(GameObject avatar, bool detailed) => SettingsSummary(avatar, detailed);
        string IAppTransferExport.DetailedProblem(GameObject avatar) => DetailedProblem(avatar);
        bool IAppTransferExport.DetailedNeedsFix(GameObject avatar) => DetailedNeedsFix(avatar);
        bool IAppTransferExport.EditDetails(GameObject avatar) => EditDetails(avatar);
        void IAppTransferExport.DrawTerms(bool markAuthor) => DrawTerms(markAuthor);
        string IAppTransferExport.Export(GameObject avatar, string name, string destination, Action saved) => Export(avatar, name, destination, saved);
        string IAppTransferExport.ExportDetailed(GameObject avatar, string name, string destination, Action saved) => ExportDetailed(avatar, name, destination, saved);
        string IAppTransferExport.SaveCopy(GameObject avatar, string name, bool detailed, Action<string, int> saved) => SaveCopy(avatar, name, detailed, saved);
        string IAppTransferExport.SaveFileName(GameObject avatar) => SaveFileName(avatar);
        string IAppTransferExport.TermsKey() => TermsKey();
        void IAppTransferExport.RememberTransferName(GameObject avatar, string name) => RememberTransferName(avatar, name);
        object IAppTransferExport.SourceStamp(GameObject avatar, bool detailed) => SourceStamp(avatar, detailed);
        bool IAppTransferExport.SourceUnchanged(object stamp, GameObject avatar) => SourceUnchanged(stamp, avatar);
        bool IAppTransferExport.ChoicesUnchanged(object stamp) => ChoicesUnchanged(stamp);
        bool IAppTransferExport.DrawQuickPreview(GameObject avatar, Rect rect) => DrawQuickPreview(avatar, rect);
        bool IAppTransferExport.QuickPreviewChanged(GameObject avatar) => QuickPreviewChanged(avatar);
        bool IAppTransferExport.QuickPreviewFailed() => QuickPreviewFailed();
        void IAppTransferExport.ReleaseQuickPreview() => ReleaseQuickPreview();
    }
}
