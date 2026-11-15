using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VRVlog.AvatarPhysBones;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // VRVLOG_avatar_physbones (Schema/VRVLOG_avatar_physbones.1.schema.json):
    // the settings of each PhysBone the app needs to feed the parameters
    // VRChat writes for it (<parameter>_IsGrabbed, _IsPosed, _Angle, _Stretch,
    // _Squish). The swing stays the SpringBone PhysBoneSpringExport writes.
    // Capture reads the PhysBones on the export copy before the conversion,
    // in hierarchy order; Resolve binds each root to its output node once
    // the nodes are known and leaves out a PhysBone whose root the file has
    // no node for. Older apps do not read the extension.
    internal sealed class AvatarPhysBonesExport
    {
        sealed class Entry
        {
            internal Transform Root;
            internal AvatarPhysBonesData.PhysBone Values;
        }

        readonly List<Entry> entries = new List<Entry>();

        internal int Count => entries.Count;

        // Null when the copy has no PhysBone.
        internal static AvatarPhysBonesExport Capture(GameObject copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            var capture = new AvatarPhysBonesExport();
            foreach (var component in PhysBones(copy))
            {
                var root = Member(component, "rootTransform") as Transform;
                // Unity's missing-object sentinel is not CLR null.
                if (root == null) root = component.transform;
                capture.entries.Add(new Entry
                {
                    Root = root,
                    Values = new AvatarPhysBonesData.PhysBone
                    {
                        Parameter = Member(component, "parameter") as string ?? "",
                        MaxStretch = Math.Max(0, Number(Member(component, "maxStretch"))),
                        // PhysBone 1.0 has no squish; SDKs before 1.1 have no field.
                        MaxSquish = Math.Max(0, Number(Member(component, "maxSquish"))),
                        AllowGrabbing = Allowed(Member(component, "allowGrabbing")),
                        AllowPosing = Allowed(Member(component, "allowPosing")),
                    }
                });
            }
            return capture.entries.Count > 0 ? capture : null;
        }

        // The FX parameter names VRChat writes for the copy's PhysBones,
        // enabled or not: VRChat owns them, so the FX extension marks them builtin.
        internal static HashSet<string> ParameterNames(GameObject copy)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (copy == null) return names;
            foreach (var component in PhysBones(copy))
                names.UnionWith(AvatarPhysBonesData.ParameterNames(Member(component, "parameter") as string));
            return names;
        }

        static IEnumerable<Component> PhysBones(GameObject copy) =>
            copy.GetComponentsInChildren<Component>(true).Where(PhysBoneSpringExport.IsPhysBone);

        // An entry per PhysBone whose root the file has a node for, in the
        // order they were read; null when there is none.
        internal AvatarPhysBonesData Resolve(Func<GameObject, int> node)
        {
            var data = new AvatarPhysBonesData();
            foreach (var entry in entries)
            {
                var index = entry.Root == null ? -1 : node(entry.Root.gameObject);
                if (index < 0) continue;
                var values = entry.Values;
                data.PhysBones.Add(new AvatarPhysBonesData.PhysBone
                {
                    Node = index, Parameter = values.Parameter, MaxStretch = values.MaxStretch, MaxSquish = values.MaxSquish,
                    AllowGrabbing = values.AllowGrabbing, AllowPosing = values.AllowPosing, LimitAngle = values.LimitAngle,
                });
            }
            return data.PhysBones.Count > 0 ? data : null;
        }

        internal static byte[] Inject(byte[] bytes, AvatarPhysBonesData data)
        {
            if (data == null || data.PhysBones.Count == 0) return bytes;
            var document = GlbDocument.Read(bytes);
            var nodes = document.Json.TryGetValue("nodes", out var rawNodes) && rawNodes is List<object> list ? list.Count : 0;
            if (data.PhysBones.Any(p => p.Node >= nodes)) throw new InvalidOperationException("Avatar PhysBone node is out of range.");
            document.PlaceExtension(AvatarPhysBonesData.Extension, AvatarPhysBonesData.Write(data));
            return document.Write();
        }

        // VRChat's AdvancedBool (False, True, Other: allowed for some) or a bool on older SDKs.
        static bool Allowed(object value) => value is bool flag ? flag : value is Enum choice && choice.ToString() != "False";

        static double Number(object value) => value is float single && !float.IsNaN(single) && !float.IsInfinity(single) ? single : 0;

        static object Member(Component source, string name)
        {
            for (var type = source.GetType(); type != null; type = type.BaseType)
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                var field = type.GetField(name, flags); if (field != null) return field.GetValue(source);
            }
            return null;
        }
    }
}
