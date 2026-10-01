using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

public class EntraIdBearerTokenValidator : IBearerTokenValidator
{
    private readonly string _clientId;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public EntraIdBearerTokenValidator(IOptions<UserAuthenticationOptions> options, IHostEnvironment environment)
        : this(options, environment, CreateConfigurationManager(options.Value))
    {
    }

    public EntraIdBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        IHostEnvironment environment,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configurationManager);

        var configured = options.Value;
        _clientId = configured.ClientId ?? string.Empty;
        _configurationManager = configurationManager;

        if (environment.IsDevelopment())
        {
            // Process-wide. Production leaves the default (false) so token details stay out of logs.
            IdentityModelEventSource.ShowPII = true;
        }
    }

    public static string? TryGetMachineId(IReadOnlyList<Claim> claims, string? clientId)
    {
        var appId = FirstClaim(claims, "azp") ?? FirstClaim(claims, "appid");

        if (string.IsNullOrEmpty(appId)
            || string.Equals(appId, clientId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return appId;
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId))
        {
            throw new InvalidOperationException("Auth:ClientId is required for Entra ID token validation.");
        }

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(rawToken))
        {
            return null;
        }

        try
        {
            // The singleton configuration manager caches the signing keys and refreshes them.
            var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);

            // Entra keeps the handler default clock skew of five minutes. AuthKit uses 30 seconds.
            var validationParameters = new TokenValidationParameters
            {
                ValidAudiences = [$"api://{_clientId}", _clientId],
                IssuerSigningKeys = configuration.SigningKeys,
                IssuerValidator = static (issuer, _, _) =>
                {
                    if (issuer is not null
                        && (issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase)
                            || issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase)))
                    {
                        return issuer;
                    }

                    throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");
                },
            };

            var principal = handler.ValidateToken(rawToken, validationParameters, out _);
            var claims = principal.Claims.ToList();
            var machineId = TryGetMachineId(claims, _clientId);
            return new BearerValidationResult(claims, machineId is not null, machineId);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or OperationCanceledException)
        {
            throw new BearerKeyRetrievalException("The OpenID signing keys could not be retrieved.", exception);
        }
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(UserAuthenticationOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.TenantId))
        {
            throw new InvalidOperationException("Auth:TenantId is required for Entra ID token validation.");
        }

        var metadataAddress = $"https://login.microsoftonline.com/{options.TenantId}/v2.0/.well-known/openid-configuration";
        return new ConfigurationManager<OpenIdConnectConfiguration>(metadataAddress, new OpenIdConnectConfigurationRetriever());
    }

    private static string? FirstClaim(IReadOnlyList<Claim> claims, string type)
    {
        foreach (var claim in claims)
        {
            if (claim.Type == type)
            {
                return claim.Value;
            }
        }

        return null;
    }
}
