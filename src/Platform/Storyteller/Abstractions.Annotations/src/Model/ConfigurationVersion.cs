using System;

namespace _42.Platform.Storyteller;

public record class ConfigurationVersion
{
    public uint Version { get; init; }

    public required string Author { get; init; }

    public DateTimeOffset CreationTime { get; init; }

    public DateTimeOffset ExpirationTime { get; init; }
}
