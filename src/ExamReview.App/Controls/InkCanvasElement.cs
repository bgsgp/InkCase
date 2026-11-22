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
/// ribbon polygon in <see cref="OnRender"/> — no per-stroke Path elements. Live
/// preview while the pen is down is a cheap thin polyline; RibbonBuilder (the
/// expensive part) only runs once on pointer-up.
/// </summary>
public sealed class InkCanvasElement : FrameworkElement
{
    private readonly List<InkStroke> _strokes = [];
    private readonly List<Point> _live = [];
    private Point _lastPoint;
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
        _live.Clear();
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

        if (_live.Count >= 2)
        {
            var pen = new Pen(new SolidColorBrush(InkColor), Math.Max(1.0, BaseWidth * 0.55))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round,
            };
            pen.Freeze();

            var preview = new StreamGeometry();
            using (var ctx = preview.Open())
            {
                ctx.BeginFigure(_live[0], false, false);
                ctx.PolyLineTo(_live.Skip(1).ToList(), true, false);
            }
            preview.Freeze();
            dc.DrawGeometry(null, pen, preview);
        }
    }

    private void DrawStroke(DrawingContext dc, InkStroke stroke)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1 || stroke.Points.Count < 2) return;

        var beautified = new BeautifiedStroke(
            stroke.Color,
            stroke.BaseWidth,
            stroke.Points.Select(p => new Pt(p.X, p.Y)).ToList(),
            stroke.WidthFactors.ToList());

        var polygon = RibbonBuilder.BuildPolygon(beautified, width, height, live: false);
        if (polygon.Count < 3) return;

        var brush = new SolidColorBrush(ParseColor(stroke.Color));
        brush.Freeze();

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(polygon[0].X, polygon[0].Y), true, true);
            ctx.PolyLineTo(polygon.Skip(1).Select(p => new Point(p.X, p.Y)).ToList(), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!_inkEnabled || string.IsNullOrEmpty(SectionId))
        {
            base.OnMouseLeftButtonDown(e);
            return;
        }

        CaptureMouse();
        _live.Clear();
        _lastPoint = e.GetPosition(this);
        _live.Add(_lastPoint);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_inkEnabled || !IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;

        var point = e.GetPosition(this);
        if ((point - _lastPoint).Length < 1.5) return;

        _live.Add(point);
        _lastPoint = point;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;

        ReleaseMouseCapture();
        e.Handled = true;

        var width = ActualWidth;
        var height = ActualHeight;
        if (_live.Count < 2 || width <= 1 || height <= 1)
        {
            _live.Clear();
            InvalidateVisual();
            return;
        }

        var colorHex = $"#{(int)InkColor.R:X2}{(int)InkColor.G:X2}{(int)InkColor.B:X2}";
        var raw = new RawStroke(colorHex, BaseWidth, _live.Select(p => new Pt(p.X / width, p.Y / height)).ToList());
        _live.Clear();

        var beautified = StrokeBeautifier.Beautify(raw, width, height);
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
