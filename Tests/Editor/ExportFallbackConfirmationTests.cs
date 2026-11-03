using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportFallbackConfirmationTests
    {
        // Records each result list opened.
        sealed class RecordingDialog : IExportFallbackDialog
        {
            internal readonly List<ExportResultList> Calls = new List<ExportResultList>();
            internal readonly List<string> Headings = new List<string>();
            internal readonly List<string> Notes = new List<string>();
            internal readonly List<ExportDiagnostics> Diagnostics = new List<ExportDiagnostics>();
            internal readonly List<Action> BlinkSettings = new List<Action>();
            internal Action Shown;
            public void Show(ExportResultList result, GameObject avatar, string heading, string note, ExportDiagnostics diagnostics, Action openBlinkSettings)
            {
                Calls.Add(result);
                Headings.Add(heading);
                Notes.Add(note);
                Diagnostics.Add(diagnostics);
                BlinkSettings.Add(openBlinkSettings);
                Shown?.Invoke();
            }
        }

        IExportFallbackDialog originalDialog;
        Func<bool> originalBatchMode;
        Object[] originalSelection;
        RecordingDialog dialog;

        [SetUp] public void SetUp()
        {
            originalDialog = ExportFallbackConfirmation.Dialog;
            originalBatchMode = ExportFallbackConfirmation.IsBatchMode;
            originalSelection = Selection.objects;
            ExportFallbackConfirmation.Dialog = dialog = new RecordingDialog();
            // The test runner itself is a batch run; pretend an Editor session.
            ExportFallbackConfirmation.IsBatchMode = () => false;
            // The wording assertions compare the Japanese source text, whatever
            // language the machine running the tests uses.
            originalLocale = LocaleField.GetValue(null);
            LocaleField.SetValue(null, "ja");
        }

        [TearDown] public void TearDown()
        {
            ExportFallbackConfirmation.Dialog = originalDialog;
            ExportFallbackConfirmation.IsBatchMode = originalBatchMode;
            Selection.objects = originalSelection;
            LocaleField.SetValue(null, originalLocale);
        }

        static readonly System.Reflection.FieldInfo LocaleField = typeof(ExporterLocalization).GetField("_locale",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        object originalLocale;

        static ExportFallbackReport AllFallbacks(Transform root = null, Transform collider = null, Transform renderer = null)
        {
            var report = new ExportFallbackReport();
            report.Add(ExportFallbackKind.NonLilToonMaterials, null, root, renderer);
            report.Add(ExportFallbackKind.ColliderSkipped, "Collider_box", root, collider);
            report.Add(ExportFallbackKind.Blink);
            return report;
        }

        [Test] public void EveryShippedLocaleTranslatesTheResultWords()
        {
            var sources = new[] { ExportResultWindow.WindowTitle, ExportResultWindow.SavedTitle,
                    ExportResultWindow.WarningsTitleFormat,
                    ExportResultWindow.AllTab,
                    ExportResultWindow.CloseButton, ExportResultWindow.SearchPlaceholder, ExportResultWindow.EmptyList,
                    ExportResultWindow.NoMatch, ExportResultWindow.ReloadedList,
                    ExportResultWindow.SelectButton, ExportResultWindow.MissingTarget, ExportResultWindow.BlinkButton,
                    ExportResultList.CountFormat, ExportResultList.NoticeGroup,
                    ExportResultList.StageOmittedContent, ExportResultList.StageApproximatedContent }
                .Concat(ExportResultList.Order.Select(outcome => ExportResultList.Label(outcome)))
                .Concat(((ExportPhase[])Enum.GetValues(typeof(ExportPhase))).Select(ExportStageGuard.PhaseLabel))
                .Concat(new[] { "基準形評価" })
                .Concat(((ExportFallbackKind[])Enum.GetValues(typeof(ExportFallbackKind))).SelectMany(kind =>
                    new[] { ExportFallbackConfirmation.Failure(kind), ExportFallbackConfirmation.Feature(kind) })).Distinct().ToArray();
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var source in sources)
                    Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }

        [Test] public void AStageIsNamedWithItsPhase()
        {
            Assert.That(ExportStageGuard.DisplayName(ExportStage.BlinkCapture), Is.EqualTo("表情: 瞬き"));
            Assert.That(ExportStageGuard.DisplayName(ExportStage.PhysBone), Is.EqualTo("揺れ物"));
        }

        [Test] public void BatchModeSavesWithoutTheList()
        {
            ExportFallbackConfirmation.IsBatchMode = () => true;
            Assert.That(ExportFallbackConfirmation.ResultOpener(AllFallbacks(), null, "a.vrm", null, interactive: true), Is.Null);
        }

        [Test] public void AnExportWithoutFallbacksOpensNoList()
        {
            Assert.That(ExportFallbackConfirmation.ResultOpener(new ExportFallbackReport(), null, "a.vrm", null, interactive: true), Is.Null);
            Assert.That(ExportFallbackConfirmation.ResultOpener(null, null, "a.vrm", null, interactive: true), Is.Null);
            Assert.That(ExportFallbackConfirmation.ResultOpener(AllFallbacks(), null, "a.vrm", null, interactive: false), Is.Null);
        }

        [Test] public void TheListShowsEveryFallbackWithItsTargets()
        {
            var report = AllFallbacks();
            foreach (var name in new[] { "A", "B", "C", "D" }) report.Add(ExportFallbackKind.ColliderSkipped, name);
            ExportFallbackConfirmation.ResultOpener(report, new[] { "別の知らせ" }, "C:/out/avatar.vrm", null, interactive: true)(ExportResultWindow.SavedTitle, null);
            var list = dialog.Calls.Single();
            Assert.That(list.FileName, Is.EqualTo("avatar.vrm"));
            var collider = list.Rows.Single(row => row.Content == "PhysBoneコライダーを変換できませんでした");
            Assert.That(collider.Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(collider.Targets.Select(target => target.Name), Is.EqualTo(new[] { "Collider_box", "A", "B", "C", "D" }),
                "All five names are kept; nothing is cut at two.");
            Assert.That(collider.TargetLabel, Is.EqualTo("5件"));
            Assert.That(list.Rows.Single(row => row.Outcome == null).Content, Is.EqualTo("別の知らせ"));

            // Default reasons are stored as Japanese source text and must follow the Editor language.
            var messages = typeof(ExporterLocalization).GetField("_messages",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            try
            {
                LocaleField.SetValue(null, "en");
                messages.SetValue(null, null);
                var english = ExportResultList.Build(report, null).Rows.Select(row => row.Content).ToArray();
                Assert.That(english, Has.No.Member("PhysBoneコライダーを変換できませんでした"));
                Assert.That(english, Has.Member(ExporterLocalization.T("PhysBoneコライダーを変換できませんでした")));
            }
            finally { messages.SetValue(null, null); }
        }

        [Test] public void KeptItemsAreListedOnceWithoutAsking()
        {
            var recovery = new ExportRecoveryReport();
            UniVrmOneClickExporter.RecordInferredPresets(new[] { ("happy", "笑顔", VrmExpressionBindings.FilledFormat) }, recovery);
            var warning = string.Format(VrmExpressionBindings.FilledFormat, "happy", "笑顔");
            Assert.That(ExportFallbackConfirmation.ShowsResult(recovery.Fallbacks, interactive: true), Is.False,
                "Only kept output; nothing to decide before saving.");
            var list = ExportResultList.Build(recovery.Fallbacks, new[] { warning });
            var row = list.Rows.Single();
            Assert.That(row.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(row.Group, Is.EqualTo(UniVrmOneClickExporter.InferredPresetCategory));
            Assert.That(row.TargetLabel, Is.EqualTo("happy"));
            Assert.That(row.Content, Does.Not.StartWith("happy:"), "The target column already names it.");
            Assert.That(list.NoticeCount, Is.Zero, "The warning with the same text is the kept line, not another notice.");
            Assert.That(list.Text(), Does.Contain("保持1").And.Contain("[" + UniVrmOneClickExporter.InferredPresetCategory + "]"));
        }

        // The list opens once the file is written: the file is already there when
        // it shows, and nothing is asked before saving.
        [Test] public void TheListOpensAfterTheFileIsWritten()
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var collider = new GameObject("Collider_box").transform; collider.SetParent(fixture.Source.transform, false);
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "avatar.vrm");
            var bytes = LilToonGlbExtension.MarkWithoutLilToon(MaterialBindingFixture.Build(fixture.Material.name));
            var calls = 0; var blinkSettings = 0;
            var order = new List<string>();
            dialog.Shown = () => order.Add(File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(bytes) ? "list after file" : "list before file");
            try
            {
                Directory.CreateDirectory(directory);
                var recovery = LilToonExporterWindow.ExportAndSaveNormally(fixture.Source, destination, (options, report, warnings) =>
                {
                    calls++;
                    // What the exporter records while it works around each case.
                    report.Fallbacks.Add(ExportFallbackKind.Blink);
                    report.Fallbacks.Add(ExportFallbackKind.ColliderSkipped, collider.name, fixture.Source.transform, collider);
                    report.Fallbacks.Add(ExportFallbackKind.NonLilToonMaterials, null, fixture.Source.transform, fixture.Renderer.transform);
                    return bytes;
                }, (output, warnings) => order.Add("completed"), interactive: true, openBlinkSettings: () => blinkSettings++);
                Assert.That(recovery, Is.Null);
                Assert.That(calls, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(bytes));
                Assert.That(order, Is.EqualTo(new[] { "completed", "list after file" }), "The list opens after the save has finished.");
                Assert.That(dialog.Calls.Single().Rows.Count(row => row.Outcome != null), Is.EqualTo(3));
                Assert.That(dialog.Calls.Single().FileName, Is.EqualTo("avatar.vrm"));
                Assert.That(dialog.Headings.Single(), Is.EqualTo(ExportResultWindow.SavedTitle));
                Assert.That(dialog.Diagnostics.Single(), Is.SameAs(ExportDiagnostics.Last));
                // Opening the blink settings is left to the button in the list.
                Assert.That(blinkSettings, Is.Zero);
                dialog.BlinkSettings.Single()();
                Assert.That(blinkSettings, Is.EqualTo(1));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        // A transfer gets the list before it starts and opens it when it has ended.
        // The file is the transfer's own copy, so the list names no file.
        [Test] public void ATransferOpensTheListWhenItHasEnded()
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "0123abcd.vrm");
            var bytes = LilToonGlbExtension.MarkWithoutLilToon(MaterialBindingFixture.Build(fixture.Material.name));
            Action<string, string> opener = null;
            var handed = 0; var openerAtCompleted = false;
            try
            {
                Directory.CreateDirectory(directory);
                LilToonExporterWindow.ExportAndSaveNormally(fixture.Source, destination, (options, report, warnings) =>
                {
                    report.Fallbacks.Add(ExportFallbackKind.Blink);
                    return bytes;
                }, (output, warnings) => openerAtCompleted = opener != null, interactive: true,
                    resultAfterTransfer: open => { handed++; opener = open; });
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(bytes));
                Assert.That(handed, Is.EqualTo(1));
                Assert.That(openerAtCompleted, Is.True, "The transfer has the list before it starts.");
                Assert.That(dialog.Calls, Is.Empty, "Nothing opens while the transfer runs.");
                opener("アプリに取り込みました", null);
                Assert.That(dialog.Headings.Single(), Is.EqualTo("アプリに取り込みました"));
                Assert.That(dialog.Calls.Single().FileName, Is.Null.Or.Empty);
                // A failed transfer names its reason beside the heading, on one line.
                opener("転送できませんでした", "通信が切れました");
                Assert.That(dialog.Headings.Last(), Is.EqualTo("転送できませんでした"));
                Assert.That(dialog.Notes.Last(), Is.EqualTo("通信が切れました"));
                Assert.That(ExportResultWindow.Note("通信が切れました\nもう一度お試しください\r\n"), Is.EqualTo("通信が切れました もう一度お試しください"));
                Assert.That(ExportResultWindow.Note(null), Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test] public void AFailedWriteOpensNoList()
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "avatar.vrm");
            var bytes = LilToonGlbExtension.MarkWithoutLilToon(MaterialBindingFixture.Build(fixture.Material.name));
            try
            {
                // A folder in the way: the write fails, and the error goes to the caller as before.
                Directory.CreateDirectory(destination);
                Assert.Catch<IOException>(() => LilToonExporterWindow.ExportAndSaveNormally(fixture.Source, destination, (options, report, warnings) =>
                {
                    report.Fallbacks.Add(ExportFallbackKind.Blink);
                    return bytes;
                }, interactive: true));
                Assert.That(dialog.Calls, Is.Empty);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test] public void NonInteractiveSavingKeepsTheEarlierBehaviour()
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "avatar.vrm");
            var bytes = LilToonGlbExtension.MarkWithoutLilToon(MaterialBindingFixture.Build(fixture.Material.name));
            var completed = 0;
            try
            {
                Directory.CreateDirectory(directory);
                LilToonExporterWindow.ExportAndSaveNormally(fixture.Source, destination, (options, report, warnings) =>
                {
                    report.Fallbacks.Add(ExportFallbackKind.Blink);
                    return bytes;
                }, (output, warnings) => completed++);
                Assert.That(dialog.Calls, Is.Empty);
                Assert.That(completed, Is.EqualTo(1));
                Assert.That(File.ReadAllBytes(destination), Is.EqualTo(bytes));
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test] public void TargetsAreFoundAgainOnTheAvatarByPath()
        {
            var avatar = new GameObject("Avatar"); var copy = Object.Instantiate(avatar);
            try
            {
                Transform Child(Transform parent, string name) { var child = new GameObject(name).transform; child.SetParent(parent, false); return child; }
                var body = Child(Child(avatar.transform, "Armature"), "Body");
                var copied = Child(Child(copy.transform, "Armature"), "Body");
                var generated = Child(copy.transform, "Generated by a build tool");
                var report = new ExportFallbackReport();
                report.Add(ExportFallbackKind.NonLilToonMaterials, null, copy.transform, copied);
                report.Add(ExportFallbackKind.NonLilToonMaterials, null, copy.transform, copied);
                Assert.That(report.Items.Single().Paths, Is.EqualTo(new[] { "Armature/Body" }));
                Assert.That(ExportFallbackConfirmation.Targets(report, avatar), Is.EqualTo(new[] { body.gameObject }));
                var missing = new ExportFallbackReport();
                missing.Add(ExportFallbackKind.ColliderSkipped, "Generated", copy.transform, generated);
                Assert.That(ExportFallbackConfirmation.Targets(missing, avatar), Is.EqualTo(new[] { avatar }),
                    "An object that exists only on the export copy falls back to the avatar.");
                var root = new ExportFallbackReport();
                root.Add(ExportFallbackKind.ColliderSkipped, "Root", copy.transform, copy.transform);
                Assert.That(ExportFallbackConfirmation.Targets(root, avatar), Is.EqualTo(new[] { avatar }));
                Assert.That(ExportFallbackConfirmation.Targets(root, null), Is.Empty);
            }
            finally { Object.DestroyImmediate(copy); Object.DestroyImmediate(avatar); }
        }

        [Test] public void ARealExportReportsBlinkAndMaterialFallbacksOnce()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var plain = new Material(Shader.Find("Standard")) { name = "Plain" };
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = plain;
                var report = new ExportRecoveryReport();
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Fallback report", "Tests", warnings,
                    exporterVersion: "0.11.15", lilToonVersion: "2.3.4", recoveryReport: report);
                Assert.That(bytes, Is.Not.Empty);
                // An unsupported shader is now approximated by official lilToon
                // instead of falling back to a flat MToon material.
                Assert.That(report.Fallbacks.Items.Select(item => item.Kind), Is.EquivalentTo(
                    new[] { ExportFallbackKind.Blink, ExportFallbackKind.ShaderSubstituted }));
                Assert.That(ExportFallbackConfirmation.Targets(report.Fallbacks, fixture.Source), Is.EquivalentTo(
                    fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().Select(skin => skin.gameObject)));
                Assert.That(warnings, Has.Member(ExporterLocalization.T(UnsupportedShaderSubstitution.Warning) + "Plain"));
                Assert.That(warnings.Any(warning => warning.StartsWith(ExporterLocalization.T(UniVrmOneClickExporter.NonLilToonWarning), StringComparison.Ordinal)), Is.False);
                Assert.That(warnings, Has.Member(ExporterLocalization.T(BlinkExportSession.MissingAutomaticWarning)));
                Assert.That(dialog.Calls, Is.Empty, "Exporting itself never opens a dialog.");
            }
            finally { Object.DestroyImmediate(plain); }
        }

        [Test] public void AMaterialWithAMissingShaderIsFoundOnceAndOtherSlotsAreIgnored()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(shader, Is.Not.Null);
            var root = new GameObject("Avatar");
            var missing = new Material(shader) { name = "Missing" };
            var plain = new Material(Shader.Find("Standard")) { name = "Plain" };
            var hidden = new Material(shader) { name = "Hidden" };
            try
            {
                GameObject Cube(string name, params Material[] materials)
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = name;
                    cube.transform.SetParent(root.transform, false);
                    cube.GetComponent<MeshRenderer>().sharedMaterials = materials;
                    return cube;
                }
                var first = Cube("First", missing, plain, null);
                Cube("Second", missing);
                // Inactive objects are not exported, so their materials are not listed.
                Cube("Inactive", hidden).SetActive(false);
                var found = UniVrmOneClickExporter.MissingShaderMaterials(root);
                Assert.That(found.Count, Is.EqualTo(1));
                Assert.That(found[0].Material, Is.SameAs(missing));
                Assert.That(found[0].Renderer, Is.SameAs(first.GetComponent<MeshRenderer>()));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(missing);
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(hidden);
            }
        }

        [Test] public void ARealExportWithAMissingShaderSavesAndWarnsOnce()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(shader, Is.Not.Null);
            using var fixture = new AttachmentConnectionTests.Fixture();
            var missing = new Material(shader) { name = "Missing" };
            try
            {
                fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()[0].sharedMaterial = missing;
                var report = new ExportRecoveryReport();
                // A warning the caller already has stays once, after the summary.
                var warnings = new List<string> { "Earlier" };
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Missing shader", "Tests", warnings,
                    exporterVersion: "0.11.15", lilToonVersion: "2.3.4", recoveryReport: report);
                // The export is not blocked; the VRM is still written.
                Assert.That(bytes, Is.Not.Empty);
                var items = report.Fallbacks.Items.Where(item => item.Kind == ExportFallbackKind.MissingShader).ToArray();
                Assert.That(items.Length, Is.EqualTo(1));
                Assert.That(items[0].Names, Is.EqualTo(new[] { "Missing" }));
                var summary = string.Format(ExporterLocalization.T(UniVrmOneClickExporter.MissingShaderWarningFormat), 1) + "Missing";
                Assert.That(warnings.Count(warning => warning == summary), Is.EqualTo(1));
                Assert.That(warnings[0], Is.EqualTo(summary), "The summary comes before the other warnings.");
                Assert.That(warnings.Count(warning => warning == "Earlier"), Is.EqualTo(1));
                var result = ExportResultList.Build(report.Fallbacks, warnings, "a.vrm");
                Assert.That(result.MissingShaderCount, Is.EqualTo(1));
                Assert.That(result.Rows.Any(row => row.Content == ExportFallbackConfirmation.MissingShaderReason &&
                    row.Targets.Any(target => target.Name == "Missing")), Is.True);
            }
            finally { Object.DestroyImmediate(missing); }
        }

        [Test] public void TheSavedTitleWarnsOnlyWhenAShaderIsMissing()
        {
            var clean = ExportResultList.Build(new ExportFallbackReport(), null);
            var report = new ExportFallbackReport();
            report.Add(ExportFallbackKind.MissingShader, "Missing");
            report.MissingShaderCount = 1;
            var missing = ExportResultList.Build(report, null);
            Assert.That(missing.MissingShaderCount, Is.EqualTo(1));
            Assert.That(ExportResultList.Build(null, null).MissingShaderCount, Is.Zero);
            // A transfer names how it ended; a missing shader is marked the same way.
            Assert.That(ExportResultWindow.Title(ExportResultWindow.SavedTitle, clean), Is.EqualTo("VRMを保存しました"));
            Assert.That(ExportResultWindow.Title(ExportResultWindow.SavedTitle, null), Is.EqualTo("VRMを保存しました"));
            Assert.That(ExportResultWindow.Title(ExportResultWindow.SavedTitle, missing), Is.EqualTo("VRMを保存しました（警告あり）"));
            Assert.That(ExportResultWindow.Title("アプリに取り込みました", missing), Is.EqualTo("アプリに取り込みました（警告あり）"));
            Assert.That(ExportResultWindow.Title("転送できませんでした", clean), Is.EqualTo("転送できませんでした"));
        }

        [Test] public void AnExcludedRendererWithAMissingShaderIsNotCounted()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(shader, Is.Not.Null);
            using var fixture = new AttachmentConnectionTests.Fixture();
            var missing = new Material(shader) { name = "Missing" };
            var keptMaterial = new Material(shader) { name = "Kept" };
            try
            {
                // The fixture has no lilToon material, so the mesh that stays also carries one; a copy of it
                // carries the material that is left out.
                var original = fixture.Source.transform.Find("Back");
                original.GetComponent<SkinnedMeshRenderer>().sharedMaterial = keptMaterial;
                var back = Object.Instantiate(original.gameObject, original.parent).transform;
                back.name = "Extra";
                back.GetComponent<SkinnedMeshRenderer>().sharedMaterial = missing;
                ExportResultList Export(IEnumerable<GameObject> excluded, out List<string> warnings)
                {
                    var report = new ExportRecoveryReport();
                    warnings = new List<string>();
                    var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Missing shader", "Tests", warnings,
                        exporterVersion: "0.11.15", lilToonVersion: "2.3.4", excludedObjects: excluded, recoveryReport: report);
                    Assert.That(bytes, Is.Not.Empty);
                    Assert.That(ExportResultList.MissingShaders(report.Fallbacks), Is.EqualTo(report.Fallbacks.MissingShaderCount));
                    return ExportResultList.Build(report.Fallbacks, warnings, "a.vrm");
                }
                // Left out with 書き出さないオブジェクト: the material never reaches the VRM.
                var excluded = Export(new[] { back.gameObject }, out var excludedWarnings);
                Assert.That(excluded.MissingShaderCount, Is.EqualTo(1));
                Assert.That(excludedWarnings[0], Is.EqualTo(string.Format(ExporterLocalization.T(UniVrmOneClickExporter.MissingShaderWarningFormat), 1) + "Kept"));
                // The same renderer kept in the export is counted.
                var kept = Export(null, out var keptWarnings);
                Assert.That(kept.MissingShaderCount, Is.EqualTo(2));
                Assert.That(ExportResultWindow.Title(ExportResultWindow.SavedTitle, kept), Is.EqualTo("VRMを保存しました（警告あり）"));
                Assert.That(keptWarnings[0], Does.StartWith(string.Format(ExporterLocalization.T(UniVrmOneClickExporter.MissingShaderWarningFormat), 2)).And.Contain("Missing"));
            }
            finally { Object.DestroyImmediate(missing); Object.DestroyImmediate(keptMaterial); }
        }

        [Test] public void TwoMaterialsWithTheSameNameCountAsTwo()
        {
            var shader = Shader.Find("Hidden/InternalErrorShader");
            Assume.That(shader, Is.Not.Null);
            using var fixture = new AttachmentConnectionTests.Fixture();
            var first = new Material(shader) { name = "Material" };
            var second = new Material(shader) { name = "Material" };
            try
            {
                fixture.Source.transform.Find("Front").GetComponent<SkinnedMeshRenderer>().sharedMaterial = first;
                fixture.Source.transform.Find("Back").GetComponent<SkinnedMeshRenderer>().sharedMaterial = second;
                var report = new ExportRecoveryReport();
                var warnings = new List<string>();
                var bytes = UniVrmOneClickExporter.Export(fixture.Source, "Missing shader", "Tests", warnings,
                    exporterVersion: "0.11.15", lilToonVersion: "2.3.4", recoveryReport: report);
                Assert.That(bytes, Is.Not.Empty);
                Assert.That(ExportResultList.MissingShaders(report.Fallbacks), Is.EqualTo(2));
                Assert.That(ExportResultList.Build(report.Fallbacks, warnings, "a.vrm").MissingShaderCount, Is.EqualTo(2));
                Assert.That(warnings[0], Is.EqualTo(string.Format(ExporterLocalization.T(UniVrmOneClickExporter.MissingShaderWarningFormat), 2) + "Material, Material"));
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
