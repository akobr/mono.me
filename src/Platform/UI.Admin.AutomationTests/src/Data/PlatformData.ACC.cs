using System.Text.Json;

namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The Access section's generators: the names of trusted issuers and the labels of shared certificates, a GitHub
// Actions subject of the run, and a JSON Web Key Set with one public RSA key for a trusted issuer whose keys are
// pasted.
public sealed partial class PlatformData
{
    // "at-<runId>-iss-<hex4>-<word>-<word>": 1 to 63 lower-case letters, digits or '-', starting and ending with a letter
    // or a digit, as a trusted issuer's name must be.
    public string TrustedIssuerName()
    {
        return Slug("iss");
    }

    // "at-<runId>-cert-<hex4>-<word>-<word>", the label (and the .pfx name) of a shared certificate.
    public string SharedCertificateLabel()
    {
        return Slug("cert");
    }

    // A concrete GitHub Actions subject of the run, without a <placeholder>:
    // "repo:ui-tests/<slug>:environment:<environment>".
    public string GitHubSubject(string environment = "production")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environment);
        return $"repo:ui-tests/{Slug()}:environment:{environment}";
    }

    // {"keys":[{"kty":"RSA","kid":"<slug>","n":"…","e":"AQAB"}]}: a compact JSON Web Key Set with one generated public
    // RSA key of 2048 bits and its kid, without any private member (d, p, q, dp, dq, qi), which the API refuses.
    public string PublicRsaJwks()
    {
        var jwk = RsaJwk(2048, Slug("kid"));
        return JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal) { ["keys"] = new[] { jwk } });
    }
}
