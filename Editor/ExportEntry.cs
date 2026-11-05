using System;
using System.Collections.Generic;
using UnityEngine;
using PackageManagerPackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace VRVlog.LilToonExporter
{
    // The shared steps of an export that the expression board and the transfers call.
    internal static class ExportEntry
    {
        // The saved message when materials lost their shader. Also used by the expression board.
        internal const string SavedWithMissingShadersFormat = "VRMを保存しましたが、シェーダーが見つからないマテリアルが{0}個あり、見た目を正しく再現できていません。アバターに必要なシェーダーをUnityプロジェクトに追加して、もう一度書き出してください。";

        // A fresh export has no old diagnosis or preview to invalidate. The
        // exporter works on its own copy; only an actual export failure starts
        // a guarded recovery session. Successful normal exports save directly.
        // The result list opens once the file is written. A transfer passes
        // resultAfterTransfer: it gets the opener (null when there is nothing to
        // show) before completed runs, and opens it when the transfer has ended.
        internal static ExportRecoverySession ExportAndSaveNormally(GameObject source, string destination,
            Func<ExportRecoveryOptions, ExportRecoveryReport, ICollection<string>, byte[]> create,
            Action<byte[], IEnumerable<string>> completed = null,
            IEnumerable<GameObject> previewExcludedObjects = null, ExportGimmickOptions previewGimmicks = null,
            bool interactive = false, Action openBlinkSettings = null, string diagnosticsKind = null,
            Action<Action<string, string>> resultAfterTransfer = null)
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
            // The result list copies the diagnostics of these bytes.
            ExportDiagnostics.Record(source, bytes, report.Fallbacks, warnings, diagnosticsKind);
            // The export already worked around what it could not set up. The list of
            // it opens after the work is done; a failed write throws before it.
            var openResult = ExportFallbackConfirmation.ResultOpener(report.Fallbacks, warnings,
                resultAfterTransfer != null ? null : destination, source, interactive, openBlinkSettings);
            ExportOutputWriter.Write(destination, bytes);
            if (resultAfterTransfer != null)
            {
                resultAfterTransfer(openResult);
                completed?.Invoke(bytes, warnings);
                return null;
            }
            completed?.Invoke(bytes, warnings);
            openResult?.Invoke(ExportResultWindow.SavedTitle, null);
            return null;
        }

        internal static string PackageVersion()
        {
            var info = PackageManagerPackageInfo.FindForAssembly(typeof(ExportEntry).Assembly);
            return info != null && !string.IsNullOrWhiteSpace(info.version) ? info.version : "1.8.0";
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
