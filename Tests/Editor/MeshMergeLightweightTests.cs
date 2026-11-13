using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using F = VRVlog.LilToon.LilToonFullContract;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // 同じマテリアルのメッシュをまとめる and 細い輪郭線を省く on synthetic avatars.
    public sealed class MeshMergeLightweightTests
    {
        readonly List<Object> cleanup = new List<Object>();
        GameObject root;
        Transform hips, chest;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Avatar");
            cleanup.Add(root);
            hips = Child("Hips", root.transform, new Vector3(0f, 1f, 0f));
            chest = Child("Chest", hips, new Vector3(0f, .3f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var item in cleanup) if (item != null) Object.DestroyImmediate(item);
            cleanup.Clear();
        }

        [Test]
        public void PartsWithOneMaterialBecomeOneDrawAndKeepTheirPlace()
        {
            var shared = Lil("lilToon");
            var other = Lil("lilToon");
            var body = Skin("Body", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            body.SetBlendShapeWeight(0, 40f);
            // Another mesh space: the donor's bindposes differ by a rigid move.
            var cloth = Skin("Cloth", new Vector3(.2f, .1f, 0f), Quaternion.Euler(0f, 30f, 0f), new[] { shared, other }, blendShape: false);
            var expected = World(body).Concat(World(cloth)).ToList();
            var owned = new List<Mesh>();
            var result = MeshMergeLightweight.Apply(root, owned, null);
            cleanup.AddRange(owned);
            Assert.That(result, Is.Not.Null);
            Assert.That(cloth.enabled, Is.False);
            Assert.That(body.sharedMesh, Is.SameAs(owned.Single()));
            Assert.That(body.sharedMaterials, Is.EqualTo(new[] { shared, other }), "The host's submeshes come first; a new material follows.");
            Assert.That(body.sharedMesh.blendShapeCount, Is.EqualTo(1));
            Assert.That(body.GetBlendShapeWeight(0), Is.EqualTo(40f));
            Assert.That(body.bones.Distinct().Count(), Is.EqualTo(body.bones.Length), "glTF joints must be unique.");
            var actual = World(body);
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < actual.Count; i++) Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(1e-4f), i.ToString());
            Assert.That((result.RenderersBefore, result.RenderersAfter, result.DrawsBefore - result.DrawsAfter), Is.EqualTo((2, 1, 1)));
            Assert.That(result.ResultLine(), Is.EqualTo("メッシュ2個を1個にまとめて描画の回数を1回減らしました"));
        }

        [Test]
        public void DonorBonesInAnotherOrderOrMissingFromTheHostKeepTheirTriangles()
        {
            var shared = Lil("lilToon");
            var head = Child("Head", chest, new Vector3(0f, .2f, 0f));
            var body = Skin("Body", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            // Weighted to chest and head; head is not one of the body's bones.
            var hat = Skin("Hat", new Vector3(0f, .1f, 0f), Quaternion.identity, new[] { shared }, blendShape: false, bones: new[] { chest, head, hips });
            var expected = Triangles(body).Concat(Triangles(hat)).ToList();
            var owned = new List<Mesh>();
            Assert.That(MeshMergeLightweight.Apply(root, owned, null), Is.Not.Null);
            cleanup.AddRange(owned);
            Assert.That(hat.enabled, Is.False);
            Assert.That(body.bones, Does.Contain(head));
            var actual = Triangles(body);
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (var i = 0; i < actual.Count; i++) Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(1e-4f), i.ToString());
            // The host's shape moves only the host's vertices.
            var mesh = body.sharedMesh;
            var deltas = new Vector3[mesh.vertexCount];
            mesh.GetBlendShapeFrameVertices(0, 0, deltas, new Vector3[mesh.vertexCount], new Vector3[mesh.vertexCount]);
            Assert.That(deltas.Take(4).All(delta => delta != Vector3.zero) && deltas.Skip(4).All(delta => delta == Vector3.zero), Is.True);
        }

        [Test]
        public void PartsThatDrawDifferentlyOrMoveOnTheirOwnStay()
        {
            var shared = Lil("lilToon");
            Skin("Body", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            var shaped = Skin("ShapedCloth", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            var seeThrough = Skin("SeeThrough", Vector3.zero, Quaternion.identity, new[] { Lil("Hidden/lilToonTransparent") }, blendShape: false);
            var toggled = Skin("Toggled", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: false);
            var shadowless = Skin("Shadowless", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: false);
            shadowless.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var owned = new List<Mesh>();
            Assert.That(MeshMergeLightweight.Apply(root, owned, new[] { toggled.transform }), Is.Null);
            Assert.That(owned, Is.Empty);
            Assert.That(new[] { shaped, seeThrough, toggled, shadowless }.All(renderer => renderer.enabled), Is.True);
        }

        [Test]
        public void PartsShownAndHiddenTogetherMergeOnlyWithEachOther()
        {
            var shared = Lil("lilToon");
            var body = Skin("Body", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            var outfit = new GameObject("Outfit").transform;
            outfit.SetParent(root.transform, false);
            var shirt = Skin("Shirt", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: true);
            var skirt = Skin("Skirt", Vector3.zero, Quaternion.identity, new[] { shared }, blendShape: false);
            shirt.transform.SetParent(outfit, true);
            skirt.transform.SetParent(outfit, true);
            var owned = new List<Mesh>();
            var result = MeshMergeLightweight.Apply(root, owned, new[] { outfit });
            cleanup.AddRange(owned);
            Assert.That(result, Is.Not.Null);
            Assert.That(skirt.enabled, Is.False);
            Assert.That(shirt.sharedMesh, Is.SameAs(owned.Single()), "The skirt joins the shirt under the same toggle.");
            Assert.That(body.sharedMesh.name, Is.EqualTo("Body"), "The body is not switched with the outfit.");
        }

        [Test]
        public void OnlyOutlinesThinnerThanTheThresholdInTheSceneAreOmitted()
        {
            var thin = Lil("Hidden/lilToonOutline", .04f);
            var wide = Lil("Hidden/lilToonOutline", .06f);
            // _OutlineFixWidth only narrows the line near the camera.
            var narrowedNear = Lil("Hidden/lilToonOutline", .01f);
            narrowedNear.SetFloat("_OutlineFixWidth", 1f);
            var renderers = new[] { thin, wide, narrowedNear }.Select((material, i) => (Renderer)Skin("Part" + i, Vector3.zero, Quaternion.identity, new[] { material }, false)).ToList();
            Assert.That(OutlineLightweight.Omitted(renderers, .5f), Is.EquivalentTo(new[] { thin, narrowedNear }));
            Assert.That(OutlineLightweight.Omitted(renderers, 0f), Is.Empty);
            // The same 0.4 mm drawn twice as large is 0.8 mm.
            hips.localScale = Vector3.one * 2f;
            chest.localScale = Vector3.one;
            Assert.That(OutlineLightweight.Omitted(renderers, .5f), Is.EquivalentTo(new[] { narrowedNear }));
            Assert.That(OutlineLightweight.ValidThreshold(float.NaN), Is.EqualTo(0f));
            Assert.That(OutlineLightweight.ValidThreshold(-1f), Is.EqualTo(0f));
        }

        [Test]
        public void TheSnapshotSwitchesOffOnlyTheOutlinePasses()
        {
            NoGraphicsDevice.AllowErrorLogs();
            var material = Lil("Hidden/lilToonOutline", .04f);
            string[] Off(ISet<Material> omitted)
            {
                var snapshot = new LilToonFullSnapshot();
                typeof(LilToonFullSnapshot).GetField("outlineOmitted", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(snapshot, omitted);
                var record = (Dictionary<string, object>)typeof(LilToonFullSnapshot).GetMethod("ReadMaterial", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(snapshot, new object[] { material });
                return F.List(record, "passes").Select(F.Object).Where(pass => !(bool)pass["enabled"]).Select(pass => F.Text(pass, "name")).ToArray();
            }
            var before = Off(null);
            Assert.That(before, Does.Not.Contain("FORWARD_OUTLINE"));
            Assert.That(Off(new HashSet<Material> { material }).Except(before), Is.EquivalentTo(new[] { "FORWARD_OUTLINE", "FORWARD_ADD_OUTLINE" }));
        }

        [Test]
        public void TheTextFollowsTheWordingRulesAndExistsInEveryLocale()
        {
            var texts = new[]
            {
                ExportNoteText.MeshesMerged, ExportNoteText.ThinOutlinesOmitted,
                ExperimentalExpressionCaptureWindow.MergeMeshesLabel, ExperimentalExpressionCaptureWindow.MergeMeshesNote,
                ExperimentalExpressionCaptureWindow.ThinOutlinesLabel, ExperimentalExpressionCaptureWindow.ThinOutlinesNote,
            };
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var text in texts)
            {
                Assert.That(System.Text.RegularExpressions.Regex.IsMatch(text, "[。、！!\n]|ください|できます|\\d \\S"), Is.False, text);
                foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
                    Assert.That(File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json")),
                        Does.Contain("{\"source\":\"" + text + "\",\"translated\":\""), locale + ": " + text);
            }
            Assert.That(OutlineLightweight.ResultLine(0, .5f), Is.Null);
            Assert.That(OutlineLightweight.ResultLine(2, .5f), Is.EqualTo("幅0.5mm未満の輪郭線をマテリアル2個で省きました"));
        }

        Transform Child(string name, Transform parent, Vector3 position)
        {
            var item = new GameObject(name).transform;
            item.SetParent(parent, false);
            item.localPosition = position;
            return item;
        }

        [Test]
        public void StencilInAnyPassAndPositionEffectsKeepAMaterialOut()
        {
            Assert.That(MeshMergeLightweight.MergeableMaterial(Lil("Hidden/lilToonOutline")), Is.True);
            foreach (var (property, value) in new[] { ("_StencilZFail", 2f), ("_StencilFail", 2f), ("_OutlineStencilPass", 2f), ("_OutlineStencilComp", 3f), ("_OutlineZTest", 8f), ("_OutlineZWrite", 0f), ("_DistanceFadeMode", 1f) })
            {
                var material = Lil("Hidden/lilToonOutline");
                Assert.That(material.HasProperty(property), Is.True, property);
                material.SetFloat(property, value);
                Assert.That(MeshMergeLightweight.MergeableMaterial(material), Is.False, property);
            }
            var dissolve = Lil("Hidden/lilToonOutline");
            dissolve.SetVector("_DissolveParams", new Vector4(3f, 1f, .5f, .1f));
            Assert.That(MeshMergeLightweight.MergeableMaterial(dissolve), Is.False, "position dissolve");
            var secondLayer = Lil("Hidden/lilToonOutline");
            secondLayer.SetVector("_Main2ndDissolveParams", new Vector4(3f, 1f, .5f, .1f));
            Assert.That(MeshMergeLightweight.MergeableMaterial(secondLayer), Is.False, "position dissolve of the 2nd layer");
            var audioLink = Lil("Hidden/lilToonOutline");
            audioLink.SetFloat("_UseAudioLink", 1f);
            Assert.That(MeshMergeLightweight.MergeableMaterial(audioLink), Is.False, "AudioLink");
        }

        Material Lil(string shader, float outline = 0f)
        {
            var material = new Material(Shader.Find(shader)) { name = "Material" + cleanup.Count };
            if (material.HasProperty("_OutlineWidth")) material.SetFloat("_OutlineWidth", outline);
            cleanup.Add(material);
            return material;
        }

        // A quad per material on hips and chest; the renderer's own object
        // sets its mesh space.
        SkinnedMeshRenderer Skin(string name, Vector3 position, Quaternion rotation, Material[] materials, bool blendShape, Transform[] bones = null)
        {
            bones = bones ?? new[] { hips, chest };
            var holder = Child(name, root.transform, position);
            holder.localRotation = rotation;
            var mesh = new Mesh { name = name };
            cleanup.Add(mesh);
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var triangles = new List<int[]>();
            for (var sub = 0; sub < materials.Length; sub++)
            {
                var start = vertices.Count;
                for (var corner = 0; corner < 4; corner++)
                {
                    vertices.Add(new Vector3(corner % 2 * .1f + sub * .3f, corner / 2 * .2f + 1f, .05f * sub));
                    weights.Add(new BoneWeight { boneIndex0 = 0, weight0 = .6f, boneIndex1 = 1, weight1 = .4f });
                }
                triangles.Add(new[] { start, start + 2, start + 1, start + 1, start + 2, start + 3 });
            }
            mesh.SetVertices(vertices);
            mesh.SetNormals(Enumerable.Repeat(Vector3.back, vertices.Count).ToList());
            mesh.SetUVs(0, vertices.Select(vertex => new Vector2(vertex.x, vertex.y)).ToList());
            mesh.subMeshCount = materials.Length;
            for (var sub = 0; sub < materials.Length; sub++) mesh.SetTriangles(triangles[sub], sub);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bones.Select(bone => bone.worldToLocalMatrix * holder.localToWorldMatrix).ToArray();
            if (blendShape) mesh.AddBlendShapeFrame("Shape", 100f, Enumerable.Repeat(Vector3.up * .01f, vertices.Count).ToArray(), null, null);
            var skin = holder.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = hips;
            skin.sharedMaterials = materials;
            return skin;
        }

        // The world corners of every triangle, submesh by submesh, as drawn.
        static List<Vector3> Triangles(SkinnedMeshRenderer skin)
        {
            var world = World(skin);
            var mesh = skin.sharedMesh;
            return Enumerable.Range(0, mesh.subMeshCount).SelectMany(sub => mesh.GetTriangles(sub)).Select(index => world[index]).ToList();
        }

        // Rest positions in the world, as the skin draws them without shapes.
        static List<Vector3> World(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices;
            var poses = mesh.bindposes;
            var bones = skin.bones;
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var result = new List<Vector3>();
            var at = 0;
            for (var i = 0; i < vertices.Length; i++)
            {
                var sum = Vector3.zero;
                for (var k = 0; k < perVertex[i]; k++, at++)
                {
                    var weight = weights[at];
                    sum += weight.weight * (bones[weight.boneIndex].localToWorldMatrix * poses[weight.boneIndex]).MultiplyPoint3x4(vertices[i]);
                }
                result.Add(sum);
            }
            return result;
        }
    }
}
