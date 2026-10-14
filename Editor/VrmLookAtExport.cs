using System;
using System.Collections.Generic;
using System.Linq;
using UniVRM10;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // VRM 1.0 bone look-at for an avatar without its own VRM look-at. The
    // origin is the midpoint of the eyes. The range follows the VRChat Eye
    // Look poses when they can be measured; otherwise it uses UniVRM's
    // defaults, which are also what UniVRM applies when lookAt is absent.
    internal sealed class VrmLookAtExport
    {
        // VRM10ObjectLookAt's defaults in the pinned UniVRM.
        internal const float DefaultInputDegrees = 90f;
        internal const float DefaultOutputDegrees = 10f;
        // A pose that turns an eye by less than this was not authored.
        internal const float MinimumDegrees = 1f;
        internal const float MaximumDegrees = 90f;
        internal const string UnmeasuredWarning = "Eye Lookの設定から目の可動範囲を測れないため視線は既定の範囲で書き出しました（Avatar DescriptorのEye Lookを確認）";

        // Head-local position in Unity coordinates, as UniVRM's OffsetFromHead.
        internal Vector3 OffsetFromHead;
        internal bool Measured;
        // Measured limits in degrees. The map is one-to-one up to each limit,
        // as VRChat's Eye Look follows a gaze directly until its pose.
        internal float HorizontalInner, HorizontalOuter, VerticalDown, VerticalUp;

        // Returns null when the copy's own VRM settings already describe the
        // look-at, or when bone look-at cannot work (no humanoid head or eyes).
        internal static VrmLookAtExport Capture(GameObject source, GameObject copy, ICollection<string> warnings)
        {
            if (source == null || copy == null) return null;
            // UniVRM serializes VRM10Object.LookAt itself. Keep an edited one;
            // untouched defaults (for example a VRM10Object kept only for its
            // expressions) carry no look-at information of the avatar.
            var authoredLookAt = copy.GetComponent<Vrm10Instance>()?.Vrm?.LookAt;
            if (authoredLookAt != null && !IsUniVrmDefault(authoredLookAt)) return null;
            var animator = copy.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman) return null;
            var head = animator.GetBoneTransform(HumanBodyBones.Head);
            var left = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var right = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (head == null || left == null || right == null) return null;
            var offset = head.InverseTransformPoint((left.position + right.position) * .5f);
            if (!Finite(offset.x) || !Finite(offset.y) || !Finite(offset.z)) return null;
            var result = new VrmLookAtExport { OffsetFromHead = offset };
            var measured = TryMeasure(source, out var authored, out var inner, out var outer, out var down, out var up);
            if (measured)
            {
                result.Measured = true;
                result.HorizontalInner = inner; result.HorizontalOuter = outer;
                result.VerticalDown = down; result.VerticalUp = up;
            }
            else if (authored) warnings?.Add(ExporterLocalization.T(UnmeasuredWarning));
            return result;
        }

        // Writes lookAt when the exported humanoid still has a head and both
        // eyes for bone look-at. A lookAt already present here came from
        // untouched UniVRM defaults (Capture returns null for an edited one).
        internal byte[] Apply(byte[] bytes)
        {
            var glb = GlbDocument.Read(bytes);
            if (!(Child(glb.Json, "extensions") is Dictionary<string, object> extensions) ||
                !(Child(extensions, "VRMC_vrm") is Dictionary<string, object> vrm)) return bytes;
            if (!(Child(Child(vrm, "humanoid") as Dictionary<string, object>, "humanBones") is Dictionary<string, object> bones) ||
                !new[] { "head", "leftEye", "rightEye" }.All(bones.ContainsKey)) return bytes;
            vrm["lookAt"] = ToJson();
            return glb.Write();
        }

        internal Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> Map(float limit) => new Dictionary<string, object>
            {
                ["inputMaxValue"] = (double)(Measured ? limit : DefaultInputDegrees),
                ["outputScale"] = (double)(Measured ? limit : DefaultOutputDegrees),
            };
            return new Dictionary<string, object>
            {
                // glTF mirrors Unity's X axis; UniVRM writes OffsetFromHead.ReverseX().
                ["offsetFromHeadBone"] = new List<object> { (double)-OffsetFromHead.x, (double)OffsetFromHead.y, (double)OffsetFromHead.z },
                ["type"] = "bone",
                ["rangeMapHorizontalInner"] = Map(HorizontalInner),
                ["rangeMapHorizontalOuter"] = Map(HorizontalOuter),
                ["rangeMapVerticalDown"] = Map(VerticalDown),
                ["rangeMapVerticalUp"] = Map(VerticalUp),
            };
        }

        // authored: Eye Look is enabled on the descriptor, so a failed
        // measurement loses information the user set up and is reported.
        internal static bool TryMeasure(GameObject source, out bool authored, out float inner, out float outer, out float down, out float up)
        {
            authored = false; inner = outer = down = up = 0;
            var descriptor = source.GetComponents<Component>().FirstOrDefault(c => c != null &&
                c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
            if (!(VrChatExpressionMenu.Member(descriptor, "enableEyeLook") is bool enabled) || !enabled) return false;
            authored = true;
            var settings = VrChatExpressionMenu.Member(descriptor, "customEyeLookSettings");
            var leftEye = VrChatExpressionMenu.Member(settings, "leftEye") as Transform;
            var rightEye = VrChatExpressionMenu.Member(settings, "rightEye") as Transform;
            var animator = source.GetComponent<Animator>();
            // VRM rotates the humanoid eyes. Poses of other transforms do not
            // describe how those eyes turn.
            if (leftEye == null || rightEye == null || animator == null || !animator.isHuman ||
                animator.GetBoneTransform(HumanBodyBones.LeftEye) != leftEye ||
                animator.GetBoneTransform(HumanBodyBones.RightEye) != rightEye) return false;
            var poses = new[] { "eyesLookingStraight", "eyesLookingUp", "eyesLookingDown", "eyesLookingLeft", "eyesLookingRight" }
                .Select(name => VrChatExpressionMenu.Member(settings, name)).ToArray();
            if (!TryEye(source.transform, leftEye, poses, true, out var leftRange) ||
                !TryEye(source.transform, rightEye, poses, false, out var rightRange)) return false;
            // Stay within the authored limit of both eyes.
            inner = Mathf.Min(leftRange.Inner, rightRange.Inner);
            outer = Mathf.Min(leftRange.Outer, rightRange.Outer);
            down = Mathf.Min(leftRange.Down, rightRange.Down);
            up = Mathf.Min(leftRange.Up, rightRange.Up);
            return new[] { inner, outer, down, up }.All(value => Finite(value) && value >= MinimumDegrees && value <= MaximumDegrees);
        }

        internal struct EyeRange { internal float Inner, Outer, Down, Up; }

        // UniVRM turns each eye by yaw and pitch on top of its rest rotation,
        // in the world frame recorded at runtime start (the avatar's frame for
        // an unrotated root). Measure each pose the same way in the root's
        // frame, relative to the straight pose. Yaw is positive toward +X,
        // pitch positive upward.
        static bool TryEye(Transform root, Transform eye, object[] poses, bool leftEye, out EyeRange range)
        {
            range = default;
            var rotations = new Quaternion[poses.Length];
            for (var i = 0; i < poses.Length; i++)
                if (!TryPose(poses[i], leftEye, out rotations[i])) return false;
            var parent = Quaternion.Inverse(root.rotation) * (eye.parent != null ? eye.parent.rotation : Quaternion.identity);
            Vector2 Gaze(Quaternion pose)
            {
                var direction = parent * pose * Quaternion.Inverse(parent * rotations[0]) * Vector3.forward;
                return new Vector2(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                    Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg);
            }
            // Use the direction each side pose actually turns this eye, even
            // when the left and right poses are swapped.
            var sideA = Gaze(rotations[3]).x; var sideB = Gaze(rotations[4]).x;
            var towardPlusX = Mathf.Max(sideA, sideB);
            var towardMinusX = Mathf.Max(-sideA, -sideB);
            // The left eye sits at -X: its outer side is -X, its inner side +X.
            range = new EyeRange
            {
                Up = Gaze(rotations[1]).y,
                Down = -Gaze(rotations[2]).y,
                Outer = leftEye ? towardMinusX : towardPlusX,
                Inner = leftEye ? towardPlusX : towardMinusX,
            };
            return true;
        }

        // A linked pose keeps the same rotation for both eyes in the SDK editor.
        static bool TryPose(object pose, bool leftEye, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (pose == null) return false;
            var linked = VrChatExpressionMenu.Member(pose, "linked") is bool value && value;
            if (!(VrChatExpressionMenu.Member(pose, leftEye || linked ? "left" : "right") is Quaternion raw)) return false;
            var length = Mathf.Sqrt(raw.x * raw.x + raw.y * raw.y + raw.z * raw.z + raw.w * raw.w);
            if (!Finite(length) || length < 1e-4f) return false;
            rotation = new Quaternion(raw.x / length, raw.y / length, raw.z / length, raw.w / length);
            return true;
        }

        internal static bool IsUniVrmDefault(VRM10ObjectLookAt lookAt)
        {
            var defaults = new VRM10ObjectLookAt();
            bool Same(CurveMapper value, CurveMapper expected) => value != null &&
                value.CurveXRangeDegree == expected.CurveXRangeDegree && value.CurveYRangeDegree == expected.CurveYRangeDegree;
            return lookAt.LookAtType == defaults.LookAtType && lookAt.OffsetFromHead == defaults.OffsetFromHead &&
                Same(lookAt.HorizontalInner, defaults.HorizontalInner) && Same(lookAt.HorizontalOuter, defaults.HorizontalOuter) &&
                Same(lookAt.VerticalDown, defaults.VerticalDown) && Same(lookAt.VerticalUp, defaults.VerticalUp);
        }

        static object Child(Dictionary<string, object> value, string key) =>
            value != null && value.TryGetValue(key, out var child) ? child : null;

        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
