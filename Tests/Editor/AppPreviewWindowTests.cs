using System;
using System.Collections.Generic;
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
        public void MaterialSummaryNamesTheReaderResult()
        {
            Assert.That(AppPreviewWindow.MaterialSummary("full-liltoon:materials:3"), Is.EqualTo(T("lilToon で表示")));
            Assert.That(AppPreviewWindow.MaterialSummary("full-liltoon:materials:3:material-fallbacks:2"), Is.EqualTo(T("一部を MToon で表示")));
            Assert.That(AppPreviewWindow.MaterialSummary("approximated-mtoon:materials:3"), Is.EqualTo(T("MToon で近似表示")));
            Assert.That(AppPreviewWindow.MaterialSummary("standard-mtoon"), Is.EqualTo(T("MToon で表示")));
            Assert.That(AppPreviewWindow.MaterialSummary("vrm0-standard"), Is.EqualTo(T("MToon で表示")));
            Assert.That(AppPreviewWindow.MaterialSummary("failed-memory"), Is.EqualTo(T("メモリが足りず読み込めません")));
            Assert.That(AppPreviewWindow.MaterialSummary(null), Is.EqualTo(T("材質を判定できません")));
            Assert.That(AppPreviewWindow.MaterialSummary("detecting-materials"), Is.EqualTo(T("材質を判定できません")));
            // An unknown code is shown as it is instead of being guessed.
            Assert.That(AppPreviewWindow.MaterialSummary("liltoon-full-applied"), Is.EqualTo("liltoon-full-applied"));
        }

        [Test]
        public void MaterialDetailDependsOnTheSelectedMode()
        {
            Assert.That(AppPreviewWindow.MaterialDetail("standard-mtoon", true), Is.EqualTo(T("VRM の MToon で表示しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("standard-mtoon", false), Is.EqualTo(T("lilToon の追加データがありません")));
            Assert.That(AppPreviewWindow.MaterialDetail("full-liltoon:materials:3", false), Is.EqualTo(T("lilToon で表示しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("full-liltoon:materials:3:material-fallbacks:2", false),
                Is.EqualTo(string.Format(T("{0} 件を MToon で表示しています"), 2)));
            Assert.That(AppPreviewWindow.MaterialDetail("approximated-mtoon:materials:3", false), Is.EqualTo(T("lilToon の設定を MToon で近似しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("standard-mtoon:low-power", false), Is.EqualTo(T("省電力モードのため MToon で表示しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("vrm0-standard", false), Is.EqualTo(T("VRM 0.x のため MToon で表示しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("fallback-mtoon", false), Is.EqualTo(T("lilToon を適用できず MToon で表示しています")));
            Assert.That(AppPreviewWindow.MaterialDetail("", false), Is.EqualTo(T("材質を判定できません")));
        }

        [Test]
        public void ErrorTextNamesTheFailedFormat()
        {
            Assert.That(AppPreviewWindow.ErrorText("file-missing"), Is.EqualTo(T("ファイルが見つかりません")));
            Assert.That(AppPreviewWindow.ErrorText("VRM-json-unsafe"), Is.EqualTo(T("VRM の JSON を読めません")));
            Assert.That(AppPreviewWindow.ErrorText("VRM10-parse"), Is.EqualTo(T("VRM 1.0 として読み込めません")));
            Assert.That(AppPreviewWindow.ErrorText("VRM0-parse"), Is.EqualTo(T("VRM 0.x として読み込めません")));
            Assert.That(AppPreviewWindow.ErrorText(null), Is.EqualTo(T("VRM として読み込めません")));
        }

        [Test]
        public void BlinkClosureClosesHoldsAndOpens()
        {
            // 0.1 s closing, 0.05 s closed, 0.15 s opening.
            Assert.That(AppPreviewWindow.BlinkClosure(0), Is.EqualTo(0).Within(1e-5f));
            Assert.That(AppPreviewWindow.BlinkClosure(.05f), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(AppPreviewWindow.BlinkClosure(.12f), Is.EqualTo(1).Within(1e-5f));
            Assert.That(AppPreviewWindow.BlinkClosure(.225f), Is.EqualTo(.5f).Within(1e-5f));
            Assert.That(AppPreviewWindow.BlinkClosure(.35f), Is.EqualTo(0).Within(1e-5f));
            Assert.That(AppPreviewWindow.BlinkClosure(2), Is.EqualTo(0));
        }

        [Test]
        public void TheMenuItemNeedsABackend()
        {
            Assert.That(AppPreviewWindow.CanOpen(), Is.False);
            AppPreviewEntry.Backend = new FakeBackend(this);
            Assert.That(AppPreviewWindow.CanOpen(), Is.True);
        }

        [Test]
        public async Task WithoutABackendTheWindowLoadsNothing()
        {
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "missing.vrm");
            await window.LastLoad;
            Assert.That(window.Load, Is.Null);
            Assert.That(window.Error, Is.EqualTo(T("VR Vlog アプリの読み込みパッケージがありません")));
        }

        [Test]
        public async Task ALoadedSessionIsAttachedAndDisposedOnClose()
        {
            var backend = new FakeBackend(this);
            AppPreviewEntry.Backend = backend;
            SessionState.SetInt(ModeKey, 1);
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "fake.vrm");
            await window.LastLoad;
            Assert.That(window.Error, Is.Null);
            Assert.That(backend.LastStandardMToon, Is.True);
            Assert.That(backend.LastName, Is.EqualTo("fake.vrm"));
            var session = sessions[0];
            Assert.That(window.Load, Is.SameAs(session));
            Assert.That(session.SpringsEnabled, Is.True, "Attach turns spring bones on.");

            window.Close();
            Assert.That(session.Disposed, Is.True);
            Assert.That(session.Root == null, Is.True);
        }

        [Test]
        public async Task AResultArrivingAfterCloseIsDisposed()
        {
            var pending = new TaskCompletionSource<IAppPreviewSession>();
            var backend = new FakeBackend(this) { Pending = pending };
            AppPreviewEntry.Backend = backend;
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "fake.vrm");
            var load = window.LastLoad;
            window.Close();
            Assert.That(backend.LastToken.IsCancellationRequested, Is.True);

            var late = new FakeSession(true);
            sessions.Add(late);
            pending.SetResult(late);
            await load;
            Assert.That(late.Disposed, Is.True);
        }

        [Test]
        public async Task AReaderFailureBecomesTheErrorState()
        {
            var failed = new FakeSession(false) { ErrorCode = "VRM10-parse", ErrorMessage = "bad" };
            sessions.Add(failed);
            AppPreviewEntry.Backend = new FakeBackend(this) { Pending = Completed(failed) };
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "broken.vrm");
            await window.LastLoad;
            Assert.That(window.Load, Is.Null);
            Assert.That(window.Error, Is.EqualTo(T("VRM 1.0 として読み込めません")));
            Assert.That(failed.Disposed, Is.True);
        }

        [Test]
        public async Task AThrowingBackendBecomesTheErrorState()
        {
            var pending = new TaskCompletionSource<IAppPreviewSession>();
            pending.SetException(new InvalidOperationException("reader failed"));
            AppPreviewEntry.Backend = new FakeBackend(this) { Pending = pending };
            var window = AppPreviewWindow.Open(new byte[] { 1 }, "broken.vrm");
            await window.LastLoad;
            Assert.That(window.Load, Is.Null);
            Assert.That(window.Error, Is.EqualTo(T("VRM として読み込めません")));
        }

        static TaskCompletionSource<IAppPreviewSession> Completed(IAppPreviewSession session)
        {
            var value = new TaskCompletionSource<IAppPreviewSession>();
            value.SetResult(session);
            return value;
        }

        sealed class FakeBackend : IAppPreviewBackend
        {
            readonly AppPreviewWindowTests owner;
            internal TaskCompletionSource<IAppPreviewSession> Pending;
            internal bool LastStandardMToon;
            internal string LastName;
            internal CancellationToken LastToken;

            internal FakeBackend(AppPreviewWindowTests owner) => this.owner = owner;

            public Task<IAppPreviewSession> LoadAsync(byte[] bytes, string name, bool standardMToon, CancellationToken token)
            {
                LastStandardMToon = standardMToon; LastName = name; LastToken = token;
                if (Pending != null) return Pending.Task;
                var session = new FakeSession(true);
                owner.sessions.Add(session);
                return Task.FromResult<IAppPreviewSession>(session);
            }
        }

        sealed class FakeSession : IAppPreviewSession
        {
            internal bool Disposed, SpringsEnabled;

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
            public void SetExpression(string name, float weight) { }
            public void ClearExpressions() { }
            public void PlayExpressionAnimation(string name) { }
            public void SetEyeBlink(float left, float right) { }
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
