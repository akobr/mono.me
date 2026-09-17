using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class CertificateAuthorityEntity
{
    public string PartitionKey => "certificates";

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required byte[] CertificateData { get; init; }

    public byte[]? Pkcs12Data { get; init; }

    public string? KeyVaultKeyIdentifier { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public bool IsActive { get; init; } = true;
}
