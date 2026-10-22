using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class OutlineRendererScaleTests
    {
        private readonly List<Object> cleanup = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in cleanup) if (item != null) Object.DestroyImmediate(item);
            cleanup.Clear();
        }

        [Test]
        public void ScaledMeshRendererWidensOnlyTheMToonOutline()
        {
            var root = New("Avatar");
            var source = Material(0.14f);
            var renderer = Static(root, "Part", new Vector3(2f, 2f, 2f), source);
            var scales = OutlineRendererScale.Measure(root);
            Assert.AreEqual(2f, scales[source], 1e-5f);
            // The VRM keeps the node scale, so the lilToon record keeps the scene width.
            Assert.AreEqual(0.14f, source.GetFloat("_OutlineWidth"), 1e-6f, "The material must not change.");
            var record = LilToonMaterialReader.Read(source, 0, (_, __) => 0);
            Assert.AreEqual(0.14f, record.floats.Single(item => item.name == "_OutlineWidth").value, 1e-6f);
            // MToon's width is metres along a normalized normal, so it takes the scale.
            var created = new List<Material>();
            UniVrmOneClickExporter.ReplaceLilToonMaterials(root, created, new List<Texture2D>(), new List<string>(), false, outlineScales: scales);
            cleanup.AddRange(created);
            Assert.AreNotSame(source, renderer.sharedMaterial);
            Assert.AreEqual(source.name, renderer.sharedMaterial.name);
            Assert.AreEqual(0.0028f, renderer.sharedMaterial.GetFloat("_OutlineWidth"), 1e-6f);
        }

        [Test]
        public void SkinnedOutlineFollowsTheSkinMatrixScale()
        {
            var root = New("Avatar");
            var bone = New("Bone", root.transform);
            var holder = New("Body", root.transform);
            var mesh = Triangle(3);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 3).ToArray();
            mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * holder.transform.localToWorldMatrix };
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone.transform };
            var source = Material(0.1f);
            skin.sharedMaterial = source;
            // The renderer itself is unscaled; only the bone it follows grows.
            bone.transform.localScale = new Vector3(3f, 3f, 3f);
            Assert.AreEqual(3f, OutlineRendererScale.Scale(skin), 1e-4f);
            Assert.AreEqual(3f, OutlineRendererScale.Measure(root)[source], 1e-4f);
        }

        [Test]
        public void UnitScaleAndMaterialsWithoutAnOutlineAreNotMeasured()
        {
            var root = New("Avatar");
            var unit = Material(0.14f);
            Static(root, "Unit", Vector3.one, unit);
            var noOutline = Material(0.14f);
            noOutline.SetFloat("_UseOutline", 0f);
            Static(root, "Plain", new Vector3(4f, 4f, 4f), noOutline);
            var scales = OutlineRendererScale.Measure(root);
            CollectionAssert.IsEmpty(scales);
            // Without a measured scale the approximation keeps the plain width.
            var created = new List<Material>();
            var fallback = UniVrmOneClickExporter.CreateMToonFallback(unit, created, new List<string>());
            cleanup.AddRange(created);
            Assert.AreEqual(0.0014f, fallback.GetFloat("_OutlineWidth"), 1e-6f);
        }

        [Test]
        public void ASharedMaterialAtDifferentScalesTakesTheLargestMeshAndWarns()
        {
            var root = New("Avatar");
            var shared = Material(0.1f);
            Static(root, "Small", new Vector3(2f, 2f, 2f), shared);
            Static(root, "Large", new Vector3(4f, 4f, 4f), shared, 6);
            Static(root, "Same", new Vector3(2f, 2f, 2f), shared);
            var warnings = new List<string>();
            var scales = OutlineRendererScale.Measure(root, warnings);
            Assert.AreEqual(4f, scales[shared], 1e-5f);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(shared.name, warnings[0]);
            StringAssert.Contains("Large", warnings[0]);
        }

        private GameObject New(string name, Transform parent = null)
        {
            var item = new GameObject(name);
            if (parent != null) item.transform.SetParent(parent, false);
            else cleanup.Add(item);
            return item;
        }

        private Material Material(float width)
        {
            var material = new Material(Shader.Find("Hidden/VRVlogTests/lilToon")) { name = "Outlined" + cleanup.Count };
            material.SetFloat("_OutlineWidth", width);
            cleanup.Add(material);
            return material;
        }

        private Mesh Triangle(int vertices)
        {
            var mesh = new Mesh
            {
                vertices = Enumerable.Range(0, vertices).Select(i => new Vector3(i % 2, i / 2, 0f)).ToArray(),
                triangles = new[] { 0, 1, 2 },
            };
            cleanup.Add(mesh);
            return mesh;
        }

        private MeshRenderer Static(GameObject root, string name, Vector3 scale, Material material, int vertices = 3)
        {
            var part = New(name, root.transform);
            part.transform.localScale = scale;
            part.AddComponent<MeshFilter>().sharedMesh = Triangle(vertices);
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
