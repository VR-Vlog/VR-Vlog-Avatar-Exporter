using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using PresetAssignment = VRVlog.LilToonExporter.ExperimentalExpressionCaptureSession.PresetAssignment;

namespace VRVlog.LilToonExporter
{
    // Per-avatar emotion preset choices for the next export. They live in the
    // project's UserSettings, so the avatar, its scene and its prefab stay
    // unchanged (no component, no fingerprint change, no VRChat upload impact).
    internal static class ExpressionPresetStore
    {
        [Serializable]
        sealed class AvatarEntry
        {
            public string Avatar;
            public List<PresetAssignment> Presets = new List<PresetAssignment>();
        }

        [Serializable]
        sealed class Store
        {
            public int Version = 1;
            public List<AvatarEntry> Avatars = new List<AvatarEntry>();
        }

        const int MaximumAvatars = 256;
        internal static string PathOverride;
        static string FilePath => PathOverride ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "VRVlogLilToonExporter", "ExpressionPresets.json"));

        // Scene objects in an unsaved scene have no stable identity; their
        // choices last only for the open window.
        internal static string KeyFor(GameObject avatar)
        {
            if (avatar == null) return null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(avatar);
            return id.identifierType == 0 || id.assetGUID.Empty() ? null : id.ToString();
        }

        // Unreadable data, removed clips and unknown presets are skipped: a
        // stale preference must never prevent preparing or saving the VRM.
        internal static List<PresetAssignment> Load(string key, out int skipped)
        {
            skipped = 0;
            var result = new List<PresetAssignment>();
            if (string.IsNullOrEmpty(key)) return result;
            var entry = (Read(out _) ?? new Store()).Avatars.FirstOrDefault(item => item != null && item.Avatar == key);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in entry?.Presets ?? new List<PresetAssignment>())
            {
                try
                {
                    var loaded = ExperimentalExpressionCaptureSession.LoadPresetsForStore(value);
                    if (!seen.Add(loaded.Preset)) throw new InvalidOperationException();
                    result.Add(loaded);
                }
                catch (InvalidOperationException) { skipped++; }
            }
            return result;
        }

        internal static void Save(string key, IEnumerable<PresetAssignment> assignments)
        {
            if (string.IsNullOrEmpty(key)) return;
            // A file from another format version is left untouched.
            var store = Read(out var foreign);
            if (foreign) return;
            store = store ?? new Store();
            store.Avatars.RemoveAll(item => item == null || item.Avatar == key);
            var saved = (assignments ?? Array.Empty<PresetAssignment>()).Where(value => value != null && !value.Automatic)
                .Select(value => value.ForSaving()).Where(value => value != null).ToList();
            if (saved.Count > 0) store.Avatars.Add(new AvatarEntry { Avatar = key, Presets = saved });
            if (store.Avatars.Count > MaximumAvatars) store.Avatars.RemoveRange(0, store.Avatars.Count - MaximumAvatars);
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ExperimentalExpressionCaptureWindow.AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(store, true)));
        }

        static Store Read(out bool foreign)
        {
            foreign = false;
            try
            {
                var path = FilePath;
                if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return null;
                var store = JsonUtility.FromJson<Store>(File.ReadAllText(path));
                if (store != null && store.Version != 1) { foreign = true; return null; }
                return store != null && store.Avatars != null ? store : null;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException) { return null; }
        }
    }
}
