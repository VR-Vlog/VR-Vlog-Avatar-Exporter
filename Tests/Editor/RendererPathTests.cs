using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class RendererPathTests
    {
        GameObject root;
        Mesh mesh;

        [SetUp] public void SetUp()
        {
            root = new GameObject("avatar");
            mesh = new Mesh { name = "face" };
        }

        [TearDown] public void TearDown()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
        }

        GameObject Child(GameObject parent, string name, bool renderer = true, bool active = true)
        {
            var value = new GameObject(name);
            value.transform.SetParent(parent.transform, false);
            if (renderer) value.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;
            value.SetActive(active);
            return value;
        }

        // The full scan FindRenderer replaced; walking the names must give the same answer.
        SkinnedMeshRenderer FullScan(string path)
        {
            if (root.GetComponentsInChildren<Transform>(true).Count(t => AnimationUtility.CalculateTransformPath(t, root.transform) == path) != 1)
                throw new InvalidOperationException("重複する階層パスの表情は取り込めません: " + path);
            var matches = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => AnimationUtility.CalculateTransformPath(r.transform, root.transform) == path).ToArray();
            if (matches.Length != 1 || matches[0].sharedMesh == null)
                throw new InvalidOperationException("Rendererのパスを一意に解決できません: " + path);
            for (var current = matches[0].transform; current != root.transform; current = current.parent)
                if (current.name.Contains("/")) throw new InvalidOperationException("名前に / を含む階層の表情は取り込めません: " + path);
            return matches[0];
        }

        static string Outcome(Func<SkinnedMeshRenderer> find)
        {
            try { return "renderer " + find().GetInstanceID(); }
            catch (InvalidOperationException exception) { return exception.Message; }
        }

        [Test] public void WalkingNamesMatchesTheFullScan()
        {
            var body = Child(root, "Body");
            Child(body, "Face");
            Child(body, "Hidden", active: false);
            Child(body, "NoRenderer", renderer: false);
            Child(body, "NoMesh").GetComponent<SkinnedMeshRenderer>().sharedMesh = null;
            Child(root, "Twin");
            Child(root, "Twin");
            Child(root, "Body.001");
            Child(root, "Bo");
            // "Slash/Face" at the top and "Face" under "Slash" have the same path.
            Child(Child(root, "Slash"), "Face");
            Child(root, "Slash/Face");
            Child(root, "Only/Slash");
            Child(Child(root, "Deep", renderer: false), "Under");
            Child(root, "");
            root.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

            var paths = new List<string> { "", "Body", "Body/Face", "Body/Hidden", "Body/NoRenderer", "Body/NoMesh",
                "Body/Missing", "Twin", "Body.001", "Bo", "Slash", "Slash/Face", "Only/Slash", "Only", "Deep", "Deep/Under",
                "Body/", "/Body", "Body//Face", "body", null };
            foreach (var path in paths)
                Assert.That(Outcome(() => VrChatExpressionSampler.FindRenderer(root, path)), Is.EqualTo(Outcome(() => FullScan(path))), path ?? "(null)");
        }

        // Empty names below the root, where the path string alone may not tell the levels apart.
        [Test] public void EmptyNamesBelowTheRootMatchTheFullScan()
        {
            var body = Child(root, "Body");
            Child(Child(body, ""), "");
            Child(Child(Child(root, "Arm", renderer: false), "", renderer: false), "Hand");

            var paths = new List<string> { "Body", "Body/", "Body//", "Arm", "Arm/", "Arm/Hand", "Arm//Hand" };
            foreach (var path in paths)
                Assert.That(Outcome(() => VrChatExpressionSampler.FindRenderer(root, path)), Is.EqualTo(Outcome(() => FullScan(path))), path);
        }

        [Test] public void RootRendererIsFoundByTheEmptyPath()
        {
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            Child(root, "Body");
            Assert.That(VrChatExpressionSampler.FindRenderer(root, ""), Is.SameAs(renderer));
        }
    }
}
