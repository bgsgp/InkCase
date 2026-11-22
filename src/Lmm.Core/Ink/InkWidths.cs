namespace Lmm.Core.Ink;

/// <summary>
/// Per-point width modulation for handwriting, following the way the xnotes
/// calligraphy pen works (shardulvs/xnotes-android, `core/stroke/StrokeEngine`):
/// the nib's radius is driven by pressure and speed, so a slow, pressed stroke
/// is broad and a fast flick is thin, instead of the pen having one fixed width.
///
/// With a mouse there is no usable pressure, so speed alone drives the width and
/// a small per-stroke random-ish variation from the first few samples keeps it
/// from looking mechanically uniform.
/// </summary>
public static class InkWidths
{
    /// <summary>Lower/upper bounds on the speed response, as a multiplier of the base width.</summary>
    public const double MinSpeedFactor = 0.60;
    public const double MaxSpeedFactor = 1.30;

    /// <summary>
    /// Exponent of the speed response: width = (median speed / local speed) ** k.
    /// k = 1 would swing as much as the pen speed does; a sub-linear exponent keeps
    /// normal handwriting stable while still rewarding slow, pressed strokes. Tuned
    /// against rendered samples — 0.22 read as almost monoline.
    /// </summary>
    public const double SpeedExponent = 0.38;

    /// <summary>
    /// Width factors for <paramref name="sampledPx"/> (the final, resampled
    /// centreline), derived from the raw captured samples. Factors are centred on
    /// 1.0 and multiplied by the base pen width by <see cref="InkRibbon"/>.
    /// </summary>
    public static List<double> FromCapture(
        IReadOnlyList<Pt> rawPx,
        IReadOnlyList<double>? pressures,
        IReadOnlyList<double>? times,
        IReadOnlyList<Pt> sampledPx,
        bool live = false)
    {
        int m = rawPx.Count, n = sampledPx.Count;
        var result = new List<double>(n);
        if (m == 0 || n == 0) return result;
        if (m == 1)
        {
            result.AddRange(Enumerable.Repeat(1.0, n));
            return result;
        }

        // 1. Sample speed (px per ms) and pressure along the raw path.
        var speed = new double[m];
        var press = new double[m];
        bool hasPressure = pressures is { Count: >= 2 } && Spread(pressures) > 0.02;
        for (int i = 0; i < m; i++)
        {
            press[i] = hasPressure && i < pressures!.Count ? Math.Clamp(pressures[i], 0, 1) : 0.5;
            if (i == 0)
            {
                speed[0] = 0;
                continue;
            }
            double d = Pt.Distance(rawPx[i], rawPx[i - 1]);
            double dt = times is { Count: > 0 } && i < times.Count ? times[i] - times[i - 1] : 16.0;
            speed[i] = dt > 0.5 ? d / dt : 0;
        }
        // Seed the smoothing with the first real sample: starting from zero would
        // make every stroke's opening read as "infinitely slow" (i.e. widest).
        double seed = speed.FirstOrDefault(v => v > 1e-6);
        speed = Ema(speed, 0.35, seed > 1e-6 ? seed : 1.0);

        // 2. Normalise speed against the stroke's median so the response is relative
        //    to how this person writes, not to an absolute px/ms scale.
        var positive = speed.Where(v => v > 0).ToArray();
        double med = positive.Length > 0 ? Median(positive) : 1.0;
        if (med <= 1e-6) med = 1.0;

        var factor = new double[m];
        for (int i = 0; i < m; i++)
        {
            double v = speed[i] <= 1e-6 ? med : speed[i];
            double speedF = Math.Pow(med / v, SpeedExponent);
            speedF = Math.Clamp(speedF, MinSpeedFactor, MaxSpeedFactor);

            // Pressure: light touch -> thinner, firm -> broader, S-curved so the
            // ends of the range stay stable.
            double pF = hasPressure ? 0.72 + 0.5 * Ease(press[i]) : 1.0;
            factor[i] = speedF * pF;
        }
        factor = Ema(factor, live ? 0.6 : 0.4);

        // 3. Map raw-length factors onto the resampled centreline by arc length.
        var rawCum = Cumulative(rawPx);
        var sampleCum = Cumulative(sampledPx);
        double rawTotal = rawCum[m - 1] > 1e-9 ? rawCum[m - 1] : 1;
        double sampleTotal = sampleCum[n - 1] > 1e-9 ? sampleCum[n - 1] : 1;
        int j = 0;
        for (int i = 0; i < n; i++)
        {
            double target = sampleCum[i] / sampleTotal * rawTotal;
            while (j < m - 2 && rawCum[j + 1] < target) j++;
            double seg = rawCum[j + 1] - rawCum[j];
            double t = seg > 1e-9 ? (target - rawCum[j]) / seg : 0;
            t = Math.Clamp(t, 0, 1);
            double v = factor[j] * (1 - t) + factor[j + 1] * t;
            result.Add(Math.Clamp(v, 0.55, 1.35));
        }
        return result;
    }

    /// <summary>Half-width at one centreline point: base nib, speed/pressure factor, and end taper.</summary>
    public static double HalfWidth(double baseWidth, double t, double widthFactor, bool live)
    {
        double taper = Taper(t, live);
        double hw = baseWidth * 0.5 * widthFactor * taper;
        return Math.Max(0.55, hw);
    }

    /// <summary>
    /// The nib lifts off gradually at both ends: a pen stroke starts thin, thickens
    /// as the nib settles, and thins again on the way out.
    /// </summary>
    public static double Taper(double t, bool live)
    {
        double zone = live ? 0.05 : 0.12;
        double floor = live ? 0.75 : 0.34;
        if (t <= 0 || t >= 1) return floor;
        if (t < zone) return floor + (1 - floor) * Smooth((t) / zone);
        if (t > 1 - zone) return floor + (1 - floor) * Smooth((1 - t) / zone);
        return 1.0;
    }

    private static double Smooth(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    private static double Ease(double p)
    {
        // Logistic S-curve, same idea as xnotes' logisticEase.
        double k = 6.0;
        return 1.0 / (1.0 + Math.Exp(-k * (p - 0.5)));
    }

    private static double Spread(IReadOnlyList<double> values)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var v in values) { if (v < min) min = v; if (v > max) max = v; }
        return max - min;
    }

    private static double Median(double[] values)
    {
        if (values.Length == 0) return 1.0;
        var copy = (double[])values.Clone();
        Array.Sort(copy);
        return copy[copy.Length / 2];
    }

    private static double[] Ema(double[] values, double alpha, double seed = double.NaN)
    {
        var outv = new double[values.Length];
        double acc = double.IsNaN(seed) ? (values.Length > 0 ? values[0] : 0) : seed;
        for (int i = 0; i < values.Length; i++)
        {
            acc = acc + alpha * (values[i] - acc);
            outv[i] = acc;
        }
        return outv;
    }

    private static double[] Cumulative(IReadOnlyList<Pt> p)
    {
        var cum = new double[p.Count];
        for (int i = 1; i < p.Count; i++) cum[i] = cum[i - 1] + Pt.Distance(p[i], p[i - 1]);
        return cum;
    }
}
