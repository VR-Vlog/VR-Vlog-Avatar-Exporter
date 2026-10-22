using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using VRVlog.LilToonExporter.Compatibility;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // "アプリに転送": opened from the Hierarchy context menu of an avatar. The window shows
    // AppTransferPanel, which takes the name and the consent, exports, uploads the encrypted VRM
    // and shows the QR of the vrvlog://import#... link. The quick transfer exports with the
    // remembered settings. The detailed one is the third page of the expression board, which
    // shows the same panel; this window opens for it only when the exporter cannot run, to say why.
    public sealed class AppTransferWindow : EditorWindow
    {
        internal const string QuickMenuPath = "GameObject/VR Vlog/アプリに転送（かんたん）";
        internal const string DetailedMenuPath = "GameObject/VR Vlog/アプリに転送（通常）";
        // The Hierarchy context menu sorts its GameObject entries by priority, whatever the value,
        // and places a submenu at its lowest one. Other avatar tools use up to 1000,
        // one 9999, so 10000 puts VR Vlog after them, the quick entry first.
        internal const int QuickMenuPriority = 10000, DetailedMenuPriority = 10001;
        // "QRができたら音を鳴らす" on the QR screen, kept for every project on this PC.
        internal const string QrReadySoundPreference = "VRVlog.AppTransfer.QrReadySound";
        internal static bool QrReadySound
        {
            get => EditorPrefs.GetBool(QrReadySoundPreference, AppTransferFlow.QrReadySoundByDefault);
            set => EditorPrefs.SetBool(QrReadySoundPreference, value);
        }

        // The avatar, the name and whether it is detailed survive a domain reload with the window.
        [SerializeField] private AppTransferPanel panel = new AppTransferPanel();

        private AppTransferPanel Panel => panel ?? (panel = new AppTransferPanel());

        [MenuItem(QuickMenuPath, false, QuickMenuPriority)]
        private static void OpenFromMenu(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            if (selected != null) Open(selected);
        }

        // Listed only in versions that transfer, and for one avatar root.
        [MenuItem(QuickMenuPath, true)]
        private static bool CanOpenFromMenu() =>
            CloudTransferAvailability.Enabled && Selection.gameObjects.Length == 1 && IsAvatarRoot(Selection.gameObjects[0]);

        // The detailed transfer starts on the expression board. When the exporter cannot
        // run, the quick window opens instead and says why.
        [MenuItem(DetailedMenuPath, false, DetailedMenuPriority)]
        private static void OpenDetailedFromMenu(MenuCommand command)
        {
            var selected = command.context as GameObject ?? Selection.activeGameObject;
            if (selected == null) return;
            var entry = DependencyDiagnostics.Error(DependencyDiagnostics.Installed()) == null ? AppTransferPanel.ExportEntry.Resolve() : null;
            if (entry != null) entry.EditDetails(selected);
            // Without the exporter the detailed window says why, under the item the user chose.
            else OpenDetailed(selected);
        }

        [MenuItem(DetailedMenuPath, true)]
        private static bool CanOpenDetailedFromMenu() => CanOpenFromMenu();

        // The top of an avatar: it has an Animator and nothing above it has one.
        internal static bool IsAvatarRoot(GameObject target)
        {
            if (target == null || target.GetComponent<Animator>() == null) return false;
            for (var parent = target.transform.parent; parent != null; parent = parent.parent)
                if (parent.GetComponent<Animator>() != null) return false;
            return true;
        }

        public static void Open(GameObject target) => Open(target, false);

        // The detailed transfer in a window of its own, for the avatar on the expression board.
        public static void OpenDetailed(GameObject target) => Open(target, true);

        private static void Open(GameObject target, bool detailedExport)
        {
            if (!MayOpen(target, detailedExport, same => same ? AppTransferPanel.ConfirmRedoingTransfer() : AppTransferPanel.ConfirmEndingTransfer())) return;
            var window = GetWindow<AppTransferWindow>(false);
            var panel = window.Panel;
            // Back to the same form, the name, consent and terms typed there stay.
            if (target != null && panel.Avatar == target && panel.Detailed == detailedExport && panel.AtForm)
            {
                window.Focus();
                return;
            }
            panel.Detailed = detailedExport;
            panel.Select(target, true);
            window.minSize = AppTransferPanel.FormMinimumSize;
            window.Show();
        }

        // Before the window shows the target: a panel uploading or showing its QR, the board's third
        // page too, is stopped when the user goes on. confirm gets whether it is the same avatar
        // ("作り直す") or another one ("変更する"). False when that transfer stays, its window in front.
        internal static bool MayOpen(GameObject target, bool detailedExport, Func<bool, bool> confirm)
        {
            foreach (var running in AppTransferPanel.Live.ToArray())
            {
                if (!running.Transferring) continue;
                // The same quick transfer keeps its QR; only the window comes forward. A switch
                // between quick and detailed leaves that QR out of date, so it asks.
                var same = running.Avatar == target;
                if (same && !detailedExport && !running.Detailed || !confirm(same)) { running.Host?.Focus(); return false; }
                running.StopTransfer();
            }
            return true;
        }

        private void OnEnable()
        {
            Panel.Window = this;
            Panel.Enable();
        }

        // Closing cancels the transfer and deletes the exported copy.
        private void OnDisable() => Panel.Disable();

        private void CreateGUI() => Panel.Build(rootVisualElement);

        // Unity 2022.3's editor text remembers, per character, which fallback font drew it, without the
        // weight: once any editor UI Toolkit panel has drawn a Japanese character in bold, that character
        // comes from the bold Japanese font in normal-weight text too, so a few kana of a line look bold.
        // The panel draws its text from its own font asset instead, made from the OS font the editor
        // uses for Japanese on Windows, so no other text shares its lookup. Korean and the Chinese
        // characters Yu Gothic lacks come from its own fallbacks, made from the OS fonts the editor
        // falls back to: through the editor's fallbacks, bold text in the panel would leak the same way.
        // Elsewhere it keeps the editor's font.
        private const string TextFontName = "VR Vlog App Transfer Text";
        private static readonly string[] TextFallbackFamilies = { "Malgun Gothic", "Microsoft JhengHei" };
        private static FontAsset textFont;
        private static bool textFontTried;

        internal static FontAsset TextFont()
        {
            // Without the font the lookup would log once per window opening; try once per domain.
            if (textFont != null || textFontTried || Application.platform != RuntimePlatform.WindowsEditor) return textFont;
            textFont = OsFont(TextFontName, "Yu Gothic UI", "Semilight");
            textFontTried = textFont == null;
            if (textFont != null && (textFont.fallbackFontAssetTable == null || textFont.fallbackFontAssetTable.Count == 0))
            {
                textFont.fallbackFontAssetTable = TextFallbackFamilies
                    .Select(family => OsFont(TextFontName + " " + family, family, "Regular"))
                    .Where(f => f != null).ToList();
            }
            return textFont;
        }

        // The asset outlives a domain reload; reuse it rather than make one more each time.
        private static FontAsset OsFont(string name, string family, string style)
        {
            var font = Resources.FindObjectsOfTypeAll<FontAsset>().FirstOrDefault(f => f.name == name);
            if (font != null) return font;
            font = FontAsset.CreateFontAsset(family, style, 90);
            if (font != null) { font.name = name; font.hideFlags = HideFlags.DontSave; }
            return font;
        }
    }
}
