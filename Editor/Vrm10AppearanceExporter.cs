using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using Unity.Collections;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.VrmLib;

namespace VRVlog.LilToonExporter
{
    internal static class Vrm10AppearanceExporter
    {
        internal static byte[] Export(GltfExportSettings settings, GameObject avatar,
            IMaterialExporter materialExporter, ITextureSerializer textureSerializer, VRM10ObjectMeta vrmMeta,
            IDictionary<Material, int> materialIndices = null,
            Action<ModelExporter, Model, ExportingGltfData> afterExport = null,
            ICollection<string> warnings = null)
        {
            using var arrays = new NativeArrayManager();
            using var meshPreparation = VrmMeshAttributePreparation.Prepare(avatar, warnings);
            var converter = new ModelExporter();
            // Keep the baked hierarchy until the end so Vrm10Exporter and
            // afterExport see the same node TRS as ModelExporter did.
            using var rootScale = RootScaleBake.Apply(avatar);
            var model = converter.Export(settings, arrays, avatar);
            PreserveMorphNormals(converter, arrays, settings);
            meshPreparation.RestoreSourceMeshKeys(converter);
            // ModelExporter accepts meshes without UV0, but the pinned
            // MeshWriter unconditionally reads it. Fill in Unity coordinates
            // so ConvertCoordinate also flips these UVs exactly once.
            foreach (var group in model.MeshGroups)
                foreach (var mesh in group.Meshes)
                    if (mesh.VertexBuffer.TexCoords == null)
                    {
                        var uv = new Vector2[mesh.VertexBuffer.Count];
                        var buffer = arrays.CreateNativeArray(uv).Reinterpret<byte>(8);
                        mesh.VertexBuffer.Add(VertexBuffer.TexCoordKey,
                            new BufferAccessor(arrays, buffer, AccessorValueType.FLOAT, AccessorVectorType.VEC2, uv.Length));
                        warnings?.Add("未設定のUV0を(0, 0)として保存しました: " + group.Name);
                    }
            UnmirrorSkins(model);
            ConvertCoordinateKeepingZeroScale(model);
            using var exporter = new Vrm10Exporter(settings, materialExporter, textureSerializer);
            exporter.Export(avatar, model, converter, new ExportArgs { removeMorphNormal = settings.ExportOnlyBlendShapePosition }, vrmMeta);
            // Vrm10Exporter emits materials in model.Materials order. Preserve
            // object identity for extension injection, even with duplicate names.
            if (materialIndices != null)
                for (var i = 0; i < model.Materials.Count; i++) materialIndices.Add((Material)model.Materials[i], i);
            if (settings.ExportVertexColor) PreserveVertexColors(model, exporter.Storage);
            afterExport?.Invoke(converter, model, exporter.Storage);
            return exporter.Storage.ToGlbBytes();
        }

        // ConvertCoordinate rebuilds every node from its parent's inverse world
        // matrix. Gimmicks hide parts with a zero or subnormal (1e-45) scale,
        // which has no usable inverse: Unity logs an Assertion failed
        // 'ValidTRS()' per descendant and they lose their local transforms.
        // Convert with near-zero axes at 1, then restore only those axes.
        //
        // UniVRM splits each reversed world matrix into a positive scale and
        // Matrix4x4.rotation, which is not a rotation when the matrix mirrors
        // (a negative scale on one or three axes). A mirrored prop then came
        // back turned. Mirroring X is F * M * F with F = diag(-1, 1, 1), which
        // keeps a local TRS exact: translation x and rotation y/z negate, scale
        // stays. Use that for every mirrored node and the nodes below it, and
        // keep UniVRM's result elsewhere so other exports stay byte-identical.
        internal static void ConvertCoordinateKeepingZeroScale(Model model)
        {
            var zeroed = new List<(Node node, Vector3 scale)>();
            var mirrored = new List<(Node node, Vector3 translation, Quaternion rotation, Vector3 scale)>();
            var mirroredNodes = new HashSet<Node>();
            foreach (var node in model.Root.Traverse().Skip(1))
            {
                var scale = node.LocalScalingWithoutUpdate;
                if (scale.x * scale.y * scale.z < 0 || node.Parent != null && mirroredNodes.Contains(node.Parent))
                {
                    mirroredNodes.Add(node);
                    mirrored.Add((node, node.LocalTranslationWithoutUpdate, node.LocalRotationWithoutUpdate, scale));
                }
                if (!NearZero(scale.x) && !NearZero(scale.y) && !NearZero(scale.z)) continue;
                zeroed.Add((node, scale));
                node.LocalScalingWithoutUpdate = new Vector3(NearZero(scale.x) ? 1 : scale.x,
                    NearZero(scale.y) ? 1 : scale.y, NearZero(scale.z) ? 1 : scale.z);
            }
            if (zeroed.Count > 0) model.Root.CalcWorldMatrix();
            model.ConvertCoordinate(Coordinates.Vrm1);
            if (zeroed.Count == 0 && mirrored.Count == 0) return;
            foreach (var (node, scale) in zeroed)
            {
                var converted = node.LocalScalingWithoutUpdate;
                node.LocalScalingWithoutUpdate = new Vector3(NearZero(scale.x) ? scale.x : converted.x,
                    NearZero(scale.y) ? scale.y : converted.y, NearZero(scale.z) ? scale.z : converted.z);
            }
            foreach (var (node, translation, rotation, scale) in mirrored)
            {
                node.LocalTranslationWithoutUpdate = new Vector3(-translation.x, translation.y, translation.z);
                node.LocalRotationWithoutUpdate = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w);
                node.LocalScalingWithoutUpdate = scale;
            }
            model.Root.CalcWorldMatrix();
        }

