using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The per-avatar JSON files in the project's UserSettings (ExportSettingsStore,
    // ExpressionPresetStore): one format version, every avatar in one file.
    internal static class UserSettingsFile
    {
        internal interface IStore
        {
            int Version { get; }
            bool HasAvatars { get; }
        }

        // problem is the warning to show instead of saving, when the file is
        // there but from another format version or cannot be read. cannotOpen
        // is for a lock or missing permission.
        internal static T Read<T>(string path, Func<string, string> otherVersion, Func<string, string> unreadable, Func<string, string> cannotOpen,
            out string problem) where T : class, IStore
        {
            problem = null;
            try
            {
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > 4 * 1024 * 1024) { problem = unreadable(path); return null; }
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var store = JsonUtility.FromJson<T>(text);
                if (store != null && store.Version != 1) { problem = otherVersion(path); return null; }
                if (store == null || !store.HasAvatars) { problem = unreadable(path); return null; }
                return store;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                problem = cannotOpen(path);
                return null;
            }
            catch (ArgumentException)
            {
                problem = unreadable(path);
                return null;
            }
        }

        // The oldest avatars are dropped past maximum.
        internal static void Write<TEntry>(string path, IStore store, List<TEntry> avatars, int maximum)
        {
            if (avatars.Count > maximum) avatars.RemoveRange(0, avatars.Count - maximum);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ExportSave.AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(store, true)));
        }
    }
}
