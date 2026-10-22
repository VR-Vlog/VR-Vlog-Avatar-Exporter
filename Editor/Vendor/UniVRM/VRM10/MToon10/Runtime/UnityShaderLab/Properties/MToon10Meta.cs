using System;

namespace VRVlog.LilToonExporter.Vendor.VRM10.MToon10
{
    public static class MToon10Meta
    {
        public static readonly string UnityShaderName = "Hidden/VRVlog/LilToonExporter/Vendor/MToon10";
        public static readonly string UnityUrpShaderName = "Hidden/VRVlog/LilToonExporter/Vendor/MToon10 URP";

        [Obsolete("Use UnityUrpShaderName instead")]
        public static readonly string URPUnityShaderName = UnityUrpShaderName;
    }
}