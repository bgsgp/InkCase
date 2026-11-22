using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

public class StrokeBeautifierTests
{
    private const double CanvasW = 300, CanvasH = 300;

    [Fact]
    public void Beautify_Checkmark_ProducesAStroke_NotCollapsedToAStraightLine()
    {
        var pixelPts = TestPaths.Checkmark(pointsPerArm: 25);
        var fractional = TestPaths.ToFractional(pixelPts, CanvasW, CanvasH);
        var raw = new RawStroke("#D33A2C", 3.0, fractional);

        var result = StrokeBeautifier.Beautify(raw, CanvasW, CanvasH);

        Assert.NotNull(result);
        // A checkmark's two arms meet at a real angle; if it had been
        // (mis-)classified as "near straight" and ruler-straightened, the
        // result would hug the start->end chord far more tightly than this.
        var pts = result!.FractionalPoints.Select(p => new Pt(p.X * CanvasW, p.Y * CanvasH)).ToList();
        var start = pts[0];
        var end = pts[^1];
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double l2 = dx * dx + dy * dy;
        double maxDev = 0;
        foreach (var p in pts)
        {
            double t = Math.Clamp(((p.X - start.X) * dx + (p.Y - start.Y) * dy) / l2, 0, 1);
            double vx = p.X - (start.X + t * dx), vy = p.Y - (start.Y + t * dy);
            maxDev = Math.Max(maxDev, Math.Sqrt(vx * vx + vy * vy));
        }
        Assert.True(maxDev > 5, $"expected a visible corner deviation from the chord, got {maxDev}");
    }

    [Fact]
    public void Beautify_ClosedLoop_BecomesARegularEllipse()
    {
        var pixelPts = TestPaths.SmoothLoop(radius: 40, samples: 80);
        // Close the loop exactly (start == end) so chord ~= 0, matching a real circled annotation.
        pixelPts.Add(pixelPts[0]);
        var fractional = TestPaths.ToFractional(pixelPts, CanvasW, CanvasH);
        var raw = new RawStroke("#D33A2C", 3.0, fractional);

        var result = StrokeBeautifier.Beautify(raw, CanvasW, CanvasH);

        Assert.NotNull(result);
        // Ellipse branch resamples to 49 ring points at step 4 then smooths -> expect a
        // substantially denser, very round point set rather than the sparse corner-split output.
        Assert.True(result!.FractionalPoints.Count > 20);
    }

    [Fact]
    public void Beautify_NearStraightLine_IsStraightened()
    {
        var pixelPts = new List<Pt>();
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            // tiny hand-tremor deviation, well under the straighten threshold
            pixelPts.Add(new Pt(t * 80, t * 2 + Math.Sin(i * 0.7) * 1.0));
        }
        var fractional = TestPaths.ToFractional(pixelPts, CanvasW, CanvasH);
        var raw = new RawStroke("#1f2328", 2.5, fractional);

        var result = StrokeBeautifier.Beautify(raw, CanvasW, CanvasH);

        Assert.NotNull(result);
        var pts = result!.FractionalPoints.Select(p => new Pt(p.X * CanvasW, p.Y * CanvasH)).ToList();
        var start = pts[0];
        var end = pts[^1];
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double l2 = dx * dx + dy * dy;
        double maxDev = 0;
        foreach (var p in pts)
        {
            double t = Math.Clamp(((p.X - start.X) * dx + (p.Y - start.Y) * dy) / l2, 0, 1);
            double vx = p.X - (start.X + t * dx), vy = p.Y - (start.Y + t * dy);
            maxDev = Math.Max(maxDev, Math.Sqrt(vx * vx + vy * vy));
        }
        Assert.True(maxDev < 2.0, $"expected the straighten branch to flatten most deviation, got {maxDev}");
    }

    [Fact]
    public void Beautify_TooShortStroke_ReturnsNull()
    {
        var fractional = TestPaths.ToFractional([new Pt(0, 0), new Pt(1, 1)], CanvasW, CanvasH);
        var raw = new RawStroke("#000", 2.0, fractional);

        Assert.Null(StrokeBeautifier.Beautify(raw, CanvasW, CanvasH));
    }
}
