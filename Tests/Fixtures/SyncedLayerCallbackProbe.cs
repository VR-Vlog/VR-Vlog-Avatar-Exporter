using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Records which layer Unity runs a behaviour on, to check what a synced
    // layer runs of the layer it copies.
    public sealed class SyncedLayerCallbackProbe : StateMachineBehaviour
    {
        public static readonly List<string> Calls = new List<string>();
        public string Label;

        public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex) => Calls.Add(Label + " " + layerIndex);
    }
}
