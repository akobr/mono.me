using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// RFC 8414 metadata of the Storyteller issuer: where actors exchange user tokens and where the keys of Storyteller
// tokens are published. The OpenAPI document names properties through Newtonsoft and the response is written with
// System.Text.Json, so both name attributes are set.
public record class AuthorizationServerMetadata
{
    // MachineAuth:Federation:Issuer, the iss of every Storyteller token.
    [JsonProperty("issuer", Required = Required.Always)]
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    // The user token exchange, {base}/v1/access/token.
    [JsonProperty("token_endpoint", Required = Required.Always)]
    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    // The public signing keys, {base}/.well-known/jwks.json.
    [JsonProperty("jwks_uri", Required = Required.Always)]
    [JsonPropertyName("jwks_uri")]
    public required string JwksUri { get; init; }

    // Required by RFC 8414; empty, as there is no authorization endpoint.
    [JsonProperty("response_types_supported", Required = Required.Always)]
    [JsonPropertyName("response_types_supported")]
    public required string[] ResponseTypesSupported { get; init; }

    // urn:ietf:params:oauth:grant-type:token-exchange.
    [JsonProperty("grant_types_supported", Required = Required.Always)]
    [JsonPropertyName("grant_types_supported")]
    public required string[] GrantTypesSupported { get; init; }

    // private_key_jwt, and client_secret_post in the Development environment.
    [JsonProperty("token_endpoint_auth_methods_supported", Required = Required.Always)]
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public required string[] TokenEndpointAuthMethodsSupported { get; init; }

    // The algorithms a client assertion may be signed with.
    [JsonProperty("token_endpoint_auth_signing_alg_values_supported", Required = Required.Always)]
    [JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")]
    public required string[] TokenEndpointAuthSigningAlgValuesSupported { get; init; }
}
