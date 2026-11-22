using System;
using System.Linq;
using Lmm.Core.Ink;
using Xunit;

namespace Lmm.Core.Tests.Ink;

public sealed class InkRibbonTests
{
    private static Pt[] Line(int count, double dx = 3.0)
        => Enumerable.Range(0, count).Select(i => new Pt(i * dx, 0)).ToArray();

    [Fact]
    public void Outline_IsClosedAndWiderThanTheCentreline()
    {
        var pts = Line(30);
        var half = Enumerable.Repeat(2.0, 30).ToArray();

        var polygon = InkRibbon.Build(pts, half);

        Assert.True(polygon.Count > pts.Length * 2, "the round caps add vertices on top of the two rails");
        double minY = polygon.Min(p => p.Y), maxY = polygon.Max(p => p.Y);
        Assert.Equal(4.0, maxY - minY, 1);
    }

    [Fact]
    public void Outline_FollowsThePerPointHalfWidth()
    {
        var pts = Line(40);
        var half = Enumerable.Range(0, 40).Select(i => i < 20 ? 0.7 : 4.0).ToArray();

        var polygon = InkRibbon.Build(pts, half);

        double early = polygon.Where(p => p.X < 20).Max(p => Math.Abs(p.Y));
        double late = polygon.Where(p => p.X > 90).Max(p => Math.Abs(p.Y));
        Assert.True(late > early + 2.0, $"the ribbon must thin out: {early:F2} -> {late:F2}");
    }

    [Fact]
    public void SinglePoint_BecomesACircle()
    {
        var polygon = InkRibbon.Build([new Pt(50, 50)], [3.0]);

        Assert.True(polygon.Count >= 12);
        Assert.All(polygon, p => Assert.Equal(3.0, Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50)), 2));
    }

    [Fact]
    public void Dashes_AreSplitIntoMultipleShapes()
    {
        var pts = Line(60, 5.0); // 295 px long
        var half = Enumerable.Repeat(1.5, 60).ToArray();

        var dashes = InkRibbon.BuildDashed(pts, half, 10, 8);

        Assert.True(dashes.Count >= 10, $"expected a dash every 18 px, got {dashes.Count}");
        Assert.All(dashes, d => Assert.True(d.Count >= 3));
    }
}
