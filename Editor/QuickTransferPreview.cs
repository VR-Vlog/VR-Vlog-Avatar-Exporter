using System;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The full-body preview beside the quick transfer's form. The quick transfer
    // exports what the board makes by default, so this hides what the board's
    // objects page hides then: the saved exclusions and the gimmicks the saved
    // choices remove (ExperimentalExpressionCaptureWindow.PreviewHidden). After a
    // change in a scene the avatar is checked again, at most once a second, and
    // the copy is made again only when the avatar itself changed.
    internal sealed class QuickTransferPreview : IDisposable
    {
        // How the preview of an avatar is made. Tests replace it to see a failure.
        internal static Func<GameObject, ExcludedObjectsPreview> Create = avatar => new ExcludedObjectsPreview(avatar);

        const double CheckInterval = 1;
        static int sceneChanges;

        [InitializeOnLoadMethod]
        static void Watch()
        {
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream stream) => sceneChanges++;
            Undo.undoRedoPerformed += () => sceneChanges++;
        }

        ExcludedObjectsPreview preview;
        GameObject avatar;
        ExportRecoverySourceStamp stamp;
        int revision = -1, seenChanges = -1;
        double checkedAt = double.NegativeInfinity;

        // Why the preview could not be made; null when it shows or nothing was tried.
        internal string Error { get; private set; }
        internal ExcludedObjectsPreview Preview => preview;

        // True when Update would change what is drawn: another avatar, a save of
        // its settings, or a change in a scene that is due to be checked.
        internal bool Stale(GameObject target) =>
            target != avatar || revision != ExportSettingsStore.Revision
            || seenChanges != sceneChanges && EditorApplication.timeSinceStartup - checkedAt >= CheckInterval;

        internal void Update(GameObject target)
        {
            if (target == null) { Dispose(); return; }
            if (!Stale(target)) return;
            var changedInScene = seenChanges != sceneChanges;
            seenChanges = sceneChanges;
            checkedAt = EditorApplication.timeSinceStartup;
            revision = ExportSettingsStore.Revision;
            if (target != avatar) { Dispose(); avatar = target; seenChanges = sceneChanges; revision = ExportSettingsStore.Revision; }
            // A failure is tried again only for another avatar, a save or a change in a scene.
            else if (changedInScene && preview != null && stamp != null && !stamp.Matches(target)) Release();
            else if (preview != null && !preview.Usable) Release();
            try
            {
                if (preview == null)
                {
                    Error = null;
                    stamp = ExportRecoverySourceStamp.Capture(target);
                    preview = Create(target);
                }
                var settings = ExportSettingsStore.Load(target);
                var exclusions = settings.Exclusions();
                var removed = ExportGimmickChoices.Removed(ExportGimmickChoices.Analyze(target, exclusions), settings.Gimmicks());
                var (objects, renderers) = ExperimentalExpressionCaptureWindow.PreviewHidden(exclusions, removed);
                preview.Show(objects, renderers);
            }
            catch (Exception exception) when (!(exception is OperationCanceledException))
            {
                Release();
                Error = exception.Message;
                Debug.LogException(exception);
            }
        }

        // The panel's background, the avatar, and on a failure one line that
        // says so with the reason in its tooltip. True when a drag or the wheel
        // moved the camera and the view should repaint.
        internal bool Draw(GameObject target, Rect rect)
        {
            if (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint) Update(target);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin ? new Color32(0x2e, 0x2e, 0x2e, 255) : new Color32(0xd6, 0xd6, 0xd6, 255));
            if (preview != null && preview.Usable)
            {
                var moved = preview.Draw(rect);
                GUI.Label(rect, new GUIContent(string.Empty, ExcludedObjectsPreview.Controls));
                return moved;
            }
            if (Error != null)
            {
                var style = new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, wordWrap = false };
                style.normal.textColor = EditorGUIUtility.isProSkin ? new Color32(0xe0, 0x67, 0x5f, 255) : new Color32(0xb3, 0x26, 0x1e, 255);
                var line = new Rect(rect.x + 8, rect.y + 8, Mathf.Max(0, rect.width - 16), 18);
                GUI.Label(line, new GUIContent(ExporterLocalization.T("プレビューを表示できませんでした"), Error), style);
            }
            return false;
        }

        void Release()
        {
            preview?.Dispose();
            preview = null;
            stamp = null;
        }

        public void Dispose()
        {
            Release();
            avatar = null;
            Error = null;
            revision = seenChanges = -1;
            checkedAt = double.NegativeInfinity;
        }
    }
}
