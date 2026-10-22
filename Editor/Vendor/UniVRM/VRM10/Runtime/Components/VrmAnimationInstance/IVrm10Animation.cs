using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniVRM10
{
    public interface IVrm10Animation : IDisposable
    {
        (INormalizedPoseProvider, ITPoseProvider) ControlRig { get; }
        IReadOnlyDictionary<ExpressionKey, Func<float>> ExpressionMap { get; }
        public void ShowBoxMan(bool enable);
        public void SetBoxManMaterial(Material material);
        LookAtInput? LookAt { get; }
    }
}
