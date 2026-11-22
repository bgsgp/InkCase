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
/// The nib model follows xnotes: each point carries a width factor derived from
/// pen pressure and speed, and the outline is the swept disk boundary, so live
/// preview uses exactly the same geometry as the committed stroke.
/// </summary>
public sealed class InkCanvasElement : FrameworkElement
{
    private readonly List<InkStroke> _strokes = [];
    private readonly List<Pt> _livePx = [];
    private readonly List<double> _livePressure = [];
    private readonly List<double> _liveTime = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private List<Pt>? _livePolygon;
    private bool _inkEnabled = true;

    public InkCanvasElement()
    {
        Cursor = Cursors.Pen;
    }

    public event EventHandler<InkStroke>? StrokeFinished;

    public Color InkColor { get; set; } = Color.FromRgb(0xD3, 0x3A, 0x2C);

    public double BaseWidth { get; set; } = 3.0;

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

        if (_livePolygon is { Count: > 2 })
            dc.DrawGeometry(new SolidColorBrush(InkColor), null, ToGeometry(_livePolygon));
    }

    private void DrawStroke(DrawingContext dc, InkStroke stroke)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1 || stroke.Points.Count < 2) return;

        var points = stroke.Points.Select(p => new Pt(p.X, p.Y)).ToList();
        var wf = stroke.WidthFactors is { Count: > 0 } list ? list.ToList() : null;

        var polygon = RibbonBuilder.BuildPolygonPx(
            points.Select(p => new Pt(p.X * width, p.Y * height)).ToList(), stroke.BaseWidth, live: false, wf);
        if (polygon.Count < 3) return;

        var brush = new SolidColorBrush(ParseColor(stroke.Color));
        brush.Freeze();
        dc.DrawGeometry(brush, null, ToGeometry(polygon));
    }

    private static StreamGeometry ToGeometry(IReadOnlyList<Pt> polygon)
    {
        var geometry = new StreamGeometry();
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
        RefreshPreview();
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
        if (_livePx.Count < 2)
        {
            _livePolygon = null;
        }
        else
        {
            var wf = InkWidths.FromCapture(_livePx, _livePressure, _liveTime, _livePx, live: true);
            _livePolygon = InkRibbon.Build(_livePx, BaseWidth, wf, live: true);
        }

        InvalidateVisual();
    }

    private void CommitStroke()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        _livePolygon = null;

        if (_livePx.Count < 2 || width <= 1 || height <= 1)
        {
            ResetLive();
            InvalidateVisual();
            return;
        }

        var colorHex = $"#{(int)InkColor.R:X2}{(int)InkColor.G:X2}{(int)InkColor.B:X2}";
        var raw = new RawStroke(
            colorHex,
            BaseWidth,
            _livePx.Select(p => new Pt(p.X / width, p.Y / height)).ToList(),
            _livePressure.ToList(),
            _liveTime.ToList());

        var beautified = StrokeBeautifier.Beautify(raw, width, height);
        ResetLive();

        if (beautified is null)
        {
            InvalidateVisual();
            return;
        }

        var stroke = new InkStroke
        {
            Id = $"stroke-{Guid.NewGuid():N}",
            Anchor = new InkAnchor { SectionId = SectionId },
            Color = colorHex,
            BaseWidth = BaseWidth,
            Points = beautified.FractionalPoints.Select(p => new InkPoint(p.X, p.Y)).ToList(),
            WidthFactors = beautified.WidthFactors.ToList(),
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
        _livePolygon = null;
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
