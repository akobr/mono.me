using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace _42.Platform.Cli.AutomationTests.Tests.Machines;

// The workload platform of the federation cases (MCH-FED-*, MCH-CTX-01): an RSA key pair of the test, its JSON Web Key
// Set (the public key only, for account machine issuer set --jwks-file) and the platform tokens it signs (RS256 JWTs
// with the key's kid, one aud, iat, nbf and exp), which a federated machine exchanges for a Storyteller token through
// STORYTELLER_TOKEN_FILE. The key is new for every test and never leaves it.
public sealed class PlatformTokenSigner : IDisposable
{
    private readonly RSA key = RSA.Create(2048);

    public PlatformTokenSigner(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        KeyId = keyId;
    }

    // The kid of the key, in the key set and in the header of every token.
    public string KeyId { get; }

    // {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":…,"n":…,"e":…}]}: the public key as the issuer stores it.
    public string Jwks()
    {
        var parameters = key.ExportParameters(includePrivateParameters: false);
        var jwk = new JsonObject
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = "RS256",
            ["kid"] = KeyId,
            ["n"] = Base64Url(parameters.Modulus!),
            ["e"] = Base64Url(parameters.Exponent!),
        };
        return new JsonObject { ["keys"] = new JsonArray(jwk) }.ToJsonString();
    }

    // A platform token of the issuer for the audience and the subject, with the claims (string values) on top. It was
    // issued a minute ago, so a clock of the API behind the test's by less than that still takes it, and lives lifetime
    // from then (5 minutes by default; MCH-FED-08 signs one above the issuer's --max-lifetime).
    public string Sign(string issuer, string audience, string subject, IReadOnlyDictionary<string, string>? claims = null, TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var expires = issuedAt + (lifetime ?? TimeSpan.FromMinutes(5));
        var header = new JsonObject { ["alg"] = "RS256", ["typ"] = "JWT", ["kid"] = KeyId };
        var payload = new JsonObject
        {
            ["iss"] = issuer,
            ["sub"] = subject,
            ["aud"] = audience,
            ["iat"] = issuedAt.ToUnixTimeSeconds(),
            ["nbf"] = issuedAt.ToUnixTimeSeconds(),
            ["exp"] = expires.ToUnixTimeSeconds(),
        };

        foreach (var (name, value) in claims ?? new Dictionary<string, string>())
        {
            payload[name] = value;
        }

        var signingInput = $"{Base64Url(Encoding.UTF8.GetBytes(header.ToJsonString()))}.{Base64Url(Encoding.UTF8.GetBytes(payload.ToJsonString()))}";
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64Url(signature)}";
    }

    public void Dispose()
    {
        key.Dispose();
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
