namespace _42.Platform.Storyteller.Api;

public class FunctionContextItemKeys
{
    public const string CachedClaims = nameof(CachedClaims);
    public const string MachineIdentity = nameof(MachineIdentity);

    // The MachineCredentialKind a machine identity was proven with, when it came from a bearer token.
    public const string MachineCredentialKind = nameof(MachineCredentialKind);

    // The organization and project a machine bearer token names (Storyteller tokens of Federated machines).
    public const string MachineOrganization = nameof(MachineOrganization);
    public const string MachineProject = nameof(MachineProject);
    public const string PresentingCertificateThumbprint = nameof(PresentingCertificateThumbprint);

    // The stored MachineAccess of a machine identity, read once by the access check of the request; the secret rights
    // and the view transfer reuse it.
    public const string StoredMachine = nameof(StoredMachine);

    // The actor that acts for the user of a Storyteller user token (its act.sub, a string), and the token's restrictions
    // plus those of the actor's current settings (ActorRestrictions, an IReadOnlyList<string>). Set only from the
    // Delegation of the Storyteller token validator, never from claims.
    public const string DelegatedActor = nameof(DelegatedActor);
    public const string DelegatedRestrictions = nameof(DelegatedRestrictions);
}
