namespace Lmm.Format.Migrations;

/// <summary>
/// One format version upgrade step. Each step knows how to bring a manifest
/// (and, if needed, the rest of the container) from exactly FromVersion to
/// exactly ToVersion; the runner chains them. Only "1.0" exists today, so
/// there are no registered steps yet — this is scaffolding for the day a
/// "1.1"/"2.0" needs one, per the approved plan's versioning section.
/// </summary>
public interface IMigrationStep
{
    string FromVersion { get; }
    string ToVersion { get; }

    void Apply(LmmManifest manifest);
}
