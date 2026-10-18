using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

// Non-interactive sign-in of a machine with the credentials in STORYTELLER_* environment variables:
// an API key, a client certificate, both, client credentials, a token file, or the platform token a
// federated machine exchanges. Nothing is stored on disk; a client-credentials or exchanged token lives
// in memory for the process.
public sealed class MachineAuthenticationService : IAuthenticationService, IMachineSignIn, IDisposable
{
    private const string ApiKeyScheme = "ApiKey ";
    private const string BearerScheme = "Bearer";
    private const string ClientCredentialsGrant = "client_credentials";

    // The client IDs of AuthKit M2M applications; only these may use the token URL of AuthKit discovery.
    private const string AuthKitClientIdPrefix = "client_";

    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FallbackTokenLifetime = TimeSpan.FromMinutes(5);

    // Longer lifetimes are cut: the process ends long before, and a huge value would overflow.
    private static readonly long MaxTokenLifetimeSeconds = (long)TimeSpan.FromDays(1).TotalSeconds;

    private readonly IFileSystem _fileSystem;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuthenticationConfigurationResolver _resolver;
    private readonly TimeProvider _time;
    private readonly Lazy<LoadedCredential> _credential;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ClientToken? _token;
    private volatile ExchangeTarget? _target;

    public MachineAuthenticationService(
        MachineSignInSettings settings,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        IAuthenticationConfigurationResolver resolver)
        : this(settings, fileSystem, httpClientFactory, resolver, TimeProvider.System)
    {
    }

    internal MachineAuthenticationService(
        MachineSignInSettings settings,
        IFileSystem fileSystem,
        IHttpClientFactory httpClientFactory,
        IAuthenticationConfigurationResolver resolver,
        TimeProvider time)
    {
        Settings = settings;
        _fileSystem = fileSystem;
        _httpClientFactory = httpClientFactory;
        _resolver = resolver;
        _time = time;
        _credential = new Lazy<LoadedCredential>(Load);
    }

    public MachineSignInSettings Settings { get; }

    public bool IsConfigured => Settings.IsConfigured;

    public MachineIdentity GetIdentity()
    {
        return GetCredential().Identity;
    }

    public MachineIdentity? TryGetIdentity()
    {
        return IsConfigured && _credential.Value.Failure is null ? _credential.Value.Identity : null;
    }

    public X509Certificate2? TryGetCertificate()
    {
        return IsConfigured && _credential.Value.Failure is null ? _credential.Value.Certificate : null;
    }

    public async Task<MachineIdentity> SignInAsync(CancellationToken cancellationToken = default)
    {
        var credential = GetCredential();

        return credential.Kind switch
        {
            MachineSignInKind.ClientCredentials => credential.Identity with { TokenExpiresAt = (await GetTokenAsync(null, null, cancellationToken)).ExpiresAt },
            MachineSignInKind.TokenFile => credential.Identity with { TokenExpiresAt = ReadTokenFileExpiry() },
            MachineSignInKind.Federated => await SignInFederatedAsync(credential.Identity, cancellationToken),
            _ => credential.Identity,
        };
    }

    public async Task<SignedInUser?> GetSignedInUserAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var identity = await SignInAsync(cancellationToken);
        var id = identity.MachineId ?? "machine";
        var name = identity.MachineId is null ? "machine" : $"machine {identity.MachineId}";
        var project = identity.Organization is null ? string.Empty : $" ({identity.Organization}.{identity.Project})";
        return new SignedInUser(id, name + project, null);
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var credential = GetCredential();

