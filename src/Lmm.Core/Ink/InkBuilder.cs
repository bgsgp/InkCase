using System;
using System.Collections.Generic;

namespace Lmm.Core.Ink;

/// <summary>One captured stroke before it has been beautified: fraction coordinates plus raw stylus channels.</summary>
public sealed record RawStroke(
    string Color,
    double Width,
    IReadOnlyList<Pt> FractionalPoints,
    IReadOnlyList<double>? Pressures = null,
    IReadOnlyList<double>? Times = null);

/// <summary>
/// A stroke ready to persist: the smoothed centreline in fraction coordinates and
/// one width multiplier per point. The multipliers are `2·halfWidth / baseWidth`,
/// so the renderer only needs the base width and the factors to redraw the ribbon.
/// </summary>
public sealed record BuiltStroke(
    string Color,
    string ToolId,
    double BaseWidth,
    IReadOnlyList<Pt> FractionalPoints,
    IReadOnlyList<double> WidthFactors);

/// <summary>
/// Entry point for new ink: runs <see cref="InkEngine"/> over the captured
/// samples and hands back the geometry to store. This replaces the old
/// three-branch "beautifier" (circle detector, straightener, Catmull-Rom fit);
/// the tool's own width model now does the shaping, which is the whole point of
/// porting xnotes rather than tuning the old heuristic.
/// </summary>
public static class InkBuilder
{
    /// <summary>Minimum travel (px) before a stroke is worth keeping.</summary>
    public const double MinLength = 1.5;

    /// <summary>Keeps stored strokes small without changing the drawn shape.</summary>
    private const double MinPointSpacing = 0.7;

    /// <summary>
    /// Beautifies one stroke. `live` builds the in-progress preview (the engine
    /// holds off its lift-time rules); the committed stroke passes false so the
    /// taper and cap-tail apply. Returns null for a stroke with no length.
    /// </summary>
    public static BuiltStroke? Build(
        RawStroke raw, InkStyle style, double canvasWidth, double canvasHeight, bool live = false)
    {
        if (canvasWidth <= 1 || canvasHeight <= 1) return null;
        int n = raw.FractionalPoints.Count;
        if (n < 2) return null;

        var px = new Pt[n];
        for (int i = 0; i < n; i++)
            px[i] = new Pt(raw.FractionalPoints[i].X * canvasWidth, raw.FractionalPoints[i].Y * canvasHeight);

        double travel = 0.0;
        for (int i = 1; i < n; i++)
            travel += Math.Sqrt((px[i].X - px[i - 1].X) * (px[i].X - px[i - 1].X) + (px[i].Y - px[i - 1].Y) * (px[i].Y - px[i - 1].Y));
        if (travel < MinLength) return null;

        var geometry = InkEngine.Build(px, raw.Pressures, raw.Times, style, finished: !live);
        if (geometry.Count < 2) return null;

        double baseWidth = Math.Max(0.5, style.BaseWidth);
        var points = new List<Pt>(geometry.Count);
        var factors = new List<double>(geometry.Count);

        // Downsample: adjacent engine points can be a fraction of a pixel apart once
        // the input has been low-passed, and they add nothing but file size.
        int lastKept = -1;
        for (int i = 0; i < geometry.Count; i++)
        {
            if (lastKept >= 0)
            {
                double dx = geometry.X[i] - geometry.X[lastKept];
                double dy = geometry.Y[i] - geometry.Y[lastKept];
                if (Math.Sqrt(dx * dx + dy * dy) < MinPointSpacing && i != geometry.Count - 1) continue;
            }

            points.Add(new Pt(geometry.X[i] / canvasWidth, geometry.Y[i] / canvasHeight));
            factors.Add(2.0 * geometry.HalfWidth[i] / baseWidth);
            lastKept = i;
        }

        if (points.Count < 2) return null;
        return new BuiltStroke(raw.Color, style.Id, baseWidth, points, factors);
    }
}
