using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The app preview's only boundary to the VR Vlog reader package. The one
    // implementation, Editor/AppPreview/AvatarRuntimeBackend.cs, compiles only
    // when that package is installed and registers itself here from
    // [InitializeOnLoadMethod]. Null means the preview is unavailable: the menu
    // item is disabled and the export window hides its button.
    internal static class AppPreviewEntry
    {
        internal static IAppPreviewBackend Backend;
    }

    internal interface IAppPreviewBackend
    {
        // Loads one VRM. Loads never overlap, even from two windows. A failure
        // the reader reports comes back as a session with a null Root; the task
        // throws only for an unexpected failure or a cancellation.
        Task<IAppPreviewSession> LoadAsync(byte[] bytes, string name, bool standardMToon, CancellationToken token);
    }

    // One loaded VRM. Dispose destroys Root and everything the load created.
    internal interface IAppPreviewSession : IDisposable
    {
        GameObject Root { get; }
        string MaterialStatus { get; }
        string ErrorCode { get; }
        string ErrorMessage { get; }
        IReadOnlyList<string> Expressions { get; }
        IReadOnlyList<string> ExpressionAnimations { get; }
        bool HasSpringBones { get; }

        // Called every frame with a positive step. Expressions and their
        // animations advance here; spring bones move only while enabled.
        void Tick(float deltaTime);
        void SetExpression(string name, float weight);
        // Also stops the playing expression animation.
        void ClearExpressions();
        // Null stops the playing animation.
        void PlayExpressionAnimation(string name);
        void SetEyeBlink(float left, float right);
        void SetSpringBonesEnabled(bool enabled);
        void ResetSpringBones();
    }
}
