using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Reads Lip Sync from the processed copy. Mesh optimizers that rename
    // shapes also rewrite the descriptor, so its names match the output.
    internal static class VrChatVisemeExport
    {
        // Lip Sync as read from one hierarchy. The recorded-expression export
        // removes the descriptor before exporting, so it captures this first
        // and, like blink, follows the renderer and shape names to the output.
        internal sealed class Captured
        {
            internal SkinnedMeshRenderer Renderer;
            // Preset -> shape name. Null once the name cannot reach the output.
            internal readonly Dictionary<string, string> Shapes = new Dictionary<string, string>();
            // Every shape the slots name, sil and the consonants included.
            // VRChat shows one viseme at a time, so while one plays the others
            // are at 0 rather than at their rest; the base shape follows that.
            // Only the base shape reads it, before the optimizer renames shapes.
            internal readonly List<string> Exclusive = new List<string>();

            internal Captured ForClone(GameObject source, GameObject clone)
            {
                var copy = new Captured();
                foreach (var pair in Shapes) copy.Shapes.Add(pair.Key, pair.Value);
                copy.Exclusive.AddRange(Exclusive);
                if (Renderer == null) return copy;
                var target = ExportRecoveryCopySession.Resolve(Renderer.transform, source.transform, clone.transform);
                var index = Array.IndexOf(Renderer.GetComponents<SkinnedMeshRenderer>(), Renderer);
                copy.Renderer = target.GetComponents<SkinnedMeshRenderer>()[index];
                return copy;
            }

            // A renderer that NDMF cannot follow is treated as not exported. A
            // renderer a pass destroyed is still passed on, so its shapes can be
            // followed to the renderer it was renamed or merged into.
            internal void RebindPrepared(Func<SkinnedMeshRenderer, SkinnedMeshRenderer> replacement)
            {
                if (!ReferenceEquals(Renderer, null)) Renderer = replacement(Renderer);
            }

            // Shapes the optimizer must keep and report. Only names on an
            // exported renderer can be declared; the rest stay unbound.
            internal (SkinnedMeshRenderer Renderer, string Shape)[] Morphs(GameObject clone)
            {
                if (Renderer == null || Renderer.sharedMesh == null ||
                    !ExportRendererSelection.Enumerate(clone).Contains(Renderer)) return Array.Empty<(SkinnedMeshRenderer, string)>();
                return Shapes.Values.Where(shape => !string.IsNullOrEmpty(shape) && Renderer.sharedMesh.GetBlendShapeIndex(shape) >= 0)
                    .Distinct(StringComparer.Ordinal).Select(shape => (Renderer, Shape: shape)).ToArray();
            }

            // The output names one renderer. A shape the optimizer moved to
            // another renderer cannot be bound there and stays unbound.
            internal void Remap(ExportOptimizationBindings mappings)
            {
                if (Renderer == null) return;
                // Only declared shapes choose the output renderer; a missing
                // shape must not pin it to a renderer the optimizer merged away.
                var routes = new Dictionary<string, (SkinnedMeshRenderer Renderer, string Shape)>();
                SkinnedMeshRenderer target = null;
                foreach (var pair in Shapes)
                {
                    if (string.IsNullOrEmpty(pair.Value) || !mappings.TryMapMorph(Renderer, pair.Value, out var mapped)) continue;
                    routes.Add(pair.Key, mapped);
                    if (target == null) target = mapped.Renderer;
                }
                if (target == null) return;
                foreach (var preset in Shapes.Keys.ToArray())
                {
                    if (string.IsNullOrEmpty(Shapes[preset])) continue;
                    if (routes.TryGetValue(preset, out var mapped)) Shapes[preset] = mapped.Renderer == target ? mapped.Shape : null;
                    else if (target != Renderer) Shapes[preset] = null;
                }
                Renderer = target;
            }

            // outputNode maps the exported GameObject to its glTF node (-1
            // when it was not exported).
            internal VrmExpressionBindings.DescriptorVisemes Resolve(Func<GameObject, int> outputNode)
            {
                var result = new VrmExpressionBindings.DescriptorVisemes();
                if (Renderer != null) result.Node = outputNode(Renderer.gameObject);
                foreach (var pair in Shapes) result.Shapes[pair.Key] = pair.Value;
                return result;
            }
        }

        // Returns null unless Lip Sync uses blend shapes on a renderer in this
        // avatar. Without that renderer VRChat has no mouth to move either.
        internal static Captured Read(GameObject avatar)
        {
            if (avatar == null) return null;
            var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c != null &&
                c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (VrChatExpressionMenu.Member(descriptor, "lipSync")?.ToString() != "VisemeBlendShape") return null;
            var renderer = VrChatExpressionMenu.Member(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            if (renderer == null || !renderer.transform.IsChildOf(avatar.transform)) return null;
            var names = VrChatExpressionMenu.Member(descriptor, "VisemeBlendShapes") as string[];
            var result = new Captured { Renderer = renderer };
            foreach (var (slot, preset) in VrmExpressionBindings.DescriptorVisemes.Slots)
                if (names != null && slot < names.Length && !string.IsNullOrEmpty(names[slot]))
                    result.Shapes[preset] = names[slot];
            if (names != null) result.Exclusive.AddRange(names.Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal));
            return result;
        }

        internal static VrmExpressionBindings.DescriptorVisemes Capture(GameObject avatar, Func<GameObject, int> outputNode) =>
            Read(avatar)?.Resolve(outputNode);
    }
}
