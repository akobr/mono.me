using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationSchemaEntity : Entity
{
    public required JObject Content { get; init; }

    public required string Author { get; init; }

    public ulong Version { get; init; }
}
