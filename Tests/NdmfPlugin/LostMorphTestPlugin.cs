using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.ndmf;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(VRVlog.LilToonExporter.Tests.LostMorphTestPlugin))]

namespace VRVlog.LilToonExporter.Tests
{
    // A test-only build pass that runs after Modular Avatar in Transforming,
    // like a generic mesh tool would. It acts only on avatars that carry a
    // child named Marker + mode, and removes that child. It does nothing until
    // a test sets Armed, so a project that only installs the package is safe.
    //   strip  : rebuild "Front" without "Unused" and generated shapes
    //   rename : rename "Front" and replace its SkinnedMeshRenderer
    //   merge  : append "Front" into "Back" and remove "Front"
    //   twin   : remove "Face blink" from "Front" and replace the
    //            SkinnedMeshRenderer of "Back", which has its own "Face blink"
    public sealed class LostMorphTestPlugin : Plugin<LostMorphTestPlugin>
    {
        internal const string Marker = "__VRVlogLostMorphTest_";
        internal const string RenamedFront = "Face renamed";
        public static bool Armed;

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).AfterPlugin("nadena.dev.modular-avatar")
                .Run("VR-Vlog lost morph test", context => Apply(context.AvatarRootObject));
        }

        static void Apply(GameObject root)
        {
            if (!Armed) return;
            var marker = root.transform.Cast<Transform>().FirstOrDefault(child => child.name.StartsWith(Marker, StringComparison.Ordinal));
            if (marker == null) return;
            var mode = marker.name.Substring(Marker.Length);
            Object.DestroyImmediate(marker.gameObject);
            var front = root.transform.Find("Front")?.GetComponent<SkinnedMeshRenderer>();
            if (front == null) throw new InvalidOperationException("The lost morph test needs a Front renderer.");
            switch (mode)
            {
                case "strip": front.sharedMesh = Strip(front.sharedMesh, name => name == "Unused" || name.StartsWith("__VRVlog_", StringComparison.Ordinal)); break;
                case "rename": Replace(front, RenamedFront); break;
                case "merge": Merge(front, Back(root)); break;
                case "twin": front.sharedMesh = Strip(front.sharedMesh, name => name == "Face blink"); Replace(Back(root), null); break;
                default: throw new InvalidOperationException("Unknown lost morph test mode: " + mode);
            }
        }

        static SkinnedMeshRenderer Back(GameObject root) =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(skin => skin.name == "Back");

        static Mesh Strip(Mesh mesh, Func<string, bool> removed)
        {
            var result = Object.Instantiate(mesh); result.name = mesh.name;
            result.ClearBlendShapes();
            var count = mesh.vertexCount;
            for (var index = 0; index < mesh.blendShapeCount; index++)
            {
                var name = mesh.GetBlendShapeName(index);
                if (removed(name)) continue;
                for (var frame = 0; frame < mesh.GetBlendShapeFrameCount(index); frame++)
                {
                    var vertices = new Vector3[count]; var normals = new Vector3[count]; var tangents = new Vector3[count];
                    mesh.GetBlendShapeFrameVertices(index, frame, vertices, normals, tangents);
                    result.AddBlendShapeFrame(name, mesh.GetBlendShapeFrameWeight(index, frame), vertices, normals, tangents);
                }
            }
            return result;
        }

        static void Replace(SkinnedMeshRenderer front, string name)
        {
            var target = front.gameObject;
            var mesh = front.sharedMesh; var materials = front.sharedMaterials;
            var bones = front.bones; var rootBone = front.rootBone;
            var weights = Enumerable.Range(0, mesh.blendShapeCount).Select(front.GetBlendShapeWeight).ToArray();
            Object.DestroyImmediate(front);
            if (name != null) target.name = name;
            var next = target.AddComponent<SkinnedMeshRenderer>();
            next.sharedMesh = mesh; next.sharedMaterials = materials; next.bones = bones; next.rootBone = rootBone;
            for (var index = 0; index < weights.Length; index++) next.SetBlendShapeWeight(index, weights[index]);
        }

        // Both meshes use the same bones and bind poses, so the vertices can be
        // appended as they are.
        static void Merge(SkinnedMeshRenderer front, SkinnedMeshRenderer back)
        {
            Mesh first = back.sharedMesh, second = front.sharedMesh;
            int a = first.vertexCount, b = second.vertexCount;
            var mesh = new Mesh { name = first.name };
            mesh.vertices = first.vertices.Concat(second.vertices).ToArray();
            mesh.normals = first.normals.Concat(second.normals).ToArray();
            mesh.uv = first.uv.Concat(second.uv).ToArray();
            mesh.boneWeights = first.boneWeights.Concat(second.boneWeights).ToArray();
            mesh.bindposes = first.bindposes;
            mesh.triangles = first.triangles.Concat(second.triangles.Select(index => index + a)).ToArray();
            var weights = new List<float>();
            void Shapes(Mesh source, SkinnedMeshRenderer renderer, bool before)
            {
                var n = source.vertexCount;
                for (var index = 0; index < source.blendShapeCount; index++)
                {
                    for (var frame = 0; frame < source.GetBlendShapeFrameCount(index); frame++)
                    {
                        var deltas = new Vector3[n]; var normals = new Vector3[n]; var tangents = new Vector3[n];
                        source.GetBlendShapeFrameVertices(index, frame, deltas, normals, tangents);
                        Vector3[] Place(Vector3[] values) => before
                            ? values.Concat(new Vector3[b]).ToArray() : new Vector3[a].Concat(values).ToArray();
                        mesh.AddBlendShapeFrame(source.GetBlendShapeName(index), source.GetBlendShapeFrameWeight(index, frame),
                            Place(deltas), Place(normals), Place(tangents));
                    }
                    weights.Add(renderer.GetBlendShapeWeight(index));
                }
            }
            Shapes(first, back, true);
            Shapes(second, front, false);
            back.sharedMesh = mesh;
            for (var index = 0; index < weights.Count; index++) back.SetBlendShapeWeight(index, weights[index]);
            Object.DestroyImmediate(front.gameObject);
        }
    }
}
