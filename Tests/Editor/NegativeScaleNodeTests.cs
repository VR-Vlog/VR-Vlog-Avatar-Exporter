using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // A prop mirrored with a negative scale on one or three axes, held by a
    // turned bone, must come back from the VRM at the same world vertices with
    // its front faces still facing out. Its child must follow it as well.
    public sealed class NegativeScaleNodeTests
    {
        [TestCase(-1, 1, 1)]
        [TestCase(1, 1, -1)]
        [TestCase(-1, -1, -1)]
        public async Task MirroredPropKeepsWorldVerticesAndFrontFacesThroughVrmRoundTrip(float x, float y, float z)
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Requires installed lilToon for the full exporter entry point.");
            using var f = new AttachmentConnectionTests.Fixture();
            var hand = f.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            var holder = new GameObject("Prop holder").transform; holder.SetParent(hand, false);
            holder.localPosition = new Vector3(.04f, -.02f, .01f); holder.localRotation = Quaternion.Euler(20, 70, -35);
            var prop = new GameObject("Mirrored prop").transform; prop.SetParent(holder, false);
            prop.localPosition = new Vector3(.03f, .01f, -.02f); prop.localRotation = Quaternion.Euler(-15, 40, 10);
            prop.localScale = new Vector3(x, y, z);
            var part = new GameObject("Mirrored prop part").transform; part.SetParent(prop, false);
            part.localPosition = new Vector3(.05f, .02f, .03f); part.localRotation = Quaternion.Euler(30, -20, 60);
            part.localScale = new Vector3(.8f, .8f, .8f);
            var mesh = new Mesh { name = "Uneven prop" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(.08f, 0, .01f), new Vector3(.01f, .05f, 0), new Vector3(.02f, .01f, .12f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.RecalculateNormals();
            var material = new Material(shader);
            foreach (var skin in f.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterials = new[] { material };
            foreach (var node in new[] { prop, part })
            {
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                node.gameObject.AddComponent<MeshRenderer>().sharedMaterials = new[] { material };
            }
            var expected = new[] { prop, part }.ToDictionary(t => t.name, t => World(t.GetComponent<MeshRenderer>()));
            var sourceBefore = f.Source.GetComponentsInChildren<Transform>(true).Select(t => t.localToWorldMatrix).ToArray();
            Vrm10Instance imported = null;
            try
            {
                var bytes = UniVrmOneClickExporter.Export(f.Source, "Mirrored prop", "Test", new List<string>(),
                    exporterVersion: "0.9.0", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(f.Source.GetComponentsInChildren<Transform>(true).Select(t => t.localToWorldMatrix), Is.EqualTo(sourceBefore),
                    "The source avatar is not changed.");
                Assert.That(prop.localScale, Is.EqualTo(new Vector3(x, y, z)));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller(),
                    controlRigGenerationOption: ControlRigGenerationOption.None);
                foreach (var pair in expected)
                {
                    var renderer = imported.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == pair.Key);
                    AssertSameSurface(pair.Value, World(renderer), pair.Key);
                }
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            }
        }

        // A skinned outfit whose mesh transform mirrors (FBX imported with a
        // negative scale, so its bindposes mirror too) must come back skinned
        // to the same world vertices and normals with its front faces out,
        // also when the mesh node is its own root bone or there is none, so
        // Unity drew the source in the mirrored node space.
        [TestCase(-1.1f, -.9f, -.8f, "bone")]
        [TestCase(-1f, 1f, 1f, "bone")]
        [TestCase(1f, 1f, -1.2f, "bone")]
        [TestCase(-1f, 1f, 1f, "mesh")]
        [TestCase(1f, -1f, 1f, "none")]
        public async Task MirroredSkinnedMeshKeepsWorldVerticesAndFrontFacesThroughVrmRoundTrip(float x, float y, float z, string root)
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Requires installed lilToon for the full exporter entry point.");
            using var f = new AttachmentConnectionTests.Fixture();
            var animator = f.Source.GetComponent<Animator>();
            var bones = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest }.Select(animator.GetBoneTransform).ToArray();
            var outfit = new GameObject("Mirrored outfit").transform; outfit.SetParent(f.Source.transform, false);
            outfit.localPosition = new Vector3(.01f, .02f, -.03f); outfit.localScale = Vector3.one * 1.12f;
            var body = new GameObject("Mirrored outfit body").transform; body.SetParent(outfit, false);
            body.localPosition = new Vector3(0, 1.1f, -.01f); body.localRotation = Quaternion.Euler(-90, 15, 5);
            body.localScale = new Vector3(x, y, z) * .2f;
            var mesh = new Mesh { name = "Uneven outfit" };
            mesh.vertices = new[] { new Vector3(0, 0, 0), new Vector3(.8f, 0, .1f), new Vector3(.1f, .5f, 0), new Vector3(.2f, .1f, 1.2f) };
            mesh.triangles = new[] { 0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3 };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.RecalculateNormals();
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = .5f, boneIndex1 = 1, weight1 = .5f },
                new BoneWeight { boneIndex0 = 1, weight0 = .7f, boneIndex1 = 2, weight1 = .3f },
                new BoneWeight { boneIndex0 = 2, weight0 = 1 },
            };
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * body.localToWorldMatrix).ToArray();
            Assert.That(mesh.bindposes[0].determinant < 0, Is.EqualTo(x * y * z < 0));
            var material = new Material(shader);
            foreach (var skin in f.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterials = new[] { material };
            var renderer = body.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh; renderer.sharedMaterials = new[] { material };
            renderer.bones = bones; renderer.rootBone = root == "bone" ? bones[0] : root == "mesh" ? body : null;
            var expected = World(renderer, renderer.rootBone != null ? renderer.rootBone : renderer.transform);
            Vrm10Instance imported = null;
            try
            {
                var bytes = UniVrmOneClickExporter.Export(f.Source, "Mirrored outfit", "Test", new List<string>(),
                    exporterVersion: "0.9.0", lilToonVersion: "2.3.4", blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None });
                Assert.That(body.localScale, Is.EqualTo(new Vector3(x, y, z) * .2f));
                imported = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller(),
                    controlRigGenerationOption: ControlRigGenerationOption.None);
                var result = imported.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == body.name);
                // Unity draws the source and the loaded mesh in the root bone's
                // space; glTF viewers such as three.js use the mesh node.
                AssertSameSurface(expected, World(result, result.rootBone != null ? result.rootBone : result.transform), body.name + " (root bone)");
                AssertSameSurface(expected, World(result, result.transform), body.name + " (node)");
            }
            finally
            {
                if (imported != null) Object.DestroyImmediate(imported.gameObject);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(material);
            }
        }

        // Moving the mirror out of the bindposes must leave every skinned
        // vertex, normal and blend shape delta where it was. A skin that
        // mirrors for only some bones is left as it is.
        [TestCase(true)]
        [TestCase(false)]
        public void UnmirrorSkinsMovesTheMirrorIntoMeshAndBlendShapes(bool allMirrored)
        {
            var root = new GameObject("Mirror root");
            var mesh = new Mesh { name = "Mirrored shape" };
            try
            {
                var bone0 = new GameObject("Bone0").transform; bone0.SetParent(root.transform, false);
                bone0.localPosition = new Vector3(.1f, 1, 0); bone0.localRotation = Quaternion.Euler(10, 20, 30);
                var bone1 = new GameObject("Bone1").transform; bone1.SetParent(bone0, false);
                bone1.localPosition = new Vector3(0, .3f, .1f); bone1.localRotation = Quaternion.Euler(-40, 5, 0);
                var body = new GameObject("Body").transform; body.SetParent(root.transform, false);
                body.localPosition = new Vector3(0, .9f, .05f); body.localRotation = Quaternion.Euler(-90, 0, 0);
                body.localScale = new Vector3(-.5f, .4f, .3f);
                var held = new GameObject("Held").transform; held.SetParent(body, false);
                held.localPosition = new Vector3(.2f, -.1f, .3f); held.localRotation = Quaternion.Euler(20, -30, 40);
                held.localScale = new Vector3(1, -2, 1.5f);
                mesh.vertices = new[] { new Vector3(.1f, 0, 0), new Vector3(.6f, .2f, .1f), new Vector3(.2f, .5f, .3f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.normals = new[] { Vector3.forward, new Vector3(.6f, 0, .8f), Vector3.up };
                mesh.boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                    new BoneWeight { boneIndex0 = 0, weight0 = .4f, boneIndex1 = 1, weight1 = .6f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 1 },
                };
                var binds = new[] { bone0.worldToLocalMatrix * body.localToWorldMatrix, bone1.worldToLocalMatrix * body.localToWorldMatrix };
                if (!allMirrored) binds[1] *= Matrix4x4.Scale(new Vector3(-1, 1, 1));
                mesh.bindposes = binds;
                mesh.AddBlendShapeFrame("Shape", 100,
                    new[] { new Vector3(.3f, .1f, 0), new Vector3(-.2f, 0, .1f), Vector3.zero },
                    new[] { new Vector3(.2f, 0, 0), Vector3.zero, new Vector3(0, .1f, -.3f) }, null);
                var renderer = body.gameObject.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh; renderer.sharedMaterials = new Material[] { null };
                renderer.bones = new[] { bone0, bone1 }; renderer.rootBone = bone0;

                using var arrays = new NativeArrayManager();
                var model = new ModelExporter().Export(new GltfExportSettings(), arrays, root);
                var group = model.MeshGroups.Single(g => g.Skin != null);
                var data = group.Meshes.Single();
                var morph = data.MorphTargets.Single().VertexBuffer;
                Vector3[] Read(BufferAccessor b) => b?.GetSpan<Vector3>().ToArray();
                Matrix4x4[] Binds() => group.Skin.InverseMatrices.GetSpan<Matrix4x4>().ToArray();
                var before = (binds: Binds(), pos: Read(data.VertexBuffer.Positions), nrm: Read(data.VertexBuffer.Normals),
                    dPos: Read(morph.Positions), dNrm: Read(morph.Normals));
                Assert.That(before.dPos.Any(v => v != Vector3.zero), Is.True, "The blend shape must reach the model.");
                var bodyNode = model.Root.Traverse().Single(n => n.Name == body.name);
                var heldNode = model.Root.Traverse().Single(n => n.Name == held.name);
                var heldWorld = heldNode.Matrix;

                Vrm10AppearanceExporter.UnmirrorSkins(model);
                var after = (binds: Binds(), pos: Read(data.VertexBuffer.Positions), nrm: Read(data.VertexBuffer.Normals),
                    dPos: Read(morph.Positions), dNrm: Read(morph.Normals));
                Assert.That(bodyNode.Matrix.determinant < 0, Is.EqualTo(!allMirrored), "the mesh node mirrors as its skin does");
                for (var k = 0; k < 16; k++) Assert.That(heldNode.Matrix[k], Is.EqualTo(heldWorld[k]).Within(1e-5f), "the child keeps its world matrix");
                if (!allMirrored)
                {
                    Assert.That(after.binds, Is.EqualTo(before.binds));
                    Assert.That(after.pos, Is.EqualTo(before.pos));
                    Assert.That(after.dPos, Is.EqualTo(before.dPos));
                    return;
                }
                Vector3 Flip(Vector3 v) => new Vector3(-v.x, v.y, v.z);
                for (var i = 0; i < after.binds.Length; i++)
                {
                    Assert.That(after.binds[i].determinant, Is.GreaterThan(0), $"bindpose {i} stays mirrored");
                    for (var j = 0; j < after.pos.Length; j++)
                    {
                        AssertNear(after.binds[i].MultiplyPoint3x4(after.pos[j]), before.binds[i].MultiplyPoint3x4(before.pos[j]), $"vertex {j} by bone {i}");
                        AssertNear(after.binds[i].MultiplyVector(after.dPos[j]), before.binds[i].MultiplyVector(before.dPos[j]), $"blend shape delta {j} by bone {i}");
                    }
                }
                for (var j = 0; j < after.nrm.Length; j++) AssertNear(after.nrm[j], Flip(before.nrm[j]), $"normal {j}");
                if (before.dNrm != null)
                    for (var j = 0; j < after.dNrm.Length; j++) AssertNear(after.dNrm[j], Flip(before.dNrm[j]), $"blend shape normal {j}");
            }
            finally
            {
                Object.DestroyImmediate(root); Object.DestroyImmediate(mesh);
            }
        }

        static void AssertNear(Vector3 actual, Vector3 expected, string what) =>
            Assert.That((actual - expected).magnitude, Is.LessThan(1e-5f), $"{what}: {actual} != {expected}");

        // Linear blend skinning as Unity draws it. The culled side follows the
        // determinant of the space the mesh is drawn in.
        static Surface World(SkinnedMeshRenderer renderer, Transform space)
        {
            var mesh = renderer.sharedMesh;
            var bindposes = mesh.bindposes; var bones = renderer.bones;
            var vertices = mesh.vertices; var normals = mesh.normals; var weights = mesh.boneWeights;
            var surface = new Surface
            {
                Positions = new Vector3[vertices.Length], Normals = new Vector3[vertices.Length], Triangles = mesh.triangles,
                Mirrored = space.localToWorldMatrix.determinant < 0,
            };
            for (var i = 0; i < vertices.Length; i++)
            {
                var w = weights[i];
                var matrix = new Matrix4x4();
                foreach (var (index, weight) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                {
                    if (weight == 0) continue;
                    var m = bones[index].localToWorldMatrix * bindposes[index];
                    for (var k = 0; k < 16; k++) matrix[k] += weight * m[k];
                }
                surface.Positions[i] = matrix.MultiplyPoint3x4(vertices[i]);
                surface.Normals[i] = matrix.inverse.transpose.MultiplyVector(normals[i]).normalized;
            }
            return surface;
        }

        sealed class Surface
        {
            internal Vector3[] Positions, Normals;
            internal int[] Triangles;
            internal bool Mirrored;
        }

        static Surface World(MeshRenderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            var matrix = renderer.transform.localToWorldMatrix;
            var normalMatrix = matrix.inverse.transpose;
            return new Surface
            {
                Positions = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray(),
                Normals = mesh.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray(),
                Triangles = mesh.triangles,
                // Unity draws the other side of each triangle under a mirroring matrix.
                Mirrored = matrix.determinant < 0,
            };
        }

        static Vector3 Front(Surface surface, int triangle)
        {
            var p = surface.Positions; var t = surface.Triangles;
            var a = p[t[triangle]]; var b = p[t[triangle + 1]]; var c = p[t[triangle + 2]];
            var normal = Vector3.Cross(b - a, c - a).normalized;
            return surface.Mirrored ? -normal : normal;
        }

        static void AssertSameSurface(Surface expected, Surface actual, string name)
        {
            Assert.That(actual.Positions.Length, Is.EqualTo(expected.Positions.Length), name);
            Assert.That(actual.Triangles.Length, Is.EqualTo(expected.Triangles.Length), name);
            // Vertex order may change on export; every source vertex is at a distinct place.
            var map = new int[expected.Positions.Length];
            for (var i = 0; i < map.Length; i++)
            {
                var source = expected.Positions[i];
                map[i] = Enumerable.Range(0, actual.Positions.Length).OrderBy(j => Vector3.Distance(actual.Positions[j], source)).First();
                Assert.That(Vector3.Distance(actual.Positions[map[i]], source), Is.LessThan(.0005f), name + " vertex " + i + " world position");
                Assert.That(Vector3.Dot(actual.Normals[map[i]], expected.Normals[i]), Is.GreaterThan(.999f), name + " vertex " + i + " normal");
            }
            for (var triangle = 0; triangle < expected.Triangles.Length; triangle += 3)
            {
                var corners = new HashSet<int> { map[expected.Triangles[triangle]], map[expected.Triangles[triangle + 1]], map[expected.Triangles[triangle + 2]] };
                var match = Enumerable.Range(0, actual.Triangles.Length / 3).Select(k => k * 3)
                    .Where(k => corners.SetEquals(new[] { actual.Triangles[k], actual.Triangles[k + 1], actual.Triangles[k + 2] })).ToArray();
                Assert.That(match.Length, Is.EqualTo(1), name + " triangle " + triangle / 3);
                Assert.That(Vector3.Dot(Front(actual, match[0]), Front(expected, triangle)), Is.GreaterThan(.999f), name + " triangle " + triangle / 3 + " front face");
            }
        }
    }
}
