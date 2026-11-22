using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lmm.Format;

/// <summary>
/// Shared JSON options for every part inside a .lmm container. camelCase +
/// indentation so the JSON parts stay human-readable/diffable if someone
/// renames the file to .zip and opens it — this is deliberate per the format
/// design (ZIP container, not an opaque blob).
/// </summary>
internal static class LmmJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
