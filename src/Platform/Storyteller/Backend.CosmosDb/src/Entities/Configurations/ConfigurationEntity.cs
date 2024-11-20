using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationEntity : ExtendableEntity
{
    public bool IsServerSubstitutionDisabled { get; init; }

    public bool IsCachingDisabled { get; init; }

    public ulong Version { get; init; }

    public ulong AffectedCounter { get; init; }

    public required JObject Content { get; init; }

    public required string Author { get; init; }

    public JObject? CalculatedContent { get; init; }

    public string? CalculatedContentHash { get; init; }
}
