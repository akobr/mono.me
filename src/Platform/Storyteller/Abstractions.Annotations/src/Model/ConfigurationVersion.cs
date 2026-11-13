using System;

namespace _42.Platform.Storyteller;

public record class ConfigurationVersion
{
    public uint Version { get; init; }

    // The write that produced this version.
    public required AuditStamp Modified { get; init; }

    public DateTimeOffset ExpirationTime { get; init; }

    // The version whose content a restore copied into this one; null for any other write.
    public uint? RestoredFrom { get; init; }
}
