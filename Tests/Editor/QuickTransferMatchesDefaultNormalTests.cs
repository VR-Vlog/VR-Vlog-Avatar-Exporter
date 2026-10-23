using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using NUnit.Framework;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The invariant of the two app transfers: "アプリに転送（かんたん）" writes the
    // same VRM as "アプリに転送（通常）" opened on the avatar and exported with
    // nothing on the board touched. Each case exports one avatar both ways,
    // the normal one through the real board window, and compares the files and
    // what each export reported.
    public sealed class QuickTransferMatchesDefaultNormalTests
    {
        const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        static readonly Type Board = typeof(ExperimentalExpressionCaptureWindow);

        // Answers the result list at once, as if the user pressed 保存する, and
        // keeps its text.
        sealed class SavingDialog : IExportFallbackDialog
        {
            internal readonly List<string> Texts = new List<string>();
            public void Show(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings)
            {
                Texts.Add(result.Text());
                save();
            }
        }

        // What one export wrote and reported.
        sealed class Exported
        {
            internal string Path;
            // The result list: shown before saving by "通常", logged by "かんたん". Null
            // when the export worked around nothing.
            internal string ResultList;
            // The detail lines logged after saving.
            internal string[] Details = Array.Empty<string>();
        }

        string settingsPath, previousSettingsPath, presetPath, previousPresetPath;
        IExportFallbackDialog originalDialog;
        Func<bool> originalBatchMode;
        SavingDialog dialog;
        readonly List<string> files = new List<string>();

        [SetUp]
        public void SetUp()
        {
            previousSettingsPath = ExportSettingsStore.PathOverride;
            settingsPath = Path.Combine(Path.GetTempPath(), "vrvlog-same-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = settingsPath;
            previousPresetPath = ExpressionPresetStore.PathOverride;
            presetPath = Path.Combine(Path.GetTempPath(), "vrvlog-same-presets-" + Guid.NewGuid().ToString("N") + ".json");
            ExpressionPresetStore.PathOverride = presetPath;
            originalDialog = ExportFallbackConfirmation.Dialog;
            originalBatchMode = ExportFallbackConfirmation.IsBatchMode;
            ExportFallbackConfirmation.Dialog = dialog = new SavingDialog();
            // The test runner may be a batch run; pretend an Editor session so
            // "通常" asks about the result list as it does for a user.
            ExportFallbackConfirmation.IsBatchMode = () => false;
            // A board left open with added faces would ask before changing avatar.
            CloseBoards();
        }

        [TearDown]
        public void TearDown()
        {
            CloseBoards();
            ExportFallbackConfirmation.Dialog = originalDialog;
            ExportFallbackConfirmation.IsBatchMode = originalBatchMode;
            ExportSettingsStore.PathOverride = previousSettingsPath;
            ExpressionPresetStore.PathOverride = previousPresetPath;
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
            if (File.Exists(presetPath)) File.Delete(presetPath);
            foreach (var file in files) if (File.Exists(file)) File.Delete(file);
            files.Clear();
        }

        static void CloseBoards()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().ToArray())
                if (window != null) window.Close();
        }

        string NewPath(string kind)
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-same-" + kind + "-" + Guid.NewGuid().ToString("N") + ".vrm");
            files.Add(path);
            return path;
        }

        static AppTransferExport Transfer(GameObject avatar)
        {
            var transfer = new AppTransferExport();
            transfer.SelectAvatar(avatar);
            typeof(AppTransferExport).GetField("author", Flags).SetValue(transfer, "SampleAuthor");
            return transfer;
        }

        // A saved choice the name inference would not make.
        [Test]
        public void TheSavedExpressionsMatch()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            ExpressionPresetStore.Save(ExpressionPresetStore.KeyFor(fixture.Source),
                new[] { ExperimentalExpressionCaptureSession.PresetAssignment.ForShape(ExpressionPreset.happy, "Front", "Hair detail") });
            var (quick, normal) = ExportBoth(fixture.Source);
            AssertSame(quick, normal);
            Assert.That(MorphBinds(Read(quick.Path).Json, "happy"), Is.GreaterThan(0), "The saved choice is in both files.");
        }

        // A renderer the gimmick detection leaves out, and an object the export
        // window was told to leave out.
        [Test]
        public void TheGimmicksAndTheSavedExclusionsMatch()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var front = fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>();
            var hiddenMaterial = new Material(Shader.Find("VRVlogTests/UnsupportedHiddenFallback"));
            try
            {
                var auxiliary = new GameObject("Auxiliary fallback");
                auxiliary.transform.SetParent(fixture.Source.transform, false);
                var hiddenSkin = auxiliary.AddComponent<SkinnedMeshRenderer>();
                hiddenSkin.sharedMesh = front.sharedMesh; hiddenSkin.sharedMaterial = hiddenMaterial;
                hiddenSkin.bones = front.bones; hiddenSkin.rootBone = front.rootBone;
                var pet = new GameObject("Pet");
                pet.transform.SetParent(fixture.Source.transform, false);
                var petSkin = pet.AddComponent<SkinnedMeshRenderer>();
                petSkin.sharedMesh = front.sharedMesh; petSkin.sharedMaterial = front.sharedMaterial;
                petSkin.bones = front.bones; petSkin.rootBone = front.rootBone;
                Assert.That(EditorSceneManager.SaveScene(fixture.Source.scene), Is.True);
                var settings = new ExportSettings();
                settings.ExcludedObjects.Add(pet);
                ExportSettingsStore.Save(fixture.Source, settings);

                var (quick, normal) = ExportBoth(fixture.Source);
                AssertSame(quick, normal);
                var drawn = MeshNodes(Read(quick.Path).Json);
                Assert.That(drawn, Is.Not.Empty);
                Assert.That(drawn, Has.None.EqualTo("Pet"), "The saved exclusion applies to both.");
                Assert.That(drawn, Has.None.EqualTo("Auxiliary fallback"), "The detected gimmick is left out of both.");
            }
            finally { Object.DestroyImmediate(hiddenMaterial); }
        }

        // An avatar with nothing saved for it.
        [Test]
        public void APlainAvatarMatches()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var (quick, normal) = ExportBoth(fixture.Source);
            AssertSame(quick, normal);
        }

        // A stage that fails (a lilToon second texture that is a RenderTexture)
        // with 省いて続ける on: both leave it out, save, and list it.
        [Test]
        public void AFailingStageMatches()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var material = new Material(Shader.Find("lilToon"));
            var texture = new RenderTexture(16, 16, 0);
            var hadPreference = EditorPrefs.HasKey(ExportStageGuard.ContinuePreference);
            var previous = EditorPrefs.GetBool(ExportStageGuard.ContinuePreference, true);
            try
            {
                EditorPrefs.SetBool(ExportStageGuard.ContinuePreference, true);
                material.SetFloat("_UseMain2ndTex", 1); material.SetTexture("_Main2ndTex", texture);
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var (quick, normal) = ExportBoth(fixture.Source);
                Assert.That(quick.ResultList, Is.Not.Null, "The left-out stage is in the result list of the quick transfer.");
                Assert.That(normal.ResultList, Is.Not.Null, "The left-out stage is in the result list of the normal transfer.");
                AssertSame(quick, normal);
                Assert.That(Resources.FindObjectsOfTypeAll<ExportFailureWindow>().Any(), Is.False, "Neither stops at the failing stage.");
            }
            finally
            {
                if (hadPreference) EditorPrefs.SetBool(ExportStageGuard.ContinuePreference, previous);
                else EditorPrefs.DeleteKey(ExportStageGuard.ContinuePreference);
                foreach (var failure in Resources.FindObjectsOfTypeAll<ExportFailureWindow>()) Object.DestroyImmediate(failure);
                Object.DestroyImmediate(material); Object.DestroyImmediate(texture);
            }
        }

        // A board used for another avatar, with choices that belong to that
        // avatar, starts over for this one: the open board then holds what the
        // hidden board of the quick transfer starts from.
        [Test]
        public void ABoardReusedForAnotherAvatarStartsFromTheSameDefaults()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            using var other = new AttachmentConnectionTests.Fixture();
            foreach (var skin in other.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial.shader = Shader.Find("lilToon");
            var window = OpenNormal(other.Source);
            Board.GetField("automaticBlink", Flags).SetValue(window, false);
            ((AvatarLicenseOptions)Board.GetField("licenseOptions", Flags).GetValue(window)).CopyrightInformation = "Other avatar";
            Board.GetField("status", Flags).SetValue(window, "Other avatar");
            Assert.That(OpenNormal(fixture.Source), Is.SameAs(window));
            var hidden = ExperimentalExpressionCaptureWindow.PrepareHidden(fixture.Source);
            try
            {
                Assert.That(Defaults(window), Is.EqualTo(Defaults(hidden)));
            }
            finally { Object.DestroyImmediate(hidden); }
            var (quick, normal) = (ExportQuick(fixture.Source), ExportNormal(fixture.Source, window));
            AssertSame(quick, normal);
        }

        sealed class CountingLimitDialog : IPoseLimitDialog
        {
            internal int Asked;
            internal readonly List<string> OverFrameLimit = new List<string>();
            public bool Ask(PoseLimitPlan plan, GameObject avatar)
            {
                Asked++; OverFrameLimit.AddRange(plan.Rows.Where(row => row.OverFrameLimit).Select(row => row.Name));
                return true;
            }
        }

        // Moving poses that together exceed the app's 16MB of pose data.
        // "通常" asks once and the window starts from the default; "かんたん"
        // asks nothing. Both leave out the last pose and list it. A moving
        // pose over 8,192 frames is shown in the window and left out.
        [Test]
        public void PosesOverTheAppLimitMatch()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var assets = new List<string>();
            var previousDialog = PoseLimitChooser.Dialog;
            var limits = new CountingLimitDialog();
            try
            {
                var arm = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftUpperArm);
                var settings = new ExportSettings();
                for (var i = 0; i < 4; i++)
                {
                    // 40 seconds is 2,401 frames; three of them pass 16MB, two do not.
                    // 140 seconds is 8,401 frames, over one moving pose's 8,192.
                    var clip = new AnimationClip();
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(arm, fixture.Source.transform),
                        typeof(Transform), "localEulerAnglesRaw.z"), AnimationCurve.Linear(0, 0, i < 3 ? 40 : 140, 60 + i * 10));
                    var asset = "Assets/AppTransferPose-" + Guid.NewGuid().ToString("N") + ".anim";
                    AssetDatabase.CreateAsset(clip, asset); assets.Add(asset);
                    settings.Poses.Manual.Add(new ManualPose { Clip = clip, Name = "ポーズ " + (i + 1) });
                }
                ExportSettingsStore.Save(fixture.Source, settings);
                PoseLimitChooser.Dialog = limits;

                var quick = ExportQuick(fixture.Source);
                Assert.That(limits.Asked, Is.Zero, "The quick transfer asks nothing.");
                var normal = ExportNormal(fixture.Source, OpenNormal(fixture.Source));
                Assert.That(limits.Asked, Is.EqualTo(1), "The normal transfer asks once.");
                Assert.That(limits.OverFrameLimit, Is.EqualTo(new[] { "ポーズ 4" }), "The pose over 8,192 frames is shown, not an error.");
                AssertSame(quick, normal);
                Assert.That(Animations(Read(quick.Path).Json), Is.EqualTo(new[] { "ポーズ 1", "ポーズ 2" }));
                Assert.That(quick.ResultList, Does.Contain("ポーズ 3"), "The left-out pose is listed.");
            }
            finally
            {
                PoseLimitChooser.Dialog = previousDialog;
                foreach (var asset in assets) AssetDatabase.DeleteAsset(asset);
            }
        }

        // The Project window's context menu hands the prefab asset itself to both
        // transfers. They write what the same avatar in a scene writes, and leave
        // the open scene and the prefab file as they were.
        [Test]
        public void APrefabAssetMatchesTheSameAvatarInTheSceneAndStaysUnchanged()
        {
            using var fixture = new AppTransferQuickExportTests.SavedFixture();
            var folder = "Assets/VRVlogPrefabExport-" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            GameObject prefab = null;
            try
            {
                prefab = SaveAsPrefab(fixture.Source, folder);
                var prefabPath = AssetDatabase.GetAssetPath(prefab);
                var prefabText = File.ReadAllText(prefabPath);
                var scene = fixture.Source.scene;
                // The avatar in the scene now refers to the saved copies.
                Assert.That(EditorSceneManager.SaveScene(scene), Is.True);
                var roots = scene.GetRootGameObjects();
                Assert.That(scene.isDirty, Is.False);

                var fromScene = ExportQuick(fixture.Source);
                var (quick, normal) = ExportBoth(prefab);
                AssertSame(quick, normal);
                AssertSame(fromScene, quick);

                Assert.That(scene.isDirty, Is.False, "The open scene is not marked as changed.");
                Assert.That(scene.GetRootGameObjects(), Is.EqualTo(roots), "Nothing is left in the open scene.");
                Assert.That(EditorUtility.IsDirty(prefab), Is.False, "The prefab is not marked as changed.");
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllText(prefabPath), Is.EqualTo(prefabText), "The prefab file is unchanged.");
            }
            finally
            {
                // Saving through the board remembers the author for the prefab too.
                if (prefab != null) ExportAuthorTests.Forget(ExportAuthor.Identity(prefab));
                AssetDatabase.DeleteAsset(folder);
            }
        }

        // The fixture keeps its mesh, material and Avatar in memory; a prefab can
        // only refer to assets, so copies of them are saved next to it. The avatar
        // in the scene switches to the same copies (saving names them after their
        // files), so both refer to the same assets.
        static GameObject SaveAsPrefab(GameObject source, string folder)
        {
            var copies = new Dictionary<Object, Object>();
            T Saved<T>(T value, string extension) where T : Object
            {
                if (value == null) return null;
                if (!copies.TryGetValue(value, out var copy))
                {
                    copy = Object.Instantiate(value);
                    AssetDatabase.CreateAsset(copy, folder + "/" + copies.Count + extension);
                    copies.Add(value, copy);
                }
                return (T)copy;
            }
            var animator = source.GetComponent<Animator>();
            animator.avatar = Saved(animator.avatar, ".asset");
            foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                skin.sharedMesh = Saved(skin.sharedMesh, ".asset");
                skin.sharedMaterials = skin.sharedMaterials.Select(material => Saved(material, ".mat")).ToArray();
                // A renderer the editor never drew has no bounds yet, and reading them
                // later would write them into the prefab. Reading them now does what
                // drawing it once does for a prefab made in the editor.
                _ = skin.localBounds;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(source, folder + "/" + source.name + ".prefab", out var saved);
            Assert.That(saved, Is.True);
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(source), Is.False, "The avatar in the scene stays a plain object.");
            return prefab;
        }

        static string[] Animations(Dictionary<string, object> json) =>
            json.TryGetValue("extensions", out var extensions) && extensions is Dictionary<string, object> map &&
            map.TryGetValue(VRVlog.Poses.HumanoidAnimationData.Extension, out var poses) && poses is Dictionary<string, object> extension
                ? Items(extension, "animations").OfType<Dictionary<string, object>>().Select(item => item["name"] as string).ToArray()
                : Array.Empty<string>();

        // The board state each transfer starts from, as text for the comparison.
        static string[] Defaults(ExperimentalExpressionCaptureWindow window)
        {
            var session = (ExperimentalExpressionCaptureSession)Board.GetField("session", Flags).GetValue(window);
            var blink = (BlinkExportOptions)Board.GetMethod("ExportBlinkOptions", Flags).Invoke(window, null);
            return new[]
            {
                "transferMode " + window.TransferMode,
                "page " + window.Page,
                "automaticBlink " + Board.GetField("automaticBlink", Flags).GetValue(window),
                "status " + (Board.GetField("status", Flags).GetValue(window) ?? "(none)"),
                "license " + JsonUtility.ToJson(Board.GetField("licenseOptions", Flags).GetValue(window)),
                "blink " + (blink == null ? "(automatic)" : blink.Mode.ToString()),
                "excluded " + string.Join(",", session.ExcludedObjects.Select(item => item == null ? "(none)" : item.name)),
                "gimmicks " + session.GimmickOptions?.AutoExclude + " " + string.Join(",", (session.GimmickOptions?.IncludedObjects ?? Enumerable.Empty<GameObject>()).Select(item => item == null ? "(none)" : item.name)),
                "poses " + string.Join(",", session.PoseOptions.Excluded.OrderBy(item => item, StringComparer.Ordinal)),
                "presets " + string.Join(",", session.Presets.Keys.Select(key => key.ToString()).OrderBy(key => key, StringComparer.Ordinal)),
            };
        }

        // The quick transfer first: the normal one runs on the stores as the
        // quick one leaves them (the author and license it remembers).
        (Exported Quick, Exported Normal) ExportBoth(GameObject avatar)
        {
            var quick = ExportQuick(avatar);
            var normal = ExportNormal(avatar, OpenNormal(avatar));
            return (quick, normal);
        }

        Exported ExportQuick(GameObject avatar)
        {
            var result = new Exported { Path = NewPath("quick") };
            var transfer = Transfer(avatar);
            var asked = dialog.Texts.Count;
            var saved = 0;
            var logs = Capture(() => Assert.That(transfer.Export(avatar, "Avatar", result.Path, () => saved++), Is.Null));
            Assert.That(saved, Is.EqualTo(1));
            Assert.That(dialog.Texts.Count, Is.EqualTo(asked), "The quick transfer asks nothing.");
            result.ResultList = logs.LastOrDefault(log => log.StartsWith(ExporterLocalization.T("VR Vlog 書き出し結果"), StringComparison.Ordinal));
            result.Details = Details(logs);
            return result;
        }

        // Opens the board as "アプリに転送（通常）" does and lets it finish what it
        // queued, as the editor would between frames.
        static ExperimentalExpressionCaptureWindow OpenNormal(GameObject avatar)
        {
            Assert.That(ExperimentalExpressionCaptureWindow.OpenForTransfer(avatar), Is.True);
            var window = Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>().Single(item => item != null && item.TransferMode);
            var advance = Board.GetMethod("Advance", Flags);
            bool Busy() => Board.GetField("pending", Flags).GetValue(window) != null || (bool)Board.GetField("candidateValidationPending", Flags).GetValue(window);
            for (var step = 0; step < 20 && Busy(); step++) advance.Invoke(window, null);
            Assert.That(Busy(), Is.False, "The board finished opening.");
            Assert.That(Board.GetField("error", Flags).GetValue(window), Is.Null);
            Assert.That(Board.GetField("session", Flags).GetValue(window), Is.Not.Null);
            Assert.That(ExperimentalExpressionCaptureWindow.ForTransfer(avatar), Is.SameAs(window));
            return window;
        }

        Exported ExportNormal(GameObject avatar, ExperimentalExpressionCaptureWindow window)
        {
            var result = new Exported { Path = NewPath("normal") };
            try
            {
                var transfer = Transfer(avatar);
                var asked = dialog.Texts.Count;
                var saved = 0;
                var logs = Capture(() => Assert.That(transfer.ExportDetailed(avatar, "Avatar", result.Path, () => saved++), Is.Null));
                Assert.That(saved, Is.EqualTo(1));
                Assert.That(dialog.Texts.Count - asked, Is.LessThanOrEqualTo(1));
                result.ResultList = dialog.Texts.Count > asked ? dialog.Texts.Last() : null;
                result.Details = Details(logs);
            }
            finally { if (window != null) window.Close(); }
            return result;
        }

        static List<string> Capture(Action action)
        {
            var logs = new List<string>();
            void Received(string condition, string stackTrace, LogType type) => logs.Add(condition);
            Application.logMessageReceived += Received;
            try { action(); }
            finally { Application.logMessageReceived -= Received; }
            return logs;
        }

        static string[] Details(IEnumerable<string> logs)
        {
            var header = ExporterLocalization.T("VR Vlog 書き出し詳細\n・");
            return logs.Where(log => log.StartsWith(header, StringComparison.Ordinal))
                .SelectMany(log => log.Substring(header.Length).Split(new[] { "\n・" }, StringSplitOptions.None)).ToArray();
        }

        // What the two exports reported, without the destination. With a
        // result list, that list (it holds the detail lines too) is compared;
        // "通常" also logs the details then, which only repeats them. Without
        // one, the logged detail lines are compared. Other Console messages are
        // not: some are written once per editor session, so the export that
        // runs first would carry them alone.
        static string[] Warnings(Exported exported)
        {
            string Clean(string line) => line.Replace(exported.Path, "<destination>").Replace(Path.GetFileName(exported.Path), "<destination>").TrimEnd();
            var lines = exported.ResultList != null
                ? new[] { "[result list]" }.Concat(exported.ResultList.Split('\n').Select(Clean).Where(line => line.Length > 0).OrderBy(line => line, StringComparer.Ordinal))
                : new[] { "[details]" }.Concat(exported.Details.Select(Clean).OrderBy(line => line, StringComparer.Ordinal));
            return lines.ToArray();
        }

        static GlbDocument Read(string path) => GlbDocument.Read(File.ReadAllBytes(path));

        // The one comparison of the two transfers. The readable summaries come
        // first so a difference names itself; the whole JSON and the binary
        // chunk then catch everything else.
        static void AssertSame(Exported quick, Exported normal)
        {
            Assert.That(Warnings(normal), Is.EqualTo(Warnings(quick)), "What the export reported");
            var a = Read(quick.Path);
            var b = Read(normal.Path);
            foreach (var kind in new[] { "preset", "custom" })
                Assert.That(Expressions(b.Json, kind), Is.EqualTo(Expressions(a.Json, kind)), "Expressions (" + kind + ")");
            Assert.That(Meshes(b.Json), Is.EqualTo(Meshes(a.Json)), "Meshes");
            Assert.That(Names(b.Json, "materials"), Is.EqualTo(Names(a.Json, "materials")), "Materials");
            Assert.That(Canonical(b.Json), Is.EqualTo(Canonical(a.Json)), "glTF JSON");
            Assert.That(Sha256(b.Binary), Is.EqualTo(Sha256(a.Binary)), "Binary chunk");
        }

        // Parts that change on every export of the same avatar (see VOLATILE_SUB
        // in Tools/compare-exports.py): the random suffix of a baked emission
        // texture, and a random suffix after "$" in a name.
        static readonly Regex[] Volatile =
        {
            new Regex("(__VRVlog_Emission)_[0-9a-f]{32}$"),
            new Regex(@"(\$)[0-9a-f]{32}$"),
        };

        // The JSON with its keys in ordinal order and the volatile parts masked.
        static string Canonical(Dictionary<string, object> json) => JsonDom.Serialize(Canonical((object)json));

        static object Canonical(object value)
        {
            switch (value)
            {
                case Dictionary<string, object> map:
                    var sorted = new SortedDictionary<string, object>(StringComparer.Ordinal);
                    foreach (var pair in map) sorted[pair.Key] = Canonical(pair.Value);
                    return sorted;
                case List<object> list:
                    return list.Select(Canonical).ToList();
                case string text:
                    foreach (var pattern in Volatile) text = pattern.Replace(text, "$1_<random>");
                    return text;
                default:
                    return value;
            }
        }

        static string Sha256(byte[] bytes)
        {
            if (bytes == null) return "(none)";
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        static List<object> Items(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out var value) && value is List<object> list ? list : new List<object>();

        static string[] Names(Dictionary<string, object> json, string key) =>
            Items(json, key).Select(item => item is Dictionary<string, object> map && map.TryGetValue("name", out var name) ? name as string ?? "(none)" : "(none)").ToArray();

        // The names of the nodes that carry a mesh.
        static string[] MeshNodes(Dictionary<string, object> json) =>
            Items(json, "nodes").OfType<Dictionary<string, object>>().Where(node => node.ContainsKey("mesh"))
                .Select(node => node.TryGetValue("name", out var name) ? name as string ?? "(none)" : "(none)").ToArray();

        static Dictionary<string, object> VrmExpressions(Dictionary<string, object> json)
        {
            if (!json.TryGetValue("extensions", out var extensions) || !(extensions is Dictionary<string, object> map)) return new Dictionary<string, object>();
            if (!map.TryGetValue("VRMC_vrm", out var vrm) || !(vrm is Dictionary<string, object> vrmMap)) return new Dictionary<string, object>();
            return vrmMap.TryGetValue("expressions", out var expressions) && expressions is Dictionary<string, object> found ? found : new Dictionary<string, object>();
        }

        static int Count(Dictionary<string, object> expression, string key) =>
            expression.TryGetValue(key, out var value) && value is List<object> list ? list.Count : 0;

        // Each expression of the kind with its morph, material and texture binds.
        static string[] Expressions(Dictionary<string, object> json, string kind) =>
            VrmExpressions(json).TryGetValue(kind, out var group) && group is Dictionary<string, object> map
                ? map.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                {
                    var expression = pair.Value as Dictionary<string, object> ?? new Dictionary<string, object>();
                    return pair.Key + " morph=" + Count(expression, "morphTargetBinds") + " material=" + Count(expression, "materialColorBinds")
                        + " texture=" + Count(expression, "textureTransformBinds");
                }).ToArray()
                : Array.Empty<string>();

        static int MorphBinds(Dictionary<string, object> json, string preset) =>
            VrmExpressions(json).TryGetValue("preset", out var group) && group is Dictionary<string, object> map
                && map.TryGetValue(preset, out var value) && value is Dictionary<string, object> expression ? Count(expression, "morphTargetBinds") : 0;

        // The mesh, primitive and vertex counts.
        static string Meshes(Dictionary<string, object> json)
        {
            var accessors = Items(json, "accessors");
            var meshes = Items(json, "meshes");
            var primitives = 0;
            long vertices = 0;
            foreach (var mesh in meshes.OfType<Dictionary<string, object>>())
                foreach (var primitive in Items(mesh, "primitives").OfType<Dictionary<string, object>>())
                {
                    primitives++;
                    if (primitive.TryGetValue("attributes", out var attributes) && attributes is Dictionary<string, object> map
                        && map.TryGetValue("POSITION", out var index) && index is long accessor && accessor < accessors.Count
                        && accessors[(int)accessor] is Dictionary<string, object> found && found.TryGetValue("count", out var count) && count is long value)
                        vertices += value;
                }
            return "meshes=" + meshes.Count + " primitives=" + primitives + " vertices=" + vertices;
        }
    }
}
