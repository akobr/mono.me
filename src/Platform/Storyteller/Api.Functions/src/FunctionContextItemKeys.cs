namespace _42.Platform.Storyteller.Api;

public class FunctionContextItemKeys
{
    public const string CachedClaims = nameof(CachedClaims);
    public const string MachineIdentity = nameof(MachineIdentity);

    // The MachineCredentialKind a machine identity was proven with, when it came from a bearer token.
    public const string MachineCredentialKind = nameof(MachineCredentialKind);
    public const string PresentingCertificateThumbprint = nameof(PresentingCertificateThumbprint);
}
