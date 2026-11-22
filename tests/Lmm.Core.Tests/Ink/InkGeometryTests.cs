using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

public class InkGeometryTests
{
    [Fact]
    public void CornerSplit_Checkmark_SplitsAtTheVertex()
    {
        var p = TestPaths.Checkmark();
        int cornerIdx = TestPaths.CheckmarkCornerIndex();

        var segs = InkGeometry.CornerSplit(p);

        Assert.Equal(2, segs.Count);
        // The corner point is duplicated between the two segments (see InkGeometry.CornerSplit
        // remarks): last point of segment 1 and first point of segment 2 must both be
        // (approximately) the checkmark's vertex, and segment boundaries land near it.
        Assert.True(PointsClose(segs[0][^1], p[cornerIdx]));
        Assert.True(PointsClose(segs[1][0], p[cornerIdx]));
    }

    [Fact]
    public void CornerSplit_SmoothLoop_IsNotSplit()
    {
        // A continuously curving loop has no straight arms, so it must never
        // be mistaken for a corner — this was the actual bug the user's
        // reference images were pointing at (cursive handwriting turned jagged).
        var p = TestPaths.SmoothLoop();

        var segs = InkGeometry.CornerSplit(p);

        Assert.Single(segs);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(40)]
    public void CornerSplit_SmoothLoop_AtVariousTightness_IsNotSplit(double radius)
    {
        var p = TestPaths.SmoothLoop(radius: radius);
        var segs = InkGeometry.CornerSplit(p);
        Assert.Single(segs);
    }

    [Fact]
    public void CornerSplit_IsRobustToJitterNoise()
    {
        var baseline = TestPaths.Checkmark();
        int cornerIdx = TestPaths.CheckmarkCornerIndex();

        for (int seed = 0; seed < 10; seed++)
        {
            var jittered = TestPaths.WithJitter(baseline, amplitude: 0.6, seed);
            var segs = InkGeometry.CornerSplit(jittered);

            Assert.Equal(2, segs.Count);
            Assert.True(PointsClose(segs[0][^1], jittered[cornerIdx], tolerance: 3));
        }
    }

    [Fact]
    public void SimplifyDpIndices_StraightLine_CollapsesToEndpoints()
    {
        var p = new List<Pt>();
        for (int i = 0; i <= 50; i++) p.Add(new Pt(i, i * 0.5));

        var idx = InkGeometry.SimplifyDpIndices(p, eps: 1.0);

        Assert.Equal([0, 50], idx);
    }

    [Fact]
    public void ResamplePts_ProducesApproximatelyEqualSpacing()
    {
        var p = TestPaths.SmoothLoop(radius: 30, samples: 200);
        var rs = InkGeometry.ResamplePts(p, step: 4);

        for (int i = 1; i < rs.Count - 1; i++)
        {
            double d = Pt.Distance(rs[i], rs[i - 1]);
            Assert.InRange(d, 3.5, 4.5);
        }
    }

    private static bool PointsClose(Pt a, Pt b, double tolerance = 0.001) => Pt.Distance(a, b) <= tolerance;
}