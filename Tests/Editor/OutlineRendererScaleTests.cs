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
        public void ScaledMeshRendererOutlineIsWidenedOnAnOwnedCopyWithTheSameName()
        {
            var root = New("Avatar");
            var source = Material(0.14f);
            var renderer = Static(root, "Part", new Vector3(2f, 2f, 2f), source);
            var owned = new List<Material>();
            var observed = new List<(Material Copy, Material Original)>();
            OutlineRendererScale.Apply(root, owned, null, (copy, original) => observed.Add((copy, original)));
            var exported = renderer.sharedMaterial;
            cleanup.AddRange(owned);
            Assert.AreNotSame(source, exported);
            Assert.AreEqual(source.name, exported.name);
            Assert.AreEqual(0.28f, exported.GetFloat("_OutlineWidth"), 1e-5f);
            Assert.AreEqual(0.14f, source.GetFloat("_OutlineWidth"), 1e-6f, "The source material must not change.");
            CollectionAssert.AreEqual(new[] { exported }, owned);
            CollectionAssert.AreEqual(new[] { (exported, source) }, observed);
            // Both readers of the width see the scene-sized outline.
            var record = LilToonMaterialReader.Read(exported, 0, (_, __) => 0);
            Assert.AreEqual(0.28f, record.floats.Single(item => item.name == "_OutlineWidth").value, 1e-5f);
            var fallback = UniVrmOneClickExporter.CreateMToonFallback(exported, owned, new List<string>());
            cleanup.AddRange(owned);
            Assert.AreEqual(0.0028f, fallback.GetFloat("_OutlineWidth"), 1e-6f);
        }

        [Test]
        public void SkinnedOutlineFollowsTheSkinMatrixScale()
        {
            var root = New("Avatar");
            var bone = New("Bone", root.transform);
            var holder = New("Body", root.transform);
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            cleanup.Add(mesh);
            mesh.boneWeights = Enumerable.Repeat(new BoneWeight { boneIndex0 = 0, weight0 = 1f }, 3).ToArray();
            mesh.bindposes = new[] { bone.transform.worldToLocalMatrix * holder.transform.localToWorldMatrix };
            var skin = holder.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = new[] { bone.transform };
            var source = Material(0.1f);
            skin.sharedMaterial = source;
            // The renderer itself is unscaled; only the bone it follows grows.
            bone.transform.localScale = new Vector3(3f, 3f, 3f);
            var owned = new List<Material>();
            OutlineRendererScale.Apply(root, owned);
            cleanup.AddRange(owned);
            Assert.AreEqual(3f, OutlineRendererScale.Scale(skin), 1e-4f);
            Assert.AreEqual(0.3f, skin.sharedMaterial.GetFloat("_OutlineWidth"), 1e-5f);
        }

        [Test]
        public void UnitScaleKeepsTheMaterialAndOwnedCopiesChangeInPlace()
        {
            var root = New("Avatar");
            var unit = Material(0.14f);
            var unitRenderer = Static(root, "Unit", Vector3.one, unit);
            var already = Material(0.2f);
            var ownedRenderer = Static(root, "Owned", new Vector3(0.5f, 0.5f, 0.5f), already);
            var noOutline = Material(0.14f);
            noOutline.SetFloat("_UseOutline", 0f);
            var plainRenderer = Static(root, "Plain", new Vector3(4f, 4f, 4f), noOutline);
            var owned = new List<Material> { already };
            OutlineRendererScale.Apply(root, owned);
            Assert.AreSame(unit, unitRenderer.sharedMaterial);
            Assert.AreEqual(0.14f, unit.GetFloat("_OutlineWidth"), 1e-6f);
            Assert.AreSame(already, ownedRenderer.sharedMaterial, "A copy the export already owns needs no second copy.");
            Assert.AreEqual(0.1f, already.GetFloat("_OutlineWidth"), 1e-6f);
            Assert.AreSame(noOutline, plainRenderer.sharedMaterial, "A material without an outline is left alone.");
            CollectionAssert.AreEqual(new[] { already }, owned);
        }

        [Test]
        public void ASharedMaterialAtDifferentScalesTakesTheFirstScaleAndWarns()
        {
            var root = New("Avatar");
            var shared = Material(0.1f);
            var first = Static(root, "First", new Vector3(2f, 2f, 2f), shared);
            var second = Static(root, "Second", new Vector3(4f, 4f, 4f), shared);
            var same = Static(root, "Same", new Vector3(2f, 2f, 2f), shared);
            var owned = new List<Material>();
            var warnings = new List<string>();
            OutlineRendererScale.Apply(root, owned, warnings);
            cleanup.AddRange(owned);
            Assert.AreEqual(1, owned.Count);
            Assert.AreSame(owned[0], first.sharedMaterial);
            Assert.AreSame(owned[0], second.sharedMaterial);
            Assert.AreSame(owned[0], same.sharedMaterial);
            Assert.AreEqual(0.2f, owned[0].GetFloat("_OutlineWidth"), 1e-5f);
            Assert.AreEqual(1, warnings.Count);
            StringAssert.Contains(shared.name, warnings[0]);
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

        private MeshRenderer Static(GameObject root, string name, Vector3 scale, Material material)
        {
            var part = New(name, root.transform);
            part.transform.localScale = scale;
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            cleanup.Add(mesh);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }
    }
}
