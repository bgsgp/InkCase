using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lmm.Core.Ink;
using Lmm.Core.Model;
using Pt = Lmm.Core.Ink.Pt;

namespace ExamReview.App.Controls;

/// <summary>
/// Freehand annotation layer. Per the project's rendering constraint it is a
/// <see cref="FrameworkElement"/> that draws every finished stroke as a filled
/// ribbon polygon in <see cref="OnRender"/> — no per-stroke Path elements.
///
/// The nib model is xnotes': <see cref="InkStyle"/> picks one of six tools, and
/// <see cref="InkEngine"/> derives the per-point half-width (pressure, nib
/// direction, speed or taper depending on the tool). Live preview runs the very
/// same engine, so what is drawn while the pen is down is what gets committed.
/// </summary>
public sealed class InkCanvasElement : FrameworkElement
{
    private readonly List<InkStroke> _strokes = [];
    private readonly List<Pt> _livePx = [];
    private readonly List<double> _livePressure = [];
    private readonly List<double> _liveTime = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private List<IReadOnlyList<Pt>> _livePolygons = [];
    private bool _inkEnabled = true;
    private InkStyle _style = InkStyle.For(InkTool.Pen);

    public InkCanvasElement()
    {
        Cursor = Cursors.Pen;
    }

    public event EventHandler<InkStroke>? StrokeFinished;

    public Color InkColor { get; set; } = Color.FromRgb(0xD3, 0x3A, 0x2C);

    /// <summary>The active pen (tool + width + pressure model).</summary>
    public InkStyle Pen
    {
        get => _style;
        set
        {
            _style = value ?? InkStyle.For(InkTool.Pen);
            InvalidateVisual();
        }
    }

    public double BaseWidth
    {
        get => _style.BaseWidth;
        set => _style = _style with { BaseWidth = value };
    }

    public string? SectionId { get; set; }

    public bool InkEnabled
    {
        get => _inkEnabled;
        set
        {
            _inkEnabled = value;
            Cursor = value ? Cursors.Pen : Cursors.Arrow;
        }
    }

    public IReadOnlyList<InkStroke> Strokes => _strokes;

    public void SetStrokes(IEnumerable<InkStroke> strokes)
    {
        _strokes.Clear();
        _strokes.AddRange(strokes);
        InvalidateVisual();
    }

    public void ClearInk()
    {
        _strokes.Clear();
        ResetLive();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
        return new Size(width, height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent fill so the element is hit-testable without stealing paint.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        foreach (var stroke in _strokes)
            DrawStroke(dc, stroke);

        if (_livePolygons.Count == 0) return;

        var liveBrush = new SolidColorBrush(InkColor);
        liveBrush.Freeze();
        dc.PushOpacity(_style.AlphaScale);
        foreach (var polygon in _livePolygons)
            dc.DrawGeometry(liveBrush, null, ToGeometry(polygon));
        dc.Pop();
    }

    private void DrawStroke(DrawingContext dc, InkStroke stroke)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1 || stroke.Points.Count == 0) return;

        var style = InkStyle.For(stroke.Tool, stroke.BaseWidth);
        var center = new Pt[stroke.Points.Count];
        var halfWidth = new double[stroke.Points.Count];
        for (int i = 0; i < center.Length; i++)
        {
            center[i] = new Pt(stroke.Points[i].X * width, stroke.Points[i].Y * height);
            double factor = stroke.WidthFactors is { } factors && i < factors.Count && factors[i] > 0 ? factors[i] : 1.0;
            halfWidth[i] = Math.Max(0.2, stroke.BaseWidth * 0.5 * factor);
        }

        var brush = new SolidColorBrush(ParseColor(stroke.Color));
        brush.Freeze();

        // xnotes fades the highlighter in its paint pass; with source-over only,
        // element opacity is the honest equivalent (strokes never overlap themselves).
        dc.PushOpacity(style.AlphaScale);

        if (style.Tool == InkTool.Dashed)
        {
            foreach (var polygon in InkRibbon.BuildDashed(center, halfWidth, style.DashLength, style.DashGap))
                if (polygon.Count >= 3)
                    dc.DrawGeometry(brush, null, ToGeometry(polygon));
        }
        else
        {
            var polygon = InkRibbon.Build(center, halfWidth);
            if (polygon.Count >= 3)
                dc.DrawGeometry(brush, null, ToGeometry(polygon));
        }

