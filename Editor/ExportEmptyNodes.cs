using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // UniVRM writes every transform of the copy as a node, including objects
    // that only held VRChat settings (menus, contacts, PhysBone holders) or
    // what the build passes left behind. Those carry nothing in a VRM.
    internal static class ExportEmptyNodes
    {
        // Components the VRM is written from: UniVRM's own, the humanoid it
        // reads, and this exporter's markers.
        static readonly string[] VrmNamespaces = { "UniVRM10", "UniGLTF", "UniHumanoid", "VRM", "VRM10", "VrmLib", "VRVlog" };

        // Deletes the objects of the copy whose whole hierarchy has nothing to
        // write and returns how many transforms left. Kept: the root, humanoid
        // bones, every mesh renderer (the captures bind them by object even
        // when disabled), VRM components, and whatever these refer to.
        internal static int Prune(GameObject copy)
        {
            if (copy == null || EditorUtility.IsPersistent(copy))
                throw new ArgumentException("An independent export copy is required.", nameof(copy));
            var root = copy.transform;
            var kept = new HashSet<Transform> { root };
            var followed = new HashSet<Component>();
            var pending = new Stack<Component>();
            void Keep(Component component)
            {
                if (component == null || !component.transform.IsChildOf(root)) return;
                kept.Add(component.transform);
                // Transform references are parent and children: hierarchy, not use.
                if (!(component is Transform) && followed.Add(component)) pending.Push(component);
            }
            foreach (var component in copy.GetComponentsInChildren<Component>(true))
                if (component is MeshRenderer || component is SkinnedMeshRenderer || OfVrm(component)) Keep(component);
            var animator = copy.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.isHuman)
                for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
                {
                    Keep(animator.GetBoneTransform(bone));
                    Keep(HumanoidBones.Resolve(animator, bone));
                }
            // Bones, root bones, probe anchors, spring joints and colliders,
            // constraint sources: followed until nothing new is reached.
            while (pending.Count > 0)
            {
                using var serialized = new SerializedObject(pending.Pop());
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    Keep(value is GameObject go ? go.transform : value as Component);
                }
            }
            var needed = new HashSet<Transform>();
            foreach (var transform in kept)
                for (var at = transform; at != null && needed.Add(at) && at != root; at = at.parent) { }
            var removed = new List<GameObject>();
            void Visit(Transform parent)
            {
                foreach (Transform child in parent)
                    if (needed.Contains(child)) Visit(child);
                    else removed.Add(child.gameObject);
            }
            Visit(root);
            var count = removed.Sum(go => go.GetComponentsInChildren<Transform>(true).Length);
            foreach (var go in removed) UnityEngine.Object.DestroyImmediate(go);
            return count;
        }

        static bool OfVrm(Component component)
        {
            var space = component != null ? component.GetType().Namespace : null;
            return space != null && VrmNamespaces.Any(name => space == name || space.StartsWith(name + ".", StringComparison.Ordinal));
        }
    }
}
