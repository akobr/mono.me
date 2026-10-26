using System.Text.Json.Serialization;

using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// An RFC 8693 token request of an actor for a user token, sent as application/x-www-form-urlencoded. TokenExchangeHttp
// parses the form itself; this model describes it in the OpenAPI document, whose names come from Newtonsoft, so both
// name attributes are set. Every member is a string: the generated C# client sends a form through a string dictionary.
public record class UserTokenRequest
{
    public const string GrantTypeName = "grant_type";
    public const string SubjectTokenName = "subject_token";
    public const string SubjectTokenTypeName = "subject_token_type";
    public const string ActorTokenName = "actor_token";
    public const string ActorTokenTypeName = "actor_token_type";
    public const string ClientAssertionName = "client_assertion";
    public const string ClientAssertionTypeName = "client_assertion_type";
    public const string ClientIdName = "client_id";
    public const string ClientSecretName = "client_secret";
    public const string AudienceName = "audience";
    public const string ResourceName = "resource";
    public const string ScopeName = "scope";
    public const string RequestedTokenTypeName = "requested_token_type";

    // urn:ietf:params:oauth:grant-type:token-exchange
    [JsonProperty(GrantTypeName, Required = Required.Always)]
    [JsonPropertyName(GrantTypeName)]
    public string GrantType { get; init; } = string.Empty;

    // The person's token for the actor: a user token of the deployment's identity provider whose aud is one of the
    // actor's SubjectAudiences.
    [JsonProperty(SubjectTokenName, Required = Required.Always)]
    [JsonPropertyName(SubjectTokenName)]
    public string SubjectToken { get; init; } = string.Empty;

    // urn:ietf:params:oauth:token-type:access_token or :jwt.
    [JsonProperty(SubjectTokenTypeName, Required = Required.Always)]
    [JsonPropertyName(SubjectTokenTypeName)]
    public string SubjectTokenType { get; init; } = string.Empty;

    // The actor's workload identity token, for an actor with WorkloadIdentity.
    [JsonProperty(ActorTokenName)]
    [JsonPropertyName(ActorTokenName)]
    public string? ActorToken { get; init; }

    // urn:ietf:params:oauth:token-type:jwt or :access_token, with actor_token only.
    [JsonProperty(ActorTokenTypeName)]
    [JsonPropertyName(ActorTokenTypeName)]
    public string? ActorTokenType { get; init; }

    // An RFC 7523 client assertion (private_key_jwt), for an actor with ClientAssertion.
    [JsonProperty(ClientAssertionName)]
    [JsonPropertyName(ClientAssertionName)]
    public string? ClientAssertion { get; init; }

    // urn:ietf:params:oauth:client-assertion-type:jwt-bearer, with client_assertion only.
    [JsonProperty(ClientAssertionTypeName)]
    [JsonPropertyName(ClientAssertionTypeName)]
    public string? ClientAssertionType { get; init; }

    // The actor id (Auth:TokenExchange:Actors:Id).
    [JsonProperty(ClientIdName, Required = Required.Always)]
    [JsonPropertyName(ClientIdName)]
    public string ClientId { get; init; } = string.Empty;

    // The client secret (client_secret_post) of an actor with ClientSecretHash; the Development environment only.
    [JsonProperty(ClientSecretName)]
    [JsonPropertyName(ClientSecretName)]
    public string? ClientSecret { get; init; }

    // Repeatable; each value must be MachineAuth:Federation:Audience.
    [JsonProperty(AudienceName)]
    [JsonPropertyName(AudienceName)]
    public string? Audience { get; init; }

    // Repeatable; each value must be MachineAuth:Federation:Audience.
    [JsonProperty(ResourceName)]
    [JsonPropertyName(ResourceName)]
    public string? Resource { get; init; }

    // Space separated, within the actor's Scopes and with User.Impersonation. None: every scope of the actor.
    [JsonProperty(ScopeName)]
    [JsonPropertyName(ScopeName)]
    public string? Scope { get; init; }

    // urn:ietf:params:oauth:token-type:access_token, the only one issued.
    [JsonProperty(RequestedTokenTypeName)]
    [JsonPropertyName(RequestedTokenTypeName)]
    public string? RequestedTokenType { get; init; }
}
