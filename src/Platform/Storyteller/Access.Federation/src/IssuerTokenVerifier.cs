using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// The checks every JWT of an issuer gets, whatever it proves: the signature with the issuer's keys from
// FederatedIssuerKeyResolver (fetched again once for an unknown kid), the exact iss, an accepted aud and the lifetime
// with the skew, then the caller's audience and lifetime policy on the verified payload. A platform token of a machine
// has one aud and lives at most an hour; a managed identity token lives a day, and a client assertion five minutes.
internal sealed class IssuerTokenVerifier
{
    private readonly FederatedIssuerKeyResolver _keyResolver;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public IssuerTokenVerifier(FederatedIssuerKeyResolver keyResolver, TimeProvider timeProvider)
    {
        _keyResolver = keyResolver;
        _timeProvider = timeProvider;
    }

    // rawToken and token are the same token; the keys are cached under organization and project. Throws
    // BearerKeyRetrievalException when the keys cannot be retrieved, or the issuer did not publish the token's kid
    // right after a fetch (it may be rotating: the caller retries later).
    public async Task<IssuerTokenVerification> VerifyAsync(
        string rawToken,
        UnverifiedJwt token,
        string organization,
        string project,
        FederatedIssuer issuer,
        IssuerTokenPolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(policy);

        // Without a kid, every key of a fetched set would be an unknown key and fetch the set again.
        if (string.IsNullOrEmpty(token.KeyId))
        {
            return IssuerTokenVerification.NotVerified("the token has no kid");
        }

        var keys = await _keyResolver.GetKeysAsync(organization, project, issuer, cancellationToken);

        if (keys is null)
        {
            return IssuerTokenVerification.NotVerified("the trusted issuer is misconfigured");
        }

        var result = await ValidateAsync(rawToken, issuer, keys, policy);

        if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            var refreshed = await _keyResolver.RefreshForUnknownKeyAsync(organization, project, issuer, keys, cancellationToken);

            if (refreshed is null)
            {
                return IssuerTokenVerification.NotVerified("the token's kid is not in the issuer's key set");
            }

            result = await ValidateAsync(rawToken, issuer, refreshed, policy);

            // Just fetched, so the issuer may still be publishing the key: retry later.
            if (result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                throw new BearerKeyRetrievalException($"The trusted issuer {issuer.Name} did not publish the key {token.KeyId}.");
            }
        }

        if (!result.IsValid)
        {
            return IssuerTokenVerification.NotVerified($"the token did not validate ({result.Exception?.GetType().Name})");
        }

        return CheckPolicy(token.Payload, policy) is { } policyError
            ? IssuerTokenVerification.Refused(policyError)
            : IssuerTokenVerification.Valid;
    }

    // A string or a one-element array of a string.
    public static string? GetSingleAudience(JsonElement payload)
    {
        if (!payload.TryGetProperty("aud", out var audience))
        {
            return null;
        }

        if (audience.ValueKind == JsonValueKind.Array && audience.GetArrayLength() == 1)
        {
            audience = audience[0];
        }

        return audience.ValueKind == JsonValueKind.String ? audience.GetString() : null;
    }

    // A non-empty string claim of the payload, or null.
    public static string? GetString(JsonElement payload, string name)
    {
        return payload.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
                ? text
                : null;
    }

    private async Task<TokenValidationResult> ValidateAsync(
        string rawToken,
        FederatedIssuer issuer,
        FederatedIssuerKeys keys,
        IssuerTokenPolicy policy)
    {
        return await _handler.ValidateTokenAsync(rawToken, new TokenValidationParameters
        {
            IssuerSigningKeys = keys.Keys,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidAlgorithms = SubjectTokenVerifier.AllowedAlgorithms,
            ValidateIssuer = true,
            IssuerValidator = (tokenIssuer, _, _) => string.Equals(tokenIssuer, issuer.Issuer, StringComparison.Ordinal)
                ? tokenIssuer
                : throw new SecurityTokenInvalidIssuerException("The issuer is not the trusted issuer."),
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudiences = policy.Audiences,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = policy.ClockSkew,
            LifetimeValidator = TokenLifetime.CreateValidator(_timeProvider, policy.ClockSkew),
        });
    }

    // The checks the handler doesn't make, on the verified payload. Null when they pass, else why not.
    private string? CheckPolicy(JsonElement payload, IssuerTokenPolicy policy)
    {
        // A token minted for several relying parties may be replayed by any of them.
        if (policy.RequireSingleAudience && GetSingleAudience(payload) is null)
        {
            return "the token has not exactly one audience";
        }

        var hasIssuedAt = payload.TryGetProperty("iat", out var issuedAtValue);
        double issuedAt = 0;

        if ((hasIssuedAt && !TryGetNumber(issuedAtValue, out issuedAt)) || (!hasIssuedAt && policy.RequireIssuedAt))
        {
            return "the token has no iat";
        }

        if (!payload.TryGetProperty("exp", out var expiresValue) || !TryGetNumber(expiresValue, out var expires))
        {
            return "the token has no exp";
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var clockSkewSeconds = policy.ClockSkew.TotalSeconds;

        if (hasIssuedAt && issuedAt > now + clockSkewSeconds)
        {
            return "the token was issued in the future";
        }

        if (policy.MaxLifetimeSeconds is not { } maxLifetime)
        {
            return null;
        }

        // Without iat, the time left counts, with the skew for the issuer's clock.
        var lifetime = hasIssuedAt ? expires - issuedAt : expires - now - clockSkewSeconds;

        return lifetime > maxLifetime
            ? $"the token lives longer than {maxLifetime} seconds"
            : null;
    }

    // NumericDate (RFC 7519); a value of another JSON kind is no date, rather than an InvalidOperationException.
    private static bool TryGetNumber(JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }
}
