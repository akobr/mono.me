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
    private const string SubjectClaimType = "sub";
    private const string ScopeClaimType = "http://schemas.microsoft.com/identity/claims/scope";
    private const string TenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    private readonly string _clientId;
    private readonly IReadOnlyList<DelegatedClientOptions> _delegatedClients;
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
        _delegatedClients = NormalizeDelegatedClients(configured.DelegatedClients);
        _configurationManager = configurationManager;

        if (environment.IsDevelopment())
        {
            // Process-wide. Production leaves the default (false) so token details stay out of logs.
            IdentityModelEventSource.ShowPII = true;
        }
    }

    // v1 (sts.windows.net) and v2 (login.microsoftonline.com) issuers. The trailing slash keeps a
    // host such as login.microsoftonline.com.evil out.
    public static bool IsEntraIssuer(string? issuer)
    {
        return issuer is not null
            && (issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase)
                || issuer.StartsWith("https://login.microsoftonline.com/", StringComparison.OrdinalIgnoreCase));
    }

    public static string? TryGetMachineId(IReadOnlyList<Claim> claims, string? clientId)
    {
        return TryGetMachineId(claims, clientId, []);
    }

    // A listed client app acts for the user only with a delegated token from its own tenant: a scope claim and no
    // idtyp=app. Its app-only tokens, and its tokens from another tenant, stay machines like every other azp.
    public static string? TryGetMachineId(IReadOnlyList<Claim> claims, string? clientId, IReadOnlyList<DelegatedClientOptions> delegatedClients)
    {
        var appId = FirstClaim(claims, "azp") ?? FirstClaim(claims, "appid");

        if (string.IsNullOrEmpty(appId)
            || string.Equals(appId, clientId, StringComparison.OrdinalIgnoreCase)
            || IsDelegatedTokenOfListedClient(claims, appId, delegatedClients))
        {
            return null;
        }

        return appId;
    }

    // The settings accept any GUID form ({...}, 32 digits); tokens carry azp and tid in the dashed form (D).
    public static IReadOnlyList<DelegatedClientOptions> NormalizeDelegatedClients(IEnumerable<DelegatedClientOptions> delegatedClients)
    {
        ArgumentNullException.ThrowIfNull(delegatedClients);

        return delegatedClients
            .Select(client => new DelegatedClientOptions { ClientId = NormalizeGuid(client.ClientId), TenantId = NormalizeGuid(client.TenantId) })
            .ToArray();
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
            return await ValidateWithSigningKeysAsync(configuration => Validate(handler, rawToken, configuration), cancellationToken);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // The claims of a user, keyed by the account id {tid}:{oid} (UserIdentity): it replaces sub and the mapped
    // NameIdentifier as sub, and the pairwise sub stays as entra_sub. Without a GUID tid and oid, accountId is null and
    // the claims keep no sub.
    internal static List<Claim> KeyByAccountId(IReadOnlyList<Claim> claims, out string? accountId)
    {
        accountId = UserIdentity.GetAccountId(claims, IdentityProviderKind.EntraId);
        var pairwiseSubject = FirstClaim(claims, SubjectClaimType) ?? FirstClaim(claims, ClaimTypes.NameIdentifier);
        var keyed = new List<Claim>(claims.Count + 1);

        foreach (var claim in claims)
        {
            if (claim.Type is not (SubjectClaimType or ClaimTypes.NameIdentifier or UserIdentity.EntraSubjectClaimType))
            {
                keyed.Add(claim);
            }
        }

        if (accountId is not null)
        {
            keyed.Add(new Claim(SubjectClaimType, accountId));

            if (!string.IsNullOrWhiteSpace(pairwiseSubject))
            {
                keyed.Add(new Claim(UserIdentity.EntraSubjectClaimType, pairwiseSubject));
            }
        }

        return keyed;
    }

    // Runs validate with the signing keys of the tenant's metadata; EntraIdUserSubjectTokenValidator shares them. The
    // validation exceptions of validate reach the caller. Throws BearerKeyRetrievalException when the keys cannot be
    // retrieved.
    internal async Task<T> ValidateWithSigningKeysAsync<T>(Func<OpenIdConnectConfiguration, T> validate, CancellationToken cancellationToken)
    {
        try
        {
            // The singleton manager keeps one snapshot of the signing keys. An unknown kid is
            // how a rollover shows up in that snapshot. Ask for a refresh and try the new set once.
            // The manager's RefreshInterval stops a made-up kid from fetching metadata on every call.
            var configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);

            try
            {
                return validate(configuration);
            }
            catch (SecurityTokenSignatureKeyNotFoundException)
            {
                _configurationManager.RequestRefresh();
                configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
                return validate(configuration);
            }
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

    private BearerValidationResult? Validate(JwtSecurityTokenHandler handler, string rawToken, OpenIdConnectConfiguration configuration)
    {
        // Entra keeps the handler default clock skew of five minutes. AuthKit uses 30 seconds.
        var validationParameters = new TokenValidationParameters
        {
            ValidAudiences = [$"api://{_clientId}", _clientId],
            IssuerSigningKeys = configuration.SigningKeys,
            IssuerValidator = static (issuer, _, _) => IsEntraIssuer(issuer)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}"),
        };

        var principal = handler.ValidateToken(rawToken, validationParameters, out _);
        var claims = principal.Claims.ToList();
        var machineId = TryGetMachineId(claims, _clientId, _delegatedClients);

        if (machineId is not null)
        {
            return new BearerValidationResult(claims, true, machineId);
        }

        // A user token names its account by tid and oid; without them it is refused (401).
        var keyed = KeyByAccountId(claims, out var accountId);
        return accountId is null ? null : new BearerValidationResult(keyed, false, null);
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

    private static bool IsDelegatedTokenOfListedClient(IReadOnlyList<Claim> claims, string appId, IReadOnlyList<DelegatedClientOptions> delegatedClients)
    {
        if (delegatedClients.Count == 0)
        {
            return false;
        }

        // Validated claims arrive mapped (JwtSecurityTokenHandler), decoded claims raw: both names are read.
        var tenantId = FirstClaim(claims, "tid") ?? FirstClaim(claims, TenantIdClaimType);
        var isListed = delegatedClients.Any(client =>
            string.Equals(client.ClientId, appId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(client.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));

        if (!isListed || string.Equals(FirstClaim(claims, "idtyp"), "app", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return claims.Any(claim => claim.Type is ("scp" or ScopeClaimType) && !string.IsNullOrWhiteSpace(claim.Value));
    }

    // A value that is not a GUID stays as it is; the options validator refuses it.
    private static string NormalizeGuid(string? value)
    {
        return Guid.TryParse(value, out var id) ? id.ToString("D") : value ?? string.Empty;
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
