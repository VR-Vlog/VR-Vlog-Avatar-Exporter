using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The choices of one avatar's export: manual exclusions, the gimmick
    // auto-exclusion and its exceptions, blink, poses and the VRM license.
    // The export window, the app transfer and batch callers all export through
    // this class, so a VRM saved from any of them is built the same way.
    // ExportSettingsStore remembers it per avatar outside the avatar itself.
    internal sealed class ExportSettings
    {
        internal readonly List<GameObject> ExcludedObjects = new List<GameObject>();
        internal bool AutoExcludeGimmicks = true;
        internal readonly List<GameObject> IncludedGimmicks = new List<GameObject>();
        internal BlinkExportOptions Blink = new BlinkExportOptions();
        internal PoseExportOptions Poses = new PoseExportOptions();
        internal AvatarLicenseOptions License = new AvatarLicenseOptions();

        internal GameObject[] Exclusions() => ExcludedObjects.Where(item => item != null).ToArray();

        internal ExportGimmickOptions Gimmicks() => new ExportGimmickOptions
        {
            AutoExclude = AutoExcludeGimmicks,
            IncludedObjects = IncludedGimmicks.Where(item => item != null).ToArray()
        };

        internal ExportSettings Copy()
        {
            var copy = new ExportSettings
            {
                AutoExcludeGimmicks = AutoExcludeGimmicks,
                Blink = (Blink ?? new BlinkExportOptions()).Copy(),
                Poses = (Poses ?? new PoseExportOptions()).Copy(),
                License = (License ?? new AvatarLicenseOptions()).Copy()
            };
            copy.ExcludedObjects.AddRange(ExcludedObjects);
            copy.IncludedGimmicks.AddRange(IncludedGimmicks);
            return copy;
        }

        // A cheap text that changes whenever a choice changes. Windows compare
        // it to know when to save; it is not stored.
        internal string Signature()
        {
            var text = new StringBuilder();
            void Id(UnityEngine.Object value) => text.Append(value != null ? value.GetInstanceID() : 0).Append(',');
            foreach (var item in ExcludedObjects) Id(item);
            text.Append('|').Append(AutoExcludeGimmicks).Append('|');
            foreach (var item in IncludedGimmicks) Id(item);
            var blink = Blink ?? new BlinkExportOptions();
            text.Append('|').Append((int)blink.Mode);
            foreach (var slot in new[] { blink.Both, blink.Left, blink.Right })
            {
                text.Append('|');
                foreach (var binding in slot)
                {
                    Id(binding?.Renderer);
                    text.Append(binding?.Shape).Append('\u001f').Append((binding?.Weight ?? 0).ToString("R", CultureInfo.InvariantCulture)).Append(';');
                }
            }
            var poses = Poses ?? new PoseExportOptions();
            text.Append('|');
            foreach (var pose in poses.Manual)
            {
                Id(pose?.Clip);
                text.Append(pose?.Time.ToString("R", CultureInfo.InvariantCulture)).Append('\u001f').Append(pose?.Name).Append('\u001f').Append(pose?.Category).Append(';');
            }
            text.Append('|').Append(string.Join("\u001f", poses.Excluded.OrderBy(item => item, StringComparer.Ordinal)));
            text.Append('|').Append(string.Join("\u001f", poses.Names.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => item.Key + "\u001e" + item.Value)));
            text.Append('|').Append(JsonUtility.ToJson(License ?? new AvatarLicenseOptions()));
            return text.ToString();
        }

        // Builds the VRM of avatar with these settings. The stage guard follows
        // the project-wide choice (ExportStageGuard.ContinueByDefault), and
        // AudioLink is always off on the export copy because the app cannot drive it.
        internal byte[] Export(GameObject avatar, string name, string author, ICollection<string> warnings,
            ExportRecoveryOptions recoveryOptions, ExportRecoveryReport recoveryReport)
        {
            if (avatar == null) throw new InvalidOperationException(ExporterLocalization.T("この書き出しで選んだアバターが見つかりません。アバターを指定し直してください。"));
            if (recoveryReport != null) recoveryReport.ContinueOnStageFailure = ExportStageGuard.ContinueByDefault;
            return UniVrmOneClickExporter.Export(avatar, name, author, warnings,
                exporterVersion: LilToonExporterWindow.PackageVersion(), lilToonVersion: LilToonExporterWindow.RequireSupportedLilToon(avatar),
                excludedObjects: Exclusions(), gimmickOptions: Gimmicks(), blinkOptions: Blink, poseOptions: Poses,
                recoveryOptions: recoveryOptions, recoveryReport: recoveryReport, licenseOptions: License, disableAudioLink: true);
        }

        // The create step of LilToonExporterWindow.ExportAndSaveNormally. It
        // keeps a copy taken now, so a retry from the failure window uses the
        // choices of this attempt even if the window has changed since.
        internal Func<ExportRecoveryOptions, ExportRecoveryReport, ICollection<string>, byte[]> Creator(GameObject avatar, string name, string author)
        {
            var snapshot = Copy();
            return (options, report, warnings) => snapshot.Export(avatar, name, author, warnings, options, report);
        }

        // Batch entry: exports avatar with its remembered settings to
        // destination without dialogs, like the export window would. Returns
        // the recovery session of a failed export (nothing saved), else null.
        internal static ExportRecoverySession SaveVrm(GameObject avatar, string destination, string author, string name = null,
            Action<byte[], IEnumerable<string>> completed = null)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            var settings = ExportSettingsStore.Load(avatar);
            var avatarName = !string.IsNullOrWhiteSpace(name) ? name.Trim() : !string.IsNullOrWhiteSpace(avatar.name) ? avatar.name.Trim() : "avatar";
            return LilToonExporterWindow.ExportAndSaveNormally(avatar, destination, settings.Creator(avatar, avatarName, author), completed,
                settings.Exclusions(), settings.Gimmicks(), interactive: false);
        }
    }
}
