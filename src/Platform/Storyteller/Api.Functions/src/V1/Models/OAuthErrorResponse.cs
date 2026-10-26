using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// An RFC 6749 section 5.2 error of the token endpoint, written by the handler instead of ErrorResponse.
public record class OAuthErrorResponse
{
    // invalid_request, invalid_client and invalid_grant (the user token exchange), unsupported_grant_type, invalid_scope,
    // invalid_target or temporarily_unavailable (503).
    [JsonProperty("error", Required = Required.Always)]
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonProperty("error_description")]
    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
