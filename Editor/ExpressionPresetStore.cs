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
            // Names the user gave faces on the board; older files have none.
            public List<ClipName> Names = new List<ClipName>();
        }

        [Serializable]
        sealed class ClipReference
        {
            public string Guid;
            public long LocalId;
        }

        [Serializable]
        sealed class ClipName
        {
            public string Guid;
            public long LocalId;
            public string Name;
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
                var clip = reference == null ? null : Resolve(reference.Guid, reference.LocalId);
                if (clip == null) skipped++;
                else if (!result.Contains(clip)) result.Add(clip);
            }
            return result;
        }

        // Names of clips no longer in the project, and names the board would
        // not take (empty, over 128 characters, control characters), are skipped.
        internal static Dictionary<AnimationClip, string> LoadNames(string key, out int skipped)
        {
            skipped = 0;
            var result = new Dictionary<AnimationClip, string>();
            if (string.IsNullOrEmpty(key)) return result;
            var entry = (Read(out _) ?? new Store()).Avatars.FirstOrDefault(item => item != null && item.Avatar == key);
            foreach (var named in entry?.Names ?? new List<ClipName>())
            {
                var clip = named == null ? null : Resolve(named.Guid, named.LocalId);
                var name = named?.Name?.Trim();
                if (clip == null || string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(char.IsControl) || result.ContainsKey(clip)) skipped++;
                else result.Add(clip, name);
            }
            return result;
        }

        static AnimationClip Resolve(string assetGuid, long localId) => string.IsNullOrEmpty(assetGuid) ? null
            : AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(assetGuid)).OfType<AnimationClip>().FirstOrDefault(candidate =>
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(candidate, out string guid, out long id) && id == localId);

        // Without an excluded list or names the stored ones are kept as they are.
        internal static void Save(string key, IEnumerable<PresetAssignment> assignments, IEnumerable<AnimationClip> excluded = null,
            IEnumerable<KeyValuePair<AnimationClip, string>> names = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            // A file from another format version or one that cannot be read is
            // left untouched, so other avatars' choices are never lost.
            var store = Read(out var problem);
            if (problem != null) { ExportSettingsStore.WarnOnce(problem); return; }
            store = store ?? new Store();
            var stored = store.Avatars.LastOrDefault(item => item != null && item.Avatar == key);
            var previous = stored?.Excluded ?? new List<ClipReference>();
            var previousNames = stored?.Names ?? new List<ClipName>();
            store.Avatars.RemoveAll(item => item == null || item.Avatar == key);
            var saved = (assignments ?? Array.Empty<PresetAssignment>()).Where(value => value != null && !value.Automatic)
                .Select(value => value.ForSaving()).Where(value => value != null).ToList();
            var references = excluded == null ? previous.Where(item => item != null).ToList() : References(excluded);
            var named = names == null ? previousNames.Where(item => item != null).ToList() : Named(names);
            if (saved.Count > 0 || references.Count > 0 || named.Count > 0)
                store.Avatars.Add(new AvatarEntry { Avatar = key, Presets = saved, Excluded = references, Names = named });
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

        static List<ClipName> Named(IEnumerable<KeyValuePair<AnimationClip, string>> names)
        {
            var result = new List<ClipName>();
            foreach (var pair in names)
                if (pair.Key != null && !string.IsNullOrWhiteSpace(pair.Value) &&
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(pair.Key, out string guid, out long id) && !string.IsNullOrEmpty(guid) &&
                    !result.Any(item => item.Guid == guid && item.LocalId == id))
                    result.Add(new ClipName { Guid = guid, LocalId = id, Name = pair.Value.Trim() });
            return result;
        }

        // problem is the warning to show instead of saving, when the file is
        // there but from another format version or cannot be read.
        static Store Read(out string problem)
        {
            problem = null;
            var path = FilePath;
            try
            {
                if (!File.Exists(path)) return null;
                if (new FileInfo(path).Length > 4 * 1024 * 1024) { problem = Unreadable(path); return null; }
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                var store = JsonUtility.FromJson<Store>(text);
                if (store != null && store.Version != 1) { problem = string.Format(ExporterLocalization.T(OtherVersionFormat), path); return null; }
                if (store == null || store.Avatars == null) { problem = Unreadable(path); return null; }
                return store;
            }
            // A lock or missing permission is not a broken file: deleting it
            // would lose every avatar's choices, so that message does not ask to.
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                problem = string.Format(ExporterLocalization.T(CannotOpenFormat), path);
                return null;
            }
            catch (ArgumentException)
            {
                problem = Unreadable(path);
                return null;
            }
        }

        internal const string OtherVersionFormat = "表情ボードの選択を記録したファイルが別の版のため、選択を保存しませんでした: {0}";
        internal const string CannotOpenFormat = "表情ボードの選択を記録したファイルを開けないため、選択を保存しませんでした。別のプログラムが使っていないか確かめてください: {0}";

        static string Unreadable(string path) =>
            string.Format(ExporterLocalization.T("表情ボードの選択を記録したファイルを読み込めないため、選択を保存しませんでした。ファイルを直すか削除すると、また保存できます: {0}"), path);
    }
}
