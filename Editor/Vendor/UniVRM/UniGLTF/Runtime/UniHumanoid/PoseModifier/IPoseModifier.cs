using UnityEngine;


namespace VRVlog.LilToonExporter.Vendor.UniHumanoid
{
    public interface IPoseModifier
    {
        void Modify(ref HumanPose pose);
    }
}
