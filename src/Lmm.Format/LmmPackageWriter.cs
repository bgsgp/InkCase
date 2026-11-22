using System.IO.Compression;
using System.Text.Json;
using Lmm.Core.Model;

namespace Lmm.Format;

/// <summary>
/// Writes a Paper domain object out as a .lmm ZIP container. Binary assets
/// are stored as real bytes (never base64-inflated into JSON); JSON parts
/// are written indented/camelCase so the container stays human-readable if
/// opened as a plain .zip, per the approved format design.
/// </summary>
public sealed class LmmPackageWriter
{
    /// <summary>How many ink strokes go in each ink/strokes-NNNN.json chunk file.</summary>
    private const int InkStrokesPerChunk = 500;

    public void Write(Paper paper, string path)
    {
        // Write to a temp file first and swap in — a half-written .lmm from a crash
        // mid-save must never look like a valid (but truncated) paper.
        var tempPath = path + ".tmp";
        using (var stream = File.Create(tempPath))
        {
            Write(paper, stream);
        }
        File.Move(tempPath, path, overwrite: true);
    }

    public void Write(Paper paper, Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true);

        var manifest = new LmmManifest
        {
            FormatVersion = LmmFormatVersions.Current,
            Id = paper.Id,
            ModuleId = paper.ModuleId,
            Title = paper.Title,
            Subtitle = paper.Subtitle,
            CreatedAt = paper.CreatedAt,
            ModifiedAt = paper.ModifiedAt,
            ModuleExtensions = paper.ModuleExtensions,
        };

        for (int i = 0; i < paper.Sections.Count; i++)
        {
            var file = $"sections/section-{i + 1:D4}.json";
            manifest.Sections.Add(new ManifestSectionRef { Id = paper.Sections[i].Id, File = file, Order = i });
            WriteJson(archive, file, paper.Sections[i]);
        }

        foreach (var asset in paper.Assets)
        {
            var file = $"assets/{asset.Id}{GuessExtension(asset.MimeType)}";
            manifest.Assets.Add(new ManifestAssetRef { Id = asset.Id, File = file, MimeType = asset.MimeType });
            var entry = archive.CreateEntry(file, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(asset.Bytes, 0, asset.Bytes.Length);
        }

        for (int chunkIndex = 0, offset = 0; offset < paper.InkStrokes.Count; chunkIndex++, offset += InkStrokesPerChunk)
        {
            var chunk = paper.InkStrokes.Skip(offset).Take(InkStrokesPerChunk).ToList();
            WriteJson(archive, $"ink/strokes-{chunkIndex + 1:D4}.json", chunk);
        }

        // manifest.json last: by the time it's written every section/asset/ink file referenced
        // from it already exists in the archive.
        WriteJson(archive, "manifest.json", manifest);
    }

    private static void WriteJson<T>(ZipArchive archive, string entryPath, T value)
    {
        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        JsonSerializer.Serialize(entryStream, value, LmmJson.Options);
    }

    private static string GuessExtension(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/webp" => ".webp",
        "application/pdf" => ".pdf",
        _ => ".bin",
    };
}
