using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // The expressions page, the blink, the excluded objects and the recovery
    // previews light the avatar the same way through one place.
    public sealed class PreviewRenderingTests
    {
        [Test]
        public void ApplySetsTheSharedCameraAndLights()
        {
            var preview = new PreviewRenderUtility();
            try
            {
                PreviewRendering.Apply(preview);
                Assert.That(preview.camera.fieldOfView, Is.EqualTo(30));
                Assert.That(preview.camera.nearClipPlane, Is.EqualTo(.01f));
                Assert.That(preview.camera.farClipPlane, Is.EqualTo(100));
                Assert.That(preview.camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
                Assert.That(preview.camera.backgroundColor, Is.EqualTo(new Color(.18f, .18f, .2f)));
                Assert.That(preview.lights[0].intensity, Is.EqualTo(1));
                Assert.That(Quaternion.Angle(preview.lights[0].transform.rotation, Quaternion.Euler(30, 150, 0)), Is.LessThan(.01f));
                Assert.That(preview.lights[1].intensity, Is.EqualTo(.5f));
                Assert.That(preview.ambientColor, Is.EqualTo(Color.gray));
            }
            finally { preview.Cleanup(); }
        }
    }
}
