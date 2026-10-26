using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Storyteller tokens on every API call: those of Federated machines, and the user tokens of the user token exchange. It
// owns Federation:Issuer, so the bearer pipeline never tries the user provider with them. A machine result has the
// claims MachineAuthenticationMiddleware builds for API keys, plus the kind, organization and project the per-call checks
// compare with the stored machine. A user result has the account (sub) and its scopes (scp), and its Delegation names the
// actor that acts for the person, with the restrictions of the token and those the actor's settings add.
public class StorytellerTokenValidator : IUserTokenIssuerValidator
{
    // Two claims with one name would let this reader and the token handler see different values.
    private static readonly JsonDocumentOptions PayloadOptions = new() { AllowDuplicateProperties = false, MaxDepth = 32 };

    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly IOptions<FederationOptions> _options;
    private readonly IOptions<TokenExchangeOptions> _exchangeOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorytellerTokenValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public StorytellerTokenValidator(
        TokenSigningKeyProvider keyProvider,
        IOptions<FederationOptions> options,
        IOptions<TokenExchangeOptions> exchangeOptions,
        TimeProvider timeProvider,
        ILogger<StorytellerTokenValidator> logger)
    {
        _keyProvider = keyProvider;
        _options = options;
        _exchangeOptions = exchangeOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public bool CanValidate(string issuer)
    {
        return OwnsIssuer(issuer);
    }

    public bool OwnsIssuer(string issuer)
    {
        return string.Equals(issuer, _options.Value.Issuer, StringComparison.Ordinal);
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || !_handler.CanReadToken(rawToken))
        {
            return null;
        }

        JsonWebToken token;

        try
        {
            token = _handler.ReadJsonWebToken(rawToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        // RFC 9068: only access tokens, so an ID token or another JWT signed with the same key never passes.
        if (!string.Equals(token.Typ, StorytellerTokenClaims.TokenType, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(token.Typ, StorytellerTokenClaims.MediaTokenType, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Unknown kids are refused without I/O between the provider's reloads.
        var key = await _keyProvider.GetValidationKeyAsync(token.Kid, cancellationToken);

        if (key is null)
        {
            _logger.LogInformation("A Storyteller token is refused: its key {Kid} is not the signing key", token.Kid);
            return null;
        }

        var options = _options.Value;
        var clockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds);
        var result = await _handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            IssuerSigningKey = key,
            TryAllIssuerSigningKeys = false,
            RequireSignedTokens = true,
            ValidAlgorithms = [TokenSigningKeyProvider.Algorithm],
            ValidateIssuer = true,
            IssuerValidator = (issuer, _, _) => OwnsIssuer(issuer)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException("The issuer is not Storyteller."),
            ValidateAudience = true,
            RequireAudience = true,
            ValidAudience = options.Audience,
            IgnoreTrailingSlashWhenValidatingAudience = false,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = clockSkew,
            LifetimeValidator = TokenLifetime.CreateValidator(_timeProvider, clockSkew),
        });

        if (!result.IsValid)
        {
            _logger.LogInformation("A Storyteller token is refused: {Reason}", result.Exception?.GetType().Name);
            return null;
        }

        var validated = (JsonWebToken)result.SecurityToken;

        // The JSON claims (act, restrictions) are read from the payload of the validated token only.
        if (!TryReadPayload(validated, out var payload))
        {
            _logger.LogInformation("A Storyteller token is refused: its payload is not a JSON object of distinct claims");
            return null;
        }

        // token_use decides the shape before any claim of a machine is read: machine tokens carry none, user tokens "user".
        if (!payload.TryGetProperty(StorytellerTokenClaims.TokenUse, out var tokenUse))
        {
            return ValidateMachine(validated, payload);
        }

        if (tokenUse.ValueKind == JsonValueKind.String && tokenUse.ValueEquals(StorytellerTokenClaims.UserTokenUse))
        {
            return ValidateUser(payload);
        }

        _logger.LogInformation("A Storyteller token is refused: its token_use is not {TokenUse}", StorytellerTokenClaims.UserTokenUse);
        return null;
    }

    private static bool TryGetString(JsonWebToken token, string name, out string value)
    {
        value = string.Empty;

        if (!token.TryGetPayloadValue<string>(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    // A non-blank JSON string, exactly as the payload has it.
    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && value.GetString() is { } text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;
    }

    // RFC 8693 section 4.1: one JSON object whose sub is the actor. A nested act (a chain of actors) is refused.
    private static bool TryGetActor(JsonElement payload, [NotNullWhen(true)] out string? actorId)
    {
        actorId = null;

        if (!payload.TryGetProperty(StorytellerTokenClaims.Act, out var act)
            || act.ValueKind != JsonValueKind.Object
            || act.TryGetProperty(StorytellerTokenClaims.Act, out _))
        {
            return false;
        }

        actorId = GetString(act, JwtRegisteredClaimNames.Sub);
        return actorId is not null;
    }

    // A JSON array of known restrictions (ActorRestrictions), possibly empty. A missing claim or an unknown restriction
    // refuses the token: the API would otherwise drop a restriction it cannot enforce.
    private static bool TryGetRestrictions(JsonElement payload, [NotNullWhen(true)] out IReadOnlyList<string>? restrictions)
    {
        restrictions = null;

        if (!payload.TryGetProperty(StorytellerTokenClaims.Restrictions, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var known = new List<string>();

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } restriction || !ActorRestrictions.IsKnown(restriction))
            {
                return false;
            }

            if (!known.Contains(restriction, StringComparer.Ordinal))
            {
                known.Add(restriction);
            }
        }

        restrictions = known;
        return true;
    }

    private static bool TryReadPayload(JsonWebToken token, out JsonElement payload)
    {
        payload = default;

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(token.EncodedPayload), PayloadOptions);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            payload = document.RootElement.Clone();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return false;
        }
    }

    // A token of a Federated machine (StorytellerTokenIssuer.IssueAsync). Only user tokens name an actor.
    private BearerValidationResult? ValidateMachine(JsonWebToken validated, JsonElement payload)
    {
        if (payload.TryGetProperty(StorytellerTokenClaims.Act, out _))
        {
            _logger.LogInformation("A Storyteller machine token is refused: it carries {Claim}", StorytellerTokenClaims.Act);
            return null;
        }

        if (!TryGetString(validated, JwtRegisteredClaimNames.Sub, out var machineId)
            || !TryGetString(validated, StorytellerTokenClaims.Organization, out var organization)
            || !TryGetString(validated, StorytellerTokenClaims.Project, out var project)
            || !TryGetString(validated, StorytellerTokenClaims.Scope, out var scope))
        {
            return null;
        }

        var claims = new List<Claim>
        {
            new("azp", machineId),
            new("sub", machineId),
        };

        foreach (var role in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim("roles", role));
        }

        if (TryGetString(validated, StorytellerTokenClaims.AnnotationKey, out var annotationKey))
        {
            claims.Add(new Claim("annotationKey", annotationKey));
        }

        return new BearerValidationResult(
            claims,
            IsMachine: true,
            MachineId: machineId,
            CredentialKind: MachineCredentialKind.Federated,
            Organization: organization,
            Project: project);
    }

