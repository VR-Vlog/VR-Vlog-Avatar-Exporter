using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRVlog.Expressions;

namespace VRVlog.LilToonExporter
{
    // The time at which a moving face is read as one still face: the board's
    // presets, previews and checks, and the VRM expression of a moving face
    // (VrChatExpressionBaker). Curves whose value never changes read the same
    // at every time; a face without a changing curve keeps its last frame.
    // A moving face (a loop that starts and ends at rest, with a blink or a
    // sparkle on top) is read where it holds its face:
    // - The holding curves are the changing curves that stand still for at
    //   least half of the duration. A curve that keeps moving, such as a
    //   sparkle, is not one of them and takes its value at the chosen time.
    // - A hold is a stretch in which every holding curve stands still with
    //   the same values. Each hold scores its length times how far its
    //   values are from the rest weights; a short blink or a long wait at
    //   rest does not outscore a face held for long. The highest score wins;
    //   then the longer hold, then the earlier one.
    // - The face is the middle of that hold. Without a hold away from rest,
    //   the last frame.
    internal static partial class ClipFaceTime
    {
        // A clip with every rest at 0, for the board without an avatar.
        internal static float Of(AnimationClip clip) => Of(clip, _ => 0);

        // rest gives the rest weight of a BlendShape binding, or null for a
        // binding the face does not use.
        internal static float Of(AnimationClip clip, Func<EditorCurveBinding, double?> rest)
        {
            if (clip == null) return 0;
            var length = clip.length;
            if (!(length > 0) || float.IsInfinity(length)) return length;
            var curves = new List<(ExpressionAnimationData.Curve, double)>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(PoseSampling.IsBlendShape))
            {
                var weight = rest(binding);
                var curve = weight == null ? null : AnimationUtility.GetEditorCurve(clip, binding);
                if (curve != null && curve.length > 0) curves.Add((VrChatGestureExpressions.ConvertCurve(curve), weight.Value));
            }
            var time = Of(curves, length);
            return time == null ? length : (float)time.Value;
        }
    }
}
