namespace _42.Platform.Cli.Authentication;

// The identity in a Storyteller client certificate: the URI SAN
// spiffe://{trust domain}/org/{organization}/project/{project}/machine/{id} or …/shared/{label}.
public sealed record MachineCertificateIdentity(string Organization, string Project, string? MachineId, string? SharedLabel)
{
    public bool IsShared => SharedLabel is not null;
}
