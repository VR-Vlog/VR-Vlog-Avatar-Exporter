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
            Assert.Throws<InvalidOperationException>(() => session.SelectedAnimations());
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
