namespace _42.Platform.Storyteller.Accessing;

public record CertificateRecord(
    string MachineAccessId,
    string Thumbprint,
    string? PreviousThumbprint,
    DateTimeOffset? PreviousValidUntil,
    string SerialNumber,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    bool IsRevoked,
    MachineAccessScope Scope,
    string? AnnotationKey,
    DateTimeOffset? LastRenewalAt);
