using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.Poses;

namespace VRVlog.LilToonExporter.Tests
{
    // Which bundled poses an export keeps over the app's limits, and when the
    // normal export asks about them.
    public sealed class PoseLimitPlanTests
    {
        static readonly double[] Identity = { 0, 0, 0, 1 };

        static PoseCandidate Still(string id, int bones = 1, string name = null)
        {
            var data = new HumanoidPoseData { Id = id, Name = name ?? id, Category = "", Source = "手動" };
            foreach (var bone in HumanoidPoseData.BoneNames.Take(bones).Select((value, index) => (value, index)))
                data.Bones.Add(new HumanoidPoseData.Bone { Name = bone.value, Node = bone.index, Rotation = (double[])Identity.Clone() });
            return new PoseCandidate { Id = id, Name = data.Name, Source = "手動", Data = data };
        }

        internal static PoseCandidate Moving(string id, int frames, int bones = 1)
        {
            var data = new HumanoidAnimationData { Id = id, Name = id, Category = "", Source = "手動" };
            foreach (var bone in HumanoidAnimationData.BoneNames.Take(bones).Select((value, index) => (value, index)))
                data.Bones.Add(new HumanoidAnimationData.Bone { Name = bone.value, Node = bone.index });
            SetFrames(data, frames);
            return new PoseCandidate { Id = id, Name = id, Source = "手動", IsAnimation = true, Animation = data };
        }

        static void SetFrames(HumanoidAnimationData data, int frames)
        {
            data.Frames.Clear();
            for (var i = 0; i < frames; i++)
                data.Frames.Add(new HumanoidAnimationData.Frame { Time = i / 60.0,
                    Rotations = data.Bones.Select(_ => (double[])Identity.Clone()).ToArray() });
            data.Duration = (frames - 1) / 60.0;
        }

        static string[] Dropped(PoseLimitPlan plan) => plan.Rows.Where(row => !row.Keep).Select(row => row.Id).ToArray();

        [Test]
        public void WithinTheLimitsNothingIsDroppedWhateverWasSaved()
        {
            var plan = new PoseLimitPlan(new[] { Still("a"), Still("b"), Moving("c", 10) });
            Assert.That(plan.NeedsChoice, Is.False);
            plan.Choose(new[] { "a", "c" });
            Assert.That(Dropped(plan), Is.Empty);
            Assert.That(plan.Within, Is.True);
        }

