using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// A public RSA signing key: never a private part.
public record class PublicJsonWebKey
{
    // RSA.
    [JsonProperty("kty", Required = Required.Always)]
    [JsonPropertyName("kty")]
    public required string Kty { get; init; }

    // The RFC 7638 thumbprint, the kid of the tokens it signs.
    [JsonProperty("kid", Required = Required.Always)]
    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    // sig.
    [JsonProperty("use", Required = Required.Always)]
    [JsonPropertyName("use")]
    public required string Use { get; init; }

    // RS256.
    [JsonProperty("alg", Required = Required.Always)]
    [JsonPropertyName("alg")]
    public required string Alg { get; init; }

    // The modulus, base64url.
    [JsonProperty("n", Required = Required.Always)]
    [JsonPropertyName("n")]
    public required string N { get; init; }

    // The public exponent, base64url.
    [JsonProperty("e", Required = Required.Always)]
    [JsonPropertyName("e")]
    public required string E { get; init; }
}
