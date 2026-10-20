using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionSlotLayoutTests
    {
        static readonly int[] Sizes = ExperimentalExpressionCaptureSession.PresetGroups.Select(group => group.Presets.Length).ToArray();

        // The narrowest slot area: the window's minimum width less its margins and scrollbar.
        const float NarrowWidth = 600 - 32 - ExpressionSlotLayout.ScrollbarWidth - 2;

        static int[] RowsOf(ExpressionSlotLayout.Result layout) => layout.Groups.Select(group => group.Row).ToArray();

        [Test]
        public void SmallThumbnailsPutEmotionsAndBlinkOnOneRowWhenWide()
        {
            Assert.That(Sizes, Is.EqualTo(new[] { 5, 3, 5, 4, 1 }));
            var layout = ExpressionSlotLayout.Compute(1000, false, false, Sizes);
            Assert.That(layout.Rows, Is.EqualTo(2));
            Assert.That(layout.Groups.Select(group => group.Index), Is.EqualTo(new[] { 0, 1, 2 }), "The fold hides gaze and the neutral face.");
            Assert.That(RowsOf(layout), Is.EqualTo(new[] { 0, 0, 1 }));
            Assert.That(layout.Height, Is.EqualTo(298));
            Assert.That(layout.Fold.y, Is.EqualTo(278));
            Assert.That(layout.Groups[1].Slots[0].x, Is.EqualTo(5 * 84 - 6 + ExpressionSlotLayout.GroupGap), "Blink starts after the emotions and the group gap.");

            var open = ExpressionSlotLayout.Compute(1000, false, true, Sizes);
            Assert.That(open.Groups.Select(group => group.Index), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(RowsOf(open), Is.EqualTo(new[] { 0, 0, 1, 2, 2 }));
            Assert.That(open.Groups[3].Caption.y, Is.EqualTo(open.Fold.yMax), "The extra groups open under the fold row.");
        }

        [Test]
        public void LargeThumbnailsGiveEachMainGroupARow()
        {
            var layout = ExpressionSlotLayout.Compute(1000, true, false, Sizes);
            Assert.That(layout.Rows, Is.EqualTo(3));
            Assert.That(RowsOf(layout), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(layout.Groups.All(group => group.Slots.All(slot => Mathf.Approximately(slot.y, group.Slots[0].y))), Is.True);
            var open = ExpressionSlotLayout.Compute(1000, true, true, Sizes);
            Assert.That(open.Rows, Is.EqualTo(5));
        }

        [TestCase(false, 1000f)]
        [TestCase(false, 760f)]
        [TestCase(false, NarrowWidth)]
        [TestCase(true, 1000f)]
        [TestCase(true, 760f)]
        public void AGroupNeverWrapsWhenItFitsTheWidth(bool large, float width)
        {
            var layout = ExpressionSlotLayout.Compute(width, large, true, Sizes);
            foreach (var group in layout.Groups)
            {
                Assert.That(group.Slots.Select(slot => slot.y).Distinct().Count(), Is.EqualTo(1), $"Group {group.Index} stays on one line.");
                Assert.That(group.Slots.Last().xMax, Is.LessThanOrEqualTo(width + 0.01f));
            }
            // Groups share a row only side by side, never overlapping.
            foreach (var row in layout.Groups.GroupBy(group => group.Row))
            {
                var ordered = row.OrderBy(group => group.Caption.x).ToArray();
                for (var index = 1; index < ordered.Length; index++)
                    Assert.That(ordered[index].Slots[0].x, Is.GreaterThanOrEqualTo(ordered[index - 1].Slots.Last().xMax + ExpressionSlotLayout.GroupGap - 0.01f));
            }
        }

        [Test]
        public void TheNarrowestWindowWrapsLargeEmotionsFourAndOne()
        {
            var layout = ExpressionSlotLayout.Compute(NarrowWidth, true, false, Sizes);
            var emotions = layout.Groups[0];
            Assert.That(emotions.Slots.Take(4).Select(slot => slot.y).Distinct().Count(), Is.EqualTo(1));
            Assert.That(emotions.Slots[4].y, Is.GreaterThan(emotions.Slots[0].y));
            Assert.That(emotions.Slots[4].x, Is.EqualTo(emotions.Slots[0].x));
            Assert.That(layout.Groups[1].Caption.y, Is.GreaterThanOrEqualTo(emotions.Slots[4].yMax), "Blink starts below the wrapped emotions.");

            var small = ExpressionSlotLayout.Compute(NarrowWidth, false, false, Sizes);
            Assert.That(RowsOf(small), Is.EqualTo(new[] { 0, 1, 2 }), "A row too narrow for emotions and blink moves blink down.");
            Assert.That(small.Groups.All(group => group.Slots.Select(slot => slot.y).Distinct().Count() == 1), Is.True);
        }

        [Test]
        public void TheFontSizeDoesNotMoveASlot()
        {
            var before = new[] { false, true }.Select(large => ExpressionSlotLayout.Compute(760, large, true, Sizes)).ToArray();
            var label = EditorStyles.label.fontSize; var mini = EditorStyles.miniLabel.fontSize;
            try
            {
                EditorStyles.label.fontSize = 24; EditorStyles.miniLabel.fontSize = 20;
                var after = new[] { false, true }.Select(large => ExpressionSlotLayout.Compute(760, large, true, Sizes)).ToArray();
                for (var index = 0; index < before.Length; index++)
                {
                    Assert.That(after[index].Height, Is.EqualTo(before[index].Height));
                    Assert.That(after[index].Fold, Is.EqualTo(before[index].Fold));
                    Assert.That(after[index].Groups.SelectMany(group => group.Slots.Prepend(group.Caption)),
                        Is.EqualTo(before[index].Groups.SelectMany(group => group.Slots.Prepend(group.Caption))));
                }
            }
            finally { EditorStyles.label.fontSize = label; EditorStyles.miniLabel.fontSize = mini; }
        }
    }
}
