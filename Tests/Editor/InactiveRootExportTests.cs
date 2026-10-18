using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // A hidden avatar, or one under a hidden parent, still exports. Only the
    // export copy's root is shown; the scene object is never changed.
    public sealed class InactiveRootExportTests
    {
        readonly List<Object> owned = new List<Object>();

        [TearDown] public void TearDown()
        {
            for (var i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }

        // Adds an inactive outfit with its own mesh next to the visible parts.
        SkinnedMeshRenderer HiddenOutfit(AttachmentConnectionTests.Fixture fixture, Material material)
        {
            var go = new GameObject("Hidden outfit");
            go.transform.SetParent(fixture.Source.transform, false);
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var mesh = Own(Object.Instantiate(fixture.Mesh)); mesh.name = "Hidden outfit mesh";
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh; skin.sharedMaterial = material;
            skin.bones = front.bones; skin.rootBone = front.rootBone;
            go.SetActive(false);
            return skin;
        }

        static byte[] Export(GameObject source, ExportRecoveryReport report, ICollection<string> warnings) =>
            UniVrmOneClickExporter.Export(source, "Hidden root", "Tests", warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);

        static HashSet<string> NodesWithMesh(byte[] bytes) => new HashSet<string>(((List<object>)GlbDocument.Read(bytes).Json["nodes"])
            .Cast<Dictionary<string, object>>().Where(node => node.ContainsKey("mesh")).Select(node => node["name"] as string));

        void AssertExportsVisibleChildrenOnly(AttachmentConnectionTests.Fixture fixture, GameObject hiddenObject)
        {
            var material = Own(new Material(Shader.Find("lilToon")) { name = "Hidden root" });
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.sharedMaterial = material;
            HiddenOutfit(fixture, material);
            hiddenObject.SetActive(false);
            Assert.That(fixture.Source.activeInHierarchy, Is.False);

            var scene = fixture.Source.scene;
            var dirty = scene.isDirty;
            var roots = scene.rootCount;
            var sourceChanges = EditorUtility.GetDirtyCount(fixture.Source);
            var hiddenChanges = EditorUtility.GetDirtyCount(hiddenObject);
            var children = fixture.Source.GetComponentsInChildren<Transform>(true).Select(t => (t, t.gameObject.activeSelf)).ToArray();

            var report = new ExportRecoveryReport();
            var warnings = new List<string>();
            var bytes = Export(fixture.Source, report, warnings);

            Assert.That(bytes, Is.Not.Null.And.Not.Empty);
            var nodes = NodesWithMesh(bytes);
            Assert.That(nodes, Does.Contain("Front").And.Contain("Back"), "Shown children of a hidden avatar are exported.");
            Assert.That(nodes, Does.Not.Contain("Hidden outfit"), "Hidden children stay out, as on a shown avatar.");
            Assert.That(warnings, Does.Contain(ExporterLocalization.T(UniVrmOneClickExporter.HiddenRootWarning)));
            var entry = report.Fallbacks.Results.Single(result => result.Name == fixture.Source.name);
            Assert.That(entry.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(report.Succeeded, Is.True);

            // Non-destructive: the scene object keeps its activity and is not dirtied.
            Assert.That(hiddenObject.activeSelf, Is.False);
            Assert.That(fixture.Source.activeInHierarchy, Is.False);
            Assert.That(children.All(pair => pair.t != null && pair.t.gameObject.activeSelf == pair.activeSelf), Is.True);
            Assert.That(scene.isDirty, Is.EqualTo(dirty));
            Assert.That(scene.rootCount, Is.EqualTo(roots));
            Assert.That(EditorUtility.GetDirtyCount(fixture.Source), Is.EqualTo(sourceChanges));
            Assert.That(EditorUtility.GetDirtyCount(hiddenObject), Is.EqualTo(hiddenChanges));
        }

        [Test] public void AHiddenAvatarExportsItsShownChildren()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            AssertExportsVisibleChildrenOnly(fixture, fixture.Source);
        }

        [Test] public void AnAvatarUnderAHiddenParentExports()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var parent = Own(new GameObject("Hidden parent"));
            fixture.Source.transform.SetParent(parent.transform, false);
            AssertExportsVisibleChildrenOnly(fixture, parent);
            Assert.That(fixture.Source.activeSelf, Is.True);
            Assert.That(fixture.Source.transform.parent, Is.SameAs(parent.transform));
        }

        [Test] public void AShownAvatarRecordsNoHiddenRoot()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = Own(new Material(Shader.Find("lilToon")) { name = "Shown root" });
            foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skin.sharedMaterial = material;
            HiddenOutfit(fixture, material);
            var report = new ExportRecoveryReport();
            var warnings = new List<string>();
            var nodes = NodesWithMesh(Export(fixture.Source, report, warnings));
            Assert.That(nodes, Does.Contain("Front").And.Not.Contain("Hidden outfit"));
            Assert.That(warnings, Does.Not.Contain(ExporterLocalization.T(UniVrmOneClickExporter.HiddenRootWarning)));
            Assert.That(report.Fallbacks.Results.Any(result => result.Name == fixture.Source.name), Is.False);
        }

        [Test] public void ActivityIsJudgedInsideTheAvatar()
        {
            var parent = Own(new GameObject("parent"));
            var avatar = new GameObject("avatar"); avatar.transform.SetParent(parent.transform, false);
            var wardrobe = new GameObject("wardrobe"); wardrobe.transform.SetParent(avatar.transform, false);
            var shown = new GameObject("shown"); shown.transform.SetParent(avatar.transform, false);
            var outfit = new GameObject("outfit"); outfit.transform.SetParent(wardrobe.transform, false);
            var shownRenderer = shown.AddComponent<MeshRenderer>();
            outfit.AddComponent<MeshRenderer>();
            wardrobe.SetActive(false);

            foreach (var hide in new[] { (GameObject)null, avatar, parent })
            {
                if (hide != null) hide.SetActive(false);
                Assert.That(ExportRendererSelection.ActiveUnder(shown, avatar.transform), Is.True);
                Assert.That(ExportRendererSelection.ActiveUnder(outfit, avatar.transform), Is.False);
                Assert.That(ExportRendererSelection.ActiveUnder(avatar, avatar.transform), Is.True);
                Assert.That(ExportRendererSelection.Enumerate(avatar), Is.EqualTo(new Renderer[] { shownRenderer }));
                Assert.That(ExportRendererSelection.HiddenInScene(avatar), Is.EqualTo(hide != null));
                if (hide != null) hide.SetActive(true);
            }
            // Without a root, or outside it, the scene's activity applies.
            parent.SetActive(false);
            Assert.That(ExportRendererSelection.ActiveUnder(shown, null), Is.False);
            Assert.That(ExportRendererSelection.ActiveUnder(shown, outfit.transform), Is.False);
        }

        [Test] public void BlinkIsFoundUnderAHiddenAvatar()
        {
            var parent = Own(new GameObject("parent"));
            var root = new GameObject("avatar"); root.transform.SetParent(parent.transform, false);
            SkinnedMeshRenderer Skin(string name, params string[] shapes)
            {
                var go = new GameObject(name); go.transform.SetParent(root.transform, false);
                var mesh = Own(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
                foreach (var shape in shapes)
                    mesh.AddBlendShapeFrame(shape, 100, new[] { Vector3.up * .2f, Vector3.zero, Vector3.zero }, new Vector3[3], new Vector3[3]);
                var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.sharedMesh = mesh;
                return skin;
            }
            var face = Skin("face", "Blink");
            var spare = Skin("spare face", "Blink");
            spare.gameObject.SetActive(false);
            foreach (var hide in new[] { root, parent })
            {
                hide.SetActive(false);
                var resolved = BlinkExportSession.Resolve(root);
                Assert.That(resolved.Disabled, Is.False);
                Assert.That(resolved.Slots[0].Single().Renderer, Is.SameAs(face), "The hidden spare face stays out.");
                Assert.DoesNotThrow(() => resolved.Validate(root));
                hide.SetActive(true);
            }
        }

        [Test] public void GimmicksAreFoundUnderAHiddenAvatar()
        {
            var parent = Own(new GameObject("parent"));
            var avatar = new GameObject("avatar"); avatar.transform.SetParent(parent.transform, false);
            var hiddenFallback = Own(new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback")));
            MeshRenderer Render(string name)
            {
                var go = new GameObject(name); go.transform.SetParent(avatar.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = Own(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
                var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = hiddenFallback; return renderer;
            }
            var effect = Render("effect");
            Render("hidden effect").gameObject.SetActive(false);
            foreach (var hide in new[] { avatar, parent })
            {
                hide.SetActive(false);
                var findings = ExportGimmickDetection.Analyze(avatar);
                Assert.That(findings.Select(finding => finding.Renderer), Is.EqualTo(new Renderer[] { effect }));
                Assert.That(findings.Single().Unit, Is.EqualTo(GimmickExclusionUnit.Renderer));
                hide.SetActive(true);
            }
        }
    }
}
