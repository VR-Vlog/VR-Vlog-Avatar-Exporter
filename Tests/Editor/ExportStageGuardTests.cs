using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRVlog.Poses;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExportStageGuardTests
    {
        [TearDown]
        public void ResetFaultInjection() => ExportStageGuard.FaultInjection = null;

        // Stage names, since the internal enum cannot be a public test parameter.
        static IEnumerable<string> Stages => Enum.GetNames(typeof(ExportStage));

        [TestCaseSource(nameof(Stages))]
        public void EveryStageCanFailAndTheFileIsStillWritten(string stageName)
        {
            var stage = (ExportStage)Enum.Parse(typeof(ExportStage), stageName);
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")) { name = "Guarded" };
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                var roots = SceneManager.GetActiveScene().rootCount;
                var children = fixture.Source.GetComponentsInChildren<Transform>(true).Length;
                var fired = 0;
                ExportStageGuard.FaultInjection = current =>
                {
                    if (current != stage) return;
                    fired++;
                    throw new InvalidOperationException("injected " + current);
                };
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                var warnings = new List<string>();
                var bytes = Export(fixture.Source, report, warnings);

                Assert.That(fired, Is.EqualTo(1), "The fixture must reach the stage, and a left-out stage must not run again.");
                Assert.That(bytes, Is.Not.Null.And.Not.Empty);
                var extensions = (Dictionary<string, object>)GlbDocument.Read(bytes).Json["extensions"];
                Assert.That(extensions.ContainsKey("VRMC_vrm"), Is.True, "The file must stay a VRM.");
                Assert.DoesNotThrow(() => LilToonGlbExtension.Validate(bytes), "The save check must accept the file.");
                Assert.That(report.Succeeded, Is.True);
                Assert.That(report.BuildSupportText(), Does.Contain("Error: none"), "A left-out stage must not show up as the export's error.");
                var entry = report.Fallbacks.Results.Single(result => result.Stage != null);
                Assert.That(entry.Stage, Is.EqualTo(stage));
                Assert.That(entry.Reason, Is.EqualTo("injected " + stage));
                Assert.That(entry.Outcome, Is.EqualTo(ExportStageGuard.Outcome(stage)));
                Assert.That(report.Fallbacks.Items.Single(item => item.Kind == ExportFallbackKind.StageOmitted).Names,
                    Is.EqualTo(new[] { ExportStageGuard.DisplayName(stage) }));
                Assert.That(warnings.Count(warning => warning.Contains("injected " + stage)), Is.EqualTo(1),
                    "Warnings of a discarded attempt must not be repeated.");
                var list = ExportResultList.Build(report.Fallbacks, warnings);
                Assert.That(list.Rows.Single(row => row.Stage == stage).Group, Is.EqualTo(ExportStageGuard.DisplayName(stage)));
                Assert.That(list.Rows.Any(row => row.Outcome == null && row.Content.Contains("injected " + stage)), Is.False,
                    "The omission warning is the same item as the stage line, not another notice.");

                // Non-destructive: the copy and its temporary objects are gone.
                Assert.That(SceneManager.GetActiveScene().rootCount, Is.EqualTo(roots));
                Assert.That(fixture.Source.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(children));
                Assert.That(fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>().All(skin => skin.sharedMaterial == material), Is.True);
                Assert.That(material.shader.name, Is.EqualTo("lilToon"));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [TestCaseSource(nameof(Stages))]
        public void StrictExportsStillStopAtTheFirstFailure(string stageName)
        {
            var stage = (ExportStage)Enum.Parse(typeof(ExportStage), stageName);
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon")) { name = "Strict" };
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                ExportStageGuard.FaultInjection = current =>
                {
                    if (current == stage) throw new InvalidOperationException("injected " + current);
                };
                var report = new ExportRecoveryReport();
                var error = Assert.Throws<InvalidOperationException>(() => Export(fixture.Source, report, new List<string>()));
                Assert.That(error.Message, Is.EqualTo("injected " + stage), "The original error must not be wrapped.");
                Assert.That(report.Succeeded, Is.False);
                Assert.That(report.Fallbacks.Results.Any(result => result.Stage != null), Is.False);
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void EveryStageBelongsToAPhase()
        {
            foreach (ExportStage stage in Enum.GetValues(typeof(ExportStage)))
                Assert.DoesNotThrow(() => ExportStageGuard.Phase(stage), stage.ToString());
        }

        [Test]
        public void CancellationIsNeverLeftOut()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(Shader.Find("lilToon"));
            try
            {
                foreach (var skin in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) skin.sharedMaterial = material;
                ExportStageGuard.FaultInjection = current =>
                {
                    if (current == ExportStage.PhysBone) throw new OperationCanceledException();
                };
                var report = new ExportRecoveryReport { ContinueOnStageFailure = true };
                Assert.Throws<OperationCanceledException>(() => Export(fixture.Source, report, null));
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void ACancellationWrappedByAnotherToolIsNeverLeftOut()
        {
            var guard = new ExportStageGuard(true);
            var attempts = 0;
            var error = Assert.Throws<InvalidOperationException>(() => guard.Retry(() =>
            {
                attempts++;
                guard.Watch(ExportStage.BuildProcess, () =>
                {
                    try { guard.Step(ExportStage.Expressions, () => throw new InvalidOperationException("inner")); }
                    catch (Exception inner) { throw new InvalidOperationException("build failed", new AggregateException(new OperationCanceledException(), inner)); }
                });
                return true;
            }, null));
            Assert.That(error.Message, Is.EqualTo("build failed"));
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(guard.Omissions, Is.Empty);
        }

        [Test]
        public void TheStandardVrmIsCheckedAgainstTheAppLimits()
        {
            static byte[] Glb(int materials, int textures) => GlbDocument.Create(new Dictionary<string, object>
            {
                ["asset"] = new Dictionary<string, object> { ["version"] = "2.0" },
                ["materials"] = Enumerable.Range(0, materials).Select(i => (object)new Dictionary<string, object>()).ToList(),
                ["textures"] = Enumerable.Range(0, textures).Select(i => (object)new Dictionary<string, object> { ["source"] = (long)i }).ToList(),
            }, new byte[0]).Write();
            Assert.That(LilToonGlbExtension.StandardCounts(Glb(3, 5)), Is.EqualTo((3, 5)));
            Assert.DoesNotThrow(() => LilToonGlbExtension.RequireWithinLimits(Glb(LilToonMobileProfile.MaximumMaterials, LilToonMobileProfile.MaximumTextures)));
            Assert.Throws<InvalidOperationException>(() => LilToonGlbExtension.RequireWithinLimits(Glb(LilToonMobileProfile.MaximumMaterials + 1, 0)));
            Assert.Throws<InvalidOperationException>(() => LilToonGlbExtension.RequireWithinLimits(Glb(0, LilToonMobileProfile.MaximumTextures + 1)));
        }

        [Test]
        public void StagesThatChangeTheCopyRestartFromAFreshCopy()
        {
            var guard = new ExportStageGuard(true);
            var attempts = 0;
            var result = guard.Retry(() =>
            {
                attempts++;
                guard.Step(ExportStage.PhysBone, () => throw new InvalidOperationException("first"));
                return guard.Step(ExportStage.LookAt, () => attempts == 2 ? throw new InvalidOperationException("second") : "done", () => "skipped");
            }, null);
            Assert.That(result, Is.EqualTo("skipped"));
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(guard.Omissions.Select(omission => omission.Stage), Is.EqualTo(new[] { ExportStage.PhysBone, ExportStage.LookAt }));
            Assert.That(guard.Omissions.Select(omission => omission.Error.Message), Is.EqualTo(new[] { "first", "second" }));
        }

        [Test]
        public void AStageWrappedByAnotherToolIsStillFound()
        {
            var guard = new ExportStageGuard(true);
            var attempts = 0;
            guard.Retry(() =>
            {
                attempts++;
                // Build tools report callback failures inside their own summary.
                guard.Watch(ExportStage.BuildProcess, () =>
                {
                    try { guard.Step(ExportStage.Expressions, () => throw new InvalidOperationException("inner")); }
                    catch (Exception error) { throw new InvalidOperationException("build failed", new AggregateException(error)); }
                });
                return true;
            }, null);
            Assert.That(attempts, Is.EqualTo(2));
            Assert.That(guard.Omissions.Single().Stage, Is.EqualTo(ExportStage.Expressions), "The outer build stage must stay in the file.");
        }

        [Test]
        public void OverTheLimitPosesKeepTheFirst128()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = new PoseExportSession(fixture.Source, new PoseExportOptions());
            session.CollectPrepared(fixture.Copy);
            for (var i = 0; i < 130; i++)
                session.Entries.Add(new PoseCandidate { Id = "pose" + i, IsAnimation = true, Animation = new HumanoidAnimationData { Id = "pose" + i } });
            Assert.Throws<InvalidOperationException>(() => session.SelectedAnimations());
            session.TrimToLimit = true;
            Assert.That(session.TrimmedCount, Is.EqualTo(2));
            Assert.That(session.SelectedAnimations().Select(animation => animation.Id),
                Is.EqualTo(Enumerable.Range(0, 128).Select(i => "pose" + i)));
        }

        [Test]
        public void ANoPoseSessionCollectsNothing()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            using var session = PoseExportSession.None(fixture.Source);
            session.CollectPrepared(fixture.Copy);
            Assert.That(session.Entries, Is.Empty);
            var bytes = new byte[] { 1, 2, 3 };
            Assert.That(session.Inject(bytes, Vector3.one), Is.SameAs(bytes));
        }

        [Test]
        public void TheResultListCountsEachOutcome()
        {
            var report = new ExportFallbackReport();
            report.Add(ExportFallbackKind.Blink);
            report.Add(ExportFallbackKind.MaterialApproximated, "Body", reason: "unreadable");
            report.Add(ExportFallbackKind.MaterialApproximated, "Body", reason: "unreadable");
            report.AddStage(ExportStage.LookAt, "broken");
            report.Record(ExportOutcome.Kept, "lilToonの表現", "Hair", count: 3);
            var summary = report.Summary();
            Assert.That(summary[ExportOutcome.Omitted], Is.EqualTo(2));
            Assert.That(summary[ExportOutcome.Approximated], Is.EqualTo(1), "The same material is listed once.");
            Assert.That(summary[ExportOutcome.Kept], Is.EqualTo(3));
            Assert.That(summary[ExportOutcome.Baked], Is.Zero);
            Assert.That(report.Results.Single(result => result.Name == "Body").Reason, Is.EqualTo("unreadable"));
            Assert.That(report.Results.Single(result => result.Stage == ExportStage.LookAt).Category, Is.EqualTo(ExportStageGuard.Label(ExportStage.LookAt)));
            report.Clear();
            Assert.That(report.Results, Is.Empty);
        }

        static byte[] Export(GameObject source, ExportRecoveryReport report, ICollection<string> warnings) =>
            UniVrmOneClickExporter.Export(source, "Stage guard", "Tests", warnings,
                exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);
    }
}
