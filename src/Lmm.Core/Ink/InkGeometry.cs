namespace Lmm.Core.Ink;

/// <summary>
/// Pure point-array-in/point-array-out ink geometry primitives, ported
/// faithfully from the legacy app's validated "板书美化" (handwriting
/// beautify) JavaScript implementation. Variable names and structure
/// deliberately mirror the original so the two can be diffed by eye;
/// do not "clean up" the numeric constants below without re-running the
/// synthetic validation cases this was checked against (checkmark/X/arrow
/// corner preservation, cursive-loop non-oversplitting at multiple
/// tightness levels, duplicate-corner-point ribbon-join correctness,
/// jitter-noise robustness).
/// </summary>
public static class InkGeometry
{
    /// <summary>
    /// Douglas-Peucker line simplification, returning the kept points themselves.
    /// </summary>
    public static List<Pt> SimplifyDp(IReadOnlyList<Pt> p, double eps)
    {
        var idx = SimplifyDpIndices(p, eps);
        var outPts = new List<Pt>(idx.Count);
        foreach (var i in idx) outPts.Add(p[i]);
        return outPts;
    }

    /// <summary>
    /// Douglas-Peucker line simplification, but returns the *indices into p*
    /// of the kept points rather than the points themselves. A straight run
    /// collapses to just its two endpoints; a curved arc needs many kept
    /// points to stay within eps — this asymmetry is exactly what
    /// <see cref="CornerSplit"/> exploits to tell "a corner formed by two
    /// straight strokes" apart from "a continuously curving arc".
    /// </summary>
    public static List<int> SimplifyDpIndices(IReadOnlyList<Pt> p, double eps)
    {
        int n = p.Count;
        if (n < 3) return n == 0 ? [] : [0, n - 1];

        var keep = new bool[n];
        keep[0] = keep[n - 1] = true;
        var stack = new Stack<(int a, int b)>();
        stack.Push((0, n - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            double ax = p[a].X, ay = p[a].Y;
            double dx = p[b].X - ax, dy = p[b].Y - ay;
            double l2 = dx * dx + dy * dy;
            int mi = -1;
            double md = eps;
            for (int i = a + 1; i < b; i++)
            {
                double t = l2 > 0 ? ((p[i].X - ax) * dx + (p[i].Y - ay) * dy) / l2 : 0;
                t = Math.Clamp(t, 0, 1);
                double vx = p[i].X - (ax + t * dx);
                double vy = p[i].Y - (ay + t * dy);
                double d = Math.Sqrt(vx * vx + vy * vy);
                if (d > md) { md = d; mi = i; }
            }
            if (mi > 0)
            {
                keep[mi] = true;
                stack.Push((a, mi));
                stack.Push((mi, b));
            }
        }
        var outIdx = new List<int>();
        for (int j = 0; j < n; j++) if (keep[j]) outIdx.Add(j);
        return outIdx;
    }

    /// <summary>Resample a polyline to (approximately) equal arc-length spacing.</summary>
    public static List<Pt> ResamplePts(IReadOnlyList<Pt> p, double step)
    {
        if (p.Count == 0) return [];
        var outPts = new List<Pt> { p[0] };
        // Arc length walked since the last emitted sample. Emitting is driven by the
        // *input segment* being consumed, not by the chord from the last emitted
        // point to p[i]: that chord skips over already-walked segments, so the old
        // bookkeeping re-counted path length and produced clusters of near-duplicate
        // samples (measured spacing ~0.03-1.2px for step=4) instead of ~step spacing.
        double carried = 0;
        for (int i = 1; i < p.Count; i++)
        {
            var a = p[i - 1];
            var b = p[i];
            double dx = b.X - a.X, dy = b.Y - a.Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= 1e-12) continue;

            double pos = 0;
            while (carried + (d - pos) >= step)
            {
                pos += step - carried;
                double t = pos / d;
                outPts.Add(new Pt(a.X + dx * t, a.Y + dy * t));
                carried = 0;
            }
            carried += d - pos;
        }
        if (outPts.Count == 1) outPts.Add(p[^1]);
        return outPts;
    }

    /// <summary>Sliding-window moving-average smoothing; first/last points are preserved untouched.</summary>
    public static List<Pt> SmoothPts(IReadOnlyList<Pt> p, int passes, int win)
    {
        var pts = p;
        List<Pt> result = [.. p];
        for (int k = 0; k < passes; k++)
        {
            if (pts.Count < 3) return result;
            var outPts = new List<Pt>(pts.Count) { pts[0] };
            for (int i = 1; i < pts.Count - 1; i++)
            {
                double sx = 0, sy = 0;
                int cnt = 0;
                for (int j = Math.Max(0, i - win); j <= Math.Min(pts.Count - 1, i + win); j++)
                {
                    sx += pts[j].X; sy += pts[j].Y; cnt++;
                }
                outPts.Add(new Pt(sx / cnt, sy / cnt));
            }
            outPts.Add(pts[^1]);
            result = outPts;
            pts = result;
        }
        return result;
    }

    public static double Median(IReadOnlyList<double> v)
    {
        if (v.Count == 0) return 1;
        var a = v.ToList();
        a.Sort();
        return a[a.Count / 2];
    }

