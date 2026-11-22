namespace Lmm.Plugins.Abstractions;

/// <summary>
/// What a subject module knows how to render. The shell reads these to decide
/// which panels/commands to show, so an imported maths paper doesn't get an
/// empty "中英对照" tab.
/// </summary>
public sealed class SubjectCapabilities
{
    public bool SupportsInk { get; init; }
    public bool SupportsBilingualPairs { get; init; }
    public bool SupportsStructureTable { get; init; }
    public bool SupportsDrills { get; init; }
    public bool SupportsAssetImages { get; init; }

    public static SubjectCapabilities None { get; } = new();
}
