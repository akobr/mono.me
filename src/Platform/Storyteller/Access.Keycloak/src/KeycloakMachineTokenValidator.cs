using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Keycloak client-credentials tokens of machine clients: issuer {ServerUrl}/realms/{Realm}, keys
// from the realm's OpenID metadata, audience Keycloak:Audience, RS256. The machine is the azp; its
// Storyteller scopes come from the storyteller_scope claim the client's mapper adds.
public class KeycloakMachineTokenValidator : IMachineTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly string _issuer;
    private readonly string _audience;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JsonWebTokenHandler _handler = new();

    public KeycloakMachineTokenValidator(IOptions<KeycloakOptions> options)
        : this(options, CreateConfigurationManager(options.Value))
    {
    }

    public KeycloakMachineTokenValidator(
        IOptions<KeycloakOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configurationManager);

        _issuer = options.Value.GetIssuer();
        _audience = options.Value.Audience;
        _configurationManager = configurationManager;
    }

    public bool CanValidate(string issuer)
    {
        return string.Equals(issuer.TrimEnd('/'), _issuer, StringComparison.Ordinal);
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !_handler.CanReadToken(rawToken))
        {
            return null;
        }

        OpenIdConnectConfiguration configuration;

        try
        {
            configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or OperationCanceledException or ArgumentException)
        {
            throw new BearerKeyRetrievalException("The Keycloak signing keys could not be retrieved.", exception);
        }

        var result = await _handler.ValidateTokenAsync(rawToken, new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = configuration.SigningKeys,
            IssuerValidator = (issuer, _, _) => CanValidate(issuer)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}"),
            ValidAudience = _audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
        });

        if (!result.IsValid)
        {
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                _configurationManager.RequestRefresh();
            }

            return null;
        }

        var claims = result.ClaimsIdentity.Claims.ToList();
        var clientId = claims.FirstOrDefault(claim => claim.Type == "azp")?.Value;

        return string.IsNullOrWhiteSpace(clientId) ? null : Normalize(claims, clientId);
    }

    // Scope-bearing claims are rebuilt from storyteller_scope only, so realm roles or other mappers
    // cannot grant API scopes.
    internal static BearerValidationResult Normalize(IReadOnlyList<Claim> claims, string clientId)
    {
        var normalized = claims
            .Where(claim => claim.Type is not ("scp" or "roles" or "azp" or ClaimTypes.Role or KeycloakMachineAccessService.ScopeClaimType)
                && !claim.Type.EndsWith("/scope", StringComparison.Ordinal))
            .ToList();
        normalized.Add(new Claim("azp", clientId));

        var scopes = claims
            .Where(claim => claim.Type == KeycloakMachineAccessService.ScopeClaimType)
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (scopes.Count > 0)
        {
            normalized.Add(new Claim("scp", string.Join(' ', scopes)));
        }

        return new BearerValidationResult(normalized, IsMachine: true, MachineId: clientId);
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(KeycloakOptions options)
    {
        // A local Keycloak (http://localhost:8080) has no TLS; anything else must use HTTPS.
        var requireHttps = !Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var server) || !server.IsLoopback;

        return new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{options.GetIssuer()}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = requireHttps });
    }
}
