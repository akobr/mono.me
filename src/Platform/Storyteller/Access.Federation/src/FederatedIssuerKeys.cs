using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// The signing keys of a trusted issuer as one fetch (or the stored JWKS) returned them.
public sealed class FederatedIssuerKeys
{
    public FederatedIssuerKeys(IReadOnlyList<SecurityKey> keys, DateTimeOffset fetchedAt, bool isStatic)
    {
        Keys = keys;
        FetchedAt = fetchedAt;
        IsStatic = isStatic;
    }

    public IReadOnlyList<SecurityKey> Keys { get; }

    public DateTimeOffset FetchedAt { get; }

    // KeySource Jwks: the stored key set, never fetched again.
    public bool IsStatic { get; }
}
