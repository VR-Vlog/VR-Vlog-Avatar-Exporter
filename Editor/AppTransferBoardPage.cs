using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRVlog.LilToonExporter
{
    // The expression board's last page comes from the optional QR module, which references
    // this assembly and registers the page here when it loads. Packages without the module
    // leave it unregistered, and the board then says to install the QR package.
    public static class AppTransferBoardPage
    {
        internal static Func<bool> available;
        internal static Func<GameObject, Action, Func<bool>, VisualElement> create;
        internal static Func<VisualElement, bool> transferring;
        internal static Action<VisualElement, Action> setChangeAvatar;

        // The page for the board's avatar, given the board's "戻る" and whether "VRMを保存" is the
        // blue button. Null without the module, or while it is turned off for this package.
        public static Func<GameObject, Action, Func<bool>, VisualElement> Create => On ? create : null;

        // Whether that page is uploading or showing its QR, asked before the board leaves its page.
        public static Func<VisualElement, bool> Transferring => On ? transferring : null;

        // Gives that page the board's avatar change for "別のアバターを選ぶ" after a transfer.
        public static Action<VisualElement, Action> SetChangeAvatar => On ? setChangeAvatar : null;

        private static bool On => available != null && available();

        public static void Register(Func<bool> available, Func<GameObject, Action, Func<bool>, VisualElement> create,
            Func<VisualElement, bool> transferring, Action<VisualElement, Action> setChangeAvatar)
        {
            AppTransferBoardPage.available = available ?? throw new ArgumentNullException(nameof(available));
            AppTransferBoardPage.create = create ?? throw new ArgumentNullException(nameof(create));
            AppTransferBoardPage.transferring = transferring ?? throw new ArgumentNullException(nameof(transferring));
            AppTransferBoardPage.setChangeAvatar = setChangeAvatar ?? throw new ArgumentNullException(nameof(setChangeAvatar));
        }
    }
}
