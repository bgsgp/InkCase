namespace Lmm.Core.Model;

/// <summary>
/// A binary asset belonging to a Paper (a scanned page image, a figure a
/// question depends on, ...). Stored as real binary inside the .lmm ZIP
/// container's assets/ folder — never base64-inflated into JSON — and
/// referenced from Question.AssetRefs / manifest by Id.
/// </summary>
public sealed class PaperAsset
{
    public string Id { get; set; } = "";
    public string MimeType { get; set; } = "";
    public byte[] Bytes { get; set; } = [];
}
