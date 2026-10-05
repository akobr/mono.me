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

    // ClientCredentials only, returned on create and reset (not stored): where the machine
    // exchanges Id and AccessKey for a token, and the scope parameter that exchange needs.
    public string? TokenEndpoint { get; init; }

    public string? TokenScope { get; init; }
}
