using System;
using System.Text.RegularExpressions;

namespace VRVlog.LilToonExporter.LanTransfer
{
    internal static class CloudTransferAvailability
    {
#if UNITY_EDITOR && VRVLOG_CLOUD_TRANSFER_DEVELOPMENT
        internal static bool Enabled => true;
#elif UNITY_EDITOR
        internal static bool Enabled => IsSupportedVersion(UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CloudTransferAvailability).Assembly)?.version);
#else
        internal static bool Enabled => false;
#endif
        // Stable 0.11.15 adopted the reviewed beta.2 transfer module, and every
        // stable release from 1.0.0 ships the transfer menus. Other 0.x stable
        // packages remain export-only.
        internal static bool IsSupportedVersion(string version) =>
            version == "0.11.15" || IsStableFromOne(version) || IsPrereleaseVersion(version);

        internal static bool IsStableFromOne(string version)
        {
            var match = Regex.Match(version ?? "", @"\A([0-9]+)\.[0-9]+\.[0-9]+\z");
            return match.Success && int.TryParse(match.Groups[1].Value, out var major) && major >= 1;
        }

        internal static bool IsPrereleaseVersion(string version) => version != null &&
            Regex.IsMatch(version, @"\A\d+\.\d+\.\d+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\z");
        internal const string DisabledMessage = "この版ではクラウドQR転送を利用できません。対応するExporterへ更新するか、VRMをファイルに書き出して、VR Vlogへ取り込んでください。";

        internal static void RequireEnabled()
        {
            if (!Enabled) throw new NotSupportedException(DisabledMessage);
        }
    }
}
