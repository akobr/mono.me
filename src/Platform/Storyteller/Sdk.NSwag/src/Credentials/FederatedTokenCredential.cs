using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// RFC 8693 at the token endpoint of the target project, with a platform token got again for every exchange: from a file,
// or from the GitHub Actions runner.
internal sealed class FederatedTokenCredential : CachedTokenCredential
{
    private const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string TokenTypePrefix = "urn:ietf:params:oauth:token-type:";

    private static readonly string[] SubjectTokenTypes =
        [FederatedTokenOptions.JwtTokenType, FederatedTokenOptions.IdTokenType, FederatedTokenOptions.AccessTokenType];

    private readonly Exchange _exchange;
    private readonly PlatformTokenSource _source;
    private ExchangeTarget? _target;

    private FederatedTokenCredential(
        Exchange exchange,
        PlatformTokenSource source,
        StorytellerCredentialBinding? binding,
        Func<string, string?> getVariable,
        HttpMessageHandler? primaryHandler)
        : base(exchange.ConfigureHandler, exchange.TimeProvider, binding, getVariable, primaryHandler)
    {
        _exchange = exchange;
        _source = source;
    }

    public StorytellerSubjectTokenSource SubjectTokenSource => _source is GitHubActionsSource
        ? StorytellerSubjectTokenSource.GitHubActions
        : StorytellerSubjectTokenSource.File;

