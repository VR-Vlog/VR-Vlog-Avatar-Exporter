using System.Linq;
using UnityEngine;

namespace VRVlog.LilToonExporter
{
    // The step row at the top of the expression board: three circles joined by
    // lines, each followed by its step name. Everything is placed from the row
    // width only, never from the font or the names, so a larger font or a longer
    // translation cannot move a circle or a line; a name longer than its box is
    // shortened with an ellipsis.
    internal static class StepIndicator
    {
        internal enum State { Done, Current, Upcoming }

        // What the circle shows: a check (done), the number (current), the number
        // in a ring (upcoming), or "!" in red (the step has an error).
        internal enum Marker { Check, Number, Ring, Error }

        internal const float Height = 24, Circle = 20, CircleGap = 8, LineGap = 10, MinLine = 16;
        // The name boxes take what the circles and the shortest lines leave, up to
        // this much; a wider row lengthens the lines instead.
        internal const float MaxLabels = 420;
        // Shares of the name boxes in step order. The first name is the shortest in
        // every locale; the second and third are the long ones ("Objects to exclude",
        // "Save and send the VRM").
        static readonly float[] Shares = { .8f, 1.1f, 1.1f };

        internal static int Count => Shares.Length;

        // The third page saves and sends the VRM where the transfer module is there and turned on;
        // without it the page is the VRM's information and its save.
        internal static string[] Names(bool panelUsable) =>
            new[] { "1 表情の設定", "2 除外するオブジェクト", panelUsable ? "3 VRMの保存と転送" : "3 VRMの情報" };

        // "1 表情の設定" is ("1", "表情の設定"): the number goes in the circle.
        internal static (string Number, string Name) Split(string text)
        {
            var space = string.IsNullOrEmpty(text) ? -1 : text.IndexOf(' ');
            return space > 0 && text.Take(space).All(char.IsDigit) ? (text.Substring(0, space), text.Substring(space + 1)) : ("", text ?? "");
        }

        // Before the board is prepared, the first step is the current one.
        internal static int Shown(bool prepared, int page) => prepared ? page : ExperimentalExpressionCaptureWindow.ExpressionsPage;

        internal static State StateOf(int index, int shown) => index < shown ? State.Done : index == shown ? State.Current : State.Upcoming;

        // The objects page is the only step that holds an error of its own: an
        // exclusion that could not be applied to the preview.
        internal static bool HasError(int index, string excludedObjectError) =>
            index == ExperimentalExpressionCaptureWindow.ObjectsPage && !string.IsNullOrEmpty(excludedObjectError);

        internal static Marker MarkerOf(State state, bool error) =>
            error ? Marker.Error : state == State.Done ? Marker.Check : state == State.Current ? Marker.Number : Marker.Ring;

        // The current step's name stays bold when it has an error; the error makes it red.
        internal static (bool Bold, bool Error, bool Dim) LabelOf(State state, bool error) =>
            (state == State.Current, error, !error && state == State.Upcoming);

        // Where to draw a glyph in the circle, in points on whole device pixels. The
        // glyph's box (minX..maxX, minY..maxY: device pixels from its pen and
        // baseline, y up) is centred across. Up and down, the baseline is placed so
        // that the box of "1" would be centred: the numbers and "!" all stand on the
        // baseline and reach the same height, but their boxes carry uneven margins.
        internal static Rect GlyphRect(Rect circle, int minX, int maxX, int minY, int maxY, int oneMinY, int oneMaxY, float pixelsPerPoint)
        {
            var left = Mathf.Round(circle.center.x * pixelsPerPoint - (maxX - minX) / 2f);
            var baseline = Mathf.Round(circle.center.y * pixelsPerPoint + (oneMaxY + oneMinY) / 2f);
            return new Rect(left / pixelsPerPoint, (baseline - maxY) / pixelsPerPoint, (maxX - minX) / pixelsPerPoint, (maxY - minY) / pixelsPerPoint);
        }

        internal sealed class Layout
        {
            internal Rect[] Circles, Labels, Lines;
        }

        internal static Layout Compute(Rect row)
        {
            var count = Count;
            var circles = count * (Circle + CircleGap);
            var labels = Mathf.Clamp(row.width - circles - (count - 1) * (MinLine + LineGap * 2), 0, MaxLabels);
            var line = Mathf.Max(0, (row.width - circles - labels) / (count - 1) - LineGap * 2);
            var total = Shares.Sum();
            var layout = new Layout { Circles = new Rect[count], Labels = new Rect[count], Lines = new Rect[count - 1] };
            var top = row.y + Mathf.Round((row.height - Circle) / 2);
            var middle = Mathf.Round(row.center.y);
            var x = row.x;
            for (var index = 0; index < count; index++)
            {
                if (index > 0)
                {
                    layout.Lines[index - 1] = new Rect(Mathf.Round(x + LineGap), middle, Mathf.Floor(line), 1);
                    x += line + LineGap * 2;
                }
                layout.Circles[index] = new Rect(Mathf.Round(x), top, Circle, Circle);
                x += Circle + CircleGap;
                var width = Mathf.Floor(labels * Shares[index] / total);
                layout.Labels[index] = new Rect(Mathf.Round(x), row.y, width, row.height);
                x += width;
            }
            return layout;
        }
    }
}
