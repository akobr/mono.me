using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

public record class MachineAccessEntity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string ObjectId { get; init; }

    public required string AccessKey { get; init; }

    public required MachineAccessScope Scope { get; init; }

    public string? AnnotationKey { get; init; }

    public MachineCredentialKind CredentialKind { get; init; }

    public string? HashedSecret { get; init; }

    public string? CertificateThumbprint { get; init; }

    public string? PreviousThumbprint { get; init; }

    public DateTimeOffset? PreviousValidUntil { get; init; }

    public string? CertificateSerialNumber { get; init; }

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    public bool IsRevoked { get; init; }

    public DateTimeOffset? LastRenewalAt { get; init; }

    [JsonProperty("_etag")]
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
