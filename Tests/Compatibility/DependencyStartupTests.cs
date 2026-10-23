using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.Tests
{
    public class DependencyStartupTests
    {
        [UnityTest, Timeout(600000)]
        public IEnumerator MenuResolvesBackendWithoutInitializationRegistration()
        {
            // While Unity is importing or compiling, the menu opens the diagnostics
            // window on purpose. The first test after a fresh import can start in
            // that state. The test runner locks assembly reloads, so a pending
            // recompile never finishes by itself: let it reload, then continue.
            // Nothing before this yield may be relied on after the reload.
            if (EditorApplication.isCompiling) yield return new WaitForDomainReload();
            var entries = TypeCache.GetMethodsWithAttribute<MenuItem>()
                .SelectMany(method => method.GetCustomAttributes(typeof(MenuItem), false).Cast<MenuItem>())
                .Where(item => item.menuItem.StartsWith("VR Vlog/", StringComparison.Ordinal))
                .Select(item => item.menuItem).ToArray();
            // Only the two app transfers (each with its validate method). They come from
            // the transfer module, which only some packages carry.
            Assert.That(entries, Has.None.EqualTo("VR Vlog/VRMを書き出す..."));
            Assert.That(entries.Distinct(), Is.SubsetOf(new[] { "VR Vlog/アプリに転送（かんたん）...", "VR Vlog/アプリに転送（通常）..." }));
            var expected = Environment.GetEnvironmentVariable("VRVLOG_TEST_UNIVRM");
            if (string.IsNullOrEmpty(expected)) Assert.Ignore("Requires an explicit compatibility environment.");
            var deadline = EditorApplication.timeSinceStartup + 300;
            while (DependencyDiagnostics.IsBusy && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(DependencyDiagnostics.IsBusy, Is.False, "Unity was still importing or compiling after 300 seconds.\n" + DependencyDiagnostics.CreateReport());
            // Environment tests may already have resolved the backend. Reset the
            // resolver so it is found again from the loaded assemblies alone, without
            // any InitializeOnLoadMethod registration (the 0.10.3 dead end).
            DependencyDiagnostics.RefreshBackend();
            // Ready with or without the project's own UniVRM, of any version.
            Assert.That(DependencyDiagnostics.BackendReady, Is.True, DependencyDiagnostics.CreateReport());
            Assert.That(DependencyDiagnostics.StartupError(DependencyDiagnostics.Installed()), Is.Null, DependencyDiagnostics.CreateReport());
        }
    }
}
