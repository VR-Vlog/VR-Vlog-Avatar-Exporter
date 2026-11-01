using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The quick "アプリに転送" writes what "通常" writes when nothing on the
    // expression board is touched, through the same board save.
    public sealed class AppTransferQuickExportTests
    {
        string settingsPath, previousSettingsPath, presetPath, previousPresetPath;

        [SetUp]
        public void UseTemporaryStores()
        {
            previousSettingsPath = ExportSettingsStore.PathOverride;
            settingsPath = Path.Combine(Path.GetTempPath(), "vrvlog-quick-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = settingsPath;
            previousPresetPath = ExpressionPresetStore.PathOverride;
            presetPath = Path.Combine(Path.GetTempPath(), "vrvlog-quick-presets-" + Guid.NewGuid().ToString("N") + ".json");
            ExpressionPresetStore.PathOverride = presetPath;
        }

        [TearDown]
        public void RestoreStores()
        {
            ExportSettingsStore.PathOverride = previousSettingsPath;
            ExpressionPresetStore.PathOverride = previousPresetPath;
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            if (File.Exists(presetPath)) File.Delete(presetPath);
        }

        // An avatar that the board can prepare and export, in a saved scene so
        // its choices and transfer name are remembered.
        internal sealed class SavedFixture : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Fixture;
            internal GameObject Source => Fixture.Source;
            readonly string scenePath;

            internal SavedFixture()
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Fixture = new AttachmentConnectionTests.Fixture();
                foreach (var skin in Fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
                scenePath = "Assets/AppTransferQuick-" + Guid.NewGuid().ToString("N") + ".unity";
                Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
                Assert.That(ExpressionPresetStore.KeyFor(Fixture.Source), Is.Not.Null);
            }

            public void Dispose()
            {
                Fixture.Dispose();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        static AppTransferExport Transfer(GameObject avatar)
        {
            var transfer = new AppTransferExport();
            transfer.SelectAvatar(avatar);
            typeof(AppTransferExport).GetField("author", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(transfer, "SampleAuthor");
            return transfer;
        }

        static Dictionary<string, object> Expressions(string path)
        {
            var extensions = (Dictionary<string, object>)GlbDocument.Read(File.ReadAllBytes(path)).Json["extensions"];
            return (Dictionary<string, object>)((Dictionary<string, object>)extensions["VRMC_vrm"])["expressions"];
        }

        static string[] Names(Dictionary<string, object> expressions, string kind) =>
            expressions.TryGetValue(kind, out var group) ? ((Dictionary<string, object>)group).Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray() : new string[0];

        static int MorphBinds(Dictionary<string, object> expressions, string preset)
        {
            var presets = (Dictionary<string, object>)expressions["preset"];
            if (!presets.TryGetValue(preset, out var value)) return 0;
            return ((Dictionary<string, object>)value).TryGetValue("morphTargetBinds", out var binds) ? ((List<object>)binds).Count : 0;
        }

        [Test]
        public void QuickTransferWritesTheExpressionsOfAnUntouchedBoard()
        {
            using var fixture = new SavedFixture();
            // A choice the name inference would not make, saved on the board earlier.
            ExpressionPresetStore.Save(ExpressionPresetStore.KeyFor(fixture.Source),
                new[] { ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Hair detail") });
            var quickPath = Path.Combine(Path.GetTempPath(), "vrvlog-quick-" + Guid.NewGuid().ToString("N") + ".vrm");
            var boardPath = Path.Combine(Path.GetTempPath(), "vrvlog-quick-board-" + Guid.NewGuid().ToString("N") + ".vrm");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            try
            {
                var saved = 0;
                Assert.That(Transfer(fixture.Source).Export(fixture.Source, "Avatar", quickPath, () => saved++), Is.Null);
                Assert.That(saved, Is.EqualTo(1));

                // The board opened for the same avatar and saved without a change.
                type.GetField("source", flags).SetValue(window, fixture.Source);
                type.GetMethod("Prepare", flags).Invoke(window, null);
                var boardSaved = false;
                Assert.That(window.TransferExport("Avatar", "SampleAuthor", new AvatarLicenseOptions(), boardPath, () => boardSaved = true, false), Is.Null);
                Assert.That(boardSaved, Is.True);

                var quick = Expressions(quickPath);
                var board = Expressions(boardPath);
                Assert.That(Names(quick, "preset"), Is.EqualTo(Names(board, "preset")));
                Assert.That(Names(quick, "custom"), Is.EqualTo(Names(board, "custom")));
                Assert.That(MorphBinds(quick, "happy"), Is.GreaterThan(0), "The saved choice is in the quick transfer too.");
                Assert.That(MorphBinds(quick, "happy"), Is.EqualTo(MorphBinds(board, "happy")));
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (File.Exists(quickPath)) File.Delete(quickPath);
                if (File.Exists(boardPath)) File.Delete(boardPath);
            }
        }

        // The board starts from the blink saved for the avatar, as the old quick
        // transfer took it from the export settings.
        // The mode goes in as an int because the enum is internal and the test is public.
        [TestCase((int)BlinkExportMode.Auto)]
        [TestCase((int)BlinkExportMode.Manual)]
        public void TheHiddenBoardUsesTheSavedBlink(int modeValue)
        {
            var mode = (BlinkExportMode)modeValue;
            using var fixture = new SavedFixture();
            var settings = new ExportSettings();
            settings.Blink.Mode = mode;
            if (mode == BlinkExportMode.Manual)
                settings.Blink.Both.Add(new BlinkShapeBinding { Renderer = fixture.Source.GetComponentInChildren<SkinnedMeshRenderer>(), Shape = "Hair detail", Weight = 80f });
            ExportSettingsStore.Save(fixture.Source, settings);
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                var blink = (BlinkExportOptions)typeof(ExperimentalExpressionCaptureWindow)
                    .GetMethod("ExportBlinkOptions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(board, null);
                if (mode == BlinkExportMode.Auto) Assert.That(blink, Is.Null, "Automatic, as the board starts without a saved blink.");
                else Assert.That(blink.Mode, Is.EqualTo(mode));
                if (mode == BlinkExportMode.Manual)
                {
                    Assert.That(blink.Both.Single().Shape, Is.EqualTo("Hair detail"));
                    Assert.That(blink.Both.Single().Weight, Is.EqualTo(80f));
                }
            }
            finally { Object.DestroyImmediate(board); }
        }

        // The board starts from the poses saved for the avatar, as the old quick
        // transfer took them from the export settings; a rebuild does not add them again.
        [Test]
        public void TheHiddenBoardUsesTheSavedPosesOnce()
        {
            using var fixture = new SavedFixture();
            var settings = new ExportSettings();
            settings.Poses.Excluded.Add("SamplePose");
            settings.Poses.Names["SamplePose"] = "Sample";
            ExportSettingsStore.Save(fixture.Source, settings);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                PoseExportOptions Poses() => ((ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(board)).PoseOptions;
                Assert.That(Poses().Excluded, Is.EquivalentTo(new[] { "SamplePose" }));
                Assert.That(Poses().Names["SamplePose"], Is.EqualTo("Sample"));
                type.GetMethod("RebuildKeepingAssignments", flags).Invoke(board, new object[] { Array.Empty<GameObject>(), null });
                Assert.That(Poses().Excluded, Is.EquivalentTo(new[] { "SamplePose" }));
                Assert.That(Poses().Names.Count, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(board); }
        }

        // Making the copy again (a changed object list, an avatar that changed
        // under the board) keeps the blink and pose windows open, and the pose
        // window keeps editing the list the new copy exports. Leaving the avatar closes them.
        [Test]
        public void ARebuildKeepsTheBlinkAndPoseWindowsOpen()
        {
            using var fixture = new SavedFixture();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            var blink = ScriptableObject.CreateInstance<BlinkConfigurationWindow>();
            var poses = ScriptableObject.CreateInstance<PoseReviewWindow>();
            try
            {
                PoseExportOptions Poses() => ((ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(board)).PoseOptions;
                var edited = Poses();
                typeof(PoseReviewWindow).GetField("options", flags).SetValue(poses, edited);
                // Shown, as the board opens them; a window never shown cannot be closed.
                blink.ShowUtility(); poses.ShowUtility();
                type.GetField("blinkConfiguration", flags).SetValue(board, blink);
                type.GetField("poseReview", flags).SetValue(board, poses);
                type.GetMethod("RebuildKeepingAssignments", flags).Invoke(board, new object[] { Array.Empty<GameObject>(), null });
                Assert.That(blink != null, Is.True, "The blink window stays open.");
                Assert.That(poses != null, Is.True, "The pose window stays open.");
                Assert.That(type.GetField("blinkConfiguration", flags).GetValue(board), Is.SameAs(blink));
                Assert.That(type.GetField("poseReview", flags).GetValue(board), Is.SameAs(poses));
                Assert.That(Poses(), Is.SameAs(edited), "The new copy exports the list the pose window edits.");
                edited.Excluded.Add("SamplePose");
                Assert.That(Poses().Excluded, Is.EquivalentTo(new[] { "SamplePose" }));
                // Following an avatar that changed under the board, the pose window previews it again.
                Assert.That(poses.IsBusy, Is.False);
                type.GetMethod("FollowSource", flags).Invoke(board, new object[] { "test" });
                Assert.That(blink != null && poses != null, Is.True, "Following the avatar keeps the windows open.");
                Assert.That(Poses(), Is.SameAs(edited));
                Assert.That(poses.IsBusy, Is.True, "The pose window previews the avatar as it is now.");
                type.GetMethod("ChangeAvatar", flags).Invoke(board, null);
                Assert.That(blink == null, Is.True, "Leaving the avatar closes the blink window.");
                Assert.That(poses == null, Is.True, "Leaving the avatar closes the pose window.");
            }
            finally
            {
                if (blink != null) blink.Close();
                if (poses != null) poses.Close();
                Object.DestroyImmediate(board);
            }
        }

        // A rebuild whose objects leave no preview falls back to the previous
        // objects and keeps the windows and the poses as they were; one with
        // nothing to fall back to leaves no avatar on the board and closes them.
        [Test]
        public void AFailedRebuildKeepsThePosesAndClosesTheWindowsOnlyWithoutACopy()
        {
            using var fixture = new SavedFixture();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            var blink = ScriptableObject.CreateInstance<BlinkConfigurationWindow>();
            var poses = ScriptableObject.CreateInstance<PoseReviewWindow>();
            try
            {
                ExperimentalExpressionCaptureSession Session() => (ExperimentalExpressionCaptureSession)type.GetField("session", flags).GetValue(board);
                var edited = Session().PoseOptions;
                edited.Excluded.Add("SamplePose"); edited.Names["SamplePose"] = "Sample";
                typeof(PoseReviewWindow).GetField("options", flags).SetValue(poses, edited);
                blink.ShowUtility(); poses.ShowUtility();
                type.GetField("blinkConfiguration", flags).SetValue(board, blink);
                type.GetField("poseReview", flags).SetValue(board, poses);
                // Leaving out every mesh leaves nothing to preview.
                var meshes = fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.gameObject).ToArray();
                var rebuild = type.GetMethod("RebuildKeepingAssignments", flags);
                var problem = (string)rebuild.Invoke(board, new object[] { meshes, Array.Empty<GameObject>() });
                Assert.That(problem, Is.Not.Null, "The objects asked for leave no preview.");
                Assert.That(Session(), Is.Not.Null);
                Assert.That(blink != null && poses != null, Is.True, "With the previous objects back, the windows stay open.");
                Assert.That(Session().PoseOptions, Is.SameAs(edited));
                Assert.That(edited.Excluded, Is.EquivalentTo(new[] { "SamplePose" }));
                Assert.That(edited.Names, Is.EquivalentTo(new Dictionary<string, string> { ["SamplePose"] = "Sample" }));
                Assert.That(edited.Manual, Is.Empty, "The saved poses are not read again.");
                var failure = Assert.Throws<System.Reflection.TargetInvocationException>(() => rebuild.Invoke(board, new object[] { meshes, null }));
                Assert.That(failure.InnerException, Is.Not.Null);
                Assert.That(Session(), Is.Null);
                Assert.That(blink == null, Is.True, "Without a copy the blink window closes.");
                Assert.That(poses == null, Is.True, "Without a copy the pose window closes.");
            }
            finally
            {
                if (blink != null) blink.Close();
                if (poses != null) poses.Close();
                Object.DestroyImmediate(board);
            }
        }

        // Out of sight, a blocked board names what to fix; the open board shows it itself.
        [Test]
        public void ABlockedHiddenBoardSaysWhatToFix()
        {
            using var fixture = new SavedFixture();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-quick-blocked-" + Guid.NewGuid().ToString("N") + ".vrm");
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                typeof(ExperimentalExpressionCaptureWindow).GetField("excludedObjectEdits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .SetValue(board, new List<GameObject>());
                var needsFix = ExporterLocalization.T(AppTransferExport.BoardNeedsFix);
                var saved = false;
                Assert.That(board.TransferExport("Avatar", "SampleAuthor", new AvatarLicenseOptions(), path, () => saved = true, false),
                    Is.EqualTo(needsFix + "\n" + ExporterLocalization.T("書き出さないオブジェクトがプレビューに反映されていません")));
                Assert.That(board.TransferExport("Avatar", "SampleAuthor", new AvatarLicenseOptions(), path, () => saved = true), Is.EqualTo(needsFix));
                Assert.That(saved, Is.False);
                Assert.That(File.Exists(path), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(board);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // A board switched to another avatar starts with blink on, as a new board
        // (the quick transfer) does, before that avatar's saved blink applies.
        [Test]
        public void ChangingTheAvatarTurnsTheBlinkBackOn()
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(ExperimentalExpressionCaptureWindow);
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            try
            {
                type.GetField("automaticBlink", flags).SetValue(window, false);
                type.GetMethod("ChangeAvatar", flags).Invoke(window, null);
                Assert.That(type.GetField("automaticBlink", flags).GetValue(window), Is.True);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void QuickTransferLeavesNoBoardBehind()
        {
            using var fixture = new SavedFixture();
            var plain = new GameObject("Plain");
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-quick-left-" + Guid.NewGuid().ToString("N") + ".vrm");
            var before = Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Length;
            try
            {
                Assert.That(Transfer(fixture.Source).Export(fixture.Source, "Avatar", path, () => { }), Is.Null);
                Assert.That(Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Length, Is.EqualTo(before));
                Assert.That(ExperimentalExpressionCaptureWindow.ForTransfer(fixture.Source), Is.Null);
                // A board that cannot be prepared is gone as well.
                Assert.Catch<Exception>(() => Transfer(plain).Export(plain, "Plain", path + ".plain", () => { }));
                Assert.That(Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Length, Is.EqualTo(before));
                Assert.That(ExperimentalExpressionCaptureWindow.ForTransfer(plain), Is.Null);
            }
            finally
            {
                ExportAuthorTests.Forget(ExportAuthor.Identity(plain));
                Object.DestroyImmediate(plain);
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".plain")) File.Delete(path + ".plain");
            }
        }

        // The transfer screen sends a file saved earlier instead of exporting again, and saves the file it
        // sent, only while the stamp taken as the file was written still matches.
        [Test]
        public void AWrittenFileMatchesUntilWhatItIsMadeOfChanges()
        {
            using var fixture = new SavedFixture();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-quick-stamp-" + Guid.NewGuid().ToString("N") + ".vrm");
            try
            {
                var transfer = Transfer(fixture.Source);
                object stamp = null;
                Assert.That(transfer.Export(fixture.Source, "Avatar", path, () => stamp = transfer.SourceStamp(fixture.Source, false)), Is.Null);
                // Nothing the export does after the file is written counts as a change.
                Assert.That(transfer.SourceUnchanged(stamp, fixture.Source), Is.True);
                // Placing the avatar in the scene does not reach the file.
                fixture.Source.transform.position += Vector3.right;
                Assert.That(transfer.SourceUnchanged(stamp, fixture.Source), Is.True);

                var child = fixture.Source.GetComponentInChildren<SkinnedMeshRenderer>().gameObject;
                child.SetActive(!child.activeSelf);
                Assert.That(transfer.ChoicesUnchanged(stamp), Is.True);
                Assert.That(transfer.SourceUnchanged(stamp, fixture.Source), Is.False, "An object shown or hidden changes the file.");
                child.SetActive(!child.activeSelf);

                stamp = transfer.SourceStamp(fixture.Source, false);
                ExpressionPresetStore.Save(ExpressionPresetStore.KeyFor(fixture.Source),
                    new[] { ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Hair detail") });
                Assert.That(transfer.ChoicesUnchanged(stamp), Is.False, "A choice saved on a board changes the file.");

                stamp = transfer.SourceStamp(fixture.Source, false);
                ExportSettingsStore.Save(fixture.Source, ExportSettingsStore.Load(fixture.Source));
                Assert.That(transfer.ChoicesUnchanged(stamp), Is.False, "Saved export settings change the file.");

                var terms = transfer.TermsKey();
                Assert.That(transfer.TermsKey(), Is.EqualTo(terms));
                var settings = ExportSettingsStore.Load(fixture.Source);
                settings.License.AllowRedistribution = !settings.License.AllowRedistribution;
                ExportSettingsStore.Save(fixture.Source, settings);
                transfer.SelectAvatar(fixture.Source);
                Assert.That(transfer.TermsKey(), Is.Not.EqualTo(terms), "Other VRM terms write other meta.");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        // The open board keeps the blink, the added poses and the added expressions in memory only, and the
        // detailed transfer writes them: a change there also means the saved file is not what it would send.
        [Test]
        public void ADetailedStampAlsoMatchesTheInputsOfTheOpenBoard()
        {
            using var fixture = new SavedFixture();
            var board = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                var transfer = Transfer(fixture.Source);
                var stamp = transfer.SourceStamp(fixture.Source, true);
                Assert.That(transfer.ChoicesUnchanged(stamp), Is.True);
                var blink = typeof(ExperimentalExpressionCaptureWindow).GetField("automaticBlink", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                blink.SetValue(board, !(bool)blink.GetValue(board));
                Assert.That(transfer.ChoicesUnchanged(stamp), Is.False, "Another blink on the board changes the file.");
                Assert.That(transfer.ChoicesUnchanged(transfer.SourceStamp(fixture.Source, false)), Is.True, "The quick transfer starts from the stores.");
            }
            finally { Object.DestroyImmediate(board); }
        }
    }
}
