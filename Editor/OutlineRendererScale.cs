using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // lilToon pushes the outline out along the object-space normal
    // (_OutlineWidth cm), so in the scene it grows with the renderer's scale:
    // a MeshRenderer draws its unit normals through a scaled object-to-world
    // matrix, and GPU skinning leaves each normal as long as the skin matrix
    // scale. The VRM keeps the node scales, so the lilToon extension draws
    // the same way and keeps its width. MToon's world-mode width is in metres
    // along a normalized normal, so only the MToon approximation needs the
    // width multiplied by the scale the scene drew it with.
    internal static class OutlineRendererScale
    {
        // Below this the outline differs by less than the float noise of the scale itself.
        private const float Tolerance = 1e-3f;

        internal const string SharedScaleWarning = "輪郭の太さ: {0} は大きさの違うメッシュで共有されているため、互換表示の輪郭は頂点の最も多い {1} の大きさ（{2}倍）に合わせました";

        // The scale of each outlined lilToon material whose renderers are not drawn at 1x.
        internal static Dictionary<Material, float> Measure(GameObject copy, ICollection<string> warnings = null)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            // A material keeps one width, so renderers drawn at different scales
            // share the scale of the renderer with the most vertices.
            var chosen = new Dictionary<Material, (float Scale, int Vertices, Renderer Renderer)>();
            var mixed = new HashSet<Material>();
            foreach (var renderer in ExportRendererSelection.Enumerate(copy))
            {
                float? scale = null;
                var vertices = VertexCount(renderer);
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!Outlined(material)) continue;
                    scale ??= Scale(renderer);
                    if (!chosen.TryGetValue(material, out var first)) { chosen.Add(material, (scale.Value, vertices, renderer)); continue; }
                    if (Mathf.Abs(first.Scale - scale.Value) > Tolerance * Mathf.Max(first.Scale, scale.Value)) mixed.Add(material);
                    if (vertices > first.Vertices) chosen[material] = (scale.Value, vertices, renderer);
                }
            }
            var result = new Dictionary<Material, float>();
            foreach (var pair in chosen)
            {
                var (scale, _, renderer) = pair.Value;
                if (Mathf.Abs(scale - 1f) <= Tolerance || !(scale > 0f) || float.IsInfinity(scale)) continue;
                result.Add(pair.Key, scale);
                if (mixed.Contains(pair.Key))
                    warnings?.Add(string.Format(ExporterLocalization.T(SharedScaleWarning), pair.Key.name, renderer.name, scale.ToString("0.###")));
            }
            return result;
        }

        private static bool Outlined(Material material) =>
            material != null && LilToonMaterialReader.IsLilToon(material) && LilToonMaterialReader.HasOutline(material) &&
            material.HasProperty("_OutlineWidth") && material.GetFloat("_OutlineWidth") > 0f;

        private static int VertexCount(Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
            return mesh != null ? mesh.vertexCount : 0;
        }

        // The length a unit normal has when the scene draws this renderer.
        internal static float Scale(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh != null)
            {
                var bones = skin.bones;
                var bindposes = skin.sharedMesh.bindposes;
                if (bones.Length > 0 && bindposes.Length > 0)
                {
                    var count = Mathf.Min(bones.Length, bindposes.Length);
                    var boneScales = new float[count];
                    for (var i = 0; i < count; i++)
                        boneScales[i] = bones[i] == null ? float.NaN : Mean((bones[i].localToWorldMatrix * bindposes[i]).lossyScale);
                    // Weight each skin matrix by how much of the mesh follows it.
                    double sum = 0, total = 0;
                    var weights = skin.sharedMesh.GetAllBoneWeights();
                    foreach (var weight in weights)
                        if (weight.boneIndex < count && !float.IsNaN(boneScales[weight.boneIndex]) && weight.weight > 0f)
                        {
                            sum += boneScales[weight.boneIndex] * (double)weight.weight;
                            total += weight.weight;
                        }
                    if (total > 0) return (float)(sum / total);
                    var valid = boneScales.Where(value => !float.IsNaN(value)).OrderBy(value => value).ToArray();
                    if (valid.Length > 0) return valid[valid.Length / 2];
                }
            }
            return Mean(renderer.transform.lossyScale);
        }

        private static float Mean(Vector3 scale) => (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
    }
}
