using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
            // One export entry only. The two app transfers (each with its validate
            // method) come from the transfer module, which only some packages carry.
            Assert.That(entries.Count(entry => entry == "VR Vlog/VRMを書き出す..."), Is.EqualTo(1));
            Assert.That(entries.Distinct(), Is.SubsetOf(new[] { "VR Vlog/VRMを書き出す...", "VR Vlog/アプリに転送（かんたん）...", "VR Vlog/アプリに転送（通常）..." }));
            var expected = Environment.GetEnvironmentVariable("VRVLOG_TEST_UNIVRM");
            if (string.IsNullOrEmpty(expected)) Assert.Ignore("Requires an explicit compatibility environment.");
            var supported = DependencyPolicy.SupportsUniVrm(expected);
            var deadline = EditorApplication.timeSinceStartup + 300;
            while (DependencyDiagnostics.IsBusy && EditorApplication.timeSinceStartup < deadline) yield return null;
            Assert.That(DependencyDiagnostics.IsBusy, Is.False, "Unity was still importing or compiling after 300 seconds.\n" + DependencyDiagnostics.CreateReport());
            // Reproduces the 0.10.3 dead end without relying on the user's reload
            // timing. This is a no-op once the eager registration field is removed.
            var legacyField = typeof(DependencyDiagnostics).GetField("OpenExporter", BindingFlags.Public | BindingFlags.Static);
            var original = legacyField?.GetValue(null);
            try
            {
                if (legacyField == null)
                {
                    // Environment tests may already have resolved the backend.
                    // Require and reset the new resolver before exercising the menu.
                    var refresh = typeof(DependencyDiagnostics).GetMethod("RefreshBackend", BindingFlags.Public | BindingFlags.Static);
                    Assert.That(refresh, Is.Not.Null);
                    refresh.Invoke(null, null);
                }
                legacyField?.SetValue(null, null);
                Assert.That(EditorApplication.ExecuteMenuItem("VR Vlog/VRMを書き出す..."), Is.True);
                var exporter = Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .SingleOrDefault(w => w.GetType().FullName == "VRVlog.LilToonExporter.ExperimentalExpressionCaptureWindow");
                var diagnostics = Resources.FindObjectsOfTypeAll<DependencyDiagnosticsWindow>();
                Assert.That(exporter != null, Is.EqualTo(supported), DependencyDiagnostics.CreateReport());
                if (exporter != null) Assert.That(exporter.titleContent.text, Is.EqualTo("VRM書き出し"));
                if (exporter != null) Assert.That(exporter.titleContent.image, Is.Null);
                Assert.That(diagnostics.Length, Is.EqualTo(supported ? 0 : 1));
            }
            finally
            {
                legacyField?.SetValue(null, original);
                foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()
                    .Where(w => w.GetType().Namespace == "VRVlog.LilToonExporter" || w is DependencyDiagnosticsWindow)) window.Close();
            }
        }
    }
}
