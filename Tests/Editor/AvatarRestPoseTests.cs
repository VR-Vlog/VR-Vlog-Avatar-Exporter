using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
