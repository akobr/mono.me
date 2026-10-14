using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationSchemaEntity : Entity
{
    public required JObject Content { get; init; }

    // The write that produced the current version.
    public required AuditStamp Modified { get; init; }

    public ulong Version { get; init; }
}
