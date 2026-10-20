using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // Where the preset slots of the expression board go. Everything follows
    // from the width, the thumbnail size and the fold state only, never from
    // the font, so a larger font cannot move a slot.
    internal static class ExpressionSlotLayout
    {
        internal const float Gap = 6, GroupGap = 18, Caption = 16, Label = 16, Name = 16, State = 14, Fold = 20;

        // The width a vertical scrollbar takes from a scroll view, as
        // GUI.BeginScrollView takes it: the bar and its left margin.
        internal static float ScrollbarWidth(GUIStyle verticalScrollbar) =>
            verticalScrollbar == null ? 0 : verticalScrollbar.fixedWidth + verticalScrollbar.margin.left;

        // Groups 0-2 (emotions, blink, mouth) are always shown; 3-4 (gaze,
        // neutral) sit under the fold row.
        internal const int MainGroups = 3;

        internal static float Thumb(bool large) => large ? 96 : 64;
        internal static float SlotWidth(bool large) => Thumb(large) + 14;
        // The tile holds the thumbnail and the name under it.
        internal static float Tile(bool large) => Thumb(large) + 6 + Name;
        internal static float SlotHeight(bool large) => Label + 2 + Tile(large) + 1 + State + 2;

        internal sealed class Group
        {
            internal int Index, Row;
            // The caption reaches to the next group, or to the end of the row.
            internal Rect Caption;
            internal Rect[] Slots;
        }

        internal sealed class Result
        {
            internal Group[] Groups;
            internal Rect Fold;
            internal float Height;
            internal int Rows;
        }

        // Rows the groups prefer: small puts emotions and blink side by side,
        // large gives each group a row. A row that is too narrow moves its
        // next group down; a group wider than the whole width wraps inside.
        static int[][] PreferredRows(bool large, int groups) => large
            ? BuildRows(groups, index => new[] { index })
            : new[] { new[] { 0, 1 }, new[] { 2 }, new[] { 3, 4 } };

        static int[][] BuildRows(int groups, Func<int, int[]> row)
        {
            var rows = new int[groups][];
            for (var index = 0; index < groups; index++) rows[index] = row(index);
            return rows;
        }

        // The header caption is followed by the confirm button, 8 apart; the
        // caption gives way first. button: the button width, 0 when there is none.
        internal static void CaptionAndButton(float left, float right, float text, float button, out float caption, out float buttonX)
        {
            var room = Mathf.Max(0, right - left - (button > 0 ? button + 8 : 0));
            caption = Mathf.Min(text, room);
            buttonX = left + caption + 8;
        }

        // width: the inside of the slot area.
        // sizes: the number of slots of each group, in PresetGroups order.
        internal static Result Compute(float width, bool large, bool extraOpen, IReadOnlyList<int> sizes)
        {
            if (sizes == null) throw new ArgumentNullException(nameof(sizes));
            width = Mathf.Max(width, SlotWidth(large));
            var slotWidth = SlotWidth(large); var slotHeight = SlotHeight(large);
            var perRow = Mathf.Max(1, Mathf.FloorToInt((width + Gap) / (slotWidth + Gap)));
            var groups = new List<Group>();
            var y = 0f; var rows = 0;
            var fold = Rect.zero;

            void Place(int[] preferred)
            {
                var x = 0f; var rowHeight = 0f; var first = true;
                var placed = new List<(Group Group, float Width)>();
                void Close()
                {
                    // Captions reach to the next group or the row end.
                    for (var index = 0; index < placed.Count; index++)
                    {
                        var caption = placed[index].Group.Caption;
                        var end = index + 1 < placed.Count ? placed[index + 1].Group.Caption.x - GroupGap : width;
                        placed[index].Group.Caption = new Rect(caption.x, caption.y, Mathf.Max(placed[index].Width, end - caption.x), Caption);
                    }
                    placed.Clear();
                    y += rowHeight; rows++;
                    x = 0; rowHeight = 0; first = true;
                }
                foreach (var index in preferred)
                {
                    if (index >= sizes.Count) continue;
                    var count = sizes[index];
                    var columns = Mathf.Min(count, perRow);
                    var lines = Mathf.Max(1, (count + perRow - 1) / perRow);
                    var groupWidth = columns * (slotWidth + Gap) - Gap;
                    if (!first && x + GroupGap + groupWidth > width) Close();
                    if (!first) x += GroupGap;
                    var group = new Group { Index = index, Row = rows, Caption = new Rect(x, y, groupWidth, Caption), Slots = new Rect[count] };
                    for (var slot = 0; slot < count; slot++)
                        group.Slots[slot] = new Rect(x + slot % perRow * (slotWidth + Gap), y + Caption + slot / perRow * (slotHeight + Gap), slotWidth, slotHeight);
                    groups.Add(group); placed.Add((group, groupWidth));
                    rowHeight = Mathf.Max(rowHeight, Caption + lines * slotHeight + (lines - 1) * Gap + 2);
                    x += groupWidth; first = false;
                }
                if (!first) Close();
            }

            foreach (var preferred in PreferredRows(large, sizes.Count))
            {
                var main = Array.FindAll(preferred, index => index < MainGroups);
                var extra = Array.FindAll(preferred, index => index >= MainGroups);
                if (main.Length > 0) Place(main);
                if (extra.Length == 0) continue;
                if (fold == Rect.zero) { fold = new Rect(0, y, width, Fold); y += Fold; }
                if (extraOpen) Place(extra);
            }
            if (fold == Rect.zero) { fold = new Rect(0, y, width, Fold); y += Fold; }
            return new Result { Groups = groups.ToArray(), Fold = fold, Height = y, Rows = rows };
        }
    }
}
