using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

// Non-interactive sign-in of a machine with the credentials in STORYTELLER_* environment variables:
// an API key, a client certificate, both, client credentials, or a token file. Nothing is stored on
// disk; a client-credentials token lives in memory for the process.
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
            MachineSignInKind.ClientCredentials => credential.Identity with { TokenExpiresAt = (await GetClientTokenAsync(cancellationToken)).ExpiresAt },
            MachineSignInKind.TokenFile => credential.Identity with { TokenExpiresAt = ReadTokenFileExpiry() },
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
            MachineSignInKind.ClientCredentials => (await GetClientTokenAsync(cancellationToken)).AccessToken,
            MachineSignInKind.TokenFile => ReadTokenFile(),
            _ => null,
        };
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
            var variables = Settings.CredentialVariables;
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
                MachineSignInKind.ClientCredentials => new MachineIdentity(kind, variables, Settings.ClientId, null, null),
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
        string content;

        try
        {
            if (!_fileSystem.File.Exists(path))
            {
                throw Invalid($"The token file {path} in {StorytellerVariables.TOKEN_FILE} does not exist.");
            }

            content = _fileSystem.File.ReadAllText(path).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.Configuration,
                $"The token file {path} in {StorytellerVariables.TOKEN_FILE} could not be read.",
                exception);
        }

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

    private async Task<ClientToken> GetClientTokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (_token is null || _token.ExpiresAt - RefreshMargin <= _time.GetUtcNow())
            {
                _token = await RequestClientTokenAsync(cancellationToken);
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
                    $"The token request to {tokenUrl} was rejected{(detail.Length > 0 ? $": {detail}" : string.Empty)}.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new AuthenticationException(
                    AuthenticationFailureReason.ServiceError,
                    $"The token request to {tokenUrl} failed with {(int)response.StatusCode}.");
            }

            return ReadToken(body, tokenUrl);
        }
    }

    private ClientToken ReadToken(string body, Uri tokenUrl)
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
                return new ClientToken(accessToken, expiresAt);
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

    private sealed record ClientToken(string AccessToken, DateTimeOffset ExpiresAt)
    {
        // A record prints its members; the token must never reach a log.
        public override string ToString()
        {
            return $"{nameof(ClientToken)} {{ ExpiresAt = {ExpiresAt:O} }}";
        }
    }

    private sealed record LoadedCredential(
        MachineSignInKind Kind,
        MachineIdentity Identity,
        X509Certificate2? Certificate,
        AuthenticationException? Failure);
}
