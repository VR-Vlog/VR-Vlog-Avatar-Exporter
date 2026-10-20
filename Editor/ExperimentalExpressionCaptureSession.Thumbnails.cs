using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Faces for the board thumbnails. They are drawn on the preview copy one
    // after another, so these skip the source checks the export and the
    // assignment errors already run; a face that fails just stays undrawn.
    internal sealed partial class ExperimentalExpressionCaptureSession
    {
        // The face the preview copy shows now, to put back after drawing.
        internal Pose CurrentFace() => Snapshot("プレビュー");

        internal void ShowFace(Pose pose)
        {
            ValidatePose(pose);
            ApplyUnchecked(pose);
        }

        // The face of a file at its last frame, as the board previews it.
        internal Pose ThumbnailFace(AnimationClip clip) => BuildClipPoseUnchecked(clip, clip.length, "プレビュー");

        // The neutral face with one BlendShape at 100, as a shape assignment exports it.
        internal Pose ThumbnailFace(string path, string shape)
        {
            var channel = AuthoringChannel(path ?? "");
            var pose = ClonePose(Baseline);
            Set(pose, channel.Path, shape, 100f);
            return pose;
        }
    }
}
