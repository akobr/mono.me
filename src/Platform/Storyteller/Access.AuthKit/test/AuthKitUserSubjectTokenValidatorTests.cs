using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

public sealed class AuthKitUserSubjectTokenValidatorTests : IDisposable
{
    private const string ClientId = "client_123";
    private const string Issuer = "https://api.workos.com/";
    private const string AuthKitDomain = "https://auth.example.com";
    private const string AuthKitDomainJwks = "https://auth.example.com/oauth2/jwks";
    private const string McpResource = "https://mcp.example.com/mcp";
    private const string McpClientId = "client_mcp_01";

    private static readonly string[] Audiences = [McpResource];

    private readonly RSA _userRsa = RSA.Create(2048);
    private readonly RSA _domainRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _userKey;
    private readonly RsaSecurityKey _domainKey;
    private readonly StaticKeyManager _userKeys;
    private readonly StaticKeyManager _domainKeys;
    private readonly List<string> _keyManagerUris = [];

    public AuthKitUserSubjectTokenValidatorTests()
    {
        _userKey = new RsaSecurityKey(_userRsa) { KeyId = "user-key" };
        _domainKey = new RsaSecurityKey(_domainRsa) { KeyId = "domain-key" };
        _userKeys = new StaticKeyManager(Jwks(_userRsa, "user-key"));
        _domainKeys = new StaticKeyManager(Jwks(_domainRsa, "domain-key"));
    }

    [Fact]
    public async Task UserTokenOfTheApiIssuer_IsAcceptedWithTheAccountId()
    {
        var expires = DateTime.UtcNow.AddMinutes(10);
        var claims = UserClaims(new Dictionary<string, object>
        {
            ["client_id"] = McpClientId,
            ["jti"] = "jti-1",
            ["name"] = "Ada Lovelace",
            ["permissions"] = new[] { "storyteller:annotation-read" },
        });
        var validator = CreateValidator(authKit =>
        {
            authKit.DefaultUserScopes = ["User.Impersonation"];
            authKit.PermissionMap = new Dictionary<string, string> { ["storyteller:annotation-read"] = "Annotation.Read" };
        });

        var result = await validator.ValidateAsync(Token(claims, expires: expires), Audiences, [McpClientId]);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.AccountId.ShouldBe("user_01");
        result.ClientId.ShouldBe(McpClientId);
        result.TokenId.ShouldBe("jti-1");
        result.ExpiresAt.ShouldBe(new DateTimeOffset(expires), TimeSpan.FromSeconds(1));

        // The claims API routes get for the same person.
        Value(result, "sub").ShouldBe("user_01");
        Value(result, "name").ShouldBe("Ada Lovelace");
        Value(result, "scp").ShouldBe("User.Impersonation Annotation.Read");
        _keyManagerUris.ShouldBe([AuthKitDomainJwks]);
        _domainKeys.Calls.ShouldBe(0);
    }

