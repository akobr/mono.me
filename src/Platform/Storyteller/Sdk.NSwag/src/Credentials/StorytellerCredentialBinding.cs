using System;
using System.Net.Http;
using _42.Platform.Storyteller.Sdk.Resilience;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// The context of a credential's requests. Values set in the credential's own options win; the binding fills the rest.
public sealed class StorytellerCredentialBinding
{
    public StorytellerCredentialBinding(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        Endpoint = endpoint;
    }

    // The API base, /api included.
    public Uri Endpoint { get; }

    // The target project of a federated exchange.
    public string? Organization { get; init; }

    public string? Project { get; init; }

    // For proxies and TLS settings of token and discovery requests.
    public Action<SocketsHttpHandler>? ConfigureHandler { get; init; }

    public TimeProvider? TimeProvider { get; init; }

    public StorytellerResilienceOptions? Resilience { get; init; }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredentialBinding)} {{ Endpoint = {StorytellerUrlRule.Describe(Endpoint)}, Organization = {Organization}, Project = {Project} }}";
    }
}
