using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

public record Subject : Annotation, ISubject
{

    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> ContextNames { get; init; } = new HashSet<string>(0);

    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> ResponsibilityNames { get; init; } = new HashSet<string>(0);
}
