using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // Where a transfer keeps the VRM and encrypted copies it makes for itself.
    // The system temp folder can be missing, read-only or full, so the folders
    // below are tried in order and the first one that takes a test file wins.
    // Only when none does is the transfer given up. The message is the
    // untranslated source text; the windows translate it where they show it.
    internal static class TransferTempDirectory
    {
        internal const string NoWritableFolder = "転送用の一時ファイルを置ける場所が見つかりません。空き容量と、一時フォルダーへの書き込み権限を確認してください。";

        internal const string ShownAsIs = "VRVlog.ShowMessageAsIs";

        // A transfer writes the VRM and an encrypted copy of the same size.
        internal static long RequiredFreeBytes = 2L * CloudTransferProtocol.MaximumSize;

        // Copies left by a crashed or killed editor. Links live for minutes, so a day is safely stale.
        internal static readonly TimeSpan StaleAge = TimeSpan.FromDays(1);
        static readonly Regex OwnedName = new Regex("^[0-9a-f]{32}\\.(vrm|bin)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // Tests replace the candidate roots.
        internal static Func<IEnumerable<string>> TestRoots = null;

        // The encryption runs off the main thread, where Unity paths cannot be read.
#if !VRVLOG_LAN_TRANSFER_CLI
        static string cachePath;
        [UnityEditor.InitializeOnLoadMethod]
        static void CaptureCachePath() => cachePath = Safe(() => UnityEngine.Application.temporaryCachePath);
#endif

        internal static IEnumerable<string> Roots()
        {
            if (TestRoots != null)
            {
                foreach (var root in TestRoots()) yield return root;
                yield break;
            }
            yield return Safe(Path.GetTempPath);
#if !VRVLOG_LAN_TRANSFER_CLI
            yield return cachePath;
#endif
            yield return Safe(() => Path.Combine(Directory.GetCurrentDirectory(), "Temp"));
            yield return Safe(() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRVlog"));
        }

        static string Safe(Func<string> root)
        {
            try { return root(); }
            catch (Exception) { return null; }
        }

        // A new file name in the first usable folder. Folders that work but lack
        // the free space for a transfer are used only when nothing else works.
        internal static string NewFile(string folder, string extension)
        {
            string low = null;
            foreach (var root in Roots().Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var directory = Writable(root, folder);
                if (directory == null) continue;
                if (HasFreeSpace(directory)) return Name(directory, extension);
                low = low ?? directory;
            }
            if (low != null) return Name(low, extension);
            var failure = new InvalidOperationException(NoWritableFolder);
            // Callers that wrap transfer errors in a generic message show this one as is.
            failure.Data[ShownAsIs] = true;
            throw failure;
        }

        static string Name(string directory, string extension) => Path.Combine(directory, Guid.NewGuid().ToString("N") + extension);

        static string Writable(string root, string folder)
        {
            try
            {
                var directory = Path.Combine(Path.GetFullPath(root), folder);
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, "." + Guid.NewGuid().ToString("N") + ".probe");
                try
                {
                    using (var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.WriteByte(0);
                }
                finally { LanVrmTransferServer.TryDelete(probe); }
                SweepStale(directory);
                return directory;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                              exception is ArgumentException || exception is NotSupportedException ||
                                              exception is System.Security.SecurityException)
            {
                return null;
            }
        }

        static bool HasFreeSpace(string directory)
        {
            try { return new DriveInfo(Path.GetPathRoot(directory)).AvailableFreeSpace >= RequiredFreeBytes; }
            // Network and unusual paths have no drive information; let the write decide.
            catch (Exception) { return true; }
        }

        // Only files this exporter names (32 hex digits) in its own folders are removed.
        internal static void SweepStale(string directory)
        {
            try
            {
                var limit = DateTime.UtcNow - StaleAge;
                foreach (var file in Directory.GetFiles(directory))
                    if (OwnedName.IsMatch(Path.GetFileName(file)) && File.GetLastWriteTimeUtc(file) < limit)
                        LanVrmTransferServer.TryDelete(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
