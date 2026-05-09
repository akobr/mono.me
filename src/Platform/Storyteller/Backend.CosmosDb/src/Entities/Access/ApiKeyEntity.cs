using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class ApiKeyEntity
{
    public required string PartitionKey { get; init; }

    /// <summary>
    /// SHA-256 hash of the raw API key, base64url-encoded.
    /// </summary>
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string MachineAccessId { get; init; }

    public required MachineAccessScope Scope { get; init; }
}
