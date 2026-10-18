using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // Holds object identities across authoring passes; never resolves renamed
    // or duplicate objects by a textual animation path.
    internal sealed class ExportGimmickSession : IDisposable
    {
        readonly GameObject clone;
        readonly bool enabled;
        readonly HashSet<Transform> included = new HashSet<Transform>();
        readonly List<Object> assets = new List<Object>();
        readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> removedPaths = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<Transform> removedTransforms = new HashSet<Transform>();
        readonly List<Component> removedComponents = new List<Component>();

        internal bool ContainsPath(string path)
        {
            if (path == null) return false;
            // Removing a render component does not remove every binding on its
            // GameObject. Resolve the current hierarchy so a retained skin on
            // that object, or a skin moved into an old path, keeps its morphs.
            if (clone.GetComponentsInChildren<SkinnedMeshRenderer>().Any(skin =>
                skin.enabled && skin.sharedMesh != null && skin.gameObject.activeInHierarchy &&
                AnimationUtility.CalculateTransformPath(skin.transform, clone.transform) == path)) return false;
            return removedPaths.Contains(path) || removedTransforms.Any(target => target != null &&
                AnimationUtility.CalculateTransformPath(target, clone.transform) == path);
        }

        internal ExportGimmickSession(GameObject source, GameObject clone, ExportGimmickOptions options,
            IEnumerable<ExportGimmickFinding> findings = null)
        {
            if (source == null || clone == null || source == clone) throw new ArgumentException("An independent export clone is required.");
            this.clone = clone;
            enabled = options?.AutoExclude != false;
            var kept = (options?.IncludedObjects ?? Array.Empty<GameObject>()).ToArray();
            var copies = new Dictionary<Transform, Transform>();
            void Map(Transform from, Transform to)
            {
                copies[from] = to;
                if (ExportGimmickDetection.Kept(from, kept)) included.Add(to);
                for (var i = 0; i < from.childCount; i++) Map(from.GetChild(i), to.GetChild(i));
            }
            Map(source.transform, clone.transform);
            if (findings == null) return;
            // The copy keeps the source's component order, so a component is
            // found again by its index and type, never by a name.
            foreach (var component in ExportGimmickDetection.AutomaticComponents(findings, options).SelectMany(f => f.Components))
            {
                if (component == null || !copies.TryGetValue(component.transform, out var copy)) continue;
                var index = Array.IndexOf(component.GetComponents<Component>(), component);
                var candidates = copy.GetComponents<Component>();
                if (index >= 0 && index < candidates.Length && candidates[index] != null && candidates[index].GetType() == component.GetType())
                    removedComponents.Add(candidates[index]);
            }
        }

        internal void Apply(PreparedExpressionBindings bindings, VrChatExpressionMenu.Source menu, ICollection<string> warnings)
        {
            if (!enabled) return;
            // Settings that build tools read are removed before they run.
            foreach (var component in removedComponents) if (component != null) Object.DestroyImmediate(component);
            removedComponents.Clear();
            bool Kept(Transform target)
            {
                for (var t = target; t != null; t = t.parent) if (included.Contains(t)) return true;
                return false;
            }
            var removed = new HashSet<Renderer>();
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                if (Kept(renderer.transform)) continue;
                var finding = ExportGimmickDetection.Inspect(renderer);
                if (finding == null) continue;
                var path = AnimationUtility.CalculateTransformPath(renderer.transform, clone.transform);
                var message = (finding.Unit == GimmickExclusionUnit.Renderer ? "補助Rendererを省略: " : "自動除外せず保持: ") + path + " — " + finding.Reason;
                if (reported.Add(message)) warnings?.Add(message);
                if (finding.Unit == GimmickExclusionUnit.Renderer) removed.Add(renderer);
            }
            if (removed.Count == 0) return;
            bindings?.FilterRemoved(menu, removed, warnings);
            ExportReferencePruning.Prepare(clone, Array.Empty<Transform>(), assets, warnings, removed);
            foreach (var renderer in removed)
            {
                removedPaths.Add(AnimationUtility.CalculateTransformPath(renderer.transform, clone.transform));
                removedTransforms.Add(renderer.transform);
                // Delete the render component, not its Transform: a retained
                // skin, constraint, or ordinary child may still depend on it.
                Object.DestroyImmediate(renderer);
            }
        }

        public void Dispose()
        {
            foreach (var asset in assets) if (asset != null) Object.DestroyImmediate(asset);
        }
    }
}
