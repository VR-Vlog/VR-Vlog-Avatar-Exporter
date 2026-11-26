using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRVlog.AvatarAnimator;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // The FX's enabled targets of the parts the other extensions write by
    // table (#3667, Documentation~/decisions/2026-11-16-fx-part-enabled.md): a
    // ParticleSystem, TrailRenderer, LineRenderer and Light of
    // VRVLOG_avatar_effects (component particle, trail, line, light), a
    // Contact Sender or Receiver of VRVLOG_avatar_contacts (contact: the
    // senders, then the receivers) and a PhysBone of VRVLOG_avatar_physbones
    // (physBone), each by its index in that table.
    //
    // Two kinds of curve turn one: its own enabled (m_Enabled, and a particle
    // system's EmissionModule.enabled), and the activeSelf of its object or
    // an ancestor that the file has no node for. The export removes the
    // objects that hold only such parts, so their activeSelf curves had no
    // target; Unity runs no part under an inactive object, so each becomes
    // an enabled target of every part under it. A player turns a part on
    // when every enabled target over it is on: they are separate targets,
    // one per curve, even on the same index.
    //
    // The format does not know these components, so a target carries its
    // keys (node, property, component, index, rest) in Fields and a reader
    // keeps them as written (Target.Known false), as it does with any
    // component it does not know. Their tracks come after every other track
    // of a clip and take the room left, and their curves take the keys the
    // others leave, so they never leave out what the file wrote before.
    internal sealed partial class AvatarAnimatorCapture
    {
        // A part a table may write, as read from the copy before the export
        // removes objects.
        internal sealed class Part
        {
            // particle, trail, line, light, contact or physBone.
            internal string Component;
            // The component on the copy; Unity's null once the export removed it.
            internal UnityEngine.Component Source;
            // The full name of the component's type (a binding's type).
            internal string Type;
            // The path from the avatar root of its object, then of each
            // ancestor below the root, with each one's activeSelf. A path
            // Unity binds to another object (a sibling of the same name
            // before it) is null: a curve on it does not turn this part.
            internal string[] Paths;
            internal bool[] ActiveSelf;
            // Its own enabled when read: the component's (a particle system's
            // renderer for m_Enabled) and a particle system's emission.
            internal bool Enabled, Emission;
        }

        // Reads a part of component kind from the component under root.
        internal static Part ReadPart(string kind, UnityEngine.Component component, Transform root)
        {
            var paths = new List<string>();
            var active = new List<bool>();
            for (var current = component.transform; current != null && current != root; current = current.parent)
            {
                var path = AnimationUtility.CalculateTransformPath(current, root);
                paths.Add(root.Find(path) == current ? path : null);
                active.Add(current.gameObject.activeSelf);
            }
            var system = component as ParticleSystem;
            var renderer = system != null ? system.GetComponent<ParticleSystemRenderer>() : component as Renderer;
            return new Part
            {
                Component = kind, Source = component, Type = component.GetType().FullName,
                Paths = paths.ToArray(), ActiveSelf = active.ToArray(),
                Enabled = renderer != null ? renderer.enabled : !(component is Behaviour behaviour) || behaviour.enabled,
                Emission = system != null && system.emission.enabled,
            };
        }

        // The PhysBones of the copy as parts; the table reads them later.
        internal static IEnumerable<Part> PhysBoneParts(GameObject clone) =>
            clone == null ? Enumerable.Empty<Part>()
                : clone.GetComponentsInChildren<UnityEngine.Component>(true).Where(PhysBoneSpringExport.IsPhysBone).Select(c => ReadPart("physBone", c, clone.transform));

        // One Unity curve target that turns parts: an object's activeSelf
        // (ActiveTarget its active target, -1 when the copy no longer has the
        // object) or a part's own enabled curve (Property).
        sealed class PartSource
        {
            internal int[] Parts;
            internal int Rest;
            internal int ActiveTarget = -1;
            // Where its curves count when no part takes them, as before.
            internal string OmitKind;
        }

        readonly List<Part> parts = new List<Part>();
        // The part targets the last Build wrote.
        int partTargetCount;
        // The parts under each path, by the path of their object or an ancestor.
        readonly Dictionary<string, List<int>> partsUnder = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        readonly List<PartSource> partSources = new List<PartSource>();
        readonly Dictionary<string, int> partSourceIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<List<(int Source, ExpressionAnimationData.Curve Curve)>> clipPartTracks = new List<List<(int, ExpressionAnimationData.Curve)>>();

        void ReadParts(IEnumerable<Part> read)
        {
            if (read == null) return;
            foreach (var part in read)
            {
                if (part?.Paths == null || part.Paths.All(path => path == null)) continue;
                var index = parts.Count;
                parts.Add(part);
                foreach (var path in part.Paths.Where(path => path != null))
                {
                    if (!partsUnder.TryGetValue(path, out var under)) partsUnder[path] = under = new List<int>();
                    under.Add(index);
                }
            }
        }

        bool HasPartsUnder(string path) => path != null && partsUnder.ContainsKey(path);

        // The part kind a binding's own enabled curve turns, or null.
        static string PartSwitch(EditorCurveBinding binding)
        {
            if (binding.type == typeof(ParticleSystem)) return binding.propertyName == "EmissionModule.enabled" ? "particle" : null;
            if (binding.propertyName != "m_Enabled") return null;
            if (binding.type == typeof(ParticleSystemRenderer)) return "particle";
            if (binding.type == typeof(TrailRenderer)) return "trail";
            if (binding.type == typeof(LineRenderer)) return "line";
            if (binding.type == typeof(Light)) return "light";
            var name = binding.type?.FullName;
            if (name == AvatarContactsExport.SenderType || name == AvatarContactsExport.ReceiverType) return "contact";
            if (name == PhysBoneSpringExport.PhysBoneType) return "physBone";
            return null;
        }

        // The source of an own enabled curve: the first part of its kind on
        // the binding's object, of the binding's type (a particle system for
        // its renderer), as Unity binds the first component of a type.
        int OwnPartSource(EditorCurveBinding binding, string kind, string omitKind)
        {
            var type = binding.type == typeof(ParticleSystemRenderer) ? typeof(ParticleSystem).FullName : binding.type.FullName;
            if (!partsUnder.TryGetValue(binding.path, out var under)) return -1;
            var found = under.Where(p => parts[p].Component == kind && parts[p].Type == type && parts[p].Paths[0] == binding.path).DefaultIfEmpty(-1).First();
            if (found < 0) return -1;
            var key = "own\n" + found + "\n" + binding.type.FullName + "\n" + binding.propertyName;
            if (partSourceIndex.TryGetValue(key, out var index)) return index;
            var part = parts[found];
            var rest = binding.propertyName == "EmissionModule.enabled" ? part.Emission : part.Enabled;
            partSourceIndex[key] = partSources.Count;
            partSources.Add(new PartSource { Parts = new[] { found }, Rest = rest ? 1 : 0, OmitKind = omitKind });
            return partSources.Count - 1;
        }

        // The source of an activeSelf curve on path over parts: activeTarget
        // when the copy has the object, else the parts' record of it.
        int ActivePartSource(string path, int activeTarget)
        {
            var key = "active\n" + path;
            if (partSourceIndex.TryGetValue(key, out var index)) return index;
            var under = partsUnder[path];
            var first = parts[under[0]];
            var rest = activeTarget >= 0 ? targets[activeTarget].Default : first.ActiveSelf[Array.IndexOf(first.Paths, path)] ? 1 : 0;
            partSourceIndex[key] = partSources.Count;
            partSources.Add(new PartSource { Parts = under.ToArray(), Rest = rest, ActiveTarget = activeTarget, OmitKind = "missingNode" });
            return partSources.Count - 1;
        }

        // Adds the part targets (after every other target) and returns, for
        // each source, its targets; null for an active source whose object
        // the file has a node for (its active target turns it).
        List<int>[] PartTargets(int[] map, Func<UnityEngine.Component, (int Index, int Node)> part)
        {
            partTargetCount = 0;
            var result = new List<int>[partSources.Count];
            for (var s = 0; s < partSources.Count; s++)
            {
                var source = partSources[s];
                if (source.ActiveTarget >= 0 && map[source.ActiveTarget] >= 0) continue;
                result[s] = new List<int>();
                if (part == null) continue;
                foreach (var p in source.Parts)
                {
                    var (index, node) = part(parts[p].Source);
                    if (index < 0) continue;
                    var component = parts[p].Component;
                    partTargetCount++;
                    result[s].Add(data.Targets.Count);
                    data.Targets.Add(new AvatarAnimatorData.Target
                    {
                        Node = -1, Property = "enabled", Component = component, Index = index, Rest = source.Rest,
                        Fields = new Dictionary<string, object>
                        {
                            ["node"] = (long)node, ["property"] = "enabled", ["component"] = component, ["index"] = (long)index, ["rest"] = (long)source.Rest
                        }
                    });
                }
            }
            return result;
        }
    }
}
