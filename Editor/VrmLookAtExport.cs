using System;
using System.Collections.Generic;
using System.Linq;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // VRM 1.0 bone look-at for an avatar without its own VRM look-at. The
    // origin is the midpoint of the eyes. The range follows the VRChat Eye
    // Look poses when they can be measured; otherwise it uses UniVRM's
    // defaults, which are also what UniVRM applies when lookAt is absent.
    // 視線の動く範囲 (GazeRangeOptions) replaces the limits the user changed.
    internal sealed class VrmLookAtExport
    {
        // VRM10ObjectLookAt's defaults in the pinned UniVRM.
        internal const float DefaultInputDegrees = 90f;
        internal const float DefaultOutputDegrees = 10f;
        // A pose that turns an eye by less than this was not authored.
        internal const float MinimumDegrees = 1f;
        internal const float MaximumDegrees = 90f;
        internal const string UnmeasuredWarning = "Eye Lookの設定から目の可動範囲を測れないため視線は既定の範囲で書き出しました（Avatar DescriptorのEye Lookを確認）";

        // One VRM range map: a gaze of Input degrees turns the eye by Output.
        internal struct Range
        {
            internal float Input, Output;
            internal static Range Default => new Range { Input = DefaultInputDegrees, Output = DefaultOutputDegrees };
            // One-to-one up to the limit, as VRChat's Eye Look follows a gaze
            // directly until its pose.
            internal static Range Linear(float degrees) => new Range { Input = degrees, Output = degrees };
        }

        // The four range maps of the VRM look-at, in degrees.
        internal struct Ranges
        {
            internal Range HorizontalInner, HorizontalOuter, VerticalDown, VerticalUp;
            internal static Ranges Default => new Ranges
            {
                HorizontalInner = Range.Default, HorizontalOuter = Range.Default, VerticalDown = Range.Default, VerticalUp = Range.Default,
            };

            // 視線の動く範囲 replaces the limits it sets; the axes left at 0 keep these.
            internal Ranges With(GazeRangeOptions options)
            {
                options = options?.Validated() ?? new GazeRangeOptions();
                var result = this;
                if (options.Up > 0) result.VerticalUp = Range.Linear(options.Up);
                if (options.Down > 0) result.VerticalDown = Range.Linear(options.Down);
                if (options.Side > 0) result.HorizontalInner = result.HorizontalOuter = Range.Linear(options.Side);
                return result;
            }

            // How far the eyes turn at most, as the sliders show it. The sides
            // show the smaller of the two limits, the one both eyes reach.
            internal GazeRangeOptions Shown() => new GazeRangeOptions
            {
                Up = VerticalUp.Output, Down = VerticalDown.Output, Side = Mathf.Min(HorizontalInner.Output, HorizontalOuter.Output),
            };
        }

        // Whether 視線の動く範囲 can change the avatar's look-at.
        internal enum GazeSupport { Ranges, NoEyeBones, AuthoredVrm }

        internal struct GazeInspection
        {
            internal GazeSupport Support;
            // What the export writes when nothing is changed.
            internal Ranges Ranges;
            // The ranges came from VRChat's Eye Look, not from UniVRM's defaults.
            internal bool FromEyeLook;
        }

        // Head-local position in Unity coordinates, as UniVRM's OffsetFromHead.
        internal Vector3 OffsetFromHead;
        internal bool Measured;
        internal Ranges Limits = Ranges.Default;

        // Returns null when the copy's own VRM settings already describe the
        // look-at, or when bone look-at cannot work (no humanoid head or eyes).
        // gaze is 視線の動く範囲 of the board; null or its default keeps the avatar's ranges.
        internal static VrmLookAtExport Capture(GameObject source, GameObject copy, ICollection<string> warnings, GazeRangeOptions gaze = null)
        {
            if (source == null || copy == null) return null;
            // UniVRM serializes VRM10Object.LookAt itself. Keep an edited one;
            // untouched defaults (for example a VRM10Object kept only for its
            // expressions) carry no look-at information of the avatar.
            var authoredLookAt = copy.TryGetComponent<Vrm10Instance>(out var instance) ? instance.Vrm?.LookAt : null;
            if (authoredLookAt != null && !IsUniVrmDefault(authoredLookAt)) return null;
            var animator = copy.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman) return null;
            var head = HumanoidBones.Resolve(animator, HumanBodyBones.Head);
            var left = HumanoidBones.Resolve(animator, HumanBodyBones.LeftEye);
            var right = HumanoidBones.Resolve(animator, HumanBodyBones.RightEye);
            if (head == null || left == null || right == null) return null;
            var offset = head.InverseTransformPoint((left.position + right.position) * .5f);
            if (!Finite(offset.x) || !Finite(offset.y) || !Finite(offset.z)) return null;
            var result = new VrmLookAtExport { OffsetFromHead = offset };
            result.Measured = TryMeasure(source, out var authored, out var measured);
            if (result.Measured) result.Limits = measured;
            else if (authored) warnings?.Add(ExporterLocalization.T(UnmeasuredWarning));
            result.Limits = result.Limits.With(gaze);
            return result;
        }

        // What Capture writes for this avatar when 視線の動く範囲 is left as it
        // is, or why it writes nothing that setting could change. Reads the
        // source as Capture reads the export copy.
        internal static GazeInspection Inspect(GameObject source)
        {
            var inspection = new GazeInspection { Support = GazeSupport.NoEyeBones, Ranges = Ranges.Default };
            if (source == null) return inspection;
            var authoredLookAt = source.TryGetComponent<Vrm10Instance>(out var instance) ? instance.Vrm?.LookAt : null;
            if (authoredLookAt != null && !IsUniVrmDefault(authoredLookAt)) { inspection.Support = GazeSupport.AuthoredVrm; return inspection; }
            var animator = source.GetComponent<Animator>();
            if (animator == null || animator.avatar == null || !animator.isHuman ||
                HumanoidBones.Resolve(animator, HumanBodyBones.Head) == null ||
                HumanoidBones.Resolve(animator, HumanBodyBones.LeftEye) == null ||
                HumanoidBones.Resolve(animator, HumanBodyBones.RightEye) == null) return inspection;
            inspection.Support = GazeSupport.Ranges;
            if (TryMeasure(source, out _, out var measured)) { inspection.Ranges = measured; inspection.FromEyeLook = true; }
            return inspection;
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
            Dictionary<string, object> Map(Range range) => new Dictionary<string, object>
            {
                ["inputMaxValue"] = (double)range.Input,
                ["outputScale"] = (double)range.Output,
            };
            return new Dictionary<string, object>
            {
                // glTF mirrors Unity's X axis; UniVRM writes OffsetFromHead.ReverseX().
                ["offsetFromHeadBone"] = new List<object> { (double)-OffsetFromHead.x, (double)OffsetFromHead.y, (double)OffsetFromHead.z },
                ["type"] = "bone",
                ["rangeMapHorizontalInner"] = Map(Limits.HorizontalInner),
                ["rangeMapHorizontalOuter"] = Map(Limits.HorizontalOuter),
                ["rangeMapVerticalDown"] = Map(Limits.VerticalDown),
                ["rangeMapVerticalUp"] = Map(Limits.VerticalUp),
            };
        }

        // authored: Eye Look is enabled on the descriptor, so a failed
        // measurement loses information the user set up and is reported.
        internal static bool TryMeasure(GameObject source, out bool authored, out Ranges ranges)
        {
            authored = false; ranges = Ranges.Default;
            var descriptor = VrChatExpressionMenu.Descriptor(source);
            if (!(VrChatExpressionMenu.Member(descriptor, "enableEyeLook") is bool enabled) || !enabled) return false;
            authored = true;
            var settings = VrChatExpressionMenu.Member(descriptor, "customEyeLookSettings");
            var leftEye = VrChatExpressionMenu.Member(settings, "leftEye") as Transform;
            var rightEye = VrChatExpressionMenu.Member(settings, "rightEye") as Transform;
            var animator = source.GetComponent<Animator>();
            // VRM rotates the humanoid eyes. Poses of other transforms do not
            // describe how those eyes turn.
            if (leftEye == null || rightEye == null || animator == null || !animator.isHuman ||
                HumanoidBones.Resolve(animator, HumanBodyBones.LeftEye) != leftEye ||
                HumanoidBones.Resolve(animator, HumanBodyBones.RightEye) != rightEye) return false;
            var poses = new[] { "eyesLookingStraight", "eyesLookingUp", "eyesLookingDown", "eyesLookingLeft", "eyesLookingRight" }
                .Select(name => VrChatExpressionMenu.Member(settings, name)).ToArray();
            if (!TryEye(source.transform, leftEye, poses, true, out var leftRange) ||
                !TryEye(source.transform, rightEye, poses, false, out var rightRange)) return false;
            // Stay within the authored limit of both eyes.
            var inner = Mathf.Min(leftRange.Inner, rightRange.Inner);
            var outer = Mathf.Min(leftRange.Outer, rightRange.Outer);
            var down = Mathf.Min(leftRange.Down, rightRange.Down);
            var up = Mathf.Min(leftRange.Up, rightRange.Up);
            if (!new[] { inner, outer, down, up }.All(value => Finite(value) && value >= MinimumDegrees && value <= MaximumDegrees)) return false;
            ranges = new Ranges
            {
                HorizontalInner = Range.Linear(inner), HorizontalOuter = Range.Linear(outer),
                VerticalDown = Range.Linear(down), VerticalUp = Range.Linear(up),
            };
            return true;
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
    }
}
