namespace Lmm.Core.Ink;

/// <summary>
/// Builds the closed outline of a pen stroke with a variable-width nib.
///
/// This is the modern replacement for <see cref="RibbonBuilder"/>'s fixed-taper
/// polygon and follows what the xnotes calligraphy pen does
/// (shardulvs/xnotes-android, `core/stroke/StrokeGeometry` + `Renderer.fillDiskRibbon`):
/// the ink is a brush disc swept along the centreline at a per-point radius, so
/// joins are round, the two ends are real caps, and the stroke's body swells and
/// thins with pressure and speed instead of being one extruded line.
///
/// Output is a flat closed polygon in pixel space: the left rail forward, an end
/// cap, the right rail backward, a start cap. WPF fills it directly.
/// </summary>
public static class InkRibbon
{
    private const int CapSteps = 10;

    /// <summary>
    /// Polyline outline for one stroke.
    /// </summary>
    /// <param name="centerline">Smoothed centreline, pixel coordinates.</param>
    /// <param name="baseWidth">Nominal nib diameter in pixels.</param>
    /// <param name="widthFactors">Per-point modulation, same length as <paramref name="centerline"/>; may be null/short.</param>
    /// <param name="live">While the pen is down: less end taper, so the growing line keeps its ink.</param>
    public static List<Pt> Build(
        IReadOnlyList<Pt> centerline,
        double baseWidth,
        IReadOnlyList<double>? widthFactors,
        bool live = false)
    {
        int n = centerline.Count;
        if (n == 0) return [];
        if (n == 1)
        {
            double r = Math.Max(0.55, baseWidth * 0.5);
            return Circle(centerline[0], r);
        }

        var cum = new double[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Pt.Distance(centerline[i], centerline[i - 1]);
        double total = cum[n - 1] > 1e-9 ? cum[n - 1] : 1;

        var left = new Pt[n];
        var right = new Pt[n];
        var radii = new double[n];

        for (int i = 0; i < n; i++)
        {
            double t = cum[i] / total;
            double wf = widthFactors is { Count: > 0 } && i < widthFactors.Count ? widthFactors[i] : 1.0;
            double hw = InkWidths.HalfWidth(baseWidth, t, wf, live);
            radii[i] = hw;

            // Tangent from the neighbours; miter the two segment normals so joins
            // stay sharp but don't spike on a hairpin.
            var prev = centerline[i > 0 ? i - 1 : 0];
            var next = centerline[i < n - 1 ? i + 1 : n - 1];
            var (nx, ny) = Normal(prev, next, centerline[i]);

            left[i] = new Pt(centerline[i].X + nx * hw, centerline[i].Y + ny * hw);
            right[i] = new Pt(centerline[i].X - nx * hw, centerline[i].Y - ny * hw);
        }

        var poly = new List<Pt>(n * 2 + CapSteps * 2 + 4);
        poly.AddRange(left);

        // End cap: sweep from the left rail around the nib to the right rail,
        // through the direction of travel.
        var endDir = Unit(centerline[n - 2], centerline[n - 1]);
        AppendCap(poly, centerline[n - 1], radii[n - 1], endDir, startsAtLeft: true);

        for (int i = n - 1; i >= 0; i--) poly.Add(right[i]);

        // Start cap: same idea, sweeping backwards out of the stroke.
        var startDir = Unit(centerline[1], centerline[0]);
        AppendCap(poly, centerline[0], radii[0], startDir, startsAtLeft: false);

        return poly;
    }

    private static void AppendCap(List<Pt> poly, Pt center, double radius, Pt dir, bool startsAtLeft)
    {
        // In screen coordinates (y down) the sweep sign that carries the arc
        // through the travel direction is the sign of cross(normal, dir).
        var n = new Pt(-dir.Y, dir.X);
        double cross = n.X * dir.Y - n.Y * dir.X;
        double sign = cross >= 0 ? 1 : -1;
        double start = startsAtLeft ? Math.Atan2(n.Y, n.X) : Math.Atan2(-n.Y, -n.X);
        double sweep = sign * Math.PI;
        for (int k = 1; k < CapSteps; k++)
        {
            double a = start + sweep * k / CapSteps;
            poly.Add(new Pt(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius));
        }
    }

    private static (double nx, double ny) Normal(Pt prev, Pt next, Pt at)
    {
        double tx = next.X - prev.X, ty = next.Y - prev.Y;
        double len = Math.Sqrt(tx * tx + ty * ty);
        if (len < 1e-9)
        {
            tx = next.X - at.X; ty = next.Y - at.Y;
            len = Math.Sqrt(tx * tx + ty * ty);
            if (len < 1e-9) return (0, 1);
        }
        return (-ty / len, tx / len);
    }

    private static Pt Unit(Pt a, Pt b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        return len < 1e-9 ? new Pt(1, 0) : new Pt(dx / len, dy / len);
    }

    /// <summary>A filled dot, as a closed polygon; used for taps and single samples.</summary>
    public static List<Pt> Circle(Pt center, double radius, int steps = 24)
    {
        var pts = new List<Pt>(steps);
        for (int i = 0; i < steps; i++)
        {
            double a = i * Math.PI * 2 / steps;
            pts.Add(new Pt(center.X + Math.Cos(a) * radius, center.Y + Math.Sin(a) * radius));
        }
        return pts;
    }
}
