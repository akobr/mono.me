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

    public required AuditStamp Created { get; init; }

    public static string GetPartitionKey(string project) => $"{project}.meta";

    public View ToView() => new()
    {
        Name = Name,
        Description = Description,
        Created = Created,
        IsDefault = Name == Constants.DefaultViewName,
        IsRegistered = true,
    };
}
