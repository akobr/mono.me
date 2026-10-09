using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// One AuthKit token issuer with its key set: RS256, 30 s clock skew, an exact issuer
// (only a trailing slash may differ) and the audience when one is required.
internal sealed class AuthKitTokenSource
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly JsonWebTokenHandler _handler = new();

    public AuthKitTokenSource(string issuer, string? audience, IConfigurationManager<JsonWebKeySet> keys)
    {
        Issuer = issuer;
        Audience = audience;
        Keys = keys;
    }

    public string Issuer { get; }

    public string? Audience { get; }

    public IConfigurationManager<JsonWebKeySet> Keys { get; }

    public static ConfigurationManager<JsonWebKeySet> CreateKeyManager(string jwksUri)
    {
        return new ConfigurationManager<JsonWebKeySet>(jwksUri, new JsonWebKeySetRetriever(), new HttpDocumentRetriever());
    }

    // The WorkOS docs show issuers both with and without the trailing slash.
    public static bool IssuerMatches(string? issuer, string expected)
    {
        return issuer is not null
            && string.Equals(TrimSlash(issuer), TrimSlash(expected), StringComparison.Ordinal);

        static string TrimSlash(string value)
        {
            return value.EndsWith('/') ? value[..^1] : value;
        }
    }

    // Unverified; only picks the source whose keys and rules then check the token. Null when unreadable.
    public static string? TryReadIssuer(string rawToken)
    {
        var handler = new JsonWebTokenHandler();

        if (string.IsNullOrWhiteSpace(rawToken) || !handler.CanReadToken(rawToken))
        {
            return null;
        }

        try
        {
            return handler.ReadJsonWebToken(rawToken).Issuer;
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    // Null when the token is not valid for this source.
    public async Task<List<Claim>?> ValidateAsync(string rawToken, CancellationToken cancellationToken)
    {
        JsonWebKeySet keySet;

        try
        {
            // The singleton configuration manager caches the key set and refreshes it.
            keySet = await Keys.GetConfigurationAsync(cancellationToken);
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
            IssuerValidator = (issuer, _, _) => IssuerMatches(issuer, Issuer)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}"),
            ValidateAudience = Audience is not null,
            ValidAudience = Audience,
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
                Keys.RequestRefresh();
            }

            return null;
        }

        return result.ClaimsIdentity.Claims.ToList();
    }
}
