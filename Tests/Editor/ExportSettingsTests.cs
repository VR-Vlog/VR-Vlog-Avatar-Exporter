using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportSettingsTests
    {
        string storePath, previousPath, presetPath, previousPresetPath;

        [SetUp]
        public void UseTemporaryStore()
        {
            previousPath = ExportSettingsStore.PathOverride;
            storePath = Path.Combine(Path.GetTempPath(), "vrvlog-export-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = storePath;
            // The app transfer prepares an expression board, which reads and saves the avatar's choices.
            previousPresetPath = ExpressionPresetStore.PathOverride;
            presetPath = Path.Combine(Path.GetTempPath(), "vrvlog-export-presets-" + Guid.NewGuid().ToString("N") + ".json");
            ExpressionPresetStore.PathOverride = presetPath;
        }

        [TearDown]
        public void RestoreStore()
        {
            ExportSettingsStore.PathOverride = previousPath;
            ExpressionPresetStore.PathOverride = previousPresetPath;
            if (File.Exists(storePath)) File.Delete(storePath);
            if (File.Exists(presetPath)) File.Delete(presetPath);
        }

        sealed class SavedAvatar : IDisposable
        {
            internal readonly GameObject Avatar, Pet, Gimmick, Outside;
            internal readonly SkinnedMeshRenderer Face;
            internal readonly BoxCollider RootSettings;
            readonly string scenePath;

            internal SavedAvatar()
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Avatar = new GameObject("SampleAvatar");
                RootSettings = Avatar.AddComponent<BoxCollider>();
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
            settings.IncludedGimmickComponents.Add(fixture.RootSettings);
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
            Assert.That(loaded.IncludedGimmickComponents, Is.EqualTo(new Component[] { fixture.RootSettings }), "A kept component on the root stays a component.");
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
        public void AFileThatCannotBeReadIsNeverOverwritten()
        {
            using var fixture = new SavedAvatar();
            const string damaged = "{ not json";
            File.WriteAllText(storePath, damaged, Encoding.UTF8);
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            ExportSettingsStore.SaveTransferName(fixture.Avatar, "Avatar");
            Assert.That(File.ReadAllText(storePath), Is.EqualTo(damaged), "Other avatars' entries may still be in the file.");
            using (new FileStream(storePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            }
            Assert.That(File.ReadAllText(storePath), Is.EqualTo(damaged), "A locked file is not replaced either.");
        }

        [Test]
        public void ASaveThatStoresNothingDoesNotTellOtherWindowsToReload()
        {
            using var fixture = new SavedAvatar();
            var before = ExportSettingsStore.Revision;
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            Assert.That(ExportSettingsStore.Revision, Is.EqualTo(before + 1), "A stored save tells other windows.");
            File.WriteAllText(storePath, "{ not json", Encoding.UTF8);
            before = ExportSettingsStore.Revision;
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            Assert.That(ExportSettingsStore.Revision, Is.EqualTo(before), "A file that cannot be read is not saved.");
            File.WriteAllText(storePath, "{\"Version\":2,\"Avatars\":[]}", Encoding.UTF8);
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            Assert.That(ExportSettingsStore.Revision, Is.EqualTo(before), "A file from another format version is not saved.");
        }

        [Test]
        public void BlinkBindingsToAMissingRendererAreDropped()
        {
            using var fixture = new SavedAvatar();
            var other = new GameObject("Other");
            other.transform.SetParent(fixture.Avatar.transform);
            var lashes = other.AddComponent<SkinnedMeshRenderer>();
            var settings = Adjusted(fixture);
            settings.Blink.Left.Add(new BlinkShapeBinding { Renderer = lashes, Shape = "CloseLeft", Weight = 100f });
            settings.Blink.Right.Add(new BlinkShapeBinding { Renderer = fixture.Face, Shape = "CloseRight", Weight = 100f });
            ExportSettingsStore.Save(fixture.Avatar, settings);
            Object.DestroyImmediate(other);
            var loaded = ExportSettingsStore.Load(fixture.Avatar, out var skipped);
            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(loaded.Blink.Left, Is.Empty, "A binding without its renderer would make the export leave out all blinking.");
            Assert.That(loaded.Blink.Right.Single().Renderer, Is.SameAs(fixture.Face));
            Assert.That(loaded.Blink.Both.Single().Renderer, Is.SameAs(fixture.Face));
        }

        [Test]
        public void TheWindowKeepsTheStoredSettingsAfterAScriptReload()
        {
            using var fixture = new SavedAvatar();
            var adjusted = Adjusted(fixture);
            ExportSettingsStore.Save(fixture.Avatar, adjusted);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(LilToonExporterWindow);
            void Call(LilToonExporterWindow target, string name) => type.GetMethod(name, flags).Invoke(target, null);
            var window = ScriptableObject.CreateInstance<LilToonExporterWindow>();
            try
            {
                // After a reload the avatar is restored but the settings are the
                // defaults and nothing has been loaded yet.
                type.GetField("avatar", flags).SetValue(window, fixture.Avatar);
                Call(window, "OnInspectorUpdate");
                Assert.That(ExportSettingsStore.Load(fixture.Avatar).Signature(), Is.EqualTo(adjusted.Signature()),
                    "The defaults never replace the stored entry before the settings are loaded.");
                Call(window, "OnEnable");
                var settings = (ExportSettings)type.GetField("settings", flags).GetValue(window);
                Assert.That(settings.Signature(), Is.EqualTo(adjusted.Signature()));
                Call(window, "OnInspectorUpdate");
                Assert.That(ExportSettingsStore.Load(fixture.Avatar).Signature(), Is.EqualTo(adjusted.Signature()));
            }
            finally { Object.DestroyImmediate(window); }
            Assert.That(ExportSettingsStore.Load(fixture.Avatar).Signature(), Is.EqualTo(adjusted.Signature()), "Closing the window keeps them too.");
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
            // The quick transfer exports from a board prepared out of sight, so
            // the avatar needs a mesh the board can show.
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var pet = new GameObject("Pet");
            pet.transform.SetParent(fixture.Source.transform);
            var gimmick = new GameObject("Gimmick");
            gimmick.transform.SetParent(fixture.Source.transform);
            Assert.That(EditorSceneManager.SaveScene(fixture.Source.scene), Is.True);
            var settings = new ExportSettings { AutoExcludeGimmicks = false };
            settings.ExcludedObjects.Add(pet);
            settings.IncludedGimmicks.Add(gimmick);
            settings.License.CopyrightInformation = "Sample copyright";
            ExportSettingsStore.Save(fixture.Source, settings);

            // The board prepared for the quick transfer leaves out what the export window leaves out.
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                var session = (ExperimentalExpressionCaptureSession)typeof(ExperimentalExpressionCaptureWindow)
                    .GetField("session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(board);
                Assert.That(session.ExcludedObjects, Is.EqualTo(new[] { pet }), "The transfer leaves out what the export window leaves out.");
                var gimmicks = session.GimmickOptions;
                Assert.That(gimmicks.AutoExclude, Is.False);
                Assert.That(gimmicks.IncludedObjects, Is.EqualTo(new[] { gimmick }));
            }
            finally { Object.DestroyImmediate(board); }

            var transfer = new AppTransferExport();
            transfer.SelectAvatar(fixture.Source);
            typeof(AppTransferExport).GetField("author", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transfer, "SampleAuthor");
            var destination = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-settings-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                Assert.That(transfer.Export(fixture.Source, "Avatar", destination, () => { }), Is.Null);
                var extensions = (System.Collections.Generic.Dictionary<string, object>)GlbDocument.Read(File.ReadAllBytes(destination)).Json["extensions"];
                var meta = (System.Collections.Generic.Dictionary<string, object>)((System.Collections.Generic.Dictionary<string, object>)extensions["VRMC_vrm"])["meta"];
                Assert.That(meta["copyrightInformation"], Is.EqualTo("Sample copyright"), "The VRM carries the remembered license.");
            }
            finally { if (File.Exists(destination)) File.Delete(destination); }
            Assert.That(ExportSettingsStore.Load(fixture.Source).License.CopyrightInformation, Is.EqualTo("Sample copyright"),
                "The license shown in the transfer is the remembered one and stays remembered.");
        }

        [Test]
        public void TransferNameComesBackAndSurvivesSavingTheSettings()
        {
            using var fixture = new SavedAvatar();
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Avatar), Is.Null);
            ExportSettingsStore.SaveTransferName(fixture.Avatar, " Avatar ");
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Avatar), Is.EqualTo("Avatar"));
            Assert.That(ExportSettingsStore.Load(fixture.Avatar).Signature(), Is.EqualTo(new ExportSettings().Signature()),
                "The name is not part of the export settings.");
            ExportSettingsStore.Save(fixture.Avatar, Adjusted(fixture));
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Avatar), Is.EqualTo("Avatar"), "Saving the settings keeps the name.");
            ExportSettingsStore.Save(fixture.Avatar, new ExportSettings());
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Avatar), Is.EqualTo("Avatar"),
                "An entry with default settings stays while it holds a name.");
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Outside), Is.Null, "Another avatar has no name.");
            ExportSettingsStore.SaveTransferName(fixture.Avatar, "");
            Assert.That(ExportSettingsStore.LoadTransferName(fixture.Avatar), Is.Null);
            Assert.That(File.ReadAllText(storePath), Does.Not.Contain(ExpressionPresetStore.KeyFor(fixture.Avatar)),
                "An entry that only held the name is removed with it.");
        }

        [Test]
        public void AppTransferRemembersTheNameOnlyAfterASave()
        {
            static AppTransferExport Transfer(GameObject avatar)
            {
                var transfer = new AppTransferExport();
                transfer.SelectAvatar(avatar);
                typeof(AppTransferExport).GetField("author", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(transfer, "SampleAuthor");
                return transfer;
            }
            var destination = Path.Combine(Path.GetTempPath(), "vrvlog-transfer-name-" + Guid.NewGuid().ToString("N") + ".vrm");
            var saved = 0;
            try
            {
                // Failed before saving: the board prepares (its renderer has no mesh
                // but is there to show), and the export stops with the reason and
                // the recovery window, as the board's save does.
                using (var empty = new SavedAvatar())
                {
                    var failing = Transfer(empty.Avatar);
                    Assert.That(failing.Export(empty.Avatar, "Failed", destination, () => saved++), Is.Not.Null.And.Not.Empty);
                    Assert.That(failing.RememberedName(empty.Avatar), Is.Null);
                    Assert.That(saved, Is.Zero);
                    Assert.That(File.Exists(destination), Is.False);
                }
                // Saved.
                using var fixture = new AppTransferQuickExportTests.SavedFixture();
                var transfer = Transfer(fixture.Source);
                Assert.That(transfer.RememberedName(fixture.Source), Is.Null);
                Assert.That(transfer.Export(fixture.Source, "Avatar", destination, () => saved++), Is.Null);
                Assert.That(saved, Is.EqualTo(1));
                Assert.That(transfer.RememberedName(fixture.Source), Is.EqualTo("Avatar"));
            }
            finally
            {
                foreach (var failure in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failure);
                if (File.Exists(destination)) File.Delete(destination);
            }
        }

        [Test]
        public void DetailedAppTransferNeedsTheExpressionBoard()
        {
            using var fixture = new SavedAvatar();
            var transfer = new AppTransferExport();
            transfer.SelectAvatar(fixture.Avatar);
            typeof(AppTransferExport).GetField("author", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transfer, "SampleAuthor");
            var saved = false;
            var reason = ExporterLocalization.T(AppTransferExport.BoardClosed);
            Assert.That(transfer.DetailedProblem(fixture.Avatar), Is.EqualTo(reason));
            Assert.That(transfer.ExportDetailed(fixture.Avatar, "Avatar", Path.Combine(Path.GetTempPath(), "unused.vrm"), () => saved = true), Is.EqualTo(reason));
            Assert.That(saved, Is.False);
            Assert.That(transfer.RememberedName(fixture.Avatar), Is.Null);
        }
    }
}
