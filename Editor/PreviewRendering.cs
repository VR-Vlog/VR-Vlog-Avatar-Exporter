using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The camera and lights the expressions page, the blink, the excluded objects and the recovery previews share.
    internal static class PreviewRendering
    {
        internal static void Apply(PreviewRenderUtility preview)
        {
            preview.camera.fieldOfView = 30;
            preview.camera.nearClipPlane = .01f;
            preview.camera.farClipPlane = 100;
            preview.camera.clearFlags = CameraClearFlags.SolidColor;
            preview.camera.backgroundColor = new Color(.18f, .18f, .2f);
            preview.lights[0].intensity = 1;
            preview.lights[0].transform.rotation = Quaternion.Euler(30, 150, 0);
            preview.lights[1].intensity = .5f;
            preview.ambientColor = Color.gray;
        }

        // Frames the face of a humanoid, and the whole bounds of anything else.
        internal static void FrameHead(GameObject root, Bounds bounds, out Vector3 center, out float distance)
        {
            var animator = root.GetComponent<Animator>();
            var head = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman
                ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            center = head != null ? head.position + Vector3.up * bounds.size.y * .065f : bounds.center;
            distance = head != null ? Mathf.Max(.35f, bounds.size.y * .65f) : Mathf.Max(.5f, bounds.size.magnitude * 1.2f);
        }
    }
}
