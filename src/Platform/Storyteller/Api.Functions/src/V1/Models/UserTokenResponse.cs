using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// The RFC 8693 token response of the user token exchange: a Storyteller user token for Authorization: Bearer, which
// names the person and the actor. The OpenAPI document names properties through Newtonsoft and the response is written
// with System.Text.Json, so both name attributes are set.
public record class UserTokenResponse
{
    [JsonProperty("access_token", Required = Required.Always)]
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    // Always urn:ietf:params:oauth:token-type:access_token.
    [JsonProperty("issued_token_type", Required = Required.Always)]
    [JsonPropertyName("issued_token_type")]
    public required string IssuedTokenType { get; init; }

    // Always Bearer.
    [JsonProperty("token_type", Required = Required.Always)]
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    // Seconds until the token expires; never after the subject token.
    [JsonProperty("expires_in", Required = Required.Always)]
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    // The granted scopes, space separated.
    [JsonProperty("scope", Required = Required.Always)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }

    // Not in RFC 8693: the actor's restrictions, which the token carries too (NoSecretReveal unless the actor sets
    // AllowSecrets), so the actor can offer only what the API allows. Empty when there are none.
    [JsonProperty("restrictions", Required = Required.Always)]
    [JsonPropertyName("restrictions")]
    public required string[] Restrictions { get; init; }
}
