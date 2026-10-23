using System.Collections.Generic;
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
    }
}
