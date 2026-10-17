using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// The RFC 8693 token response: a Storyteller token for Authorization: Bearer. The OpenAPI document names properties
// through Newtonsoft and the response is written with System.Text.Json, so both name attributes are set.
public record class MachineTokenResponse
{
    [JsonProperty("access_token", Required = Required.Always)]
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    // Echoes requested_token_type: urn:ietf:params:oauth:token-type:access_token or :jwt.
    [JsonProperty("issued_token_type", Required = Required.Always)]
    [JsonPropertyName("issued_token_type")]
    public required string IssuedTokenType { get; init; }

    // Always Bearer.
    [JsonProperty("token_type", Required = Required.Always)]
    [JsonPropertyName("token_type")]
    public required string TokenType { get; init; }

    // Seconds until the token expires.
    [JsonProperty("expires_in", Required = Required.Always)]
    [JsonPropertyName("expires_in")]
    public required int ExpiresIn { get; init; }

    // The granted Storyteller scopes, space separated.
    [JsonProperty("scope", Required = Required.Always)]
    [JsonPropertyName("scope")]
    public required string Scope { get; init; }
}
