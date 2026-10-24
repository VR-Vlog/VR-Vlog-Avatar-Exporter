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
