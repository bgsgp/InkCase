using System;
using System.Collections.Generic;
using System.Linq;
using Lmm.Core.Ink;
using Xunit;

namespace Lmm.Core.Tests.Ink;

public sealed class InkBuilderTests
{
    private static RawStroke Stroke(int count, double width = 3.0)
    {
        var pts = Enumerable.Range(0, count).Select(i => new Pt(i * 3.0 / 600.0, 0.5)).ToList();
        return new RawStroke("#D33A2C", width, pts, Enumerable.Repeat(0.6, count).ToList(),
            Enumerable.Range(0, count).Select(i => i * 6.0).ToList());
    }

    [Fact]
    public void Build_ProducesCentrelineAndFactorsOfEqualLength()
    {
        var built = InkBuilder.Build(Stroke(80), InkStyle.For(InkTool.Pen), 600, 400);

        Assert.NotNull(built);
        Assert.Equal("pen", built!.ToolId);
        Assert.Equal(built.FractionalPoints.Count, built.WidthFactors.Count);
        Assert.True(built.FractionalPoints.Count >= 2);
        Assert.All(built.FractionalPoints, p => Assert.InRange(p.Y, 0.0, 1.0));
    }

    [Fact]
    public void Build_RejectsATapWithNoLength()
    {
        var pts = new List<Pt> { new(0.5, 0.5), new(0.5002, 0.5002) };
        var tap = new RawStroke("#D33A2C", 3, pts);

        Assert.Null(InkBuilder.Build(tap, InkStyle.For(InkTool.Pen), 600, 400));
    }

    [Fact]
    public void Build_TaperStrokeEndsThinnerThanItStarts()
    {
        var built = InkBuilder.Build(Stroke(120), InkStyle.For(InkTool.Taper), 600, 400);

        Assert.NotNull(built);
        var factors = built!.WidthFactors;
        Assert.True(factors[^1] < factors[factors.Count / 2]);
    }

    [Theory]
    [InlineData("pen")]
    [InlineData("dashed")]
    [InlineData("calligraphy")]
    [InlineData("speed")]
    [InlineData("taper")]
    [InlineData("highlighter")]
    public void Build_WorksForEveryTool(string id)
    {
        var built = InkBuilder.Build(Stroke(80), InkStyle.For(InkStyle.ParseTool(id)), 600, 400);

        Assert.NotNull(built);
        Assert.Equal(id, built!.ToolId);
        Assert.All(built.WidthFactors, f => Assert.True(double.IsFinite(f) && f > 0 && f < 6));
    }
}
