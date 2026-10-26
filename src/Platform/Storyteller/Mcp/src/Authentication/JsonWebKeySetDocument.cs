using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// An RFC 7517 JWK Set of public keys only, as the server publishes the key of its client assertions.
public sealed class JsonWebKeySetDocument
{
    [JsonPropertyName("keys")]
    public required IReadOnlyList<PublicJsonWebKey> Keys { get; init; }
}