    // The AuthKit domain issues MCP sign-in tokens; its tokens serve the exchange, with keys from its JwksUri.
    [Fact]
    public async Task UserTokenOfAnAdditionalIssuer_IsAcceptedForTheExchange()
    {
        var token = Token(
            UserClaims(new Dictionary<string, object> { ["client_id"] = McpClientId, ["auth_time"] = 1793000000L }),
            issuer: AuthKitDomain,
            key: _domainKey);

        var result = await CreateValidator().ValidateAsync(token, Audiences, [McpClientId]);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.AccountId.ShouldBe("user_01");
        result.ClientId.ShouldBe(McpClientId);
        result.AuthTime.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793000000));
        _domainKeys.Calls.ShouldBe(1);
        _userKeys.Calls.ShouldBe(0);
    }

    // Only for the exchange: on API routes the same token is not a bearer token, and no keys are fetched for it.
    [Fact]
    public async Task UserTokenOfAnAdditionalIssuer_IsNotABearerToken()
    {
        var token = Token(UserClaims(), issuer: AuthKitDomain, key: _domainKey, audience: null);
        var options = UserOptions(authKit => authKit.Audience = null);
        var bearer = new AuthKitBearerTokenValidator(options, new AuthKitClaimNormalizer(options), new TestHostEnvironment(), _userKeys);

        var result = await bearer.ValidateAsync(token);

        result.ShouldBeNull();
        _userKeys.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task IssuerWithOrWithoutTheTrailingSlash_Matches()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), issuer: "https://api.workos.com"), Audiences, []);
        var domain = await CreateValidator().ValidateAsync(Token(UserClaims(), issuer: $"{AuthKitDomain}/", key: _domainKey), Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        domain.IsAccepted.ShouldBeTrue(domain.RefusalReason);
    }

    // The keys are per issuer: a token of the AuthKit domain signed with the user-management keys does not pass.
    [Fact]
    public async Task UserTokenOfAnAdditionalIssuerSignedWithAnotherIssuersKey_IsRefused()
    {
        var token = Token(UserClaims(), issuer: AuthKitDomain, key: _userKey);

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldNotBeNull().ShouldStartWith("The token is not valid (");
    }

    // Audience binding: a token for another app, or one without aud, is not this actor's.
    [Theory]
    [InlineData("https://other.example.com")]
    [InlineData(null)]
    public async Task TokenForAnotherAudience_IsRefused(string? audience)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), audience: audience), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidAudienceException).");
    }

    [Fact]
    public async Task TokenOfAnotherIssuer_IsRefusedWithoutFetchingKeys()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), issuer: "https://other.authkit.app"), Audiences, []);

        result.RefusalReason.ShouldBe("The token's issuer is not Auth:AuthKit:Issuer or one of Auth:TokenExchange:AdditionalIssuers.");
        _userKeys.Calls.ShouldBe(0);
        _domainKeys.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ExpiredToken_IsRefused()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), expires: DateTime.UtcNow.AddMinutes(-1)), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenExpiredException).");
    }

    [Fact]
    public async Task TokenThatAlreadyNamesAnActor_IsRefused()
    {
        var token = Token(UserClaims(new Dictionary<string, object> { ["act"] = new Dictionary<string, object> { ["sub"] = "other-actor" } }));

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.RefusalReason.ShouldBe("The token already names an actor (act).");
    }

    [Fact]
    public async Task ClientOutsideAllowedClientIds_IsRefused()
    {
        var token = Token(UserClaims(new Dictionary<string, object> { ["client_id"] = "client_other" }));

        var result = await CreateValidator().ValidateAsync(token, Audiences, [McpClientId]);

        result.RefusalReason.ShouldBe("The token's client (client_id) is not one of the actor's AllowedClientIds.");
    }

    [Fact]
    public async Task WithoutAllowedClientIds_AnyClientIsAccepted()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims()), Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.ClientId.ShouldBeNull();
    }

    // An M2M token of the AuthKit domain names its application, not a WorkOS user.
    [Theory]
    [InlineData("client_m2m")]
    [InlineData("01HXYZ")]
    public async Task TokenWhoseSubIsNotAUser_IsRefused(string subject)
    {
        var claims = UserClaims(new Dictionary<string, object> { ["sub"] = subject, ["client_id"] = subject });

        var result = await CreateValidator().ValidateAsync(Token(claims, issuer: AuthKitDomain, key: _domainKey), Audiences, []);

        result.RefusalReason.ShouldBe("The token's sub is not a WorkOS user id (user_…).");
    }

    [Fact]
    public async Task TokenNotSignedWithRs256_IsRefused()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), algorithm: SecurityAlgorithms.RsaSha512), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task NoToken_IsRefused(string token)
    {
        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        _userKeys.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task KeysOfTheApiIssuerThatCannotBeRetrieved_Throw()
    {
        var validator = CreateValidator(userKeys: new StaticKeyManager(null));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(Token(UserClaims()), Audiences, []));
    }

    [Fact]
    public async Task KeysOfAnAdditionalIssuerThatCannotBeRetrieved_Throw()
    {
        var validator = CreateValidator(domainKeys: new StaticKeyManager(null));
        var token = Token(UserClaims(), issuer: AuthKitDomain, key: _domainKey);

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(token, Audiences, []));
    }

    [Fact]
    public async Task UnknownKid_RequestsARefresh()
    {
        using var rolled = RSA.Create(2048);
        var token = Token(UserClaims(), key: new RsaSecurityKey(rolled) { KeyId = "rolled-key" });

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        _userKeys.RefreshRequests.ShouldBe(1);
    }

    [Fact]
    public async Task AddAuthKitUserAuthentication_RegistersTheSubjectTokenValidatorWithTheAdditionalIssuers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "AuthKit",
            ["Auth:AuthKit:ClientId"] = ClientId,
            ["Auth:TokenExchange:AdditionalIssuers:0:Issuer"] = AuthKitDomain,
            ["Auth:TokenExchange:AdditionalIssuers:0:JwksUri"] = AuthKitDomainJwks,
        });
        builder.Services.AddAuthKitUserAuthentication(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<IUserSubjectTokenValidator>().ShouldBeOfType<AuthKitUserSubjectTokenValidator>();
        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeSameAs(host.Services.GetRequiredService<AuthKitBearerTokenValidator>());
        host.Services.GetRequiredService<IOptions<TokenExchangeOptions>>().Value.AdditionalIssuers
            .ShouldHaveSingleItem().Issuer.ShouldBe(AuthKitDomain);

        await host.StopAsync();
    }

    public void Dispose()
    {
        _userRsa.Dispose();
        _domainRsa.Dispose();
    }

    private static string Jwks(RSA rsa, string kid)
    {
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return $$"""
            {"keys":[{"kty":"RSA","use":"sig","alg":"RS256","kid":"{{kid}}","n":"{{Base64UrlEncoder.Encode(parameters.Modulus)}}","e":"{{Base64UrlEncoder.Encode(parameters.Exponent)}}"}]}
            """;
    }

    private static IOptions<UserAuthenticationOptions> UserOptions(Action<AuthKitOptions>? configure = null)
    {
        var authKit = new AuthKitOptions { ClientId = ClientId, Issuer = Issuer };
        configure?.Invoke(authKit);
        return Options.Create(new UserAuthenticationOptions { Provider = IdentityProviderKind.AuthKit, AuthKit = authKit });
    }

    private static Dictionary<string, object> UserClaims(Dictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object> { ["sub"] = "user_01", ["sid"] = "session_01" };

        foreach (var (type, value) in extra ?? [])
        {
            claims[type] = value;
        }

        return claims;
    }

    private static string? Value(UserSubjectTokenResult result, string type)
    {
        return result.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }

    private AuthKitUserSubjectTokenValidator CreateValidator(
        Action<AuthKitOptions>? configure = null,
        StaticKeyManager? userKeys = null,
        StaticKeyManager? domainKeys = null)
    {
        var options = UserOptions(configure);
        var normalizer = new AuthKitClaimNormalizer(options);
        var bearer = new AuthKitBearerTokenValidator(options, normalizer, new TestHostEnvironment(), userKeys ?? _userKeys);
        var exchange = Options.Create(new TokenExchangeOptions
        {
            AdditionalIssuers = [new TokenExchangeIssuerOptions { Issuer = AuthKitDomain, JwksUri = AuthKitDomainJwks }],
        });

        return new AuthKitUserSubjectTokenValidator(bearer, normalizer, exchange, jwksUri =>
        {
            _keyManagerUris.Add(jwksUri);
            return domainKeys ?? _domainKeys;
        });
    }

    private string Token(
        Dictionary<string, object> claims,
        string issuer = Issuer,
        string? audience = McpResource,
        DateTime? expires = null,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expiresAt.AddMinutes(-15),
            NotBefore = expiresAt.AddMinutes(-15),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(key ?? _userKey, algorithm),
        });
    }

    private sealed class StaticKeyManager(string? jwks) : IConfigurationManager<JsonWebKeySet>
    {
        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return jwks is null
                ? throw new HttpRequestException("The key set endpoint is unreachable.")
                : Task.FromResult(new JsonWebKeySet(jwks));
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
        }
    }
}
