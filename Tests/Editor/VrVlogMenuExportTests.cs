using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRVlog.LilToonExporter.Compatibility;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // "VRMを書き出す..." in an avatar's Hierarchy context menu opens the export window with
    // that avatar chosen, or the diagnostics when the export cannot start.
    public sealed class VrVlogMenuExportTests
    {
        const string ContextExport = "GameObject/VR Vlog/VRMを書き出す...";

        static GameObject Source(ExperimentalExpressionCaptureWindow window) =>
            (GameObject)typeof(ExperimentalExpressionCaptureWindow).GetField("source", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);

        static object Session(ExperimentalExpressionCaptureWindow window) =>
            typeof(ExperimentalExpressionCaptureWindow).GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);

        static void CloseWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(window => window is ExperimentalExpressionCaptureWindow || window is DependencyDiagnosticsWindow)) window.Close();
        }

        [Test]
        public void ExportFromAnAvatarsContextMenuOpensTheExportWindowWithThatAvatar()
        {
            CloseWindows();
            var selection = Selection.objects;
            var first = new GameObject("SampleAvatar_a", typeof(Animator));
            var second = new GameObject("SampleAvatar_b", typeof(Animator));
            try
            {
                var ready = DependencyDiagnostics.StartupError(DependencyDiagnostics.Installed()) == null;
                Selection.activeGameObject = first;
                Assert.That(EditorApplication.ExecuteMenuItem(ContextExport), Is.True);
                var windows = Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>();
                if (!ready)
                {
                    Assert.That(windows, Is.Empty, DependencyDiagnostics.CreateReport());
                    Assert.That(Resources.FindObjectsOfTypeAll<DependencyDiagnosticsWindow>().Length, Is.EqualTo(1));
                    return;
                }
                Assert.That(windows.Length, Is.EqualTo(1), DependencyDiagnostics.CreateReport());
                var board = windows[0];
                Assert.That(Source(board), Is.SameAs(first));
                Assert.That(board.TransferMode, Is.False);
                Assert.That(board.titleContent.text, Is.EqualTo(ExporterLocalization.T("VRM書き出し")));
                // Choosing is not preparing: "書き出しを準備" stays the user's step.
                Assert.That(Session(board), Is.Null);

                // A board not yet prepared takes the next avatar without asking.
                Selection.activeGameObject = second;
                Assert.That(EditorApplication.ExecuteMenuItem(ContextExport), Is.True);
                Assert.That(Resources.FindObjectsOfTypeAll<ExperimentalExpressionCaptureWindow>(), Is.EqualTo(new[] { board }));
                Assert.That(Source(board), Is.SameAs(second));
                Assert.That(Session(board), Is.Null);
            }
            finally
            {
                Selection.objects = selection;
                CloseWindows();
                Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            }
        }
    }
}
