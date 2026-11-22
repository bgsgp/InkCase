namespace Lmm.Core.Model;

/// <summary>
/// Root aggregate for one exam paper (one ".lmm" file once loaded).
/// Corresponds to the old single-paper record inside the legacy app's
/// localStorage blob (store.papers[i]), now split out into its own document.
/// </summary>
public sealed class Paper
{
    /// <summary>Stable identifier for this paper (distinct from the file path on disk).</summary>
    public string Id { get; set; } = "";

    /// <summary>Which subject module (plugin) this paper belongs to, e.g. "cn.examreview.subject.english.v1".</summary>
    public string ModuleId { get; set; } = "";

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public List<Section> Sections { get; set; } = [];

    /// <summary>Ink strokes captured on top of this paper's review UI.</summary>
    public List<InkStroke> InkStrokes { get; set; } = [];

    /// <summary>Binary assets (scanned pages, figures) referenced by Questions/manifest.</summary>
    public List<PaperAsset> Assets { get; set; } = [];

    /// <summary>
    /// Module-specific extension data that doesn't fit the common schema below.
    /// Kept as opaque key/value so a subject module can round-trip its own
    /// extra fields without the host or the format layer needing to know about them.
    /// </summary>
    public Dictionary<string, object?> ModuleExtensions { get; set; } = [];
}
