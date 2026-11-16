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

        // A player drives the arm by muscles measured from the VRM's rest, so
        // the elbow keeps the bend of the humanoid's T-pose, not the one the
        // lowered arm was authored with.
        [TestCase(0f, 12f)]
        [TestCase(7f, 12f)]
        [TestCase(7f, 2f)]
        public void ARaisedForearmTakesTheBendOfTheHumanoidsTPose(float humanoid, float authored)
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm); var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var rightLower = Bone(f.Source, HumanBodyBones.RightLowerArm); var rightHand = Bone(f.Source, HumanBodyBones.RightHand);
            var avatar = BendHumanoidElbows(f.Source, humanoid);
            try
            {
                LowerArms(f.Source, 40);
                lower.Rotate(Vector3.forward, authored - humanoid, Space.World);
                rightLower.Rotate(Vector3.forward, humanoid - authored, Space.World);

                AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.EqualTo(humanoid).Within(.1f));
                Assert.That(AngleFrom(Vector3.right, rightLower, rightHand), Is.EqualTo(humanoid).Within(.1f));
                if (humanoid > 0) Assert.That(hand.position.y, Is.LessThan(lower.position.y), "the forearm bends down as in the humanoid");
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        // The upper arm takes the humanoid's T-pose too, its slope and its twist,
        // so a muscle value turns the shoulder as far as it did on the avatar.
        [Test]
        public void ARaisedUpperArmTakesTheSlopeAndTwistOfTheHumanoidsTPose()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var upper = Bone(f.Source, HumanBodyBones.LeftUpperArm); var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm);
            upper.Rotate(Vector3.forward, 3, Space.World);
            upper.Rotate(Vector3.right, 30, Space.World);
            var humanoid = upper.localRotation;
            var avatar = Rebuild(f.Source, description =>
            {
                description.skeleton = description.skeleton.Select(bone =>
                {
                    if (bone.name == upper.name) bone.rotation = humanoid;
                    return bone;
                }).ToArray();
                return description;
            });
            try
            {
                upper.Rotate(Vector3.right, -30, Space.World);
                LowerArms(f.Source, 40);

                AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

                Assert.That(Quaternion.Angle(upper.localRotation, humanoid), Is.LessThan(.1f));
                Assert.That(AngleFrom(Vector3.left, upper, lower), Is.EqualTo(3f).Within(.1f));
            }
            finally { Object.DestroyImmediate(avatar); }
        }

        [Test]
        public void AHumanoidElbowBentPastTheLimitIsStraightenedAsBefore()
        {
            using var f = new AttachmentConnectionTests.Fixture();
            var lower = Bone(f.Source, HumanBodyBones.LeftLowerArm); var hand = Bone(f.Source, HumanBodyBones.LeftHand);
            var avatar = BendHumanoidElbows(f.Source, AvatarRestPose.MaximumArmAngle + 5);
            try
            {
                LowerArms(f.Source, 40);

                AvatarRestPose.AlignArms(f.Source.GetComponent<Animator>());

                Assert.That(AngleFrom(Vector3.left, lower, hand), Is.LessThan(.1f));
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
