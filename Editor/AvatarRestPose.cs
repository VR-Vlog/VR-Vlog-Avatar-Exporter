using System.Collections.Generic;
using System.Linq;
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

        // tPose, when given, gets the rotation each raised bone takes in the
        // humanoid's own T-pose, as a normalized pose in the animator's axes (see
        // HumanoidTPose). The rest itself stays a plain T-pose.
        internal static void AlignArms(Animator animator, IDictionary<HumanBodyBones, Quaternion> tPose = null)
        {
            if (animator == null || !animator.isHuman) return;
            var outward = animator.transform.right;
            AlignArm(animator, -outward, tPose, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AlignArm(animator, outward, tPose, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        }

        static void AlignArm(Animator animator, Vector3 outward, IDictionary<HumanBodyBones, Quaternion> tPose, params HumanBodyBones[] arm)
        {
            var bones = arm.Select(bone => HumanoidBones.Resolve(animator, bone)).ToArray();
            if (bones.Any(bone => bone == null)) return;
            Transform upperBone = bones[0], lowerBone = bones[1], handBone = bones[2];
            if (!IsPast(upperBone, lowerBone, outward) && !IsPast(lowerBone, handBone, outward)) return;
            var humanoid = tPose == null ? null : HumanoidTPose(animator, outward, bones);
            // Parent first: raising the upper arm carries the forearm with it. The upper
            // arm goes even when only the forearm is past, or the elbow would bend backwards.
            AlignBone(upperBone, lowerBone, outward);
            if (IsPast(lowerBone, handBone, outward)) AlignBone(lowerBone, handBone, outward);
            if (humanoid == null) return;
            // Each bone's turn from the raised rest to the humanoid's T-pose, in the
            // animator's axes, and from it the turn of each bone from its parent's.
            // The shoulder above does not move, so the upper arm's turn is its own.
            var root = Quaternion.Inverse(animator.transform.rotation);
            var parent = Quaternion.identity;
            for (var i = 0; i < bones.Length; i++)
            {
                var turn = humanoid[i] * Quaternion.Inverse(root * bones[i].rotation);
                tPose[arm[i]] = Quaternion.Inverse(parent) * turn;
                parent = turn;
            }
        }

        // The rotations, in the animator's axes, the upper arm, the forearm and
        // the hand have when each takes its rotation from its parent in the
        // humanoid's T-pose, or null when the humanoid does not name each of them
        // once or its T-pose would itself leave the arm past the limit. A muscle
        // player that builds its humanoid from this pose gives the arm the pose a
        // muscle value gave it on the avatar, with its slope, elbow bend and twist.
        // See Documentation~/decisions/2026-11-17-arm-t-pose-in-extras.md.
        static Quaternion[] HumanoidTPose(Animator animator, Vector3 outward, Transform[] bones)
        {
            var avatar = animator.avatar;
            if (avatar == null || !avatar.isHuman) return null;
            // A name the skeleton has twice does not say which bone it is.
            var skeleton = new Dictionary<string, SkeletonBone?>();
            foreach (var bone in avatar.humanDescription.skeleton ?? System.Array.Empty<SkeletonBone>())
                if (!string.IsNullOrEmpty(bone.name)) skeleton[bone.name] = skeleton.ContainsKey(bone.name) ? (SkeletonBone?)null : bone;
            var rotations = new Quaternion[bones.Length];
            for (var i = 0; i < bones.Length; i++)
            {
                if (!skeleton.TryGetValue(bones[i].name, out var found) || !(found is SkeletonBone rest)) return null;
                rotations[i] = rest.rotation;
            }
            var before = bones.Select(bone => bone.localRotation).ToArray();
            try
            {
                for (var i = 0; i < bones.Length; i++) bones[i].localRotation = rotations[i];
                if (IsPast(bones[0], bones[1], outward) || IsPast(bones[1], bones[2], outward)) return null;
                var root = Quaternion.Inverse(animator.transform.rotation);
                return bones.Select(bone => root * bone.rotation).ToArray();
            }
            finally
            {
                for (var i = 0; i < bones.Length; i++) bones[i].localRotation = before[i];
            }
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
