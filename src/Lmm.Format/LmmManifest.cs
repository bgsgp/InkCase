namespace Lmm.Format;

/// <summary>
/// The .lmm container's manifest.json — the one file read before anything
/// else, so the host can resolve which subject module (plugin) owns this
/// paper before touching any section data. See LmmPackageReader/Writer for
/// the rest of the container layout.
/// </summary>
public sealed class LmmManifest
{
    public string FormatVersion { get; set; } = LmmFormatVersions.Current;

    /// <summary>Stable paper id, independent of the file's path/name on disk.</summary>
    public string Id { get; set; } = "";

    /// <summary>Which subject module this paper belongs to, e.g. "cn.examreview.subject.english.v1".</summary>
    public string ModuleId { get; set; } = "";

    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ModifiedAt { get; set; }

    public List<ManifestSectionRef> Sections { get; set; } = [];
    public List<ManifestAssetRef> Assets { get; set; } = [];

    /// <summary>Opaque module-specific extension data the host/format layer doesn't interpret.</summary>
    public Dictionary<string, object?> ModuleExtensions { get; set; } = [];
}

public sealed class ManifestSectionRef
{
    public string Id { get; set; } = "";

    /// <summary>Path inside the ZIP, e.g. "sections/section-0001.json".</summary>
    public string File { get; set; } = "";

    public int Order { get; set; }
}

public sealed class ManifestAssetRef
{
    public string Id { get; set; } = "";

    /// <summary>Path inside the ZIP, e.g. "assets/img-0001.png".</summary>
    public string File { get; set; } = "";

    public string MimeType { get; set; } = "";
}

public static class LmmFormatVersions
{
    public const string Current = "1.0";
}