        [Test]
        public void OverTheCountTheDefaultDropsTheLastRows()
        {
            var candidates = Enumerable.Range(0, 130).Select(i => Still("pose" + i)).ToArray();
            var plan = new PoseLimitPlan(candidates);
            Assert.That(plan.NeedsChoice && plan.CountOver, Is.True);
            plan.Default();
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "pose128", "pose129" }));
            Assert.That(plan.Within && plan.IsDefault, Is.True);
            // The same poses give the same choice every time.
            var again = new PoseLimitPlan(candidates); again.Default();
            Assert.That(Dropped(again), Is.EqualTo(Dropped(plan)));
        }

        [Test]
        public void OverTheFramesTheDefaultDropsMovingPosesOnly()
        {
            // No bones keeps the data small, so only the frame total is over.
            var plan = new PoseLimitPlan(new[] { Moving("m0", 8000, 0), Moving("m1", 8000, 0), Still("s0"), Moving("m2", 8000, 0),
                Moving("m3", 8000, 0), Moving("m4", 1000, 0), Still("s1") });
            Assert.That(plan.FramesOver, Is.True);
            Assert.That(plan.AnimationBytesOver, Is.False);
            plan.Default();
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "m4" }));
            Assert.That(plan.Within, Is.True);
        }

        [Test]
        public void OverTheMovingPoseDataTheDefaultDropsFromTheEnd()
        {
            var plan = new PoseLimitPlan(new[] { Moving("m0", 3000, 20), Moving("m1", 3000, 20), Moving("m2", 3000, 20), Still("s0") });
            Assert.That(plan.AnimationBytesOver, Is.True);
            Assert.That(plan.FramesOver, Is.False);
            plan.Default();
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "m1", "m2" }), "The still pose stays.");
            Assert.That(plan.Within, Is.True);
        }

        [Test]
        public void OverTheStillPoseDataTheDefaultDropsStillPosesOnly()
        {
            var candidates = new List<PoseCandidate> { Moving("m0", 10) };
            candidates.AddRange(Enumerable.Range(0, 80).Select(i => Still("s" + i, HumanoidPoseData.BoneNames.Length, new string('x', 200))));
            var plan = new PoseLimitPlan(candidates);
            Assert.That(plan.PoseBytesOver, Is.True);
            Assert.That(plan.CountOver, Is.False);
            plan.Default();
            var dropped = Dropped(plan);
            Assert.That(dropped, Is.Not.Empty);
            Assert.That(dropped, Is.EqualTo(Enumerable.Range(80 - dropped.Length, dropped.Length).Select(i => "s" + i)));
            Assert.That(plan.Within, Is.True);
        }

        [Test]
        public void ASavedChoiceIsToppedUpFromTheEnd()
        {
            var plan = new PoseLimitPlan(Enumerable.Range(0, 131).Select(i => Still("pose" + i)));
            // An id no longer listed is ignored.
            plan.Choose(new[] { "pose0", "gone" });
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "pose0", "pose129", "pose130" }));
            Assert.That(plan.Within, Is.True);
            Assert.That(plan.IsDefault, Is.False);
            plan.Choose(new[] { "pose0", "pose1", "pose2" });
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "pose0", "pose1", "pose2" }), "Enough was dropped already.");
        }

        [Test]
        public void AMovingPoseOverTheDataLimitAloneIsLeftOutAndAsked()
        {
            var plan = new PoseLimitPlan(new[] { Still("s0"), Moving("big", 8000, 20), Moving("m0", 10) });
            var big = plan.Rows.Single(row => row.Id == "big");
            Assert.That(big.TooLarge && !big.Selectable && !big.Keep, Is.True);
            Assert.That(plan.Within, Is.True);
            Assert.That(plan.NeedsChoice, Is.True, "Losing a pose is asked about even when the rest fits.");
            Assert.That(plan.Exceeds, Is.False);
            // The rest fits, so a choice saved for more poses drops nothing.
            plan.Choose(new[] { "m0" });
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "big" }));
            Assert.That(plan.Dropped, Is.Empty, "Only choices are remembered.");
            Assert.That(plan.LeftOut.Select(row => row.Id), Is.EqualTo(new[] { "big" }));
            Assert.That(plan.IsDefault, Is.True);
        }

        [Test]
        public void ASavedChoiceStillAppliesWhenTheRestIsOverWithATooLargePose()
        {
            var candidates = new List<PoseCandidate> { Moving("big", 8000, 20) };
            candidates.AddRange(Enumerable.Range(0, 130).Select(i => Still("pose" + i)));
            var plan = new PoseLimitPlan(candidates);
            Assert.That(plan.Exceeds && plan.CountOver, Is.True);
            plan.Choose(new[] { "pose0", "pose1" });
            Assert.That(Dropped(plan), Is.EqualTo(new[] { "big", "pose0", "pose1" }));
            Assert.That(plan.Within, Is.True);
        }

        [Test]
        public void TheWindowShowsTheNoticeOnlyWhenOverAndComparesWithItsDefaults()
        {
            var plan = new PoseLimitPlan(Enumerable.Range(0, 130).Select(i => Still("pose" + i)));
            plan.Choose(new[] { "pose0" });
            var window = ScriptableObject.CreateInstance<PoseLimitWindow>();
            try
            {
                window.Open(plan, "avatar");
                Assert.That(Dropped(plan), Is.EqualTo(new[] { "pose0", "pose129" }), "Opening keeps the saved choice.");
                Assert.That(window.Choosable, Is.True);
                Assert.That(window.AtDefault, Is.False);
                Assert.That(window.Notice, Is.Empty, "Within the limits the heading says nothing is over.");
                plan.Rows.Single(row => row.Id == "pose0").Keep = true;
                Assert.That(window.Notice, Is.EqualTo(ExporterLocalization.T(PoseLimitWindow.OverLimit)));
                plan.Default();
                Assert.That(window.AtDefault, Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void TheWindowForATooLargePoseAloneHasNothingToChoose()
        {
            var plan = new PoseLimitPlan(new[] { Still("s0"), Moving("big", 8000, 20) });
            plan.Choose(new[] { "s0" });
            var window = ScriptableObject.CreateInstance<PoseLimitWindow>();
            try
            {
                window.Open(plan, "avatar");
                Assert.That(window.Choosable, Is.False);
                Assert.That(window.AtDefault, Is.True);
                Assert.That(window.Notice, Is.Empty);
                Assert.That(Dropped(plan), Is.EqualTo(new[] { "big" }));
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
        }

        [Test]
        public void AMovingPoseOverTheFrameLimitIsShownButNotAsked()
        {
            var plan = new PoseLimitPlan(new[] { Still("s0"), new PoseCandidate { Id = "long", Name = "long", IsAnimation = true, OverFrameLimit = true } });
            var row = plan.Rows.Single(item => item.Id == "long");
            Assert.That(row.Animated && !row.Selectable && !row.Keep, Is.True);
            Assert.That(plan.NeedsChoice, Is.False);
            Assert.That(plan.LeftOut, Is.Empty, "The board already reports it.");
        }

        // The size the contract's Measure gives the written extension.
        static long Measured(Type contract, Dictionary<string, object> extension)
        {
            var measure = contract.GetMethod("Measure", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var arguments = new object[] { extension, int.MaxValue, 0 };
            measure.Invoke(null, arguments);
            return int.MaxValue - (int)arguments[1];
        }

        // The plan's sizes are the contract's bound, so a plan within the
        // limits is always written and one over them never is.
        [Test]
        public void TheSizesAreTheContractsBound()
        {
            var moving = new[] { Moving("m0", 120, 20), Moving("動くポーズ", 61, HumanoidAnimationData.BoneNames.Length), Moving("m2", 2, 1) };
            moving[1].Animation.Category = "カテゴリ \"引用\"";
            var still = new[] { Still("s0"), Still("静止ポーズ", HumanoidPoseData.BoneNames.Length, "名前 \\ 区切り"), Still("s2", 3) };
            var plan = new PoseLimitPlan(moving.Concat(still));
            Assert.That(plan.AnimationBytes, Is.EqualTo(Measured(typeof(HumanoidAnimationData),
                HumanoidAnimationData.Write(moving.Select(row => row.Animation).ToList()))));
            Assert.That(plan.PoseBytes, Is.EqualTo(Measured(typeof(HumanoidPoseData),
                HumanoidPoseData.Write(still.Select(row => row.Data).ToList()))));
        }

        [Test]
        public void MovingPosesTheDefaultKeepsAreWritten()
        {
            var rows = new[] { Moving("m0", 3000, 20), Moving("m1", 3000, 20), Moving("m2", 3000, 20) };
            Assert.Throws<InvalidOperationException>(() => HumanoidAnimationData.Write(rows.Select(row => row.Animation).ToList()));
            var plan = new PoseLimitPlan(rows); plan.Default();
            Assert.DoesNotThrow(() => HumanoidAnimationData.Write(rows.Where(row => !plan.Dropped.Contains(row.Id)).Select(row => row.Animation).ToList()));
        }

        [Test]
        public void EveryShippedLocaleTranslatesTheWindow()
        {
            var sources = typeof(PoseLimitWindow).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                .Where(field => field.IsLiteral && field.FieldType == typeof(string)).Select(field => (string)field.GetRawConstantValue())
                .Concat(new[] { PoseLimitChooser.Category, PoseLimitChooser.DroppedReason, PoseLimitChooser.TooLargeReason }).Distinct().ToArray();
            Assert.That(sources.Length, Is.GreaterThan(20));
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = File.ReadAllText(Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var source in sources)
                    Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }
    }

    // The chooser between the plan and an export: when the window opens, and
    // what an export keeps without it.
    public sealed class PoseLimitChooserTests
    {
        sealed class FakeDialog : IPoseLimitDialog
        {
            internal int Asked;
            internal bool Answer = true;
            internal Action<PoseLimitPlan> Change;
            public bool Ask(PoseLimitPlan plan, GameObject avatar)
            {
                Asked++;
                Change?.Invoke(plan);
                return Answer;
            }
        }

        string settingsPath, previousSettingsPath;
        IPoseLimitDialog originalDialog;
        Func<bool> originalBatchMode;
        FakeDialog dialog;

        [SetUp]
        public void SetUp()
        {
            previousSettingsPath = ExportSettingsStore.PathOverride;
            settingsPath = Path.Combine(Path.GetTempPath(), "vrvlog-limit-settings-" + Guid.NewGuid().ToString("N") + ".json");
            ExportSettingsStore.PathOverride = settingsPath;
            originalDialog = PoseLimitChooser.Dialog;
            PoseLimitChooser.Dialog = dialog = new FakeDialog();
            originalBatchMode = ExportFallbackConfirmation.IsBatchMode;
            ExportFallbackConfirmation.IsBatchMode = () => false;
        }

        [TearDown]
        public void TearDown()
        {
            PoseLimitChooser.Dialog = originalDialog;
            ExportFallbackConfirmation.IsBatchMode = originalBatchMode;
            ExportSettingsStore.PathOverride = previousSettingsPath;
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }

        static PoseExportSession Session(AttachmentConnectionTests.Fixture fixture, int count)
        {
            var session = new PoseExportSession(fixture.Source, new PoseExportOptions());
            session.CollectPrepared(fixture.Copy);
            for (var i = 0; i < count; i++)
                session.Entries.Add(new PoseCandidate { Id = "pose" + i, Name = "ポーズ " + (i + 1), Source = "手動",
                    IsAnimation = true, Animation = new HumanoidAnimationData { Id = "pose" + i } });
            return session;
        }

        static string[] Omitted(ExportRecoveryReport report) => report.Fallbacks.Results
            .Where(result => result.Category == PoseLimitChooser.Category && result.Outcome == ExportOutcome.Omitted).Select(result => result.Name).ToArray();

        [Test]
        public void WithinTheLimitsNothingIsAsked()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = Session(fixture, 3);
            var report = new ExportRecoveryReport(); var warnings = new List<string>();
            ExportSettingsStore.SaveLimitChoice(fixture.Source, new[] { "pose0" });
            using (new PoseLimitChooser.Scope(fixture.Source, true))
                PoseLimitChooser.Apply(session, warnings, report);
            Assert.That(dialog.Asked, Is.Zero);
            Assert.That(session.LimitExcluded, Is.Empty, "A choice saved for more poses is not applied.");
            Assert.That(warnings, Is.Empty);
            Assert.That(Omitted(report), Is.Empty);
        }

        [Test]
        public void TheNormalExportAsksOnceAndListsWhatWasLeftOut()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = Session(fixture, 130);
            var report = new ExportRecoveryReport(); var warnings = new List<string>();
            using (new PoseLimitChooser.Scope(fixture.Source, true))
            {
                PoseLimitChooser.Apply(session, warnings, report);
                // A retry of the same export reuses the answer.
                PoseLimitChooser.Apply(session, new List<string>(), new ExportRecoveryReport());
            }
            Assert.That(dialog.Asked, Is.EqualTo(1));
            Assert.That(session.LimitExcluded, Is.EquivalentTo(new[] { "pose128", "pose129" }));
            Assert.That(session.SelectedAnimations().Select(animation => animation.Id), Is.EqualTo(Enumerable.Range(0, 128).Select(i => "pose" + i)));
            Assert.That(Omitted(report), Is.EqualTo(new[] { "ポーズ 129", "ポーズ 130" }));
            Assert.That(warnings, Is.EqualTo(new[] { ExporterLocalization.T(PoseLimitChooser.DroppedReason) }));
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.Empty, "The default is not stored.");
        }

        [Test]
        public void TheChoiceIsSavedAndUsedWithoutAsking()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            dialog.Change = plan =>
            {
                foreach (var row in plan.Rows) row.Keep = row.Id != "pose0" && row.Id != "pose1";
            };
            using (var session = Session(fixture, 130))
            using (new PoseLimitChooser.Scope(fixture.Source, true))
                PoseLimitChooser.Apply(session, null, null);
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.EquivalentTo(new[] { "pose0", "pose1" }));
            // The quick transfer, and a retry outside the export.
            foreach (var scope in new[] { new PoseLimitChooser.Scope(fixture.Source, false), null })
                using (scope)
                using (var session = Session(fixture, 130))
                {
                    PoseLimitChooser.Apply(session, null, null);
                    Assert.That(session.LimitExcluded, Is.EquivalentTo(new[] { "pose0", "pose1" }));
                }
            Assert.That(dialog.Asked, Is.EqualTo(1));
        }

        [Test]
        public void ConfirmingKeepsTheSavedChoiceForPosesNotListed()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            // "gone" is not listed this time; pose5 is replaced by this answer.
            ExportSettingsStore.SaveLimitChoice(fixture.Source, new[] { "gone", "pose5" });
            dialog.Change = plan =>
            {
                foreach (var row in plan.Rows) row.Keep = row.Id != "pose0" && row.Id != "pose1";
            };
            using (var session = Session(fixture, 130))
            using (new PoseLimitChooser.Scope(fixture.Source, true))
                PoseLimitChooser.Apply(session, null, null);
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.EquivalentTo(new[] { "gone", "pose0", "pose1" }));
            // Choosing the default forgets only the listed rows.
            dialog.Change = plan => plan.Default();
            using (var session = Session(fixture, 130))
            using (new PoseLimitChooser.Scope(fixture.Source, true))
                PoseLimitChooser.Apply(session, null, null);
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.EquivalentTo(new[] { "gone" }));
        }

        [Test]
        public void ATooLargePoseAloneDropsNothingElseAndKeepsTheSavedChoice()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            ExportSettingsStore.SaveLimitChoice(fixture.Source, new[] { "pose0" });
            // The window cannot change rows here; a test dialog that tries is ignored.
            dialog.Change = plan => { foreach (var row in plan.Rows) row.Keep = false; };
            PoseExportSession Big()
            {
                var session = Session(fixture, 3);
                session.Entries.Add(PoseLimitPlanTests.Moving("big", 8000, 20));
                return session;
            }
            var report = new ExportRecoveryReport();
            using (var session = Big())
            {
                using (new PoseLimitChooser.Scope(fixture.Source, true))
                    PoseLimitChooser.Apply(session, null, report);
                Assert.That(session.LimitExcluded, Is.EquivalentTo(new[] { "big" }));
            }
            Assert.That(dialog.Asked, Is.EqualTo(1), "The pose too large to keep is still shown.");
            Assert.That(Omitted(report), Is.EqualTo(new[] { "big" }));
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.EquivalentTo(new[] { "pose0" }));
            // The quick transfer writes the same poses.
            using (var session = Big())
            using (new PoseLimitChooser.Scope(fixture.Source, false))
            {
                PoseLimitChooser.Apply(session, null, null);
                Assert.That(session.LimitExcluded, Is.EquivalentTo(new[] { "big" }));
            }
            Assert.That(dialog.Asked, Is.EqualTo(1));
        }

        [Test]
        public void StoppingInTheWindowCancelsTheExport()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = Session(fixture, 130);
            dialog.Answer = false;
            using (new PoseLimitChooser.Scope(fixture.Source, true))
                Assert.Throws<OperationCanceledException>(() => PoseLimitChooser.Apply(session, null, null));
            Assert.That(ExportSettingsStore.LoadLimitChoice(fixture.Source), Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(true, true)]
        public void QuickAndBatchExportsUseTheDefaultWithoutAsking(bool interactive, bool batch)
        {
            ExportFallbackConfirmation.IsBatchMode = () => batch;
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = Session(fixture, 130);
            var report = new ExportRecoveryReport();
            using (new PoseLimitChooser.Scope(fixture.Source, interactive))
                PoseLimitChooser.Apply(session, null, report);
            Assert.That(dialog.Asked, Is.Zero);
            Assert.That(session.LimitExcluded, Is.EquivalentTo(new[] { "pose128", "pose129" }));
            Assert.That(Omitted(report), Is.EqualTo(new[] { "ポーズ 129", "ポーズ 130" }));
        }
    }
}
