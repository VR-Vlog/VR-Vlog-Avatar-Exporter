using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // NaN and infinity checks shared by the exporter. Files import them with
    // `using static` so call sites stay `Finite(value)`.
    internal static class NumericChecks
    {
        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        internal static bool Finite(Vector4 value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w);
    }
}
