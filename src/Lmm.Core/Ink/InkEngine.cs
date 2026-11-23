using System;
using System.Collections.Generic;

namespace Lmm.Core.Ink;

/// <summary>Ribbon geometry for one stroke: centreline, unit tangent, and half-width per point.</summary>
public sealed class StrokeGeometry
{
    public required double[] X { get; init; }
    public required double[] Y { get; init; }
    public required double[] Tx { get; init; }
    public required double[] Ty { get; init; }
    public required double[] HalfWidth { get; init; }

    public int Count => X.Length;
}

/// <summary>
/// Port of xnotes' `StrokeEngine` (shardulvs/xnotes-android,
/// `core/stroke/StrokeEngine.kt`) — the variable-width ink model behind its six
/// pens. Kept as a faithful transliteration of the Kotlin, names and constants
/// included, because the whole point is that the numbers behave like the original:
///
///  * position and pressure are low-passed **by arc length**, not per sample, so a
///    thinned or re-spaced sample set draws the same curve;
///  * half-width comes from pressure through a logistic S-curve, times a
///    calligraphic direction term;
///  * the calligraphy nib is a slew limiter over the raw heading — it can widen only
///    over `openLen` of travel and thin only over `closeLen` — with the head of the
///    stroke pinned to one chord and the tail capped so a lift-off skid can't inflate it;
///  * the speed pen measures raw travel per moving millisecond in a window around each
///    sample, then blurs that along the path;
///  * the taper pen eases the whole stroke down to `taperMinFactor` at the tip.
///
/// All arithmetic is in doubles; the calls happen in pixel space (the caller converts
/// to and from fractional coordinates), so `smoothScale` stays 1.0.
/// </summary>
public static class InkEngine
{
    /// <summary>EMA low-pass factor per sample the arc-length lengths are tuned to reproduce.</summary>
    public const double Alpha = 0.5;

    /// <summary>Sample spacing (px) <see cref="SmoothLen"/> is tuned at.</summary>
    public const double ReferenceSpacing = 1.5;

    /// <summary>Low-pass length (px) for position and pressure: `d · (1 - alpha) / alpha`.</summary>
    public static readonly double SmoothLen = ReferenceSpacing * (1.0 - Alpha) / Alpha;

    private const double MinTangentLen = 1e-6;
    private const double MinStep = 1e-9;
    private const double MinDirection = 0.1;

    /// <summary>Steepness of the pressure response S-curve.</summary>
    private const double PressureCurveK = 8.0;

    private const double HeadLenConst = 8.0;
    private const double OpenLenConst = 8.0;
    private const double CloseLenConst = 8.0;
    private const double NibRateWidth = 6.0;
    private const double ReversalFromDeg = 120.0;
    private const double ReversalFullDeg = 160.0;
    private const double ReversalWaitConst = 0.5;
    private const double ReversalMinWait = 3.0;
    private const double DotDirY = 1.5;

    /// <summary>Speed pen: top speed (px/ms) where the line is thinnest.</summary>
    public const double SpeedHi = 0.6;

    /// <summary>
    /// Bottom of the speed pen's width factor. xnotes lets the pen fall to
    /// 1 - strength (0.2 at strength 0.8), which reads as a hairline when you
    /// write quickly — fine on a phone held at arm's length, invisible from the
    /// back of a classroom. The factor never goes below this.
    /// </summary>
    public const double MinSpeedFactor = 0.5;

    private const double SpeedWindowMs = 25.0;
    private const double SpeedFloor = 0.1;
    public const double DefaultSpeedSmoothing = 0.25;
    private const double SpeedBlurReach = 3.0;

    private const double TaperMinLen = 8.0;
    private const double TaperTail = 0.01;
    private const int CapHoldSamples = 4;

    private static readonly double TaperCurveK = 2.0 * Math.Log((1.0 - TaperTail) / TaperTail);
    private static readonly double ReversalFromCos = Math.Cos(ReversalFromDeg * Math.PI / 180.0);

