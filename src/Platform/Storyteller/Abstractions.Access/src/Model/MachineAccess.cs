using System;

namespace _42.Platform.Storyteller;

public record class MachineAccess : IMachineAccess
{
    public required string Id { get; init; }

    public required string ObjectId { get; init; }

    public required string AccessKey { get; init; }

    public required MachineAccessScope Scope { get; init; }

    public string? AnnotationKey { get; init; }

    public MachineCredentialKind CredentialKind { get; init; }

    public string? CertificateThumbprint { get; init; }

    public string? Certificate { get; init; }

    public string? CertificatePassword { get; init; }

    public DateTimeOffset? LastRenewalAt { get; init; }

    // Kinds with a certificate only: the lifetime chosen at creation, kept for renewals, and when the current certificate expires.
    public int? CertificateLifetimeDays { get; init; }

    public DateTimeOffset? CertificateNotAfter { get; init; }

    // CertificateAndApiKey only: the machine has no certificate of its own and presents a shared certificate of the project.
    public bool UsesSharedCertificate { get; init; }

    // ClientCredentials only, returned on create and not stored. Get, list, and reset omit them.
    // TokenEndpoint is where the machine exchanges Id and AccessKey for a token. TokenScope is
    // the scope parameter that exchange needs.
    public string? TokenEndpoint { get; init; }

    public string? TokenScope { get; init; }
}
