namespace _42.Platform.Storyteller.Accessing.Model;

public record class MachineAccessCreate
{
    public required string Organization { get; set; }

    public required string Project { get; set; }

    // The requested scopes. Not required, so a missing list reaches the handler and gets its 400 instead of a body
    // that cannot be read. Creation completes the list (write implies read) and needs one runtime scope at least.
    public IReadOnlyList<MachineScope>? Scopes { get; init; }

    public string? AnnotationKey { get; init; }

    // One of the kinds the project enables. Null: the project's default kind.
    public MachineCredentialKind? CredentialKind { get; init; }

    // Kinds with a certificate only. Null: the project's default certificate lifetime.
    public int? CertificateLifetimeDays { get; init; }

    // CertificateAndApiKey only: no certificate is issued for the machine, it presents a shared certificate of the project.
    public bool? UseSharedCertificate { get; init; }

    // Federated only, and required for it: the rules a platform token must match.
    public IReadOnlyList<FederationRule>? FederationRules { get; init; }
}
