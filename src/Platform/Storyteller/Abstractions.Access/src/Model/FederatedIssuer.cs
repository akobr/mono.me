using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// An OIDC issuer a project trusts for its Federated machines. Members the API requires are plain strings,
// not C# required members, so a missing value reaches the validation (400) instead of failing the JSON input.
public record FederatedIssuer
{
    // Project-unique name, the route segment and the reference used by rules: ^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$
    // The route value wins over the body.
    public string Name { get; init; } = string.Empty;

    // The exact `iss` value, compared ordinally (a trailing slash matters). Immutable after creation.
    public string Issuer { get; init; } = string.Empty;

    public FederatedIssuerKeySource KeySource { get; init; } = FederatedIssuerKeySource.Discovery;

    // KeySource == JwksUri: where the JSON Web Key Set is fetched.
    public string? JwksUri { get; init; }

    // KeySource == Jwks: the JSON Web Key Set as JSON text (public keys only), for issuers without public discovery.
    public string? Jwks { get; init; }

    // The longest accepted `exp - iat` of a platform token, in seconds. Null: 3600. Range 60..86400.
    public int? MaxTokenLifetimeSeconds { get; init; }

    // Administrator-owned pins, same shape and matching as FederationRule.Claims, ANDed into every rule
    // of this issuer (for example repository_owner_id of the company's GitHub organization).
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? RequiredClaims { get; init; }

    public string? Description { get; init; }
}
