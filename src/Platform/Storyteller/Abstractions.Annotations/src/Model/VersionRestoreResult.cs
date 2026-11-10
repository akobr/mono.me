namespace _42.Platform.Storyteller;

// The result of restoring a version of a configuration, a template or a schema.
public record class VersionRestoreResult
{
    public required VersionRestoreOutcome Outcome { get; init; }

    // The version whose content was asked for.
    public required ulong FromVersion { get; init; }

    // Restored: the new version; Unchanged: the current version.
    public required ulong Version { get; init; }

    // The write that produced Version.
    public required AuditStamp Modified { get; init; }

    public static VersionRestoreResult Restored(ulong fromVersion, ulong version, AuditStamp modified) => new()
    {
        Outcome = VersionRestoreOutcome.Restored,
        FromVersion = fromVersion,
        Version = version,
        Modified = modified,
    };

    public static VersionRestoreResult Unchanged(ulong fromVersion, ulong version, AuditStamp modified) => new()
    {
        Outcome = VersionRestoreOutcome.Unchanged,
        FromVersion = fromVersion,
        Version = version,
        Modified = modified,
    };
}
