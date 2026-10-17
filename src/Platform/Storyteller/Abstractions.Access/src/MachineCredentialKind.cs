using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum MachineCredentialKind
{
    ApiKey = 0,
    Certificate,
    CertificateAndApiKey,

    // An identity provider client ID and secret, exchanged for a JWT (AuthKit M2M application).
    ClientCredentials,

    // No secret: the workload exchanges a token of a trusted OIDC issuer for a short-lived Storyteller token.
    Federated,
}
