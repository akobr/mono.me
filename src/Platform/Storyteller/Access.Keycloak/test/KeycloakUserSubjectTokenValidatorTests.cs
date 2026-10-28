using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public sealed class KeycloakUserSubjectTokenValidatorTests : IDisposable
{
    private const string Issuer = "https://keycloak.example/realms/storyteller";
    private const string ApiAudience = "storyteller-api";
    private const string McpResource = "https://mcp.example.com/mcp";
    private const string McpClientId = "mcp-client";
    private const string UserId = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b";

    private static readonly string[] Audiences = [McpResource];

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly StaticConfigurationManager _configuration;

    public KeycloakUserSubjectTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "realm-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_key);
        _configuration = new StaticConfigurationManager(configuration);
    }

    public static TheoryData<Dictionary<string, object>> NotAPerson => new()
    {
        { new Dictionary<string, object> { ["client_id"] = McpClientId } },
        { new Dictionary<string, object> { ["clientId"] = McpClientId } },
        { new Dictionary<string, object> { ["preferred_username"] = "service-account-mcp-client" } },
        { new Dictionary<string, object> { ["typ"] = "ID" } },
    };

    // The person signed in to the actor's client, which Auth:Keycloak:AllowedClientIds (sform) does not list.
    [Fact]
    public async Task PersonToken_IsAcceptedWithTheAccountId()
    {
        var expires = DateTime.UtcNow.AddMinutes(5);
        var claims = UserClaims(new Dictionary<string, object> { ["jti"] = "jti-1", ["auth_time"] = 1793000000L });
        var validator = CreateValidator(options => options.DefaultUserScopes = ["User.Impersonation"]);

        var result = await validator.ValidateAsync(Token(claims, audience: ["account", McpResource], expires: expires), Audiences, [McpClientId]);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.AccountId.ShouldBe(UserId);
        result.ClientId.ShouldBe(McpClientId);
        result.TokenId.ShouldBe("jti-1");
        result.AuthTime.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793000000));
        result.ExpiresAt.ShouldBe(new DateTimeOffset(expires), TimeSpan.FromSeconds(1));
        Values(result, "sub").ShouldBe([UserId]);
        Values(result, "scp").ShouldBe(["User.Impersonation"]);
        Values(result, "name").ShouldBe(["Ada Lovelace"]);
    }

    // Audience binding: a token for the API itself, or for another client, is not this actor's.
    [Theory]
    [InlineData(ApiAudience)]
    [InlineData("account")]
    public async Task TokenForAnotherAudience_IsRefused(string audience)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), audience: [audience]), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidAudienceException).");
    }

    [Theory]
    [InlineData("https://keycloak.example/realms/other")]
    [InlineData("https://login.microsoftonline.com/tenant/v2.0")]
    public async Task TokenOfAnotherIssuer_IsRefusedWithoutFetchingKeys(string issuer)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), issuer: issuer), Audiences, []);

        result.RefusalReason.ShouldBe("The token's issuer is not the realm Auth:Keycloak:Issuer.");
        _configuration.Calls.ShouldBe(0);
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
        var result = await CreateValidator().ValidateAsync(Token(UserClaims()), Audiences, ["claude"]);

        result.RefusalReason.ShouldBe("The token's client (azp) is not one of the actor's AllowedClientIds.");
    }

    [Fact]
    public async Task WithoutAllowedClientIds_AnyClientIsAccepted()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(new Dictionary<string, object> { ["azp"] = "sform" })), Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.ClientId.ShouldBe("sform");
    }

    [Theory]
    [MemberData(nameof(NotAPerson))]
    public async Task ServiceAccountOrIdToken_IsRefused(Dictionary<string, object> extra)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(extra)), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not a person's access token (no sub, a service account, or not typ Bearer).");
    }

    [Fact]
    public async Task TokenWithoutTyp_IsRefused()
    {
        var claims = UserClaims();
        claims.Remove("typ");

        var result = await CreateValidator().ValidateAsync(Token(claims), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not a person's access token (no sub, a service account, or not typ Bearer).");
    }

    [Fact]
    public async Task TokenWithoutSub_IsRefused()
    {
        var claims = UserClaims();
        claims.Remove("sub");

        var result = await CreateValidator().ValidateAsync(Token(claims), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
    }

    [Fact]
    public async Task TokenWithTypBearer_IsAccepted()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(new Dictionary<string, object> { ["typ"] = "Bearer" })), Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
    }

    [Fact]
    public async Task TokenNotSignedWithRs256_IsRefused()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), algorithm: SecurityAlgorithms.RsaSha384), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
    }

    [Fact]
    public async Task NoAudience_IsRefusedWithoutFetchingKeys()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims()), [], []);

        result.RefusalReason.ShouldBe("The actor names no subject audience.");
        _configuration.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task KeysThatCannotBeRetrieved_Throw()
    {
        var validator = CreateValidator(configuration: new StaticConfigurationManager(null));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(Token(UserClaims()), Audiences, []));
    }

    [Fact]
    public async Task AddKeycloakUserAuthentication_RegistersTheSubjectTokenValidator()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "Keycloak",
            ["Auth:Keycloak:Issuer"] = Issuer,
            ["Auth:Keycloak:Audience"] = ApiAudience,
            ["Auth:Keycloak:AllowedClientIds:0"] = "sform",
        });
        builder.Services.AddKeycloakUserAuthentication(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetRequiredService<IUserSubjectTokenValidator>().ShouldBeOfType<KeycloakUserSubjectTokenValidator>();
        host.Services.GetRequiredService<IBearerTokenValidator>().ShouldBeSameAs(host.Services.GetRequiredService<KeycloakBearerTokenValidator>());

        await host.StopAsync();
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static Dictionary<string, object> UserClaims(Dictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = UserId,
            ["typ"] = "Bearer",
            ["azp"] = McpClientId,
            ["preferred_username"] = "ada",
            ["given_name"] = "Ada",
            ["family_name"] = "Lovelace",
            ["scope"] = "openid profile",
        };

        foreach (var (type, value) in extra ?? [])
        {
            claims[type] = value;
        }

        return claims;
    }

    private static string[] Values(UserSubjectTokenResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }

    private KeycloakUserSubjectTokenValidator CreateValidator(
        Action<KeycloakUserOptions>? configure = null,
        StaticConfigurationManager? configuration = null)
    {
        var options = new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions { Issuer = Issuer, Audience = ApiAudience, AllowedClientIds = ["sform"] },
        };
        configure?.Invoke(options.Keycloak);
        var resolved = Options.Create(options);
        var normalizer = new KeycloakClaimNormalizer(resolved);

        return new KeycloakUserSubjectTokenValidator(
            new KeycloakBearerTokenValidator(resolved, normalizer, configuration ?? _configuration),
            normalizer);
    }

    private string Token(
        Dictionary<string, object> claims,
        string issuer = Issuer,
        string[]? audience = null,
        DateTime? expires = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(5);
        var payload = new Dictionary<string, object>(claims) { ["aud"] = audience ?? [McpResource] };

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = payload,
            IssuedAt = expiry.AddMinutes(-10),
            NotBefore = expiry.AddMinutes(-10),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(_key, algorithm),
        });
    }

    private sealed class StaticConfigurationManager(OpenIdConnectConfiguration? configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Calls { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return configuration is null
                ? throw new InvalidOperationException("IDX20803: Unable to obtain configuration.")
                : Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
        }
    }
}
