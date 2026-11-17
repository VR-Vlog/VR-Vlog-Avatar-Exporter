using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.Poses;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class AvatarRestPoseTests
    {
        static Transform Bone(GameObject avatar, HumanBodyBones bone) => avatar.GetComponent<Animator>().GetBoneTransform(bone);

        // Rotates both shoulders about the body's forward axis so the arms come down.
        static void LowerArms(GameObject avatar, float angle)
        {
            Bone(avatar, HumanBodyBones.LeftUpperArm).Rotate(Vector3.forward, angle, Space.World);
            Bone(avatar, HumanBodyBones.RightUpperArm).Rotate(Vector3.forward, -angle, Space.World);
        }

        static float AngleFrom(Vector3 outward, Transform bone, Transform child) => Vector3.Angle(child.position - bone.position, outward);

        // Bends both elbows down by the angle and rebuilds the humanoid from that
        // T-pose, as an imported model whose arms are not quite straight. The
        // caller destroys the returned avatar.
        static Avatar BendHumanoidElbows(GameObject source, float angle)
        {
            var lower = new[] { Bone(source, HumanBodyBones.LeftLowerArm), Bone(source, HumanBodyBones.RightLowerArm) };
            lower[0].Rotate(Vector3.forward, angle, Space.World);
            lower[1].Rotate(Vector3.forward, -angle, Space.World);
            return Rebuild(source, description =>
            {
                description.skeleton = description.skeleton.Select(bone =>
                {
                    if (lower.FirstOrDefault(t => t.name == bone.name) is Transform t) bone.rotation = t.localRotation;
                    return bone;
                }).ToArray();
                return description;
            });
        }

        // Builds the fixture's humanoid again from its description, changed. The
        // caller destroys the returned avatar.
        internal static Avatar Rebuild(GameObject source, System.Func<HumanDescription, HumanDescription> change)
        {
            var animator = source.GetComponent<Animator>();
            // The fixture adds a second "Head" after building its humanoid; a
            // name mapped to a human bone must be unique while building.
            var hair = source.transform.Find("Independent hair/Head");
            if (hair != null) hair.name = "Hair joint";
            try
            {
                var avatar = AvatarBuilder.BuildHumanAvatar(source, change(animator.avatar.humanDescription));
                Assert.That(avatar.isValid && avatar.isHuman, Is.True);
                animator.avatar = avatar;
                return avatar;
            }
            finally { if (hair != null) hair.name = "Head"; }
        }

        [Test]
        public void ArmsLoweredPastTheLimitReachTheTPoseWithEverythingBelowThem()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var leftHand = Bone(f.Source, HumanBodyBones.LeftHand); var rightHand = Bone(f.Source, HumanBodyBones.RightHand);
            var expectedLeft = leftHand.position; var expectedRight = rightHand.position;
            LowerArms(f.Source, 40);
            Assert.That(Vector3.Distance(leftHand.position, expectedLeft), Is.GreaterThan(.1f));

            AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

            Assert.That(Vector3.Distance(leftHand.position, expectedLeft), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(rightHand.position, expectedRight), Is.LessThan(.001f));
            Assert.That(AngleFrom(Vector3.left, Bone(f.Source, HumanBodyBones.LeftUpperArm), Bone(f.Source, HumanBodyBones.LeftLowerArm)), Is.LessThan(.1f));
            Assert.That(AngleFrom(Vector3.right, Bone(f.Source, HumanBodyBones.RightLowerArm), rightHand), Is.LessThan(.1f));
        }

        [Test]
        public void ABentElbowPastTheLimitIsStraightenedOnItsOwn()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var upper = Bone(f.Source, HumanBodyBones.LeftUpperArm); var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm);
            var upperRotation = upper.rotation;
            lower.Rotate(Vector3.forward, 35, Space.World);

            AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

            Assert.That(Quaternion.Angle(upper.rotation, upperRotation), Is.LessThan(.01f));
            Assert.That(AngleFrom(Vector3.left, lower, Bone(f.Source, HumanBodyBones.LeftHand)), Is.LessThan(.1f));
        }

        [Test]
        public void AForearmPastTheLimitLiftsTheUpperArmSoTheElbowDoesNotBendBackwards()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var upper = Bone(f.Source, HumanBodyBones.LeftUpperArm); var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm);
            var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var avatar = BendHumanoidElbows(f.Source, 10);
            try
            {
                LowerArms(f.Source, 15);
                lower.Rotate(Vector3.forward, 10, Space.World);

                AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

                Assert.That(AngleFrom(Vector3.left, upper, lower), Is.LessThan(.1f));
                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.EqualTo(10f).Within(.1f));
                Assert.That(hand.position.y, Is.LessThan(lower.position.y), "the forearm keeps bending down");
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        // Turns the arms from the rest by the recorded T-pose as a muscle player
        // does with a humanoid built from it (UniVRM's ControlRig: each bone turns
        // in the animator's axes by its normalized rotation and its parents').
        static void TakeTPose(GameObject avatar, IReadOnlyDictionary<HumanBodyBones, Quaternion> tPose)
        {
            var root = avatar.transform.rotation;
            foreach (var arm in new[]
            {
                new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand },
                new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }
            })
            {
                var bones = arm.Select(bone => Bone(avatar, bone)).ToArray();
                var rest = bones.Select(bone => bone.rotation).ToArray();
                var turn = Quaternion.identity;
                for (var i = 0; i < arm.Length; i++)
                {
                    if (tPose.TryGetValue(arm[i], out var rotation)) turn *= rotation;
                    bones[i].rotation = root * turn * Quaternion.Inverse(root) * rest[i];
                }
            }
        }

        static Dictionary<HumanBodyBones, Quaternion> AlignArms(GameObject avatar)
        {
            var tPose = new Dictionary<HumanBodyBones, Quaternion>();
            AvatarRestPose.AlignArms(avatar.GetComponent<Animator>(), tPose);
            return tPose;
        }

        // The rest stays a plain T-pose for players that copy rotations; a
        // player that drives the arm by muscles builds its humanoid from the
        // recorded T-pose, which gives the elbow the bend of the humanoid's
        // T-pose, not the one the lowered arm was authored with.
        [TestCase(0f, 12f)]
        [TestCase(7f, 12f)]
        [TestCase(7f, 2f)]
        public void ARaisedArmKeepsItsRestAndRecordsTheBendOfTheHumanoidsTPose(float humanoid, float authored)
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var upper = Bone(f.Source, HumanBodyBones.LeftUpperArm);
            var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm); var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var rightLower = Bone(f.Source, HumanBodyBones.RightLowerArm); var rightHand = Bone(f.Source, HumanBodyBones.RightHand);
            var straight = (lower.localRotation, rightLower.localRotation);
            var avatar = BendHumanoidElbows(f.Source, humanoid);
            try
            {
                (lower.localRotation, rightLower.localRotation) = straight;
                lower.Rotate(Vector3.forward, authored, Space.World);
                rightLower.Rotate(Vector3.forward, -authored, Space.World);
                LowerArms(f.Source, 40);

                var tPose = AlignArms(f.Source);

                Assert.That(AngleFrom(Vector3.left, upper, lower), Is.LessThan(.1f));
                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.EqualTo(authored).Within(.1f), "the rest keeps the authored bend under the limit");
                Assert.That(tPose.Keys, Is.EquivalentTo(new[]
                {
                    HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                    HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
                }));
                TakeTPose(f.Source, tPose);
                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.EqualTo(humanoid).Within(.1f));
                Assert.That(AngleFrom(Vector3.right, rightLower, rightHand), Is.EqualTo(humanoid).Within(.1f));
                if (humanoid > 0) Assert.That(hand.position.y, Is.LessThan(lower.position.y), "the forearm bends down as in the humanoid");
                foreach (var bone in new[] { upper, lower, hand })
                    Assert.That(Quaternion.Angle(bone.localRotation, avatar.humanDescription.skeleton.First(b => b.name == bone.name).rotation), Is.LessThan(.1f), bone.name);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        // The recorded T-pose has the upper arm's slope and twist too, so a
        // muscle value turns the shoulder as far as it did on the avatar. The
        // forearm's and the hand's rotations are from the upper arm's, so the
        // twist is not added again below the elbow. The rotations are in the
        // animator's axes, whichever way it faces.
        [Test]
        public void TheRecordedTPoseHasTheSlopeAndTwistOfTheHumanoidsUpperArm()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var upper = Bone(f.Source, HumanBodyBones.LeftUpperArm); var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm);
            var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var straight = (upper.localRotation, lower.localRotation);
            upper.Rotate(Vector3.forward, 3, Space.World);
            upper.Rotate(Vector3.right, 30, Space.World);
            lower.Rotate(Vector3.forward, 10, Space.World);
            var humanoid = (upper: upper.localRotation, lower: lower.localRotation, hand: hand.localRotation);
            var avatar = Rebuild(f.Source, description =>
            {
                description.skeleton = description.skeleton.Select(bone =>
                {
                    if (bone.name == upper.name) bone.rotation = humanoid.upper;
                    if (bone.name == lower.name) bone.rotation = humanoid.lower;
                    return bone;
                }).ToArray();
                return description;
            });
            try
            {
                (upper.localRotation, lower.localRotation) = straight;
                upper.Rotate(Vector3.forward, 3, Space.World);
                LowerArms(f.Source, 40);
                f.Source.transform.rotation = Quaternion.Euler(0, 50, 0);
                var left = f.Source.transform.rotation * Vector3.left;

                var tPose = AlignArms(f.Source);

                Assert.That(AngleFrom(left, upper, lower), Is.LessThan(.1f));
                TakeTPose(f.Source, tPose);
                Assert.That(Quaternion.Angle(upper.localRotation, humanoid.upper), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(lower.localRotation, humanoid.lower), Is.LessThan(.1f));
                Assert.That(Quaternion.Angle(hand.localRotation, humanoid.hand), Is.LessThan(.1f));
                Assert.That(AngleFrom(left, upper, lower), Is.EqualTo(3f).Within(.1f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void AHumanoidElbowBentPastTheLimitRecordsNoTPose()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm); var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var avatar = BendHumanoidElbows(f.Source, AvatarRestPose.MaximumArmAngle + 5);
            try
            {
                LowerArms(f.Source, 40);

                var tPose = AlignArms(f.Source);

                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.LessThan(.1f));
                Assert.That(tPose, Is.Empty);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        // The humanoid's T-pose bends both elbows; an arm whose forearm it
        // cannot name once records nothing, the other records the bend.
        [TestCase(false)]
        [TestCase(true)]
        public void AnArmTheHumanoidDoesNotNameOnceRecordsNoTPose(bool twice)
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var left = (Bone(f.Source, HumanBodyBones.LeftLowerArm), Bone(f.Source, HumanBodyBones.LeftHand));
            var right = (Bone(f.Source, HumanBodyBones.RightLowerArm), Bone(f.Source, HumanBodyBones.RightHand));
            var avatar = BendHumanoidElbows(f.Source, 10);
            Avatar named = null;
            try
            {
                if (twice)
                    named = Rebuild(f.Source, description =>
                    {
                        var copy = description.skeleton.First(bone => bone.name == left.Item1.name);
                        copy.rotation = Quaternion.identity;
                        description.skeleton = description.skeleton.Append(copy).ToArray();
                        return description;
                    });
                LowerArms(f.Source, 40);
                if (!twice) left.Item1.name = "Renamed forearm";

                var tPose = AlignArms(f.Source);

                Assert.That(tPose.Keys, Is.EquivalentTo(new[] { HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand }));
                TakeTPose(f.Source, tPose);
                Assert.That(AngleFrom(Vector3.left, left.Item1, left.Item2), Is.LessThan(.1f));
                Assert.That(AngleFrom(Vector3.right, right.Item1, right.Item2), Is.EqualTo(10).Within(.1f));
            }
            finally { Object.DestroyImmediate(avatar); if (named != null) Object.DestroyImmediate(named); }
        }

        // Arms that are not raised keep their rest and need no T-pose.
        [Test]
        public void ArmsWithinTheLimitRecordNoTPose()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var avatar = BendHumanoidElbows(f.Source, 10);
            try
            {
                LowerArms(f.Source, AvatarRestPose.MaximumArmAngle - 1);

                Assert.That(AlignArms(f.Source), Is.Empty);
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void ArmsWithinTheLimitStayExactlyAsAuthored()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            LowerArms(f.Source, AvatarRestPose.MaximumArmAngle - 1);
            var bones = f.Source.GetComponentsInChildren<Transform>(true);
            var before = bones.Select(bone => (bone.localPosition, bone.localRotation)).ToArray();

            AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

            Assert.That(bones.Select(bone => (bone.localPosition, bone.localRotation)), Is.EqualTo(before));
        }

        [Test]
        public void AlignedArmsStayPutWhenAlignedAgain()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            LowerArms(f.Source, 40);
            var animator = f.Source.GetComponent<Animator>();
            AvatarRestPose.AlignArms(animator);
            var aligned = Bone(f.Source, HumanBodyBones.LeftUpperArm).rotation;

            AvatarRestPose.AlignArms(animator);

            Assert.That(Bone(f.Source, HumanBodyBones.LeftUpperArm).rotation, Is.EqualTo(aligned));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PosesFromLoweredArmsAreTheDifferencesFromTheTPose(bool muscles)
        {
            using var t = new AttachmentConnectionTests.Fixture(); using var a = new AttachmentConnectionTests.Fixture();
            HumanoidPoseTests.AddFingers(t.Source); HumanoidPoseTests.AddFingers(a.Source);
            // The humanoid definition keeps the T-pose, as an imported model's does.
            LowerArms(a.Source, 40);
            var clip = muscles ? new AnimationClip { name = "Pose" } : HumanoidPoseTests.Clip(t.Source);
            if (muscles) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "Left Arm Down-Up"), AnimationCurve.Constant(0, 1, .5f));
            try
            {
                var candidate = new PoseCandidate { Id = "arm", Name = "Arm", Category = "", Source = "test" };
                candidate.Layers.Add(new PoseLayer { Clip = clip });
                var fromT = PoseSampling.Sample(t.Source, candidate);
                var fromA = PoseSampling.Sample(a.Source, candidate);
                foreach (var name in new[] { "leftUpperArm", "rightUpperArm" })
                {
                    var expected = Quat(fromT, name); var actual = Quat(fromA, name);
                    Assert.That(Quaternion.Angle(actual, expected), Is.LessThan(.5f), name);
                }
                // The arm no clip moves stays at the rest it would have on a T-pose avatar.
                Assert.That(Quaternion.Angle(Quat(fromA, "rightUpperArm"), Quaternion.identity), Is.LessThan(.5f));
                Assert.That(Quaternion.Angle(Quat(fromA, "leftUpperArm"), Quaternion.identity), Is.GreaterThan(1f));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        static Quaternion Quat(HumanoidPoseData data, string bone)
        {
            var q = data.Bones.Single(b => b.Name == bone).Rotation;
            return new Quaternion((float)q[0], (float)q[1], (float)q[2], (float)q[3]);
        }
    }
}
