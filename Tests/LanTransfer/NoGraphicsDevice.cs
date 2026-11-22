using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
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
    }
}
