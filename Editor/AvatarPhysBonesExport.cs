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

        // Null when the copy has no PhysBone. VRChat removes objects tagged
        // EditorOnly at upload, so their PhysBones are not read.
        internal static AvatarPhysBonesExport Capture(GameObject copy)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            var capture = new AvatarPhysBonesExport();
            foreach (var component in copy.GetComponentsInChildren<Component>(true).Where(PhysBoneSpringExport.IsPhysBone))
            {
                if (EditorOnly(component.transform, copy.transform)) continue;
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
                        MaxSquish = Member(component, "version") is Enum version && Convert.ToInt32(version) == 0
                            ? 0 : Math.Max(0, Number(Member(component, "maxSquish"))),
                        AllowGrabbing = Allowed(Member(component, "allowGrabbing")),
                        AllowPosing = Allowed(Member(component, "allowPosing")),
                    }
                });
            }
            return capture.entries.Count > 0 ? capture : null;
        }

        // The parameter names VRChat writes for the entries of the table,
        // enabled or not: the FX extension marks them builtin, so a name is
        // builtin exactly when the file carries the PhysBone that feeds it.
        // Empty for null.
        internal static HashSet<string> ParameterNames(AvatarPhysBonesData data)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (data != null)
                foreach (var physBone in data.PhysBones)
                    names.UnionWith(AvatarPhysBonesData.ParameterNames(physBone.Parameter));
            return names;
        }

        static bool EditorOnly(Transform transform, Transform root)
        {
            for (var current = transform; current != null; current = current == root ? null : current.parent)
                if (current.CompareTag("EditorOnly")) return true;
            return false;
        }

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
            Checked(data, nodes);
            document.PlaceExtension(AvatarPhysBonesData.Extension, AvatarPhysBonesData.Write(data));
            return document.Write();
        }

        // The data when it fits the contract and every node is below nodes;
        // throws otherwise. Null stays null.
        internal static AvatarPhysBonesData Checked(AvatarPhysBonesData data, int nodes)
        {
            if (data == null) return null;
            AvatarPhysBonesData.Write(data);
            if (data.PhysBones.Any(p => p.Node >= nodes)) throw new InvalidOperationException("Avatar PhysBone node is out of range.");
            return data;
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
