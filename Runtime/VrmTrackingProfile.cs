using System;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    [Serializable]
    public sealed class TrackingMorph
    {
        public string shape;
        [Range(0.01f, 1f)] public float weight = 1f;
    }

    [Serializable]
    public sealed class TrackingExpression
    {
        public string name;
        public TrackingMorph[] morphs = Array.Empty<TrackingMorph>();
    }

    [CreateAssetMenu(menuName = "VR Vlog/Face Tracking Profile")]
    public sealed class VrmTrackingProfile : ScriptableObject
    {
        public TrackingExpression[] expressions = Array.Empty<TrackingExpression>();
    }

}
