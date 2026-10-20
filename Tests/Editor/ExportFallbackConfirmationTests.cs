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
        // Answers at once, as if the user pressed 保存する or 設定を変更する.
        sealed class RecordingDialog : IExportFallbackDialog
        {
            internal bool Answer;
            internal readonly List<ExportResultList> Calls = new List<ExportResultList>();
            public void Show(ExportResultList result, GameObject avatar, Action save, Action changeSettings, Action openBlinkSettings)
            {
                Calls.Add(result);
                if (Answer) save(); else changeSettings();
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
            var sources = new[] { ExportResultWindow.WindowTitle, ExportResultWindow.PendingTitle, ExportResultWindow.SavedTitle,
                    ExportResultWindow.SaveButton, ExportResultWindow.ChangeSettingsButton, ExportResultWindow.AllTab,
                    ExportResultWindow.CloseButton, ExportResultWindow.SearchPlaceholder, ExportResultWindow.EmptyList,
                    ExportResultWindow.NoMatch, ExportResultWindow.ReloadedList, ExportResultWindow.ReloadedPending,
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

        [TestCase(false, false)][TestCase(true, true)]
        public void BatchAndNonInteractiveCallersSaveWithoutTheList(bool interactive, bool batch)
        {
            ExportFallbackConfirmation.IsBatchMode = () => batch;
            var saved = 0;
            ExportFallbackConfirmation.Confirm(AllFallbacks(), null, "a.vrm", null, interactive, () => saved++);
            Assert.That(saved, Is.EqualTo(1));
            Assert.That(dialog.Calls, Is.Empty);
        }

        [Test] public void AnExportWithoutFallbacksNeverAsks()
        {
            var saved = 0;
            ExportFallbackConfirmation.Confirm(new ExportFallbackReport(), null, "a.vrm", null, interactive: true, () => saved++);
            ExportFallbackConfirmation.Confirm(null, null, "a.vrm", null, interactive: true, () => saved++);
            Assert.That(saved, Is.EqualTo(2));
            Assert.That(dialog.Calls, Is.Empty);
        }

        [Test] public void TheListShowsEveryFallbackWithItsTargets()
        {
            var report = AllFallbacks();
            foreach (var name in new[] { "A", "B", "C", "D" }) report.Add(ExportFallbackKind.ColliderSkipped, name);
            dialog.Answer = false;
            ExportFallbackConfirmation.Confirm(report, new[] { "別の知らせ" }, "C:/out/avatar.vrm", null, interactive: true, () => Assert.Fail("Not saved."));
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
            UniVrmOneClickExporter.RecordInferredEmotions(new[] { ("happy", "笑顔", VrmExpressionBindings.FilledFormat) }, recovery);
            var warning = string.Format(VrmExpressionBindings.FilledFormat, "happy", "笑顔");
            Assert.That(ExportFallbackConfirmation.NeedsAnswer(recovery.Fallbacks, interactive: true), Is.False,
                "Only kept output; nothing to decide before saving.");
            var list = ExportResultList.Build(recovery.Fallbacks, new[] { warning });
            var row = list.Rows.Single();
            Assert.That(row.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(row.Group, Is.EqualTo(UniVrmOneClickExporter.InferredEmotionCategory));
            Assert.That(row.TargetLabel, Is.EqualTo("happy"));
            Assert.That(row.Content, Does.Not.StartWith("happy:"), "The target column already names it.");
            Assert.That(list.NoticeCount, Is.Zero, "The warning with the same text is the kept line, not another notice.");
            Assert.That(list.Text(), Does.Contain("保持1").And.Contain("[" + UniVrmOneClickExporter.InferredEmotionCategory + "]"));
        }

        [TestCase(true)][TestCase(false)]
        public void TheAnswerDecidesBetweenSavingAndShowingWhatToChange(bool export)
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var collider = new GameObject("Collider_box").transform; collider.SetParent(fixture.Source.transform, false);
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "avatar.vrm");
            var bytes = LilToonGlbExtension.Inject(MaterialBindingFixture.Build(fixture.Material.name), fixture.Source, "0.11.9", "2.3.4");
            var calls = 0; var completed = 0; var blinkSettings = 0;
            dialog.Answer = export;
            Selection.objects = new Object[0];
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
                }, (output, warnings) => completed++, interactive: true, openBlinkSettings: () => blinkSettings++);
                Assert.That(recovery, Is.Null, "Neither answer is an export failure.");
                Assert.That(calls, Is.EqualTo(1), "The dialog reuses the finished export; it never runs it again.");
                Assert.That(dialog.Calls.Count, Is.EqualTo(1));
                Assert.That(dialog.Calls[0].Rows.Count(row => row.Outcome != null), Is.EqualTo(3));
                if (export)
                {
                    Assert.That(File.ReadAllBytes(destination), Is.EqualTo(bytes));
                    Assert.That(completed, Is.EqualTo(1));
                    Assert.That(blinkSettings, Is.Zero);
                    Assert.That(Selection.objects, Is.Empty);
                }
                else
                {
                    Assert.That(Directory.GetFileSystemEntries(directory), Is.Empty, "Nothing is written when the user goes back to the settings.");
                    Assert.That(completed, Is.Zero);
                    Assert.That(blinkSettings, Is.EqualTo(1));
                    Assert.That(Selection.objects, Is.EquivalentTo(new Object[] { collider.gameObject, fixture.Renderer.gameObject }));
                }
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }

        [Test] public void NonInteractiveSavingKeepsTheEarlierBehaviour()
        {
            using var fixture = new ExportRecoveryTests.RecoveryFixture();
            var directory = Path.Combine(Path.GetTempPath(), "vrvlog-fallback-" + Guid.NewGuid().ToString("N"));
            var destination = Path.Combine(directory, "avatar.vrm");
            var bytes = LilToonGlbExtension.Inject(MaterialBindingFixture.Build(fixture.Material.name), fixture.Source, "0.11.9", "2.3.4");
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
    }
}
