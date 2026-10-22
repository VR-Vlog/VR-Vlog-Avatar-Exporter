using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.LanTransfer.Tests
{
    public sealed class CloudTransferWindowTests
    {
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void TransferAvailabilityRemainsEnabledWithoutAddingAnotherToolbarMenu()
        {
            var menu = typeof(CloudVrmTransferWindow).GetMethod("OpenDevelopmentTransfer", BindingFlags.NonPublic | BindingFlags.Static);
#if UNITY_EDITOR
            Assert.That(menu, Is.Not.Null);
            Assert.That(menu.GetCustomAttributes(typeof(MenuItem), false).Length, Is.EqualTo(0));
            var validation = typeof(CloudVrmTransferWindow).GetMethod("CanOpenDevelopmentTransfer", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(validation, Is.Not.Null);
            Assert.That(validation.GetCustomAttributes(typeof(MenuItem), false).Length, Is.EqualTo(0));
            Assert.That(validation.Invoke(null, null), Is.EqualTo(CloudTransferAvailability.Enabled));
            Assert.That(CloudVrmTransferWindow.IsAvailable, Is.EqualTo(CloudTransferAvailability.Enabled));
#else
            Assert.That(CloudTransferAvailability.Enabled, Is.False);
            Assert.That(menu, Is.Null);
#endif
        }

        [Test]
        public void OptionalExporterEntryMatchesInstalledReleaseChannel()
        {
            var exporter = Type.GetType("VRVlog.LilToonExporter.LilToonExporterWindow, VRVlog.LilToonExporter.Editor", true);
            var method = exporter.GetMethod("SavedVrmTransferMethod", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(method, Is.Not.Null);
            var entry = (MethodInfo)method.Invoke(null, null);
            Assert.That(entry != null, Is.EqualTo(CloudTransferAvailability.Enabled));
            if (entry != null)
            {
                Assert.That(entry.DeclaringType, Is.EqualTo(typeof(CloudVrmTransferWindow)));
                Assert.That(entry.Name, Is.EqualTo("OpenSavedVrmForDevelopment"));
            }
            // The expression board's "次へ" opens the detailed transfer the same way.
            var detailed = (MethodInfo)exporter.GetMethod("DetailedTransferMethod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.That(detailed != null, Is.EqualTo(CloudTransferAvailability.Enabled));
            if (detailed != null)
            {
                Assert.That(detailed.DeclaringType, Is.EqualTo(typeof(AppTransferWindow)));
                Assert.That(detailed.Name, Is.EqualTo("OpenDetailed"));
            }
            // The board asks its third page whether it transfers the same way.
            var transferring = (MethodInfo)exporter.GetMethod("EmbeddedTransferringMethod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.That(transferring != null, Is.EqualTo(CloudTransferAvailability.Enabled));
            if (transferring != null)
            {
                Assert.That(transferring.DeclaringType, Is.EqualTo(typeof(AppTransferPanel)));
                Assert.That(transferring.Name, Is.EqualTo("EmbeddedTransferring"));
            }
            // Its third page itself, with the board's "戻る" and which button is blue. It is also
            // what tells the board that page is usable in the normal mode.
            var embedded = (MethodInfo)exporter.GetMethod("EmbeddedTransferMethod", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.That(embedded != null, Is.EqualTo(CloudTransferAvailability.Enabled));
            if (embedded != null)
            {
                Assert.That(embedded.DeclaringType, Is.EqualTo(typeof(AppTransferPanel)));
                Assert.That(embedded.Name, Is.EqualTo("CreateEmbedded"));
                Assert.That(embedded.GetParameters().Select(parameter => parameter.ParameterType),
                    Is.EqualTo(new[] { typeof(UnityEngine.GameObject), typeof(Action), typeof(Func<bool>) }));
            }
        }

        [Test]
        public void AppTransferBindsEveryExportMemberOfTheLoadedExporterByName()
        {
            var exporter = Type.GetType("VRVlog.LilToonExporter.AppTransferExport, VRVlog.LilToonExporter.Editor", false);
            var bound = typeof(AppTransferPanel).GetNestedType("ExportEntry", BindingFlags.NonPublic)
                .GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
            Assert.That(bound != null, Is.EqualTo(exporter != null));
            if (bound == null) return;
            var members = bound.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(members.Length, Is.EqualTo(19));
            foreach (var member in members) Assert.That(member.GetValue(bound), Is.Not.Null, member.Name);
        }

#if UNITY_EDITOR
        [Test]
        public void ExpressionCaptureSavedResultOpensAnImmutableCopyWithoutUploading()
        {
            var type = Type.GetType("VRVlog.LilToonExporter.ExperimentalExpressionCaptureWindow, VRVlog.LilToonExporter.Editor", true);
            var savedType = Type.GetType("VRVlog.LilToonExporter.SavedExpressionVrm, VRVlog.LilToonExporter.Editor", true);
            var capture = ScriptableObject.CreateInstance(type);
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-expression-qr-" + Guid.NewGuid().ToString("N") + ".vrm");
            var bytes = new byte[] { 9, 8, 7, 6 };
            string copyPath = null;
            CloudVrmTransferWindow transfer = null;
            try
            {
                File.WriteAllBytes(path, bytes);
                var saved = Activator.CreateInstance(savedType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { path, bytes, 9, 1 }, null);
                type.GetField("saved", Instance).SetValue(capture, saved);
                var open = type.GetMethod("OpenSavedTransfer", Instance);
                if (!CloudTransferAvailability.Enabled)
                {
                    Assert.Throws<TargetInvocationException>(() => open.Invoke(capture, null));
                    return;
                }
                open.Invoke(capture, null);
                transfer = Resources.FindObjectsOfTypeAll<CloudVrmTransferWindow>().Single();
                Assert.That(typeof(CloudVrmTransferWindow).GetField("uploading", Instance).GetValue(transfer), Is.Null);
                Assert.That(typeof(CloudVrmTransferWindow).GetField("session", Instance).GetValue(transfer), Is.Null, "Opening the transfer must not create remote storage.");
                var source = (CloudVrmTransferSource)typeof(CloudVrmTransferWindow).GetField("source", Instance).GetValue(transfer);
                copyPath = (string)typeof(CloudVrmTransferSource).GetField("path", Instance).GetValue(source);
                Assert.That(copyPath, Is.Not.EqualTo(path));
                Assert.That(File.ReadAllBytes(copyPath), Is.EqualTo(bytes));
                File.WriteAllBytes(path, new byte[] { 1 });
                Assert.That(File.ReadAllBytes(copyPath), Is.EqualTo(bytes));
                Assert.Throws<TargetInvocationException>(() => open.Invoke(capture, null));
                Assert.That(typeof(CloudVrmTransferWindow).GetField("source", Instance).GetValue(transfer), Is.SameAs(source));
                transfer.Close(); transfer = null;
                Assert.That(File.Exists(copyPath), Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 1 }));
            }
            finally
            {
                transfer?.Close(); Object.DestroyImmediate(capture);
                if (copyPath != null && File.Exists(copyPath)) File.Delete(copyPath);
                if (File.Exists(path)) File.Delete(path);
            }
        }
#endif

        [Test]
        public void PublicCloudEntryCannotAdoptOrDeleteAnOrdinarySavedVrm()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-saved-export-" + Guid.NewGuid().ToString("N") + ".vrm");
            var bytes = new byte[] { 51, 52, 53 };
            var windows = Resources.FindObjectsOfTypeAll<CloudVrmTransferWindow>();
            try
            {
                File.WriteAllBytes(path, bytes);
                if (CloudTransferAvailability.Enabled)
                    Assert.Throws<InvalidOperationException>(() => CloudVrmTransferWindow.Show(path, "saved.vrm"));
                else
                {
                Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.CreateSnapshotPath());
                Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.Show(path, "saved.vrm"));
                Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path));
                }
                Assert.That(Resources.FindObjectsOfTypeAll<CloudVrmTransferWindow>(), Is.EquivalentTo(windows));
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
            }
            finally { File.Delete(path); }
        }

