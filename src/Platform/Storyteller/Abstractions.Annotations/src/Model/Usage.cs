using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

public record Usage : Annotation, IUsage
{
    public required string SubjectKey { get; init; }

    public required string ResponsibilityKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> ContextNames { get; init; } = new HashSet<string>(0);
}
