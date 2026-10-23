using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

internal sealed class ClientCredentialsCredential : CachedTokenCredential
{
    private const string AuthKitProvider = "AuthKit";

    // The client ids of AuthKit M2M applications; only these may use the token URL of discovery.
    private const string AuthKitClientIdPrefix = "client_";

    private readonly ClientCredentialsOptions _options;
    private Uri? _discoveredTokenUrl;

    private ClientCredentialsCredential(
        ClientCredentialsOptions options,
        StorytellerCredentialBinding? binding,
        Func<string, string?> getVariable,
        HttpMessageHandler? primaryHandler)
        : base(options.ConfigureHandler, options.TimeProvider, binding, getVariable, primaryHandler)
    {
        _options = options;
    }

    // tokenUrlName names the token URL in messages: the option, or the variable it was read from.
    public static ClientCredentialsCredential Create(
        ClientCredentialsOptions options,
        Func<string, string?> getVariable,
        string tokenUrlName = "ClientCredentialsOptions.TokenUrl")
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId, "options.ClientId");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientSecret, "options.ClientSecret");

        if (options.TokenUrl is not null)
        {
            LocalUrlRule.Require(options.TokenUrl, tokenUrlName);
        }
        else if (!options.ClientId.StartsWith(AuthKitClientIdPrefix, StringComparison.Ordinal))
        {
            // Discovery describes the user provider and is anonymous: a Keycloak or Entra ID secret never goes to WorkOS.
            throw StorytellerAuthenticationException.Configuration(
                $"Set {tokenUrlName} to the token URL that sform account machine create printed; only an AuthKit client id ({AuthKitClientIdPrefix}…) discovers it.");
        }

        if (options.Endpoint is not null)
        {
            LocalUrlRule.Require(options.Endpoint, "ClientCredentialsOptions.Endpoint");
        }

        return new ClientCredentialsCredential(options, null, getVariable, null);
    }

    public override StorytellerCredential Bind(StorytellerCredentialBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new ClientCredentialsCredential(_options, binding, GetVariable, PrimaryHandler);
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredential)} {{ Kind = ClientCredentials, ClientId = {_options.ClientId} }}";
    }

    internal override StorytellerCredential WithPrimaryHandler(HttpMessageHandler primaryHandler)
    {
        return new ClientCredentialsCredential(_options, Binding, GetVariable, primaryHandler);
    }

    protected override async Task<AccessToken> RequestTokenAsync(TokenEndpointClient client, CancellationToken cancellationToken)
    {
        var tokenUrl = _options.TokenUrl ?? (_discoveredTokenUrl ??= await DiscoverTokenUrlAsync(client, cancellationToken).ConfigureAwait(false));
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        };

        if (!string.IsNullOrWhiteSpace(_options.Scope))
        {
            form["scope"] = _options.Scope;
        }

        return await client.RequestTokenAsync(tokenUrl, form, cancellationToken).ConfigureAwait(false);
    }

    // Trusted only for AuthKit and an https domain without path, query or user info.
    private async Task<Uri> DiscoverTokenUrlAsync(TokenEndpointClient client, CancellationToken cancellationToken)
    {
        var endpoint = ResolveEndpoint(_options.Endpoint, "The AuthKit discovery of a client-credentials credential without a token URL");
        var discovery = await client.DiscoverSignInAsync(endpoint, cancellationToken).ConfigureAwait(false);
        var target = LocalUrlRule.Describe(endpoint);

        if (!string.Equals(discovery.Provider, AuthKitProvider, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Rejected,
                $"The platform at {target} signs in with {discovery.Provider ?? "an unknown provider"}, not AuthKit, so the token URL of {_options.ClientId} cannot be discovered; set the token URL.");
        }

        if (!Uri.TryCreate(discovery.AuthKitDomain, UriKind.Absolute, out var domain)
            || domain.Scheme != Uri.UriSchemeHttps
            || domain.UserInfo.Length > 0
            || domain.AbsolutePath != "/"
            || domain.Query.Length > 0
            || domain.Fragment.Length > 0)
        {
            throw new StorytellerAuthenticationException(
                StorytellerAuthenticationFailureReason.Rejected,
                $"The platform at {target} names no https AuthKit domain without a path; set the token URL.");
        }

        return new Uri($"{domain.GetLeftPart(UriPartial.Authority)}/oauth2/token", UriKind.Absolute);
    }
}
