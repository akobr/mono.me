namespace _42.Platform.Storyteller;

public record MachineAuthenticationPolicy
{
    public MachineCredentialKind CredentialKind { get; init; }

    public int? CertificateLifetimeDays { get; init; }
}
