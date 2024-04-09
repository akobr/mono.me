using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class AccessPointEntity
{
    public string PartitionKey => "access";

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string Id => $"apt.{Key}";

    public required string Key { get; init; }

    public required Dictionary<string, AccountRole> AccessMap { get; init; } = new();
}
