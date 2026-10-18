using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FaceMaskVTuber.UniVrmRuntime;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The only file that uses the VR Vlog reader package (com.vrvlog.avatar-runtime).
    // Its assembly compiles only when that package and UniVRM 0.131 are
    // installed. If the package moves, this file and its asmdef are the ones to
    // change; the window talks only to IAppPreviewBackend and IAppPreviewSession.
    internal sealed class AvatarRuntimeBackend : IAppPreviewBackend
    {
        // The reader package swaps a global shader hook while it loads, so two
        // loads, even from two windows, must never overlap.
        static Task pendingLoad = Task.CompletedTask;

        [InitializeOnLoadMethod]
        static void Register() => AppPreviewEntry.Backend = new AvatarRuntimeBackend();

        public Task<IAppPreviewSession> LoadAsync(byte[] bytes, string name, bool standardMToon, CancellationToken token)
        {
            var options = new AvatarPreviewOptions
            {
                materialMode = standardMToon ? AvatarPreviewMaterialMode.StandardMToon : AvatarPreviewMaterialMode.App,
            };
            var started = Start(pendingLoad, bytes, name, options, token);
            pendingLoad = started;
            return started;
        }

        static async Task<IAppPreviewSession> Start(Task previous, byte[] bytes, string name, AvatarPreviewOptions options, CancellationToken token)
        {
            // The owner of the previous load handles its result and its failure.
            try { await previous; } catch (Exception) { }
            var load = await AvatarRuntimePreview.LoadAsync(bytes, name, options, token);
            return load == null ? null : new Session(load);
        }

        sealed class Session : IAppPreviewSession
        {
            readonly AvatarPreviewLoad load;

            internal Session(AvatarPreviewLoad load) => this.load = load;

            public GameObject Root => load.Root;
            public string MaterialStatus => load.MaterialStatus;
            public string ErrorCode => load.ErrorCode;
            public string ErrorMessage => load.ErrorMessage;
            public IReadOnlyList<string> Expressions => load.Expressions ?? (IReadOnlyList<string>)Array.Empty<string>();
            public IReadOnlyList<string> ExpressionAnimations => load.ExpressionAnimations ?? (IReadOnlyList<string>)Array.Empty<string>();
            public bool HasSpringBones => load.HasSpringBones;

            public void Tick(float deltaTime) => AvatarRuntimePreview.Tick(load, deltaTime);
            public void SetExpression(string name, float weight) => AvatarRuntimePreview.SetExpression(load, name, weight);
            public void ClearExpressions() => AvatarRuntimePreview.ClearExpressions(load);
            public void PlayExpressionAnimation(string name) => AvatarRuntimePreview.PlayExpressionAnimation(load, name);
            public void SetEyeBlink(float left, float right) => AvatarRuntimePreview.SetEyeBlink(load, left, right);
            public void SetSpringBonesEnabled(bool enabled) => AvatarRuntimePreview.SetSpringBonesEnabled(load, enabled);
            public void ResetSpringBones() => AvatarRuntimePreview.ResetSpringBones(load);
            public void Dispose() => load.Dispose();
        }
    }
}
