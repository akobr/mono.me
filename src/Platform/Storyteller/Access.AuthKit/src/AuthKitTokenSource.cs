using System.Security.Claims;
using System.Text.Json;

using _42.Platform.Storyteller.Accessing;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// One AuthKit token issuer with its key set: RS256, 30 s clock skew, an exact issuer
// (only a trailing slash may differ) and the audience when one is required.
internal sealed class AuthKitTokenSource
{
    public const string NoActorSubject = "(no act.sub)";
    public const int MaxLoggedLength = 256;

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

    // A top-level act member of the validated token, whatever its value. The claims miss some shapes: "act": [] gives no
    // claim, and of a duplicated member the last one wins. Fails closed for a token that is not a JsonWebToken.
    public static bool HasActor(SecurityToken token)
    {
        return token is not JsonWebToken jwt || jwt.TryGetPayloadValue<object>(UserSubjectTokenRules.ActorClaimType, out _);
    }

    // The actor of the validated token for a log: act.sub (WorkOS impersonation puts the impersonator's email there), a
    // string act as it is, otherwise a marker. Never the raw JSON; cut to MaxLoggedLength, control characters replaced.
    public static string DescribeActor(SecurityToken token)
    {
        object? act = null;
        var found = token is JsonWebToken jwt && jwt.TryGetPayloadValue(UserSubjectTokenRules.ActorClaimType, out act);
        var actor = act switch
        {
            JsonElement { ValueKind: JsonValueKind.Object } element
                when element.TryGetProperty(JwtRegisteredClaimNames.Sub, out var sub) && sub.ValueKind == JsonValueKind.String
                => sub.GetString(),
            string text => text,
            _ => null,
        };

        return found && actor is not null ? Sanitize(actor) : NoActorSubject;
    }

    // Null when the token is not valid for this source.
    public async Task<List<Claim>?> ValidateAsync(string rawToken, CancellationToken cancellationToken)
    {
        var result = await ValidateTokenAsync(rawToken, cancellationToken);
        return result.IsValid ? result.ClaimsIdentity.Claims.ToList() : null;
    }

    // ValidateTokenAsync with the source's own audience (none: aud is not checked).
    public Task<TokenValidationResult> ValidateTokenAsync(string rawToken, CancellationToken cancellationToken)
    {
        return ValidateTokenAsync(rawToken, Audience is null ? null : [Audience], cancellationToken);
    }

    // The source's keys, issuer and lifetime rules with the given audiences (null: aud is not checked); the result keeps
    // the reason of a refusal. Throws BearerKeyRetrievalException when the key set cannot be retrieved.
    public async Task<TokenValidationResult> ValidateTokenAsync(
        string rawToken,
        IReadOnlyCollection<string>? audiences,
        CancellationToken cancellationToken)
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
            ValidateAudience = audiences is not null,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
        };

        var result = await _handler.ValidateTokenAsync(rawToken, validationParameters);

        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // A rotated key: the next request fetches the key set again (rate limited by the manager).
            Keys.RequestRefresh();
        }

        return result;
    }

    private static string Sanitize(string value)
    {
        var text = value.Length > MaxLoggedLength ? value[..MaxLoggedLength] : value;
        return string.Create(text.Length, text, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                span[index] = char.IsControl(source[index]) ? '?' : source[index];
            }
        });
    }
}
