using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// One version of a configuration (an annotation's own document), a template or a schema.
public sealed record class ConfigurationDocument
{
    public required ulong Version { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required JObject Content { get; init; }
}
