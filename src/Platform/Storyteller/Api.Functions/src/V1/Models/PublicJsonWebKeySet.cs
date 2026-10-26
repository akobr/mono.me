using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// An RFC 7517 JWK Set of the public keys Storyteller tokens are signed with. Named apart from RsaJsonWebKey, the
// recipient key of client encryption, because OpenAPI schemas are named by type.
public record class PublicJsonWebKeySet
{
    [JsonProperty("keys", Required = Required.Always)]
    [JsonPropertyName("keys")]
    public required PublicJsonWebKey[] Keys { get; init; }
}
