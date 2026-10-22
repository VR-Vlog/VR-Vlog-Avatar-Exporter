using System;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.Blittables;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.InputPorts
{
    [Serializable]
    public struct FastSpringBoneCollider
    {
        public Transform Transform;
        public BlittableCollider Collider;
    }
}