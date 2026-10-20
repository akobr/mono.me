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

    // Federated only, and required for it: the rules a platform token must match.
    public IReadOnlyList<FederationRule>? FederationRules { get; init; }

    // Granted on top of Scope; never part of a preset. Null (or empty): none. Not required, so older clients
    // can leave it out.
    public IReadOnlyList<MachineManagementScope>? ManagementScopes { get; init; }
}
