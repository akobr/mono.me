using System;
using System.IO;
using System.IO.Abstractions;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Authentication;

// Picks the sign-in provider: authentication.provider in app.config.json wins, then the cached
// discovery answer for the current baseUrl, then GET v1/auth/configuration. A server without the
// endpoint falls back to the Entra ID settings in app.config.json.
public sealed class AuthenticationConfigurationResolver : IAuthenticationConfigurationResolver
{
    private const string DiscoveryPath = "v1/auth/configuration";

    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions ServerJson = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = true };

    private readonly AuthenticationOptions _options;
    private readonly string _baseUrl;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger<AuthenticationConfigurationResolver> _logger;
    private readonly string _cachePath;

    public AuthenticationConfigurationResolver(
        IOptions<AuthenticationOptions> options,
        IOptions<GeneralOptions> generalOptions,
        IHttpClientFactory httpClientFactory,
        IFileSystem fileSystem,
        ILogger<AuthenticationConfigurationResolver> logger)
        : this(
            options,
            generalOptions,
            httpClientFactory,
            fileSystem,
            logger,
            fileSystem.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? Environment.CurrentDirectory)
    {
    }

    internal AuthenticationConfigurationResolver(
        IOptions<AuthenticationOptions> options,
        IOptions<GeneralOptions> generalOptions,
        IHttpClientFactory httpClientFactory,
        IFileSystem fileSystem,
        ILogger<AuthenticationConfigurationResolver> logger,
        string cacheDirectory)
    {
        _options = options.Value;
        _baseUrl = (generalOptions.Value.BaseUrl ?? string.Empty).TrimEnd('/');
        _httpClientFactory = httpClientFactory;
        _fileSystem = fileSystem;
        _logger = logger;
        _cachePath = fileSystem.Path.Combine(cacheDirectory, Constants.AUTH_DISCOVERY_JSON);
    }

    public async Task<ResolvedAuthentication> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(_options.Provider))
        {
            return FromOptions(ParseProvider(_options.Provider, "authentication.provider"));
        }

        var discovered = ReadCache() ?? await DiscoverAsync(cancellationToken);

        if (discovered is not null)
        {
            return FromDiscovery(discovered);
        }

        if (!string.IsNullOrWhiteSpace(_options.ClientId))
        {
            // Servers from before the discovery endpoint only support Entra ID.
            return FromOptions(AuthenticationProvider.EntraId);
        }

        throw new AuthenticationException(
            AuthenticationFailureReason.ServiceError,
            $"The sign-in settings could not be discovered from {_baseUrl}. Set authentication.provider and authentication.clientId in {Constants.APPLICATION_CONFIG_JSON}.");
    }

    public Task ForgetAsync(CancellationToken cancellationToken = default)
    {
        if (_fileSystem.File.Exists(_cachePath))
        {
            _fileSystem.File.Delete(_cachePath);
        }

        return Task.CompletedTask;
    }

    private static AuthenticationProvider ParseProvider(string value, string source)
    {
        return Enum.TryParse<AuthenticationProvider>(value.Trim(), ignoreCase: true, out var provider) && Enum.IsDefined(provider)
            ? provider
            : throw new AuthenticationException(
                AuthenticationFailureReason.ServiceError,
                $"Unknown sign-in provider '{value}' in {source}. Use EntraId or AuthKit.");
    }

    private ResolvedAuthentication FromOptions(AuthenticationProvider provider)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId))
        {
            throw new AuthenticationException(
                AuthenticationFailureReason.ServiceError,
                $"authentication.clientId is missing in {Constants.APPLICATION_CONFIG_JSON}.");
        }

        return new ResolvedAuthentication
        {
            Provider = provider,
            ClientId = _options.ClientId,
            TenantId = _options.TenantId,
            AuthKitApiBaseUrl = GetAuthKitApiBaseUrl(),
        };
    }

    private ResolvedAuthentication FromDiscovery(AuthenticationDiscoveryCache discovered)
    {
        return new ResolvedAuthentication
        {
            Provider = ParseProvider(discovered.Provider, DiscoveryPath),
            ClientId = discovered.ClientId,
            TenantId = discovered.TenantId,
            Scopes = discovered.Scopes ?? [],
            AuthKitApiBaseUrl = GetAuthKitApiBaseUrl(),
            AuthKitDomain = discovered.AuthKitDomain,
        };
    }

    private string GetAuthKitApiBaseUrl()
    {
        return string.IsNullOrWhiteSpace(_options.AuthKitApiBaseUrl) ? AuthKitDefaults.ApiBaseUrl : _options.AuthKitApiBaseUrl;
    }

    private AuthenticationDiscoveryCache? ReadCache()
    {
        if (!_fileSystem.File.Exists(_cachePath))
        {
            return null;
        }

        try
        {
            var cached = JsonSerializer.Deserialize<AuthenticationDiscoveryCache>(_fileSystem.File.ReadAllText(_cachePath));

            // The answer belongs to one server; a changed general.baseUrl asks again.
            return cached is not null && string.Equals(cached.BaseUrl, _baseUrl, StringComparison.OrdinalIgnoreCase)
                ? cached
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<AuthenticationDiscoveryCache?> DiscoverAsync(CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate($"{_baseUrl}/{DiscoveryPath}", UriKind.Absolute, out var discoveryUri))
        {
            return null;
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient(nameof(AuthenticationConfigurationResolver));
            httpClient.Timeout = DiscoveryTimeout;

            // Anonymous on purpose: a stale bearer token would be rejected with 401.
            using var response = await httpClient.GetAsync(discoveryUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Sign-in discovery at {Uri} returned {StatusCode}", discoveryUri, (int)response.StatusCode);
                return null;
            }

            var answer = await response.Content.ReadFromJsonAsync<DiscoveryResponse>(ServerJson, cancellationToken);

            if (string.IsNullOrWhiteSpace(answer?.Provider) || string.IsNullOrWhiteSpace(answer.ClientId))
            {
                return null;
            }

            var discovered = new AuthenticationDiscoveryCache
            {
                BaseUrl = _baseUrl,
                Provider = answer.Provider,
                ClientId = answer.ClientId,
                TenantId = answer.TenantId,
                Scopes = answer.Scopes,
                AuthKitDomain = answer.AuthKitDomain,
            };

            // The answer is already valid. A cache that cannot be written is used on the next sign-in.
            try
            {
                _fileSystem.File.WriteAllText(_cachePath, JsonSerializer.Serialize(discovered, CacheJson));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                _logger.LogInformation(exception, "Sign-in discovery settings could not be cached at {Path}", _cachePath);
            }

            return discovered;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or NotSupportedException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogInformation(exception, "Sign-in discovery at {Uri} failed", discoveryUri);
            return null;
        }
    }

    // The JSON shape of the API, which keeps PascalCase property names.
    private sealed record DiscoveryResponse(string? Provider, string? ClientId, string? TenantId, string[]? Scopes, string? AuthKitDomain);
}
