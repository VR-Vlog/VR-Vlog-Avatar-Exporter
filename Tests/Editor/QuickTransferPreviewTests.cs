using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The full-body preview beside the quick transfer's form: it hides what the
    // board's objects page hides by default, from the saved settings.
    public sealed class QuickTransferPreviewTests
    {
        readonly List<Object> owned = new List<Object>();
        GameObject avatar;
        string storePath, previousPath;
        Func<GameObject, ExcludedObjectsPreview> previousCreate;

        [SetUp] public void SetUp()
        {
            avatar = Own(new GameObject("quick-preview-avatar"));
            previousPath = ExportSettingsStore.PathOverride;
            storePath = Path.Combine(Path.GetTempPath(), "vrvlog-quick-preview-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = storePath;
            previousCreate = QuickTransferPreview.Create;
        }

        [TearDown] public void TearDown()
        {
            QuickTransferPreview.Create = previousCreate;
            for (var i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.DestroyImmediate(owned[i]);
            owned.Clear();
            ExportSettingsStore.PathOverride = previousPath;
            if (File.Exists(storePath)) File.Delete(storePath);
        }

        T Own<T>(T item) where T : Object { owned.Add(item); return item; }

        MeshRenderer Render(string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(avatar.transform, false);
            var mesh = Own(new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } });
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static int OwnedCopies(string name) => Resources.FindObjectsOfTypeAll<GameObject>()
            .Count(item => item != null && item.name == name && item.transform.parent == null && EditorSceneManager.IsPreviewScene(item.scene));

        [Test] public void HidesTheSavedExclusionsAndTheRemovedGimmicks()
        {
            var body = Render("body", Own(new Material(Shader.Find("Unlit/Color"))));
            var hat = Render("hat", Own(new Material(Shader.Find("Unlit/Color"))));
            var effect = Render("effect", Own(new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback"))));
            using (var preview = new QuickTransferPreview())
            {
                preview.Update(avatar);
                Assert.That(preview.Error, Is.Null);
                Assert.That(preview.Preview.Shows(body.gameObject), Is.True);
                Assert.That(preview.Preview.Shows(hat.gameObject), Is.True);
                // Removed by default, as on the board.
                Assert.That(preview.Preview.Shows(effect), Is.False);

                var settings = ExportSettingsStore.Load(avatar);
                settings.ExcludedObjects.Add(hat.gameObject);
                ExportSettingsStore.Save(avatar, settings);
                Assert.That(preview.Stale(avatar), Is.True);
                preview.Update(avatar);
                Assert.That(preview.Stale(avatar), Is.False);
                Assert.That(preview.Preview.Shows(hat.gameObject), Is.False);
                Assert.That(preview.Preview.Shows(body.gameObject), Is.True);
            }
        }

        [Test] public void DisposeLeavesNoCopy()
        {
            Render("body", Own(new Material(Shader.Find("Unlit/Color"))));
            var before = OwnedCopies(avatar.name);
            var preview = new QuickTransferPreview();
            preview.Update(avatar);
            Assert.That(OwnedCopies(avatar.name), Is.EqualTo(before + 1));
            preview.Update(null);
            Assert.That(OwnedCopies(avatar.name), Is.EqualTo(before));
            preview.Update(avatar);
            preview.Dispose();
            Assert.That(OwnedCopies(avatar.name), Is.EqualTo(before));
        }

        [Test] public void AFailureKeepsItsReasonUntilTheAvatarChanges()
        {
            Render("body", Own(new Material(Shader.Find("Unlit/Color"))));
            var calls = 0;
            QuickTransferPreview.Create = target => { calls++; throw new InvalidOperationException("no preview here"); };
            using (var preview = new QuickTransferPreview())
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("no preview here"));
                preview.Update(avatar);
                Assert.That(preview.Error, Is.EqualTo("no preview here"));
                Assert.That(preview.Preview, Is.Null);
                // Not tried again on every paint.
                Assert.That(preview.Stale(avatar), Is.False);
                preview.Update(avatar);
                Assert.That(calls, Is.EqualTo(1));
                preview.Update(null);
                Assert.That(preview.Error, Is.Null);
            }
        }
    }
}
