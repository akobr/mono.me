using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Annotations;

// A registered view, in the organization container, partition "{project}.meta".
// It has no ViewName property on purpose, so view discovery never counts it as data of a view.
public record class ViewEntity
{
    public const string IdPrefix = "view.";

    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string Id => $"{IdPrefix}{Name}";

    public required string Name { get; init; }

    public string? Description { get; init; }

    // Null on views registered before the stamps (see GetCreated).
    public AuditStamp? Created { get; init; }

    // Legacy creation of views registered before the stamps; never written again.
    public DateTimeOffset? CreatedAt { get; init; }

    public string? CreatedBy { get; init; }

    public static string GetPartitionKey(string project) => $"{project}.meta";

    public AuditStamp? GetCreated()
    {
        return Created ?? (CreatedAt is { } at ? LegacyAuthors.ToStamp(CreatedBy, at) : null);
    }

    public View ToView() => new()
    {
        Name = Name,
        Description = Description,
        Created = GetCreated(),
        IsDefault = Name == Constants.DefaultViewName,
        IsRegistered = true,
    };
}
