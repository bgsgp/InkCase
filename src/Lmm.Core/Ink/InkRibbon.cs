using System;
using System.Collections.Generic;

namespace Lmm.Core.Ink;

/// <summary>
/// Turns a centreline plus per-point half-widths into the closed outline of the
/// swept disk — the shape xnotes fills (left rail out, round cap, right rail
/// back, round cap). Caps are real semicircles, so a thin stroke still ends in a
/// dot and a thick one ends in a rounded nib rather than a chopped rectangle.
///
/// A stroke whose ends meet (a circled word) is *not* special-cased: the two sets
/// of caps simply overlap, and the shapes are filled with the nonzero winding rule
/// so the overlap unions instead of punching a hole (with even-odd it would).
/// </summary>
public static class InkRibbon
{
    /// <summary>Semicircle resolution used at both ends.</summary>
    public const int CapSteps = 10;

    /// <summary>The outline for one stroke, in the same coordinate space as the centreline.</summary>
    public static IReadOnlyList<Pt> Build(IReadOnlyList<Pt> center, IReadOnlyList<double> halfWidth)
    {
        if (center.Count == 0) return [];
        if (center.Count == 1) return Circle(center[0], Math.Max(0.3, halfWidth[0]));

        var (tx, ty) = Tangents(center);
        return Outline(center, halfWidth, tx, ty);
    }

    /// <summary>
    /// Splits the stroke into dashes of <paramref name="dashLength"/> with
    /// <paramref name="dashGap"/> of empty space, each dash capped round.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<Pt>> BuildDashed(
        IReadOnlyList<Pt> center, IReadOnlyList<double> halfWidth, double dashLength, double dashGap)
    {
        var result = new List<IReadOnlyList<Pt>>();
        int n = center.Count;
        if (n == 0) return result;
        if (n == 1) { result.Add(Circle(center[0], Math.Max(0.3, halfWidth[0]))); return result; }

        var cum = new double[n];
        for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Distance(center[i - 1], center[i]);
        if (cum[n - 1] <= 0.0) { result.Add(Circle(center[0], Math.Max(0.3, halfWidth[0]))); return result; }

        double dash = Math.Max(1.0, dashLength);
        double gap = Math.Max(0.5, dashGap);
        for (double start = 0; start < cum[n - 1]; start += dash + gap)
            result.Add(DashOutline(center, halfWidth, cum, start, Math.Min(start + dash, cum[n - 1])));

        return result;
    }

    /// <summary>A filled disc, used for the single-point tap.</summary>
    public static IReadOnlyList<Pt> Circle(Pt center, double radius, int steps = 24)
    {
        var pts = new List<Pt>(steps);
        for (int i = 0; i < steps; i++)
        {
            double a = 2.0 * Math.PI * i / steps;
            pts.Add(new Pt(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }
        return pts;
    }

    // ---- geometry ---------------------------------------------------------

    private static IReadOnlyList<Pt> Outline(
        IReadOnlyList<Pt> center, IReadOnlyList<double> halfWidth, double[] tx, double[] ty)
    {
        int n = center.Count;
        var poly = new List<Pt>(n * 2 + CapSteps * 2);

        // Left rail, forward.
        for (int i = 0; i < n; i++)
        {
            double h = Math.Max(0.05, halfWidth[i]);
            poly.Add(new Pt(center[i].X - ty[i] * h, center[i].Y + tx[i] * h));
        }

        // End cap: sweep from the left normal, through the outward tangent, to the right normal.
        double he = Math.Max(0.05, halfWidth[n - 1]);
        for (int k = 1; k < CapSteps; k++)
            AddCap(poly, center[n - 1], tx[n - 1], ty[n - 1], he, Math.PI * k / CapSteps);

        // Right rail, back.
        for (int i = n - 1; i >= 0; i--)
        {
            double h = Math.Max(0.05, halfWidth[i]);
            poly.Add(new Pt(center[i].X + ty[i] * h, center[i].Y - tx[i] * h));
        }

        // Start cap: from the right normal, through the backward tangent, back to the left normal.
        double hs = Math.Max(0.05, halfWidth[0]);
        for (int k = 1; k < CapSteps; k++)
            AddCap(poly, center[0], tx[0], ty[0], hs, Math.PI + Math.PI * k / CapSteps);

        return poly;
    }

    private static void AddCap(List<Pt> poly, Pt at, double tx, double ty, double r, double angle)
    {
        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        poly.Add(new Pt(at.X + (-ty * cos + tx * sin) * r, at.Y + (tx * cos + ty * sin) * r));
    }

    private static IReadOnlyList<Pt> DashOutline(
        IReadOnlyList<Pt> center, IReadOnlyList<double> halfWidth, double[] cum, double from, double to)
    {
        if (to - from <= 0.05) return [];

        var sub = new List<Pt>();
        var subH = new List<double>();

        var start = SampleAt(center, halfWidth, cum, from);
        sub.Add(start.point);
        subH.Add(start.halfWidth);

        const double step = 1.5;
        for (double s = from + step; s < to; s += step)
        {
            var p = SampleAt(center, halfWidth, cum, s);
            sub.Add(p.point);
            subH.Add(p.halfWidth);
        }

        var end = SampleAt(center, halfWidth, cum, to);
        sub.Add(end.point);
        subH.Add(end.halfWidth);

        var (tx, ty) = Tangents(sub);
        return Outline(sub, subH, tx, ty);
    }

    private static (Pt point, double halfWidth) SampleAt(
        IReadOnlyList<Pt> center, IReadOnlyList<double> halfWidth, double[] cum, double s)
    {
        int n = cum.Length;
        if (s <= 0) return (center[0], halfWidth[0]);
        if (s >= cum[n - 1]) return (center[n - 1], halfWidth[n - 1]);

        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (cum[mid] < s) lo = mid + 1; else hi = mid;
        }

        double span = cum[lo] - cum[lo - 1];
        double f = span > 1e-9 ? (s - cum[lo - 1]) / span : 1.0;
        var a = center[lo - 1];
        var b = center[lo];
        return (new Pt(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f),
            halfWidth[lo - 1] + (halfWidth[lo] - halfWidth[lo - 1]) * f);
    }

    /// <summary>Unit tangents by central difference; degenerate samples inherit the previous one.</summary>
    private static (double[] tx, double[] ty) Tangents(IReadOnlyList<Pt> center)
    {
        int n = center.Count;
        var tx = new double[n];
        var ty = new double[n];
        if (n == 0) return (tx, ty);
        double lastX = 1.0, lastY = 0.0;
        for (int i = 0; i < n; i++)
        {
            double dx, dy;
            if (n == 1) { dx = 1.0; dy = 0.0; }
            else if (i == 0) { dx = center[1].X - center[0].X; dy = center[1].Y - center[0].Y; }
            else if (i == n - 1) { dx = center[i].X - center[i - 1].X; dy = center[i].Y - center[i - 1].Y; }
            else { dx = center[i + 1].X - center[i - 1].X; dy = center[i + 1].Y - center[i - 1].Y; }

            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-9) { tx[i] = lastX; ty[i] = lastY; }
            else { tx[i] = dx / len; ty[i] = dy / len; lastX = tx[i]; lastY = ty[i]; }
        }
        return (tx, ty);
    }

    private static double Distance(Pt a, Pt b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}