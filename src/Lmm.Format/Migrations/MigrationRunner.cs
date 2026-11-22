namespace Lmm.Format.Migrations;

/// <summary>
/// Applies registered IMigrationStep instances in sequence to bring a loaded
/// manifest up to LmmFormatVersions.Current. The approved plan also calls for
/// backing up the file as .bak before the first migration is applied to it —
/// that belongs in LmmPackageReader (it owns the file path), not here.
/// </summary>
public sealed class MigrationRunner(IReadOnlyList<IMigrationStep>? steps = null)
{
    private readonly IReadOnlyList<IMigrationStep> _steps = steps ?? [];

    public void MigrateToCurrent(LmmManifest manifest)
    {
        while (manifest.FormatVersion != LmmFormatVersions.Current)
        {
            var step = _steps.FirstOrDefault(s => s.FromVersion == manifest.FormatVersion)
                ?? throw new LmmFormatException(
                    $"No migration step registered from format version \"{manifest.FormatVersion}\" " +
                    $"to \"{LmmFormatVersions.Current}\". This file may be newer than this build of the app.");
            step.Apply(manifest);
            manifest.FormatVersion = step.ToVersion;
        }
    }
}
