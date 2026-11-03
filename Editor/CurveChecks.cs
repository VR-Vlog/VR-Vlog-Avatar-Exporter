using UnityEngine;
using static VRVlog.LilToonExporter.NumericChecks;

namespace VRVlog.LilToonExporter
{
    // Whether an authored float curve holds its value. The callers differ on
    // purpose; the differences are arguments, not separate rules.
    internal static class CurveChecks
    {
        internal static bool Constant(AnimationCurve curve, bool emptyIsConstant = true) => Constant(curve, out _, emptyIsConstant);

        // value is the first key's value. emptyIsConstant: a missing curve or
        // one without keys counts as constant. endTangents: the first key's
        // in and the last key's out tangent, which no segment uses, must be
        // flat too. finite: the value and every key time must be finite.
        internal static bool Constant(AnimationCurve curve, out float value, bool emptyIsConstant = true, bool endTangents = true, bool finite = false)
        {
            value = 0;
            if (curve == null || curve.length == 0) return emptyIsConstant;
            var keys = curve.keys; value = keys[0].value;
            if (finite && !Finite(value)) return false;
            for (var index = 0; index < keys.Length; index++)
            {
                var key = keys[index];
                if (key.value != keys[0].value || finite && !Finite(key.time) ||
                    (endTangents || index > 0) && !Flat(key.inTangent) ||
                    (endTangents || index + 1 < keys.Length) && !Flat(key.outTangent)) return false;
            }
            return true;
        }

        // Every segment that ends after time holds one value. Equal endpoint
        // values alone cannot exclude Hermite/weighted overshoot.
        internal static bool HoldsAfter(AnimationCurve curve, double time)
        {
            var keys = curve.keys;
            for (var index = 1; index < keys.Length; index++)
            {
                var left = keys[index - 1]; var right = keys[index];
                if (right.time <= time) continue;
                if (left.value != right.value || !Flat(left.outTangent) || !Flat(right.inTangent)) return false;
            }
            return true;
        }

        static bool Flat(float tangent) => tangent == 0 || float.IsInfinity(tangent);
    }
}
