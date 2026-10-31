using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    // The old export window is gone: every export runs through the expression
    // board. What stays are the shared steps the board and the transfers call.
    // It is still an EditorWindow so a window kept in a saved layout or across
    // a reload finds its type; such a window closes itself.
    public sealed class LilToonExporterWindow : EditorWindow
    {
        // The saved message when materials lost their shader. Also used by the expression board.
        internal const string SavedWithMissingShadersFormat = "VRMを保存しましたが、シェーダーが見つからないマテリアルが{0}個あり、見た目を正しく再現できていません。アバターに必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出してください。";

        private void OnEnable()
        {
            EditorApplication.delayCall += () => { if (this != null) Close(); };
        }

        // A fresh export has no old diagnosis or preview to invalidate. The
        // exporter works on its own copy; only an actual export failure starts
        // a guarded recovery session. Successful normal exports save directly.
        internal static ExportRecoverySession ExportAndSaveNormally(GameObject source, string destination,
            Func<ExportRecoveryOptions, ExportRecoveryReport, ICollection<string>, byte[]> create,
            Action<byte[], IEnumerable<string>> completed = null,
            IEnumerable<GameObject> previewExcludedObjects = null, ExportGimmickOptions previewGimmicks = null,
            bool interactive = false, Action openBlinkSettings = null, string diagnosticsKind = null)
        {
            if (create == null) throw new ArgumentNullException(nameof(create));
            var warnings = new List<string>();
            var report = new ExportRecoveryReport();
            byte[] bytes;
            // Poses over the app's limits are asked once here; a later retry
            // from the recovery window reuses the choice saved for the avatar.
            try
            {
                using (new PoseLimitChooser.Scope(source, interactive && !ExportFallbackConfirmation.IsBatchMode()))
                    bytes = create(new ExportRecoveryOptions(), report, warnings);
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception exception)
            {
                return ExportRecoverySession.FromFailedExport(source, destination, create, exception, report,
                    previewExcludedObjects, previewGimmicks);
            }
            // The result list can copy the diagnostics before anything is saved.
            ExportDiagnostics.Record(source, bytes, report.Fallbacks, warnings, diagnosticsKind);
            // The export already worked around what it could not set up. Show the
            // result list once, before anything is written, and save from there.
            ExportFallbackConfirmation.Confirm(report.Fallbacks, warnings, destination, source, interactive, () =>
            {
                ExportOutputWriter.Write(destination, bytes);
                completed?.Invoke(bytes, warnings);
            }, openBlinkSettings);
            return null;
        }

        internal static string PackageVersion()
        {
            var info = PackageManagerPackageInfo.FindForAssembly(typeof(LilToonExporterWindow).Assembly);
            return info != null && !string.IsNullOrWhiteSpace(info.version) ? info.version : "1.4.0";
        }

        // The same step as the expression board's third page: returns the panel as a VisualElement, given
        // the board's "戻る" and whether "VRMを保存" is the blue button. Null without the module, or while
        // it is turned off, and the board then shows its own page without the QR.
        internal static System.Reflection.MethodInfo EmbeddedTransferMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferPanel", false), "CreateEmbedded",
                typeof(GameObject), typeof(Action), typeof(Func<bool>));

        // Whether that panel is uploading or showing its QR, asked before the board leaves its page.
        internal static System.Reflection.MethodInfo EmbeddedTransferringMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferPanel", false), "EmbeddedTransferring", typeof(UnityEngine.UIElements.VisualElement));

        // Gives that panel the board's avatar change for "別のアバターを選ぶ" after a transfer.
        internal static System.Reflection.MethodInfo EmbeddedChangeAvatarMethod() =>
            TransferMethod(type => type.Assembly.GetType(type.Namespace + ".AppTransferPanel", false), "SetEmbeddedChangeAvatar",
                typeof(UnityEngine.UIElements.VisualElement), typeof(Action));

        private static System.Reflection.MethodInfo TransferMethod(Func<Type, Type> owner, string name, params Type[] parameters)
        {
            try
            {
                var type = Type.GetType("VRVlog.LilToonExporter.LanTransfer.CloudVrmTransferWindow, VRVlog.LanTransfer.Editor", false);
                if (type == null || !Equals(type.GetProperty("IsAvailable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null), true)) return null;
                return owner(type)?.GetMethod(name, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                    null, parameters, null);
            }
            catch { return null; }
        }

        // Stops only without lilToon, or when lilToon's baker that the avatar
        // needs is missing or changed. Any other installed version exports:
        // warnings lists how it differs from the catalogue and what the export
        // leaves out. Returns the installed version for asset.extras;
        // sourceLilToonVersion is always the catalogue version.
        internal static string RequireLilToon(GameObject avatar, ICollection<string> warnings)
        {
            var version = InstalledLilToonVersion();
            var error = Compatibility.DependencyPolicy.LilToonError(version, ExporterLocalization.T);
            if (error != null) throw new InvalidOperationException(error);
            var warning = Compatibility.DependencyPolicy.LilToonWarning(version, ExporterLocalization.T);
            if (warning != null) warnings?.Add(warning);
            LilToonStructureCheck.Inspect(avatar, Compatibility.DependencyPolicy.DisplayLilToon(version, ExporterLocalization.T), warnings);
            return version;
        }

        // The package version, LilToonUnknownVersion for lilToon under Assets
        // without one, or null without lilToon.
        internal static string InstalledLilToonVersion() => Compatibility.LilToonInstallation.Find()?.Version;
    }
}
