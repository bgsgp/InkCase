using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

/// <summary>
/// Synthetic point-path generators mirroring the test shapes used to validate
/// the original JS ink-beautify algorithm this session (checkmark/X/arrow vs.
/// cursive loops and multi-letter signatures): dense point sampling (a few
/// pixels apart) like a real pointer-capture stream, not hand-picked coordinates.
/// </summary>
internal static class TestPaths
{
    /// <summary>
    /// A checkmark: a short stroke descending-right, then a long stroke
    /// ascending-right, meeting at one sharp vertex. Each arm is straight and
    /// comfortably longer than the corner-detection arm threshold (26px max).
    /// </summary>
    public static List<Pt> Checkmark(int pointsPerArm = 20)
    {
        var pts = new List<Pt>();
        var start = new Pt(0, 0);
        var corner = new Pt(15, 20);
        var end = new Pt(55, -30);

        for (int i = 0; i <= pointsPerArm; i++)
        {
            double t = (double)i / pointsPerArm;
            pts.Add(new Pt(start.X + (corner.X - start.X) * t, start.Y + (corner.Y - start.Y) * t));
        }
        for (int i = 1; i <= pointsPerArm; i++)
        {
            double t = (double)i / pointsPerArm;
            pts.Add(new Pt(corner.X + (end.X - corner.X) * t, corner.Y + (end.Y - corner.Y) * t));
        }
        return pts;
    }

    /// <summary>Index of the Checkmark's true corner vertex in the point list above.</summary>
    public static int CheckmarkCornerIndex(int pointsPerArm = 20) => pointsPerArm;

    /// <summary>
    /// A smooth, continuously curving loop (approximated circle/trochoid) with
    /// no straight segments anywhere — the shape a cursive signature's loops
    /// actually have. `tightness` scales the radius (smaller = tighter loop).
    /// </summary>
    public static List<Pt> SmoothLoop(double radius = 25, int samples = 80)
    {
        var pts = new List<Pt>();
        for (int i = 0; i <= samples; i++)
        {
            double a = i / (double)samples * Math.PI * 2;
            pts.Add(new Pt(radius * Math.Cos(a), radius * Math.Sin(a)));
        }
        return pts;
    }

    /// <summary>Adds small deterministic jitter noise to a path, like real pointer sampling noise.</summary>
    public static List<Pt> WithJitter(IReadOnlyList<Pt> p, double amplitude, int seed)
    {
        var rnd = new Random(seed);
        var outPts = new List<Pt>(p.Count);
        foreach (var pt in p)
        {
            double jx = (rnd.NextDouble() * 2 - 1) * amplitude;
            double jy = (rnd.NextDouble() * 2 - 1) * amplitude;
            outPts.Add(new Pt(pt.X + jx, pt.Y + jy));
        }
        return outPts;
    }

    /// <summary>Converts a pixel-space path to fractional (0..1) coordinates for a given canvas size.</summary>
    public static List<Pt> ToFractional(IReadOnlyList<Pt> pixelPts, double canvasW, double canvasH, double originX = 100, double originY = 100)
    {
        var outPts = new List<Pt>(pixelPts.Count);
        foreach (var p in pixelPts)
            outPts.Add(new Pt((p.X + originX) / canvasW, (p.Y + originY) / canvasH));
        return outPts;
    }
}
