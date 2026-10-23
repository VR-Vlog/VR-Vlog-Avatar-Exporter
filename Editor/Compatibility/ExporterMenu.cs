using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Compatibility
{
    // The "VR Vlog" menu. The menu bar and the Hierarchy context menu (GameObject/) list
    // the same two entries in the same order: the quick and the detailed app transfer.
    // They come from the optional transfer module, which takes its priorities from here.
    // Every entry acts on one avatar root, the one right-clicked or, in the menu bar, the
    // one selected in the Hierarchy; without it the entry is grey.
    public static class ExporterMenu
    {
        public const string MenuBar = "VR Vlog/", Hierarchy = "GameObject/VR Vlog/";
        // The Hierarchy context menu sorts its GameObject entries by priority and places a
        // submenu at its lowest one: 10000 puts VR Vlog after the other avatar tools. The
        // menu bar starts at 1000.
        public const int MenuBarPriority = 1000, HierarchyPriority = 10000;
        // Added to either base. The two are 1 apart, so Unity draws no separator between them.
        public const int QuickTransferOrder = 0, DetailedTransferOrder = 1;

        // The top of an avatar: it has an Animator and nothing above it has one.
        public static bool IsAvatarRoot(GameObject target)
        {
            if (target == null || target.GetComponent<Animator>() == null) return false;
            for (var parent = target.transform.parent; parent != null; parent = parent.parent)
                if (parent.GetComponent<Animator>() != null) return false;
            return true;
        }

        // Right-clicking selects the object first, so both menus read the selection.
        public static bool OneAvatarRootSelected() => Selection.gameObjects.Length == 1 && IsAvatarRoot(Selection.gameObjects[0]);
    }
}
