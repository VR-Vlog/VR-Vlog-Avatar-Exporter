using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // Per-avatar ExportSettings in the project's UserSettings, keyed by the
    // avatar's GlobalObjectId like ExpressionPresetStore. The avatar, its scene
    // and its prefab stay unchanged. Objects are kept by GlobalObjectId too and
    // are only restored when they still belong to the avatar; anything that no
    // longer resolves is skipped, so a stale entry never blocks an export.
    internal static class ExportSettingsStore
    {
        [Serializable]
        sealed class Binding
        {
            public string Renderer;
            public string Shape = "";
            public float Weight = 100f;
        }

        [Serializable]
        sealed class Pose
        {
            public string Clip;
            public float Time;
            public string Name;
            public string Category;
        }

        [Serializable]
        sealed class PoseName
        {
            public string Key;
            public string Value;
        }

        [Serializable]
        sealed class AvatarEntry
        {
            public string Avatar;
            public List<string> Excluded = new List<string>();
            public bool AutoExcludeGimmicks = true;
            public List<string> Included = new List<string>();
            public List<string> IncludedComponents = new List<string>();
            public BlinkExportMode BlinkMode;
            public List<Binding> BlinkBoth = new List<Binding>();
            public List<Binding> BlinkLeft = new List<Binding>();
            public List<Binding> BlinkRight = new List<Binding>();
            public List<Pose> ManualPoses = new List<Pose>();
            public List<string> ExcludedPoses = new List<string>();
            public List<PoseName> PoseNames = new List<PoseName>();
            public AvatarLicenseOptions License = new AvatarLicenseOptions();
            // The name of the last successful "アプリに転送". It is not part of
            // ExportSettings, so it never changes the settings' signature.
            public string TransferName;
        }

        [Serializable]
        sealed class Store
        {
            public int Version = 1;
            public List<AvatarEntry> Avatars = new List<AvatarEntry>();
        }

        const int MaximumAvatars = 256;
        internal static string PathOverride;
        static string FilePath => PathOverride ?? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserSettings", "VRVlogLilToonExporter", "ExportSettings.json"));

        // Avatars without a stable key (an unsaved scene) keep their settings
        // here until the editor reloads scripts.
        static readonly Dictionary<int, ExportSettings> Unsaved = new Dictionary<int, ExportSettings>();
        static readonly Dictionary<int, string> UnsavedNames = new Dictionary<int, string>();

        // Increases on every save, so an open window can notice that another
        // window saved and reload what it shows.
        internal static int Revision { get; private set; }

        internal static ExportSettings Load(GameObject avatar) => Load(avatar, out _);

        internal static ExportSettings Load(GameObject avatar, out int skipped)
        {
            skipped = 0;
            if (avatar == null) return new ExportSettings();
            var key = ExpressionPresetStore.KeyFor(avatar);
            if (key == null) return Unsaved.TryGetValue(avatar.GetInstanceID(), out var held) ? held.Copy() : new ExportSettings();
            var entry = (Read(out _) ?? new Store()).Avatars.FirstOrDefault(item => item != null && item.Avatar == key);
            return entry == null ? new ExportSettings() : Restore(avatar, entry, ref skipped);
        }

        internal static void Save(GameObject avatar, ExportSettings settings)
        {
            if (avatar == null || settings == null) return;
            var key = ExpressionPresetStore.KeyFor(avatar);
            Revision++;
            if (key == null) { Unsaved[avatar.GetInstanceID()] = settings.Copy(); return; }
            // A file from another format version is left untouched.
            var store = Read(out var foreign);
            if (foreign) { WarnOnce(ExporterLocalization.T("別の版の書き出し設定ファイルがあるため、書き出し設定を保存しませんでした。")); return; }
            store = store ?? new Store();
            var transferName = store.Avatars.FirstOrDefault(item => item != null && item.Avatar == key)?.TransferName;
            store.Avatars.RemoveAll(item => item == null || item.Avatar == key);
            // Defaults are not written, so the file only lists adjusted avatars
            // and avatars with a remembered transfer name.
            if (settings.Signature() != new ExportSettings().Signature() || !string.IsNullOrEmpty(transferName))
            {
                var entry = Capture(key, avatar, settings);
                entry.TransferName = transferName;
                store.Avatars.Add(entry);
            }
            Write(store);
        }

        internal static string LoadTransferName(GameObject avatar)
        {
            if (avatar == null) return null;
            var key = ExpressionPresetStore.KeyFor(avatar);
            if (key == null) return UnsavedNames.TryGetValue(avatar.GetInstanceID(), out var held) ? held : null;
            var name = (Read(out _) ?? new Store()).Avatars.FirstOrDefault(item => item != null && item.Avatar == key)?.TransferName;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }

        internal static void SaveTransferName(GameObject avatar, string name)
        {
            if (avatar == null) return;
            name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            var key = ExpressionPresetStore.KeyFor(avatar);
            if (key == null)
            {
                if (name == null) UnsavedNames.Remove(avatar.GetInstanceID()); else UnsavedNames[avatar.GetInstanceID()] = name;
                return;
            }
            var store = Read(out var foreign);
            if (foreign) { WarnOnce(ExporterLocalization.T("別の版の書き出し設定ファイルがあるため、書き出し設定を保存しませんでした。")); return; }
            store = store ?? new Store();
            var entry = store.Avatars.FirstOrDefault(item => item != null && item.Avatar == key) ?? Capture(key, avatar, new ExportSettings());
            store.Avatars.RemoveAll(item => item == null || item.Avatar == key);
            entry.TransferName = name;
            // An entry that only held the name is dropped with it.
            var skipped = 0;
            if (name != null || Restore(avatar, entry, ref skipped).Signature() != new ExportSettings().Signature()) store.Avatars.Add(entry);
            Write(store);
        }

        static void Write(Store store)
        {
            if (store.Avatars.Count > MaximumAvatars) store.Avatars.RemoveRange(0, store.Avatars.Count - MaximumAvatars);
            // Remembering is a convenience: a file that cannot be written never stops an export.
            try
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                ExperimentalExpressionCaptureWindow.AtomicWrite(path, System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(store, true)));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                WarnOnce(string.Format(ExporterLocalization.T("書き出し設定を保存できませんでした: {0}"), error.Message));
            }
        }

        static bool warned;

        static void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning(message);
        }

        static AvatarEntry Capture(string key, GameObject avatar, ExportSettings settings)
        {
            string Id(Object value) => value == null ? null : GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();
            List<Binding> Bindings(IEnumerable<BlinkShapeBinding> values) => values.Where(value => value != null)
                .Select(value => new Binding { Renderer = Id(value.Renderer), Shape = value.Shape ?? "", Weight = value.Weight }).ToList();
            var blink = settings.Blink ?? new BlinkExportOptions();
            var poses = settings.Poses ?? new PoseExportOptions();
            return new AvatarEntry
            {
                Avatar = key,
                Excluded = settings.ExcludedObjects.Where(item => item != null).Select(Id).ToList(),
                AutoExcludeGimmicks = settings.AutoExcludeGimmicks,
                Included = settings.IncludedGimmicks.Where(item => item != null).Select(Id).ToList(),
                IncludedComponents = settings.IncludedGimmickComponents.Where(item => item != null).Select(Id).ToList(),
                BlinkMode = blink.Mode,
                BlinkBoth = Bindings(blink.Both),
                BlinkLeft = Bindings(blink.Left),
                BlinkRight = Bindings(blink.Right),
                ManualPoses = poses.Manual.Where(pose => pose != null && pose.Clip != null)
                    .Select(pose => new Pose { Clip = Id(pose.Clip), Time = pose.Time, Name = pose.Name, Category = pose.Category }).ToList(),
                ExcludedPoses = poses.Excluded.OrderBy(item => item, StringComparer.Ordinal).ToList(),
                PoseNames = poses.Names.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new PoseName { Key = item.Key, Value = item.Value }).ToList(),
                License = (settings.License ?? new AvatarLicenseOptions()).Copy()
            };
        }

        static ExportSettings Restore(GameObject avatar, AvatarEntry entry, ref int skipped)
        {
            var missing = 0;
            T Resolve<T>(string id) where T : Object
            {
                if (string.IsNullOrEmpty(id)) return null;
                T value = null;
                if (GlobalObjectId.TryParse(id, out var parsed)) value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) as T;
                if (value == null) missing++;
                return value;
            }
            // Scene objects must still be part of this avatar.
            T Inside<T>(string id) where T : Object
            {
                var value = Resolve<T>(id);
                var transform = value is Component component ? component.transform : value is GameObject gameObject ? gameObject.transform : null;
                if (value == null || transform != null && (transform == avatar.transform || transform.IsChildOf(avatar.transform))) return value;
                missing++;
                return null;
            }
            List<BlinkShapeBinding> Bindings(IEnumerable<Binding> values) => (values ?? Enumerable.Empty<Binding>()).Where(value => value != null)
                .Select(value => new BlinkShapeBinding { Renderer = Inside<SkinnedMeshRenderer>(value.Renderer), Shape = value.Shape ?? "", Weight = Mathf.Clamp(value.Weight, 0f, 100f) }).ToList();

            var settings = new ExportSettings { AutoExcludeGimmicks = entry.AutoExcludeGimmicks };
            settings.ExcludedObjects.AddRange((entry.Excluded ?? new List<string>()).Select(Inside<GameObject>).Where(item => item != null && item != avatar).Distinct());
            settings.IncludedGimmicks.AddRange((entry.Included ?? new List<string>()).Select(Inside<GameObject>).Where(item => item != null && item != avatar).Distinct());
            settings.IncludedGimmickComponents.AddRange((entry.IncludedComponents ?? new List<string>()).Select(Inside<Component>).Where(item => item != null).Distinct());
            settings.Blink = new BlinkExportOptions
            {
                Mode = Enum.IsDefined(typeof(BlinkExportMode), entry.BlinkMode) ? entry.BlinkMode : BlinkExportMode.Auto,
                Both = Bindings(entry.BlinkBoth),
                Left = Bindings(entry.BlinkLeft),
                Right = Bindings(entry.BlinkRight)
            };
            foreach (var pose in entry.ManualPoses ?? new List<Pose>())
            {
                var clip = pose == null ? null : Resolve<AnimationClip>(pose.Clip);
                if (clip == null) continue;
                var manual = new ManualPose { Clip = clip, Time = Mathf.Max(0f, pose.Time), Name = pose.Name };
                if (!string.IsNullOrEmpty(pose.Category)) manual.Category = pose.Category;
                settings.Poses.Manual.Add(manual);
            }
            settings.Poses.Excluded.UnionWith((entry.ExcludedPoses ?? new List<string>()).Where(item => item != null));
            foreach (var name in entry.PoseNames ?? new List<PoseName>())
                if (name?.Key != null && name.Value != null) settings.Poses.Names[name.Key] = name.Value;
            settings.License = entry.License != null ? entry.License.Copy() : new AvatarLicenseOptions();
            skipped += missing;
            return settings;
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
