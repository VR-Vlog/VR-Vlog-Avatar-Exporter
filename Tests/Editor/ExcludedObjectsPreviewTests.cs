using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The whole avatar beside the objects page follows every 除外 and 含める at
    // once, from a copy made once for the page, and leaves nothing behind.
    public sealed class ExcludedObjectsPreviewTests
    {
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        static readonly Type WindowType = typeof(ExperimentalExpressionCaptureWindow);

        static void Drain(ExperimentalExpressionCaptureWindow window)
        {
            for (var step = 0; step < 10 && WindowType.GetField("pending", Flags).GetValue(window) != null; step++)
                WindowType.GetMethod("Advance", Flags).Invoke(window, null);
            Assert.That(WindowType.GetField("pending", Flags).GetValue(window), Is.Null);
        }

        static int OwnedCopies(string name) => Resources.FindObjectsOfTypeAll<GameObject>()
            .Count(item => item != null && item.name == name && item.transform.parent == null && EditorSceneManager.IsPreviewScene(item.scene));

        [Test]
        public void ExcludingAndIncludingByHandShowsInThePreviewAtOnce()
        {
            var scenePath = "Assets/ExcludedObjectsPreview-" + Guid.NewGuid().ToString("N") + ".unity";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                WindowType.GetField("source", Flags).SetValue(window, fixture.Source);
                WindowType.GetMethod("Prepare", Flags).Invoke(window, null);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                window.GoTo(ExperimentalExpressionCaptureWindow.ObjectsPage); Drain(window);
                var session = WindowType.GetField("session", Flags).GetValue(window);
                var copiesBefore = OwnedCopies(fixture.Source.name);
                var undoBefore = Undo.GetCurrentGroup();

                window.RefreshObjectsPreview();
                var preview = window.ObjectsPreview;
                Assert.That(preview, Is.Not.Null);
                Assert.That(preview.Usable, Is.True);
                var copy = preview.Copy;
                Assert.That(EditorSceneManager.IsPreviewScene(copy.scene), Is.True);
                Assert.That(OwnedCopies(fixture.Source.name), Is.EqualTo(copiesBefore + 1));
                var front = fixture.Source.transform.Find("Front").gameObject;
                var back = fixture.Source.transform.Find("Back").gameObject;
                Assert.That(preview.Shows(front) && preview.Shows(back), Is.True);

                // 除外: gone from the preview, nothing rebuilt, the avatar as it was.
                WindowType.GetField("excludedObjectEdits", Flags).SetValue(window, new System.Collections.Generic.List<GameObject> { front });
                window.RefreshObjectsPreview();
                Assert.That(preview.Shows(front), Is.False);
                Assert.That(preview.Shows(back), Is.True);
                Assert.That(window.ObjectsPreview, Is.SameAs(preview));
                Assert.That(preview.Copy, Is.SameAs(copy));
                Assert.That(WindowType.GetField("session", Flags).GetValue(window), Is.SameAs(session));
                Assert.That(front.activeSelf, Is.True);
                Assert.That(front.GetComponent<SkinnedMeshRenderer>().enabled, Is.True);

                // 解除 brings it back; an empty row hides nothing.
                WindowType.GetField("excludedObjectEdits", Flags).SetValue(window, new System.Collections.Generic.List<GameObject> { null });
                window.RefreshObjectsPreview();
                Assert.That(preview.Shows(front) && preview.Shows(back), Is.True);

                Assert.That(scene.isDirty, Is.False);
                Assert.That(Undo.GetCurrentGroup(), Is.EqualTo(undoBefore));
                WindowType.GetField("excludedObjectEdits", Flags).SetValue(window, null);

                // Leaving the page drops the copy.
                window.GoTo(ExperimentalExpressionCaptureWindow.ExpressionsPage); Drain(window);
                Assert.That(window.ObjectsPreview, Is.Null);
                Assert.That(copy == null, Is.True);
                Assert.That(OwnedCopies(fixture.Source.name), Is.EqualTo(copiesBefore));
                Assert.That(scene.isDirty, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(window); fixture.Dispose();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void ClosingTheWindowLeavesNoPreviewCopy()
        {
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                WindowType.GetField("source", Flags).SetValue(window, fixture.Source);
                WindowType.GetMethod("Prepare", Flags).Invoke(window, null);
                window.GoTo(ExperimentalExpressionCaptureWindow.ObjectsPage); Drain(window);
                window.RefreshObjectsPreview();
                var copy = window.ObjectsPreview.Copy;
                Assert.That(copy != null, Is.True);
                Object.DestroyImmediate(window); window = null;
                Assert.That(copy == null, Is.True);
                Assert.That(OwnedCopies(fixture.Source.name), Is.Zero);
            }
            finally
            {
                if (window != null) Object.DestroyImmediate(window);
                fixture.Dispose();
            }
        }

        // A gimmick shown as 除外 hides what the export leaves out: the whole
        // object, or only its renderer.
        [Test]
        public void GimmicksChosenToBeLeftOutAreHiddenInThePreview()
        {
            var fixture = new AttachmentConnectionTests.Fixture();
            ExcludedObjectsPreview preview = null;
            try
            {
                var front = fixture.Source.transform.Find("Front").gameObject;
                var back = fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>();
                var findings = new[]
                {
                    new ExportGimmickFinding { Target = front, Unit = GimmickExclusionUnit.Hierarchy },
                    new ExportGimmickFinding { Target = back.gameObject, Renderer = back, Unit = GimmickExclusionUnit.Renderer },
                };
                var (objects, renderers) = ExperimentalExpressionCaptureWindow.PreviewHidden(null, findings);
                Assert.That(objects, Is.EqualTo(new[] { front }));
                Assert.That(renderers, Is.EqualTo(new Renderer[] { back }));

                preview = new ExcludedObjectsPreview(fixture.Source);
                preview.Show(objects, renderers);
                Assert.That(preview.Shows(front), Is.False);
                Assert.That(preview.Shows(back), Is.False);
                Assert.That(preview.Shows(back.gameObject), Is.True);
                // 含める
                preview.Show(objects, Array.Empty<Renderer>());
                Assert.That(preview.Shows(back), Is.True);
                preview.Show(Array.Empty<GameObject>(), Array.Empty<Renderer>());
                Assert.That(preview.Shows(front), Is.True);
                Assert.That(front.activeSelf && back.enabled, Is.True);
            }
            finally { preview?.Dispose(); fixture.Dispose(); }
        }

        // An object added to the avatar after the page opened can be left out too.
        [Test]
        public void AnObjectAddedAfterThePageOpenedCanBeHidden()
        {
            var fixture = new AttachmentConnectionTests.Fixture();
            ExcludedObjectsPreview preview = null;
            try
            {
                preview = new ExcludedObjectsPreview(fixture.Source);
                var added = new GameObject("Added");
                added.transform.SetParent(fixture.Source.transform, false);
                Assert.That(preview.Shows(added), Is.False);
                preview.Show(new[] { added }, null);
                Assert.That(preview.Shows(added), Is.False);
                preview.Show(null, null);
                Assert.That(preview.Shows(added), Is.True);
            }
            finally { preview?.Dispose(); fixture.Dispose(); }
        }

        // A camera kept far from the body and a skinned mesh whose bounds were
        // widened on purpose do not shrink the avatar: the fit is the body.
        [Test]
        public void TheFitIsTheBodyNotAFarGimmickOrWidenedBounds()
        {
            var fixture = new AttachmentConnectionTests.Fixture();
            ExcludedObjectsPreview preview = null;
            try
            {
                var far = GameObject.CreatePrimitive(PrimitiveType.Cube);
                far.transform.SetParent(fixture.Source.transform, false);
                far.transform.position = new Vector3(6, 8, -4);
                fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().localBounds = new Bounds(Vector3.zero, Vector3.one * 20);
                var drawn = fixture.Source.GetComponentsInChildren<Renderer>().Select(item => item.bounds).Aggregate((all, next) => { all.Encapsulate(next); return all; });
                Assert.That(drawn.size.y, Is.GreaterThan(15));

                var body = ExcludedObjectsPreview.BodyBounds(fixture.Source);
                Assert.That(body.HasValue, Is.True);
                // The hair mesh: 1.7 to 1.91 high, 0.12 wide either way with the blend shape.
                Assert.That(body.Value.min.y, Is.EqualTo(1.7f).Within(.02f));
                Assert.That(body.Value.max.y, Is.EqualTo(1.9f + .0035f).Within(.02f));
                Assert.That(body.Value.size.x, Is.LessThan(.3f));

                // The camera stays where it was framed whatever is hidden or brought back.
                preview = new ExcludedObjectsPreview(fixture.Source);
                preview.Show(new[] { far }, null);
                var framing = preview.Framing;
                Assert.That(framing.max.y, Is.LessThan(2.1f));
                preview.Show(null, null);
                Assert.That(preview.Framing, Is.EqualTo(framing));
            }
            finally { preview?.Dispose(); fixture.Dispose(); }
        }

        // Without a humanoid skeleton every mesh counts, by its vertices, in the
        // avatar's own axes when it is turned in the scene; when everything lies
        // out of the skeleton's reach every mesh counts again.
        [Test]
        public void WithoutASkeletonOrInReachTheFitStillHoldsTheMeshes()
        {
            var plain = new GameObject("Plain");
            try
            {
                var near = GameObject.CreatePrimitive(PrimitiveType.Cube);
                near.transform.SetParent(plain.transform, false);
                var aside = GameObject.CreatePrimitive(PrimitiveType.Cube);
                aside.transform.SetParent(plain.transform, false);
                aside.transform.position = new Vector3(3, 0, 0);
                var body = ExcludedObjectsPreview.BodyBounds(plain);
                Assert.That(body.HasValue, Is.True);
                Assert.That(body.Value.min.x, Is.EqualTo(-.5f).Within(.001f));
                Assert.That(body.Value.max.x, Is.EqualTo(3.5f).Within(.001f));
                plain.transform.rotation = Quaternion.Euler(0, 90, 0);
                var turned = ExcludedObjectsPreview.BodyBounds(plain).Value;
                Assert.That(turned.min.x, Is.EqualTo(-.5f).Within(.001f));
                Assert.That(turned.max.x, Is.EqualTo(3.5f).Within(.001f));
                Assert.That(turned.size.z, Is.EqualTo(1).Within(.001f));
                Object.DestroyImmediate(near); Object.DestroyImmediate(aside);
                Assert.That(ExcludedObjectsPreview.BodyBounds(plain).HasValue, Is.False);
            }
            finally { Object.DestroyImmediate(plain); }

            var fixture = new AttachmentConnectionTests.Fixture();
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) Object.DestroyImmediate(skin.gameObject);
                var far = GameObject.CreatePrimitive(PrimitiveType.Cube);
                far.transform.SetParent(fixture.Source.transform, false);
                far.transform.position = new Vector3(6, 8, -4);
                var body = ExcludedObjectsPreview.BodyBounds(fixture.Source);
                Assert.That(body.HasValue, Is.True);
                Assert.That(body.Value.center, Is.EqualTo(new Vector3(6, 8, -4)));
            }
            finally { fixture.Dispose(); }
        }

        [TestCase(568f, .4f)]
        [TestCase(1400f, .5f)]
        public void ThePreviewTakesTwoFifthsToHalfOfTheWidth(float width, float ratio)
        {
            var (list, preview) = ExperimentalExpressionCaptureWindow.ObjectsColumns(width);
            Assert.That(preview / (width - 12), Is.EqualTo(ratio).Within(.01f));
            Assert.That(list + preview + 12, Is.EqualTo(width).Within(.01f));
        }
    }
}
