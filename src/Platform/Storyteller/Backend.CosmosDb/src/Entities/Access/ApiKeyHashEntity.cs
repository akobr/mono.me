using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class ApiKeyHashEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string HashedSecret { get; init; }

    public required MachineAccessScope Scope { get; init; }

    // Granted on top of Scope; null for none and on hashes stored before it was kept.
    public List<MachineManagementScope>? ManagementScopes { get; init; }
}
