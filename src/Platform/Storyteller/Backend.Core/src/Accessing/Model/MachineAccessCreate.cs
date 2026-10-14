namespace _42.Platform.Storyteller.Accessing.Model;

public record class MachineAccessCreate
{
    public required string Organization { get; set; }

    public required string Project { get; set; }

    public required MachineAccessScope Scope { get; init; } = MachineAccessScope.DefaultRead;

    public string? AnnotationKey { get; init; }

    // One of the kinds the project enables. Null: the project's default kind.
    public MachineCredentialKind? CredentialKind { get; init; }

    // Kinds with a certificate only. Null: the project's default certificate lifetime.
    public int? CertificateLifetimeDays { get; init; }

    // CertificateAndApiKey only: no certificate is issued for the machine, it presents a shared certificate of the project.
    public bool? UseSharedCertificate { get; init; }
}
