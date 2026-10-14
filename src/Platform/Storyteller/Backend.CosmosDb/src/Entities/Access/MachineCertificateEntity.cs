using System;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

// A machine certificate record, in the organization container, partition "{project}.access", next to the shared
// certificates. Expires 30 days after the certificate does.
public record class MachineCertificateEntity
{
    public const string IdPrefix = "mcert.";

    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }

    public required string MachineAccessId { get; init; }

    public required string Thumbprint { get; init; }

    public string? PreviousThumbprint { get; init; }

    public DateTimeOffset? PreviousValidUntil { get; init; }

    public required string SerialNumber { get; init; }

    public required DateTimeOffset NotBefore { get; init; }

    public required DateTimeOffset NotAfter { get; init; }

    public bool IsRevoked { get; init; }

    public required MachineAccessScope Scope { get; init; }

    public string? AnnotationKey { get; init; }

    public DateTimeOffset? LastRenewalAt { get; init; }

    // The kind of the machine. Null on records stored before it was kept here.
    public MachineCredentialKind? CredentialKind { get; init; }

    public static string GetId(string thumbprint) => $"{IdPrefix}{thumbprint.ToUpperInvariant()}";

    public CertificateRecord ToRecord() => new(
        MachineAccessId,
        Thumbprint,
        PreviousThumbprint,
        PreviousValidUntil,
        SerialNumber,
        NotBefore,
        NotAfter,
        IsRevoked,
        Scope,
        AnnotationKey,
        LastRenewalAt,
        CredentialKind);

    public static MachineCertificateEntity FromRecord(string partitionKey, CertificateRecord record, DateTimeOffset now) => new()
    {
        PartitionKey = partitionKey,
        Id = GetId(record.Thumbprint),
        TimeToLiveInSeconds = (int)Math.Max(60, (record.NotAfter.AddDays(30) - now).TotalSeconds),
        MachineAccessId = record.MachineAccessId,
        Thumbprint = record.Thumbprint,
        PreviousThumbprint = record.PreviousThumbprint,
        PreviousValidUntil = record.PreviousValidUntil,
        SerialNumber = record.SerialNumber,
        NotBefore = record.NotBefore,
        NotAfter = record.NotAfter,
        IsRevoked = record.IsRevoked,
        Scope = record.Scope,
        AnnotationKey = record.AnnotationKey,
        LastRenewalAt = record.LastRenewalAt,
        CredentialKind = record.CredentialKind,
    };
}
