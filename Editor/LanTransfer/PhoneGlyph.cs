using UnityEngine;
using UnityEngine.UIElements;

namespace VRVlog.LilToonExporter.LanTransfer
{
    // A phone outline with a small square on its screen, drawn in the text colour so it follows the theme.
    // It sits before the caption under the QR.
    internal sealed class PhoneGlyph : VisualElement
    {
        public PhoneGlyph()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            var r = contentRect;
            if (r.width <= 0f || r.height <= 0f) return;
            var p = context.painter2D;
            var colour = resolvedStyle.color;
            var line = Mathf.Max(1.2f, r.width * 0.13f);
            var inset = line / 2f;
            var outer = new Rect(r.x + inset, r.y + inset, r.width - line, r.height - line);
            p.strokeColor = colour;
            p.lineWidth = line;
            p.lineJoin = LineJoin.Round;
            p.BeginPath();
            RoundRect(p, outer, r.width * 0.22f);
            p.ClosePath();
            p.Stroke();
            // A QR-like square on the screen.
            var square = r.width * 0.42f;
            var x = r.center.x - square / 2f;
            var y = r.y + r.height * 0.42f - square / 2f;
            p.fillColor = colour;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + square, y));
            p.LineTo(new Vector2(x + square, y + square));
            p.LineTo(new Vector2(x, y + square));
            p.ClosePath();
            p.Fill();
        }

        private static void RoundRect(Painter2D p, Rect r, float radius)
        {
            p.MoveTo(new Vector2(r.xMin + radius, r.yMin));
            p.LineTo(new Vector2(r.xMax - radius, r.yMin));
            p.ArcTo(new Vector2(r.xMax, r.yMin), new Vector2(r.xMax, r.yMin + radius), radius);
            p.LineTo(new Vector2(r.xMax, r.yMax - radius));
            p.ArcTo(new Vector2(r.xMax, r.yMax), new Vector2(r.xMax - radius, r.yMax), radius);
            p.LineTo(new Vector2(r.xMin + radius, r.yMax));
            p.ArcTo(new Vector2(r.xMin, r.yMax), new Vector2(r.xMin, r.yMax - radius), radius);
            p.LineTo(new Vector2(r.xMin, r.yMin + radius));
            p.ArcTo(new Vector2(r.xMin, r.yMin), new Vector2(r.xMin + radius, r.yMin), radius);
        }
    }
}