        return credential.Kind switch
        {
            MachineSignInKind.ApiKey or MachineSignInKind.CertificateAndApiKey => ApiKeyScheme + Settings.ApiKey,

            // The certificate goes in the TLS handshake (or the development header); no Authorization header.
            MachineSignInKind.Certificate => null,
            MachineSignInKind.ClientCredentials => (await GetTokenAsync(null, null, cancellationToken)).AccessToken,
            MachineSignInKind.TokenFile => ReadTokenFile(),

            // Only for the target a command bound: sform account ca and the user-only commands must not
            // exchange, nor ask GitHub for a token, just because STORYTELLER_PROJECT is set.
            MachineSignInKind.Federated when _target is { } target => (await GetTokenAsync(target, null, cancellationToken)).AccessToken,
            _ => null,
        };
    }

    // Kinds other than Federated never read it.
    public void BindTarget(string organization, string project)
    {
        _target = new ExchangeTarget(organization, project);
    }

    public async Task<string?> RenewAccessTokenAsync(string rejectedToken, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || GetCredential().Kind != MachineSignInKind.Federated || _target is not { } target)
        {
            return null;
        }

        return (await GetTokenAsync(target, rejectedToken, cancellationToken)).AccessToken;
    }

    public Task<SignedInUser> LoginWithDeviceCodeAsync(
        Func<DeviceCodePrompt, Task> onPrompt,
        Func<IReadOnlyList<OrganizationChoice>, Task<string>>? selectOrganization = null,
        CancellationToken cancellationToken = default)
    {
        throw new AuthenticationException(
            AuthenticationFailureReason.Configuration,
            $"sform signs in with the machine credentials in {string.Join(", ", Settings.CredentialVariables)}; unset them to sign in as a user.");
    }

    // Nothing is stored, so there is nothing to forget.
    public Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _gate.Dispose();

        if (_credential.IsValueCreated)
        {
            _credential.Value.Certificate?.Dispose();
        }
    }

    private static AuthenticationException Invalid(string message)
    {
        return new AuthenticationException(AuthenticationFailureReason.Configuration, message);
    }

    private static string DescribeTokenError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return string.Empty;
            }

            var error = ReadString(root, "error");
            var description = (ReadString(root, "error_description") ?? ReadString(root, "message"))?.TrimEnd('.');

            return (error, description) switch
            {
                (not null, not null) => $"{error}: {description}",
                (not null, null) => error,
                (null, not null) => description,
                _ => string.Empty,
            };
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    private static string? ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
    }

    // { "count": ..., "value": "<JWT>" }
    private static string? ReadGitHubToken(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object ? ReadString(document.RootElement, "value") : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? ReadLifetime(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var value))
        {
            return null;
        }

        // Some providers send the number as a string.
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var seconds) => seconds,
            JsonValueKind.String when long.TryParse(value.GetString(), out var seconds) => seconds,
            _ => null,
        };
    }

    private LoadedCredential GetCredential()
    {
        if (!IsConfigured)
        {
            throw Invalid("No machine sign-in variable is set.");
        }

        var credential = _credential.Value;
        var failure = credential.Failure;

        // A new exception on every call: the cached one keeps the reason and the message.
        return failure is null
            ? credential
            : throw new AuthenticationException(failure.Reason, failure.Message, failure.InnerException);
    }

    private LoadedCredential Load()
    {
        X509Certificate2? certificate = null;

        try
        {
            var kind = Settings.GetKind();

            // Messages say where a platform token from GitHub Actions came from; names only, never values.
            IReadOnlyList<string> variables = kind == MachineSignInKind.Federated && Settings.GetFederatedTokenSource() == FederatedTokenSource.GitHubActions
                ? [.. Settings.CredentialVariables, GitHubActionsVariables.ID_TOKEN_REQUEST_URL, GitHubActionsVariables.ID_TOKEN_REQUEST_TOKEN]
                : Settings.CredentialVariables;
            var key = kind is MachineSignInKind.ApiKey or MachineSignInKind.CertificateAndApiKey
                ? MachineApiKey.TryParse(Settings.ApiKey)
                : null;
            MachineCertificateIdentity? certificateIdentity = null;

            if (kind is MachineSignInKind.Certificate or MachineSignInKind.CertificateAndApiKey)
            {
                certificate = MachineCertificate.Load(_fileSystem, Settings.CertificateFile!, Settings.CertificatePassword);
                certificateIdentity = MachineCertificate.ReadIdentity(certificate);
                EnsureCertificateMatchesKey(kind, certificateIdentity, key);
            }

            var identity = kind switch
            {
                MachineSignInKind.ApiKey => new MachineIdentity(kind, variables, key?.MachineId, key?.Organization, key?.Project),
                MachineSignInKind.Certificate => new MachineIdentity(kind, variables, certificateIdentity?.MachineId, certificateIdentity?.Organization, certificateIdentity?.Project),

                // A machine certificate names the machine; with a shared one the key does.
                MachineSignInKind.CertificateAndApiKey when certificateIdentity is { IsShared: false } =>
                    new MachineIdentity(kind, variables, certificateIdentity.MachineId, certificateIdentity.Organization, certificateIdentity.Project),
                MachineSignInKind.CertificateAndApiKey => new MachineIdentity(
                    kind,
                    variables,
                    key?.MachineId,
                    key?.Organization ?? certificateIdentity?.Organization,
                    key?.Project ?? certificateIdentity?.Project),

                // The credential names no project; a federated machine gets its project from the command.
                MachineSignInKind.ClientCredentials or MachineSignInKind.Federated => new MachineIdentity(kind, variables, Settings.ClientId, null, null),
                _ => new MachineIdentity(kind, variables, null, null, null),
            };

            return new LoadedCredential(kind, identity, certificate, null);
        }
        catch (AuthenticationException exception)
        {
            certificate?.Dispose();
            return new LoadedCredential(default, null!, null, exception);
        }
    }

    private static void EnsureCertificateMatchesKey(MachineSignInKind kind, MachineCertificateIdentity? certificate, MachineApiKey? key)
    {
        if (certificate is { IsShared: true } && kind != MachineSignInKind.CertificateAndApiKey)
        {
            throw Invalid($"A shared certificate signs in only together with its machine's API key; set {StorytellerVariables.API_KEY} too.");
        }

        if (certificate is null || key is null)
        {
            return;
        }

        var sameProject = string.Equals(certificate.Organization, key.Organization, StringComparison.OrdinalIgnoreCase)
            && string.Equals(certificate.Project, key.Project, StringComparison.OrdinalIgnoreCase);
        var sameMachine = certificate.IsShared || string.Equals(certificate.MachineId, key.MachineId, StringComparison.OrdinalIgnoreCase);

        if (!sameProject)
        {
            throw Invalid(
                $"The certificate in {StorytellerVariables.CERTIFICATE_FILE} belongs to {certificate.Organization}.{certificate.Project} but the API key to {key.Organization}.{key.Project}.");
        }

        if (!sameMachine)
        {
            throw Invalid(
                $"The certificate in {StorytellerVariables.CERTIFICATE_FILE} belongs to machine {certificate.MachineId} but the API key to machine {key.MachineId}.");
        }
    }

    private string ReadTokenFile()
    {
        var path = Settings.TokenFile!;
        var content = ReadFile(path, StorytellerVariables.TOKEN_FILE, "token file");

        // "Bearer <token>" as an Authorization header value works too.
        if (content.StartsWith(BearerScheme, StringComparison.OrdinalIgnoreCase)
            && (content.Length == BearerScheme.Length || char.IsWhiteSpace(content[BearerScheme.Length])))
        {
            content = content[BearerScheme.Length..].Trim();
        }

        return content.Length > 0
            ? content
            : throw Invalid($"The token file {path} in {StorytellerVariables.TOKEN_FILE} is empty.");
    }

    // The platform token of a federated machine, sent as it is: a projected Kubernetes token has no scheme.
    private string ReadSubjectTokenFile()
    {
        var path = Settings.SubjectTokenFile!;
        var content = ReadFile(path, StorytellerVariables.SUBJECT_TOKEN_FILE, "platform token file");

        return content.Length > 0
            ? content
            : throw Invalid($"The platform token file {path} in {StorytellerVariables.SUBJECT_TOKEN_FILE} is empty.");
    }

    // Read before every use, never cached: the file may be renewed while sform runs.
    private string ReadFile(string path, string variable, string description)
    {
        try
        {
            if (!_fileSystem.File.Exists(path))
            {
                throw Invalid($"The {description} {path} in {variable} does not exist.");
            }

            return _fileSystem.File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.Configuration,
                $"The {description} {path} in {variable} could not be read.",
                exception);
        }
    }

    // A token that has visibly expired fails the sign-in here instead of with a 401 on the next call.
    private DateTimeOffset? ReadTokenFileExpiry()
    {
        var expiresAt = JwtExpiry.Read(ReadTokenFile());

        return expiresAt is null || expiresAt > _time.GetUtcNow()
            ? expiresAt
            : throw new AuthenticationException(
                AuthenticationFailureReason.Expired,
                $"The token in {StorytellerVariables.TOKEN_FILE} expired at {expiresAt:O}.");
    }

    // A command binds its target; without one, STORYTELLER_PROJECT names the project to sign in to.
    private async Task<MachineIdentity> SignInFederatedAsync(MachineIdentity identity, CancellationToken cancellationToken)
    {
        var target = _target
            ?? (MachineSignInSettings.TrySplitProject(Settings.Project, out var organization, out var project) ? new ExchangeTarget(organization, project) : null)
            ?? throw Invalid($"No project is set for the machine: set {StorytellerVariables.PROJECT}=<organization>.<project> or pass -p.");
        var token = await GetTokenAsync(target, null, cancellationToken);

        return identity with { Organization = target.Organization, Project = target.Project, TokenExpiresAt = token.ExpiresAt };
    }

    // The one token of the process: of client credentials (no target) or of the exchange for a target.
    // It is replaced only by a successful request. A token the API refused is dropped first, unless a newer
    // one replaced it already, so a burst of 401s exchanges once.
    private async Task<ClientToken> GetTokenAsync(ExchangeTarget? target, string? rejectedToken, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (rejectedToken is not null && string.Equals(_token?.AccessToken, rejectedToken, StringComparison.Ordinal))
            {
                _token = null;
            }

            if (_token is null || _token.ExpiresAt - RefreshMargin <= _time.GetUtcNow() || _token.Target != target?.Key)
            {
                _token = target is null
                    ? await RequestClientTokenAsync(cancellationToken)
                    : await ExchangeTokenAsync(target, cancellationToken);
            }

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ClientToken> RequestClientTokenAsync(CancellationToken cancellationToken)
    {
        var tokenUrl = await GetTokenUrlAsync(cancellationToken);
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = ClientCredentialsGrant,
            ["client_id"] = Settings.ClientId!,
            ["client_secret"] = Settings.ClientSecret!,
        };

        if (Settings.TokenScope is not null)
        {
            form["scope"] = Settings.TokenScope;
        }

        return await PostTokenRequestAsync(tokenUrl, form, null, null, cancellationToken);
    }

    // RFC 8693 at the token endpoint of the target's project, with a platform token got again for every exchange.
    private async Task<ClientToken> ExchangeTokenAsync(ExchangeTarget target, CancellationToken cancellationToken)
    {
        // Built like the server builds it, as the audience a rule names must equal it byte for byte.
        var endpoint = $"{Settings.ApiUrl!.TrimEnd('/')}/v1/{Uri.EscapeDataString(target.Organization)}/{Uri.EscapeDataString(target.Project)}/access/token";
        var isGitHub = Settings.GetFederatedTokenSource() == FederatedTokenSource.GitHubActions;
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = TokenExchange.GrantType,
            ["client_id"] = Settings.ClientId!,
            ["subject_token"] = isGitHub ? await RequestGitHubTokenAsync(endpoint, cancellationToken) : ReadSubjectTokenFile(),

            // Validated by MachineSignInSettings.GetKind.
            ["subject_token_type"] = MachineSignInSettings.NormalizeSubjectTokenType(Settings.SubjectTokenType)!,
        };

        if (Settings.TokenScope is not null)
        {
            form["scope"] = Settings.TokenScope;
        }

        // A refused GitHub token most likely has an audience no rule names, or the job meant client credentials.
        var deniedHint = isGitHub
            ? $"; the GitHub Actions token of this job was requested for the audience {endpoint}, which a rule of the machine must name exactly. For client credentials, set {StorytellerVariables.CLIENT_SECRET} instead"
            : null;

        return await PostTokenRequestAsync(new Uri(endpoint, UriKind.Absolute), form, target.Key, deniedHint, cancellationToken);
    }

    // The job's ID token for the audience. It lives about 5 minutes, so it is requested for every exchange.
    private async Task<string> RequestGitHubTokenAsync(string audience, CancellationToken cancellationToken)
    {
        // Validated by MachineSignInSettings.GetKind; the runner's URL has a query already (api-version=2.0).
        var requestUrl = Settings.GitHubTokenRequestUrl!;
        var separator = new Uri(requestUrl, UriKind.Absolute).Query.Length > 0 ? "&" : "?";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"{requestUrl}{separator}audience={Uri.EscapeDataString(audience)}", UriKind.Absolute));

        // On this request only, never on the client: the request token must not reach the token endpoint.
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, Settings.GitHubTokenRequestToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // The client follows no redirect, so the request token never goes to another host.
        using var httpClient = _httpClientFactory.CreateClient(nameof(MachineAuthenticationService));
        HttpResponseMessage response;

        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.ServiceError,
                "The GitHub Actions ID token could not be requested.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationException(
                    AuthenticationFailureReason.ServiceError,
                    $"The GitHub Actions ID token request failed with {(int)response.StatusCode}.");
            }

            return ReadGitHubToken(await response.Content.ReadAsStringAsync(cancellationToken))
                ?? throw new AuthenticationException(
                    AuthenticationFailureReason.ServiceError,
                    "The GitHub Actions ID token request returned no token.");
        }
    }

    // A form without Authorization, on the client that follows no redirect: a redirect would resend it to another host.
    private async Task<ClientToken> PostTokenRequestAsync(
        Uri tokenUrl,
        Dictionary<string, string> form,
        string? target,
        string? deniedHint,
        CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient(nameof(MachineAuthenticationService));
        HttpResponseMessage response;

        try
        {
            using var content = new FormUrlEncodedContent(form);
            response = await httpClient.PostAsync(tokenUrl, content, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.ServiceError,
                $"The token endpoint {tokenUrl} could not be reached.",
                exception);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                var detail = DescribeTokenError(body);
                throw new AuthenticationException(
                    AuthenticationFailureReason.Denied,
                    $"The token request to {tokenUrl} was rejected{(detail.Length > 0 ? $": {detail}" : string.Empty)}{deniedHint}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationException(
                    AuthenticationFailureReason.ServiceError,
                    $"The token request to {tokenUrl} failed with {(int)response.StatusCode}.");
            }

            return ReadToken(body, tokenUrl, target);
        }
    }

    private ClientToken ReadToken(string body, Uri tokenUrl, string? target)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && ReadString(root, "access_token") is { } accessToken
                && (ReadString(root, "token_type") is not { } tokenType || string.Equals(tokenType, "Bearer", StringComparison.OrdinalIgnoreCase)))
            {
                var now = _time.GetUtcNow();
                var lifetime = ReadLifetime(root);
                var expiresAt = lifetime > 0
                    ? now.AddSeconds(Math.Min(lifetime.Value, MaxTokenLifetimeSeconds))
                    : JwtExpiry.Read(accessToken) ?? now + FallbackTokenLifetime;
                return new ClientToken(accessToken, expiresAt, target);
            }
        }
        catch (JsonException)
        {
            // Reported below, like a body without a token.
        }

        throw new AuthenticationException(
            AuthenticationFailureReason.ServiceError,
            $"The token endpoint {tokenUrl} returned no bearer token.");
    }

    private async Task<Uri> GetTokenUrlAsync(CancellationToken cancellationToken)
    {
        if (Settings.TokenUrl is not null)
        {
            // Validated by MachineSignInSettings.GetKind.
            return new Uri(Settings.TokenUrl, UriKind.Absolute);
        }

        // Discovery describes the user provider and is anonymous, so its token URL is trusted only
        // for an AuthKit client ID and an https domain: a Keycloak or Entra ID secret never goes to WorkOS.
        if (Settings.ClientId!.StartsWith(AuthKitClientIdPrefix, StringComparison.Ordinal))
        {
            try
            {
                var resolved = await _resolver.ResolveAsync(cancellationToken);

                if (resolved.Provider == AuthenticationProvider.AuthKit
                    && Uri.TryCreate(resolved.AuthKitDomain, UriKind.Absolute, out var domain)
                    && domain.Scheme == Uri.UriSchemeHttps)
                {
                    return new Uri($"{domain.AbsoluteUri.TrimEnd('/')}/oauth2/token");
                }
            }
            catch (AuthenticationException)
            {
                // Not discoverable; reported below.
            }
        }

        throw Invalid($"Set {StorytellerVariables.TOKEN_URL} to the token URL that sform account machine create printed.");
    }

    // Target is the organization.project of a federated token, null for client credentials.
    private sealed record ClientToken(string AccessToken, DateTimeOffset ExpiresAt, string? Target)
    {
        // A record prints its members; the token must never reach a log.
        public override string ToString()
        {
            return $"{nameof(ClientToken)} {{ ExpiresAt = {ExpiresAt:O}, Target = {Target} }}";
        }
    }

    // The project of a federated exchange; its token endpoint belongs to that project.
    private sealed record ExchangeTarget(string Organization, string Project)
    {
        public string Key => $"{Organization}.{Project}";
    }

    private sealed record LoadedCredential(
        MachineSignInKind Kind,
        MachineIdentity Identity,
        X509Certificate2? Certificate,
        AuthenticationException? Failure);
}