        dc.Pop();
    }

    private static StreamGeometry ToGeometry(IReadOnlyList<Pt> polygon)
    {
        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true, true);
            ctx.PolyLineTo(polygon.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, false);
        }
        geometry.Freeze();
        return geometry;
    }

    // ---- input ------------------------------------------------------------

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        if (!_inkEnabled || string.IsNullOrEmpty(SectionId))
        {
            base.OnStylusDown(e);
            return;
        }

        CaptureStylus();
        BeginStroke(e.GetStylusPoints(this));
        e.Handled = true;
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        if (!_inkEnabled || !IsStylusCaptured) return;

        AppendStylusPoints(e.GetStylusPoints(this));
        e.Handled = true;
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        if (!IsStylusCaptured) return;

        AppendStylusPoints(e.GetStylusPoints(this));
        ReleaseStylusCapture();
        e.Handled = true;
        CommitStroke();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        // Mouse input is the fallback for a mouse/touchpad; real pen input is
        // promoted from the stylus handlers above, so skip duplicates.
        if (e.StylusDevice is not null || !_inkEnabled || string.IsNullOrEmpty(SectionId))
        {
            base.OnMouseLeftButtonDown(e);
            return;
        }

        CaptureMouse();
        var p = e.GetPosition(this);
        BeginStroke([new StylusPoint(p.X, p.Y, 0.5f)]);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.StylusDevice is not null) return;
        if (!_inkEnabled || !IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

        var p = e.GetPosition(this);
        if (_livePx.Count > 0)
        {
            var last = _livePx[^1];
            if (Math.Abs(p.X - last.X) < 1.0 && Math.Abs(p.Y - last.Y) < 1.0) return;
        }

        AppendPoint(p.X, p.Y, 0.5);
        RefreshPreview();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (e.StylusDevice is not null || !IsMouseCaptured) return;

        ReleaseMouseCapture();
        e.Handled = true;
        CommitStroke();
    }

    private void BeginStroke(StylusPointCollection points)
    {
        ResetLive();
        AppendStylusPoints(points);
    }

    private void AppendStylusPoints(StylusPointCollection points)
    {
        foreach (var sp in points)
            AppendPoint(sp.X, sp.Y, sp.PressureFactor);

        RefreshPreview();
    }

    private void AppendPoint(double x, double y, double pressure)
    {
        if (_livePx.Count > 0)
        {
            var last = _livePx[^1];
            if (Math.Abs(x - last.X) < 0.6 && Math.Abs(y - last.Y) < 0.6) return;
        }

        _livePx.Add(new Pt(x, y));
        _livePressure.Add(double.IsFinite(pressure) ? Math.Clamp(pressure, 0, 1) : 0.5);
        _liveTime.Add(_clock.Elapsed.TotalMilliseconds);
    }

    private void RefreshPreview()
    {
        _livePolygons = [];
        if (_livePx.Count >= 2)
        {
            var geometry = InkEngine.Build(_livePx, _livePressure, _liveTime, _style, finished: false);
            var center = new List<Pt>(geometry.Count);
            var halfWidth = new double[geometry.Count];
            for (int i = 0; i < geometry.Count; i++)
            {
                center.Add(new Pt(geometry.X[i], geometry.Y[i]));
                halfWidth[i] = geometry.HalfWidth[i];
            }

            if (_style.Tool == InkTool.Dashed)
                _livePolygons.AddRange(InkRibbon.BuildDashed(center, halfWidth, _style.DashLength, _style.DashGap));
            else
                _livePolygons.Add(InkRibbon.Build(center, halfWidth));
        }

        InvalidateVisual();
    }

    private void CommitStroke()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        bool hadPoints = _livePx.Count >= 2;

        if (!hadPoints || width <= 1 || height <= 1)
        {
            ResetLive();
            InvalidateVisual();
            return;
        }

        var colorHex = $"#{(int)InkColor.R:X2}{(int)InkColor.G:X2}{(int)InkColor.B:X2}";
        var raw = new RawStroke(
            colorHex,
            _style.BaseWidth,
            _livePx.Select(p => new Pt(p.X / width, p.Y / height)).ToList(),
            _livePressure.ToList(),
            _liveTime.ToList());

        var built = InkBuilder.Build(raw, _style, width, height);
        ResetLive();

        if (built is null)
        {
            InvalidateVisual();
            return;
        }

        var stroke = new InkStroke
        {
            Id = $"stroke-{Guid.NewGuid():N}",
            Anchor = new InkAnchor { SectionId = SectionId },
            Color = built.Color,
            Tool = built.ToolId,
            BaseWidth = built.BaseWidth,
            Points = built.FractionalPoints.Select(p => new InkPoint(p.X, p.Y)).ToList(),
            WidthFactors = built.WidthFactors.ToList(),
        };

        _strokes.Add(stroke);
        StrokeFinished?.Invoke(this, stroke);
        InvalidateVisual();
    }

    private void ResetLive()
    {
        _livePx.Clear();
        _livePressure.Clear();
        _liveTime.Clear();
        _livePolygons = [];
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Red;
        }
    }
}