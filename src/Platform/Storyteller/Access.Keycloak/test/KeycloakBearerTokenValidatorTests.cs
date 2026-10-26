using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public sealed class KeycloakBearerTokenValidatorTests : IDisposable
{
    private const string Issuer = "https://keycloak.example/realms/storyteller";
    private const string Audience = "storyteller-api";
    private const string UserId = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly StaticConfigurationManager _configuration;

    public KeycloakBearerTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "realm-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_key);
        _configuration = new StaticConfigurationManager(configuration);
    }

    [Fact]
    public async Task UserToken_IsAUserKeyedBySubWithItsMappedScopes()
    {
        var validator = CreateValidator(options =>
        {
            options.DefaultUserScopes = ["User.Impersonation"];
            options.RoleMap = new Dictionary<string, string> { ["storyteller-api:annotation-reader"] = "Annotation.Read" };
        });

        var result = await validator.ValidateAsync(Token(UserClaims(new Dictionary<string, object>
        {
            ["resource_access"] = new Dictionary<string, object>
            {
                ["storyteller-api"] = new Dictionary<string, object> { ["roles"] = new[] { "annotation-reader" } },
            },
            ["scp"] = "Secrets.Read",
        })));

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        Values(result, "sub").ShouldBe([UserId]);
        Values(result, "scp").ShouldBe(["User.Impersonation Annotation.Read"]);
        Values(result, "name").ShouldBe(["Ada Lovelace"]);
        Values(result, "preferred_username").ShouldBe(["ada"]);
        UserIdentity.GetAccountId(result.Claims, IdentityProviderKind.Keycloak).ShouldBe(UserId);
    }

    [Fact]
    public async Task UserToken_WithTypBearer_IsAccepted_AndAnIssuerWithATrailingSlashMatches()
    {
        var validator = CreateValidator();

        var result = await validator.ValidateAsync(Token(UserClaims(new Dictionary<string, object> { ["typ"] = "Bearer" }), issuer: $"{Issuer}/"));

        result.ShouldNotBeNull().IsMachine.ShouldBeFalse();
    }

    [Theory]
    [InlineData("client_id")]
    [InlineData("clientId")]
    public async Task ServiceAccountToken_WithAClientIdClaim_IsNotAUser(string claimType)
    {
        var validator = CreateValidator();
        var claims = UserClaims(new Dictionary<string, object> { [claimType] = "42.sform.org1.proj1.abc" });
        claims["preferred_username"] = "robot";

        (await validator.ValidateAsync(Token(claims))).ShouldBeNull();
    }

    [Theory]
    [InlineData("service-account-42.sform.org1.proj1.abc")]
    [InlineData("Service-Account-machine")]
    public async Task ServiceAccountToken_ByItsUserName_IsNotAUser(string userName)
    {
        var validator = CreateValidator();
        var claims = UserClaims();
        claims["preferred_username"] = userName;

        (await validator.ValidateAsync(Token(claims))).ShouldBeNull();
    }

    [Theory]
    [InlineData("ID")]
    [InlineData("Refresh")]
    [InlineData("")]
    public async Task TokenOfAnotherType_IsNotAUser(string tokenType)
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(new Dictionary<string, object> { ["typ"] = tokenType })))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutSub_IsNotAUser()
    {
        var validator = CreateValidator();
        var claims = UserClaims();
        claims.Remove("sub");

        (await validator.ValidateAsync(Token(claims))).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://keycloak.example/realms/other")]
    [InlineData(Issuer + "2")]
    public async Task TokenOfAnotherRealm_IsRejectedWithoutFetchingKeys(string issuer)
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(), issuer: issuer))).ShouldBeNull();
        _configuration.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("account")]
    [InlineData("sform")]
    public async Task TokenForAnotherAudience_IsRejected(string audience)
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(), audience: audience))).ShouldBeNull();
    }

    [Fact]
    public async Task ClientOutsideAllowedClientIds_IsRejected()
    {
        var validator = CreateValidator(options => options.AllowedClientIds = ["sform", "admin-ui"]);
        var claims = UserClaims();
        claims["azp"] = "other-app";

        (await validator.ValidateAsync(Token(claims))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutAzp_IsRejected()
    {
        var validator = CreateValidator();
        var claims = UserClaims();
        claims.Remove("azp");

        (await validator.ValidateAsync(Token(claims))).ShouldBeNull();
    }

    [Fact]
    public async Task AllowedClient_IsAccepted()
    {
        var validator = CreateValidator(options => options.AllowedClientIds = ["sform", "admin-ui"]);
        var claims = UserClaims();
        claims["azp"] = "admin-ui";

        (await validator.ValidateAsync(Token(claims))).ShouldNotBeNull();
    }

    // Without the list, the audience would be the only gate, and Keycloak adds a client to the aud of other clients' tokens.
    [Fact]
    public void EmptyAllowedClientIds_ThrowsAtConstruction()
    {
        Should.Throw<InvalidOperationException>(() => CreateValidator(options => options.AllowedClientIds = []));
        Should.Throw<InvalidOperationException>(() => CreateValidator(options => options.AllowedClientIds = [" "]));
    }

    [Fact]
    public async Task ExpiredToken_IsRejected()
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(), expires: DateTime.UtcNow.AddMinutes(-2)))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithinTheClockSkew_IsAccepted()
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(), expires: DateTime.UtcNow.AddSeconds(-10)))).ShouldNotBeNull();
    }

    [Fact]
    public async Task TokenWithoutExpiry_IsRejected()
    {
        var validator = CreateValidator();
        var token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Claims = UserClaims(),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        });

        (await validator.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenSignedWithAnotherAlgorithm_IsRejected()
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(Token(UserClaims(), algorithm: SecurityAlgorithms.RsaSha512))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenSignedWithAnUnknownKey_IsRejectedAndRefreshesTheKeys()
    {
        var validator = CreateValidator();
        using var other = RSA.Create(2048);

        var result = await validator.ValidateAsync(Token(UserClaims(), key: new RsaSecurityKey(other) { KeyId = "rotated" }));

        result.ShouldBeNull();
        _configuration.RefreshRequests.ShouldBe(1);
    }

    [Fact]
    public async Task KeyRetrievalFailure_Throws()
    {
        var validator = CreateValidator(configuration: new StaticConfigurationManager(null));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(Token(UserClaims())));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task UnreadableToken_IsRejectedWithoutFetchingKeys(string token)
    {
        var validator = CreateValidator();

        (await validator.ValidateAsync(token)).ShouldBeNull();
        _configuration.Calls.ShouldBe(0);
    }

    [Fact]
    public void MissingIssuerOrAudience_Throws()
    {
        Should.Throw<InvalidOperationException>(() => CreateValidator(options => options.Issuer = string.Empty));
        Should.Throw<InvalidOperationException>(() => CreateValidator(options => options.Audience = " "));
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
            ["azp"] = "sform",
            ["preferred_username"] = "ada",
            ["email"] = "ada@example.com",
            ["email_verified"] = true,
            ["given_name"] = "Ada",
            ["family_name"] = "Lovelace",
            ["scope"] = "openid profile email",
        };

        foreach (var (type, value) in extra ?? [])
        {
            claims[type] = value;
        }

        return claims;
    }

    private static string[] Values(BearerValidationResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }

    private KeycloakBearerTokenValidator CreateValidator(Action<KeycloakUserOptions>? configure = null, StaticConfigurationManager? configuration = null)
    {
        var options = new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.Keycloak,
            Keycloak = new KeycloakUserOptions { Issuer = Issuer, Audience = Audience, AllowedClientIds = ["sform"] },
        };
        configure?.Invoke(options.Keycloak);
        var resolved = Options.Create(options);

        return new KeycloakBearerTokenValidator(resolved, new KeycloakClaimNormalizer(resolved), configuration ?? _configuration);
    }

    private string Token(
        Dictionary<string, object> claims,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expires = null,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var expiry = expires ?? DateTime.UtcNow.AddMinutes(5);

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = expiry.AddMinutes(-10),
            NotBefore = expiry.AddMinutes(-10),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(key ?? _key, algorithm),
        });
    }

    private sealed class StaticConfigurationManager(OpenIdConnectConfiguration? configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return configuration is null
                ? throw new InvalidOperationException("IDX20803: Unable to obtain configuration.")
                : Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
        }
    }
}