#if !(UNITY_EDITOR)
        [Test]
        public void RejectedCloudEntryDeletesOnlyItsPreviouslyIssuedSnapshot()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-owned-cloud-export-" + Guid.NewGuid().ToString("N") + ".vrm");
            var owned = (HashSet<string>)typeof(CloudVrmTransferWindow).GetField("OwnedSnapshots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                // Reproduce a transfer-only path issued before the current
                // disabled release; no public API can issue one now.
                owned.Add(path);
                Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.Show(path, "owned.vrm"));
                Assert.That(File.Exists(path), Is.False);
                Assert.That(owned.Contains(path), Is.False);
            }
            finally { owned.Remove(path); if (File.Exists(path)) File.Delete(path); }
        }

        [TestCase("OnEnable")]
        [TestCase("Begin")]
        [TestCase("Poll")]
        public void DisabledRestoredWindowReleasesItsOldSourceAndSessionBeforeStartingWork(string callback)
        {
            var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-restored-cloud-export-" + Guid.NewGuid().ToString("N") + ".vrm");
            CloudVrmTransferSource source = null;
            CloudVrmTransferSession session = null;
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                source = new CloudVrmTransferSource(path, "old.vrm");
                session = new CloudVrmTransferSession(source);
                typeof(CloudVrmTransferWindow).GetField("source", Instance).SetValue(window, source);
                typeof(CloudVrmTransferWindow).GetField("session", Instance).SetValue(window, session);
                typeof(CloudVrmTransferWindow).GetField("uploading", Instance).SetValue(window, new TaskCompletionSource<bool>().Task);
                typeof(CloudVrmTransferWindow).GetField("polling", Instance).SetValue(window, new TaskCompletionSource<bool>().Task);
                typeof(CloudVrmTransferWindow).GetField("qrTexture", Instance).SetValue(window, new Texture2D(1, 1));
                typeof(CloudVrmTransferWindow).GetMethod(callback, Instance).Invoke(window, null);
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Canceled));
                Assert.That(File.Exists(path), Is.False);
                Assert.Throws<ObjectDisposedException>(() => source.OpenRead());
                foreach (var field in new[] { "source", "session", "uploading", "polling", "qrTexture" })
                    Assert.That(typeof(CloudVrmTransferWindow).GetField(field, Instance).GetValue(window), Is.Null);
            }
            finally { Object.DestroyImmediate(window); session?.Dispose(); source?.Dispose(); if (File.Exists(path)) File.Delete(path); }
        }