    /// <summary>
    /// Builds the ribbon for one stroke. <paramref name="raw"/> is in pixel space;
    /// <paramref name="pressures"/> and <paramref name="times"/> may be null (mouse
    /// input carries neither). <paramref name="finished"/> is false while the pen is
    /// still down, which holds off the lift-time rules.
    /// </summary>
    public static StrokeGeometry Build(
        IReadOnlyList<Pt> raw,
        IReadOnlyList<double>? pressures,
        IReadOnlyList<double>? times,
        InkStyle style,
        bool finished = true)
    {
        int n = raw.Count;
        if (n == 0) return new StrokeGeometry { X = [], Y = [], Tx = [], Ty = [], HalfWidth = [] };

        var rx = new double[n];
        var ry = new double[n];
        for (int i = 0; i < n; i++)
        {
            rx[i] = raw[i].X;
            ry[i] = raw[i].Y;
        }

        double baseWidth = Math.Max(0.5, style.BaseWidth);
        bool pressureEnabled = style.PressureEnabled;
        double m = style.PressureMinFactor;
        double ds = style.DirectionStrength;

        var steps = new double[n];
        for (int i = 1; i < n; i++) steps[i] = Hypot(rx[i] - rx[i - 1], ry[i] - ry[i - 1]);

        var sx = EmaByArc(rx, steps, SmoothLen);
        var sy = EmaByArc(ry, steps, SmoothLen);

        var rawP = new double[n];
        for (int i = 0; i < n; i++)
            rawP[i] = pressures is { Count: > 0 } && i < pressures.Count ? Math.Clamp(pressures[i], 0.0, 1.0) : 0.5;

        var sp = EmaByArc(rawP, steps, SmoothLen);
        if (style.HoldEnds && pressureEnabled) HoldEndPressure(sp);

        double Hw(int i, double ty) => HalfWidth(baseWidth, pressureEnabled, m, ds, sp[i], ty);

        if (n == 1)
        {
            double h = Hw(0, finished && ds > 0.0 ? DotDirY : 0.0);
            return new StrokeGeometry
            {
                X = [sx[0]], Y = [sy[0]], Tx = [1.0], Ty = [0.0], HalfWidth = [h],
            };
        }

        // Per-point unit tangent by finite differences, carrying the last good one
        // through degenerate (repeated) samples.
        var tx = new double[n];
        var ty2 = new double[n];
        double lastTx = 1.0, lastTy = 0.0;
        for (int i = 0; i < n; i++)
        {
            double dx, dy;
            if (i == 0) { dx = sx[1] - sx[0]; dy = sy[1] - sy[0]; }
            else if (i == n - 1) { dx = sx[i] - sx[i - 1]; dy = sy[i] - sy[i - 1]; }
            else { dx = sx[i + 1] - sx[i - 1]; dy = sy[i + 1] - sy[i - 1]; }

            double len = Hypot(dx, dy);
            if (len < MinTangentLen) { tx[i] = lastTx; ty2[i] = lastTy; }
            else { tx[i] = dx / len; ty2[i] = dy / len; lastTx = tx[i]; lastTy = ty2[i]; }
        }

        bool speedOn = style.SpeedStrength > 0.0 && times is { Count: > 0 };
        bool needArc = ds > 0.0 || style.TaperEnabled || speedOn;
        var dirSteps = new double[needArc ? n : 0];
        var cum = new double[dirSteps.Length];
        if (needArc)
        {
            for (int i = 1; i < n; i++)
            {
                dirSteps[i] = Hypot(sx[i] - sx[i - 1], sy[i] - sy[i - 1]);
                cum[i] = cum[i - 1] + dirSteps[i];
            }
        }

        double[]? sf = null;
        if (speedOn)
        {
            var t = new double[n];
            for (int i = 0; i < n; i++)
                t[i] = times is { Count: > 0 } && i < times.Count ? times[i] : 0.0;
            sf = SpeedFactorsOver(t, steps, cum, n, style.SpeedStrength, SpeedHi, DefaultSpeedSmoothing * baseWidth);
        }

        double[]? tf = style.TaperEnabled ? TaperFactors(cum, style.TaperMinFactor) : null;

        double[]? dirY = null;
        if (ds > 0.0)
        {
            double window = HeadLenConst;
            var (head, holdUntil) = HeadDirection(sx, sy, cum, window, finished);
            var rcum = new double[n];
            for (int i = 1; i < n; i++) rcum[i] = rcum[i - 1] + steps[i];

            double nibOpen = Math.Max(OpenLenConst, OpenLenConst / NibRateWidth * baseWidth);
            double nibClose = Math.Max(CloseLenConst, CloseLenConst / NibRateWidth * baseWidth);
            double wait = Math.Max(ReversalMinWait, ReversalWaitConst * baseWidth);
            dirY = NibDirection(rx, ry, rcum, ty2, dirSteps, nibOpen, nibClose, wait, head, holdUntil);
            if (finished) CapTail(dirY, cum, window);
        }

        var xs = new double[n];
        var ys = new double[n];
        var txs = new double[n];
        var tys = new double[n];
        var hws = new double[n];
        for (int i = 0; i < n; i++)
        {
            double dir = dirY != null ? Math.Clamp(dirY[i], -1.0, DotDirY) : ty2[i];
            double h = Hw(i, dir);
            if (sf != null) h *= sf[i];
            if (tf != null) h *= tf[i];

            xs[i] = sx[i];
            ys[i] = sy[i];
            txs[i] = tx[i];
            tys[i] = ty2[i];
            hws[i] = h;
        }

        return new StrokeGeometry { X = xs, Y = ys, Tx = txs, Ty = tys, HalfWidth = hws };
    }

