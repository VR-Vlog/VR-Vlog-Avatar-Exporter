using Unity.Collections;
using UnityEngine.Jobs;
using VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.Blittables;
#if ENABLE_SPRINGBONE_BURST
using Unity.Burst;
#endif

namespace VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs
{
#if ENABLE_SPRINGBONE_BURST
    [BurstCompile]
#endif
    public struct PushTransformJob : IJobParallelForTransform
    {
        [ReadOnly]
        public NativeArray<BlittableTransform> Transforms;

        public void Execute(int index, TransformAccess transform)
        {
            transform.rotation = Transforms[index].rotation;
        }
    }
}