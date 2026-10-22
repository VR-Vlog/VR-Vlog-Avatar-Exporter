using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using State = VRVlog.LilToonExporter.StepIndicator.State;
using Marker = VRVlog.LilToonExporter.StepIndicator.Marker;

namespace VRVlog.LilToonExporter.Tests
{
    public sealed class StepIndicatorTests
    {
        const int Expressions = ExperimentalExpressionCaptureWindow.ExpressionsPage;
        const int Objects = ExperimentalExpressionCaptureWindow.ObjectsPage;
        const int Info = ExperimentalExpressionCaptureWindow.InfoPage;
        // Half a device pixel, with room for float rounding.
        const float HalfPixel = .5f + 1e-3f;

        static State[] StatesOn(int shown) => Enumerable.Range(0, StepIndicator.Count).Select(index => StepIndicator.StateOf(index, shown)).ToArray();

        [Test]
        public void EachPageMarksTheStepsBeforeItDoneAndTheOnesAfterItUpcoming()
        {
            Assert.That(StatesOn(Expressions), Is.EqualTo(new[] { State.Current, State.Upcoming, State.Upcoming }));
            Assert.That(StatesOn(Objects), Is.EqualTo(new[] { State.Done, State.Current, State.Upcoming }));
            Assert.That(StatesOn(Info), Is.EqualTo(new[] { State.Done, State.Done, State.Current }));
        }

        [Test]
        public void BeforeTheBoardIsPreparedTheFirstStepIsCurrent()
        {
            Assert.That(StepIndicator.Shown(false, Info), Is.EqualTo(Expressions));
            Assert.That(StepIndicator.Shown(false, Objects), Is.EqualTo(Expressions));
            Assert.That(StepIndicator.Shown(true, Objects), Is.EqualTo(Objects));
            Assert.That(StepIndicator.Shown(true, Info), Is.EqualTo(Info));
        }

        [Test]
        public void TheCircleShowsACheckTheNumberOrARing()
        {
            Assert.That(StepIndicator.MarkerOf(State.Done, false), Is.EqualTo(Marker.Check));
            Assert.That(StepIndicator.MarkerOf(State.Current, false), Is.EqualTo(Marker.Number));
            Assert.That(StepIndicator.MarkerOf(State.Upcoming, false), Is.EqualTo(Marker.Ring));
            Assert.That(StepIndicator.LabelOf(State.Current, false), Is.EqualTo((true, false, false)));
            Assert.That(StepIndicator.LabelOf(State.Done, false), Is.EqualTo((false, false, false)));
            Assert.That(StepIndicator.LabelOf(State.Upcoming, false), Is.EqualTo((false, false, true)));
        }

        [Test]
        public void OnlyTheObjectsStepTakesTheExclusionError()
        {
            const string error = "除外できませんでした";
            Assert.That(Enumerable.Range(0, StepIndicator.Count).Select(index => StepIndicator.HasError(index, error)), Is.EqualTo(new[] { false, true, false }));
            Assert.That(StepIndicator.HasError(Objects, null), Is.False);
            Assert.That(StepIndicator.HasError(Objects, ""), Is.False);
        }

        [Test]
        public void AnErrorOnTheCurrentStepKeepsItsNameBold()
        {
            var state = StepIndicator.StateOf(Objects, Objects);
            Assert.That(StepIndicator.MarkerOf(state, true), Is.EqualTo(Marker.Error));
            Assert.That(StepIndicator.LabelOf(state, true), Is.EqualTo((true, true, false)), "Bold and red.");
            // On another page the step is red but not bold, and an upcoming one is not dimmed.
            Assert.That(StepIndicator.MarkerOf(State.Done, true), Is.EqualTo(Marker.Error));
            Assert.That(StepIndicator.LabelOf(State.Done, true), Is.EqualTo((false, true, false)));
            Assert.That(StepIndicator.LabelOf(State.Upcoming, true), Is.EqualTo((false, true, false)));
        }

        [Test]
        public void TheThirdStepIsNamedForTheTransfer()
        {
            Assert.That(StepIndicator.Names(false), Is.EqualTo(new[] { "1 表情の設定", "2 除外するオブジェクト", "3 VRMの情報" }));
            Assert.That(StepIndicator.Names(true), Is.EqualTo(new[] { "1 表情の設定", "2 除外するオブジェクト", "3 名前と利用条件" }));
        }

