using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MorphValue = VRVlog.LilToonExporter.VrChatExpressionMenu.MorphValue;

namespace VRVlog.LilToonExporter
{
    // This snapshot belongs to the prepared, disposable export hierarchy. Mesh
    // and renderer identity survive later blink/rebase passes; source geometry
    // remains available to evaluate authored absolute expression endpoints.
    internal sealed class NeutralShapeSnapshot
    {
        internal sealed class RendererState
        {
            internal readonly SkinnedMeshRenderer Renderer;
            internal readonly Mesh OriginalMesh;
            internal readonly float[] Weights;
            internal readonly string Path;
            internal RendererState(SkinnedMeshRenderer renderer, Mesh mesh, float[] weights, string path)
            { Renderer = renderer; OriginalMesh = mesh; Weights = weights; Path = path; }
        }

        internal readonly GameObject Root;
        internal readonly IReadOnlyList<RendererState> Renderers;
        readonly Dictionary<SkinnedMeshRenderer, RendererState> byRenderer;

        NeutralShapeSnapshot(GameObject root, List<RendererState> renderers)
        {
            Root = root;
            Renderers = renderers;
            byRenderer = renderers.ToDictionary(state => state.Renderer);
        }

        internal static NeutralShapeSnapshot Capture(GameObject prepared, Func<Transform, bool> excluded = null)
        {
            var result = new List<RendererState>();
            foreach (var renderer in ExportRendererSelection.Enumerate(prepared).OfType<SkinnedMeshRenderer>())
            {
                if (excluded?.Invoke(renderer.transform) == true || renderer.sharedMesh == null) continue;
                var mesh = renderer.sharedMesh;
                var weights = Enumerable.Range(0, mesh.blendShapeCount).Select(renderer.GetBlendShapeWeight).ToArray();
                if (weights.Any(weight => !Finite(weight)))
                    throw new InvalidOperationException(renderer.name + ": neutralのBlendShape値が不正です。");
                result.Add(new RendererState(renderer, mesh, weights,
                    AnimationUtility.CalculateTransformPath(renderer.transform, prepared.transform)));
            }
            return new NeutralShapeSnapshot(prepared, result);
        }

        internal RendererState Get(SkinnedMeshRenderer renderer) =>
            renderer != null && byRenderer.TryGetValue(renderer, out var state) ? state : null;

        internal RendererState Get(string path)
        {
            var matches = Renderers.Where(state => string.Equals(state.Path, path ?? "", StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException("neutralの対象Rendererを一意に指定できません: " + path);
            return matches.Length == 0 ? null : matches[0];
        }

        internal static void Apply(GameObject prepared, IEnumerable<MorphValue> sample)
        {
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            var snapshot = Capture(prepared);
            var assignments = new List<(SkinnedMeshRenderer Renderer, int Shape, float Weight)>();
            var seen = new HashSet<(SkinnedMeshRenderer, int)>();
            foreach (var value in sample)
            {
                if (value == null || !Finite(value.Weight))
                    throw new InvalidOperationException("neutralのBlendShape値が不正です。");
                var state = snapshot.Get(value.Path);
                if (state == null) throw new InvalidOperationException("neutralの対象Rendererがありません: " + value.Path);
                var index = state.OriginalMesh.GetBlendShapeIndex(value.Shape);
                if (index < 0) throw new InvalidOperationException("neutralのBlendShapeがありません: " + value.Shape);
                if (!seen.Add((state.Renderer, index)))
                    throw new InvalidOperationException("neutralのBlendShapeが重複しています: " + value.Shape);
                assignments.Add((state.Renderer, index, value.Weight));
            }
            // Resolve every assignment before mutating the disposable copy.
            foreach (var assignment in assignments)
                assignment.Renderer.SetBlendShapeWeight(assignment.Shape, assignment.Weight);
        }

        // The shapes the FX default state sets (fxRest), on the renderers of
        // neutral, less the shapes blink closes the eyes with: a vowel never
        // takes the default eyelids off. AvatarBaseShape.MouthRests picks the
        // mouth shapes from these.
        internal static HashSet<(SkinnedMeshRenderer Renderer, string Shape)> DefaultFace(NeutralShapeSnapshot neutral,
            IEnumerable<MorphValue> fxRest, IEnumerable<BlinkShapeBinding> blink)
        {
            var result = new HashSet<(SkinnedMeshRenderer Renderer, string Shape)>();
            if (neutral == null || fxRest == null) return result;
            foreach (var value in fxRest)
            {
                RendererState state;
                try { state = neutral.Get(value.Path); }
                catch (InvalidOperationException) { continue; }
                if (state != null) result.Add((state.Renderer, value.Shape));
            }
            foreach (var binding in blink ?? Array.Empty<BlinkShapeBinding>()) result.Remove((binding.Renderer, binding.Shape));
            return result;
        }

        // Kept for the many callers that name it through this class.
        internal static bool Finite(float value) => NumericChecks.Finite(value);
    }
}
