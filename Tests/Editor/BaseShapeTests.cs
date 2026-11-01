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

        // Shape indices of BaseShapeFixture.VisemeMesh: 0 sil, 1 aa, 2 ou,
        // 3 E (normals and tangents only), 4 Smile (outside Lip Sync).
        // defaultFace names the shapes the FX default state sets on the face.
        static (Mesh Baked, List<string> Warnings) PreserveFace(Mesh mesh, float[] weights, ICollection<Mesh> temporary,
            System.Func<SkinnedMeshRenderer, (SkinnedMeshRenderer Renderer, string[] Shapes)> visemes, params string[] defaultFace)
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
                AvatarBaseShape.Preserve(avatar, avatar, temporary, warnings, visemeRenderer: renderer, visemeShapes: shapes,
                    defaultFace: defaultFace.Select(shape => (skin, shape)).ToList());
                return (skin.sharedMesh, warnings);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        static (SkinnedMeshRenderer, string[]) Visemes(SkinnedMeshRenderer renderer, params string[] shapes) => (renderer, shapes);

        static Vector3[] Frame(Mesh mesh, string shape, int part)
        {
            var v = new Vector3[mesh.vertexCount]; var n = new Vector3[mesh.vertexCount]; var t = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(mesh.GetBlendShapeIndex(shape), 0, v, n, t);
            return part == 0 ? v : part == 1 ? n : t;
        }

        static (int, int, int) Bits(Vector3 value) => (System.BitConverter.SingleToInt32Bits(value.x),
            System.BitConverter.SingleToInt32Bits(value.y), System.BitConverter.SingleToInt32Bits(value.z));

        static void AssertSameMesh(Mesh expected, Mesh actual) => Assert.IsNull(BaseShapeFixture.SameMesh(expected, actual));

        static void AssertAsVrChatShows(Mesh source, float[] weights, Mesh baked, string[] group, params string[] kept) =>
            Assert.IsNull(BaseShapeFixture.RebasedAsVrChatShows(source, weights, group, kept, baked));

        static void AssertAsVrChatShowsWithoutMouth(Mesh source, float[] weights, Mesh baked, string[] group, string mouth) =>
            Assert.IsNull(BaseShapeFixture.RebasedAsVrChatShows(source, weights, group, new string[0], baked, new[] { mouth }));

        static void AssertNear(Vector3 expected, Vector3 actual, string message) =>
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(0.0001f), $"{message} expected {expected}, got {actual}");

        static void WithVisemeMesh(System.Action<Mesh, List<Mesh>> body) => WithVisemeMesh(BaseShapeFixture.VisemeGroup, body);

        static void WithVisemeMesh(string[] group, System.Action<Mesh, List<Mesh>> body)
        {
            var mesh = BaseShapeFixture.VisemeMesh(group);
            var reference = BaseShapeFixture.VisemeMesh(group);
            var temporary = new List<Mesh>();
            try
            {
                body(mesh, temporary);
                if (mesh.blendShapeCount == reference.blendShapeCount) AssertSameMesh(reference, mesh); // the source mesh is unchanged
            }
            finally
            {
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh); Object.DestroyImmediate(reference);
            }
        }

        // The export before Lip Sync was rebased: each shape only drops its own rest.
        static Mesh RebasedAlone(Mesh mesh, float[] weights, ICollection<Mesh> temporary)
        {
            var result = Object.Instantiate(mesh);
            temporary.Add(result);
            AvatarBaseShape.Rebase(mesh, result, weights);
            return result;
        }

        // VRChat shows one viseme at a time: while ou plays, sil is 0. The base
        // keeps sil, so ou subtracts it and ends at the original base + ou. The
        // descriptor's slots make shapes Lip Sync whatever their names.
        [Test] public void PreserveRebasesLipSyncVisemesWithoutTheOtherVisemesRest() => WithVisemeMesh(BaseShapeFixture.MouthGroup, (mesh, temporary) =>
        {
            var weights = new[] { 100f, 0f, 0f, 0f, 50f };
            // sil named twice and a slot whose shape the mesh lacks: sil is subtracted once.
            var baked = PreserveFace(mesh, weights, temporary,
                skin => Visemes(skin, "mouth_sil", "mouth_aa", "mouth_ou", "mouth_E", "mouth_sil", "mouth_missing")).Baked;
            Assert.AreNotSame(mesh, baked);
            AssertNear(new Vector3(3, 1, 0), baked.vertices[0], "The base keeps sil and half the smile.");
            AssertNear(new Vector3(3, 0, 2), baked.vertices[0] + Frame(baked, "mouth_ou", 0)[0], "ou ends at the original base + ou, with the smile rest kept.");
            AssertAsVrChatShows(mesh, weights, baked, BaseShapeFixture.MouthGroup);
        });

        // Avatars whose own animator drives the vrc.v_* shapes (the descriptor
        // only passes the viseme parameter) have no slots. The names alone make
        // them Lip Sync.
        [Test] public void PreserveRebasesShapesNamedAsVisemesWithoutDescriptorSlots() => WithVisemeMesh((mesh, temporary) =>
        {
            var weights = new[] { 100f, 0f, 0f, 0f, 50f };
            var baked = PreserveFace(mesh, weights, temporary, _ => default).Baked;
            AssertNear(new Vector3(3, 0, 2), baked.vertices[0] + Frame(baked, "vrc.v_ou", 0)[0], "ou ends at the original base + ou, with the smile rest kept.");
            AssertAsVrChatShows(mesh, weights, baked, BaseShapeFixture.VisemeGroup);
        });

        // Each viseme leaves out every other resting viseme, and a resting
        // viseme also leaves out its own rest. The FX default state holds the
        // smile, which moves the mouth, so a viseme leaves it out too.
        [Test] public void PreserveRebasesTwoRestingVisemesAtOnce() => WithVisemeMesh((mesh, temporary) =>
        {
            var weights = new[] { 100f, 30f, 0f, 0f, 50f };
            var baked = PreserveFace(mesh, weights, temporary, _ => default, "Smile").Baked;
            AssertAsVrChatShowsWithoutMouth(mesh, weights, baked, BaseShapeFixture.VisemeGroup, "Smile");
            // aa at vertex 0: 0.7 of (0, -0.5, 0.25) minus sil's (0, 1, 0) and half the smile's (4, 0, 0).
            AssertNear(new Vector3(-2, -1.35f, .175f), Frame(baked, "vrc.v_aa", 0)[0], "aa drops its own rest, sil's rest and the smile.");
            AssertNear(mesh.vertices[1] + Frame(mesh, "vrc.v_ou", 0)[1], baked.vertices[1] + Frame(baked, "vrc.v_ou", 0)[1],
                "ou ends at the original base + ou, without the default smile.");
            AssertNear(baked.vertices[1], mesh.vertices[1] + Frame(mesh, "vrc.v_sil", 0)[1] + Frame(mesh, "vrc.v_aa", 0)[1] * .3f + Frame(mesh, "Smile", 0)[1] * .5f,
                "The base still shows the default smile.");
        });

        // A smile set on the renderer and not by the FX default state is part
        // of the avatar's own face, so a viseme keeps it.
        [Test] public void PreserveKeepsARestTheFxDefaultStateDoesNotSet() => WithVisemeMesh((mesh, temporary) =>
        {
            var weights = new[] { 100f, 30f, 0f, 0f, 50f };
            var baked = PreserveFace(mesh, weights, temporary, _ => default).Baked;
            AssertAsVrChatShows(mesh, weights, baked, BaseShapeFixture.VisemeGroup);
            AssertNear(mesh.vertices[1] + Frame(mesh, "Smile", 0)[1] * .5f + Frame(mesh, "vrc.v_ou", 0)[1], baked.vertices[1] + Frame(baked, "vrc.v_ou", 0)[1],
                "ou ends at the original base + half the smile + ou.");
        });

        // A viseme that rests only in its normals and tangents is still left
        // out of the others, which keep their positions bit for bit.
        [Test] public void PreserveRebasesAVisemeThatRestsOnlyInNormalsAndTangents() => WithVisemeMesh((mesh, temporary) =>
        {
            var weights = new[] { 0f, 0f, 0f, 50f, 0f };
            var baked = PreserveFace(mesh, weights, temporary, _ => default).Baked;
            AssertAsVrChatShows(mesh, weights, baked, BaseShapeFixture.VisemeGroup);
            CollectionAssert.AreEqual(Frame(mesh, "vrc.v_ou", 0).Select(Bits), Frame(baked, "vrc.v_ou", 0).Select(Bits), "ou's positions are unchanged.");
            AssertNear(Frame(mesh, "vrc.v_ou", 1)[0] - Frame(mesh, "vrc.v_E", 1)[0] * .5f, Frame(baked, "vrc.v_ou", 1)[0], "ou's normals drop E's rest.");
            AssertNear(Frame(mesh, "vrc.v_ou", 2)[0] - Frame(mesh, "vrc.v_E", 2)[0] * .5f, Frame(baked, "vrc.v_ou", 2)[0], "ou's tangents drop E's rest.");
        });

        // Shapes that are neither named as visemes nor descriptor slots of this
        // renderer, and visemes that all rest at 0, export the same mesh as
        // before Lip Sync was rebased.
        [TestCase("notVisemes")]
        [TestCase("otherRenderer")]
        [TestCase("visemesAtZero")]
        public void PreserveKeepsTheOutputWithoutRestingVisemes(string kind) =>
            WithVisemeMesh(kind == "visemesAtZero" ? BaseShapeFixture.VisemeGroup : BaseShapeFixture.MouthGroup, (mesh, temporary) =>
        {
            var other = new GameObject("Other");
            try
            {
                var weights = kind == "visemesAtZero" ? new[] { 0f, 0f, 0f, 0f, 50f } : new[] { 100f, 30f, 0f, 50f, 50f };
                var actual = PreserveFace(mesh, weights, temporary, skin => kind == "otherRenderer"
                    ? Visemes(other.AddComponent<SkinnedMeshRenderer>(), BaseShapeFixture.MouthGroup)
                    : kind == "visemesAtZero" ? Visemes(skin, BaseShapeFixture.VisemeGroup) : default).Baked;
                AssertSameMesh(RebasedAlone(mesh, weights, temporary), actual);
                AssertAsVrChatShows(mesh, weights, actual, new string[0]);
            }
            finally { Object.DestroyImmediate(other); }
        });

        // A face whose vertices 0 and 1 are the mouth and 2 and 3 the eyes.
        // aa and ou move only the mouth. MouthSmile moves the mouth and a
        // little of the eyes, EyeHalf only the eyes. Cheek moves the mouth,
        // but the renderer sets it, not the FX default state. Blink moves the
        // whole face as much in the mouth as around the eyes.
        static Mesh MouthAndEyesMesh()
        {
            var mesh = new Mesh { name = "Mouth and eyes" };
            mesh.vertices = new[] { new Vector3(-1, 0, 0), new Vector3(1, 0, 0), new Vector3(-1, 2, 0), new Vector3(1, 2, 0) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.normals = Enumerable.Repeat(Vector3.back, 4).ToArray();
            mesh.tangents = Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4).ToArray();
            void Add(string name, params Vector3[] moves) => mesh.AddBlendShapeFrame(name, 100, moves, new Vector3[4], new Vector3[4]);
            Add("vrc.v_aa", new Vector3(0, -1, 0), new Vector3(0, -1, 0), Vector3.zero, Vector3.zero);
            Add("vrc.v_ou", new Vector3(.5f, 0, .5f), new Vector3(-.5f, 0, .5f), Vector3.zero, Vector3.zero);
            Add("MouthSmile", new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(0, .1f, 0), Vector3.zero);
            Add("EyeHalf", Vector3.zero, Vector3.zero, new Vector3(0, -.5f, 0), new Vector3(0, -.5f, 0));
            Add("Cheek", new Vector3(0, 0, -.25f), new Vector3(0, 0, -.25f), Vector3.zero, Vector3.zero);
            Add("Blink", new Vector3(0, -.25f, 0), new Vector3(0, -.25f, 0), new Vector3(0, -.25f, 0), new Vector3(0, -.25f, 0));
            return mesh;
        }

        static Vector3 Shown(Mesh mesh, int vertex, params (string Shape, float Weight)[] weights)
        {
            var result = mesh.vertices[vertex];
            foreach (var (shape, weight) in weights) result += Frame(mesh, shape, 0)[vertex] * weight;
            return result;
        }

        // Only the default shapes that mostly move the mouth are left out of a
        // vowel, and only on the mouth: the eyelids and the eye part of the
        // smile keep their default. The cancel follows the vowel's weight: aa
        // at w takes off w of the default smile.
        [Test] public void PreserveTakesOnlyTheDefaultMouthOffAVowel()
        {
            var mesh = MouthAndEyesMesh();
            var temporary = new List<Mesh>();
            try
            {
                var weights = new[] { 0f, 0f, 60f, 40f, 50f, 0f };
                var baked = PreserveFace(mesh, weights, temporary, _ => default, "MouthSmile", "EyeHalf", "vrc.v_aa").Baked;
                var rest = new[] { ("MouthSmile", .6f), ("EyeHalf", .4f), ("Cheek", .5f) };
                for (var v = 0; v < 4; v++)
                {
                    // Vertices 2 and 3 are not moved by any vowel and keep the smile.
                    var withoutMouth = new[] { ("EyeHalf", .4f), ("Cheek", .5f), ("MouthSmile", v < 2 ? 0f : .6f) };
                    AssertNear(Shown(mesh, v, rest), baked.vertices[v], $"The base keeps the default face at vertex {v}.");
                    AssertNear(Shown(mesh, v, withoutMouth.Append(("vrc.v_aa", 1f)).ToArray()), Shown(baked, v, ("vrc.v_aa", 1f)),
                        $"aa at 1 is aa without the default smile at vertex {v}.");
                    AssertNear(Shown(mesh, v, withoutMouth.Append(("vrc.v_aa", .8f)).Append(("MouthSmile", v < 2 ? .6f * .2f : 0f)).ToArray()),
                        Shown(baked, v, ("vrc.v_aa", .8f)), $"aa at .8 takes off .8 of the default smile at vertex {v}.");
                    // Two vowels whose weights add up to 1 take the smile off once.
                    AssertNear(Shown(mesh, v, withoutMouth.Append(("vrc.v_aa", .5f)).Append(("vrc.v_ou", .5f)).ToArray()),
                        Shown(baked, v, ("vrc.v_aa", .5f), ("vrc.v_ou", .5f)), $"aa and ou at .5 take the smile off once at vertex {v}.");
                    AssertNear(Shown(mesh, v, ("EyeHalf", .4f), ("Cheek", .5f), ("MouthSmile", 1f)), Shown(baked, v, ("MouthSmile", 1f)),
                        $"The smile at 1 still shows the smile once at vertex {v}.");
                }
                // Cheek would count as a mouth too if the FX default state set it.
                CollectionAssert.AreEquivalent(new[] { 2, 4 }, AvatarBaseShape.MouthRests(mesh, weights, new HashSet<int> { 0, 1 }, new[] { 0, 2, 3, 4, 5 }));
            }
            finally
            {
                foreach (var item in temporary) Object.DestroyImmediate(item);
                Object.DestroyImmediate(mesh);
            }
        }

        // The shapes blink closes the eyes with keep their default even when
        // they move the mouth as much as the eyes.
        [Test] public void DefaultFaceLeavesOutTheBlinkShapes()
        {
            var mesh = MouthAndEyesMesh();
            var avatar = new GameObject("Avatar");
            try
            {
                var face = new GameObject("Face");
                face.transform.SetParent(avatar.transform, false);
                var skin = face.AddComponent<SkinnedMeshRenderer>();
                skin.sharedMesh = mesh;
                var weights = new[] { 0f, 0f, 60f, 0f, 0f, 30f };
                for (var i = 0; i < weights.Length; i++) skin.SetBlendShapeWeight(i, weights[i]);
                var fxRest = new[] { "MouthSmile", "Blink", "Missing" }.Select(shape => new VrChatExpressionMenu.MorphValue { Path = "Face", Shape = shape })
                    .Append(new VrChatExpressionMenu.MorphValue { Path = "Nowhere", Shape = "MouthSmile" });
                var blink = new[] { new BlinkShapeBinding { Renderer = skin, Shape = "Blink" } };
                var defaultFace = NeutralShapeSnapshot.DefaultFace(NeutralShapeSnapshot.Capture(avatar), fxRest, blink);
                CollectionAssert.AreEquivalent(new[] { (skin, "MouthSmile"), (skin, "Missing") }, defaultFace);
                CollectionAssert.AreEquivalent(new[] { 5 }, AvatarBaseShape.MouthRests(mesh, weights, new HashSet<int> { 0, 1 }, new[] { 5 }),
                    "Without the blink binding the blink shape would count as a mouth.");
                CollectionAssert.AreEquivalent(new[] { 2 }, AvatarBaseShape.MouthRests(skin, mesh, weights, new HashSet<int> { 0, 1 }, defaultFace));
                Assert.That(NeutralShapeSnapshot.DefaultFace(null, fxRest, blink), Is.Empty);
                Assert.That(NeutralShapeSnapshot.DefaultFace(NeutralShapeSnapshot.Capture(avatar), null, blink), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(mesh);
            }
        }

        // A viseme whose frames the rest cannot be evaluated from is written as
        // authored and its value is not stored in the base, so the other
        // visemes have nothing of it to subtract, while sil still is.
        [Test] public void PreserveDoesNotSubtractAVisemeKeptAsAuthored() => WithVisemeMesh((mesh, temporary) =>
        {
            BaseShapeFixture.AddKeptViseme(mesh, "vrc.v_kk");
            var group = BaseShapeFixture.VisemeGroup.Concat(new[] { "vrc.v_kk" }).ToArray();
            var weights = new[] { 100f, 0f, 0f, 0f, 50f, 25f };
            var (baked, warnings) = PreserveFace(mesh, weights, temporary, _ => default);
            Assert.AreEqual(3, baked.GetBlendShapeFrameCount(baked.GetBlendShapeIndex("vrc.v_kk")));
            AssertAsVrChatShows(mesh, weights, baked, group, "vrc.v_kk");
            Assert.That(warnings.Exists(warning => warning.Contains("vrc.v_kk")), string.Join("\n", warnings));
        });
    }
}
