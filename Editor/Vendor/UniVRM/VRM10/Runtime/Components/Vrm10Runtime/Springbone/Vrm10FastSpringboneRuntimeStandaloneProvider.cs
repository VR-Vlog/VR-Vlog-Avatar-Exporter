using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    [UnityEngine.AddComponentMenu("")]
    public class Vrm10FastSpringboneRuntimeStandaloneProvider : MonoBehaviour, IVrm10SpringBoneRuntimeProvider
    {
        public IVrm10SpringBoneRuntime CreateSpringBoneRuntime()
        {
            return new Vrm10FastSpringboneRuntimeStandalone();
        }
    }
}