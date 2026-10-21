using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class ExpressionSlotLayoutTests
    {
        static readonly int[] Sizes = ExperimentalExpressionCaptureSession.PresetGroups.Select(group => group.Presets.Length).ToArray();

        // The narrowest slot area: the window's minimum width less its margins and the page scrollbar.
        static readonly float NarrowWidth = 600 - 32 - ExpressionSlotLayout.ScrollbarWidth(EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).verticalScrollbar);

        static int[] RowsOf(ExpressionSlotLayout.Result layout) => layout.Groups.Select(group => group.Row).ToArray();

        [Test]
        public void SmallThumbnailsPutEmotionsAndBlinkOnOneRowWhenWide()
        {
            Assert.That(Sizes, Is.EqualTo(new[] { 5, 3, 5, 4, 1 }));
            Assert.That(ExperimentalExpressionCaptureWindow.MainSlotCount, Is.EqualTo(13),
                "The transfer summary divides by the same slots the board header counts.");
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

        // A negative width stands for the narrowest window, which the skin decides.
        [TestCase(false, 1000f)]
        [TestCase(false, 760f)]
        [TestCase(false, -1f)]
        [TestCase(true, 1000f)]
        [TestCase(true, 760f)]
        public void AGroupNeverWrapsWhenItFitsTheWidth(bool large, float width)
        {
            if (width < 0) width = NarrowWidth;
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

        // The real window, drawn with larger fonts, keeps every slot and the
        // expression panel where they were relative to the first slot.
        [UnityTest]
        public IEnumerator TheFontSizeDoesNotMoveASlotInTheWindow()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            // EditorStyles exists only once some editor GUI has been drawn, so
            // the sizes are read after the window's first frames.
            int? label = null, mini = null;
            try
            {
                // The fold and thumbnail size come from EditorPrefs; fix them so
                // the result does not depend on how this PC last left the board.
                type.GetField("showExtraPresets", flags).SetValue(window, false); type.GetField("largeThumbnails", flags).SetValue(window, false);
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                window.position = new Rect(40, 40, 700, 1100); window.Show();
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var before = DrawnLayout(window, type, flags);
                Assert.That(before.Length, Is.EqualTo(ExperimentalExpressionCaptureWindow.ShownPresets(false).Length + 1), "Every main slot and the panel are drawn.");
                var lineHeight = EditorStyles.label.CalcHeight(new GUIContent("Face"), 200);

                label = EditorStyles.label.fontSize; mini = EditorStyles.miniLabel.fontSize;
                EditorStyles.label.fontSize = 24; EditorStyles.miniLabel.fontSize = 20;
                type.GetMethod("ReleaseStyles", flags).Invoke(window, null);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                Assert.That(EditorStyles.label.CalcHeight(new GUIContent("Face"), 200), Is.GreaterThan(lineHeight), "The larger font reaches the labels.");
                Assert.That(DrawnLayout(window, type, flags), Is.EqualTo(before));
            }
            finally
            {
                if (label.HasValue) EditorStyles.label.fontSize = label.Value;
                if (mini.HasValue) EditorStyles.miniLabel.fontSize = mini.Value;
                Object.DestroyImmediate(window);
            }
        }

        // The drawn slots in board order and the expression panel, measured from the first slot.
        static Rect[] DrawnLayout(ExperimentalExpressionCaptureWindow window, System.Type type, System.Reflection.BindingFlags flags)
        {
            var slots = (System.Collections.Generic.Dictionary<ExpressionBoard.Place, Rect>)type.GetField("slotRects", flags).GetValue(window);
            var panel = (Rect)type.GetField("expressionDropRect", flags).GetValue(window);
            var ordered = slots.Values.OrderBy(rect => rect.y).ThenBy(rect => rect.x).Append(panel).ToArray();
            var origin = ordered[0].position;
            return ordered.Select(rect => new Rect(rect.position - origin, rect.size)).ToArray();
        }

        [Test]
        public void TheScrollbarWidthComesFromTheSkin()
        {
            var style = new GUIStyle { fixedWidth = 11, margin = new RectOffset(3, 0, 0, 0) };
            Assert.That(ExpressionSlotLayout.ScrollbarWidth(style), Is.EqualTo(14));
            style.fixedWidth = 15;
            Assert.That(ExpressionSlotLayout.ScrollbarWidth(style), Is.EqualTo(18));
            Assert.That(ExpressionSlotLayout.ScrollbarWidth(null), Is.EqualTo(0));
        }

        static readonly Rect View = new Rect(0, 100, 600, 400);

        [Test]
        public void PanelHeightStaysInsideItsRangeAndThePageView()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.ShownPanelHeight(450, 0), Is.EqualTo(450), "Before the first repaint the saved height is used.");
            Assert.That(ExperimentalExpressionCaptureWindow.ShownPanelHeight(20, 800), Is.EqualTo(ExperimentalExpressionCaptureWindow.MinPanelHeight));
            Assert.That(ExperimentalExpressionCaptureWindow.ShownPanelHeight(5000, 0), Is.EqualTo(ExperimentalExpressionCaptureWindow.MaxPanelHeight));
            Assert.That(ExperimentalExpressionCaptureWindow.ShownPanelHeight(600, 400), Is.LessThan(400), "The bottom edge stays inside a small window.");
            Assert.That(ExperimentalExpressionCaptureWindow.ShownPanelHeight(600, 100), Is.EqualTo(ExperimentalExpressionCaptureWindow.MinPanelHeight), "A tiny window keeps one row.");
        }

        [Test]
        public void DragScrollMovesOnlyNearTheEdges()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 110), null, 200, 1000), Is.EqualTo(-ExperimentalExpressionCaptureWindow.DragScrollStep));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 490), null, 200, 1000), Is.EqualTo(ExperimentalExpressionCaptureWindow.DragScrollStep));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 300), null, 200, 1000), Is.EqualTo(0));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 90), null, 200, 1000), Is.EqualTo(0), "Outside the view nothing scrolls.");
        }

        [Test]
        public void DragScrollStopsAtTheEndsOfThePage()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 110), null, 0, 1000), Is.EqualTo(0), "The top of the page does not scroll further.");
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 110), null, 3, 1000), Is.EqualTo(-3));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 490), null, 1000, 1000), Is.EqualTo(0));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(300, 490), null, 0, 0), Is.EqualTo(0), "A page that fits does not scroll.");
        }

        // A whole slot under the pointer at an edge stays put, so the slot does
        // not run away from the pointer; a clipped one scrolls into view.
        [Test]
        public void DragScrollKeepsAWholeSlotUnderThePointer()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(40, 110), new Rect(20, 104, 78, 98), 200, 1000), Is.EqualTo(0));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(40, 110), new Rect(20, 60, 78, 98), 200, 1000), Is.EqualTo(-ExperimentalExpressionCaptureWindow.DragScrollStep));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(40, 490), new Rect(20, 400, 78, 98), 200, 1000), Is.EqualTo(0));
            Assert.That(ExperimentalExpressionCaptureWindow.DragScroll(View, new Vector2(40, 490), new Rect(20, 440, 78, 98), 200, 1000), Is.EqualTo(ExperimentalExpressionCaptureWindow.DragScrollStep));
        }

        // The slots scroll with the page, so the area is as tall as its slots;
        // the old area stopped at 438 and scrolled inside.
        [Test]
        public void TheSlotAreaIsAsTallAsItsSlots()
        {
            var layout = ExpressionSlotLayout.Compute(NarrowWidth, true, true, Sizes);
            Assert.That(layout.Height, Is.GreaterThan(438));
            Assert.That(layout.Groups.SelectMany(group => group.Slots).Max(slot => slot.yMax), Is.LessThanOrEqualTo(layout.Height));
            Assert.That(layout.Fold.yMax, Is.LessThanOrEqualTo(layout.Height));
        }

        [Test]
        public void TheConfirmButtonFollowsTheCaption()
        {
            ExpressionSlotLayout.CaptionAndButton(10, 400, 120, 100, out var caption, out var button);
            Assert.That(caption, Is.EqualTo(120));
            Assert.That(button, Is.EqualTo(10 + 120 + 8));
            // A narrow header shortens the caption and keeps the button whole.
            ExpressionSlotLayout.CaptionAndButton(10, 200, 300, 100, out caption, out button);
            Assert.That(caption, Is.EqualTo(200 - 10 - 100 - 8));
            Assert.That(button + 100, Is.EqualTo(200));
            ExpressionSlotLayout.CaptionAndButton(10, 200, 300, 0, out caption, out _);
            Assert.That(caption, Is.EqualTo(190));
        }

        [Test]
        public void TheOtherTitleCountsOnlyExportedFaces()
        {
            Assert.That(ExperimentalExpressionCaptureWindow.OtherTitle(3), Is.EqualTo(string.Format(ExporterLocalization.T("その他の表情 {0}件"), 3)));
        }

        [Test]
        public void ARejectedFileIsNamedWithItsReason()
        {
            var message = ExperimentalExpressionCaptureWindow.RejectedMessage(new[] { ("Tail", "reason one"), ("Toggle", "reason two") });
            Assert.That(message.Split('\n'), Is.EqualTo(new[]
            {
                string.Format(ExporterLocalization.T("「{0}」は追加できません。{1}"), "Tail", "reason one"),
                string.Format(ExporterLocalization.T("「{0}」は追加できません。{1}"), "Toggle", "reason two"),
            }));
        }

        [TestCase("瞬きなし（閉眼用の変形が見つかりません）", "瞬きなし")]
        [TestCase("No blink (no eye-closing shape found)", "No blink")]
        [TestCase("VRChatの閉眼設定", "VRChatの閉眼設定")]
        [TestCase("閉眼用の変形が見つかりません: Body", "閉眼用の変形が見つかりません")]
        public void ABlinkSlotShowsTheHeadOfItsDescription(string text, string head)
        {
            Assert.That(ExperimentalExpressionCaptureWindow.StateHead(text), Is.EqualTo(head));
        }
    }
}
