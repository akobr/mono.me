using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class AccountEntity
{
    public string PartitionKey => "access";

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string UserName { get; init; }

    public required string Name { get; init; }

    public required Dictionary<string, AccountRole> AccessMap { get; init; } = new();
}
