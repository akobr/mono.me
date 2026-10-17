using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// An RFC 8693 token request, sent as application/x-www-form-urlencoded. FederationHttp parses the form itself; this
// model describes it in the OpenAPI document, whose names come from Newtonsoft, so both name attributes are set.
public record class MachineTokenRequest
{
    public const string GrantTypeName = "grant_type";
    public const string SubjectTokenName = "subject_token";
    public const string SubjectTokenTypeName = "subject_token_type";
    public const string ClientIdName = "client_id";
    public const string AudienceName = "audience";
    public const string ResourceName = "resource";
    public const string ScopeName = "scope";
    public const string RequestedTokenTypeName = "requested_token_type";
    public const string ActorTokenName = "actor_token";
    public const string ActorTokenTypeName = "actor_token_type";

    // urn:ietf:params:oauth:grant-type:token-exchange
    [JsonProperty(GrantTypeName, Required = Required.Always)]
    [JsonPropertyName(GrantTypeName)]
    public string GrantType { get; init; } = string.Empty;

    // The platform token (a JWT) of the workload.
    [JsonProperty(SubjectTokenName, Required = Required.Always)]
    [JsonPropertyName(SubjectTokenName)]
    public string SubjectToken { get; init; } = string.Empty;

    // urn:ietf:params:oauth:token-type:jwt, :id_token or :access_token (Azure managed identity tokens).
    [JsonProperty(SubjectTokenTypeName, Required = Required.Always)]
    [JsonPropertyName(SubjectTokenTypeName)]
    public string SubjectTokenType { get; init; } = string.Empty;

    // The id of the Federated machine.
    [JsonProperty(ClientIdName, Required = Required.Always)]
    [JsonPropertyName(ClientIdName)]
    public string ClientId { get; init; } = string.Empty;

    // Repeatable; each value must be MachineAuth:Federation:Audience.
    [JsonProperty(AudienceName)]
    [JsonPropertyName(AudienceName)]
    public string? Audience { get; init; }

    // Repeatable; each value must be MachineAuth:Federation:Audience.
    [JsonProperty(ResourceName)]
    [JsonPropertyName(ResourceName)]
    public string? Resource { get; init; }

    // Space-separated Storyteller scopes, a subset of the machine's. None: every scope of the machine.
    [JsonProperty(ScopeName)]
    [JsonPropertyName(ScopeName)]
    public string? Scope { get; init; }

    // urn:ietf:params:oauth:token-type:access_token (the default) or :jwt.
    [JsonProperty(RequestedTokenTypeName)]
    [JsonPropertyName(RequestedTokenTypeName)]
    public string? RequestedTokenType { get; init; }
}
