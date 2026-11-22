namespace Lmm.Core.Ink;

/// <summary>
/// Legacy entry point kept for callers/tests: builds the closed ink polygon from
/// a beautified stroke. The geometry itself lives in <see cref="InkRibbon"/>, which
/// models a real nib (per-point radius, round caps, mitered joins) rather than the
/// old fixed-taper extrusion.
/// </summary>
public static class RibbonBuilder
{
    /// <summary>Ribbon polygon for a persisted stroke at a given canvas pixel size.</summary>
    public static List<Pt> BuildPolygon(BeautifiedStroke stroke, double canvasW, double canvasH, bool live = false)
    {
        var p = stroke.FractionalPoints.Select(pp => new Pt(pp.X * canvasW, pp.Y * canvasH)).ToList();
        return BuildPolygonPx(p, stroke.Width, live, stroke.WidthFactors);
    }

    /// <summary>Core geometry in pixel space.</summary>
    public static List<Pt> BuildPolygonPx(IReadOnlyList<Pt> p, double baseWidth, bool live, IReadOnlyList<double>? wf)
        => InkRibbon.Build(p, baseWidth, wf, live);
}
