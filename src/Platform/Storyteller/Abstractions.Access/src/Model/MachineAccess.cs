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
}
