using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

public record class Context : Annotation, IContext
{
    public required string SubjectKey { get; init; }

    public required string SubjectName { get; init; }

    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> ResponsibilityNames { get; init; } = new HashSet<string>(0);
}
