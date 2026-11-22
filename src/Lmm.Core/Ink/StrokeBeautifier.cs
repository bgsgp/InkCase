namespace Lmm.Core.Ink;

/// <summary>
/// A stroke as captured: fractional (0..1) surface coordinates, same
/// convention as <see cref="Lmm.Core.Model.InkStroke"/>, so it is
/// resolution/zoom independent.
/// </summary>
/// <param name="Color">Pen color, e.g. "#D33A2C".</param>
/// <param name="Width">Base pen width in pixels (at the canvas size used to capture it).</param>
/// <param name="FractionalPoints">Raw captured points, fractional coordinates.</param>
public sealed record RawStroke(
    string Color,
    double Width,
    IReadOnlyList<Pt> FractionalPoints,
    IReadOnlyList<double>? Pressures = null,
    IReadOnlyList<double>? Times = null);

/// <summary>
/// Output of <see cref="StrokeBeautifier.Beautify"/>: resampled, smoothed
/// points (still fractional coordinates) plus a per-point width-modulation
/// factor. This is what gets persisted to <see cref="Lmm.Core.Model.InkStroke"/>
/// — not the final ribbon polygon, which <see cref="RibbonBuilder"/> derives
/// from this on every render.
/// </summary>
public sealed record BeautifiedStroke(string Color, double Width, IReadOnlyList<Pt> FractionalPoints, IReadOnlyList<double> WidthFactors);

