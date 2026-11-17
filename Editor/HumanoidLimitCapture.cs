using System;
using System.Collections.Generic;
using UnityEngine;
using VRVlog.AvatarFormats;

namespace VRVlog.LilToonExporter
{
    // asset.extras[VRVLOG_humanoidLimits]: the muscle limits the avatar's
    // humanoid sets for its shoulders and arms. UniVRM rebuilds the humanoid of
    // a loaded VRM with Unity's default limits, so a motion played by muscles
    // turns an avatar whose own limits are narrower further than on the avatar.
    // With them, the arms' rotations in the humanoid's own T-pose that
    // AvatarRestPose found differ from the rest (tPose).
    // See Documentation~/decisions/2026-11-16-arm-rest-and-limits.md and
    // Documentation~/decisions/2026-11-17-arm-t-pose-in-extras.md.
    internal static class HumanoidLimitCapture
    {
        internal const string FailureWarning = "VR Vlog: 腕の動く範囲を書き出しませんでした: ";

        static readonly HumanBodyBones[] Arms =
        {
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand
        };

        // The arm bones whose limits are not Unity's defaults and the given
        // T-pose rotations (in the animator's axes, see AvatarRestPose.AlignArms),
        // or null when there are neither. A failure adds a warning and also
        // returns null.
        internal static HumanoidLimitData Capture(Animator animator, IReadOnlyDictionary<HumanBodyBones, Quaternion> tPose, ICollection<string> warnings)
        {
            try
            {
                var avatar = animator == null ? null : animator.avatar;
                if (avatar == null || !avatar.isHuman) return null;
                var names = new Dictionary<string, HumanBodyBones>(StringComparer.Ordinal);
                foreach (var bone in Arms) names[HumanTrait.BoneName[(int)bone]] = bone;
                var data = new HumanoidLimitData();
                foreach (var human in avatar.humanDescription.human ?? Array.Empty<HumanBone>())
                {
                    if (human.limit.useDefaultValues || !names.TryGetValue(human.humanName ?? "", out var bone)) continue;
                    var limit = human.limit;
                    data.Bones[VrmName(bone)] = new HumanoidLimitData.Limit
                    {
                        Min = Vector(limit.min), Max = Vector(limit.max), Center = Vector(limit.center), AxisLength = limit.axisLength
                    };
                }
                foreach (var pair in tPose ?? new Dictionary<HumanBodyBones, Quaternion>())
                    data.TPose[VrmName(pair.Key)] = Gltf(pair.Value);
                if (data.IsEmpty) return null;
                HumanoidLimitData.Write(data); // Out-of-contract values fail here, not in Inject.
                return data;
            }
            catch (Exception error) when (!ExportStageGuard.IsCancellation(error))
            {
                warnings?.Add(ExporterLocalization.T(FailureWarning) + error.Message);
                return null;
            }
        }

        internal static byte[] Inject(byte[] bytes, HumanoidLimitData data)
        {
            if (data == null) return bytes;
            var value = HumanoidLimitData.Write(data);
            var document = GlbDocument.Read(bytes);
            if (!document.Json.TryGetValue("asset", out var rawAsset) || !(rawAsset is Dictionary<string, object> asset))
                throw new InvalidOperationException("glTF asset is not an object.");
            var extras = asset.TryGetValue("extras", out var rawExtras) ? rawExtras as Dictionary<string, object> : null;
            if (rawExtras != null && extras == null) throw new InvalidOperationException("glTF asset.extras is not an object.");
            if (extras == null) asset["extras"] = extras = new Dictionary<string, object>();
            extras[HumanoidLimitData.Key] = value;
            return document.Write();
        }

        // LeftUpperArm -> leftUpperArm, the VRM 1.0 humanoid name.
        static string VrmName(HumanBodyBones bone)
        {
            var name = bone.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        static double[] Vector(Vector3 value) => new double[] { value.x, value.y, value.z };

        // glTF mirrors Unity's X axis, as UniVRM's ReverseX.
        static double[] Gltf(Quaternion value) => new double[] { value.x, -value.y, -value.z, value.w };
    }
}
