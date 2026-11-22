using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

public class InkRibbonTests
{
    [Fact]
    public void Build_SinglePoint_ProducesARoundDot()
    {
        var polygon = InkRibbon.Build([new Pt(50, 50)], baseWidth: 4, widthFactors: null, live: false);

        Assert.True(polygon.Count >= 12);
        Assert.All(polygon, p =>
            Assert.InRange(Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50)), 0.5, 4.0));
    }

    [Fact]
    public void Build_WidthFactors_MakeTheStrokeVaryInThickness()
    {
        // Straight line, thin at the start, broad in the middle, thin at the end.
        var line = Enumerable.Range(0, 40).Select(i => new Pt(i * 5, 100)).ToList();
        var wf = line.Select((_, i) => i < 5 || i > 34 ? 0.6 : 1.3).ToList();

        var polygon = InkRibbon.Build(line, baseWidth: 6, wf, live: false);

        double maxY = polygon.Max(p => p.Y), minY = polygon.Min(p => p.Y);
        Assert.True(maxY - minY > 6, $"expected >6px thickness variation, got {maxY - minY}");
        Assert.All(polygon, p => Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y)));
    }

    [Fact]
    public void Build_DuplicatedPoints_DoNotProduceNaN()
    {
        var p = new List<Pt> { new(0, 0), new(10, 0), new(10, 0), new(10, 0), new(20, 5) };

        var polygon = InkRibbon.Build(p, baseWidth: 3, widthFactors: null, live: false);

        Assert.True(polygon.Count > 3);
        Assert.All(polygon, pt => Assert.False(double.IsNaN(pt.X) || double.IsNaN(pt.Y)));
    }

    [Fact]
    public void FromCapture_SlowSegmentIsWiderThanFastSegment()
    {
        // 21 samples, equal 4px spacing: the first half is drawn slowly (4 ms per
        // sample), the second half is a fast flick (1 ms per sample). Slow nib
        // travel must render wider than fast travel.
        var pts = Enumerable.Range(0, 21).Select(i => new Pt(i * 4, 0)).ToList();
        var times = new List<double>();
        double t = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            t += i <= 10 ? 4 : 1;
            times.Add(t);
        }

        var wf = InkWidths.FromCapture(pts, null, times, pts);

        Assert.Equal(pts.Count, wf.Count);
        Assert.True(wf[2] > wf[18], $"slow segment should be wider: {wf[2]:F2} vs {wf[18]:F2}");
        Assert.All(wf, v => Assert.InRange(v, 0.5, 1.4));
    }
}