    // A user token of the user token exchange (StorytellerTokenIssuer.IssueUserAsync): the account and its scope, nothing
    // of a machine, the actor of an entry of Auth:TokenExchange:Actors while the exchange is enabled (so turning it off or
    // removing the actor ends its tokens at once), and known restrictions only. The restrictions enforced are the token's
    // plus those of the actor's settings of now, so adding a restriction or turning AllowSecrets off applies to its tokens
    // at once too, and none is ever dropped here. The scopes become scp claims, which the API reads; the actor and the
    // restrictions go into the Delegation, never into claims.
    private BearerValidationResult? ValidateUser(JsonElement payload)
    {
        var accountId = GetString(payload, JwtRegisteredClaimNames.Sub);
        var scope = GetString(payload, StorytellerTokenClaims.Scope);

        if (accountId is null || scope is null)
        {
            return RefuseUser("it has no sub or no scope");
        }

        if (payload.TryGetProperty(StorytellerTokenClaims.Organization, out _)
            || payload.TryGetProperty(StorytellerTokenClaims.Project, out _)
            || payload.TryGetProperty(StorytellerTokenClaims.AnnotationKey, out _))
        {
            return RefuseUser("it carries a claim of a machine token");
        }

        if (!TryGetActor(payload, out var actorId))
        {
            return RefuseUser("its act is not one object with a string sub and no nested act");
        }

        var exchange = _exchangeOptions.Value;

        if (!exchange.Enabled)
        {
            return RefuseUser("Auth:TokenExchange:Enabled is off");
        }

        var actor = exchange.Actors.FirstOrDefault(candidate => string.Equals(candidate.Id, actorId, StringComparison.Ordinal));

        if (actor is null)
        {
            _logger.LogInformation("A Storyteller user token is refused: its actor {ActorId} is not in Auth:TokenExchange:Actors", actorId);
            return null;
        }

        if (!TryGetRestrictions(payload, out var restrictions))
        {
            return RefuseUser("its restrictions are not a list of known restrictions");
        }

        // The token's restrictions and those the actor has now: tightening an actor reaches its tokens at once, loosening it
        // waits for new ones.
        var enforced = restrictions.Union(actor.GetRestrictions(), StringComparer.Ordinal).ToArray();
        var claims = new List<Claim> { new("sub", accountId) };

        foreach (var value in scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal))
        {
            claims.Add(new Claim("scp", value));
        }

        return new BearerValidationResult(
            claims,
            IsMachine: false,
            MachineId: null,
            Delegation: new BearerDelegation(actorId, enforced));
    }

    private BearerValidationResult? RefuseUser(string reason)
    {
        _logger.LogInformation("A Storyteller user token is refused: {Reason}", reason);
        return null;
    }
}
