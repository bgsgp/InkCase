using Lmm.Core.Ink;

namespace Lmm.Core.Tests.Ink;

public class InkStyleWidthTests
{
    [Fact]
    public void Defaults_AreHeavyEnoughForABigScreen()
    {
        // A 3.0 DIP pen was tuned for a phone; the classroom target is an 86" 4K
        // panel seen from the back row, so every tool starts thicker than xnotes.
        Assert.True(InkStyle.For(InkTool.Pen).BaseWidth >= 5.0);
        Assert.True(InkStyle.For(InkTool.Dashed).BaseWidth >= 5.0);
        Assert.True(InkStyle.For(InkTool.Calligraphy).BaseWidth >= 9.0);
        Assert.True(InkStyle.For(InkTool.Speed).BaseWidth >= 6.0);
        Assert.True(InkStyle.For(InkTool.Taper).BaseWidth >= 6.0);
        Assert.True(InkStyle.For(InkTool.Highlighter).BaseWidth >= 28.0);
    }

    [Fact]
    public void ClampWidth_KeepsFineToolsInRange()
    {
        Assert.Equal(1.0, InkStyle.ClampWidth(InkTool.Pen, 0.1));
        Assert.Equal(40.0, InkStyle.ClampWidth(InkTool.Pen, 999));
        Assert.Equal(12.0, InkStyle.ClampWidth(InkTool.Pen, 12.0));
    }

    [Fact]
    public void ClampWidth_AllowsMuchWiderHighlighter()
    {
        Assert.Equal(6.0, InkStyle.ClampWidth(InkTool.Highlighter, 1.0));
        Assert.Equal(120.0, InkStyle.ClampWidth(InkTool.Highlighter, 500));
        Assert.True(InkStyle.WidthRange(InkTool.Highlighter).Max > InkStyle.WidthRange(InkTool.Pen).Max);
    }

    [Fact]
    public void BaseWidth_ChangesRibbonThickness()
    {
        var pts = Enumerable.Range(0, 40)
            .Select(i => new Pt(0.1 + i * 0.02, 0.5 + Math.Sin(i * 0.2) * 0.1))
            .ToList();

        var thin = InkEngine.Build(pts, null, null, InkStyle.For(InkTool.Pen) with { BaseWidth = 2.0 });
        var thick = InkEngine.Build(pts, null, null, InkStyle.For(InkTool.Pen) with { BaseWidth = 12.0 });

        var thinMax = thin.HalfWidth.Max();
        var thickMax = thick.HalfWidth.Max();
        Assert.True(thickMax > thinMax * 3, $"thick {thickMax} should be much wider than thin {thinMax}");
    }
}