/// <summary>
/// Ports the legacy app's "板书美化" entry point (beautifyStroke/finishStroke)
/// faithfully: resample + speed-based width modulation, with three
/// classification branches run in this order —
/// 1) near-closed, much-longer-than-bbox path → regularized ellipse;
/// 2) overall deviation from the start-end chord is small → straightened line
///    (keeping 40% of the natural deviation so it doesn't look ruler-stiff);
/// 3) everything else (Chinese character strokes, checkmarks/X/arrows,
///    cursive connected handwriting) → corner-preserving segmentation
///    (<see cref="InkGeometry.CornerSplit"/>) with Catmull-Rom fit inside
///    each segment.
/// </summary>
public static class StrokeBeautifier
{
    /// <summary>
    /// Beautifies one raw stroke. Returns null when the stroke is too short/small
    /// to be worth beautifying (matches the legacy app's "leave it alone" behavior
    /// for e.g. accidental taps).
    /// </summary>
    public static BeautifiedStroke? Beautify(RawStroke st, double canvasW, double canvasH)
    {
        var pts = st.FractionalPoints;
        if (pts.Count < 2) return null;

        if (pts.Count == 2)
        {
            var a = ToPixel(pts[0], canvasW, canvasH);
            var b = ToPixel(pts[1], canvasW, canvasH);
            double dd = Pt.Distance(a, b);
            if (dd < 8) return null;
            return FinishStroke(st.Color, st.Width, [a, b], canvasW, canvasH, 8, pts, st.Pressures, st.Times);
        }

        int n = pts.Count;
        var p = new Pt[n];
        double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
        for (int i = 0; i < n; i++)
        {
            p[i] = ToPixel(pts[i], canvasW, canvasH);
            if (p[i].X < minX) minX = p[i].X;
            if (p[i].X > maxX) maxX = p[i].X;
            if (p[i].Y < minY) minY = p[i].Y;
            if (p[i].Y > maxY) maxY = p[i].Y;
        }

        double dx0 = p[n - 1].X - p[0].X, dy0 = p[n - 1].Y - p[0].Y;
        double chord = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
        double diag = Math.Sqrt((maxX - minX) * (maxX - minX) + (maxY - minY) * (maxY - minY));
        double len = 0;
        for (int i = 1; i < n; i++) len += Pt.Distance(p[i], p[i - 1]);
        if (len < 8 || diag < 6) return null;

        // Circled/looped mark -> regularized ellipse.
        if (chord < diag * 0.4 && len > diag * 1.5 && diag > 26)
        {
            double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            double rx = Math.Max((maxX - minX) / 2, 4), ry = Math.Max((maxY - minY) / 2, 4);
            double a0 = Math.Atan2(p[0].Y - cy, p[0].X - cx);
            var ellipse = new List<Pt>(49);
            for (int i = 0; i <= 48; i++)
            {
                double ang = a0 + i / 48.0 * Math.PI * 2;
                ellipse.Add(new Pt(cx + rx * Math.Cos(ang), cy + ry * Math.Sin(ang)));
            }
            return FinishStroke(st.Color, st.Width, ellipse, canvasW, canvasH, 4, pts, st.Pressures, st.Times);
        }

        // Near-straight -> straighten (keep 40% of natural deviation so it isn't ruler-stiff).
        double l2 = dx0 * dx0 + dy0 * dy0;
        double maxDev = 0;
        if (l2 > 1)
        {
            for (int i = 0; i < n; i++)
            {
                double t2 = ((p[i].X - p[0].X) * dx0 + (p[i].Y - p[0].Y) * dy0) / l2;
                t2 = Math.Clamp(t2, 0, 1);
                double vx = p[i].X - (p[0].X + t2 * dx0), vy = p[i].Y - (p[0].Y + t2 * dy0);
                double d = Math.Sqrt(vx * vx + vy * vy);
                if (d > maxDev) maxDev = d;
            }
        }
        if (chord > 30 && maxDev < Math.Max(6, chord * 0.08))
        {
            var seg = new List<Pt>(n);
            for (int i = 0; i < n; i++)
            {
                double t3 = ((p[i].X - p[0].X) * dx0 + (p[i].Y - p[0].Y) * dy0) / l2;
                double bx = p[0].X + t3 * dx0, by = p[0].Y + t3 * dy0;
                seg.Add(new Pt(bx + (p[i].X - bx) * 0.4, by + (p[i].Y - by) * 0.4));
            }
            return FinishStroke(st.Color, st.Width, seg, canvasW, canvasH, 8, pts, st.Pressures, st.Times);
        }

        // Everything else: split only at true corners, Catmull-Rom fit within each segment.
        // Segments are concatenated WITHOUT deduplicating the shared corner point: the ribbon
        // builder computes each point's normal from its neighbors, so a corner needs to appear
        // once for "arriving" and once for "leaving" or the two directions cancel into a
        // degenerate normal and the ribbon pinches into a pinhole at the join.
        var segs = InkGeometry.CornerSplit(p);
        var s = new List<Pt>();
        foreach (var seg in segs)
        {
            List<Pt> piece;
            if (seg.Count < 5)
            {
                piece = seg;
            }
            else
            {
                var smSeg = InkGeometry.SmoothPts(seg, 2, 3);
                var simpSeg = InkGeometry.SimplifyDp(smSeg, Math.Max(1.0, len / 300));
                piece = simpSeg.Count >= 3 ? InkGeometry.CatmullRom(simpSeg, 1.8) : simpSeg;
            }
            s.AddRange(piece);
        }
        if (s.Count < 2) return null;
        return FinishStroke(st.Color, st.Width, s, canvasW, canvasH, 2, pts, st.Pressures, st.Times);
    }

    /// <summary>Resample + smooth + compute width factors; normalizes back to fractional coordinates.</summary>
    private static BeautifiedStroke FinishStroke(
        string color, double w, IReadOnlyList<Pt> pixelPoints, double canvasW, double canvasH,
        double step, IReadOnlyList<Pt>? rawFractionalPoints,
        IReadOnlyList<double>? pressures = null, IReadOnlyList<double>? times = null)
    {
        var rs = InkGeometry.ResamplePts(pixelPoints, step);
        rs = InkGeometry.SmoothPts(rs, 1, 2);
        var np = rs.Select(pp => new Pt(pp.X / canvasW, pp.Y / canvasH)).ToList();

        IReadOnlyList<Pt> raw = rawFractionalPoints is { Count: > 0 }
            ? rawFractionalPoints.Select(pp => ToPixel(pp, canvasW, canvasH)).ToList()
            : rs;

        var wf = InkWidths.FromCapture(raw, pressures, times, rs);
        return new BeautifiedStroke(color, w, np, wf);
    }

    private static Pt ToPixel(Pt fractional, double canvasW, double canvasH) =>
        new(fractional.X * canvasW, fractional.Y * canvasH);
}
