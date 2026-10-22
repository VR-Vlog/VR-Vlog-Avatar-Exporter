using VRVlog.LilToonExporter.Vendor.UniGLTF;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    public class VrmAnimationData
    {
        public GltfData Data { get; }

        public VrmAnimationData(GltfData data)
        {
            Data = data;

            // ヒューマノイド向け
            ForceGltfNodeUniqueName.Process(Data.GLTF.nodes);
        }
    }
}