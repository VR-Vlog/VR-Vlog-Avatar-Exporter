using System;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Reads Lip Sync from the processed copy. Mesh optimizers that rename
    // shapes also rewrite the descriptor, so its names match the output.
    internal static class VrChatVisemeExport
    {
        // Returns null unless Lip Sync uses blend shapes. outputNode maps the
        // exported GameObject to its glTF node (-1 when it was not exported).
        internal static VrmExpressionBindings.DescriptorVisemes Capture(GameObject avatar, Func<GameObject, int> outputNode)
        {
            if (avatar == null) return null;
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c != null &&
                c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (VrChatExpressionMenu.Member(descriptor, "lipSync")?.ToString() != "VisemeBlendShape") return null;
            var renderer = VrChatExpressionMenu.Member(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            var names = VrChatExpressionMenu.Member(descriptor, "VisemeBlendShapes") as string[];
            var result = new VrmExpressionBindings.DescriptorVisemes();
            if (renderer != null && renderer.transform.IsChildOf(avatar.transform))
                result.Node = outputNode(renderer.gameObject);
            foreach (var (slot, preset) in VrmExpressionBindings.DescriptorVisemes.Slots)
                if (names != null && slot < names.Length && !string.IsNullOrEmpty(names[slot]))
                    result.Shapes[preset] = names[slot];
            return result;
        }
    }
}
