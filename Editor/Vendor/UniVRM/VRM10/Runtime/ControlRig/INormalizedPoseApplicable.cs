using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    public interface INormalizedPoseApplicable
    {
        void SetRawHipsPosition(Vector3 position);

        void SetNormalizedLocalRotation(HumanBodyBones bone, Quaternion normalizedLocalRotation);
    }
}
