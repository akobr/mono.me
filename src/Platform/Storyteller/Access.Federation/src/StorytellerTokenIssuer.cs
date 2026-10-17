using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Signs short-lived Storyteller tokens for Federated machines: RFC 9068 access tokens (typ at+jwt, RS256).
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
}
