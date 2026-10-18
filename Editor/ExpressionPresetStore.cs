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
            // Faces the user chose not to export. Older files have no such
            // field and read as an empty list.
            public List<ClipReference> Excluded = new List<ClipReference>();
        }

        [Serializable]
        sealed class ClipReference
        {
            public string Guid;
            public long LocalId;
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

        // Clips that are no longer in the project are skipped the same way.
        internal static List<AnimationClip> LoadExcluded(string key, out int skipped)
        {
            skipped = 0;
            var result = new List<AnimationClip>();
            if (string.IsNullOrEmpty(key)) return result;
            var entry = (Read(out _) ?? new Store()).Avatars.FirstOrDefault(item => item != null && item.Avatar == key);
            foreach (var reference in entry?.Excluded ?? new List<ClipReference>())
            {
                var clip = reference == null || string.IsNullOrEmpty(reference.Guid) ? null
                    : AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(reference.Guid)).OfType<AnimationClip>().FirstOrDefault(candidate =>
                        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long id) && id == reference.LocalId);
                if (clip == null) skipped++;
                else if (!result.Contains(clip)) result.Add(clip);
            }
            return result;
        }

        // Without an excluded list the stored exclusions are kept as they are.
        internal static void Save(string key, IEnumerable<PresetAssignment> assignments, IEnumerable<AnimationClip> excluded = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            // A file from another format version is left untouched.
            var store = Read(out var foreign);
            if (foreign) return;
            store = store ?? new Store();
            var previous = store.Avatars.LastOrDefault(item => item != null && item.Avatar == key)?.Excluded ?? new List<ClipReference>();
            store.Avatars.RemoveAll(item => item == null || item.Avatar == key);
            var saved = (assignments ?? Array.Empty<PresetAssignment>()).Where(value => value != null && !value.Automatic)
                .Select(value => value.ForSaving()).Where(value => value != null).ToList();
            var references = excluded == null ? previous.Where(item => item != null).ToList() : References(excluded);
            if (saved.Count > 0 || references.Count > 0) store.Avatars.Add(new AvatarEntry { Avatar = key, Presets = saved, Excluded = references });
            if (store.Avatars.Count > MaximumAvatars) store.Avatars.RemoveRange(0, store.Avatars.Count - MaximumAvatars);
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ExperimentalExpressionCaptureWindow.AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(store, true)));
        }

        // Persisted form references the clip asset, never an instance ID.
        static List<ClipReference> References(IEnumerable<AnimationClip> clips)
        {
            var result = new List<ClipReference>();
            foreach (var clip in clips.Where(clip => clip != null).Distinct())
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id) && !string.IsNullOrEmpty(guid))
                    result.Add(new ClipReference { Guid = guid, LocalId = id });
            return result;
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
