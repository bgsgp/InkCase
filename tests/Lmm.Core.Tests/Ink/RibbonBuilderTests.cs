using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

public class RibbonBuilderTests
{
    [Fact]
    public void BuildPolygonPx_ReturnsClosedOutlineWithTwicePointCount()
    {
        var p = TestPaths.SmoothLoop(radius: 20, samples: 30);
        var wf = Enumerable.Repeat(1.0, p.Count).ToList();

        var polygon = RibbonBuilder.BuildPolygonPx(p, baseWidth: 3.0, live: false, wf);

        // Two rails plus a rounded cap arc at each end, so strictly more
        // vertices than the plain 2*n extrusion.
        Assert.True(polygon.Count > p.Count * 2, $"got {polygon.Count}");
        Assert.All(polygon, pt => Assert.False(double.IsNaN(pt.X) || double.IsNaN(pt.Y)));
    }

    [Fact]
    public void BuildPolygonPx_DoesNotThrow_WhenCornerPointIsDuplicated()
    {
        // Mirrors how StrokeBeautifier concatenates cornerSplit segments
        // WITHOUT deduplicating the shared corner point (see its remarks):
        // the same point appears twice in a row. The normal-vector
        // computation must not divide by zero or blow up on this.
        var p = new List<Pt>();
        var arm1 = TestPaths.Checkmark(10).Take(11).ToList(); // up to and including the corner
        var arm2 = TestPaths.Checkmark(10).Skip(10).ToList(); // starting from the corner again
        p.AddRange(arm1);
        p.AddRange(arm2); // corner point appears twice in a row here

        var polygon = RibbonBuilder.BuildPolygonPx(p, baseWidth: 3.0, live: false, wf: null);

        Assert.True(polygon.Count > p.Count * 2, $"got {polygon.Count}");
        Assert.All(polygon, pt =>
        {
            Assert.False(double.IsNaN(pt.X));
            Assert.False(double.IsNaN(pt.Y));
        });
    }

    [Fact]
    public void BuildPolygon_FromBeautifiedStroke_RoundTripsThroughFractionalCoordinates()
    {
        const double canvasW = 300, canvasH = 300;
        var pixelPts = TestPaths.Checkmark();
        var fractional = TestPaths.ToFractional(pixelPts, canvasW, canvasH);
        var raw = new RawStroke("#D33A2C", 3.0, fractional);

        var beautified = StrokeBeautifier.Beautify(raw, canvasW, canvasH);
        Assert.NotNull(beautified);

        var polygon = RibbonBuilder.BuildPolygon(beautified!, canvasW, canvasH);

        Assert.True(polygon.Count > beautified!.FractionalPoints.Count * 2);
        Assert.All(polygon, pt => Assert.InRange(pt.X, -50, canvasW + 50));
    }
}
