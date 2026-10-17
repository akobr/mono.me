using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// Where the public keys of a trusted issuer come from.
[JsonConverter(typeof(StringEnumConverter))]
public enum FederatedIssuerKeySource
{
    // {Issuer}/.well-known/openid-configuration, then its jwks_uri.
    Discovery = 0,

    // Keys public but no discovery document (for example a cluster with --service-account-jwks-uri).
    JwksUri,

    // The stored Jwks; nothing is fetched (self-managed Kubernetes).
    Jwks,
}
