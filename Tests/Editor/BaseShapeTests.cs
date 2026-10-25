using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class BaseShapeTests
    {
        [Test] public void GeometryAndExpressionEndpoints() => BaseShapeFixture.Run((ok, message) => Assert.IsTrue(ok, message));

        [Test] public void PreserveSharedMeshesPerRendererAndLeaveSourceUntouched()
        {
            var source = new GameObject("Avatar");
            GameObject clone = null;
            var mesh = BaseShapeFixture.Create();
            var temporary = new List<Mesh>();
            try
            {
                var a = new GameObject("Duplicate name");
                var b = new GameObject("Duplicate name");
                a.transform.SetParent(source.transform, false);
                b.transform.SetParent(source.transform, false);
                var first = a.AddComponent<SkinnedMeshRenderer>();
                var second = b.AddComponent<SkinnedMeshRenderer>();
                first.sharedMesh = mesh; second.sharedMesh = mesh;
                first.SetBlendShapeWeight(0, 25); second.SetBlendShapeWeight(0, 75);
                clone = Object.Instantiate(source);
                AvatarBaseShape.Preserve(source, clone, temporary, null);
                var copies = clone.GetComponentsInChildren<SkinnedMeshRenderer>();
                Assert.AreEqual(2f, copies[0].sharedMesh.vertices[0].x);
                Assert.AreEqual(4f, copies[1].sharedMesh.vertices[0].x);
                Assert.AreEqual(0f, copies[0].GetBlendShapeWeight(0));
                Assert.AreEqual(25f, first.GetBlendShapeWeight(0));
                Assert.AreSame(mesh, first.sharedMesh);
                Assert.AreEqual(1f, mesh.vertices[0].x);
                Assert.AreNotSame(copies[0].sharedMesh, copies[1].sharedMesh);
            }
            finally
            {
                Object.DestroyImmediate(clone); Object.DestroyImmediate(source);
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }

        // A shape whose frames the rest cannot be evaluated from is written as
        // authored at weight 0; the other shapes are still stored in the base.
        [Test] public void PreserveLeavesOutTheValueOfAShapeWithUnsupportedFramesAndWarns()
        {
            var source = new GameObject("Avatar");
            GameObject clone = null;
            var mesh = BaseShapeFixture.Create();
            var temporary = new List<Mesh>();
            var warnings = new List<string>();
            try
            {
                var empty = new Vector3[3];
                mesh.AddBlendShapeFrame("Unusual frames", -50, new[] { new Vector3(-1, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                mesh.AddBlendShapeFrame("Unusual frames", 0, new[] { new Vector3(1, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                mesh.AddBlendShapeFrame("Unusual frames", 100, new[] { new Vector3(2, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                var face = new GameObject("Face");
                face.transform.SetParent(source.transform, false);
                var skin = face.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                skin.SetBlendShapeWeight(0, 25); skin.SetBlendShapeWeight(2, 30);
                clone = Object.Instantiate(source);
                Assert.DoesNotThrow(() => AvatarBaseShape.Preserve(clone, clone, temporary, warnings));
                var copy = clone.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.AreNotSame(mesh, copy.sharedMesh);
                Assert.AreEqual(2f, copy.sharedMesh.vertices[0].x, 0.00001f);
                Assert.AreEqual(0f, copy.GetBlendShapeWeight(0));
                Assert.AreEqual(0f, copy.GetBlendShapeWeight(2));
                Assert.AreEqual(3, copy.sharedMesh.GetBlendShapeFrameCount(2));
                Assert.That(warnings.Exists(warning => warning.Contains("Unusual frames")), warnings.Count.ToString());
                Assert.AreEqual(1f, mesh.vertices[0].x);
            }
            finally
            {
                Object.DestroyImmediate(clone); Object.DestroyImmediate(source);
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }

        // A face whose Lip Sync rests on sil, with one vowel and an unrelated
        // shape that also rests away from 0. Shape indices: 0 sil, 1 ou, 2 smile.
        static Mesh VisemeMesh()
        {
            var mesh = BaseShapeFixture.Create();
            mesh.ClearBlendShapes();
            mesh.AddBlendShapeFrame("vrc.v_sil", 100, new[] { new Vector3(0, 1, 0), Vector3.zero, Vector3.zero },
                new[] { new Vector3(1, 0, 0), Vector3.zero, Vector3.zero }, new[] { new Vector3(0, 1, 0), Vector3.zero, Vector3.zero });
            mesh.AddBlendShapeFrame("vrc.v_ou", 100, new[] { new Vector3(0, 0, 2), Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            mesh.AddBlendShapeFrame("Smile", 100, new[] { new Vector3(4, 0, 0), Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
            return mesh;
        }

        static (Mesh Baked, List<string> Warnings) PreserveFace(Mesh mesh, float[] weights, ICollection<Mesh> temporary,
            System.Func<SkinnedMeshRenderer, (SkinnedMeshRenderer Renderer, string[] Shapes)> visemes)
        {
            var avatar = new GameObject("Avatar");
            try
            {
                var face = new GameObject("Face");
                face.transform.SetParent(avatar.transform, false);
                var skin = face.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                for (var i = 0; i < weights.Length; i++) skin.SetBlendShapeWeight(i, weights[i]);
                var warnings = new List<string>();
                var (renderer, shapes) = visemes(skin);
                AvatarBaseShape.Preserve(avatar, avatar, temporary, warnings, visemeRenderer: renderer, visemeShapes: shapes);
                return (skin.sharedMesh, warnings);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        static (SkinnedMeshRenderer, string[]) Visemes(SkinnedMeshRenderer renderer, params string[] shapes) => (renderer, shapes);

        static Vector3 Frame(Mesh mesh, string shape, int part, int frame = 0)
        {
            var v = new Vector3[mesh.vertexCount]; var n = new Vector3[mesh.vertexCount]; var t = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex(shape), frame, v, n, t);
            return (part == 0 ? v : part == 1 ? n : t)[0];
        }

        static (int, int, int) Bits(Vector3 value) => (System.BitConverter.SingleToInt32Bits(value.x),
            System.BitConverter.SingleToInt32Bits(value.y), System.BitConverter.SingleToInt32Bits(value.z));

        static void AssertSameMesh(Mesh expected, Mesh actual)
        {
            CollectionAssert.AreEqual(expected.vertices.Select(Bits), actual.vertices.Select(Bits));
            CollectionAssert.AreEqual(expected.normals.Select(Bits), actual.normals.Select(Bits));
            CollectionAssert.AreEqual(expected.tangents, actual.tangents);
            Assert.AreEqual(expected.blendShapeCount, actual.blendShapeCount);
            for (var shape = 0; shape < expected.blendShapeCount; shape++)
            {
                Assert.AreEqual(expected.GetBlendShapeFrameCount(shape), actual.GetBlendShapeFrameCount(shape));
                for (var frame = 0; frame < expected.GetBlendShapeFrameCount(shape); frame++)
                    for (var part = 0; part < 3; part++)
                    {
                        var a = Frame(expected, expected.GetBlendShapeName(shape), part, frame);
                        var b = Frame(actual, actual.GetBlendShapeName(shape), part, frame);
                        // Bitwise, not Vector3 ==, so byte-identical output is checked.
                        Assert.AreEqual(Bits(a), Bits(b), expected.GetBlendShapeName(shape));
                    }
            }
        }

        // VRChat shows one viseme at a time: while ou plays, sil is 0. The base
        // keeps sil, so ou subtracts it and ends at the original base + ou.
        [Test] public void PreserveRebasesLipSyncVisemesWithoutTheOtherVisemesRest()
        {
            var mesh = VisemeMesh();
            var temporary = new List<Mesh>();
            try
            {
                // sil named twice and a slot whose shape the mesh lacks: sil is subtracted once.
                var baked = PreserveFace(mesh, new[] { 100f, 0f, 50f }, temporary,
                    skin => Visemes(skin, "vrc.v_sil", "vrc.v_ou", "vrc.v_sil", "vrc.v_missing")).Baked;
                Assert.AreNotSame(mesh, baked);
                Assert.AreEqual(new Vector3(3, 1, 0), baked.vertices[0]);
                Assert.AreEqual(new Vector3(3, 0, 2), baked.vertices[0] + Frame(baked, "vrc.v_ou", 0), "ou ends at the original base + ou, with the smile rest kept.");
                Assert.AreEqual(mesh.normals[0], baked.normals[0] + Frame(baked, "vrc.v_ou", 1), "ou's normals drop sil's rest.");
                Assert.AreEqual(new Vector3(0, -1, 0), Frame(baked, "vrc.v_ou", 2), "ou's tangents drop sil's rest.");
                Assert.AreEqual(Vector3.zero, Frame(baked, "vrc.v_sil", 0), "sil itself rests at its endpoint.");
                Assert.AreEqual(new Vector3(2, 0, 0), Frame(baked, "Smile", 0), "A shape outside Lip Sync only drops its own rest.");
                Assert.AreEqual(new Vector3(0, 0, 2), Frame(mesh, "vrc.v_ou", 0), "The source mesh is unchanged.");
            }
            finally
            {
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }

        // Without Lip Sync on the renderer, or with every viseme resting at 0,
        // the output is the same as before visemes were rebased.
        [TestCase("none")]
        [TestCase("otherRenderer")]
        [TestCase("visemesAtZero")]
        public void PreserveKeepsTheOutputWithoutRestingVisemes(string kind)
        {
            var mesh = VisemeMesh();
            var other = new GameObject("Other");
            var temporary = new List<Mesh>();
            try
            {
                var weights = kind == "visemesAtZero" ? new[] { 0f, 0f, 50f } : new[] { 100f, 0f, 50f };
                var expected = PreserveFace(mesh, weights, temporary, _ => default).Baked;
                var actual = PreserveFace(mesh, weights, temporary, skin => kind == "none" ? default :
                    Visemes(kind == "otherRenderer" ? other.AddComponent<SkinnedMeshRenderer>() : skin, "vrc.v_sil", "vrc.v_ou")).Baked;
                AssertSameMesh(expected, actual);
                if (kind != "visemesAtZero")
                    Assert.AreEqual(Frame(mesh, "vrc.v_ou", 0), Frame(actual, "vrc.v_ou", 0), "ou keeps its source delta.");
            }
            finally
            {
                Object.DestroyImmediate(other);
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }

        // A viseme whose frames the rest cannot be evaluated from is written as
        // authored and its value is not stored in the base, so the other
        // visemes have nothing of it to subtract.
        [Test] public void PreserveDoesNotSubtractAVisemeKeptAsAuthored()
        {
            var mesh = VisemeMesh();
            var temporary = new List<Mesh>();
            try
            {
                var empty = new Vector3[3];
                mesh.AddBlendShapeFrame("vrc.v_kk", -50, new[] { new Vector3(-1, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                mesh.AddBlendShapeFrame("vrc.v_kk", 0, new[] { new Vector3(1, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                mesh.AddBlendShapeFrame("vrc.v_kk", 100, new[] { new Vector3(2, 0, 0), Vector3.zero, Vector3.zero }, empty, empty);
                var (baked, warnings) = PreserveFace(mesh, new[] { 0f, 0f, 0f, 25f }, temporary, skin => Visemes(skin, "vrc.v_sil", "vrc.v_ou", "vrc.v_kk"));
                Assert.AreEqual(mesh.vertices[0], baked.vertices[0]);
                Assert.AreEqual(Frame(mesh, "vrc.v_ou", 0), Frame(baked, "vrc.v_ou", 0));
                Assert.AreEqual(3, baked.GetBlendShapeFrameCount(baked.GetBlendShapeIndex("vrc.v_kk")));
                Assert.That(warnings.Exists(warning => warning.Contains("vrc.v_kk")), string.Join("\n", warnings));
            }
            finally
            {
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
