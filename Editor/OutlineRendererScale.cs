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
    // scale. The VRM bakes every scale into the mesh, so the same width there
    // is that many times thinner. Multiply the width by the scale the scene
    // drew it with, on an owned copy, before the lilToon extension and the
    // MToon approximation read it.
    internal static class OutlineRendererScale
    {
        // Below this the outline differs by less than the float noise of the scale itself.
        private const float Tolerance = 1e-3f;

        internal static void Apply(GameObject copy, ICollection<Material> owned, ICollection<string> warnings = null,
            Action<Material, Material> copyObserver = null)
        {
            if (copy == null) throw new ArgumentNullException(nameof(copy));
            if (owned == null) throw new ArgumentNullException(nameof(owned));
            // A material keeps one width, so renderers drawn at different scales share
            // the first renderer's scale. Copies keep the name: expressions and the
            // optimizer evidence find materials by name.
            var scales = new Dictionary<Material, (float Scale, Renderer Renderer)>();
            var mixed = new HashSet<Material>();
            var renderers = ExportRendererSelection.Enumerate(copy).ToArray();
            foreach (var renderer in renderers)
            {
                float? scale = null;
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!Scaled(material)) continue;
                    scale ??= Scale(renderer);
                    if (!scales.TryGetValue(material, out var first)) scales.Add(material, (scale.Value, renderer));
                    else if (Mathf.Abs(first.Scale - scale.Value) > Tolerance * Mathf.Max(first.Scale, scale.Value)) mixed.Add(material);
                }
            }
            var replacements = new Dictionary<Material, Material>();
            foreach (var pair in scales)
            {
                var (scale, renderer) = pair.Value;
                if (Mathf.Abs(scale - 1f) <= Tolerance || !(scale > 0f) || float.IsInfinity(scale)) continue;
                var original = pair.Key;
                var material = original;
                if (!owned.Contains(original))
                {
                    material = new Material(original) { name = original.name };
                    owned.Add(material);
                    copyObserver?.Invoke(material, original);
                    replacements.Add(original, material);
                }
                material.SetFloat("_OutlineWidth", original.GetFloat("_OutlineWidth") * scale);
                if (mixed.Contains(original))
                    warnings?.Add($"輪郭の太さ: {original.name} は大きさの違うメッシュで共有されているため、{renderer.name} の大きさ（{scale:0.###}倍）に合わせました");
            }
            if (replacements.Count == 0) return;
            foreach (var renderer in renderers)
            {
                var slots = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < slots.Length; i++)
                    if (slots[i] != null && replacements.TryGetValue(slots[i], out var material)) { slots[i] = material; changed = true; }
                if (changed) renderer.sharedMaterials = slots;
            }
        }

        private static bool Scaled(Material material) =>
            material != null && LilToonMaterialReader.IsLilToon(material) && LilToonMaterialReader.HasOutline(material) &&
            material.HasProperty("_OutlineWidth") && material.GetFloat("_OutlineWidth") > 0f;

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
