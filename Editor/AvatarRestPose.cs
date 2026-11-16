using System.Collections.Generic;
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
            // The arm takes the pose the humanoid's own T-pose gives it, from the
            // shoulder down: a player drives the arm by muscles measured from the
            // VRM's rest, so any difference from the humanoid's T-pose stays in every
            // motion. See Documentation~/decisions/2026-11-16-arm-rest-and-limits.md.
            if (TakeHumanoidPose(animator, outward, upperBone, lowerBone, handBone)) return;
            // Parent first: raising the upper arm carries the forearm with it. The upper
            // arm goes even when only the forearm is past, or the elbow would bend backwards.
            AlignBone(upperBone, lowerBone, outward);
            if (IsPast(lowerBone, handBone, outward)) AlignBone(lowerBone, handBone, outward);
        }

        // Gives the upper arm, the forearm and the hand their rotations in the
        // humanoid's T-pose, each from its parent. Leaves them as they were and
        // returns false when the humanoid does not name each of them once or its
        // T-pose would itself leave the arm past the limit.
        static bool TakeHumanoidPose(Animator animator, Vector3 outward, Transform upper, Transform lower, Transform hand)
        {
            var avatar = animator.avatar;
            if (avatar == null || !avatar.isHuman) return false;
            // A name the skeleton has twice does not say which bone it is.
            var skeleton = new Dictionary<string, SkeletonBone?>();
            foreach (var bone in avatar.humanDescription.skeleton ?? System.Array.Empty<SkeletonBone>())
                if (!string.IsNullOrEmpty(bone.name)) skeleton[bone.name] = skeleton.ContainsKey(bone.name) ? (SkeletonBone?)null : bone;
            var bones = new[] { upper, lower, hand };
            var rotations = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                if (!skeleton.TryGetValue(bones[i].name, out var found) || !(found is SkeletonBone rest)) return false;
                rotations[i] = rest.rotation;
            }
            var before = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++) { before[i] = bones[i].localRotation; bones[i].localRotation = rotations[i]; }
            if (!IsPast(upper, lower, outward) && !IsPast(lower, hand, outward)) return true;
            for (var i = 0; i < bones.Length; i++) bones[i].localRotation = before[i];
            return false;
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
