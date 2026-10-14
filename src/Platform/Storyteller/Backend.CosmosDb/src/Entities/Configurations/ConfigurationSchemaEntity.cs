using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationSchemaEntity : Entity
{
    public required JObject Content { get; init; }

    // The write that produced the current version; null on items stored before the stamps (see GetModified).
    public AuditStamp? Modified { get; init; }

    // Legacy author string of items stored before the stamps; never written again.
    public string? Author { get; init; }

    public ulong Version { get; init; }
}