    /// <summary>
    /// Speed-sensing width modulation: slower writing (pauses/turns) lays down
    /// slightly more ink, faster strokes slightly less — restores natural
    /// handwriting texture. Returns one factor per point in sampledPx, mapped
    /// from the raw captured path by arc-length proportion.
    /// </summary>
    public static List<double> WidthFactors(IReadOnlyList<Pt> rawPx, IReadOnlyList<Pt> sampledPx)
    {
        int m = rawPx.Count;
        var sp = new List<double> { 0 };
        var cum = new List<double> { 0 };
        for (int i = 1; i < m; i++)
        {
            double dx = rawPx[i].X - rawPx[i - 1].X, dy = rawPx[i].Y - rawPx[i - 1].Y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            sp.Add(d);
            cum.Add(cum[i - 1] + d);
        }
        double med = m > 1 ? Median(sp.Skip(1).ToList()) : 1;
        if (med == 0) med = 1;

        var f = new List<double>(m);
        for (int i = 0; i < m; i++)
        {
            double v = Math.Pow(med / (sp[i] + 0.6), 0.5);
            f.Add(Math.Clamp(v, 0.7, 1.3));
        }
        // Smooth the raw factor curve itself (reuses SmoothPts on (f, 0) pairs, as the original does).
        var fAsPts = f.Select(x => new Pt(x, 0)).ToList();
        f = SmoothPts(fAsPts, 1, 2).Select(pp => pp.X).ToList();

        int n2 = sampledPx.Count;
        var outF = new List<double>(n2);
        var cum2 = new List<double> { 0 };
        for (int i = 1; i < n2; i++)
        {
            double ddx = sampledPx[i].X - sampledPx[i - 1].X, ddy = sampledPx[i].Y - sampledPx[i - 1].Y;
            cum2.Add(cum2[i - 1] + Math.Sqrt(ddx * ddx + ddy * ddy));
        }
        double tot2 = n2 > 0 && cum2[^1] != 0 ? cum2[^1] : 1;
        double tot1 = m > 0 && cum[^1] != 0 ? cum[^1] : 1;
        int j = 0;
        for (int i = 0; i < n2; i++)
        {
            double target = cum2[i] / tot2 * tot1;
            while (j < m - 2 && cum[j + 1] < target) j++;
            double seg = cum[j + 1] - cum[j];
            double t = seg > 1e-6 ? (target - cum[j]) / seg : 0;
            t = Math.Clamp(t, 0, 1);
            outF.Add(f[j] * (1 - t) + f[j + 1] * t);
        }
        return outF;
    }

    /// <summary>Catmull-Rom spline: passes through every input point with continuous tangents.</summary>
    public static List<Pt> CatmullRom(IReadOnlyList<Pt> p, double step)
    {
        if (p.Count < 3) return [.. p];

        static double Cr(double a, double b, double c, double d, double t) =>
            0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);

        var outPts = new List<Pt> { p[0] };
        for (int i = 0; i < p.Count - 1; i++)
        {
            var p0 = p[Math.Max(0, i - 1)];
            var p1 = p[i];
            var p2 = p[i + 1];
            var p3 = p[Math.Min(p.Count - 1, i + 2)];
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            int steps = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(dx * dx + dy * dy) / step));
            for (int j = 1; j <= steps; j++)
            {
                double t = (double)j / steps;
                outPts.Add(new Pt(
                    Cr(p0.X, p1.X, p2.X, p3.X, t),
                    Cr(p0.Y, p1.Y, p2.Y, p3.Y, t)));
            }
        }
        return outPts;
    }

    /// <summary>
    /// Splits a path into segments, cutting only at "true corners": vertices
    /// that survive Douglas-Peucker simplification AND whose arms on both
    /// sides are long enough to look like straight strokes (a checkmark, an
    /// X, an arrow). A tight cursive loop turns just as sharply, but both
    /// sides of that turn are continuous arcs, not straight runs, so their
    /// DP-surviving arm length never reaches the threshold and the turn is
    /// correctly left unsplit. The arm-length threshold scales with the
    /// stroke's total length (clamped 8..26px) so short tick marks and long
    /// cursive signatures are each judged on their own scale.
    /// </summary>
    public static List<List<Pt>> CornerSplit(IReadOnlyList<Pt> p)
    {
        int n = p.Count;
        if (n < 8) return [[.. p]];

        double len = 0;
        for (int i = 1; i < n; i++) len += Pt.Distance(p[i], p[i - 1]);

        double minArm = Math.Max(8, Math.Min(26, len * 0.18));
        var dpIdx = SimplifyDpIndices(p, 2.2);

        var cuts = new List<int>();
        for (int k = 1; k < dpIdx.Count - 1; k++)
        {
            int ia = dpIdx[k - 1], ib = dpIdx[k], ic = dpIdx[k + 1];
            double armPrev = Pt.Distance(p[ib], p[ia]);
            double armNext = Pt.Distance(p[ic], p[ib]);
            if (armPrev >= minArm && armNext >= minArm) cuts.Add(ib);
        }
        if (cuts.Count == 0) return [[.. p]];

        var segs = new List<List<Pt>>();
        int prev = 0;
        foreach (var c in cuts)
        {
            segs.Add([.. p.Skip(prev).Take(c - prev + 1)]);
            prev = c;
        }
        segs.Add([.. p.Skip(prev)]);
        return segs;
    }
}
