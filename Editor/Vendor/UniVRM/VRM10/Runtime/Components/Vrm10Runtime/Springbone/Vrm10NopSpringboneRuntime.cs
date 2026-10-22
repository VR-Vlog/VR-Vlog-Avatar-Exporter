using System.Threading.Tasks;
using VRVlog.LilToonExporter.Vendor.UniGLTF;
using VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.Blittables;
using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    /// <summary>
    /// SpcriptedImporter 経由の import 向け。
    /// NativeArray の確保や DontDestroyOnLoad を回避。
    /// </summary>
    public class Vrm10NopSpringboneRuntime : IVrm10SpringBoneRuntime
    {
        public void Dispose()
        {
        }

        public Task InitializeAsync(Vrm10Instance instance, IAwaitCaller awaitCaller)
        {
            return Task.CompletedTask;
        }

        public void Process(float deltaTime)
        {
        }

        public bool ReconstructSpringBone()
        {
            return false;
        }

        public void RestoreInitialTransform()
        {
        }

        public void SetJointLevel(Transform joint, BlittableJointMutable jointSettings)
        {
        }

        public void SetModelLevel(Transform modelRoot, BlittableModelLevel modelSettings)
        {
        }

        public void DrawGizmos() { }
    }
}