using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Compatibility
{
    // The "VR Vlog" menu. The menu bar and the Hierarchy context menu (GameObject/) list
    // the same entries in the same order: the two app transfers, a separator, then the
    // export. The transfers come from the optional transfer module, which takes its
    // priorities from here. Every entry acts on one avatar root, the one right-clicked or,
    // in the menu bar, the one selected in the Hierarchy; without it the entry is grey.
    public static class ExporterMenu
    {
        public const string MenuBar = "VR Vlog/", Hierarchy = "GameObject/VR Vlog/";
        // The trailing "..." marks an entry that opens a window instead of acting at once.
        public const string Export = "VRMを書き出す...";
        // The Hierarchy context menu sorts its GameObject entries by priority and places a
        // submenu at its lowest one: 10000 puts VR Vlog after the other avatar tools. The
        // menu bar starts at 1000, the priority its export entry always had.
        public const int MenuBarPriority = 1000, HierarchyPriority = 10000;
        // Added to either base. Unity draws a separator where the priority grows by more
        // than 10, so the export is 11 after the detailed transfer.
        public const int QuickTransferOrder = 0, DetailedTransferOrder = 1, ExportOrder = 12;

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
