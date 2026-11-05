using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // VRM 1.0 takes the rest pose of a model to be a T-pose: whatever plays on
    // it replaces each bone's rest rotation with a pose measured from a T-pose.
    // An avatar authored with lowered arms keeps that difference in every pose,
    // so its arms are raised on an owned copy before anything reads the rest.
    // Only the transforms move, the skinning follows through the bind poses.
    internal static class AvatarRestPose
    {
        // Authored T-poses stay as they are, so their VRM does not change. Their
        // relaxed elbows reach about 16 degrees, well under a lowered A-pose arm.
        internal const float MaximumArmAngle = 20f;

        internal static void AlignArms(Animator animator)
        {
            if (animator == null || !animator.isHuman) return;
            var outward = animator.transform.right;
            AlignArm(animator, -outward, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AlignArm(animator, outward, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        }

        static void AlignArm(Animator animator, Vector3 outward, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones hand)
        {
            var upperBone = HumanoidBones.Resolve(animator, upper);
            var lowerBone = HumanoidBones.Resolve(animator, lower);
            var handBone = HumanoidBones.Resolve(animator, hand);
            if (upperBone == null || lowerBone == null || handBone == null) return;
            if (!IsPast(upperBone, lowerBone, outward) && !IsPast(lowerBone, handBone, outward)) return;
            // Parent first: raising the upper arm carries the forearm with it. The upper
            // arm goes even when only the forearm is past, or the elbow would bend backwards.
            AlignBone(upperBone, lowerBone, outward);
            if (IsPast(lowerBone, handBone, outward)) AlignBone(lowerBone, handBone, outward);
        }

        static bool IsPast(Transform bone, Transform child, Vector3 outward)
        {
            var direction = child.position - bone.position;
            return direction.sqrMagnitude >= 1e-10f && Vector3.Angle(direction, outward) > MaximumArmAngle;
        }

        static void AlignBone(Transform bone, Transform child, Vector3 outward)
        {
            var direction = child.position - bone.position;
            if (direction.sqrMagnitude < 1e-10f) return;
            bone.rotation = Quaternion.FromToRotation(direction, outward) * bone.rotation;
        }
    }
}
