using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // The board's page after a transfer ends or fails. The QR area is gone: the result stays at the top,
    // the notes of the export scroll below it, folded by kind, and the buttons stay at the bottom.
    // The window keeps its QR screen; only the board's panel (embedded) shows this.
    public sealed partial class AppTransferPanel
    {
        // A long kind shows this many targets and the rest on request.
        internal const int FirstNoteItems = 8;

        [NonSerialized] private TransferNotes exportNotes = TransferNotes.Build(null);
        // The notes of the file "VRMを保存" wrote, sent as they are when the transfer reuses that file.
        [NonSerialized] private TransferNotes savedCopyNotes = TransferNotes.Build(null);
        [NonSerialized] private Action changeAvatar;
        [NonSerialized] private VisualElement doneView;
        [NonSerialized] private Button changeAvatarButton;
        [NonSerialized] private string doneBuiltFor;
        [NonSerialized] private TransferNotes doneBuiltNotes;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, AppTransferPanel> Embedded =
            new System.Runtime.CompilerServices.ConditionalWeakTable<VisualElement, AppTransferPanel>();

        // Kinds the user opened, kept while the editor runs.
        private static readonly Dictionary<string, bool> OpenKinds = new Dictionary<string, bool>();

        // The board's "別のアバターを選ぶ": its own avatar change. Resolved by name, as CreateEmbedded is.
        // The board calls it before the panel is attached, so the panel is found from the element.
        public static void SetEmbeddedChangeAvatar(VisualElement element, Action change)
        {
            if (!Embedded.TryGetValue(element, out var panel)) return;
            panel.changeAvatar = change;
            if (Live.Contains(panel)) panel.Refresh();
        }

        // Whether the page after the transfer replaces the QR screen.
        internal static bool ShowsDone(bool embedded, AppTransferView view) =>
            embedded && (view == AppTransferView.Completed || view == AppTransferView.Failed);

        // Called at the end of Refresh, after RefreshTransfer and RefreshActions.
        private void RefreshDone(AppTransferView view, bool sending)
        {
            var done = sending && ShowsDone(embedded, view);
            var failed = view == AppTransferView.Failed;
            if (doneView == null)
            {
                if (!done) return;
                BuildDoneParts();
            }
            Show(doneView, done);
            transferScroll.style.display = done ? DisplayStyle.None : DisplayStyle.Flex;
            Show(changeAvatarButton, done && changeAvatar != null);
            regenerateButton.EnableInClassList("primary", done);
            // At the narrowest width a longer translation is cut short; the tooltip still has all of it.
            backButton.tooltip = done ? T(AppTransferText.BackToBoard) : null;
            if (!done) return;

            // The language is in the key: the page is built again when it changes.
            var key = view + "|" + missingShaders + "|" + T(AppTransferText.Completed) + "|" + (failed ? flow.FailureText(T) : "");
            if (doneBuiltFor != key || doneBuiltNotes != exportNotes) { BuildDone(failed); doneBuiltFor = key; doneBuiltNotes = exportNotes; }
            changeAvatarButton.text = T(AppTransferText.ChooseAnotherAvatar);
            changeAvatarButton.tooltip = changeAvatarButton.text;
            backButton.text = T(AppTransferText.BackToBoard);
            // The next transfer in blue, where "QRを作り直す" was, so "VRMを保存" does not move.
            regenerateButton.text = T(failed ? AppTransferText.Retry : AppTransferText.TransferAgain);
            Reserve(regenerateButton, true);
            saveButton.EnableInClassList("primary", false);
        }

        private void BuildDoneParts()
        {
            doneView = new VisualElement { name = "done" };
            doneView.AddToClassList("fill");
            transfer.Insert(0, doneView);
            changeAvatarButton = new Button(() => changeAvatar?.Invoke()) { name = "change-avatar" };
            changeAvatarButton.AddToClassList("action");
            changeAvatarButton.style.width = 136;
            changeAvatarButton.style.marginRight = 8;
            backButton.parent.Insert(backButton.parent.IndexOf(backButton) + 1, changeAvatarButton);
        }

        private void BuildDone(bool failed)
        {
            doneView.Clear();
            var head = ResultHead(failed);
            head.style.paddingLeft = 16; head.style.paddingRight = 16; head.style.paddingTop = 4; head.style.paddingBottom = 12;
            doneView.Add(head);
            doneView.Add(Hairline());
            var scroll = new ScrollView { name = "done-scroll" };
            scroll.AddToClassList("fill");
            var body = new VisualElement();
            body.style.paddingLeft = 16; body.style.paddingRight = 16; body.style.paddingTop = 8; body.style.paddingBottom = 12;
            if (!failed)
            {
                // The app shows the avatar without the materials whose shader is missing.
                if (missingShaders > 0)
                {
                    var box = new HelpBox(string.Format(T(MissingShadersText), missingShaders), HelpBoxMessageType.Warning);
                    box.AddToClassList("box");
                    box.style.marginBottom = 8;
                    body.Add(box);
                }
                AddNotes(body);
            }
            scroll.Add(body);
            doneView.Add(scroll);
            doneView.Add(Hairline());
        }

        // The result: an icon and one line, with the note count or the reason of a failure under it.
        private VisualElement ResultHead(bool failed)
        {
            var head = new VisualElement();
            head.style.flexShrink = 0;
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            line.style.alignItems = Align.Center;
            line.style.height = 28;
            var icon = new Image { image = EditorGUIUtility.IconContent(failed ? "console.erroricon" : "TestPassed").image, scaleMode = ScaleMode.ScaleToFit };
            icon.style.width = 20; icon.style.height = 20; icon.style.marginRight = 8; icon.style.flexShrink = 0;
            line.Add(icon);
            var title = new Label(T(failed ? (flow.ExportDidFail ? AppTransferText.ExportFailed : AppTransferText.TransferFailed) : AppTransferText.Completed));
            title.AddToClassList("t");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            line.Add(title);
            head.Add(line);
            var sub = new Label(failed ? flow.FailureText(T)
                : exportNotes.NoteCount > 0 ? string.Format(T(AppTransferText.NoteCount), exportNotes.NoteCount) : T(AppTransferText.NoNotes));
            sub.AddToClassList("t");
            sub.AddToClassList("grey");
            sub.style.marginLeft = 28;
            sub.style.whiteSpace = WhiteSpace.Normal;
            Show(sub, sub.text.Length > 0);
            head.Add(sub);
            return head;
        }

        private static VisualElement Hairline()
        {
            var line = new VisualElement();
            line.AddToClassList("hairline");
            return line;
        }

        // The notes, one folded row per kind with its count; opened, the shared reason once and then
        // each target on a line of its own. Then the records, folded the same way.
        private void AddNotes(VisualElement host)
        {
            // With no notes the result line above already says so.
            if (exportNotes.Notes.Count > 0) host.Add(SectionLabel(AppTransferText.Notes));
            foreach (var group in exportNotes.Notes) host.Add(GroupFold(group));
            if (exportNotes.Records.Count == 0) return;
            var label = SectionLabel(AppTransferText.OtherNotes);
            if (exportNotes.Notes.Count > 0) label.style.marginTop = 12;
            host.Add(label);
            foreach (var group in exportNotes.Records)
            {
                if (group.Kind == null) { foreach (var item in group.Items) host.Add(Line(item.Target)); continue; }
                host.Add(GroupFold(group));
            }
        }

        private static Label SectionLabel(string text)
        {
            var label = new Label(T(text));
            label.AddToClassList("t");
            label.AddToClassList("bold");
            label.style.height = 22;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            return label;
        }

        private static Label Line(string text)
        {
            var label = new Label(text) { tooltip = text };
            label.AddToClassList("t");
            label.style.height = 18;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.whiteSpace = WhiteSpace.NoWrap; label.style.overflow = Overflow.Hidden; label.style.textOverflow = TextOverflow.Ellipsis;
            return label;
        }

        private static Foldout GroupFold(TransferNoteGroup group)
        {
            var fold = new Foldout { text = T(group.Kind), value = OpenKinds.TryGetValue(group.Kind, out var open) && open };
            fold.RegisterValueChangedCallback(changed => { if (changed.target == fold) OpenKinds[group.Kind] = changed.newValue; });
            var toggle = fold.Q<Toggle>();
            toggle.style.height = 20;
            toggle.style.marginLeft = 0;
            var count = new Label(string.Format(T(AppTransferText.ItemCount), group.Count));
            count.AddToClassList("t");
            count.AddToClassList("grey");
            count.style.marginLeft = 8;
            count.style.unityTextAlign = TextAnchor.MiddleLeft;
            toggle.Q(className: Toggle.inputUssClassName)?.Add(count);
            fold.contentContainer.style.marginLeft = 16;
            if (!string.IsNullOrEmpty(group.CommonReason))
            {
                var reason = new Label(group.CommonReason);
                reason.AddToClassList("t");
                reason.AddToClassList("grey");
                reason.style.whiteSpace = WhiteSpace.Normal;
                reason.style.marginBottom = 4;
                fold.Add(reason);
            }
            // A long kind shows its first targets and the rest on request, so one open kind does not
            // push the others out of view.
            VisualElement Item((string Target, string Reason) item) => Line(item.Reason == null ? item.Target : item.Target + " — " + item.Reason);
            foreach (var item in group.Items.Take(FirstNoteItems)) fold.Add(Item(item));
            if (group.Items.Count > FirstNoteItems)
            {
                var more = new Label(string.Format(T(AppTransferText.ShowRest), group.Items.Count - FirstNoteItems));
                more.AddToClassList("t");
                more.AddToClassList("link");
                more.style.marginTop = 4;
                more.RegisterCallback<ClickEvent>(_ =>
                {
                    var at = fold.contentContainer.IndexOf(more);
                    more.RemoveFromHierarchy();
                    foreach (var item in group.Items.Skip(FirstNoteItems)) fold.contentContainer.Insert(at++, Item(item));
                });
                fold.Add(more);
            }
            return fold;
        }
    }
}
