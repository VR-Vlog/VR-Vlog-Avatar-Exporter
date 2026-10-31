using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 自動で軽量化する: a texture with more texels per metre of the model's
    // surface than the app's screen can show is written smaller. Measured once
    // on the export copy before any texture is read; off, nothing here runs.
    internal static class TexelDensity
    {
        // Texels per metre the app needs. A whole body of about 1.6m filling a
        // 1920px tall screen shows 1920 / 1.6 = 1200 pixels per metre; texels
        // beyond that are never on screen, so halving such a texture keeps its look.
        internal const double TargetTexelsPerMetre = 1200;
        // The smallest long side a texture is reduced to.
        internal const int MinimumSize = 32;
        // Triangles below these areas (square metres, UV units) have no meaningful density.
        internal const double MinimumWorldArea = 1e-10, MinimumUvArea = 1e-12;
        // The density of a submesh: 90% of its surface has this many texels per metre or fewer.
        internal const double Percentile = .9;
        // How many times Measure ran; the tests prove an export with the option off never measures.
        internal static int Measurements;

        // lilToon slots that never shrink, in one place. These are not sampled
        // through UV0 (MatCap, rim, reflection, glitter and the like use the view
        // or other coordinates), are lookup ramps or noise whose detail is not
        // tied to the surface, or must keep their size (Delete Mesh outline masks).
        internal static readonly string[] NeverShrinkPrefixes =
        {
            "_MatCap", "_Reflection", "_Rim", "_Backlight", "_Glitter", "_Dissolve", "_AudioLink", "_Outline", "_Fur",
        };
        internal static readonly HashSet<string> NeverShrinkSlots = new HashSet<string>(StringComparer.Ordinal)
        {
            "_Main2ndDissolveMask", "_Main2ndDissolveNoiseMask", "_Main3rdDissolveMask", "_Main3rdDissolveNoiseMask",
            "_MainGradationTex", "_EmissionGradTex", "_Emission2ndGradTex", "_DitherTex", "_AnisotropyShiftNoiseMask",
        };

        internal static bool NeverShrinks(string property) =>
            string.IsNullOrEmpty(property) || NeverShrinkSlots.Contains(property) ||
            NeverShrinkPrefixes.Any(prefix => property.StartsWith(prefix, StringComparison.Ordinal));

        // A slot with a lilToon UV mode other than UV0 (0) never shrinks.
        internal static bool NeverShrinks(Material material, string property) =>
            NeverShrinks(property) || material.HasProperty(property + "_UVMode") && material.GetFloat(property + "_UVMode") != 0;

        // UV0 area per world area of one triangle, as texels per metre per texel
        // of a 1×1 texture; null when the triangle is degenerate.
        internal static double? Ratio(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector3 p0, Vector3 p1, Vector3 p2)
        {
            var world = Vector3.Cross(p1 - p0, p2 - p0).magnitude * .5;
            var e1 = uv1 - uv0; var e2 = uv2 - uv0;
            var uv = Math.Abs((double)e1.x * e2.y - (double)e1.y * e2.x) * .5;
            if (world < MinimumWorldArea || uv < MinimumUvArea) return null;
            return Math.Sqrt(uv / world);
        }

        // The texel density of one triangle of a width × height texture tiled by tiling.
        internal static double? Triangle(Vector2 uv0, Vector2 uv1, Vector2 uv2, Vector3 p0, Vector3 p1, Vector3 p2, int width, int height, double tiling = 1)
            => Ratio(uv0, uv1, uv2, p0, p1, p2) is double ratio ? ratio * tiling * Math.Sqrt((double)width * height) : (double?)null;

        // The value below which the given share of the total weight lies; NaN without samples.
        internal static double WeightedPercentile(IEnumerable<(double Value, double Weight)> samples, double percentile = Percentile)
        {
            var sorted = samples.Where(sample => sample.Weight > 0 && !double.IsNaN(sample.Value)).OrderBy(sample => sample.Value).ToArray();
            var total = sorted.Sum(sample => sample.Weight);
            if (sorted.Length == 0 || total <= 0) return double.NaN;
            var reached = 0.0;
            foreach (var sample in sorted)
            {
                reached += sample.Weight;
                if (reached >= total * percentile) return sample.Value;
            }
            return sorted[sorted.Length - 1].Value;
        }

        // The long side to write: start from the size the export already writes
        // and halve while the halved texture still has at least the target density,
        // never below MinimumSize. density is measured at the source's long side.
        internal static int ReducedSize(double density, int sourceLongSide, int writtenLongSide)
        {
            if (double.IsNaN(density) || density <= 0 || sourceLongSide <= 0) return writtenLongSide;
            var size = writtenLongSide;
            var current = density * writtenLongSide / sourceLongSide;
            while (size / 2 >= MinimumSize && current / 2 >= TargetTexelsPerMetre)
            {
                size /= 2;
                current /= 2;
            }
            return size;
        }

        // Uncompressed RGBA bytes with every mip level.
        internal static long Bytes(int width, int height)
        {
            long total = 0;
            while (true)
            {
                total += 4L * width * height;
                if (width <= 1 && height <= 1) return total;
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
            }
        }

        // The density of every texture of the avatar's lilToon materials that may shrink.
        // A texture with a never-shrink use, an unmeasurable use or no use is left out.
        internal static Plan Measure(GameObject avatar)
        {
            Measurements++;
            var densities = new Dictionary<Texture, double>();
            var never = new HashSet<Texture>();
            foreach (var renderer in ExportRendererSelection.Enumerate(avatar))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer is MeshRenderer && filter != null ? filter.sharedMesh : null;
                if (!ExportRendererSelection.HasGeometry(mesh)) continue;
                double[] ratios = null;
                var materials = renderer.sharedMaterials;
                for (var slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    if (material == null || material.shader == null) continue;
                    var lilToon = LilToonMaterialReader.IsLilToon(material);
                    var mainTiling = lilToon && material.HasProperty("_MainTex") ? Tiling(material, "_MainTex") : 1;
                    var shader = material.shader;
                    for (var i = 0; i < shader.GetPropertyCount(); i++)
                    {
                        if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                        var name = shader.GetPropertyName(i);
                        var texture = material.GetTexture(name);
                        if (texture == null) continue;
                        if (!lilToon || NeverShrinks(material, name) || !(texture is Texture2D two) ||
                            LilToonFullSnapshot.StorageFormat(two.graphicsFormat, false) != "rgba32")
                        {
                            never.Add(texture);
                            continue;
                        }
                        // A slot sampled on the main UV is tiled by both; a smaller main
                        // tiling lowers the density, a larger one is not counted.
                        var tiling = Tiling(material, name) * (name == "_MainTex" ? 1 : Math.Min(1, mainTiling));
                        if (ratios == null)
                        {
                            try { ratios = SubmeshRatios(renderer, mesh); }
                            catch (Exception error) when (!(error is OperationCanceledException))
                            {
                                Debug.LogException(error);
                                ratios = new double[mesh.subMeshCount];
                                for (var s = 0; s < ratios.Length; s++) ratios[s] = double.NaN;
                            }
                        }
                        // Materials past the last submesh draw the last one again.
                        var ratio = ratios[Math.Min(slot, ratios.Length - 1)];
                        if (double.IsNaN(ratio) || tiling < 1e-6) { never.Add(texture); continue; }
                        var density = ratio * tiling * Math.Sqrt((double)texture.width * texture.height);
                        densities[texture] = densities.TryGetValue(texture, out var other) ? Math.Max(other, density) : density;
                    }
                }
            }
            foreach (var texture in never) densities.Remove(texture);
            return new Plan(densities);
        }

        static double Tiling(Material material, string property)
        {
            var scale = material.GetTextureScale(property);
            return Math.Max(Math.Abs(scale.x), Math.Abs(scale.y));
        }

        // The UV0 per world ratio of each submesh in the pose being exported; NaN
        // for a submesh without measurable triangles.
        static double[] SubmeshRatios(Renderer renderer, Mesh mesh)
        {
            var ratios = new double[mesh.subMeshCount];
            Vector3[] vertices;
            var baked = renderer is SkinnedMeshRenderer ? new Mesh() : null;
            try
            {
                // Baked without the renderer's scale, so the whole transform applies once.
                if (renderer is SkinnedMeshRenderer skin) { skin.BakeMesh(baked, false); vertices = baked.vertices; }
                else vertices = mesh.vertices;
            }
            finally { if (baked != null) UnityEngine.Object.DestroyImmediate(baked); }
            var matrix = renderer.transform.localToWorldMatrix;
            var uvs = mesh.uv;
            for (var submesh = 0; submesh < ratios.Length; submesh++)
            {
                ratios[submesh] = double.NaN;
                if (uvs.Length != vertices.Length || mesh.GetTopology(submesh) != MeshTopology.Triangles) continue;
                var triangles = mesh.GetTriangles(submesh);
                var samples = new List<(double, double)>(triangles.Length / 3);
                for (var t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    var p0 = matrix.MultiplyVector(vertices[a]); var p1 = matrix.MultiplyVector(vertices[b]); var p2 = matrix.MultiplyVector(vertices[c]);
                    if (!(Ratio(uvs[a], uvs[b], uvs[c], p0, p1, p2) is double ratio)) continue;
                    samples.Add((ratio, Vector3.Cross(p1 - p0, p2 - p0).magnitude * .5));
                }
                ratios[submesh] = WeightedPercentile(samples);
            }
            return ratios;
        }

        // The measured densities and the textures this export made smaller.
        internal sealed class Plan
        {
            readonly Dictionary<Texture, double> densities;
            readonly Dictionary<Texture, long> saved = new Dictionary<Texture, long>();

            internal Plan(Dictionary<Texture, double> densities) { this.densities = densities ?? new Dictionary<Texture, double>(); }

            internal bool Measured(Texture texture) => texture != null && densities.ContainsKey(texture);
            internal double Density(Texture texture) => texture != null && densities.TryGetValue(texture, out var density) ? density : double.NaN;
            internal int Count => saved.Count;
            internal long SavedBytes => saved.Values.Sum();

            // The maximum long side to write source with, given the one the export
            // already uses: smaller only for a measured texture with detail to spare.
            internal int Maximum(Texture source, int maximum)
            {
                if (source == null || !densities.TryGetValue(source, out var density)) return maximum;
                var written = TextureResizePolicy.Size(source.width, source.height, maximum);
                var longSide = Math.Max(written.Item1, written.Item2);
                var reduced = ReducedSize(density, Math.Max(source.width, source.height), longSide);
                if (reduced >= longSide) return maximum;
                var smaller = TextureResizePolicy.Size(source.width, source.height, reduced);
                var bytes = Bytes(written.Item1, written.Item2) - Bytes(smaller.Item1, smaller.Item2);
                saved[source] = saved.TryGetValue(source, out var before) ? Math.Max(before, bytes) : bytes;
                return reduced;
            }

            // The line of the result list; null when nothing was made smaller.
            internal string ResultLine() => Count == 0 ? null : Line(Count, SavedBytes);

            internal static string Line(int count, long bytes) => string.Format(CultureInfo.InvariantCulture, ExporterLocalization.T(ExportNoteText.TexturesLightened),
                count, Megabytes(bytes));

            internal static string Megabytes(long bytes) => (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
