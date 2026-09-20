namespace _42.Platform.Storyteller.Accessing.Model;

public record ClientCertificateValidationResult(
    string Organization,
    string Project,
    string? MachineAccessId,
    MachineAccessScope Scope,
    string Thumbprint,
    ClientCertificateKind Kind,
    string? AnnotationKey);
