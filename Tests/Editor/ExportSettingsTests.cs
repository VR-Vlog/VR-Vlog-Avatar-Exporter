using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportSettingsTests
    {
        string storePath, previousPath;

        [SetUp]
        public void UseTemporaryStore()
        {
            previousPath = ExportSettingsStore.PathOverride;
            storePath = Path.Combine(Path.GetTempPath(), "vrvlog-export-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = storePath;
        }

        [TearDown]
        public void RestoreStore()
        {
            ExportSettingsStore.PathOverride = previousPath;
            if (File.Exists(storePath)) File.Delete(storePath);
        }

        sealed class SavedAvatar : IDisposable
        {
            internal readonly GameObject Avatar, Pet, Gimmick, Outside;
            internal readonly SkinnedMeshRenderer Face;
            readonly string scenePath;

            internal SavedAvatar()
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Avatar = new GameObject("SampleAvatar");
                Pet = new GameObject("Pet");
                Pet.transform.SetParent(Avatar.transform);
                Gimmick = new GameObject("Gimmick");
                Gimmick.transform.SetParent(Avatar.transform);
                var face = new GameObject("Face");
                face.transform.SetParent(Avatar.transform);
                Face = face.AddComponent<SkinnedMeshRenderer>();
                Outside = new GameObject("Outside");
                scenePath = "Assets/ExportSettings-" + Guid.NewGuid().ToString("N") + ".unity";
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                Assert.That(ExpressionPresetStore.KeyFor(Avatar), Is.Not.Null);
            }

            public void Dispose()
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        static ExportSettings Adjusted(SavedAvatar fixture)
        {
            var settings = new ExportSettings { AutoExcludeGimmicks = false };
            settings.ExcludedObjects.Add(fixture.Pet);
            settings.IncludedGimmicks.Add(fixture.Gimmick);
            settings.Blink.Mode = BlinkExportMode.Manual;
            settings.Blink.Both.Add(new BlinkShapeBinding { Renderer = fixture.Face, Shape = "Close", Weight = 80f });
            settings.Poses.Excluded.Add("pose-a");
            settings.Poses.Names["pose-b"] = "Renamed";
            settings.License.CopyrightInformation = "Sample copyright";
            settings.License.AllowRedistribution = true;
            return settings;
        }

        [Test]
        public void SettingsComeBackForTheSameAvatarAfterSelectingAnother()
        {
            using var fixture = new SavedAvatar();
            var settings = Adjusted(fixture);
            ExportSettingsStore.Save(fixture.Avatar, settings);
            var loaded = ExportSettingsStore.Load(fixture.Avatar, out var skipped);
            Assert.That(skipped, Is.Zero);
            Assert.That(loaded.Signature(), Is.EqualTo(settings.Signature()));
            Assert.That(loaded.ExcludedObjects, Is.EqualTo(new[] { fixture.Pet }));
            Assert.That(loaded.IncludedGimmicks, Is.EqualTo(new[] { fixture.Gimmick }));
            Assert.That(loaded.AutoExcludeGimmicks, Is.False);
            Assert.That(loaded.Blink.Mode, Is.EqualTo(BlinkExportMode.Manual));
            Assert.That(loaded.Blink.Both.Single().Renderer, Is.SameAs(fixture.Face));
            Assert.That(loaded.Poses.Names["pose-b"], Is.EqualTo("Renamed"));
            Assert.That(loaded.License.CopyrightInformation, Is.EqualTo("Sample copyright"));
            Assert.That(ExportSettingsStore.Load(fixture.Outside).Signature(), Is.EqualTo(new ExportSettings().Signature()),
                "Another avatar starts from the defaults.");
            var text = File.ReadAllText(storePath);
            Assert.That(text, Does.Not.Contain("SampleAvatar").And.Not.Contain("Pet"), "Objects are kept by identity, not by name.");
        }

        [Test]
        public void ObjectsThatNoLongerBelongToTheAvatarAreSkipped()
        {
            using var fixture = new SavedAvatar();
            var settings = Adjusted(fixture);
            ExportSettingsStore.Save(fixture.Avatar, settings);
            fixture.Pet.transform.SetParent(null);
            Object.DestroyImmediate(fixture.Gimmick);
            var loaded = ExportSettingsStore.Load(fixture.Avatar, out var skipped);
            Assert.That(skipped, Is.EqualTo(2));
            Assert.That(loaded.ExcludedObjects, Is.Empty);
            Assert.That(loaded.IncludedGimmicks, Is.Empty);
            Assert.That(loaded.Blink.Both.Single().Renderer, Is.SameAs(fixture.Face));
        }

        [Test]
        public void DefaultsAreNotWrittenAndDamagedOrForeignFilesNeverBlock()
        {
            using var fixture = new SavedAvatar();
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            ExportSettingsStore.Save(fixture.Avatar, new ExportSettings());
            Assert.That(File.ReadAllText(storePath), Does.Not.Contain(ExpressionPresetStore.KeyFor(fixture.Avatar)), "Resetting removes the entry.");
            File.WriteAllText(storePath, "{ not json", Encoding.UTF8);
            Assert.That(ExportSettingsStore.Load(fixture.Avatar).Signature(), Is.EqualTo(new ExportSettings().Signature()));
            const string future = "{\"Version\":2,\"Avatars\":[]}";
            File.WriteAllText(storePath, future, Encoding.UTF8);
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            Assert.That(File.ReadAllText(storePath), Is.EqualTo(future), "A file from another format version is never overwritten.");
        }

        [Test]
        public void AvatarsInAnUnsavedSceneKeepSettingsForTheSession()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatar = new GameObject("SampleAvatar");
            var pet = new GameObject("Pet");
            pet.transform.SetParent(avatar.transform);
            try
            {
                Assert.That(ExpressionPresetStore.KeyFor(avatar), Is.Null);
                var settings = new ExportSettings();
                settings.ExcludedObjects.Add(pet);
                ExportSettingsStore.Save(avatar, settings);
                Assert.That(ExportSettingsStore.Load(avatar).ExcludedObjects, Is.EqualTo(new[] { pet }));
                Assert.That(File.Exists(storePath), Is.False);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void CopiesDoNotShareTheChoices()
        {
            using var fixture = new SavedAvatar();
            var settings = Adjusted(fixture);
            var copy = settings.Copy();
            settings.ExcludedObjects.Clear();
            settings.Blink.Both[0].Weight = 10f;
            settings.Poses.Names.Clear();
            settings.License.CopyrightInformation = "";
            Assert.That(copy.ExcludedObjects, Is.EqualTo(new[] { fixture.Pet }));
            Assert.That(copy.Blink.Both[0].Weight, Is.EqualTo(80f));
            Assert.That(copy.Poses.Names, Has.Count.EqualTo(1));
            Assert.That(copy.License.CopyrightInformation, Is.EqualTo("Sample copyright"));
        }

        [Test]
        public void ReloadingKeepsThePoseOptionsAnOpenPoseWindowEdits()
        {
            using var fixture = new SavedAvatar();
            var settings = new ExportSettings();
            var poses = settings.Poses;
            settings.Assign(Adjusted(fixture));
            Assert.That(settings.Poses, Is.SameAs(poses));
            Assert.That(settings.Poses.Names["pose-b"], Is.EqualTo("Renamed"));
            Assert.That(settings.Signature(), Is.EqualTo(Adjusted(fixture).Signature()));
        }

        [Test]
        public void AppTransferExportsWithTheAvatarsRememberedSettings()
        {
            using var fixture = new SavedAvatar();
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            var transfer = new AppTransferExport();
            transfer.SelectAvatar(fixture.Avatar);
            typeof(AppTransferExport).GetField("author", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transfer, "SampleAuthor");
            var original = AppTransferExport.SaveNormally;
            GameObject[] excluded = null;
            ExportGimmickOptions gimmicks = null;
            try
            {
                AppTransferExport.SaveNormally = (avatar, destination, create, completed, previewExcluded, previewGimmicks, asks, openBlink) =>
                {
                    excluded = previewExcluded?.ToArray();
                    gimmicks = previewGimmicks;
                    return null;
                };
                transfer.Export(fixture.Avatar, "SampleAvatar", Path.Combine(Path.GetTempPath(), "unused.vrm"), () => { });
            }
            finally { AppTransferExport.SaveNormally = original; }
            Assert.That(excluded, Is.EqualTo(new[] { fixture.Pet }), "The transfer leaves out what the export window leaves out.");
            Assert.That(gimmicks.AutoExclude, Is.False);
            Assert.That(gimmicks.IncludedObjects, Is.EqualTo(new[] { fixture.Gimmick }));
            Assert.That(ExportSettingsStore.Load(fixture.Avatar).License.CopyrightInformation, Is.EqualTo("Sample copyright"),
                "The license shown in the transfer is the remembered one and stays remembered.");
        }
    }
}
