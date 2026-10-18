using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal static class ExportRendererSelection
    {
        // Match ModelExporter's empty-geometry exclusion without suppressing
        // invalid but nonempty topology or changing any shared source mesh.
        internal static bool HasGeometry(Mesh mesh)
        {
            if (mesh == null || mesh.vertexCount == 0) return false;
            for (var slot = 0; slot < mesh.subMeshCount; slot++)
                if (mesh.GetIndexCount(slot) != 0) return true;
            return false;
        }

        // Only for owned copies, whose root the export has already activated.
        public static void RequireActiveRoot(GameObject avatar)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            if (!avatar.activeInHierarchy)
                throw new InvalidOperationException("アバター本体と親オブジェクトを有効にしてから書き出してください。");
        }

        // Activity relative to the avatar root. The root and its parents are
        // ignored: the export activates only its owned copy's root, so a hidden
        // avatar exports what is shown inside it. On an active root this is
        // exactly activeInHierarchy.
        internal static bool ActiveUnder(GameObject target, Transform avatarRoot)
        {
            if (target == null) return false;
            if (avatarRoot == null) return target.activeInHierarchy;
            for (var current = target.transform; current != null; current = current.parent)
            {
                if (current == avatarRoot) return true;
                if (!current.gameObject.activeSelf) return false;
            }
            // Outside the avatar the scene's own activity applies.
            return target.activeInHierarchy;
        }

        // The avatar itself or one of its parents is inactive in the scene.
        internal static bool HiddenInScene(GameObject avatar) => avatar != null && !avatar.activeInHierarchy;

        public static IEnumerable<Renderer> Enumerate(GameObject avatar)
        {
            if (avatar == null) throw new ArgumentNullException(nameof(avatar));
            // Match UniVRM 0.131 ModelExporter material collection exactly.
            // Inactive wardrobe objects and disabled renderers have no fallback
            // material to restore; do not validate or convert their materials.
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled && ActiveUnder(renderer.gameObject, avatar.transform))
                    yield return renderer;
        }
    }
}
