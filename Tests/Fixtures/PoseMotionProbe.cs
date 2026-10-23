using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Stands in for a tool component that holds pose motions, as clips or as
    // an animator controller, and plays them on the avatar.
    public sealed class PoseMotionProbe : MonoBehaviour
    {
        public AnimationClip[] Clips = new AnimationClip[0];
        public RuntimeAnimatorController Controller;
    }
}
