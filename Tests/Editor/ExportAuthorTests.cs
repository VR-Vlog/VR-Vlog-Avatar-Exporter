using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportAuthorTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(" \t\n")]
        public void EmptyOrBlankAuthorIsNotAccepted(string author)
        {
            Assert.That(ExportAuthor.IsValid(author), Is.False);
            var identity = "test-" + Guid.NewGuid().ToString("N");
            try
            {
                ExportAuthor.Remember(identity, author);
                Assert.That(ExportAuthor.Remembered(identity), Is.Empty, "A blank author is never remembered.");
                ExportAuthor.Remember(identity, "SampleAuthor");
                ExportAuthor.Remember(identity, author);
                Assert.That(ExportAuthor.Remembered(identity), Is.EqualTo("SampleAuthor"), "A blank author does not replace the remembered one.");
            }
            finally { ExportAuthor.Forget(identity); }
        }

        [Test]
        public void RememberedAuthorIsOfferedOnlyForTheSameAvatar()
        {
            var first = "test-" + Guid.NewGuid().ToString("N");
            var second = "test-" + Guid.NewGuid().ToString("N");
            try
            {
                Assert.That(ExportAuthor.IsValid("SampleAuthor"), Is.True);
                ExportAuthor.Remember(first, "  SampleAuthor ");
                Assert.That(ExportAuthor.Remembered(first), Is.EqualTo("SampleAuthor"));
                Assert.That(ExportAuthor.Remembered(second), Is.Empty);
                Assert.That(ExportAuthor.ForSelection(ExportAuthor.Remembered(first), ""), Is.EqualTo("SampleAuthor"));
                Assert.That(ExportAuthor.ForSelection(ExportAuthor.Remembered(first), "TypedName"), Is.EqualTo("SampleAuthor"),
                    "The avatar's remembered author replaces the field.");
                Assert.That(ExportAuthor.ForSelection(ExportAuthor.Remembered(second), ""), Is.Empty,
                    "Another avatar's record does not fill an empty field with the first avatar's author.");
                Assert.That(ExportAuthor.ForSelection(ExportAuthor.Remembered(second), "TypedName"), Is.EqualTo("TypedName"),
                    "Without a remembered author the field keeps what was typed.");
            }
            finally { ExportAuthor.Forget(first); ExportAuthor.Forget(second); }
        }

        [Test]
        public void SavedSceneAvatarsHaveSeparateStableIdentitiesAndUnsavedOnesHaveNone()
        {
            // The test runner supplies an unsaved temporary scene, which Unity
            // cannot use as the base for an additive scene. It restores the
            // user's scene setup when the test run ends.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var scenePath = "Assets/ExportAuthorScene-" + Guid.NewGuid().ToString("N") + ".unity";
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var other = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            string firstIdentity = null, secondIdentity = null;
            try
            {
                var first = new GameObject("SampleAvatar");
                var second = new GameObject("TestAvatar");
                Assert.That(ExportAuthor.Identity(first), Is.Null, "An avatar in an unsaved scene is not remembered.");
                Assert.That(ExportAuthor.Identity(null), Is.Null);
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                firstIdentity = ExportAuthor.Identity(first);
                secondIdentity = ExportAuthor.Identity(second);
                Assert.That(firstIdentity, Is.Not.Null.And.Not.Empty);
                Assert.That(secondIdentity, Is.Not.Null.And.Not.Empty);
                Assert.That(firstIdentity, Is.Not.EqualTo(secondIdentity));
                first.name = "SampleAvatar renamed";
                Assert.That(ExportAuthor.Identity(first), Is.EqualTo(firstIdentity), "Renaming the avatar keeps its identity.");

                // The export window offers the remembered author for that avatar only.
                ExportAuthor.Remember(first, "SampleAuthor");
                var select = typeof(ExperimentalExpressionCaptureWindow).GetMethod("SelectSource", Flags);
                var author = typeof(ExperimentalExpressionCaptureWindow).GetField("author", Flags);
                select.Invoke(window, new object[] { first });
                Assert.That(author.GetValue(window), Is.EqualTo("SampleAuthor"));
                select.Invoke(other, new object[] { second });
                Assert.That(author.GetValue(other), Is.Empty, "A new window for an avatar without a record starts empty.");
                author.SetValue(other, "TypedName");
                select.Invoke(other, new object[] { first });
                Assert.That(author.GetValue(other), Is.EqualTo("SampleAuthor"));
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(other);
                ExportAuthor.Forget(firstIdentity);
                ExportAuthor.Forget(secondIdentity);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        public void ExportWindowDoesNotStartSavingWithoutAnAuthor(string author)
        {
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            var source = new GameObject("SampleAvatar");
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-author-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var type = typeof(ExperimentalExpressionCaptureWindow);
                type.GetField("source", Flags).SetValue(window, source);
                type.GetField("author", Flags).SetValue(window, author);
                var exception = Assert.Throws<InvalidOperationException>(() => window.SaveVrm(path));
                Assert.That(exception.Message, Is.EqualTo(ExporterLocalization.T(ExportAuthor.Required)));
                Assert.That(File.Exists(path), Is.False);
                Assert.That(type.GetField("saved", Flags).GetValue(window), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(source);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
