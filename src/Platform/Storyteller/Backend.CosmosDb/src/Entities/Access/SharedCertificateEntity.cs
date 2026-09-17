using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class SharedCertificateEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Thumbprint { get; init; }

    public required DateTimeOffset NotBefore { get; init; }

    public required DateTimeOffset NotAfter { get; init; }

    public bool IsRevoked { get; init; }
}
