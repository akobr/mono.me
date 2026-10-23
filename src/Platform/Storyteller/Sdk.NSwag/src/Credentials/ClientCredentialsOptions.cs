using System;
using System.Net.Http;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// A client_credentials grant. Without TokenUrl, a client id that starts with client_ discovers the AuthKit token URL
// from the endpoint; any other client id needs TokenUrl.
public sealed record ClientCredentialsOptions
{
    public Uri? TokenUrl { get; init; }

    public required string ClientId { get; init; }

    public required string ClientSecret { get; init; }

    public string? Scope { get; init; }

    // The API base for AuthKit discovery; the binding's when unset.
    public Uri? Endpoint { get; init; }

    public Action<SocketsHttpHandler>? ConfigureHandler { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    // A record prints its members; the secret must never reach a log.
    public override string ToString()
    {
        return $"{nameof(ClientCredentialsOptions)} {{ TokenUrl = {LocalUrlRule.Describe(TokenUrl)}, ClientId = {ClientId}, Scope = {Scope}, Endpoint = {LocalUrlRule.Describe(Endpoint)} }}";
    }
}
