using System;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // 視線の動く範囲: how far the exported eyes follow a gaze, in degrees.
    // 0 keeps what the avatar sets (VrmLookAtExport.Inspect), so an avatar
    // nobody changed exports as before. Side sets both horizontal limits, as
    // VRChat's Eye Look turns the eyes the same way to either side.
    [Serializable]
    internal sealed class GazeRangeOptions
    {
        // The sliders stop here unless the avatar already allows more.
        internal const int SliderMaximum = 30;

        public float Up, Down, Side;

        internal bool IsDefault => Up <= 0 && Down <= 0 && Side <= 0;

        internal GazeRangeOptions Copy() => new GazeRangeOptions { Up = Up, Down = Down, Side = Side };

        // A stored value outside what an eye can turn is dropped back to the avatar's.
        internal static float Valid(float value) =>
            float.IsNaN(value) || value < VrmLookAtExport.MinimumDegrees || value > VrmLookAtExport.MaximumDegrees ? 0 : value;

        internal GazeRangeOptions Validated() => new GazeRangeOptions { Up = Valid(Up), Down = Valid(Down), Side = Valid(Side) };

        internal string Key() => FormattableString.Invariant($"{Up:R},{Down:R},{Side:R}");
    }
}
