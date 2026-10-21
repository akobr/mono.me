using System;
using System.Collections.Generic;
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

    // The scopes of the machine, as created. Not required: a document stored before it was kept has none, and reads
    // as a machine without scopes instead of failing every point read.
    public List<MachineScope>? Scopes { get; init; }

    public string? AnnotationKey { get; init; }

    public MachineCredentialKind CredentialKind { get; init; }

    public string? HashedSecret { get; init; }

    public string? CertificateThumbprint { get; init; }

    public string? PreviousThumbprint { get; init; }

    public DateTimeOffset? PreviousValidUntil { get; init; }

    public string? CertificateSerialNumber { get; init; }

    public DateTimeOffset? NotBefore { get; init; }

    public DateTimeOffset? NotAfter { get; init; }

    // The lifetime chosen at creation; renewals keep it. Null for machines created before it was stored.
    public int? CertificateLifetimeDays { get; init; }

    public bool IsRevoked { get; init; }

    public bool UsesSharedCertificate { get; init; }

    public DateTimeOffset? LastRenewalAt { get; init; }

    // Federated only: the rules a platform token must match.
    public List<FederationRule>? FederationRules { get; init; }

    [JsonProperty("_etag")]
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
