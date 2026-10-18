using UnityEngine;

namespace VRVlog.LilToonExporter
{
    internal static class HumanoidBones
    {
        // An Animator that has never been active in this session is not bound,
        // and GetBoneTransform can return null even for a mapped bone. A hidden
        // scene avatar is read without activating it, so fall back to the bone
        // name in the humanoid description. Only a unique name under the
        // Animator counts; an ambiguous name resolves to nothing.
        internal static Transform Resolve(Animator animator, HumanBodyBones bone)
        {
            if (animator == null || bone < 0 || bone >= HumanBodyBones.LastBone) return null;
            var found = animator.GetBoneTransform(bone);
            // A bound Animator already answered; null there means unmapped or lost.
            if (found != null || animator.isInitialized) return found;
            var avatar = animator.avatar;
            if (avatar == null || !avatar.isValid || !avatar.isHuman) return null;
            var humanName = HumanTrait.BoneName[(int)bone];
            string boneName = null;
            foreach (var human in avatar.humanDescription.human)
                if (human.humanName == humanName) { boneName = human.boneName; break; }
            if (string.IsNullOrEmpty(boneName)) return null;
            Transform match = null;
            foreach (var transform in animator.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name != boneName) continue;
                if (match != null) return null;
                match = transform;
            }
            return match;
        }
    }
}
