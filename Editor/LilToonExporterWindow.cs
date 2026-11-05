using UnityEditor;

namespace VRVlog.LilToonExporter
{
    // The old export window is gone: every export runs through the expression
    // board, and its shared steps are in ExportEntry. It is still an EditorWindow
    // so a window kept in a saved layout or across a reload finds its type; such
    // a window closes itself.
    public sealed class LilToonExporterWindow : EditorWindow
    {
        private void OnEnable()
        {
            EditorApplication.delayCall += () => { if (this != null) Close(); };
        }
    }
}
