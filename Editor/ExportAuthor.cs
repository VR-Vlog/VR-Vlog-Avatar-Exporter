using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // VRM 1.0 requires at least one author. The export windows keep the save
    // button disabled until this holds, and remember the last author per
    // avatar only in this PC's Editor preferences. Nothing is written to the
    // avatar, the project or the VRM besides the author the user exports.
    internal static class ExportAuthor
    {
        internal const string Required = "VRMには作者名が必要です";
        private const string PreferencePrefix = "VRVlog.Exporter.Author.";

        internal static bool IsValid(string author) => !string.IsNullOrWhiteSpace(author);

        // The avatar's GlobalObjectId: its scene (or prefab) asset GUID and its
        // local file ID, stable across sessions and renames. Objects that are
        // not saved in an asset yet have no stable identity and are not remembered.
        internal static string Identity(GameObject avatar)
        {
            if (avatar == null) return null;
            var id = GlobalObjectId.GetGlobalObjectIdSlow(avatar);
            if (id.identifierType == 0 || id.assetGUID.Empty() || id.targetObjectId == 0) return null;
            return id.ToString();
        }

        internal static string Remembered(string identity) =>
            string.IsNullOrEmpty(identity) ? "" : EditorPrefs.GetString(PreferencePrefix + identity, "");

        internal static void Remember(string identity, string author)
        {
            if (string.IsNullOrEmpty(identity) || !IsValid(author)) return;
            EditorPrefs.SetString(PreferencePrefix + identity, author.Trim());
        }

        internal static void Forget(string identity)
        {
            if (!string.IsNullOrEmpty(identity)) EditorPrefs.DeleteKey(PreferencePrefix + identity);
        }

        internal static string Remembered(GameObject avatar) => Remembered(Identity(avatar));
        internal static void Remember(GameObject avatar, string author) => Remember(Identity(avatar), author);

        // The author field after the avatar changes: the remembered author of
        // the new avatar, else what the field already holds (as before).
        internal static string ForSelection(string rememberedForNext, string current) =>
            IsValid(rememberedForNext) ? rememberedForNext : current ?? "";

        // A focused IMGUI text field keeps its own buffer and would write the
        // old text back on the next key, so release the focus when the shown
        // author is replaced during OnGUI.
        internal static string Replace(string current, string next)
        {
            if (next != current && Event.current != null) GUI.FocusControl(null);
            return next;
        }
    }
}
