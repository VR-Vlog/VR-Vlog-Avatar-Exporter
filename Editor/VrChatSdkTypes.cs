using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // VRChat SDK animator behaviours, matched by full type name: the SDK3
    // component and the VRC.SDKBase name it derives from. Short names are not
    // compared, so a same-named type in another namespace never counts.
    internal static class VrChatSdkTypes
    {
        internal const string ParameterDriver = "AvatarParameterDriver", TrackingControl = "AnimatorTrackingControl",
            LocomotionControl = "AnimatorLocomotionControl", PlayableLayerControl = "PlayableLayerControl",
            AnimatorLayerControl = "AnimatorLayerControl", TemporaryPoseSpace = "AnimatorTemporaryPoseSpace", PlayAudio = "AnimatorPlayAudio";

        internal static bool Is(Object value, string name)
        {
            if (value == null) return false;
            var type = value.GetType().FullName;
            return type == "VRC.SDK3.Avatars.Components.VRC" + name || type == "VRC.SDKBase.VRC_" + name;
        }
    }
}
