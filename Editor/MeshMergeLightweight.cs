using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRVlog.LilToonExporter
{
    // 同じマテリアルのメッシュをまとめる, on the export copy: the app draws each
    // submesh of each skinned renderer once (and its outline once more), so an
    // avatar of many clothing parts costs many draws for few materials. Within
    // a group of skinned renderers that draw alike (shadows, probes, layer,
    // sorting, the outline scale, the vertex attributes) one renderer, the
    // host, takes the triangles of the others, the donors, and draws each
    // material once. The host keeps its object, its BlendShapes in their order
    // and weights, and its submeshes in their order; a donor's materials it
    // lacks follow as new submeshes. A donor is a renderer without
    // BlendShapes, so no expression, blink or mouth shape points to it, and
    // ends disabled like an empty renderer (DisableEmptyMeshRenderers).
    // Only opaque and cutout lilToon materials without stencil, depth or ID
    // mask tricks take part: their order among renderers does not change
    // what is drawn. Renderers merge only with renderers below the same
    // objects that toggles or expressions show and hide, so they still
    // appear and disappear together. A donor's mesh is moved into the host's mesh space
    // by the one rigid transform its shared bones agree on; a donor without
    // one stays. Nothing changes when a step fails: every mesh is built
    // before any renderer takes it.
    internal static class MeshMergeLightweight
    {
        internal sealed class Result
        {
            internal int RenderersBefore, RenderersAfter, DrawsBefore, DrawsAfter;
            internal string ResultLine() => RenderersBefore <= RenderersAfter ? null :
                string.Format(CultureInfo.InvariantCulture, ExporterLocalization.T(ExportNoteText.MeshesMerged),
                    RenderersBefore, RenderersAfter, DrawsBefore - DrawsAfter);
        }

        sealed class Plan
        {
            internal SkinnedMeshRenderer Host;
            internal readonly List<(SkinnedMeshRenderer Renderer, Matrix4x4 ToHost)> Donors = new List<(SkinnedMeshRenderer, Matrix4x4)>();
            internal Mesh Mesh;
            internal Transform[] Bones;
            internal Material[] Materials;
            internal Bounds LocalBounds;
        }

        const int UvChannels = 8;

        // Null when nothing was merged.
        internal static Result Apply(GameObject clone, ICollection<Mesh> owned, IEnumerable<Transform> shownAndHidden)
        {
            if (clone == null) throw new ArgumentNullException(nameof(clone));
            var switched = new HashSet<Transform>((shownAndHidden ?? Enumerable.Empty<Transform>()).Where(item => item != null));
            // The switched objects above the renderer, itself included.
            string SwitchedAbove(Transform item)
            {
                var ids = new List<int>();
                for (var current = item; current != null; current = current.parent)
                {
                    if (switched.Contains(current)) ids.Add(current.GetInstanceID());
                    if (current == clone.transform) break;
                }
                return string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            }
            var candidates = ExportRendererSelection.Enumerate(clone).OfType<SkinnedMeshRenderer>().Where(Eligible).ToList();
            var plans = new List<Plan>();
            Plan building = null;
            try
            {
                foreach (var group in candidates.GroupBy(renderer => Key(renderer) + "|" + SwitchedAbove(renderer.transform)))
                {
                    var members = group.ToList();
                    if (members.Count < 2) continue;
                    var host = members.FirstOrDefault(renderer => renderer.sharedMesh.blendShapeCount > 0) ?? members[0];
                    var plan = building = new Plan { Host = host };
                    foreach (var donor in members.Where(renderer => renderer != host && renderer.sharedMesh.blendShapeCount == 0))
                        if (ToHost(host, donor) is Matrix4x4 toHost) plan.Donors.Add((donor, toHost));
                    if (plan.Donors.Count > 0 && Build(plan)) plans.Add(plan);
                }
            }
            catch (Exception error) when (!(error is OperationCanceledException))
            {
                foreach (var plan in plans.Append(building).Distinct().Where(plan => plan?.Mesh != null)) UnityEngine.Object.DestroyImmediate(plan.Mesh);
                Debug.LogException(error);
                return null;
            }
            if (plans.Count == 0) return null;
            var result = new Result();
            foreach (var plan in plans)
            {
                result.RenderersBefore += 1 + plan.Donors.Count;
                result.RenderersAfter += 1;
                result.DrawsBefore += plan.Host.sharedMesh.subMeshCount + plan.Donors.Sum(donor => donor.Renderer.sharedMesh.subMeshCount);
                result.DrawsAfter += plan.Mesh.subMeshCount;
                owned.Add(plan.Mesh);
                var host = plan.Host;
                var weights = new float[host.sharedMesh.blendShapeCount];
                for (var i = 0; i < weights.Length; i++) weights[i] = host.GetBlendShapeWeight(i);
                host.sharedMesh = plan.Mesh;
                host.bones = plan.Bones;
                host.sharedMaterials = plan.Materials;
                for (var i = 0; i < weights.Length; i++) host.SetBlendShapeWeight(i, weights[i]);
                host.localBounds = plan.LocalBounds;
                foreach (var donor in plan.Donors) donor.Renderer.enabled = false;
            }
            return result;
        }

        static bool Eligible(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            if (mesh == null || mesh.vertexCount == 0 || renderer.HasPropertyBlock()) return false;
            if (!mesh.HasVertexAttribute(VertexAttribute.Normal)) return false;
            var materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount || !materials.All(MergeableMaterial)) return false;
            for (var sub = 0; sub < mesh.subMeshCount; sub++)
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) return false;
            var bones = renderer.bones;
            if (bones.Length == 0 || mesh.bindposes.Length != bones.Length || bones.Any(bone => bone == null)) return false;
            return mesh.GetBonesPerVertex().Length == mesh.vertexCount;
        }

        // Opaque or cutout lilToon whose draw order among renderers does not
        // change the picture.
        internal static bool MergeableMaterial(Material material)
        {
            if (material == null || material.shader == null || !LilToon.LilToon234Catalogue.Shaders.ContainsKey(material.shader.name)) return false;
            if (material.renderQueue > (int)RenderQueue.GeometryLast) return false;
            var name = material.shader.name;
            if (new[] { "FakeShadow", "Fur", "Refraction", "Gem" }.Any(part => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            float Value(string property, float missing) => material.HasProperty(property) ? material.GetFloat(property) : missing;
            if (Value("_StencilComp", 8f) != 8f || Value("_StencilPass", 0f) != 0f) return false;
            if (Value("_ZWrite", 1f) != 1f || Value("_ZTest", 4f) != 4f) return false;
            if (Value("_SrcBlend", 1f) != 1f || Value("_DstBlend", 0f) != 0f) return false;
            if (Value("_IDMaskCompile", 0f) != 0f) return false;
            for (var i = 1; i <= 8; i++)
                if (Value("_IDMask" + i, 0f) != 0f) return false;
            return true;
        }

        // Renderers that draw alike; the outline scale in 2% steps.
        static string Key(SkinnedMeshRenderer renderer)
        {
            var mesh = renderer.sharedMesh;
            var scale = OutlineRendererScale.Scale(renderer);
            var step = float.IsNaN(scale) || scale <= 0f ? int.MinValue : Mathf.RoundToInt(Mathf.Log(scale) / Mathf.Log(1.02f));
            return string.Join("|", new object[]
            {
                (int)renderer.shadowCastingMode, renderer.receiveShadows, (int)renderer.lightProbeUsage, (int)renderer.reflectionProbeUsage,
                renderer.probeAnchor != null ? renderer.probeAnchor.GetInstanceID() : 0, renderer.sortingLayerID, renderer.sortingOrder,
                renderer.gameObject.layer, step, mesh.HasVertexAttribute(VertexAttribute.Color), mesh.HasVertexAttribute(VertexAttribute.Tangent),
            }.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture)));
        }

        // The rigid transform from the donor's mesh space to the host's that
        // every shared bone agrees on: host bindpose⁻¹ × donor bindpose.
        // Identity when they share no bone; null when they disagree.
        static Matrix4x4? ToHost(SkinnedMeshRenderer host, SkinnedMeshRenderer donor)
        {
            var hostBones = host.bones;
            var hostPoses = host.sharedMesh.bindposes;
            var hostIndex = new Dictionary<Transform, int>();
            for (var i = 0; i < hostBones.Length; i++)
                if (!hostIndex.ContainsKey(hostBones[i])) hostIndex[hostBones[i]] = i;
            var donorBones = donor.bones;
            var donorPoses = donor.sharedMesh.bindposes;
            Matrix4x4? result = null;
            for (var j = 0; j < donorBones.Length; j++)
            {
                if (!hostIndex.TryGetValue(donorBones[j], out var i)) continue;
                var candidate = hostPoses[i].inverse * donorPoses[j];
                if (result == null) result = candidate;
                else if (!Close(result.Value, candidate)) return null;
            }
            var toHost = result ?? Matrix4x4.identity;
            return Rigid(toHost) ? toHost : (Matrix4x4?)null;
        }

        internal static bool Close(Matrix4x4 a, Matrix4x4 b)
        {
            for (var i = 0; i < 16; i++)
                if (!(Mathf.Abs(a[i] - b[i]) <= 1e-4f)) return false;
            return true;
        }

        static bool Rigid(Matrix4x4 m)
        {
            for (var column = 0; column < 3; column++)
                if (!(Mathf.Abs(((Vector3)m.GetColumn(column)).magnitude - 1f) <= 1e-3f)) return false;
            return Mathf.Abs(m.m30) <= 1e-6f && Mathf.Abs(m.m31) <= 1e-6f && Mathf.Abs(m.m32) <= 1e-6f && Mathf.Abs(m.m33 - 1f) <= 1e-6f;
        }

        // Fills plan.Mesh, Bones, Materials and LocalBounds; false leaves
        // the plan without a mesh when no donor fits.
        static bool Build(Plan plan)
        {
            var host = plan.Host;
            var hostMesh = host.sharedMesh;
            var bones = new List<Transform>(host.bones);
            var poses = new List<Matrix4x4>(hostMesh.bindposes);
            var boneIndex = new Dictionary<Transform, int>();
            for (var i = 0; i < bones.Count; i++)
                if (!boneIndex.ContainsKey(bones[i])) boneIndex[bones[i]] = i;
            // Each donor's bone index → the combined one; a donor whose bone
            // would need a second joint for the same object stays.
            var maps = new List<int[]>();
            foreach (var donor in plan.Donors.ToList())
            {
                var inverse = donor.ToHost.inverse;
                var donorBones = donor.Renderer.bones;
                var donorPoses = donor.Renderer.sharedMesh.bindposes;
                var map = new int[donorBones.Length];
                var added = new List<(Transform Bone, Matrix4x4 Pose)>();
                var fits = true;
                for (var j = 0; j < donorBones.Length && fits; j++)
                {
                    var pose = donorPoses[j] * inverse;
                    if (boneIndex.TryGetValue(donorBones[j], out var index)) { map[j] = index; fits = Close(poses[index], pose); continue; }
                    var pending = added.FindIndex(item => item.Bone == donorBones[j]);
                    if (pending >= 0) { map[j] = bones.Count + pending; fits = Close(added[pending].Pose, pose); continue; }
                    map[j] = bones.Count + added.Count;
                    added.Add((donorBones[j], pose));
                }
                if (!fits) { plan.Donors.Remove(donor); continue; }
                foreach (var item in added)
                {
                    boneIndex[item.Bone] = bones.Count;
                    bones.Add(item.Bone);
                    poses.Add(item.Pose);
                }
                maps.Add(map);
            }
            if (plan.Donors.Count == 0) return false;

            var sources = new List<(Mesh Mesh, Matrix4x4 ToHost, int[] Map)> { (hostMesh, Matrix4x4.identity, null) };
            for (var d = 0; d < plan.Donors.Count; d++) sources.Add((plan.Donors[d].Renderer.sharedMesh, plan.Donors[d].ToHost, maps[d]));
            var total = sources.Sum(source => source.Mesh.vertexCount);
            var tangents = hostMesh.HasVertexAttribute(VertexAttribute.Tangent);
            var colors = hostMesh.HasVertexAttribute(VertexAttribute.Color);
            var dimensions = new int[UvChannels];
            for (var channel = 0; channel < UvChannels; channel++)
                dimensions[channel] = sources.Max(source => source.Mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel)
                    ? source.Mesh.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel) : 0);

            var allVertices = new List<Vector3>(total);
            var allNormals = new List<Vector3>(total);
            var allTangents = tangents ? new List<Vector4>(total) : null;
            var allColors = colors ? new List<Color>(total) : null;
            var allUvs = Enumerable.Range(0, UvChannels).Select(channel => dimensions[channel] > 0 ? new List<Vector4>(total) : null).ToArray();
            var bonesPerVertex = new List<byte>(total);
            var weights = new List<BoneWeight1>();
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangentList = new List<Vector4>();
            var colorList = new List<Color>();
            var uvList = new List<Vector4>();
            foreach (var source in sources)
            {
                var mesh = source.Mesh;
                var count = mesh.vertexCount;
                var m = source.ToHost;
                mesh.GetVertices(vertices);
                mesh.GetNormals(normals);
                foreach (var vertex in vertices) allVertices.Add(m.MultiplyPoint3x4(vertex));
                foreach (var normal in normals) allNormals.Add(m.MultiplyVector(normal));
                if (tangents)
                {
                    mesh.GetTangents(tangentList);
                    foreach (var tangent in tangentList)
                    {
                        var xyz = m.MultiplyVector(new Vector3(tangent.x, tangent.y, tangent.z));
                        allTangents.Add(new Vector4(xyz.x, xyz.y, xyz.z, tangent.w));
                    }
                }
                if (colors)
                {
                    mesh.GetColors(colorList);
                    allColors.AddRange(colorList);
                }
                for (var channel = 0; channel < UvChannels; channel++)
                {
                    if (allUvs[channel] == null) continue;
                    if (mesh.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
                    {
                        mesh.GetUVs(channel, uvList);
                        allUvs[channel].AddRange(uvList);
                    }
                    else allUvs[channel].AddRange(Enumerable.Repeat(Vector4.zero, count));
                }
                var perVertex = mesh.GetBonesPerVertex();
                var sourceWeights = mesh.GetAllBoneWeights();
                bonesPerVertex.AddRange(perVertex);
                foreach (var weight in sourceWeights)
                    weights.Add(source.Map == null ? weight : new BoneWeight1 { boneIndex = source.Map[weight.boneIndex], weight = weight.weight });
            }

            // The host's submeshes keep their order; a donor submesh joins the
            // first one with its material or follows as a new one.
            var materials = new List<Material>(host.sharedMaterials);
            var indices = Enumerable.Range(0, hostMesh.subMeshCount).Select(sub => new List<int>(hostMesh.GetIndices(sub))).ToList();
            var offset = hostMesh.vertexCount;
            foreach (var donor in plan.Donors)
            {
                var mesh = donor.Renderer.sharedMesh;
                var slots = donor.Renderer.sharedMaterials;
                for (var sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    var target = materials.FindIndex(material => ReferenceEquals(material, slots[sub]));
                    if (target < 0)
                    {
                        materials.Add(slots[sub]);
                        indices.Add(new List<int>());
                        target = materials.Count - 1;
                    }
                    var list = indices[target];
                    foreach (var index in mesh.GetIndices(sub)) list.Add(index + offset);
                }
                offset += mesh.vertexCount;
            }

            var merged = new Mesh { name = hostMesh.name, indexFormat = total > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            plan.Mesh = merged; // destroyed by Apply if a later step throws
            merged.SetVertices(allVertices);
            merged.SetNormals(allNormals);
            if (tangents) merged.SetTangents(allTangents);
            if (colors) merged.SetColors(allColors);
            for (var channel = 0; channel < UvChannels; channel++)
            {
                var uvs = allUvs[channel];
                if (uvs == null) continue;
                if (dimensions[channel] <= 2) merged.SetUVs(channel, uvs.Select(uv => (Vector2)uv).ToList());
                else if (dimensions[channel] == 3) merged.SetUVs(channel, uvs.Select(uv => (Vector3)uv).ToList());
                else merged.SetUVs(channel, uvs);
            }
            using (var perVertex = new NativeArray<byte>(bonesPerVertex.ToArray(), Allocator.Temp))
            using (var all = new NativeArray<BoneWeight1>(weights.ToArray(), Allocator.Temp))
                merged.SetBoneWeights(perVertex, all);
            merged.bindposes = poses.ToArray();
            merged.subMeshCount = indices.Count;
            for (var sub = 0; sub < indices.Count; sub++) merged.SetIndices(indices[sub].ToArray(), MeshTopology.Triangles, sub, false);
            merged.RecalculateBounds();
            // Host BlendShapes keep their names, order and frames; donor
            // vertices do not move with them.
            var hostCount = hostMesh.vertexCount;
            var deltaVertices = new Vector3[hostCount];
            var deltaNormals = new Vector3[hostCount];
            var deltaTangents = new Vector3[hostCount];
            // One set of full-length frames, reused: the donor tail stays zero
            // and AddBlendShapeFrame copies what it is given.
            var v = new Vector3[total];
            var n = new Vector3[total];
            var t = new Vector3[total];
            for (var shape = 0; shape < hostMesh.blendShapeCount; shape++)
            {
                var name = hostMesh.GetBlendShapeName(shape);
                for (var frame = 0; frame < hostMesh.GetBlendShapeFrameCount(shape); frame++)
                {
                    hostMesh.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                    Array.Copy(deltaVertices, v, hostCount);
                    Array.Copy(deltaNormals, n, hostCount);
                    Array.Copy(deltaTangents, t, hostCount);
                    merged.AddBlendShapeFrame(name, hostMesh.GetBlendShapeFrameWeight(shape, frame), v, n, t);
                }
            }

            // The host's bounds, in its root bone's space, grow to hold the donors.
            var space = host.rootBone != null ? host.rootBone : host.transform;
            var bounds = host.localBounds;
            foreach (var donor in plan.Donors)
            {
                var world = donor.Renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = new Vector3((corner & 1) == 0 ? world.min.x : world.max.x, (corner & 2) == 0 ? world.min.y : world.max.y,
                        (corner & 4) == 0 ? world.min.z : world.max.z);
                    bounds.Encapsulate(space.InverseTransformPoint(point));
                }
            }
            plan.Mesh = merged;
            plan.Bones = bones.ToArray();
            plan.Materials = materials.ToArray();
            plan.LocalBounds = bounds;
            return true;
        }
    }
}
