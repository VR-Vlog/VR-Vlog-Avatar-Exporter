using System;
using NUnit.Framework;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.Tests
{
    public class DependencyEnvironmentTests
    {
        [Test]
        public void ActualInstalledPackagesMatchRequestedTestEnvironment()
        {
            var expected = Environment.GetEnvironmentVariable("VRVLOG_TEST_UNIVRM");
            if (string.IsNullOrEmpty(expected)) Assert.Ignore("Run through Tools/run-unity-compatibility.py with an explicit environment.");
            Assert.That(UnityEngine.Application.unityVersion, Is.EqualTo(Environment.GetEnvironmentVariable("VRVLOG_TEST_UNITY")));
            var packages = DependencyDiagnostics.Installed();
            var vrm = DependencyDiagnostics.Version(packages, "com.vrmc.vrm");
            var gltf = DependencyDiagnostics.Version(packages, "com.vrmc.gltf");
            // The project's own UniVRM (none, or any version) only describes the
            // environment: the exporter compiles against its bundled copy.
            Assert.That(vrm, Is.EqualTo(expected == "missing" ? null : expected));
            Assert.That(gltf, Is.EqualTo(expected == "missing" ? null : expected));
            Assert.That(DependencyDiagnostics.BackendReady, Is.True, DependencyDiagnostics.CreateReport());
            Assert.That(DependencyPolicy.LilToonError(DependencyDiagnostics.Version(packages, "jp.lilxyzw.liltoon")), Is.Null);
            Assert.That(DependencyDiagnostics.Error(packages), Is.Null);
        }
    }
}
