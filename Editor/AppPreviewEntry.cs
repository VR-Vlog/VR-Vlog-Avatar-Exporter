using System;

namespace VRVlog.LilToonExporter
{
    // The app preview assembly compiles only when the VR Vlog reader package is
    // installed and sets this from [InitializeOnLoadMethod]. Null means the
    // preview is unavailable, and the export window hides its button.
    internal static class AppPreviewEntry
    {
        internal static Action<byte[], string> Open;
    }
}
