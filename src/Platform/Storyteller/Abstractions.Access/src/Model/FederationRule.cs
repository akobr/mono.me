using System.Collections.Generic;

namespace _42.Platform.Storyteller;

// What a platform token must carry to act as a Federated machine. A machine has 1 to 10 rules; any one may match.
public record FederationRule
{
    // FederatedIssuer.Name of the same project.
    public string Issuer { get; init; } = string.Empty;

    // The token's `aud` must be this string, or a one-element array holding it (ordinal).
    public string Audience { get; init; } = string.Empty;

    // Exact match of `sub`, or a prefix match when it ends with `:*` or `/*`.
    public string Subject { get; init; } = string.Empty;

    // Optional further pins. Key: a top-level claim name ("repository", "environment", "repository_owner_id")
    // or an RFC 6901 JSON pointer for nested claims ("/kubernetes.io/namespace").
    // Value: accepted values, any of them; every key must match (AND).
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Claims { get; init; }
}
