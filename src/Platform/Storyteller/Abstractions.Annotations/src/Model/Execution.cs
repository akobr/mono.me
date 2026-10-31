using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

public record Execution : Annotation, IExecution
{
    public required string ResponsibilityKey { get; init; }

    public required string SubjectKey { get; init; }

    public required string ContextKey { get; init; }

    public required string UsageKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required string ContextName { get; init; }

    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> UnitNames { get; init; } = new HashSet<string>(0);
}
