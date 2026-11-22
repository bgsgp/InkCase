using System.IO.Compression;
using System.Text.Json;
using Lmm.Core.Model;
using Lmm.Format.Migrations;

namespace Lmm.Format;

/// <summary>
/// Reads a .lmm ZIP container into a Paper domain object. Only knows how to
/// get bytes out of the container and deserialize them — resolving
/// manifest.ModuleId to an actual ISubjectModule, and surfacing a clear
/// "this file needs module X, which isn't installed" error, is the host's
/// job via IModuleCatalog (Lmm.Plugins.Abstractions), not this reader's.
/// </summary>
public sealed class LmmPackageReader(MigrationRunner? migrationRunner = null)
{
    private readonly MigrationRunner _migrationRunner = migrationRunner ?? new MigrationRunner();

    public Paper Read(string path)
    {
        using var stream = File.OpenRead(path);
        return Read(stream);
    }

    public Paper Read(Stream zipStream)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new LmmFormatException("Missing manifest.json — this is not a valid .lmm file.");

        var manifest = ReadJson<LmmManifest>(manifestEntry)
            ?? throw new LmmFormatException("manifest.json is empty or invalid.");

        _migrationRunner.MigrateToCurrent(manifest);

        var paper = new Paper
        {
            Id = manifest.Id,
            ModuleId = manifest.ModuleId,
            Title = manifest.Title,
            Subtitle = manifest.Subtitle,
            CreatedAt = manifest.CreatedAt,
            ModifiedAt = manifest.ModifiedAt,
            ModuleExtensions = manifest.ModuleExtensions,
        };

        foreach (var sectionRef in manifest.Sections.OrderBy(s => s.Order))
        {
            var entry = archive.GetEntry(sectionRef.File)
                ?? throw new LmmFormatException($"manifest.json references \"{sectionRef.File}\" but it is missing from the container.");
            var section = ReadJson<Section>(entry)
                ?? throw new LmmFormatException($"\"{sectionRef.File}\" is empty or invalid.");
            paper.Sections.Add(section);
        }

        foreach (var assetRef in manifest.Assets)
        {
            var entry = archive.GetEntry(assetRef.File)
                ?? throw new LmmFormatException($"manifest.json references asset \"{assetRef.File}\" but it is missing from the container.");
            using var entryStream = entry.Open();
            using var mem = new MemoryStream();
            entryStream.CopyTo(mem);
            paper.Assets.Add(new PaperAsset { Id = assetRef.Id, MimeType = assetRef.MimeType, Bytes = mem.ToArray() });
        }

        foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("ink/", StringComparison.Ordinal))
                                              .OrderBy(e => e.FullName, StringComparer.Ordinal))
        {
            var strokes = ReadJson<List<InkStroke>>(entry);
            if (strokes is not null) paper.InkStrokes.AddRange(strokes);
        }

        return paper;
    }

    private static T? ReadJson<T>(ZipArchiveEntry entry)
    {
        using var entryStream = entry.Open();
        try
        {
            return JsonSerializer.Deserialize<T>(entryStream, LmmJson.Options);
        }
        catch (JsonException ex)
        {
            throw new LmmFormatException($"\"{entry.FullName}\" contains invalid JSON.", ex);
        }
    }
}
