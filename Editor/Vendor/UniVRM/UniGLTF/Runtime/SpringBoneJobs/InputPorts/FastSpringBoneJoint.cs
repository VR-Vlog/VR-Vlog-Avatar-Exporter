using System;
using UnityEngine;
using VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.Blittables;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF.SpringBoneJobs.InputPorts
{
    [Serializable]
    public struct FastSpringBoneJoint
    {
        public Transform Transform;
        public BlittableJointMutable Joint;
        public Quaternion DefaultLocalRotation;
    }
}