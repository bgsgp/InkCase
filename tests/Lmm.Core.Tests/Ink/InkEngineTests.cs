using System;
using System.Collections.Generic;
using System.Linq;
using Lmm.Core.Ink;
using Xunit;

namespace Lmm.Core.Tests.Ink;

/// <summary>
/// Locks down the six pens' width models. The point of porting xnotes was that
/// each tool answers "how wide here?" differently, so each test asserts the one
/// behaviour that distinguishes its tool — not the exact pixel values, which are
/// free to move with the constants.
/// </summary>
public sealed class InkEngineTests
{
    private static Pt[] Line(int count, double dx = 2.0)
        => Enumerable.Range(0, count).Select(i => new Pt(i * dx, 0)).ToArray();

    private static double[] Uniform(int count, double value)
        => Enumerable.Repeat(value, count).ToArray();

    private static double[] Times(int count, double stepMs)
        => Enumerable.Range(0, count).Select(i => i * stepMs).ToArray();

    [Fact]
    public void Pen_PressureWidensTheStroke()
    {
        var pts = Line(60);
        var style = InkStyle.For(InkTool.Pen);

        var light = InkEngine.Build(pts, Uniform(60, 0.1), Times(60, 8), style);
        var heavy = InkEngine.Build(pts, Uniform(60, 0.9), Times(60, 8), style);

        Assert.True(heavy.HalfWidth[30] > light.HalfWidth[30] * 1.3,
            $"heavy {heavy.HalfWidth[30]:F2} should be clearly wider than light {light.HalfWidth[30]:F2}");
    }

    [Fact]
    public void Pen_HoldsEndPressureSoTheEndIsNotAPinch()
    {
        var pts = Line(60);
        var style = InkStyle.For(InkTool.Pen);
        var pressure = Uniform(60, 0.9);
        pressure[0] = 0.05; // pen touched down lightly and settled

        var g = InkEngine.Build(pts, pressure, Times(60, 8), style);

        Assert.True(g.HalfWidth[0] > g.HalfWidth[1] * 0.6,
            "the opening sample must be held up to the settled pressure, not drawn at the raw 0.05");
    }

    [Fact]
    public void Taper_FadesTowardsTheTip()
    {
        var pts = Line(80);
        var style = InkStyle.For(InkTool.Taper);
        var g = InkEngine.Build(pts, Uniform(80, 0.8), Times(80, 8), style);

        Assert.True(g.HalfWidth[^1] < g.HalfWidth[40] * 0.8,
            $"tip {g.HalfWidth[^1]:F2} should be thinner than the body {g.HalfWidth[40]:F2}");
    }

    [Fact]
    public void Calligraphy_HorizontalIsThinVerticalIsThick()
    {
        var style = InkStyle.For(InkTool.Calligraphy);
        var pressure = Uniform(40, 0.7);
        var times = Times(40, 8);

        var horizontal = InkEngine.Build(Line(40), pressure, times, style);
        var vertical = InkEngine.Build(
            Enumerable.Range(0, 40).Select(i => new Pt(0, i * 2.0)).ToArray(), pressure, times, style);

        Assert.True(vertical.HalfWidth[20] > horizontal.HalfWidth[20] * 1.3,
            $"a vertical drag (nib perpendicular to travel) should be wider than a horizontal one: " +
            $"v {vertical.HalfWidth[20]:F2} vs h {horizontal.HalfWidth[20]:F2}");
    }

    [Fact]
    public void Speed_SlowTravelIsWiderThanFastTravel()
    {
        var style = InkStyle.For(InkTool.Speed);
        var pts = Line(80);
        var pressure = Uniform(80, 0.8);

        var slow = InkEngine.Build(pts, pressure, Times(80, 40), style);
        var fast = InkEngine.Build(pts, pressure, Times(80, 1), style);

        Assert.True(slow.HalfWidth[40] > fast.HalfWidth[40] * 1.2,
            $"slow {slow.HalfWidth[40]:F2} vs fast {fast.HalfWidth[40]:F2}");
    }

    [Fact]
    public void Speed_FastTravelStaysLegible()
    {
        // Writing quickly must thin the line, not erase it: the factor is floored.
        Assert.True(InkEngine.MinSpeedFactor >= 0.45,
            $"speed factor floor {InkEngine.MinSpeedFactor} is too thin for a back row");
        Assert.Equal(InkEngine.MinSpeedFactor,
            InkEngine.SpeedFactor(InkEngine.SpeedHi * 5, 1.0, InkEngine.SpeedHi), 6);

        var style = InkStyle.For(InkTool.Speed);
        var fast = InkEngine.Build(Line(80), Uniform(80, 0.8), Times(80, 1), style);
        Assert.True(fast.HalfWidth[40] * 2 > style.BaseWidth * 0.45,
            $"a quick stroke drew only {fast.HalfWidth[40] * 2:F2}px wide");
    }

    [Fact]
    public void Highlighter_IgnoresPressureAndStaysWide()
    {
        var pts = Line(40);
        var style = InkStyle.For(InkTool.Highlighter);
        var pressure = Uniform(40, 0.15);
        pressure[20] = 0.95;

        var g = InkEngine.Build(pts, pressure, Times(40, 8), style);

        Assert.Equal(g.HalfWidth[0], g.HalfWidth[20], 3);
        Assert.True(g.HalfWidth[20] > style.BaseWidth * 0.4);
        Assert.True(style.AlphaScale < 0.6, "a highlighter has to be translucent or it hides the text");
    }

    [Fact]
    public void SinglePoint_BecomesADotWithTheToolsWidth()
    {
        var style = InkStyle.For(InkTool.Pen);
        var g = InkEngine.Build([new Pt(10, 10)], [0.8], [0], style);

        Assert.Equal(1, g.Count);
        Assert.True(g.HalfWidth[0] > 0.5);
    }

    [Fact]
    public void RepeatedPoints_ProduceNoNaN()
    {
        var pts = Enumerable.Repeat(new Pt(5, 5), 20).ToArray();
        var g = InkEngine.Build(pts, Uniform(20, 0.5), Times(20, 5), InkStyle.For(InkTool.Calligraphy));

        Assert.All(g.HalfWidth, h => Assert.True(double.IsFinite(h) && h > 0));
        Assert.All(g.X, x => Assert.True(double.IsFinite(x)));
    }
}
