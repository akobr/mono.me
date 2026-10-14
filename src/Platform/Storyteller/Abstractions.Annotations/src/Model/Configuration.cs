using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public record class Configuration
{
    public required string AnnotationKey { get; init; }

    public required ulong Version { get; init; }

    public required JObject Content { get; init; }

    // The write that produced this version; version 0, calculated and never written, is the calculation by System.
    public required AuditStamp Modified { get; init; }

    public string? Hash { get; init; }

    public IReadOnlyList<string>? Labels { get; init; }

    public IReadOnlyDictionary<string, object>? Values { get; init; }
}