        [TestCase("1 表情の設定", "1", "表情の設定")]
        [TestCase("3 Name and usage terms", "3", "Name and usage terms")]
        [TestCase("VRM", "", "VRM")]
        [TestCase("VRM information", "", "VRM information")]
        [TestCase("", "", "")]
        public void TheNumberIsSplitFromTheName(string text, string number, string name)
        {
            Assert.That(StepIndicator.Split(text), Is.EqualTo((number, name)));
        }

        // The window's minimum and default widths, less the 16px margins on each side.
        [TestCase(568)]
        [TestCase(968)]
        [TestCase(1400)]
        public void TheRowFitsItsWidth(float width)
        {
            var row = new Rect(16, 30, width, StepIndicator.Height);
            var layout = StepIndicator.Compute(row);
            for (var index = 0; index < StepIndicator.Count; index++)
            {
                var circle = layout.Circles[index];
                var label = layout.Labels[index];
                Assert.That(circle.size, Is.EqualTo(new Vector2(StepIndicator.Circle, StepIndicator.Circle)));
                Assert.That(circle.y, Is.GreaterThanOrEqualTo(row.y)); Assert.That(circle.yMax, Is.LessThanOrEqualTo(row.yMax));
                Assert.That(label.x, Is.GreaterThan(circle.xMax), "The name follows its circle.");
                Assert.That(label.width, Is.GreaterThan(80), "Every name has room.");
                if (index > 0)
                {
                    var line = layout.Lines[index - 1];
                    Assert.That(line.width, Is.GreaterThanOrEqualTo(StepIndicator.MinLine));
                    Assert.That(line.height, Is.EqualTo(1));
                    Assert.That(line.x, Is.GreaterThanOrEqualTo(layout.Labels[index - 1].xMax), "The line starts after the previous name.");
                    Assert.That(line.xMax, Is.LessThanOrEqualTo(circle.x), "The line ends before the circle.");
                }
            }
            Assert.That(layout.Circles[0].x, Is.EqualTo(row.x));
            Assert.That(layout.Labels[StepIndicator.Count - 1].xMax, Is.LessThanOrEqualTo(row.xMax));
        }

        [Test]
        public void AWiderRowLengthensTheLinesNotTheNames()
        {
            var narrow = StepIndicator.Compute(new Rect(0, 0, 900, StepIndicator.Height));
            var wide = StepIndicator.Compute(new Rect(0, 0, 1400, StepIndicator.Height));
            Assert.That(wide.Labels.Select(label => label.width), Is.EqualTo(narrow.Labels.Select(label => label.width)));
            Assert.That(wide.Labels.Sum(label => label.width), Is.LessThanOrEqualTo(StepIndicator.MaxLabels));
            Assert.That(wide.Lines[0].width, Is.GreaterThan(narrow.Lines[0].width));
        }

        [TestCase(1f)]
        [TestCase(1.5f)]
        [TestCase(2f)]
        [TestCase(3f)]
        public void TheGlyphBoxIsCentredOnWholeDevicePixels(float scale)
        {
            var circle = new Rect(401, 75, StepIndicator.Circle, StepIndicator.Circle);
            // A "1" box of 6 x 10 px at 100%, from 1px left of the pen and 1px under the baseline.
            var rect = StepIndicator.GlyphRect(circle, -1, 5, -1, 9, -1, 9, scale);
            Assert.That(Mathf.Abs(rect.center.x - circle.center.x) * scale, Is.LessThanOrEqualTo(HalfPixel));
            Assert.That(Mathf.Abs(rect.center.y - circle.center.y) * scale, Is.LessThanOrEqualTo(HalfPixel));
            foreach (var edge in new[] { rect.xMin, rect.yMin, rect.xMax, rect.yMax })
                Assert.That(edge * scale, Is.EqualTo(Mathf.Round(edge * scale)).Within(1e-3f), "The glyph is not resampled between device pixels.");
        }

