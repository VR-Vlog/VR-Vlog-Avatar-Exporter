using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Colors more than one IMGUI window draws with, for the dark and the light skin.
    // The two reds differ on purpose: each is the one its windows have always shown.
    // The accent of アプリに転送 is --accent in AppTransferWindow.uss.
    internal static class ExporterColors
    {
        // The expressions board's; the rename popup and the quick transfer preview share its red.
        internal static Color Accent => Skin(Hex(0x7fa6c9), Hex(0x3a72b0));
        internal static Color Error => Skin(Hex(0xe0675f), Hex(0xb3261e));
        // The red of the transfer window's own warnings (AppTransferWindow.uss).
        internal static Color TransferError => Skin(new Color32(245, 107, 102, 255), new Color32(184, 36, 31, 255));
        // Secondary text and thin rules of the export result and pose limit windows.
        internal static Color Muted => Skin(new Color(0.62f, 0.62f, 0.62f), new Color(0.38f, 0.38f, 0.38f));
        internal static Color Hairline => Skin(new Color(0f, 0f, 0f, 0.35f), new Color(0f, 0f, 0f, 0.15f));

        static Color Skin(Color dark, Color light) => EditorGUIUtility.isProSkin ? dark : light;
        static Color Hex(int value) => new Color32((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
    }
}
