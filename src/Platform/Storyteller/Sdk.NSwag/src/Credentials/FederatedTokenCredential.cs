using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// RFC 8693 at the token endpoint of the target project, with the platform token read again for every exchange.
internal sealed class FederatedTokenCredential : CachedTokenCredential
{
    private const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string TokenTypePrefix = "urn:ietf:params:oauth:token-type:";

    private static readonly string[] SubjectTokenTypes =
        [FederatedTokenOptions.JwtTokenType, FederatedTokenOptions.IdTokenType, FederatedTokenOptions.AccessTokenType];

    private readonly FederatedTokenOptions _options;
    private readonly string _subjectTokenType;
    private readonly IFileSystem _fileSystem;
    private readonly string? _pathSource;
    private Uri? _exchangeUrl;

    private FederatedTokenCredential(
        FederatedTokenOptions options,
        string subjectTokenType,
        string? pathSource,
        StorytellerCredentialBinding? binding,
        Func<string, string?> getVariable,
        HttpMessageHandler? primaryHandler)
        : base(options.ConfigureHandler, options.TimeProvider, binding, getVariable, primaryHandler)
    {
        _options = options;
        _subjectTokenType = subjectTokenType;
        _fileSystem = options.FileSystem ?? new FileSystem();
        _pathSource = pathSource;
    }

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
            LocalUrlRule.RequireEndpoint(options.Endpoint, "FederatedTokenOptions.Endpoint");
        }

        return new FederatedTokenCredential(options, subjectTokenType, pathSource, null, getVariable, null);
    }

    public override StorytellerCredential Bind(StorytellerCredentialBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        return new FederatedTokenCredential(_options, _subjectTokenType, _pathSource, binding, GetVariable, PrimaryHandler);
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredential)} {{ Kind = Federated, ClientId = {_options.ClientId}, SubjectTokenPath = {_options.SubjectTokenPath} }}";
    }

    internal override StorytellerCredential WithPrimaryHandler(HttpMessageHandler primaryHandler)
    {
        return new FederatedTokenCredential(_options, _subjectTokenType, _pathSource, Binding, GetVariable, primaryHandler);
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
        var exchangeUrl = _exchangeUrl ??= ResolveExchangeUrl();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = GrantType,
            ["client_id"] = _options.ClientId,

            // A projected Kubernetes token has no scheme; sent as it is.
            ["subject_token"] = CredentialFiles.ReadText(_fileSystem, _options.SubjectTokenPath, _pathSource, "platform token file"),
            ["subject_token_type"] = _subjectTokenType,
        };

        if (!string.IsNullOrWhiteSpace(_options.Scope))
        {
            form["scope"] = _options.Scope;
        }

        return await client.RequestTokenAsync(exchangeUrl, form, cancellationToken).ConfigureAwait(false);
    }

    private Uri ResolveExchangeUrl()
    {
        var endpoint = ResolveEndpoint(_options.Endpoint, "A federated credential");
        var organization = _options.Organization;
        var project = _options.Project;

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

        return new Uri($"{LocalUrlRule.TrimBase(endpoint)}/v1/{Uri.EscapeDataString(organization)}/{Uri.EscapeDataString(project)}/access/token", UriKind.Absolute);
    }
}
