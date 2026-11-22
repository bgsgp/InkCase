namespace Lmm.Core.Ink;

/// <summary>
/// The ink tools xnotes ships as its quick-wheel set
/// (shardulvs/xnotes-android, `core/tools/Tool.kt`): pen, dashed, calligraphy,
/// speed, taper and highlighter. Each is not a cosmetic filter over one shared
/// ribbon — the six differ in *how the half-width is derived* (pressure only,
/// a nib direction channel, speed over time, a taper over arc length, or none at
/// all), which is why the engine below carries one branch per tool.
/// </summary>
public enum InkTool
{
    Pen,
    Dashed,
    Calligraphy,
    Speed,
    Taper,
    Highlighter,
}

/// <summary>
/// One tool's style record, the port of xnotes' `ToolConfig` narrowed to what
/// affects the ribbon. Values match `ToolDefaults.configFor` in
/// `core/tools/ToolConfig.kt`; the only deliberate difference is the
/// highlighter alpha, explained on <see cref="AlphaScale"/>.
/// </summary>
public sealed record InkStyle(
    InkTool Tool,
    double BaseWidth,
    bool PressureEnabled,
    double PressureMinFactor,
    double DirectionStrength,
    double SpeedStrength,
    bool TaperEnabled,
    double TaperMinFactor,
    double AlphaScale,
    double DashLength,
    double DashGap)
{
    /// <summary>Pen and highlighter hold their end pressure out to the tip, so the swept end disc meets the line at body width.</summary>
    public bool HoldEnds => Tool is InkTool.Pen or InkTool.Highlighter;

    /// <summary>Stable id for persistence: what `Tool.id` is in xnotes.</summary>
    public string Id => Tool switch
    {
        InkTool.Pen => "pen",
        InkTool.Dashed => "dashed",
        InkTool.Calligraphy => "calligraphy",
        InkTool.Speed => "speed",
        InkTool.Taper => "taper",
        _ => "highlighter",
    };

    public static InkTool ParseTool(string? id) => id switch
    {
        "dashed" => InkTool.Dashed,
        "calligraphy" => InkTool.Calligraphy,
        "speed" => InkTool.Speed,
        "taper" => InkTool.Taper,
        "highlighter" => InkTool.Highlighter,
        _ => InkTool.Pen,
    };

    /// <summary>Factory defaults, one row per tool (xnotes `ToolDefaults.configFor`).</summary>
    public static InkStyle For(InkTool tool) => tool switch
    {
        InkTool.Dashed => new InkStyle(tool, 3.0, false, 1.0, 0.0, 0.0, false, 0.0, 1.0, 10.0, 8.0),
        InkTool.Calligraphy => new InkStyle(tool, 6.0, true, 0.40, 0.60, 0.0, false, 0.0, 1.0, 10.0, 8.0),
        InkTool.Speed => new InkStyle(tool, 4.0, true, 0.35, 0.0, 0.8, false, 0.0, 1.0, 10.0, 8.0),
        InkTool.Taper => new InkStyle(tool, 4.0, true, 0.45, 0.0, 0.0, true, 0.50, 1.0, 10.0, 8.0),
        // xnotes composites the highlighter with MULTIPLY at alpha 0.90. WPF's plain
        // source-over would blot the text out at that alpha, so the same intended tint
        // is dialled to 0.45 here rather than faking a blend mode the renderer lacks.
        InkTool.Highlighter => new InkStyle(tool, 16.0, false, 1.0, 0.0, 0.0, false, 0.0, 0.45, 10.0, 8.0),
        _ => new InkStyle(InkTool.Pen, 3.0, true, 0.35, 0.0, 0.0, false, 0.0, 1.0, 10.0, 8.0),
    };

    /// <summary>The style for a persisted tool id, with a stroke's own stored width.</summary>
    public static InkStyle For(string? id, double baseWidth)
    {
        var style = For(ParseTool(id));
        return baseWidth > 0 ? style with { BaseWidth = baseWidth } : style;
    }
}
