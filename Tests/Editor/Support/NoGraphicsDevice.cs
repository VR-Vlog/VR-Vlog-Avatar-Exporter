using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace VRVlog.LilToonExporter.Tests
{
    // Without a graphics device (-nographics) the Editor draws nothing: pixels
    // read back from the GPU are undefined, and Unity logs an error for each
    // image format it cannot read back and each window it opens. The export
    // and the window go on after these errors. CI runs with a GPU.
    static class NoGraphicsDevice
    {
        public static bool Missing => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        // Lets the running test pass through those errors. With a GPU the
        // test still fails on any error it logs.
        public static void AllowErrorLogs()
        {
            if (Missing) LogAssert.ignoreFailingMessages = true;
        }

        // Stops before the checks that need a GPU. Call it after the checks
        // that do not.
        public static void IgnoreRest(string reason)
        {
            if (Missing) Assert.Ignore(reason);
        }

        // For pixels drawn and read back where nothing stands in for them
        // without a GPU.
        public static void IgnorePixelChecks() =>
            IgnoreRest("Without a graphics device the pixels read back are undefined, and nothing stands in for them.");

        // For what a window lays out and handles in OnGUI.
        public static void IgnoreWindowChecks() =>
            IgnoreRest("Without a graphics device a window has no view, and its events do not reach OnGUI.");

        // For what depends on the passes of lilToon: the exporter's catalogue
        // and the saved appearance list the passes of the subshader used with a GPU.
        public static void IgnoreLilToonPassChecks() =>
            IgnoreRest("Without a graphics device Unity uses another subshader of lilToon, without the passes the exporter expects.");
    }
}