        private static bool NearZero(float value) => Mathf.Abs(value) < 1e-6f;

        // A skinned mesh whose transform mirrors (an FBX imported with a
        // negative scale) has mirrored bindposes. UniVRM converts inverse bind
        // matrices with the same positive-scale split as nodes, both when
        // exporting and when loading, and drops the mirror: the mesh came out
        // upside down and inside out. Move the mirror into the mesh instead.
        // With F = diag(-1, 1, 1), bindpose * v = (bindpose * F) * (F * v), so
        // negating x of the mesh-local positions and normals (morph targets
        // too) and right-multiplying each bindpose by F leaves every skinned
        // world vertex, normal and triangle as it was, and the bindposes become
        // a rotation and a positive scale that any loader converts exactly.
        // A skin whose bindposes mirror only for some bones has no single F
        // that fixes it and is left as it was.
        internal static void UnmirrorSkins(Model model)
        {
            // Buffers can be shared, so each one is mirrored once.
            var done = new HashSet<NativeArray<byte>>();
            var unmirrored = new HashSet<NativeArray<byte>>();
            void Mirror(BufferAccessor buffer)
            {
                if (buffer == null || !done.Add(buffer.Bytes)) return;
                var values = buffer.GetSpan<Vector3>();
                for (var i = 0; i < values.Length; i++) values[i] = new Vector3(-values[i].x, values[i].y, values[i].z);
            }
            foreach (var group in model.MeshGroups)
            {
                var binds = group.Skin?.InverseMatrices;
                if (binds == null) continue;
                if (!unmirrored.Contains(binds.Bytes))
                {
                    var poses = binds.GetSpan<Matrix4x4>();
                    if (poses.Length == 0 || Enumerable.Range(0, poses.Length).Any(i => !(poses[i].determinant < 0))) continue;
                    for (var i = 0; i < poses.Length; i++) poses[i] = poses[i] * MirrorX;
                    unmirrored.Add(binds.Bytes);
                }
                foreach (var mesh in group.Meshes)
                {
                    Mirror(mesh.VertexBuffer.Positions);
                    Mirror(mesh.VertexBuffer.Normals);
                    foreach (var morph in mesh.MorphTargets)
                    {
                        Mirror(morph.VertexBuffer.Positions);
                        Mirror(morph.VertexBuffer.Normals);
                    }
                }
            }
            if (unmirrored.Count == 0) return;
            // glTF and three.js choose the front side of a skinned primitive by
            // the determinant of its node, while Unity draws a skinned mesh in
            // its root bone's space. Turn the mesh node's world matrix W into
            // W * F so it no longer mirrors, and give each child F * local so
            // the children stay where they were. The skin ignores the node.
            model.Root.CalcWorldMatrix();
            var joints = new HashSet<Node>(model.Skins.SelectMany(s => s.Joints));
            var nodes = model.Root.Traverse().Where(n => n.MeshGroup?.Skin?.InverseMatrices is { } b
                && unmirrored.Contains(b.Bytes) && n.Matrix.determinant < 0 && !joints.Contains(n)).ToList();
            foreach (var node in nodes)
            {
                var s = node.LocalScalingWithoutUpdate;
                node.LocalScalingWithoutUpdate = new Vector3(-s.x, s.y, s.z);
                foreach (var child in node.Children)
                {
                    var t = child.LocalTranslationWithoutUpdate;
                    var r = child.LocalRotationWithoutUpdate;
                    var c = child.LocalScalingWithoutUpdate;
                    child.LocalTranslationWithoutUpdate = new Vector3(-t.x, t.y, t.z);
                    child.LocalRotationWithoutUpdate = new Quaternion(r.x, -r.y, -r.z, r.w);
                    child.LocalScalingWithoutUpdate = new Vector3(-c.x, c.y, c.z);
                }
            }
            if (nodes.Count > 0) model.Root.CalcWorldMatrix();
        }