        [Test]
        public void AGlyphWithAnUnevenBoxStandsOnTheSameBaselineAsOne()
        {
            var circle = new Rect(16, 75, StepIndicator.Circle, StepIndicator.Circle);
            var one = StepIndicator.GlyphRect(circle, -1, 5, -1, 9, -1, 9, 1);
            // "!" reaches as high as "1" but its box has an extra row below the baseline.
            var bang = StepIndicator.GlyphRect(circle, -1, 4, -2, 9, -1, 9, 1);
            Assert.That(bang.yMin, Is.EqualTo(one.yMin));
            Assert.That(bang.yMax, Is.EqualTo(one.yMax + 1));
        }

        [UnityTest]
        public IEnumerator TheNumbersFromTheEditorFontLandOnTheCircleCentre()
        {
            // EditorStyles exists only once some editor GUI has been drawn.
            using var fixture = new AttachmentConnectionTests.Fixture();
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                window.Show();
                for (var frame = 0; frame < 4; frame++) { window.Repaint(); yield return null; }
                var font = EditorStyles.miniBoldFont;
                Assert.That(font, Is.Not.Null);
                var circle = new Rect(16, 75, StepIndicator.Circle, StepIndicator.Circle);
                foreach (var scale in new[] { 1f, 1.5f, 2f, 3f })
                {
                    var size = Mathf.RoundToInt(EditorStyles.miniBoldLabel.fontSize * scale);
                    font.RequestCharactersInTexture("123!", size);
                    Assert.That(font.GetCharacterInfo('1', out var one, size), Is.True);
                    foreach (var c in "123!")
                    {
                        Assert.That(font.GetCharacterInfo(c, out var glyph, size), Is.True);
                        var rect = StepIndicator.GlyphRect(circle, glyph.minX, glyph.maxX, glyph.minY, glyph.maxY, one.minY, one.maxY, scale);
                        Assert.That(Mathf.Abs(rect.center.x - circle.center.x) * scale, Is.LessThanOrEqualTo(HalfPixel), $"'{c}' across at {scale}");
                        Assert.That(circle.Contains(rect.min) && circle.Contains(rect.max), Is.True, $"'{c}' inside the circle at {scale}");
                    }
                }
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator TheFontSizeDoesNotMoveTheStepRowInTheWindow()
        {
            using var fixture = new AttachmentConnectionTests.Fixture();
            var type = typeof(ExperimentalExpressionCaptureWindow); var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var window = ScriptableObject.CreateInstance<ExperimentalExpressionCaptureWindow>();
            // EditorStyles exists only once some editor GUI has been drawn, so
            // the sizes are read after the window's first frames.
            int? label = null, bold = null, mini = null;
            try
            {
                type.GetField("source", flags).SetValue(window, fixture.Source); type.GetMethod("Prepare", flags).Invoke(window, null);
                // Show() restores the size saved for this window type; set ours after it.
                window.Show(); window.position = new Rect(40, 40, 600, 900);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                var before = DrawnRow(window, type, flags);
                Assert.That(before.Length, Is.EqualTo(StepIndicator.Count * 3 - 1), "Every circle, name and line is drawn.");

                label = EditorStyles.label.fontSize; bold = EditorStyles.boldLabel.fontSize; mini = EditorStyles.miniBoldLabel.fontSize;
                EditorStyles.label.fontSize = 24; EditorStyles.boldLabel.fontSize = 24; EditorStyles.miniBoldLabel.fontSize = 20;
                type.GetMethod("ReleaseStyles", flags).Invoke(window, null);
                for (var frame = 0; frame < 8; frame++) { window.Repaint(); yield return null; }
                Assert.That(DrawnRow(window, type, flags), Is.EqualTo(before));
            }
            finally
            {
                if (label.HasValue) EditorStyles.label.fontSize = label.Value;
                if (bold.HasValue) EditorStyles.boldLabel.fontSize = bold.Value;
                if (mini.HasValue) EditorStyles.miniBoldLabel.fontSize = mini.Value;
                Object.DestroyImmediate(window);
            }
        }

        static Rect[] DrawnRow(ExperimentalExpressionCaptureWindow window, System.Type type, System.Reflection.BindingFlags flags)
        {
            var layout = (StepIndicator.Layout)type.GetField("stepLayout", flags).GetValue(window);
            Assert.That(layout, Is.Not.Null, "The step row is drawn.");
            return layout.Circles.Concat(layout.Labels).Concat(layout.Lines).ToArray();
        }
    }
}
