using System;
using System.Threading;
using System.Threading.Tasks;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniVRM10;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter
{
    // The expression board's view of the VRM it would write now: the bytes of an
    // unsaved export, read back as ExportRecoveryComparisonWindow reads them, with
    // the lilToon materials the app uses restored from the file. Nothing here
    // comes from the avatar in the scene; Dispose destroys all of it.
    internal sealed class ExportedLookPreview : IDisposable
    {
        Vrm10Instance imported;
        IDisposable lilToon;
        internal GameObject Root => imported != null ? imported.gameObject : null;
        internal PreviewRenderUtility Preview { get; private set; }
        internal Vector3 Center { get; private set; }
        internal Vector3 Forward { get; private set; }
        internal float Distance { get; private set; }
        // When the export finished, shown so the user can tell an old look from a new one.
        internal DateTime MadeAt { get; private set; }
        // Null when the file's lilToon materials are shown; otherwise why the standard MToon ones are.
        internal string MaterialNote { get; private set; }

        internal static async Task<ExportedLookPreview> LoadAsync(byte[] bytes, CancellationToken token)
        {
            // UniVRM owns the loaded meshes, materials and textures through its
            // RuntimeGltfInstance. Destroying the root releases them too.
            var loaded = await Vrm10.LoadBytesAsync(bytes, canLoadVrm0X: false, awaitCaller: new ImmediateCaller(), ct: token,
                controlRigGenerationOption: ControlRigGenerationOption.None);
            if (loaded == null) throw new InvalidOperationException(ExporterLocalization.T("VRMの再読み込み結果を取得できませんでした。"));
            var look = new ExportedLookPreview { imported = loaded, MadeAt = DateTime.Now };
            try
            {
                token.ThrowIfCancellationRequested();
                // A VRM written without the lilToon extension (no lilToon material,
                // or over the app's limits) is shown with the MToon the app falls back to.
                try { look.lilToon = ExportVrmLilToonPreview.Apply(bytes, loaded.gameObject); }
                catch (Exception error) when (!(error is OperationCanceledException)) { look.MaterialNote = error.Message; }
                ExportRecoveryComparisonWindow.NormalizeForPreview(loaded.gameObject);
                loaded.UpdateType = Vrm10Instance.UpdateTypes.None;
                look.Preview = ExportRecoveryComparisonWindow.CreateRenderer(loaded.gameObject);
                look.Frame();
                return look;
            }
            catch { look.Dispose(); throw; }
        }

        // The board frames the avatar the same way: just above the head, at a distance from its height.
        void Frame()
        {
            var bounds = ExportRecoveryComparisonWindow.VisibleBounds(imported.gameObject);
            var head = imported.TryGetBoneTransform(HumanBodyBones.Head, out var bone) ? bone : null;
            Center = head != null ? head.position + Vector3.up * bounds.size.y * .065f : bounds.center;
            Distance = head != null ? Mathf.Max(.35f, bounds.size.y * .65f) : Mathf.Max(.5f, bounds.size.magnitude * 1.2f);
            Forward = imported.transform.forward;
        }

        internal Texture Render(Rect rect, float yaw, float zoom)
        {
            Preview.BeginPreview(rect, GUIStyle.none);
            Preview.camera.transform.position = Center + Quaternion.AngleAxis(yaw, Vector3.up) * Forward * Distance * zoom;
            Preview.camera.transform.LookAt(Center, Vector3.up);
            Preview.Render();
            return Preview.EndPreview();
        }

        public void Dispose()
        {
            // The utility owns only its camera and lights; the loaded root is ours.
            Preview?.Cleanup(); Preview = null;
            lilToon?.Dispose(); lilToon = null;
            if (imported != null) Object.DestroyImmediate(imported.gameObject);
            imported = null;
        }
    }
}
