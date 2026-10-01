using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public record class ConfigurationSchema
{
    public required string View { get; init; }

    public string? AnnotationType { get; init; }

    public string? AnnotationKey { get; init; }

    public required ulong Version { get; init; }

    public required JObject Content { get; init; }

    public required string Author { get; init; }
}
