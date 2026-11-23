namespace Lmm.Core.Model;

/// <summary>
/// One stored handwriting stroke. Deliberately stores the beautyfied centreline
/// and the per-point width multipliers rather than the outline polygon: the
/// outline is a derived value, rebuilt by <see cref="Ink.InkRibbon"/> on every
/// render, so the file never depends on the current renderer's cap count or
/// flattening. <see cref="Tool"/> names the xnotes pen it was drawn with, which
/// is what decides alpha, dashes and whether width came from pressure, nib or speed.
/// </summary>
public sealed class InkStroke
{
    public string Id { get; set; } = "";

    /// <summary>Where this stroke is anchored in the review UI (e.g. which section it was drawn over).</summary>
    public InkAnchor Anchor { get; set; } = new();

    /// <summary>Pen color, e.g. "#D33A2C".</summary>
    public string Color { get; set; } = "#D33A2C";

    /// <summary>
    /// Ink tool id: "pen" (default), "dashed", "calligraphy", "speed", "taper"
    /// or "highlighter". Older files without the field read as "pen".
    /// </summary>
    public string Tool { get; set; } = "pen";

    public double BaseWidth { get; set; } = 3.0;

    /// <summary>
    /// Smoothed centreline, in fractional (0..1) coordinates relative to the
    /// anchor surface — matches the legacy app's convention so strokes are
    /// unaffected by canvas zoom/resize.
    /// </summary>
    public List<InkPoint> Points { get; set; } = [];

    /// <summary>
    /// Per-point width multipliers, same length as Points: the ribbon's radius at
    /// that point is <c>BaseWidth / 2 * WidthFactors[i]</c>. Missing or short lists
    /// fall back to 1.0 so hand-edited files still draw.
    /// </summary>
    public List<double> WidthFactors { get; set; } = [];
}

/// <summary>Anchor surface names, shared by the app and the file format.</summary>
public static class InkSurfaces
{
    /// <summary>Ink on the reading pane; one layer for the whole section.</summary>
    public const string Article = "article";

    /// <summary>Ink on a question's detail pane; one layer per question.</summary>
    public const string Answer = "answer";

    /// <summary>Ink over the paragraph-structure table.</summary>
    public const string Structure = "structure";

    /// <summary>Ink over the bilingual (English/Chinese) page.</summary>
    public const string Bilingual = "bilingual";

    /// <summary>Ink over the classroom-drill page.</summary>
    public const string Drills = "drills";

    /// <summary>Every surface a stroke can be anchored to.</summary>
    public static readonly string[] All = [Article, Answer, Structure, Bilingual, Drills];
}

public sealed class InkAnchor
{
    public string? SectionId { get; set; }

    /// <summary>
    /// Which surface the stroke was drawn on: "article" (the reading pane) or
    /// "answer" (the question detail pane). Null/absent in older files means
    /// "article", which is where every stroke used to go.
    /// </summary>
    public string? Surface { get; set; }

    /// <summary>For "answer" strokes: the question number whose detail the ink sits on.</summary>
    public int? QuestionNumber { get; set; }
}

/// <summary>A single fractional-coordinate ink sample point.</summary>
public readonly record struct InkPoint(double X, double Y);
