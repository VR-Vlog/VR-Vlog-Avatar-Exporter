using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The choices of one avatar's export: manual exclusions, the gimmick
    // auto-exclusion and its exceptions, blink, poses, the automatic texture
    // lightening and the VRM license.
    // The expression board and the app transfer read them; the export itself
    // runs through the board. ExportSettingsStore remembers them per avatar
    // outside the avatar itself.
    internal sealed class ExportSettings
    {
        internal readonly List<GameObject> ExcludedObjects = new List<GameObject>();
        internal bool AutoExcludeGimmicks = true;
        internal readonly List<GameObject> IncludedGimmicks = new List<GameObject>();
        // Kept components of findings that remove only components. They are
        // separate from IncludedGimmicks so keeping one never keeps its object.
        internal readonly List<Component> IncludedGimmickComponents = new List<Component>();
        internal BlinkExportOptions Blink = new BlinkExportOptions();
        internal PoseExportOptions Poses = new PoseExportOptions();
        // 自動で軽量化する: textures more detailed than the screen shows are
        // written smaller (TexelDensity). Off writes the same file as before.
        internal bool AutoLightweight;
        internal AvatarLicenseOptions License = new AvatarLicenseOptions();

        internal GameObject[] Exclusions() => ExcludedObjects.Where(item => item != null).ToArray();

        internal ExportGimmickOptions Gimmicks() => new ExportGimmickOptions
        {
            AutoExclude = AutoExcludeGimmicks,
            IncludedObjects = IncludedGimmicks.Where(item => item != null).ToArray(),
            IncludedComponents = IncludedGimmickComponents.Where(item => item != null).ToArray()
        };

        internal ExportSettings Copy()
        {
            var copy = new ExportSettings
            {
                AutoExcludeGimmicks = AutoExcludeGimmicks,
                Blink = (Blink ?? new BlinkExportOptions()).Copy(),
                Poses = (Poses ?? new PoseExportOptions()).Copy(),
                AutoLightweight = AutoLightweight,
                License = (License ?? new AvatarLicenseOptions()).Copy()
            };
            copy.ExcludedObjects.AddRange(ExcludedObjects);
            copy.IncludedGimmicks.AddRange(IncludedGimmicks);
            copy.IncludedGimmickComponents.AddRange(IncludedGimmickComponents);
            return copy;
        }

        // Replaces the choices with those of other while keeping this object
        // and its Poses, which an open pose window may be editing.
        internal void Assign(ExportSettings other)
        {
            other = other?.Copy() ?? new ExportSettings();
            ExcludedObjects.Clear(); ExcludedObjects.AddRange(other.ExcludedObjects);
            AutoExcludeGimmicks = other.AutoExcludeGimmicks;
            IncludedGimmicks.Clear(); IncludedGimmicks.AddRange(other.IncludedGimmicks);
            IncludedGimmickComponents.Clear(); IncludedGimmickComponents.AddRange(other.IncludedGimmickComponents);
            Blink = other.Blink;
            if (Poses == null) Poses = new PoseExportOptions();
            Poses.Manual.Clear(); Poses.Manual.AddRange(other.Poses.Manual);
            Poses.Excluded.Clear(); Poses.Excluded.UnionWith(other.Poses.Excluded);
            Poses.Names.Clear(); foreach (var item in other.Poses.Names) Poses.Names[item.Key] = item.Value;
            AutoLightweight = other.AutoLightweight;
            License = other.License;
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
            text.Append('|');
            foreach (var item in IncludedGimmickComponents) Id(item);
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
            text.Append('|').Append(AutoLightweight);
            text.Append('|').Append(JsonUtility.ToJson(License ?? new AvatarLicenseOptions()));
            return text.ToString();
        }
    }
}
