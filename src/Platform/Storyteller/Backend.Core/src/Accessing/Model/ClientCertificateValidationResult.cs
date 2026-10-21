namespace _42.Platform.Storyteller.Accessing.Model;

public record ClientCertificateValidationResult(
    string Organization,
    string Project,
    string? MachineAccessId,
    IReadOnlyList<MachineScope> Scopes,
    string Thumbprint,
    ClientCertificateKind Kind,
    string? AnnotationKey,
    MachineCredentialKind? CredentialKind = null);
