using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter.Vendor.UniGLTF
{
    public interface IAnimationExporter
    {
        void Export(ExportingGltfData _data, GameObject Copy, List<Transform> Nodes);
    }
}
