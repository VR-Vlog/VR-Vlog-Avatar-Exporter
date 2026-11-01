using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using AimAxis = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_node_constraint.AimAxis;
using RollAxis = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_node_constraint.RollAxis;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
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

        // A merged outfit bone under a humanoid bone drives a helper bone that
        // hangs from that bone's parent through a wrapper, as after merging
        // an outfit armature: the parents differ, but neither ever turns.
        sealed class MergedFixture : IDisposable
        {
            internal readonly AttachmentConnectionTests.Fixture Avatar = new AttachmentConnectionTests.Fixture();
            internal readonly Transform Hand, Forearm, Merged, Wrapper, Helper;
            internal readonly ExportFallbackReport Report = new ExportFallbackReport();

            internal MergedFixture()
            {
                Hand = Avatar.Copy.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.LeftHand);
                Forearm = Hand.parent;
                Hand.localRotation = Quaternion.Euler(0, 0, -15);
                Merged = Fixture.Child(Hand, "Merged bone", new Vector3(-.05f, 0, 0));
                Wrapper = Fixture.Child(Forearm, "Wrapper", Vector3.zero);
                Helper = Fixture.Child(Wrapper, "Helper", Forearm.InverseTransformPoint(Merged.position));
                Helper.rotation = Merged.rotation;
                var constraint = Helper.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = Merged, weight = 1 });
                constraint.locked = true; constraint.constraintActive = true;
            }

            internal void Convert() => ConstraintExport.Convert(Avatar.Source, Avatar.Copy, null, Report);
            internal ExportResultEntry Entry => Report.Results.Single();
            public void Dispose() => Avatar.Dispose();
        }

        [Test]
        public void AWorldSpaceRotationAcrossBonesThatNeverTurnFollowsTheTurningBone()
        {
            using var f = new MergedFixture();
            f.Convert();
            var rotation = f.Helper.GetComponent<Vrm10RotationConstraint>();
            Assert.That(rotation, Is.Not.Null);
            Assert.That(rotation.Source, Is.EqualTo(f.Hand), "The merged bone never turns on its own; the hand does.");
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.KeptRotation));
            // Bend the hand and solve both ways: Unity keeps the rest offset to
            // the merged bone in world space, VRM adds the hand's local change.
            var helperRest = f.Helper.localRotation;
            var helperWorldRest = f.Helper.rotation;
            var handRest = f.Hand.localRotation;
            var mergedRest = f.Merged.rotation;
            f.Hand.localRotation *= Quaternion.Euler(10, 40, -25);
            var expected = f.Merged.rotation * Quaternion.Inverse(mergedRest) * helperWorldRest;
            // The same step as Vrm10RotationConstraint.Process, which the tests cannot call.
            f.Helper.localRotation = Quaternion.SlerpUnclamped(helperRest, helperRest * (Quaternion.Inverse(handRest) * f.Hand.localRotation), rotation.Weight);
            Assert.That(Quaternion.Angle(f.Helper.rotation, expected), Is.LessThan(.01f));
            Assert.That(Quaternion.Angle(f.Helper.rotation, helperWorldRest), Is.GreaterThan(30f), "The helper turns with the hand.");
        }

        [TestCase("spring")][TestCase("constraint")]
        public void AWorldSpaceRotationAcrossABoneThatTurnsKeepsThePose(string between)
        {
            using var f = new MergedFixture();
            // The wrapper turns on its own, so the helper no longer shares the hand's frame.
            if (between == "spring") f.Wrapper.gameObject.AddComponent<VRM10SpringBoneJoint>();
            else
            {
                var constraint = f.Wrapper.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = f.Forearm, weight = 1 });
                constraint.constraintActive = true;
            }
            f.Convert();
            Assert.That(f.Helper.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Report.Results.Single(r => r.Path.EndsWith("/Helper", StringComparison.Ordinal)).Reason,
                Is.EqualTo(ConstraintExport.OmittedParent));
        }

        [TestCase("left out", false)][TestCase("converted", false)][TestCase("converted", true)][TestCase("vrm", false)][TestCase("spring", false)]
        public void AWorldSpaceRotationDoesNotFollowABoneThatTurnsOnItsOwn(string kind, bool driverFirst)
        {
            using var f = new MergedFixture();
            // The merged bone hangs from a bone that is not posed by the app.
            // A target the export leaves out never turns in VRM; one it converts
            // here is not used either, so the order does not matter; VRM solves
            // other constraints and spring joints in its own order.
            var driver = Fixture.Child(f.Forearm, "Driver", f.Hand.localPosition);
            driver.rotation = f.Hand.rotation;
            if (driverFirst) driver.SetSiblingIndex(0);
            f.Merged.SetParent(driver, true);
            if (kind == "spring") driver.gameObject.AddComponent<VRM10SpringBoneJoint>();
            else if (kind == "vrm") driver.gameObject.AddComponent<Vrm10RotationConstraint>().Source = f.Hand;
            else
            {
                var constraint = kind == "left out" ? (IConstraint)driver.gameObject.AddComponent<PositionConstraint>() : driver.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = f.Hand, weight = 1 });
                constraint.locked = true; constraint.constraintActive = true;
            }
            f.Convert();
            Assert.That(f.Helper.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Report.Results.Single(r => r.Path.EndsWith("/Helper", StringComparison.Ordinal)).Reason,
                Is.EqualTo(ConstraintExport.OmittedParent));
        }

        [TestCase("spring", false)][TestCase("vrm", false)][TestCase("constraint", false)][TestCase("constraint", true)]
        public void AWorldSpaceRotationDoesNotFollowAConstrainedHumanoidBone(string kind, bool helperFirst)
        {
            using var f = new MergedFixture();
            // The hand itself swings or is constrained, so its turn depends on
            // the order VRM solves constraints and spring joints in.
            if (helperFirst) f.Wrapper.SetSiblingIndex(0);
            if (kind == "spring") f.Hand.gameObject.AddComponent<VRM10SpringBoneJoint>();
            else if (kind == "vrm") f.Hand.gameObject.AddComponent<Vrm10RotationConstraint>().Source = f.Forearm;
            else
            {
                var constraint = f.Hand.gameObject.AddComponent<RotationConstraint>();
                constraint.AddSource(new ConstraintSource { sourceTransform = f.Forearm, weight = 1 });
                constraint.locked = true; constraint.constraintActive = true;
            }
            f.Convert();
            Assert.That(f.Helper.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Report.Results.Single(r => r.Path.EndsWith("/Helper", StringComparison.Ordinal)).Reason,
                Is.EqualTo(ConstraintExport.OmittedParent));
        }

        [Test]
        public void AWorldSpaceRotationAcrossBonesThatNeverTurnStillChecksTheRest()
        {
            using var f = new MergedFixture();
            f.Helper.rotation = f.Merged.rotation * Quaternion.Euler(0, 90, 0);
            f.Convert();
            Assert.That(f.Helper.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedRest));
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
        public void ScaleKeepsThePose()
        {
            using var f = new Fixture();
            f.Constraint<ScaleConstraint>(f.Parent, 1, f.A);
            var result = f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(result.Omitted, Is.EqualTo(1));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedKind));
            Assert.That(f.Warnings.Single(), Does.Contain("省略1件"));
        }

        // Armor on the forearm placed at the hand by a position constraint
        // (#597): as a child of the hand it moves with it, and also turns with it.
        [Test]
        public void APositionConstrainedPropMovesUnderItsSource()
        {
            using var f = new Fixture();
            var prop = f.Other;
            prop.SetPositionAndRotation(new Vector3(.12f, .98f, 0), Quaternion.Euler(0, 30, 0));
            var world = prop.localToWorldMatrix;
            f.Constraint<PositionConstraint>(prop, 1, f.A);
            var result = f.Convert();
            Assert.That(prop.parent, Is.EqualTo(f.A));
            Assert.That(SameMatrix(prop.localToWorldMatrix, world), Is.True, "The prop keeps its world pose.");
            Assert.That(result.Approximated, Is.EqualTo(1));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Approximated));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.ApproximatedPosition));
            Assert.That(f.Entry.Path, Is.EqualTo("Other parent/Other"), "The path names the prop where the user put it.");
        }

        // An aim on the same object still turns it: VRM aims by the world
        // direction to the source, whatever the parent.
        [Test]
        public void APositionConstrainedPropKeepsTheAimOnIt()
        {
            using var f = new Fixture();
            var prop = f.Other;
            prop.position = f.A.position + new Vector3(0, -.2f, 0);
            prop.rotation = Quaternion.LookRotation(f.B.position - prop.position);
            f.Constraint<AimConstraint>(prop, 1, f.B).aimVector = Vector3.forward;
            f.Constraint<PositionConstraint>(prop, 1, f.A);
            f.Convert();
            Assert.That(prop.parent, Is.EqualTo(f.A));
            Assert.That(prop.GetComponent<Vrm10AimConstraint>().Source, Is.EqualTo(f.B));
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EquivalentTo(new[] { ConstraintExport.ApproximatedAim, ConstraintExport.ApproximatedPosition }));
        }

        [TestCase("translation axis")][TestCase("weight")][TestCase("source weight")][TestCase("rotation")][TestCase("rotation source")]
        public void APositionConstraintThatAChildWouldNotMatchKeepsThePose(string condition)
        {
            using var f = new Fixture();
            var constraint = f.Constraint<PositionConstraint>(f.Other, 1, f.A);
            var reason = ConstraintExport.OmittedReparent;
            switch (condition)
            {
                case "translation axis": constraint.translationAxis = Axis.X | Axis.Y; break;
                case "weight": constraint.weight = .5f; break;
                case "source weight": constraint.SetSource(0, new ConstraintSource { sourceTransform = f.A, weight = .5f }); break;
                // A rotation on the same object already drives it.
                case "rotation":
                    f.Constraint<RotationConstraint>(f.Other, 1, Fixture.Child(f.Other.parent, "Twin", Vector3.zero));
                    reason = ConstraintExport.OmittedDuplicate;
                    break;
                // A VRM rotation constraint reads the local rotation of its source.
                case "rotation source":
                    f.Constraint<RotationConstraint>(Fixture.Child(f.Other.parent, "Twin", Vector3.zero), 1, f.Other);
                    break;
            }
            f.Convert();
            Assert.That(f.Other.parent.name, Is.EqualTo("Other parent"));
            Assert.That(f.Report.Results.Single(r => r.Name.Contains(nameof(PositionConstraint))).Reason, Is.EqualTo(reason));
        }

        [Test]
        public void AParentConstrainedPropMovesUnderItsSource()
        {
            // A prop held in a hand: outside the hand, attached by the constraint.
            using var f = new Fixture();
            var prop = f.Other;
            prop.SetPositionAndRotation(new Vector3(.3f, .2f, .1f), Quaternion.Euler(10, 40, 70));
            var mesh = Fixture.Child(prop, "Mesh", new Vector3(0, .05f, 0));
            var world = mesh.localToWorldMatrix;
            f.Constraint<ParentConstraint>(prop, 1, f.A);
            var result = f.Convert();
            Assert.That(prop.parent, Is.EqualTo(f.A));
            Assert.That(SameMatrix(mesh.localToWorldMatrix, world), Is.True, "The prop stays where it is.");
            Assert.That((result.Sources, result.Kept, result.Omitted), Is.EqualTo((1, 1, 0)));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Kept));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.KeptParent));
            Assert.That(f.Entry.Path, Is.EqualTo("Other parent/Other"), "The result points at the object in the user's avatar.");
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.Empty);
            Assert.That(f.Copy.GetComponent<Vrm10Instance>(), Is.Null, "Moving a transform needs no VRM constraint.");
            Assert.That(f.Warnings.Single(), Is.EqualTo("コンストレイント変換: 元1件 / 変換1件（うち近似0件） / 省略0件。"));
            Assert.That(f.Source.GetComponentsInChildren<Component>().Length, Is.EqualTo(1), "The source avatar is not touched.");
        }

        [TestCase(true)][TestCase(false)]
        public void ChainedParentConstraintsMoveInAnyOrder(bool secondFirst)
        {
            using var f = new Fixture();
            var second = Fixture.Child(f.Copy.transform, "Second", Vector3.left);
            // First: read before the constraint it hangs from.
            if (secondFirst) second.SetSiblingIndex(0);
            f.Constraint<ParentConstraint>(second, 1, f.Other);
            f.Constraint<ParentConstraint>(f.Other, 1, f.A);
            f.Convert();
            Assert.That((second.parent, f.Other.parent), Is.EqualTo((f.Other, f.A)));
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EqualTo(new[] { ConstraintExport.KeptParent, ConstraintExport.KeptParent }));
            Assert.That(f.Report.Results.Select(r => r.Path), Is.EquivalentTo(new[] { "Second", "Other parent/Other" }), "Paths are taken before anything moves.");
        }

        [TestCase(false)][TestCase(true)]
        public void APropUsedByVrmSettingsKeepsThePose(bool firstPerson)
        {
            // UniVRM finds these renderers by path when it writes the file.
            using var f = new Fixture();
            Fixture.Child(f.Other, "Mesh", Vector3.zero);
            var settings = ScriptableObject.CreateInstance<VRM10Object>();
            var expression = ScriptableObject.CreateInstance<VRM10Expression>();
            try
            {
                if (firstPerson)
                    settings.FirstPerson.Renderers.Add(new RendererFirstPersonFlags { Renderer = "Other parent/Other/Mesh",
                        FirstPersonFlag = VRVlog.LilToonExporter.Vendor.UniGLTF.Extensions.VRMC_vrm.FirstPersonType.thirdPersonOnly });
                else
                {
                    expression.MorphTargetBindings = new[] { new MorphTargetBinding { RelativePath = "Other parent/Other/Mesh", Index = 0, Weight = 1 } };
                    settings.Expression.Happy = expression;
                }
                f.Copy.AddComponent<Vrm10Instance>().Vrm = settings;
                f.Constraint<ParentConstraint>(f.Other, 1, f.A);
                f.Convert();
                Assert.That(f.Other.parent.name, Is.EqualTo("Other parent"));
                Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedBound));
            }
            finally { Object.DestroyImmediate(settings); Object.DestroyImmediate(expression); }
        }

        [TestCase("translation axis")][TestCase("rotation axis")][TestCase("weight")][TestCase("source weight")][TestCase("rotation source")]
        public void AParentConstraintThatAChildWouldNotMatchKeepsThePose(string condition)
        {
            using var f = new Fixture();
            var constraint = f.Constraint<ParentConstraint>(f.Other, 1, f.A);
            switch (condition)
            {
                case "translation axis": constraint.translationAxis = Axis.X | Axis.Y; break;
                case "rotation axis": constraint.rotationAxis = Axis.Y; break;
                case "weight": constraint.weight = .5f; break;
                case "source weight": constraint.SetSource(0, new ConstraintSource { sourceTransform = f.A, weight = .5f }); break;
                // A VRM rotation constraint reads the local rotation of its source.
                case "rotation source":
                    f.Constraint<RotationConstraint>(Fixture.Child(f.Other.parent, "Twin", Vector3.zero), 1, f.Other);
                    break;
            }
            f.Convert();
            Assert.That(f.Other.parent.name, Is.EqualTo("Other parent"));
            Assert.That(f.Report.Results.Single(r => r.Name.Contains(nameof(ParentConstraint))).Reason, Is.EqualTo(ConstraintExport.OmittedReparent));
        }

        [Test]
        public void SeveralSourcesOrASourceInsideTheTargetKeepThePose()
        {
            using var f = new Fixture();
            f.Constraint<ParentConstraint>(f.Other, 1, f.A, f.B);
            f.Constraint<ParentConstraint>(f.Parent, 1, f.A);
            f.Convert();
            Assert.That((f.Other.parent.name, f.Parent.parent), Is.EqualTo(("Other parent", f.Copy.transform)));
            Assert.That(f.Report.Results.Select(r => r.Reason), Is.EqualTo(new[] { ConstraintExport.OmittedSetting, ConstraintExport.OmittedSetting }));
        }

        [Test]
        public void AParentConstraintOnAHumanoidBoneKeepsTheSkeleton()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var animator = fixture.Copy.GetComponent<Animator>();
            var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            var forearm = hand.parent;
            var prop = new GameObject("Prop").transform; prop.SetParent(fixture.Copy.transform, false);
            var constraint = hand.gameObject.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = prop, weight = 1 }); constraint.constraintActive = true;
            var report = new ExportFallbackReport();
            ConstraintExport.Convert(fixture.Source, fixture.Copy, null, report);
            Assert.That(hand.parent, Is.EqualTo(forearm));
            Assert.That(report.Results.Single().Reason, Is.EqualTo(ConstraintExport.OmittedReparent));
        }

        static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (var i = 0; i < 16; i++) if (Mathf.Abs(a[i] - b[i]) > 1e-4f) return false;
            return true;
        }

        [TestCase("weightless")][TestCase("sourceless")]
        public void ConstraintsThatNeverMoveAnythingAreNotListed(string condition)
        {
            using var f = new Fixture();
            var constraint = f.Constraint<RotationConstraint>(f.B, 1, condition == "sourceless" ? new Transform[0] : new[] { f.A });
            if (condition == "weightless") constraint.weight = 0;
            var result = f.Convert();
            Assert.That(result.Sources, Is.Zero);
            Assert.That(f.Report.Results, Is.Empty);
            Assert.That(f.Warnings, Is.Empty);
        }

        [Test]
        public void AnUnlockedUnityConstraintIsStillConverted()
        {
            // Lock only matters while editing; in Play Mode and in the app the
            // target moves the same as a locked one.
            using var f = new Fixture();
            f.Constraint<RotationConstraint>(f.B, 1, f.A).locked = false;
            f.A.localRotation = f.B.localRotation = Quaternion.identity;
            var result = f.Convert();
            Assert.That(f.B.GetComponent<Vrm10RotationConstraint>().Source, Is.EqualTo(f.A));
            Assert.That((result.Sources, result.Kept, result.Omitted), Is.EqualTo((1, 1, 0)));
        }

        [TestCase("disabled")][TestCase("inactive")][TestCase("hidden")]
        public void AnOffConstraintKeepsThePoseAndIsListed(string condition)
        {
            using var f = new Fixture();
            var constraint = f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.A.localRotation = f.B.localRotation = Quaternion.identity;
            switch (condition)
            {
                case "disabled": constraint.enabled = false; break;
                case "inactive": constraint.constraintActive = false; break;
                case "hidden": f.B.gameObject.SetActive(false); break;
            }
            var result = f.Convert();
            AssertListedAsOmitted(f, result, ConstraintExport.OmittedOff);
        }

        static void AssertListedAsOmitted(Fixture f, ConstraintExport.Result result, string reason)
        {
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(true), Is.Empty);
            Assert.That(f.Copy.GetComponent<Vrm10Instance>(), Is.Null);
            Assert.That((result.Sources, result.Kept, result.Approximated, result.Omitted), Is.EqualTo((1, 0, 0, 1)));
            Assert.That(f.Entry.Outcome, Is.EqualTo(ExportOutcome.Omitted));
            Assert.That(f.Entry.Category, Is.EqualTo(ConstraintExport.Category));
            Assert.That(f.Entry.Reason, Is.EqualTo(reason));
            Assert.That(f.Entry.Name, Is.EqualTo("B (" + nameof(RotationConstraint) + ")"));
            Assert.That(f.Entry.Path, Is.EqualTo("Parent/B"));
            Assert.That(f.Warnings.Single(), Is.EqualTo("コンストレイント変換: 元1件 / 変換0件（うち近似0件） / 省略1件。"));
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
        public void AConstraintThatClosesACycleWithAnAuthoredVrmConstraintKeepsThePose()
        {
            // VRMC_node_constraint forbids cycles; an authored A <- B plus a
            // converted B <- A would make the file invalid.
            using var f = new Fixture();
            var authored = f.A.gameObject.AddComponent<Vrm10RotationConstraint>(); authored.Source = f.B; authored.Weight = 1;
            f.Constraint<RotationConstraint>(f.B, 1, f.A);
            f.A.localRotation = f.B.localRotation = Quaternion.Euler(0, 0, 20);
            var result = f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>(), Is.EqualTo(new IVrm10Constraint[] { authored }));
            Assert.That(result.Omitted, Is.EqualTo(1));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedCycle));
        }

        [Test]
        public void ConstraintsThatSourceEachOtherConvertOnlyOneSide()
        {
            using var f = new Fixture();
            var c = Fixture.Child(f.Parent, "C", Vector3.zero);
            f.Constraint<RotationConstraint>(f.A, 1, f.B);
            f.Constraint<RotationConstraint>(f.B, 1, c);
            f.Constraint<RotationConstraint>(c, 1, f.A);
            f.A.localRotation = f.B.localRotation = c.localRotation = Quaternion.identity;
            f.Convert();
            Assert.That(f.Copy.GetComponentsInChildren<IVrm10Constraint>().Length, Is.EqualTo(2));
            Assert.That(f.Report.Results.Select(r => r.Reason),
                Is.EquivalentTo(new[] { ConstraintExport.KeptRotation, ConstraintExport.KeptRotation, ConstraintExport.OmittedCycle }));
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
        public void ALocalSpaceRotationAcrossParentsComparesTheLocalRest()
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
                settings.FindProperty("SolveInLocalSpace").boolValue = true;
                settings.FindProperty("Sources.totalLength").intValue = 1;
                settings.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = f.Other;
                settings.FindProperty("Sources.source0.Weight").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            // Same world rotation, 90 degrees apart in local space.
            f.Parent.localRotation = f.Other.localRotation = Quaternion.Euler(0, 0, 90);
            f.Convert();
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedRest));
        }

        [TestCase(true)][TestCase(false)]
        public void AnUnlockedVrchatConstraintIsConvertedUnlessOff(bool isActive)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint")).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("Real VRChat SDK integration test; Unity constraints are covered without it.");
            using var f = new Fixture();
            var component = f.B.gameObject.AddComponent(type);
            using (var settings = new SerializedObject(component))
            {
                settings.FindProperty("IsActive").boolValue = isActive;
                settings.FindProperty("Locked").boolValue = false;
                settings.FindProperty("GlobalWeight").floatValue = 1;
                settings.FindProperty("Sources.totalLength").intValue = 1;
                settings.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = f.A;
                settings.FindProperty("Sources.source0.Weight").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            f.A.localRotation = f.B.localRotation = Quaternion.identity;
            var result = f.Convert();
            Assert.That(f.Entry.Name, Does.Contain("VRCRotationConstraint"));
            if (isActive)
            {
                // VRChat drives an unlocked constraint at runtime the same as a locked one.
                Assert.That(f.B.GetComponent<Vrm10RotationConstraint>().Source, Is.EqualTo(f.A));
                Assert.That((result.Sources, result.Kept, result.Omitted), Is.EqualTo((1, 1, 0)));
                return;
            }
            Assert.That(f.B.GetComponent<IVrm10Constraint>(), Is.Null);
            Assert.That(result.Omitted, Is.EqualTo(1));
            Assert.That(f.Entry.Reason, Is.EqualTo(ConstraintExport.OmittedOff));
            Assert.That(f.Warnings.Single(), Does.Contain("省略1件"));
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
        public void AFullExportWritesAParentConstrainedPropUnderItsSource()
        {
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Requires installed lilToon for the full exporter entry point.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(shader);
            foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            var hand = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.RightHand);
            var prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prop.name = "Held prop";
            Object.DestroyImmediate(prop.GetComponent<Collider>());
            prop.GetComponent<MeshRenderer>().sharedMaterial = material;
            prop.transform.SetParent(fixture.Source.transform, false);
            prop.transform.SetPositionAndRotation(hand.position + new Vector3(.05f, 0, 0), Quaternion.Euler(0, 0, 30));
            prop.transform.localScale = Vector3.one * .1f;
            var constraint = prop.AddComponent<ParentConstraint>();
            constraint.AddSource(new ConstraintSource { sourceTransform = hand, weight = 1 });
            constraint.locked = true; constraint.constraintActive = true;
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
            var held = nodes.FindIndex(node => (string)node["name"] == "Held prop");
            var parent = nodes.Single(node => node.TryGetValue("children", out var children) && ((List<object>)children).Select(Convert.ToInt32).Contains(held));
            Assert.That((string)parent["name"], Is.EqualTo("RightHand"));
            Assert.That(report.Fallbacks.Results.Single(r => r.Category == ConstraintExport.Category).Reason, Is.EqualTo(ConstraintExport.KeptParent));
            Assert.That(prop.transform.parent, Is.EqualTo(fixture.Source.transform), "Only the export copy is moved.");
        }

        [Test]
        public void AFullExportWritesAVrchatRotationConstraint()
        {
            // The owned copy drops edit-mode scripts before activation; the
            // VRChat constraints must survive it to be converted.
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("VRC.SDK3.Dynamics.Constraint.Components.VRCRotationConstraint")).FirstOrDefault(t => t != null);
            if (type == null) Assert.Ignore("Install the real VRChat SDK with VRCRotationConstraint.");
            var shader = Shader.Find("lilToon");
            if (shader == null) Assert.Ignore("Requires installed lilToon for the full exporter entry point.");
            using var fixture = new AttachmentConnectionTests.Fixture();
            var material = new Material(shader);
            foreach (var renderer in fixture.Source.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = material;
            var hips = fixture.Source.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Hips);
            var a = Fixture.Child(hips, "Constraint source", new Vector3(.1f, -.1f, 0));
            var b = Fixture.Child(hips, "Constraint target", new Vector3(-.1f, -.1f, 0));
            var component = b.gameObject.AddComponent(type);
            using (var settings = new SerializedObject(component))
            {
                settings.FindProperty("IsActive").boolValue = true;
                settings.FindProperty("Locked").boolValue = true;
                settings.FindProperty("GlobalWeight").floatValue = 1;
                settings.FindProperty("Sources.totalLength").intValue = 1;
                settings.FindProperty("Sources.source0.SourceTransform").objectReferenceValue = a;
                settings.FindProperty("Sources.source0.Weight").floatValue = 1;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
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
            Assert.That(target.ContainsKey("extensions"), Is.True, "The VRChat constraint is converted on the export copy.");
            var extension = (Dictionary<string, object>)((Dictionary<string, object>)target["extensions"])["VRMC_node_constraint"];
            var rotation = (Dictionary<string, object>)((Dictionary<string, object>)extension["constraint"])["rotation"];
            Assert.That((string)nodes[Convert.ToInt32(rotation["source"])]["name"], Is.EqualTo("Constraint source"));
            Assert.That(report.Fallbacks.Results.Single(r => r.Category == ConstraintExport.Category).Name, Does.Contain("VRCRotationConstraint"));
            Assert.That(b.GetComponent<IVrm10Constraint>(), Is.Null, "Only the export copy gets the VRM constraint.");
            Assert.That(b.GetComponent(type), Is.Not.Null, "The source avatar keeps its VRChat constraint.");
        }
    }
}
