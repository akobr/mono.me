using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

// A trusted issuer of a project: organization container, partition {project}.federation, id = name.
public record class FederatedIssuerEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string Issuer { get; init; }

    public FederatedIssuerKeySource KeySource { get; init; }

    public string? JwksUri { get; init; }

    public string? Jwks { get; init; }

    public int? MaxTokenLifetimeSeconds { get; init; }

    public Dictionary<string, List<string>>? RequiredClaims { get; init; }

    public string? Description { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    [JsonProperty("_etag")]
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
