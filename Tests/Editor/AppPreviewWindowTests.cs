using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    // The preview window's own logic with a fake backend, so it runs without
    // the VR Vlog reader package. Tests/AppPreview covers the real package.
    public sealed class AppPreviewWindowTests
    {
        const string ModeKey = "VRVlog.LilToonExporter.AppPreview.MaterialMode";

        IAppPreviewBackend savedBackend;
        int savedMode;
        readonly List<FakeSession> sessions = new List<FakeSession>();

        static string T(string source) => ExporterLocalization.T(source);

        [SetUp]
        public void SetUp()
        {
            savedBackend = AppPreviewEntry.Backend;
            savedMode = SessionState.GetInt(ModeKey, 0);
            AppPreviewEntry.Backend = null;
            SessionState.SetInt(ModeKey, 0);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<AppPreviewWindow>()) window.Close();
            foreach (var session in sessions) session.Dispose();
            sessions.Clear();
            AppPreviewEntry.Backend = savedBackend;
            SessionState.SetInt(ModeKey, savedMode);
        }

        [Test]
        public async Task SelectingAnExpressionKeepsTheManualEyeClosure()
        {
            AppPreviewEntry.Backend = new FakeBackend(this);
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "fake.vrm");
            await window.LastLoad;
            var session = sessions[0];
            window.SetBlinkAmount(.8f);
            session.Calls.Clear();

            // In VRM 1.0 clearing the expressions can also open the eyes.
            window.SelectExpression("happy");
            Assert.That(session.Calls, Is.EqualTo(new[] { "ClearExpressions", "SetExpression happy 1", "SetEyeBlink 0.8 0.8" }));
            session.Calls.Clear();
            window.SelectExpression(null);
            Assert.That(session.Calls, Is.EqualTo(new[] { "ClearExpressions", "SetEyeBlink 0.8 0.8" }));
        }

        [Test]
        public async Task TheAnimationButtonTurnsBackToPlayWhenASinglePlayEnds()
        {
            AppPreviewEntry.Backend = new FakeBackend(this);
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "fake.vrm");
            await window.LastLoad;
            var session = sessions[0];
            var field = typeof(AppPreviewWindow).GetField("playingAnimation", BindingFlags.Instance | BindingFlags.NonPublic);

            window.PlayAnimation("wave");
            window.Step(.1f);
            Assert.That(field.GetValue(window), Is.EqualTo("wave"));
            session.PlayingExpressionAnimation = null;
            window.Step(.1f);
            Assert.That(field.GetValue(window), Is.Null);
        }

        [Test]
        public async Task AfterAScriptReloadTheWindowOffersToLoadTheFileAgain()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assert.That(typeof(AppPreviewWindow).GetField("bytes", flags).IsNotSerialized, Is.True,
                "The VRM bytes stay out of the reload state.");
            var path = Path.Combine(Path.GetTempPath(), "AppPreviewWindowTests-" + Guid.NewGuid().ToString("N") + ".vrm");
            File.WriteAllBytes(path, new byte[] { 1, 2 });
            try
            {
                var backend = new FakeBackend(this);
                AppPreviewEntry.Backend = backend;
                var window = AppPreviewWindow.Open(File.ReadAllBytes(path), "fake.vrm", path);
                await window.LastLoad;
                var first = sessions[0];

                // What a script reload does: OnDisable, the non-serialized fields
                // reset, OnEnable.
                typeof(AppPreviewWindow).GetMethod("OnDisable", flags).Invoke(window, null);
                typeof(AppPreviewWindow).GetField("bytes", flags).SetValue(window, null);
                typeof(AppPreviewWindow).GetMethod("OnEnable", flags).Invoke(window, null);
                Assert.That(first.Disposed, Is.True);
                Assert.That(window.Load, Is.Null);
                Assert.That(window.Error, Is.EqualTo(T("読み込んでいません")));
                Assert.That(window.CanReload, Is.True);

                window.Reload();
                await window.LastLoad;
                Assert.That(window.Error, Is.Null);
                Assert.That(window.Load, Is.SameAs(sessions[1]));
                Assert.That(backend.LastBytes, Is.EqualTo(new byte[] { 1, 2 }), "The file is read again.");
            }
            finally { File.Delete(path); }
        }

        sealed class FakeBackend : IAppPreviewBackend
        {
            readonly AppPreviewWindowTests owner;
            internal TaskCompletionSource<IAppPreviewSession> Pending;
            internal bool LastStandardMToon;
            internal string LastName;
            internal byte[] LastBytes;
            internal CancellationToken LastToken;

            internal FakeBackend(AppPreviewWindowTests owner) => this.owner = owner;

            public Task<IAppPreviewSession> LoadAsync(byte[] bytes, string name, bool standardMToon, CancellationToken token)
            {
                LastStandardMToon = standardMToon; LastName = name; LastToken = token; LastBytes = bytes;
                if (Pending != null) return Pending.Task;
                var session = new FakeSession(true);
                owner.sessions.Add(session);
                return Task.FromResult<IAppPreviewSession>(session);
            }
        }

        sealed class FakeSession : IAppPreviewSession
        {
            internal bool Disposed, SpringsEnabled;
            internal readonly List<string> Calls = new List<string>();

            static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

            // A cube gives the window a bounds and something to render.
            internal FakeSession(bool withRoot)
            {
                if (!withRoot) return;
                Root = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Root.name = "Fake preview";
            }

            public GameObject Root { get; }
            public string MaterialStatus { get; set; } = "full-liltoon:materials:1";
            public string ErrorCode { get; set; }
            public string ErrorMessage { get; set; }
            public IReadOnlyList<string> Expressions { get; } = new[] { "happy" };
            public IReadOnlyList<string> ExpressionAnimations { get; } = Array.Empty<string>();
            public bool HasSpringBones => true;

            public void Tick(float deltaTime) { }
            public void SetExpression(string name, float weight) => Calls.Add("SetExpression " + name + " " + Number(weight));
            public void ClearExpressions() => Calls.Add("ClearExpressions");
            public void PlayExpressionAnimation(string name)
            {
                Calls.Add("PlayExpressionAnimation " + name);
                PlayingExpressionAnimation = name;
            }

            public string PlayingExpressionAnimation { get; set; }
            public void SetEyeBlink(float left, float right) => Calls.Add("SetEyeBlink " + Number(left) + " " + Number(right));
            public void SetSpringBonesEnabled(bool enabled) => SpringsEnabled = enabled;
            public void ResetSpringBones() { }

            public void Dispose()
            {
                Disposed = true;
                if (Root != null) Object.DestroyImmediate(Root);
            }
        }
    }
}
