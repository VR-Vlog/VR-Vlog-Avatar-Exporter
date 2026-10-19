using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AimAxis = UniGLTF.Extensions.VRMC_node_constraint.AimAxis;
using RollAxis = UniGLTF.Extensions.VRMC_node_constraint.RollAxis;
using UniVRM10;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using VRVlog.Poses;
using ConstraintSource = UnityEngine.Animations.ConstraintSource;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ConstraintExportTests
    {
        // Two siblings under one parent, plus an unrelated branch for a
        // source with another parent. Rotations are set after the constraint
        // so the editor has no chance to solve it first.
        sealed class Fixture : IDisposable
        {
            internal readonly GameObject Source = new GameObject("Source avatar");
            internal readonly GameObject Copy = new GameObject("Copy");
            internal readonly Transform Parent, A, B, Other;
            internal readonly ExportFallbackReport Report = new ExportFallbackReport();
            internal readonly List<string> Warnings = new List<string>();

            internal Fixture()
            {
                Parent = Child(Copy.transform, "Parent", Vector3.up);
                A = Child(Parent, "A", new Vector3(.1f, 0, 0));
                B = Child(Parent, "B", new Vector3(-.1f, 0, 0));
                Other = Child(Child(Copy.transform, "Other parent", Vector3.down), "Other", Vector3.zero);
            }

            internal static Transform Child(Transform parent, string name, Vector3 local)
            {
                var value = new GameObject(name).transform; value.SetParent(parent, false); value.localPosition = local; return value;
            }

            internal T Constraint<T>(Transform target, float weight = 1, params Transform[] sources) where T : Behaviour, IConstraint
            {
                var constraint = target.gameObject.AddComponent<T>();
                foreach (var source in sources) constraint.AddSource(new ConstraintSource { sourceTransform = source, weight = 1 });
                constraint.weight = weight; constraint.locked = true; constraint.constraintActive = true;
                return constraint;
            }

            internal ConstraintExport.Result Convert() => ConstraintExport.Convert(Source, Copy, Warnings, Report);
            internal ExportResultEntry Entry => Report.Results.Single();
            public void Dispose() { Object.DestroyImmediate(Copy); Object.DestroyImmediate(Source); }
        }

        [Test]
        public void SiblingRotationWithTheSameRestBecomesAKeptVrmRotation()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.A.localRotation = f.B.localRotation = Quaternion.Euler(0, 0, 20);
            var result = f.Convert();
            var rotation = f.B.GetComponent<Vrm10RotationConstraint>();
            Assert.That(rotation, Is.Not.Null);
            Assert.That(rotation.Source, Is.EqualTo(f.A));
            Assert.That(rotation.Weight, Is.EqualTo(1));
            Assert.That(result.Kept, Is.EqualTo(1));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(f.Entry.Category, Is.EqualTo(ConstraintExport.Category));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.KeptRotation));
            Assert.That(f.Entry.Path, Is.EqualTo("Parent/B"));
            Assert.That(f.Copy.GetComponent<Vrm10Instance>(), Is.Not.Null, "UniVRM reads constraints under its instance.");
            Assert.That(f.Source.GetComponentsInChildren<Component>().Length, Is.EqualTo(1), "The source avatar is not touched.");
        }

        [Test]
        public void TheVrmFormulaFollowsTheSourceExactlyWhenKept()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.A.localRotation = f.B.localRotation = Quaternion.Euler(10, 0, 20);
            f.Convert();
            // Ideal: world = source world * inverse(source rest world) * target rest world.
            var restA = f.A.rotation; var restB = f.B.rotation; var localA = f.A.localRotation; var localB = f.B.localRotation;
            f.Parent.localRotation = Quaternion.Euler(0, 30, 0);
            f.A.localRotation = Quaternion.Euler(40, -15, 70);
            var ideal = f.A.rotation * Quaternion.Inverse(restA) * restB;
            // UniVRM: local = target rest * inverse(source rest local) * source local.
            f.B.localRotation = localB * Quaternion.Inverse(localA) * f.A.localRotation;
            Assert.That(Quaternion.Angle(f.B.rotation, ideal), Is.LessThan(.01f));
        }

        [Test]
        public void ADifferentRestOrPartialWeightIsApproximated()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, .5f, f.A);
            f.B.localRotation = Quaternion.Euler(0, 30, 0);
            f.Convert();
            Assert.That(f.B.GetComponent<Vrm10RotationConstraint>().Weight, Is.EqualTo(.5f));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Approximated));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.ApproximatedRotation));
        }

        [Test]
        public void ARestDifferenceThatWouldDriftMoreThanThePoseKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.B.localRotation = Quaternion.Euler(0, 90, 0);
            f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedRest));
            Assert.That(f.Copy.GetComponent<Vrm10Instance>(), Is.Null);
        }

        [Test]
        public void AWorldSpaceRotationAcrossParentsKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.Other);
            f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedParent));
        }

        [TestCase(Axis.X, RollAxis.X)][TestCase(Axis.Y, RollAxis.Y)][TestCase(Axis.Z, RollAxis.Z)]
        public void ASingleAxisRotationBecomesARoll(Axis axis, RollAxis expected)
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A).rotationAxis = axis;
            f.Convert();
            var roll = f.B.GetComponent<Vrm10RollConstraint>();
            Assert.That(roll.RollAxis, Is.EqualTo(expected));
            Assert.That(roll.Source, Is.EqualTo(f.A));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Approximated));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.ApproximatedRoll));
        }

        [Test]
        public void TwoAxesOrSeveralSourcesKeepThePose()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A).rotationAxis = Axis.X | Axis.Y;
            f.Constraint<RotationConstraint>(f.Other, 1, f.A, f.B);
            f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EqualTo(new[] { ConstraintExport.OmittedSetting, ConstraintExport.OmittedSetting }));
        }

        [Test]
        public void AnAimThatPointsAtTheSourceBecomesAVrmAim()
        {
            using var f = new Fixture();
            f.Constraint<AimConstraint>(f.B, 1, f.A).aimVector = Vector3.down;
            // -Y of B towards A (A is at +X from B).
            f.B.localRotation = Quaternion.FromToRotation(Vector3.down, Vector3.right);
            f.Convert();
            var aim = f.B.GetComponent<Vrm10AimConstraint>();
            Assert.That(aim.AimAxis, Is.EqualTo(AimAxis.NegativeY));
            Assert.That(aim.Source, Is.EqualTo(f.A));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.ApproximatedAim));
        }

        [Test]
        public void AnAimOffTheSourceAtRestKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<LookAtConstraint>(f.B, 1, f.A);
            f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null, "VRM would turn +Z onto the source even at rest.");
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedRest));
        }

        [Test]
        public void ParentPositionAndScaleKeepThePose()
        {
            using var f = new Fixture();
            f.Constraint<ParentConstraint>(f.B, 1, f.A);
            f.Constraint<PositionConstraint>(f.Other, 1, f.A);
            f.Constraint<ScaleConstraint>(f.Parent, 1, f.A);
            var result = f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(result.Omitted, Is.EqualTo(3));
            Assert.That(f.Report.Results.All(r => r.Outcome == ExportOutcome.Omitted && r.Reason == ConstraintExport.OmittedKind), Is.True);
            Assert.That(f.Warnings.Single(), Does.Contain("省略3件"));
        }

        [TestCase("disabled")][TestCase("inactive")][TestCase("unlocked")][TestCase("weightless")][TestCase("sourceless")]
        public void ConstraintsThatNeverMoveAnythingAreNotListed(string condition)
        {
            using var f = new Fixture();
            var constraint = f.Constraint<RotationConstraint>(f.B, 1, condition == "sourceless" ? new Transform[0] : new[] { f.A });
            switch (condition)
            {
                case "disabled": constraint.enabled = false; break;
                case "inactive": constraint.constraintActive = false; break;
                case "unlocked": constraint.locked = false; break;
                case "weightless": constraint.weight = 0; break;
            }
            var result = f.Convert();
            Assert.That(result.Sources, Is.Zero);
            Assert.That(f.Report.Results, Is.Empty);
            Assert.That(f.Warnings, Is.Empty);
        }

        [Test]
        public void AnAuthoredVrmConstraintWins()
        {
            using var f = new Fixture();
            var authored = f.B.gameObject.AddComponent<Vrm10RotationConstraint>(); authored.Source = f.Other; authored.Weight = .25f;
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.Convert();
            Assert.That(f.B.GetComponents<IVrm10Constraint>(), Is.EqualTo(new IVrm10Constraint[] { authored }));
            Assert.That(authored.Source, Is.EqualTo(f.Other));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedDuplicate));
        }

        [Test]
        public void AReferenceOutsideTheCopyKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.Source.transform);
            f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedOutside));
        }

        [Test]
        public void TheAvatarRootIsNeitherSourceNorTarget()
        {
            // UniVRM does not write the root as a node; a reference to it
            // would become -1 in the file.
            using var f = new Fixture();
            f.Constraint<AimConstraint>(f.B, 1, f.Copy.transform).aimVector = Vector3.down;
            f.B.rotation = Quaternion.FromToRotation(Vector3.down, f.Copy.transform.position - f.B.position);
            f.Constraint<RotationConstraint>(f.Copy.transform, 1, f.A);
            f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EqualTo(new[] { ConstraintExport.OmittedOutside, ConstraintExport.OmittedOutside }));
        }

        [Test]
        public void ASingleAxisRotationAcrossParentsOrOntoItselfKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.Other).rotationAxis = Axis.Y;
            f.Constraint<RotationConstraint>(f.A, 1, f.A);
            f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EquivalentTo(new[] { ConstraintExport.OmittedParent, ConstraintExport.OmittedSetting }));
        }

        [Test]
        public void TheSourceAvatarIsNeverConverted()
        {
            using var f = new Fixture();
            Assert.Throws<InvalidOperationException>(() => ConstraintExport.Convert(f.Copy, f.Copy, null));
        }

        [Test]
        public void AVrchatRotationConstraintIsReadWithoutTheSdkReference()
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint")).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("Real VRChat SDK integration test; Unity constraints are covered without it.");
            using var f = new Fixture();
            var component = f.B.gameObject.AddComponent(type);
            using (var settings = new SerializedObject(component))
            {
                settings.FindProperty("IsActive").boolValue = true;
                settings.FindProperty("Locked").boolValue = true;
                settings.FindProperty("GlobalWeight").floatValue = .75f;
                settings.FindProperty("Sources.totalLength").intValue = 1;
                settings.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = f.A;
                settings.FindProperty("Sources.source0.Weight").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            f.A.localRotation = f.B.localRotation = Quaternion.identity;
            f.Convert();
            var rotation = f.B.GetComponent<Vrm10RotationConstraint>();
            Assert.That(rotation.Source, Is.EqualTo(f.A));
            Assert.That(rotation.Weight, Is.EqualTo(.75f));
            Assert.That(f.Entry.Name, Does.Contain("VRCRotationConstraint"));
        }

        [Test]
        public void AFullExportWritesTheNodeConstraint()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Requires installed lilToon for the full exporter entry point.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(shader);
            foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            var hips = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var a = Fixture.Child(hips, "Constraint source", new Vector3(.1f, -.1f, 0));
            var b = Fixture.Child(hips, "Constraint target", new Vector3(-.1f, -.1f, 0));
            var constraint = b.gameObject.AddComponent<RotationConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = a, weight = 1 });
            constraint.locked = true; constraint.constraintActive = true;
            a.localRotation = b.localRotation = Quaternion.identity;
            var report = new ExportRecoveryReport();
            byte[] bytes;
            try
            {
                bytes = UniVrmOneClickExporter.Export(fixture.Source, "Constraint", "Tests", new List<string>(),
                    exporterVersion: "0.11.8", lilToonVersion: "2.3.4",
                    blinkOptions: new BlinkExportOptions { Mode = BlinkExportMode.None }, recoveryReport: report);
            }
            finally { Object.DestroyImmediate(material); }
            var nodes = ((List<object>)GlbDocument.Read(bytes).Json["nodes"]).Cast<Dictionary<string, object>>().ToList();
            var target = nodes.Single(node => (string)node["name"] == "Constraint target");
            var extension = (Dictionary<string, object>)((Dictionary<string, object>)target["extensions"])["VRMC_node_constraint"];
            var rotation = (Dictionary<string, object>)((Dictionary<string, object>)extension["constraint"])["rotation"];
            Assert.That((string)nodes[Convert.ToInt32(rotation["source"])]["name"], Is.EqualTo("Constraint source"));
            Assert.That(report.Fallbacks.Results.Single(r => r.Category == ConstraintExport.Category).Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(b.GetComponent<IVrm10Constraint>(), Is.Null, "Only the export copy gets the VRM constraint.");
        }

        [Test]
        public void EveryShippedLocaleTranslatesTheConstraintResults()
        {
            var guid = AssetDatabase.FindAssets("ExporterLocale_en t:TextAsset").Single();
            var directory = System.IO.Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var locale in new[] { "en", "ko", "zh-Hans", "zh-Hant" })
            {
                var text = System.IO.File.ReadAllText(System.IO.Path.Combine(directory, "ExporterLocale_" + locale + ".json"));
                foreach (var source in ConstraintExport.Reasons.Append(ConstraintExport.Category).Append(ExportStageGuard.Label(ExportStage.Constraints)))
                    Assert.That(text, Does.Contain("{\"source\":\"" + source + "\",\"translated\":\""), locale + ": " + source);
            }
        }
    }
}
