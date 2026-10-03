using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Validates AuthKit user access tokens. M2M tokens from the AuthKit domain are a later phase.
public class AuthKitBearerTokenValidator : IBearerTokenValidator
{
    private const string UserIdPrefix = "user_";

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly string _issuer;
    private readonly string? _audience;
    private readonly IConfigurationManager<JsonWebKeySet> _keyManager;
    private readonly AuthKitClaimNormalizer _normalizer;
    private readonly JsonWebTokenHandler _handler = new();

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment)
        : this(options, normalizer, environment, CreateKeyManager(options.Value.AuthKit.GetJwksUri()))
    {
    }

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment,
        IConfigurationManager<JsonWebKeySet> keyManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(keyManager);

        var authKit = options.Value.AuthKit;

        if (string.IsNullOrWhiteSpace(authKit.Issuer))
        {
            throw new InvalidOperationException("Auth:AuthKit:Issuer is required for AuthKit token validation.");
        }

        _issuer = authKit.Issuer;
        _audience = string.IsNullOrWhiteSpace(authKit.Audience) ? null : authKit.Audience;
        _keyManager = keyManager;
        _normalizer = normalizer;

        if (environment.IsDevelopment())
        {
            // Process-wide. Production leaves the default (false) so token details stay out of logs.
            IdentityModelEventSource.ShowPII = true;
        }
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !_handler.CanReadToken(rawToken))
        {
            return null;
        }

        JsonWebKeySet keySet;

        try
        {
            // The singleton configuration manager caches the key set and refreshes it.
            keySet = await _keyManager.GetConfigurationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or OperationCanceledException or ArgumentException)
        {
            throw new BearerKeyRetrievalException("The AuthKit signing keys could not be retrieved.", exception);
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = keySet.GetSigningKeys(),
            IssuerValidator = ValidateIssuer,
            ValidateAudience = _audience is not null,
            ValidAudience = _audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
        };

        var result = await _handler.ValidateTokenAsync(rawToken, validationParameters);

        if (!result.IsValid)
        {
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                // A rotated key: the next request fetches the key set again (rate limited by the manager).
                _keyManager.RequestRefresh();
            }

            return null;
        }

        var claims = result.ClaimsIdentity.Claims.ToList();
        var subject = claims.FirstOrDefault(claim => claim.Type == "sub")?.Value;

        // User tokens carry a WorkOS user id. Anything else (an M2M client sharing a custom
        // auth domain issuer, for example) must not be treated as a user.
        if (subject is null || !subject.StartsWith(UserIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return _normalizer.Normalize(claims);
    }

    private static ConfigurationManager<JsonWebKeySet> CreateKeyManager(string jwksUri)
    {
        return new ConfigurationManager<JsonWebKeySet>(jwksUri, new JsonWebKeySetRetriever(), new HttpDocumentRetriever());
    }

    // Exact, case-sensitive match. The WorkOS docs show the issuer both with and without the
    // trailing slash, so only that one character is allowed to differ.
    private string ValidateIssuer(string issuer, SecurityToken token, TokenValidationParameters parameters)
    {
        if (issuer is not null
            && string.Equals(TrimSlash(issuer), TrimSlash(_issuer), StringComparison.Ordinal))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");

        static string TrimSlash(string value)
        {
            return value.EndsWith('/') ? value[..^1] : value;
        }
    }
}