#endif

#if UNITY_EDITOR
        [Test]
        public void PrivacyChoiceDefaultsOffAndSurvivesClosingAndReopeningTheWindow()
        {
            WithPrivacyPreference(() =>
            {
                EditorPrefs.DeleteKey(CloudVrmTransferWindow.PrivacyConsentPreference);
                Assert.That(CloudVrmTransferWindow.PrivacyConsentAccepted, Is.False);
                var first = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
                CloudVrmTransferWindow.PrivacyConsentAccepted = true;
                Object.DestroyImmediate(first);
                var next = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
                try
                {
                    Assert.That(EditorPrefs.GetBool(CloudVrmTransferWindow.PrivacyConsentPreference), Is.True);
                    Assert.That(CloudVrmTransferWindow.PrivacyConsentAccepted, Is.True);
                    Assert.That(next.CanBeginTransfer, Is.False, "Remembering consent must not invent a saved VRM.");
                    Assert.That(Get(next, "session"), Is.Null);
                    Assert.That(Get(next, "uploading"), Is.Null);
                    CloudVrmTransferWindow.PrivacyConsentAccepted = false;
                }
                finally { Object.DestroyImmediate(next); }
                Assert.That(EditorPrefs.GetBool(CloudVrmTransferWindow.PrivacyConsentPreference), Is.False);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingPrivacyConsentBlocksInitialUploadAndNewQrCreation(bool retry)
        {
            WithPrivacyPreference(() =>
            {
                EditorPrefs.DeleteKey(CloudVrmTransferWindow.PrivacyConsentPreference);
                var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
                var path = Path.Combine(Path.GetTempPath(), "vrvlog-consent-" + Guid.NewGuid().ToString("N") + ".vrm");
                File.WriteAllBytes(path, new byte[] { 7, 8, 9 });
                var source = new CloudVrmTransferSource(path, "fixture.vrm");
                CloudVrmTransferSession previous = null;
                try
                {
                    Set(window, "source", source);
                    if (retry)
                    {
                        previous = new CloudVrmTransferSession(source);
                        previous.Dispose();
                        Assert.That(previous.Terminal, Is.True);
                        Set(window, "session", previous);
                    }
                    Assert.That(window.CanBeginTransfer, Is.False);
                    typeof(CloudVrmTransferWindow).GetMethod("Begin", Instance).Invoke(window, null);
                    Assert.That(Get(window, "session"), Is.SameAs(previous));
                    Assert.That(Get(window, "uploading"), Is.Null, "No worker may begin encryption or remote creation before consent.");
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 7, 8, 9 }));
                }
                finally { Object.DestroyImmediate(window); previous?.Dispose(); source.Dispose(); if (File.Exists(path)) File.Delete(path); }
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RememberedPrivacyChoiceNeverUploadsWhenOpeningASavedVrm(bool accepted)
        {
            WithPrivacyPreference(() =>
            {
                CloudVrmTransferWindow.PrivacyConsentAccepted = accepted;
                var path = Path.Combine(Path.GetTempPath(), "vrvlog-consent-open-" + Guid.NewGuid().ToString("N") + ".vrm");
                File.WriteAllBytes(path, new byte[] { 3, 4, 5 });
                CloudVrmTransferWindow window = null;
                try
                {
                    if (!CloudTransferAvailability.Enabled)
                    {
                        Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path));
                        return;
                    }
                    CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path);
                    window = EditorWindow.GetWindow<CloudVrmTransferWindow>();
                    Assert.That(CloudVrmTransferWindow.PrivacyConsentAccepted, Is.EqualTo(accepted));
                    Assert.That(window.CanBeginTransfer, Is.EqualTo(accepted));
                    Assert.That(Get(window, "session"), Is.Null);
                    Assert.That(Get(window, "uploading"), Is.Null);
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 3, 4, 5 }));
                }
                finally { if (window != null) Object.DestroyImmediate(window); File.Delete(path); }
            });
        }

        [Test]
        public void WithdrawingConsentAndBusyTransfersPreventNewQrCreation()
        {
            WithPrivacyPreference(() =>
            {
                var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
                var path = Path.Combine(Path.GetTempPath(), "vrvlog-consent-busy-" + Guid.NewGuid().ToString("N") + ".vrm");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                var source = new CloudVrmTransferSource(path, "fixture.vrm");
                var session = new CloudVrmTransferSession(source);
                try
                {
                    Set(window, "source", source);
                    CloudVrmTransferWindow.PrivacyConsentAccepted = true;
                    Assert.That(window.CanBeginTransfer, Is.EqualTo(CloudTransferAvailability.Enabled));
                    Set(window, "session", session);
                    Assert.That(window.CanBeginTransfer, Is.False);
                    session.Dispose();
                    Set(window, "uploading", new TaskCompletionSource<bool>().Task);
                    Assert.That(window.CanBeginTransfer, Is.False);
                    Set(window, "uploading", null);
                    Assert.That(window.CanBeginTransfer, Is.EqualTo(CloudTransferAvailability.Enabled));
                    CloudVrmTransferWindow.PrivacyConsentAccepted = false;
                    Assert.That(window.CanBeginTransfer, Is.False);
                    typeof(CloudVrmTransferWindow).GetMethod("Begin", Instance).Invoke(window, null);
                    Assert.That(Get(window, "session"), Is.SameAs(session));
                    Assert.That(Get(window, "uploading"), Is.Null);
                }
                finally { Object.DestroyImmediate(window); session.Dispose(); source.Dispose(); if (File.Exists(path)) File.Delete(path); }
            });
        }

        [Test]
        public void AppTransferNeedsTheSavedPrivacyConsentBeforeAnExportOrANewUpload()
        {
            WithPrivacyPreference(() =>
            {
                var type = typeof(AppTransferPanel);
                var avatar = new GameObject("vrvlog-app-transfer-privacy");
                var window = new AppTransferPanel();
                try
                {
                    type.GetField("avatar", Instance).SetValue(window, avatar);
                    type.GetField("avatarName", Instance).SetValue(window, "SampleAvatar");
                    type.GetField("consented", Instance).SetValue(window, true);
                    // Every other input is ready, whatever exporter the test project has.
                    var entryType = type.GetNestedType("ExportEntry", BindingFlags.NonPublic);
                    var entry = Activator.CreateInstance(entryType, true);
                    entryType.GetField("HasAuthor", Instance).SetValue(entry, (Func<bool>)(() => true));
                    type.GetField("exporter", Instance).SetValue(window, entry);
                    type.GetField("exporterResolved", Instance).SetValue(window, true);
                    var reason = type.GetMethod("Reason", Instance);

                    EditorPrefs.DeleteKey(CloudVrmTransferWindow.PrivacyConsentPreference);
                    Assert.That(reason.Invoke(window, null), Is.EqualTo(AppTransferText.NeedsPrivacyConsent));
                    type.GetMethod("Begin", Instance).Invoke(window, null);
                    Assert.That(type.GetField("flow", Instance).GetValue(window), Is.Null, "No export may start before the privacy consent.");

                    // A consent withdrawn in "スマホに送る" stops a new QR and returns to the form.
                    var flow = new AppTransferFlow();
                    type.GetField("flow", Instance).SetValue(window, flow);
                    flow.BeginExport();
                    flow.ExportFailed("fixture");
                    type.GetMethod("Retry", Instance).Invoke(window, null);
                    Assert.That(flow.View, Is.EqualTo(AppTransferView.Form));

                    CloudVrmTransferWindow.PrivacyConsentAccepted = true;
                    Assert.That(reason.Invoke(window, null), Is.Null);
                }
                finally { Object.DestroyImmediate(avatar); }
            });
        }

        [Test]
        public void AppTransferSavesToPcWithoutTheTransferConsentButNotWhileExporting()
        {
            WithPrivacyPreference(() =>
            {
                var type = typeof(AppTransferPanel);
                var avatar = new GameObject("vrvlog-app-transfer-save");
                var other = new GameObject("vrvlog-app-transfer-save-other");
                var window = new AppTransferPanel();
                try
                {
                    type.GetField("avatar", Instance).SetValue(window, avatar);
                    type.GetField("avatarName", Instance).SetValue(window, "  ");
                    var entryType = type.GetNestedType("ExportEntry", BindingFlags.NonPublic);
                    var entry = Activator.CreateInstance(entryType, true);
                    entryType.GetField("HasAuthor", Instance).SetValue(entry, (Func<bool>)(() => true));
                    entryType.GetField("TermsKey", Instance).SetValue(entry, (Func<string>)(() => "fixture terms"));
                    string savedName = null, failure = null;
                    var later = false;
                    Action<string, int> pending = null;
                    entryType.GetField("SaveCopy", Instance).SetValue(entry, (Func<GameObject, string, bool, Action<string, int>, string>)((target, name, detailed, saved) =>
                    {
                        savedName = name;
                        if (later) { pending = saved; return ""; }
                        if (failure == null) saved("C:/fixture/out.vrm", 2);
                        return failure;
                    }));
                    type.GetField("exporter", Instance).SetValue(window, entry);
                    type.GetField("exporterResolved", Instance).SetValue(window, true);
                    var canSave = type.GetProperty("CanSaveCopy", Instance);
                    var save = type.GetMethod("SaveCopy", Instance);

                    // Neither the terms nor the privacy consent of the transfer service apply to a file on this PC.
                    EditorPrefs.DeleteKey(CloudVrmTransferWindow.PrivacyConsentPreference);
                    Assert.That(canSave.GetValue(window), Is.True);
                    save.Invoke(window, null);
                    Assert.That(savedName, Is.EqualTo("vrvlog-app-transfer-save"), "Without a name, the avatar's.");
                    Assert.That(type.GetField("savedCopy", Instance).GetValue(window), Is.EqualTo("C:/fixture/out.vrm"));
                    Assert.That(type.GetField("savedCopyMissingShaders", Instance).GetValue(window), Is.EqualTo(2));

                    failure = "fixture failure\nsecond line";
                    save.Invoke(window, null);
                    Assert.That(type.GetField("savedCopy", Instance).GetValue(window), Is.Null);
                    Assert.That(type.GetField("saveProblem", Instance).GetValue(window), Is.EqualTo(failure));

                    // The result list saves later; by then the panel may show another avatar, whose row stays as it is.
                    later = true;
                    save.Invoke(window, null);
                    type.GetField("avatar", Instance).SetValue(window, other);
                    pending("C:/fixture/late.vrm", 0);
                    Assert.That(type.GetField("savedCopy", Instance).GetValue(window), Is.Null);
                    type.GetField("avatar", Instance).SetValue(window, avatar);

                    // The transfer's own export holds the Editor; the button waits for it.
                    var flow = new AppTransferFlow();
                    type.GetField("flow", Instance).SetValue(window, flow);
                    flow.BeginExport();
                    Assert.That(canSave.GetValue(window), Is.False);
                }
                finally { Object.DestroyImmediate(avatar); Object.DestroyImmediate(other); }
            });
        }

        [Test]
        public void AppTransferExportsOnlyWithTheSupportedDependencyVersions()
        {
            var diagnostics = Type.GetType("VRVlog.LilToonExporter.Compatibility.DependencyDiagnostics, VRVlog.LilToonExporter.Compatibility", true);
            var installed = diagnostics.GetMethod("Installed").Invoke(null, null);
            var dependencyError = (string)diagnostics.GetMethod("Error").Invoke(null, new[] { installed });
            var resolvable = typeof(AppTransferPanel).GetNestedType("ExportEntry", BindingFlags.NonPublic)
                .GetMethod("Resolve", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null) != null;
            var window = new AppTransferPanel();
            var exporter = typeof(AppTransferPanel).GetProperty("Exporter", Instance).GetValue(window);
            // An unsupported lilToon or UniVRM leaves the window on the diagnostics message and button.
            Assert.That(exporter != null, Is.EqualTo(dependencyError == null && resolvable));
            var problem = (string)typeof(AppTransferPanel).GetField("exporterProblem", Instance).GetValue(window);
            if (exporter == null) Assert.That(string.IsNullOrEmpty(problem), Is.False);
            if (dependencyError != null && !EditorApplication.isCompiling && !EditorApplication.isUpdating) Assert.That(problem.StartsWith(dependencyError, StringComparison.Ordinal), Is.True);
        }

        [Test]
        public void QrReadySoundDefaultsOnAndIsSaved()
        {
            var exists = EditorPrefs.HasKey(AppTransferWindow.QrReadySoundPreference);
            var saved = EditorPrefs.GetBool(AppTransferWindow.QrReadySoundPreference, true);
            try
            {
                EditorPrefs.DeleteKey(AppTransferWindow.QrReadySoundPreference);
                Assert.That(AppTransferWindow.QrReadySound, Is.True);
                AppTransferWindow.QrReadySound = false;
                Assert.That(EditorPrefs.GetBool(AppTransferWindow.QrReadySoundPreference, true), Is.False);
                Assert.That(AppTransferWindow.QrReadySound, Is.False);
            }
            finally
            {
                if (exists) EditorPrefs.SetBool(AppTransferWindow.QrReadySoundPreference, saved);
                else EditorPrefs.DeleteKey(AppTransferWindow.QrReadySoundPreference);
            }
        }

        private static void WithPrivacyPreference(Action test)
        {
            var exists = EditorPrefs.HasKey(CloudVrmTransferWindow.PrivacyConsentPreference);
            var saved = EditorPrefs.GetBool(CloudVrmTransferWindow.PrivacyConsentPreference, false);
            try { test(); }
            finally
            {
                if (exists) EditorPrefs.SetBool(CloudVrmTransferWindow.PrivacyConsentPreference, saved);
                else EditorPrefs.DeleteKey(CloudVrmTransferWindow.PrivacyConsentPreference);
            }
        }

        [Test]
        public void DevelopmentFileSelectionCopiesTheVrmWithoutStartingAnUploadAndClosureDeletesOnlyTheCopy()
        {
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-dev-selected-" + Guid.NewGuid().ToString("N") + ".vrm");
            var bytes = new byte[] { 51, 52, 53, 54 };
            CloudVrmTransferWindow window = null;
            string copyPath = null;
            try
            {
                File.WriteAllBytes(path, bytes);
                if (!CloudTransferAvailability.Enabled)
                {
                    Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path));
                    Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                    return;
                }
                CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path);
                window = EditorWindow.GetWindow<CloudVrmTransferWindow>();
                var source = (CloudVrmTransferSource)Get(window, "source");
                Assert.That(source, Is.Not.Null);
                Assert.That(source.Name, Is.EqualTo(Path.GetFileName(path)));
                using (var reader = source.OpenRead())
                {
                    copyPath = reader.Name;
                    Assert.That(Path.GetFullPath(copyPath), Is.Not.EqualTo(Path.GetFullPath(path)));
                    using (var copied = new MemoryStream())
                    {
                        reader.CopyTo(copied);
                        Assert.That(copied.ToArray(), Is.EqualTo(bytes));
                    }
                }
                foreach (var field in new[] { "session", "uploading", "polling", "qrTexture" })
                    Assert.That(Get(window, field), Is.Null, "Selecting a file must wait for the explicit upload button.");
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Object.DestroyImmediate(window); window = null;
                Assert.That(File.Exists(copyPath), Is.False);
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(bytes));
                Assert.Throws<ObjectDisposedException>(() => source.OpenRead());
            }
            finally { if (window != null) Object.DestroyImmediate(window); if (copyPath != null && File.Exists(copyPath)) File.Delete(copyPath); if (File.Exists(path)) File.Delete(path); }
        }

        [TestCase(null)]
        [TestCase("")]
        public void CanceledDevelopmentFileSelectionKeepsTheExistingSourceAndSession(string selection)
        {
            var window = EditorWindow.GetWindow<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-dev-existing-" + Guid.NewGuid().ToString("N") + ".vrm");
            CloudVrmTransferSource source = null;
            CloudVrmTransferSession session = null;
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                source = new CloudVrmTransferSource(path, "existing.vrm");
                session = new CloudVrmTransferSession(source);
                Set(window, "source", source); Set(window, "session", session);
                if (CloudTransferAvailability.Enabled) CloudVrmTransferWindow.OpenSavedVrmForDevelopment(selection);
                else Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(selection));
                Assert.That(Get(window, "source"), Is.SameAs(source));
                Assert.That(Get(window, "session"), Is.SameAs(session));
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Preparing));
                Assert.That(File.Exists(path), Is.True);
                using (var reader = source.OpenRead()) Assert.That(reader.Length, Is.EqualTo(3));
                Assert.That(Get(window, "uploading"), Is.Null);
            }
            finally { Object.DestroyImmediate(window); session?.Dispose(); source?.Dispose(); if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void RejectedDevelopmentFileSelectionKeepsTheExistingSourceAndSession()
        {
            var window = EditorWindow.GetWindow<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-dev-existing-invalid-" + Guid.NewGuid().ToString("N") + ".vrm");
            CloudVrmTransferSource source = null;
            CloudVrmTransferSession session = null;
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                source = new CloudVrmTransferSource(path, "existing.vrm");
                session = new CloudVrmTransferSession(source);
                Set(window, "source", source); Set(window, "session", session);
                if (CloudTransferAvailability.Enabled)
                    Assert.Throws<FileNotFoundException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path + ".missing.vrm"));
                else Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.OpenSavedVrmForDevelopment(path + ".missing.vrm"));
                Assert.That(Get(window, "source"), Is.SameAs(source));
                Assert.That(Get(window, "session"), Is.SameAs(session));
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Preparing));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(Get(window, "uploading"), Is.Null);
            }
            finally { Object.DestroyImmediate(window); session?.Dispose(); source?.Dispose(); if (File.Exists(path)) File.Delete(path); }
        }

        [Test]
        public void InvalidOwnedSelectionKeepsTheExistingSessionAndDeletesOnlyTheRejectedSnapshot()
        {
            if (!CloudTransferAvailability.Enabled)
            {
                Assert.Throws<NotSupportedException>(() => CloudVrmTransferWindow.CreateSnapshotPath());
                return;
            }
            var window = EditorWindow.GetWindow<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-dev-existing-owned-" + Guid.NewGuid().ToString("N") + ".vrm");
            var rejected = CloudVrmTransferWindow.CreateSnapshotPath();
            CloudVrmTransferSource source = null;
            CloudVrmTransferSession session = null;
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                source = new CloudVrmTransferSource(path, "existing.vrm");
                session = new CloudVrmTransferSession(source);
                Set(window, "source", source); Set(window, "session", session);
                Directory.CreateDirectory(Path.GetDirectoryName(rejected));
                File.WriteAllBytes(rejected, Array.Empty<byte>());
                Assert.Throws<InvalidOperationException>(() => CloudVrmTransferWindow.Show(rejected, "empty.vrm"));
                Assert.That(Get(window, "source"), Is.SameAs(source));
                Assert.That(Get(window, "session"), Is.SameAs(session));
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Preparing));
                Assert.That(File.Exists(path), Is.True);
                Assert.That(File.Exists(rejected), Is.False);
                Assert.That(Get(window, "uploading"), Is.Null);
                var owned = (HashSet<string>)typeof(CloudVrmTransferWindow).GetField("OwnedSnapshots", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                Assert.That(owned.Contains(rejected), Is.False);
            }
            finally { Object.DestroyImmediate(window); session?.Dispose(); source?.Dispose(); if (File.Exists(path)) File.Delete(path); if (File.Exists(rejected)) File.Delete(rejected); }
        }
