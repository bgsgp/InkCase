namespace Lmm.Core.Model;

/// <summary>
/// One raw, captured handwriting stroke. Deliberately stores only the raw
/// input points, not the beautified ribbon polygon: the ribbon is a derived
/// value, recomputed from these points by the current ink geometry algorithm
/// every time the paper is opened (see Lmm.Core ink-geometry library, Phase 2a).
/// That keeps old files benefiting automatically from future algorithm
/// improvements and keeps the file format decoupled from the renderer.
/// </summary>
public sealed class InkStroke
{
    public string Id { get; set; } = "";

    /// <summary>Where this stroke is anchored in the review UI (e.g. which section it was drawn over).</summary>
    public InkAnchor Anchor { get; set; } = new();

    /// <summary>Pen color, e.g. "#D33A2C".</summary>
    public string Color { get; set; } = "#D33A2C";

    public double BaseWidth { get; set; } = 3.0;

    /// <summary>
    /// Raw captured points, in fractional (0..1) coordinates relative to the
    /// anchor surface — matches the legacy app's convention so strokes are
    /// unaffected by canvas zoom/resize.
    /// </summary>
    public List<InkPoint> Points { get; set; } = [];

    /// <summary>
    /// Per-point width modulation factors (speed-based), same length as Points.
    /// Multiplies BaseWidth when the ribbon polygon is (re)built.
    /// </summary>
    public List<double> WidthFactors { get; set; } = [];
}

public sealed class InkAnchor
{
    public string? SectionId { get; set; }
}

/// <summary>A single fractional-coordinate ink sample point.</summary>
public readonly record struct InkPoint(double X, double Y);
