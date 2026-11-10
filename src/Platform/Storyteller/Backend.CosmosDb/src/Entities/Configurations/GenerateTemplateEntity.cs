using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class GenerateTemplateEntity : Entity
{
    public required JObject Content { get; init; }

    // The write that produced the current version.
    public required AuditStamp Modified { get; init; }

    public ulong Version { get; init; }

    // The version whose content a restore copied into the current version; null after any other write.
    public ulong? RestoredFrom { get; init; }
}
