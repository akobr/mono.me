using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// A public RSA signing key as an RFC 7517 JSON Web Key: the base64url modulus and exponent, never a private part.
public sealed class PublicJsonWebKey
{
    [JsonPropertyName("kty")]
    public string Kty { get; init; } = "RSA";

    [JsonPropertyName("use")]
    public string Use { get; init; } = "sig";

    [JsonPropertyName("alg")]
    public string Alg { get; init; } = "RS256";

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("n")]
    public required string N { get; init; }

    [JsonPropertyName("e")]
    public required string E { get; init; }
}
