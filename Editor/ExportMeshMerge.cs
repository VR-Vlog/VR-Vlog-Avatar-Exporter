using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Merges the meshes of an avatar that already uses Avatar Optimizer into a
    // few renderers before AAO's Optimizing pass. Three kinds stay separate:
    // renderers an animation switches (on/off, material or other renderer
    // properties), renderers holding shapes the app or the exporter drives by
    // name (the face), and renderers an authored merge or Cloth already owns.
    // The rest becomes one renderer. AAO renames clashing shapes; the face is
    // never merged, so the names the app drives stay as they are.
    // See Documentation~/decisions/2026-11-17-aao-auto-mesh-merge.md.
    internal static class ExportMeshMerge
    {
        internal const string MergedName = "VRVlog Merged";
        const string MergeSkinnedMeshType = "Anatawa12.AvatarOptimizer.MergeSkinnedMesh";

        internal static bool Available => ExportOptimizationMarker.AddMergeComponent != null && ExportOptimizationMarker.AvatarOptimizerAdapterAvailable;

        // The renderers merged into one, in hierarchy order; empty when fewer
        // than two qualify. routed lists the renderers whose shapes or
        // material slots the exporter binds after optimizing.
        internal static List<SkinnedMeshRenderer> Sources(GameObject avatar, IEnumerable<Renderer> routed)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            var keep = new HashSet<Renderer>(routed?.Where(renderer => renderer != null) ?? Enumerable.Empty<Renderer>());
            keep.UnionWith(Switched(avatar));
            keep.UnionWith(AuthoredMergeRenderers(avatar));
            var result = new List<SkinnedMeshRenderer>();
            foreach (var renderer in ExportRendererSelection.ExportedMeshRenderers(avatar))
            {
                if (!(renderer is SkinnedMeshRenderer skin) || keep.Contains(skin)) continue;
                if (skin.GetComponent<Cloth>() != null || DrivesByName(skin.sharedMesh)) continue;
                result.Add(skin);
            }
            if (result.Count < 2) result.Clear();
            return result;
        }

        // Adds the merge when the avatar has AAO and at least two renderers
        // qualify. Returns whether a merge was added.
        internal static bool Apply(GameObject avatar, IEnumerable<Renderer> routed)
        {
            if (!Available || !UsesAvatarOptimizer(avatar)) return false;
            var sources = Sources(avatar, routed);
            if (sources.Count == 0) return false;
            var largest = sources.OrderByDescending(source => source.sharedMesh.vertexCount).First();
            var host = new GameObject(MergedName);
            host.transform.SetParent(avatar.transform, false);
            var merged = host.AddComponent<SkinnedMeshRenderer>();
            // AAO computes the bounds around this root bone; the anchor and the
            // shadow settings follow the largest source so lighting stays alike.
            merged.rootBone = largest.rootBone != null ? largest.rootBone : largest.transform;
            merged.probeAnchor = largest.probeAnchor != null ? largest.probeAnchor : merged.rootBone;
            merged.lightProbeUsage = largest.lightProbeUsage;
            merged.reflectionProbeUsage = largest.reflectionProbeUsage;
            merged.shadowCastingMode = largest.shadowCastingMode;
            merged.receiveShadows = largest.receiveShadows;
            merged.updateWhenOffscreen = largest.updateWhenOffscreen;
            ExportOptimizationMarker.AddMergeComponent(host, sources);
            return true;
        }

        internal static bool UsesAvatarOptimizer(GameObject avatar) =>
            avatar != null && avatar.GetComponentsInChildren<Component>(true)
                .Any(component => component != null && ExportTypeNames.InheritsFrom(component.GetType(), ExportTypeNames.AaoAvatarTag));

        // Shapes the app moves by name (expressions, blink, Lip Sync, tracking)
        // and the shapes the exporter generated for its own expressions.
        static bool DrivesByName(Mesh mesh)
        {
            if (mesh == null) return false;
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                var name = mesh.GetBlendShapeName(index);
                if (name.StartsWith("__VRVlog_", StringComparison.Ordinal) || UnusedMorphTargets.AppDrives(name)) return true;
            }
            return false;
        }

        // Renderers some animation of the avatar switches on or off or changes
        // other than through blend shapes. Paths are resolved from the animator
        // that plays the clip: the avatar root for the VRChat playable layers.
        internal static HashSet<Renderer> Switched(GameObject avatar)
        {
            var result = new HashSet<Renderer>();
            var players = new List<(Transform Root, RuntimeAnimatorController Controller)>();
            var playables = AvatarPlayables.Read(avatar);
            if (playables != null)
                foreach (var controller in new[] { playables.Fx, playables.Gesture, playables.Action })
                    if (controller != null) players.Add((avatar.transform, controller));
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
                if (animator.runtimeAnimatorController != null) players.Add((animator.transform, animator.runtimeAnimatorController));
            var seen = new HashSet<(Transform, AnimationClip)>();
            foreach (var (root, controller) in players)
                foreach (var clip in controller.animationClips)
                {
                    if (clip == null || !seen.Add((root, clip))) continue;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip).Concat(AnimationUtility.GetObjectReferenceCurveBindings(clip)))
                    {
                        var target = binding.path.Length == 0 ? root : root.Find(binding.path);
                        if (target == null) continue;
                        if (binding.type == typeof(GameObject))
                        {
                            if (binding.propertyName == "m_IsActive") result.UnionWith(target.GetComponentsInChildren<Renderer>(true));
                        }
                        else if (typeof(Renderer).IsAssignableFrom(binding.type) && !binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                            result.UnionWith(target.GetComponents<Renderer>());
                    }
                }
            return result;
        }

        // An authored MergeSkinnedMesh keeps its own sources and its renderer.
        // Every renderer its serialized data names is kept, including entries a
        // prefab override removed: leaving one unmerged is harmless.
        static IEnumerable<Renderer> AuthoredMergeRenderers(GameObject avatar)
        {
            foreach (var component in avatar.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().FullName != MergeSkinnedMeshType) continue;
                if (component.TryGetComponent<Renderer>(out var host)) yield return host;
                using var data = new SerializedObject(component);
                var property = data.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue is Renderer renderer)
                        yield return renderer;
            }
        }
    }
}
