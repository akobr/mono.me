using System.Security.Claims;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Storyteller tokens of Federated machines on every API call. It owns Federation:Issuer, so the bearer pipeline
// never tries the user provider with them. The result has the claims MachineAuthenticationMiddleware builds for
// API keys, plus the kind, organization and project the per-call checks compare with the stored machine.
public class StorytellerTokenValidator : IMachineTokenValidator
{
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorytellerTokenValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new() { MapInboundClaims = false };

    public StorytellerTokenValidator(
        TokenSigningKeyProvider keyProvider,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<StorytellerTokenValidator> logger)
    {
        _keyProvider = keyProvider;
        _options = options;
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
}
