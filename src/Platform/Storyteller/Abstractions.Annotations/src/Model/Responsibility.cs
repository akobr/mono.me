using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

public record class Responsibility : Annotation, IResponsibility
{
    [JsonConverter(typeof(RelationNamesJsonConverter))]
    public IReadOnlySet<string> UnitNames { get; init; } = new HashSet<string>(0);
}