#endif

        [Test]
        public void WindowClosureCancelsStartupAndDeletesOnlyTheOwnedSnapshot()
        {
            var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-cloud-window-" + Guid.NewGuid().ToString("N") + ".vrm");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var source = new CloudVrmTransferSource(path, "fixture.vrm");
            var session = new CloudVrmTransferSession(source);
            try
            {
                typeof(CloudVrmTransferWindow).GetField("source", Instance).SetValue(window, source);
                typeof(CloudVrmTransferWindow).GetField("session", Instance).SetValue(window, session);
                typeof(CloudVrmTransferWindow).GetMethod("StopAndClean", Instance).Invoke(window, null);
                Assert.That(session.State, Is.EqualTo(CloudTransferState.Canceled));
                Assert.That(File.Exists(path), Is.False);
                Assert.Throws<ObjectDisposedException>(() => source.OpenRead());
            }
            finally { Object.DestroyImmediate(window); session.Dispose(); source.Dispose(); }
        }

        [Test]
        public void RestoredWindowWithoutItsNonserializedSourceCannotStartAnUpload()
        {
            var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
            try
            {
                typeof(CloudVrmTransferWindow).GetMethod("Begin", Instance).Invoke(window, null);
                Assert.That(typeof(CloudVrmTransferWindow).GetField("session", Instance).GetValue(window), Is.Null);
                Assert.That(typeof(CloudVrmTransferWindow).GetField("uploading", Instance).GetValue(window), Is.Null);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void SessionFailureIsDisplayedOnceWhileDistinctWindowErrorsRemainVisible()
        {
            var window = ScriptableObject.CreateInstance<CloudVrmTransferWindow>();
            var path = Path.Combine(Path.GetTempPath(), "vrvlog-message-test-" + Guid.NewGuid().ToString("N") + ".vrm");
            File.WriteAllBytes(path, new byte[] { 1 });
            using (var source = new CloudVrmTransferSource(path, "fixture.vrm"))
            using (var session = new CloudVrmTransferSession(source))
            {
                try
                {
                    Set(window, "session", session); Set(window, "error", session.Message);
                    Assert.That(window.ExtraMessage, Is.Null);
                    Set(window, "error", "Distinct QR rendering failure");
                    Assert.That(window.ExtraMessage, Is.EqualTo("Distinct QR rendering failure"));
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
            }
        }

        private static object Get(CloudVrmTransferWindow window, string name) => typeof(CloudVrmTransferWindow).GetField(name, Instance).GetValue(window);
        private static void Set(CloudVrmTransferWindow window, string name, object value) => typeof(CloudVrmTransferWindow).GetField(name, Instance).SetValue(window, value);
    }
}
