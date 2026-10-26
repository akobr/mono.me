using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Signs short-lived Storyteller tokens: RFC 9068 access tokens (typ at+jwt, RS256) for Federated machines, and user
// tokens of the user token exchange that name the person and the actor.
public class StorytellerTokenIssuer
{
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _handler = new();

    public StorytellerTokenIssuer(
        TokenSigningKeyProvider keyProvider,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider)
    {
        _keyProvider = keyProvider;
        _options = options;
        _timeProvider = timeProvider;
    }

    // TokenSigningKeyUnavailableException when no signing key is stored and none may be created.
    public async Task<StorytellerToken> IssueAsync(StorytellerTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var options = _options.Value;
        var credentials = await _keyProvider.GetSigningCredentialsAsync();

        // Whole seconds, as the token carries them.
        var now = DateTimeOffset.FromUnixTimeSeconds(_timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var lifetime = TimeSpan.FromMinutes(options.TokenLifetimeMinutes);
        var expiresAt = now + lifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = request.MachineId,
            [StorytellerTokenClaims.ClientId] = request.MachineId,
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("D"),
            [StorytellerTokenClaims.Scope] = string.Join(' ', request.Scopes),
            [StorytellerTokenClaims.Organization] = request.Organization,
            [StorytellerTokenClaims.Project] = request.Project,
            [StorytellerTokenClaims.FederationIssuer] = request.FederationIssuer,
            [StorytellerTokenClaims.FederationSubject] = request.FederationSubject,
        };

        if (!string.IsNullOrEmpty(request.AnnotationKey))
        {
            claims[StorytellerTokenClaims.AnnotationKey] = request.AnnotationKey;
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = StorytellerTokenClaims.TokenType,
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = credentials,
        });

        return new StorytellerToken(token, expiresAt, (int)lifetime.TotalSeconds);
    }

    // A user token: sub the account, token_use "user", act { sub: the actor }, client_id the actor, the scope and the
    // restrictions. It lives MaxLifetime, never past NotAfter (the subject token's exp), in whole seconds.
    // TokenSigningKeyUnavailableException when no signing key is stored and none may be created; ArgumentException
    // when NotAfter leaves less than a second.
    public async Task<StorytellerToken> IssueUserAsync(StorytellerUserTokenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(request.MaxLifetime, TimeSpan.Zero, nameof(request));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.MaxLifetime, TimeSpan.FromMinutes(TokenExchangeOptions.MaxTokenLifetimeMinutes), nameof(request));

        var options = _options.Value;
        var now = DateTimeOffset.FromUnixTimeSeconds(_timeProvider.GetUtcNow().ToUnixTimeSeconds());
        var lifetime = GetUserTokenLifetime(now, request.MaxLifetime, request.NotAfter);

        if (lifetime < TimeSpan.FromSeconds(1))
        {
            throw new ArgumentException("The subject token expires before a user token can be issued.", nameof(request));
        }

        var credentials = await _keyProvider.GetSigningCredentialsAsync();
        var expiresAt = now + lifetime;

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = request.AccountId,
            [StorytellerTokenClaims.TokenUse] = StorytellerTokenClaims.UserTokenUse,
            [StorytellerTokenClaims.Scope] = string.Join(' ', request.Scopes),

            // A JSON object (RFC 8693), not SecurityTokenDescriptor.Subject.Actor, which writes actort.
            [StorytellerTokenClaims.Act] = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = request.ActorId },
            [StorytellerTokenClaims.ClientId] = request.ActorId,
            [StorytellerTokenClaims.Restrictions] = request.Restrictions.ToArray(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("D"),
        };

        if (request.AuthTime is { } authTime)
        {
            claims[StorytellerTokenClaims.AuthTime] = authTime.ToUnixTimeSeconds();
        }

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = StorytellerTokenClaims.TokenType,
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = credentials,
        });

        return new StorytellerToken(token, expiresAt, (int)lifetime.TotalSeconds);
    }

    // min(maxLifetime, notAfter - now) in whole seconds; zero or less when notAfter has passed.
    internal static TimeSpan GetUserTokenLifetime(DateTimeOffset now, TimeSpan maxLifetime, DateTimeOffset notAfter)
    {
        var seconds = Math.Min(Math.Floor(maxLifetime.TotalSeconds), Math.Floor((notAfter - now).TotalSeconds));
        return TimeSpan.FromSeconds(seconds);
    }
}