    // pathSource names the variable the subject token path came from, for messages; null for code.
    public static FederatedTokenCredential Create(FederatedTokenOptions options, string? pathSource, Func<string, string?> getVariable)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClientId, "options.ClientId");
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SubjectTokenPath, "options.SubjectTokenPath");

        var subjectTokenType = NormalizeSubjectTokenType(options.SubjectTokenType)
            ?? throw StorytellerAuthenticationException.Configuration(
                $"The subject token type must be jwt, id_token or access_token, or the full {TokenTypePrefix} URN.");

        if (options.Endpoint is not null)
        {
            StorytellerUrlRule.RequireEndpoint(options.Endpoint, "FederatedTokenOptions.Endpoint");
        }

        var exchange = new Exchange(
            options.ClientId,
            subjectTokenType,
            options.Scope,
            options.Endpoint,
            options.Organization,
            options.Project,
            options.ConfigureHandler,
            options.TimeProvider);

        return new FederatedTokenCredential(exchange, new FileSource(options.SubjectTokenPath, options.FileSystem ?? new FileSystem(), pathSource), null, getVariable, null);
    }

    // The ID token of the GitHub Actions job, for the audience of the token endpoint; reached through the variables only.
    // The request URL is checked by the URL rule already.
    public static FederatedTokenCredential CreateGitHubActions(
        string clientId,
        string subjectTokenType,
        string? scope,
        Uri requestUrl,
        string requestToken,
        TimeProvider? timeProvider,
        Func<string, string?> getVariable)
    {
        var exchange = new Exchange(clientId, subjectTokenType, scope, null, null, null, null, timeProvider);
        return new FederatedTokenCredential(exchange, new GitHubActionsSource(requestUrl, requestToken), null, getVariable, null);
    }

    public override StorytellerCredential Bind(StorytellerCredentialBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new FederatedTokenCredential(_exchange, _source, binding, GetVariable, PrimaryHandler);
    }

    // Part of the cache identity of the provider (CredentialFingerprint): the file-sourced text must not change.
    public override string ToString()
    {
        return _source switch
        {
            FileSource file => $"{nameof(StorytellerCredential)} {{ Kind = Federated, ClientId = {_exchange.ClientId}, SubjectTokenPath = {file.Path} }}",
            _ => $"{nameof(StorytellerCredential)} {{ Kind = Federated, ClientId = {_exchange.ClientId}, SubjectTokenSource = {StorytellerSubjectTokenSource.GitHubActions} }}",
        };
    }

    internal override StorytellerCredential WithPrimaryHandler(HttpMessageHandler primaryHandler)
    {
        return new FederatedTokenCredential(_exchange, _source, Binding, GetVariable, primaryHandler);
    }

    // jwt, id_token or access_token (any case), or one of the three URNs; null when the value is none of them.
    internal static string? NormalizeSubjectTokenType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FederatedTokenOptions.JwtTokenType;
        }

        var trimmed = value.Trim();
        return SubjectTokenTypes.FirstOrDefault(type =>
            string.Equals(trimmed, type, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, type[TokenTypePrefix.Length..], StringComparison.OrdinalIgnoreCase));
    }

    protected override async Task<AccessToken> RequestTokenAsync(TokenEndpointClient client, CancellationToken cancellationToken)
    {
        var target = _target ??= ResolveTarget();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = GrantType,
            ["client_id"] = _exchange.ClientId,

            // A projected Kubernetes token has no scheme; sent as it is.
            ["subject_token"] = await _source.GetTokenAsync(client, target.Audience, cancellationToken).ConfigureAwait(false),
            ["subject_token_type"] = _exchange.SubjectTokenType,
        };

        if (!string.IsNullOrWhiteSpace(_exchange.Scope))
        {
            form["scope"] = _exchange.Scope;
        }

        try
        {
            return await client.RequestTokenAsync(target.Url, form, cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerAuthenticationException exception) when (exception.Reason == StorytellerAuthenticationFailureReason.Rejected && _source is GitHubActionsSource)
        {
            // A refused GitHub token most likely has an audience no rule names, or the job meant client credentials.
            throw new StorytellerAuthenticationException(
                exception.Reason,
                $"{exception.Message.TrimEnd('.')}; the GitHub Actions token of this job was requested for the audience {target.Audience}, which a rule of the machine must name exactly. For client credentials, set {StorytellerVariables.CLIENT_SECRET} instead.",
                exception);
        }
    }

    private ExchangeTarget ResolveTarget()
    {
        var endpoint = ResolveEndpoint(_exchange.Endpoint, "A federated credential");
        var organization = _exchange.Organization;
        var project = _exchange.Project;

        if (organization is null || project is null)
        {
            var (boundOrganization, boundProject) = Binding is null
                ? UnboundTarget.ReadProject(GetVariable)
                : (Binding.Organization, Binding.Project);
            organization ??= boundOrganization;
            project ??= boundProject;
        }

        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project))
        {
            throw StorytellerAuthenticationException.Configuration(
                $"A federated credential exchanges at the token endpoint of a project: bind it to the organization and project, set them in its options, or set {StorytellerVariables.KEY}.");
        }

        var path = $"/v1/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/access/token";

        // The audience is built from the endpoint as written, as sform builds it and machine create prints it: the
        // server compares the token's aud with a rule's audience byte for byte. The exchange posts to the normalised URI.
        return new ExchangeTarget(
            new Uri($"{StorytellerUrlRule.TrimBase(endpoint)}{path}", UriKind.Absolute),
            $"{endpoint.OriginalString.TrimEnd('/')}{path}");
    }

    // What every copy shares, apart from the source of the platform token.
    private sealed record Exchange(
        string ClientId,
        string SubjectTokenType,
        string? Scope,
        Uri? Endpoint,
        string? Organization,
        string? Project,
        Action<SocketsHttpHandler>? ConfigureHandler,
        TimeProvider? TimeProvider);

    private sealed record ExchangeTarget(Uri Url, string Audience);

    // Asked again before every exchange: a file the platform renews, or an ID token that lives about 5 minutes.
    private abstract class PlatformTokenSource
    {
        public abstract Task<string> GetTokenAsync(TokenEndpointClient client, string audience, CancellationToken cancellationToken);
    }

    private sealed class FileSource(string path, IFileSystem fileSystem, string? pathSource) : PlatformTokenSource
    {
        public string Path => path;

        public override Task<string> GetTokenAsync(TokenEndpointClient client, string audience, CancellationToken cancellationToken)
        {
            return Task.FromResult(CredentialFiles.ReadText(fileSystem, path, pathSource, "platform token file"));
        }
    }

    private sealed class GitHubActionsSource(Uri requestUrl, string requestToken) : PlatformTokenSource
    {
        public override Task<string> GetTokenAsync(TokenEndpointClient client, string audience, CancellationToken cancellationToken)
        {
            return client.RequestGitHubIdTokenAsync(requestUrl, requestToken, audience, cancellationToken);
        }

        // The request token is a secret.
        public override string ToString()
        {
            return $"{nameof(GitHubActionsSource)} {{ RequestUrl = {StorytellerUrlRule.Describe(requestUrl)} }}";
        }
    }
}
