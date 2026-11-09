using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class AccessPointEntity
{
    // The partition of core with the access points, and the prefix of their ids ("apt.{organization}[.{project}]").
    public const string Partition = "access";
    public const string IdPrefix = "apt.";

    public string PartitionKey => Partition;

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public string Id => $"{IdPrefix}{Key}";

    public required string Key { get; init; }

    public required Dictionary<string, AccountRole> AccessMap { get; init; } = new();

    public MachineAuthenticationPolicy? MachineAuthentication { get; init; }

    // The key/value policy of a project; null means the defaults.
    public KeyValuePolicy? KeyValues { get; init; }

    // The description of a project; absent for an organization and in documents stored before descriptions.
    public string? Description { get; init; }
}
