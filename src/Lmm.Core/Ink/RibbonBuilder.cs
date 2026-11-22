namespace Lmm.Core.Ink;

/// <summary>
/// Builds the variable-width "ink ribbon" polygon from a beautified stroke's
/// point list, ported from the legacy app's drawRibbonPx. Pure geometry —
/// returns the closed polygon outline (left edge forward, then right edge
/// backward) in pixel coordinates; a renderer (WPF DrawingVisual +
/// StreamGeometry per the approved Phase 2b plan) fills it with the pen
/// color. Kept separate from <see cref="StrokeBeautifier"/> because this is
/// recomputed on every render/zoom/DPI change, not just once per stroke.
/// </summary>
public static class RibbonBuilder
{
    /// <summary>
    /// Converts a persisted/beautified stroke's fractional points to the
    /// ribbon polygon at a given canvas pixel size.
    /// </summary>
    public static List<Pt> BuildPolygon(BeautifiedStroke stroke, double canvasW, double canvasH, bool live = false)
    {
        var p = stroke.FractionalPoints.Select(pp => new Pt(pp.X * canvasW, pp.Y * canvasH)).ToList();
        return BuildPolygonPx(p, stroke.Width, live, stroke.WidthFactors);
    }

    /// <summary>
    /// Core geometry: given points already in pixel space, builds the ribbon
    /// outline. `live` selects the shorter/less-tapered pen-tip profile used
    /// while the stroke is still being drawn (see e0/e1/m0/m1 below), versus
    /// the fuller taper used once a stroke is finished.
    /// </summary>
    public static List<Pt> BuildPolygonPx(IReadOnlyList<Pt> p, double baseWidth, bool live, IReadOnlyList<double>? wf)
    {
        int n = p.Count;
        var cum = new double[n];
        for (int i = 1; i < n; i++)
        {
            double ddx = p[i].X - p[i - 1].X, ddy = p[i].Y - p[i - 1].Y;
            cum[i] = cum[i - 1] + Math.Sqrt(ddx * ddx + ddy * ddy);
        }
        double total = n > 0 && cum[n - 1] != 0 ? cum[n - 1] : 1;

        var left = new List<Pt>(n);
        var right = new List<Pt>(n);

        double e0 = 0.16, e1 = live ? 0.08 : 0.16;      // pen-tip taper zone, start/end of stroke
        double m0 = 0.1, m1 = live ? 0.62 : 0.1;        // minimum width fraction in that zone

        for (int i = 0; i < n; i++)
        {
            var a = p[i > 0 ? i - 1 : 0];
            var b = p[i < n - 1 ? i + 1 : n - 1];
            double tx = b.X - a.X, ty = b.Y - a.Y;
            double l = Math.Sqrt(tx * tx + ty * ty);
            if (l == 0) l = 1;
            double nx = -ty / l, ny = tx / l;

            double t = cum[i] / total;
            double prof;
            if (t < e0) prof = m0 + (1 - m0) * (t / e0);
            else if (t > 1 - e1) prof = m1 + (1 - m1) * ((1 - t) / e1);
            else prof = 1;

            double wfi = wf is { Count: > 0 } && i < wf.Count ? wf[i] : 1;
            double hw = baseWidth * prof * wfi / 2;

            left.Add(new Pt(p[i].X + nx * hw, p[i].Y + ny * hw));
            right.Add(new Pt(p[i].X - nx * hw, p[i].Y - ny * hw));
        }

        var polygon = new List<Pt>(n * 2);
        polygon.AddRange(left);
        for (int i = n - 1; i >= 0; i--) polygon.Add(right[i]);
        return polygon;
    }
}