        private static readonly Matrix4x4 MirrorX = Matrix4x4.Scale(new Vector3(-1, 1, 1));

        // The pinned ModelExporter computes useNormal but only copies each
        // morph's POSITION buffer. Fill NORMAL from its exact renderer/node map
        // while both buffers still use Unity coordinates and source vertex order.
        // MeshWriter subsequently applies its own submesh index remapping.
        private static void PreserveMorphNormals(ModelExporter converter, NativeArrayManager arrays, GltfExportSettings settings)
        {
            if (settings.ExportOnlyBlendShapePosition) return;
            foreach (var pair in converter.Nodes)
            {
                var group = pair.Value.MeshGroup;
                if (group == null) continue;
                var skin = pair.Key.GetComponent<SkinnedMeshRenderer>();
                var filter = pair.Key.GetComponent<MeshFilter>();
                var source = skin != null ? skin.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (source == null)
                    throw new InvalidOperationException("UniVRMの元メッシュを特定できないため、表情の法線を安全に保存できません。");
                if (group.Meshes.Count != 1 || group.Meshes[0].VertexBuffer.Count != source.vertexCount ||
                    group.Meshes[0].MorphTargets.Count != source.blendShapeCount)
                    throw new InvalidOperationException("UniVRMの元メッシュとMorphTargetの対応が一致しないため、表情の法線を安全に保存できません。");
                if (source.normals.Length != source.vertexCount) continue;
                var mesh = group.Meshes[0];
                var vertices = new Vector3[source.vertexCount];
                for (var shape = 0; shape < source.blendShapeCount; shape++)
                {
                    // UniVRM 0.131.3+ CreateNativeArray wraps the managed array
                    // without copying, so each morph needs its own normals array.
                    var normals = new Vector3[source.vertexCount];
                    var target = mesh.MorphTargets[shape];
                    var frames = source.GetBlendShapeFrameCount(shape);
                    if (frames == 0 || target.Name != source.GetBlendShapeName(shape) || target.VertexBuffer.Count != source.vertexCount)
                        throw new InvalidOperationException("UniVRMの元BlendShapeとMorphTargetの対応が一致しないため、表情の法線を安全に保存できません: " + source.name);
                    source.GetBlendShapeFrameVertices(shape, frames - 1, vertices, normals, null);
                    var buffer = arrays.CreateNativeArray(normals).Reinterpret<byte>(12);
                    target.VertexBuffer.Add(VertexBuffer.NormalKey,
                        new BufferAccessor(arrays, buffer, AccessorValueType.FLOAT, AccessorVectorType.VEC3, normals.Length));
                }
            }
        }

        // UniVRM 0.131 ModelExporter retains COLOR_0 but MeshWriter's divided
        // primitive path drops it. Use the model/primitive correspondence while
        // it is still available, never mesh names or nearest-position matching.
        internal static void PreserveVertexColors(Model model, ExportingGltfData storage)
        {
            if (model.MeshGroups.Count != storage.Gltf.meshes.Count)
                throw new InvalidOperationException("UniVRMのメッシュ対応が変更されています。頂点カラーを安全に保存できません。");
            for (var meshIndex = 0; meshIndex < model.MeshGroups.Count; meshIndex++)
            {
                var group = model.MeshGroups[meshIndex];
                if (group.Meshes.Count != 1) throw new InvalidOperationException("UniVRMのメッシュ構成に対応していません。");
                var mesh = group.Meshes[0];
                if (mesh.VertexBuffer.Colors == null) continue;
                var colors = mesh.VertexBuffer.Colors.GetSpan<Color>();
                var indices = mesh.IndexBuffer.GetAsIntArray();
                var primitives = storage.Gltf.meshes[meshIndex].primitives;
                if (primitives.Count != mesh.Submeshes.Count) throw new InvalidOperationException("頂点カラーのサブメッシュ対応が一致しません。");
                for (var submeshIndex = 0; submeshIndex < mesh.Submeshes.Count; submeshIndex++)
                {
                    var submesh = mesh.Submeshes[submeshIndex];
                    // Same ascending source-index order as 0.131 MeshWriter.
                    var used = new SortedSet<int>();
                    for (var i = submesh.Offset; i < submesh.Offset + submesh.DrawCount; i++) used.Add(indices[i]);
                    var output = used.Select(i => colors[i]).ToArray();
                    var attributes = primitives[submeshIndex].attributes;
                    if (storage.Gltf.accessors[attributes.POSITION].count != output.Length)
                        throw new InvalidOperationException("頂点カラーと出力形状の頂点数が一致しません。");
                    attributes.COLOR_0 = storage.ExtendBufferAndGetAccessorIndex(output, glBufferTarget.ARRAY_BUFFER);
                }
            }
        }
    }
}
