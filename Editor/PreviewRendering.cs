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

        const float NoticeMaxHeight = 76, NoticeIconSize = 14;

        // What a preview's build left out (NdmfExportPreparation.PreviewNotices).
        // The list can be long, so it scrolls and does not push the preview away.
        // The warning icon is on the heading: a HelpBox centres its icon on all
        // its lines, which puts it below the scrolled view when they are many.
        internal static void DrawExportNotices(System.Collections.Generic.IReadOnlyList<string> notices, ref Vector2 scroll)
        {
            if (notices == null || notices.Count == 0) return;
            var icon = EditorGUIUtility.IconContent("console.warnicon.sml").image;
            var previousIconSize = EditorGUIUtility.GetIconSize();
            EditorGUIUtility.SetIconSize(new Vector2(NoticeIconSize, NoticeIconSize));
            try { EditorGUILayout.LabelField(new GUIContent(ExporterLocalization.T("書き出しの注意"), icon), EditorStyles.miniBoldLabel); }
            finally { EditorGUIUtility.SetIconSize(previousIconSize); }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MaxHeight(NoticeMaxHeight));
            EditorGUILayout.HelpBox(string.Join("\n", System.Linq.Enumerable.Select(notices, ExporterLocalization.T)), MessageType.None);
            EditorGUILayout.EndScrollView();
            GUILayout.Space(4f);
        }

        // Frames the face of a humanoid, and the whole bounds of anything else.
        internal static void FrameHead(GameObject root, Bounds bounds, out Vector3 center, out float distance)
        {
            var animator = root.GetComponent<Animator>();
            var head = animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman
                ? HumanoidBones.Resolve(animator, HumanBodyBones.Head) : null;
            center = head != null ? head.position + Vector3.up * bounds.size.y * .065f : bounds.center;
            distance = head != null ? Mathf.Max(.35f, bounds.size.y * .65f) : Mathf.Max(.5f, bounds.size.magnitude * 1.2f);
        }
    }
}