    // ---- shared math ------------------------------------------------------

    /// <summary>Normalized logistic S-curve on [0,1], endpoints exact; k sets the steepness.</summary>
    public static double LogisticEase(double x, double k)
    {
        if (k <= 0.0) return x;
        double lo = 1.0 / (1.0 + Math.Exp(k * 0.5));
        double hi = 1.0 / (1.0 + Math.Exp(-k * 0.5));
        double raw = 1.0 / (1.0 + Math.Exp(-k * (x - 0.5)));
        return (raw - lo) / (hi - lo);
    }

    /// <summary>Hermite smoothstep: 0 below lo, 1 above hi, an S-curve between.</summary>
    public static double Smoothstep(double lo, double hi, double x)
    {
        if (hi <= lo) return x >= hi ? 1.0 : 0.0;
        double t = Math.Clamp((x - lo) / (hi - lo), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Half-width at a point from smoothed pressure and the nib's direction-y.</summary>
    public static double HalfWidth(double baseWidth, bool pressureEnabled, double m, double ds, double pressure, double ty)
    {
        double pEff = pressureEnabled ? LogisticEase(pressure, PressureCurveK) : 1.0;
        double wBase = baseWidth * (m + (1 - m) * pEff);
        double direction = Math.Max(1 + ds * ty, MinDirection);
        return wBase * direction / 2.0;
    }

    /// <summary>One-pole IIR low-pass. Seeds on the first sample, not on zero.</summary>
    public static double[] Ema(IReadOnlyList<double> values, double alpha)
    {
        var outv = new double[values.Count];
        if (values.Count == 0) return outv;
        outv[0] = values[0];
        for (int i = 1; i < values.Count; i++) outv[i] = alpha * values[i] + (1 - alpha) * outv[i - 1];
        return outv;
    }

    /// <summary>The same filter measured in travel: lag is lambda whatever the sample spacing.</summary>
    public static double[] EmaByArc(double[] values, double[] steps, double lambda)
    {
        var outv = new double[values.Length];
        if (values.Length == 0) return outv;
        outv[0] = values[0];
        for (int i = 1; i < values.Length; i++)
            outv[i] = EmaStep(outv[i - 1], values[i - 1], values[i], steps[i], lambda);
        return outv;
    }

    private static double EmaStep(double prevOut, double prevIn, double curIn, double d, double lambda)
    {
        if (lambda <= 0.0) return curIn;
        if (d <= MinStep) return prevOut; // the pen did not move: no arc to integrate over
        double decay = Math.Exp(-d / lambda);
        double slope = (curIn - prevIn) * lambda * (1.0 - decay) / d;
        return decay * prevOut + curIn - decay * prevIn - slope;
    }

    /// <summary>Holds each end's first/last samples up to the settled pressure just inside, so a light lift-off ends full.</summary>
    private static void HoldEndPressure(double[] p)
    {
        int n = p.Length;
        int w = Math.Min(CapHoldSamples, (n - 1) / 2);
        if (w < 1) return;
        double headFloor = p[w];
        for (int i = 0; i < w; i++) if (p[i] < headFloor) p[i] = headFloor;
        double tailFloor = p[n - 1 - w];
        for (int i = n - w; i < n; i++) if (p[i] < tailFloor) p[i] = tailFloor;
    }

    // ---- taper ------------------------------------------------------------

    /// <summary>Width multipliers in [taperMinFactor, 1]: full at the head, easing to the tip.</summary>
    public static double[] TaperFactors(double[] cum, double taperMinFactor)
    {
        int n = cum.Length;
        var outv = new double[n];
        Array.Fill(outv, 1.0);
        if (n < 2) return outv;
        double total = cum[n - 1];
        if (total < TaperMinLen) return outv;
        for (int i = 0; i < n; i++)
        {
            double edge = (total - cum[i]) / total;
            outv[i] = taperMinFactor + (1.0 - taperMinFactor) * LogisticEase(edge, TaperCurveK);
        }
        return outv;
    }

    // ---- calligraphy ------------------------------------------------------

    private static (double head, int holdUntil) HeadDirection(double[] sx, double[] sy, double[] cum, double headLen, bool finished)
    {
        int k = -1;
        for (int i = 0; i < cum.Length; i++)
            if (cum[i] >= headLen) { k = i; break; }
        if (k < 0) return (finished ? DotDirY : -1.0, cum.Length - 1);
        double dx = sx[k] - sx[0], dy = sy[k] - sy[0];
        double len = Hypot(dx, dy);
        return (len < MinTangentLen ? -1.0 : dy / len, k);
    }

    private static void CapTail(double[] dir, double[] cum, double window)
    {
        int n = dir.Length;
        double start = cum[n - 1] - window;
        int i = 1;
        while (i < n - 1 && cum[i] < start) i++;
        double ceiling = dir[i - 1];
        double span = cum[i] - cum[i - 1];
        if (start > cum[i - 1] && span > MinStep)
            ceiling += (dir[i] - dir[i - 1]) * (start - cum[i - 1]) / span;
        for (int j = i; j < n; j++)
        {
            if (dir[j] > ceiling) dir[j] = ceiling;
            else ceiling = dir[j];
        }
    }

    private static double[] NibDirection(
        double[] rx, double[] ry, double[] rcum, double[] ty, double[] steps,
        double openLen, double closeLen, double wait, double seed, int holdUntil)
    {
        int n = ty.Length;
        var outv = new double[n];
        var scratch = new double[6];
        double d = seed;
        for (int i = 0; i < n; i++)
        {
            if (i <= holdUntil) { outv[i] = seed; continue; }
            d = NibStep(d, ty[i], i == 0 ? 0.0 : steps[i], openLen, closeLen);
            d = Backfill(d, rx, ry, rcum, n, i, wait, scratch);
            outv[i] = d;
        }
        return outv;
    }

    private static double NibStep(double d, double target, double step, double openLen, double closeLen)
    {
        double rate = target > d ? openLen : closeLen;
        double limit = rate > 0.0 ? step * 2.0 / rate : double.MaxValue;
        return d + Math.Clamp(target - d, -limit, limit);
    }

    private static double Backfill(double d, double[] rx, double[] ry, double[] rcum, int n, int i, double wait, double[] scratch)
    {
        if (!ReversalAt(rx, ry, rcum, n, i, wait, scratch)) return d;
        double target = scratch[5];
        return target > d ? d + scratch[4] * (target - d) : d;
    }

    private static bool ReversalAt(double[] rx, double[] ry, double[] rcum, int n, int i, double wait, double[] scratch)
    {
        if (i < 1 || i + 1 >= n || wait <= 0.0 || rcum[n - 1] <= rcum[i + 1] + wait) return false;
        double c = TurnCos(rx, ry, rcum, n, i, wait, scratch);
        if (c > ReversalFromCos) return false;
        double inX = rx[i] - scratch[0], inY = ry[i] - scratch[1];
        if (c > TurnCos(rx, ry, rcum, n, i - 1, wait, scratch) || c >= TurnCos(rx, ry, rcum, n, i + 1, wait, scratch)) return false;
        int k = i + 1;
        while (k < n && rcum[k] <= rcum[i] + wait)
        {
            if ((rx[k] - rx[k - 1]) * inX + (ry[k] - ry[k - 1]) * inY > 0.0) return false;
            k++;
        }
        scratch[4] = Smoothstep(ReversalFromDeg, ReversalFullDeg, Math.Acos(Math.Clamp(c, -1.0, 1.0)) * 180.0 / Math.PI);
        scratch[5] = Math.Clamp((scratch[3]) / Hypot(scratch[2] - rx[i], scratch[3]), -1.0, 1.0);
        return scratch[4] > 0.0;
    }

    private static double TurnCos(double[] rx, double[] ry, double[] rcum, int n, int j, double wait, double[] scratch)
    {
        if (j <= 0 || j >= n - 1 || rcum[j] - wait < 0.0 || rcum[j] + wait > rcum[n - 1]) return 1.0;
        PointAt(rx, ry, rcum, n, rcum[j] - wait, scratch, 0);
        PointAt(rx, ry, rcum, n, rcum[j] + wait, scratch, 2);
        double ix = rx[j] - scratch[0], iy = ry[j] - scratch[1];
        double ox = scratch[2] - rx[j], oy = scratch[3] - ry[j];
        double li = Hypot(ix, iy), lo = Hypot(ox, oy);
        if (li < MinTangentLen || lo < MinTangentLen) return 1.0;
        return (ix * ox + iy * oy) / (li * lo);
    }

    private static void PointAt(double[] x, double[] y, double[] cum, int n, double s, double[] outv, int at)
    {
        int lo = 0, hi = n - 1;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (cum[mid] < s) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) { outv[at] = x[0]; outv[at + 1] = y[0]; return; }
        double span = cum[lo] - cum[lo - 1];
        double f = span > 0.0 ? (s - cum[lo - 1]) / span : 1.0;
        outv[at] = x[lo - 1] + (x[lo] - x[lo - 1]) * f;
        outv[at + 1] = y[lo - 1] + (y[lo] - y[lo - 1]) * f;
    }

    // ---- speed ------------------------------------------------------------

    /// <summary>Width factor per sample: raw travel per moving ms in a centred window, blurred along the path.</summary>
    public static double[] SpeedFactorsOver(double[] times, double[] steps, double[] path, int n, double strength, double top, double sigma)
    {
        var outv = new double[n];
        Array.Fill(outv, 1.0);
        if (strength <= 0.0 || n < 2) return outv;

        double floor = SpeedFloor * top;
        var tau = new double[n];
        var cum = new double[n];
        for (int i = 1; i < n; i++)
        {
            tau[i] = tau[i - 1] + MovingStep(times[i] - times[i - 1], steps[i], floor);
            cum[i] = cum[i - 1] + steps[i];
        }

        var f = new double[n];
        var cursor = new int[2];
        for (int i = 0; i < n; i++) f[i] = SpeedFactor(WindowSpeed(tau, cum, n, i, cursor), strength, top);

        var blurCursor = new int[1];
        for (int i = 0; i < n; i++) outv[i] = BlurAt(f, path, n, i, sigma, blurCursor);
        return outv;
    }

    private static double MovingStep(double dt, double step, double floor)
    {
        double t = Math.Max(dt, 0.0);
        return floor > 0.0 ? Math.Min(t, step / floor) : t;
    }

    private static double WindowSpeed(double[] tau, double[] cum, int n, int i, int[] cursor)
    {
        double total = tau[n - 1];
        if (total <= 0.0) return 0.0;
        double a = tau[i] - SpeedWindowMs;
        double b = tau[i] + SpeedWindowMs;
        if (a < 0.0) { b -= a; a = 0.0; }
        if (b > total) { a = Math.Max(a - (b - total), 0.0); b = total; }
        if (b <= a) return 0.0;
        return (ArcAt(tau, cum, n, b, cursor, 1) - ArcAt(tau, cum, n, a, cursor, 0)) / (b - a);
    }

    private static double ArcAt(double[] tau, double[] cum, int n, double x, int[] cursor, int end)
    {
        int k = cursor[end];
        while (k < n - 1 && tau[k] < x) k++;
        cursor[end] = k;
        if (k == 0 || tau[k] < x) return cum[k];
        double t0 = tau[k - 1];
        double span = tau[k] - t0;
        return span > 0.0 ? cum[k - 1] + (cum[k] - cum[k - 1]) * (x - t0) / span : cum[k];
    }

    /// <summary>The width factor a speed of v px/ms draws at.</summary>
    public static double SpeedFactor(double v, double strength, double top)
        => Math.Max(MinSpeedFactor, 1.0 - strength * Smoothstep(0.0, top, v));

    private static double BlurAt(double[] f, double[] path, int n, int i, double sigma, int[] cursor)
    {
        if (sigma <= 0.0) return f[i];
        double reach = SpeedBlurReach * sigma;
        double at = path[i];
        int lo = cursor[0];
        while (lo < i && path[lo] < at - reach) lo++;
        cursor[0] = lo;
        double inv = 1.0 / reach;
        double num = 0.0, den = 0.0;
        int j = lo;
        while (j < n)
        {
            double d = path[j] - at;
            if (d > reach) break;
            double own = (j > 0 ? path[j] - path[j - 1] : 0.0) + (j < n - 1 ? path[j + 1] - path[j] : 0.0);
            double u = d * inv;
            double q = 1.0 - u * u;
            double w = q * q * q * own;
            num += w * f[j];
            den += w;
            j++;
        }
        return den > 0.0 ? num / den : f[i];
    }

    private static double Hypot(double x, double y) => Math.Sqrt(x * x + y * y);
}
