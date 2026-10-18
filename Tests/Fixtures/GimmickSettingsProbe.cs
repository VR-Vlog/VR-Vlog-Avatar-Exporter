using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    // Stands in for a build-tool settings component that a gimmick rule anchors
    // on. Linked objects belong to the gimmick; avatar parts do not.
    public sealed class GimmickSettingsProbe : MonoBehaviour
    {
        public GameObject Linked;
        public GameObject[] LinkedList = new GameObject[0];
        public GameObject AvatarPart;
    }
}
