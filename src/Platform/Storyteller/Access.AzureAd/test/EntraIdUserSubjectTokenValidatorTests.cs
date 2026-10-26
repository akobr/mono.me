using System.Security.Claims;
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

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public sealed class EntraIdUserSubjectTokenValidatorTests : IDisposable
{
    private const string TenantId = "0f1e2d3c-4b5a-4968-8776-655443322110";
    private const string OtherTenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    private const string ConsumerTenantId = "9188040d-6c67-4c5b-b112-36a304b66dad";
    private const string ObjectId = "3f2c6a8e-6c1d-4a59-9a47-0a2f4f4c1b2d";
    private const string ApiClientId = "api-client-id";
    private const string McpClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11";
    private const string McpResource = "https://mcp.example.com/mcp";
    private const string V2Issuer = $"https://login.microsoftonline.com/{TenantId}/v2.0";
    private const string V1Issuer = $"https://sts.windows.net/{TenantId}/";

    private static readonly string[] Audiences = [McpResource, McpClientId];

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly StaticConfigurationManager _configuration;

    public EntraIdUserSubjectTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
        _configuration = new StaticConfigurationManager(Configuration(_key));
    }

    [Fact]
    public async Task DelegatedUserToken_IsAcceptedWithTheAccountId()
    {
        var expires = DateTime.UtcNow.AddMinutes(30);
        var claims = UserClaims(new Dictionary<string, object>
        {
            ["azp"] = McpClientId,
            ["uti"] = "uti-1",
            ["name"] = "Ada Lovelace",
        });
        var token = Token(claims, expires: expires);

        var result = await CreateValidator().ValidateAsync(token, Audiences, [McpClientId]);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.AccountId.ShouldBe($"{TenantId}:{ObjectId}");
        result.ClientId.ShouldBe(McpClientId);
        result.TokenId.ShouldBe("uti-1");
        result.AuthTime.ShouldBeNull();
        result.ExpiresAt.ShouldBe(new DateTimeOffset(expires), TimeSpan.FromSeconds(1));
        result.RefusalReason.ShouldBeNull();

        // The claims API routes get for the same person: sub is the account id, the pairwise sub stays as entra_sub.
        Values(result, "sub").ShouldBe([$"{TenantId}:{ObjectId}"]);
        Values(result, UserIdentity.EntraSubjectClaimType).ShouldBe(["pairwise-sub"]);
        Values(result, ClaimTypes.NameIdentifier).ShouldBeEmpty();
        Values(result, "name").ShouldBe(["Ada Lovelace"]);
    }

    [Fact]
    public async Task V1Token_IsAcceptedWithAppIdAsTheClientAndItsJtiAndAuthTime()
    {
        var token = Token(
            UserClaims(new Dictionary<string, object> { ["appid"] = McpClientId, ["jti"] = "jti-1", ["auth_time"] = 1793000000L }),
            issuer: V1Issuer,
            audience: McpResource);

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        result.ClientId.ShouldBe(McpClientId);
        result.TokenId.ShouldBe("jti-1");
        result.AuthTime.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1793000000));
    }

    // Audience binding: a token the person got for the API itself, or for another app, is not this actor's.
    [Theory]
    [InlineData($"api://{ApiClientId}")]
    [InlineData("https://other.example.com")]
    public async Task TokenForAnotherAudience_IsRefused(string audience)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), audience: audience), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidAudienceException).");
    }

    [Theory]
    [InlineData($"https://login.microsoftonline.com/{OtherTenantId}/v2.0")]
    [InlineData($"https://sts.windows.net/{OtherTenantId}/")]
    [InlineData($"https://login.microsoftonline.com/{TenantId}/")]
    [InlineData($"https://login.microsoftonline.com.evil/{TenantId}/v2.0")]
    [InlineData("https://keycloak.example.com/realms/storyteller")]
    public async Task TokenOfAnotherIssuer_IsRefused(string issuer)
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(), issuer: issuer), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
    }

    // The issuer is pinned to the configured tenant also when the token says it is of another one.
    [Fact]
    public async Task TokenOfAnotherTenant_IsRefused()
    {
        var token = Token(
            UserClaims(new Dictionary<string, object> { ["tid"] = OtherTenantId }),
            issuer: $"https://login.microsoftonline.com/{OtherTenantId}/v2.0");

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
    }

    [Theory]
    [InlineData("common")]
    [InlineData("Common")]
    [InlineData("organizations")]
    public async Task MultiTenantSetting_AcceptsTheIssuerOfTheTokensOwnTenant(string tenantSetting)
    {
        var validator = CreateValidator(tenantSetting);
        var otherTenant = Token(
            UserClaims(new Dictionary<string, object> { ["tid"] = OtherTenantId }),
            issuer: $"https://sts.windows.net/{OtherTenantId}/");
        var mismatched = Token(
            UserClaims(new Dictionary<string, object> { ["tid"] = OtherTenantId }),
            issuer: V2Issuer);

        var accepted = await validator.ValidateAsync(otherTenant, Audiences, []);
        var refused = await validator.ValidateAsync(mismatched, Audiences, []);

        accepted.IsAccepted.ShouldBeTrue(accepted.RefusalReason);
        accepted.AccountId.ShouldBe($"{OtherTenantId}:{ObjectId}");
        refused.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
    }

    // A verified domain names one tenant: its metadata issuer pins it, as a GUID setting would.
    [Fact]
    public async Task DomainNameSetting_PinsTheTenantOfTheMetadata()
    {
        var configuration = new StaticConfigurationManager(Configuration(_key, V2Issuer));
        var validator = CreateValidator("contoso.onmicrosoft.com", configuration);
        var otherTenant = Token(
            UserClaims(new Dictionary<string, object> { ["tid"] = OtherTenantId }),
            issuer: $"https://login.microsoftonline.com/{OtherTenantId}/v2.0");

        var v2 = await validator.ValidateAsync(Token(UserClaims()), Audiences, []);
        var v1 = await validator.ValidateAsync(Token(UserClaims(), issuer: V1Issuer), Audiences, []);
        var refused = await validator.ValidateAsync(otherTenant, Audiences, []);

        v2.IsAccepted.ShouldBeTrue(v2.RefusalReason);
        v2.AccountId.ShouldBe($"{TenantId}:{ObjectId}");
        v1.IsAccepted.ShouldBeTrue(v1.RefusalReason);
        refused.IsAccepted.ShouldBeFalse();
        refused.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
    }

    // Metadata that names no tenant (the templated {tenantid} of common, or a mis-set value) refuses every token.
    [Theory]
    [InlineData("https://login.microsoftonline.com/{tenantid}/v2.0")]
    [InlineData($"https://login.microsoftonline.com.evil/{TenantId}/v2.0")]
    [InlineData($"http://login.microsoftonline.com/{TenantId}/v2.0")]
    [InlineData($"https://sts.windows.net/{TenantId}/")]
    [InlineData("")]
    public async Task DomainNameSetting_WithoutATenantIssuerInTheMetadata_IsRefused(string metadataIssuer)
    {
        var configuration = new StaticConfigurationManager(Configuration(_key, metadataIssuer));
        var validator = CreateValidator("contoso.onmicrosoft.com", configuration);

        var result = await validator.ValidateAsync(Token(UserClaims()), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The configured tenant could not be resolved from the OpenID metadata.");
    }

    // The organizations endpoint excludes personal Microsoft accounts; common admits them.
    [Theory]
    [InlineData("organizations", false)]
    [InlineData("common", true)]
    public async Task PersonalAccount_IsRefusedUnderOrganizationsOnly(string tenantSetting, bool accepted)
    {
        var token = Token(
            UserClaims(new Dictionary<string, object> { ["tid"] = ConsumerTenantId }),
            issuer: $"https://login.microsoftonline.com/{ConsumerTenantId}/v2.0");

        var result = await CreateValidator(tenantSetting).ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBe(accepted, result.RefusalReason);

        if (!accepted)
        {
            result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
        }
    }

    [Fact]
    public async Task ExpiredToken_IsRefused()
    {
        var token = Token(UserClaims(), expires: DateTime.UtcNow.AddMinutes(-10));

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenExpiredException).");
    }

    [Fact]
    public async Task TokenWithoutExp_IsRefused()
    {
        var descriptor = Descriptor(UserClaims(), V2Issuer, McpResource, DateTime.UtcNow.AddMinutes(10));
        descriptor.Expires = null;
        descriptor.SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256);
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };

        var result = await CreateValidator().ValidateAsync(handler.CreateToken(descriptor), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenNoExpirationException).");
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
        var token = Token(UserClaims(new Dictionary<string, object> { ["azp"] = "11111111-2222-3333-4444-555555555555" }));

        var result = await CreateValidator().ValidateAsync(token, Audiences, [McpClientId]);

        result.RefusalReason.ShouldBe("The token's client (azp or appid) is not one of the actor's AllowedClientIds.");
    }

    // Entra writes the client id in lower case; a setting may not.
    [Fact]
    public async Task AllowedClientIds_AreComparedIgnoringCase()
    {
        var token = Token(UserClaims(new Dictionary<string, object> { ["azp"] = McpClientId }));

        var result = await CreateValidator().ValidateAsync(token, Audiences, [McpClientId.ToUpperInvariant()]);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
    }

    [Fact]
    public async Task TokenWithoutAClient_IsRefusedWhenClientsAreListed()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims()), Audiences, [McpClientId]);

        result.IsAccepted.ShouldBeFalse();
    }

    [Fact]
    public async Task AppOnlyToken_IsRefused()
    {
        var token = Token(UserClaims(new Dictionary<string, object> { ["idtyp"] = "app" }));

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.RefusalReason.ShouldBe("The token is not a delegated user token (no scp, or idtyp app).");
    }

    [Fact]
    public async Task TokenWithRolesAndNoScope_IsRefused()
    {
        var claims = UserClaims(new Dictionary<string, object> { ["roles"] = new[] { "Annotation.Read" } });
        claims.Remove("scp");

        var result = await CreateValidator().ValidateAsync(Token(claims), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not a delegated user token (no scp, or idtyp app).");
    }

    [Fact]
    public async Task TokenWithABlankScope_IsRefused()
    {
        var result = await CreateValidator().ValidateAsync(Token(UserClaims(new Dictionary<string, object> { ["scp"] = " " })), Audiences, []);

        result.RefusalReason.ShouldBe("The token is not a delegated user token (no scp, or idtyp app).");
    }

    [Fact]
    public async Task TokenWithoutOid_IsRefused()
    {
        var claims = UserClaims();
        claims.Remove("oid");

        var result = await CreateValidator().ValidateAsync(Token(claims), Audiences, []);

        result.RefusalReason.ShouldBe("The token has no GUID tid and oid.");
    }

    [Fact]
    public async Task TokenWithoutATenant_IsRefused()
    {
        var claims = UserClaims();
        claims.Remove("tid");

        var result = await CreateValidator().ValidateAsync(Token(claims), Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldBe("The token is not valid (SecurityTokenInvalidIssuerException).");
    }

    [Fact]
    public async Task TokenNotSignedWithRs256_IsRefused()
    {
        var token = Token(UserClaims(), algorithm: SecurityAlgorithms.RsaSha384);

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        result.RefusalReason.ShouldNotBeNull().ShouldStartWith("The token is not valid (");
    }

    [Fact]
    public async Task TokenOfAnotherKey_IsRefused()
    {
        using var other = RSA.Create(2048);
        var token = Token(UserClaims(), key: new RsaSecurityKey(other) { KeyId = "test-key" });

        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    public async Task NoToken_IsRefusedWithoutFetchingKeys(string token)
    {
        var result = await CreateValidator().ValidateAsync(token, Audiences, []);

        result.IsAccepted.ShouldBeFalse();
        _configuration.Calls.ShouldBe(0);
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

    // A rolled signing key: the metadata is fetched again and the token is checked once more, as on API routes.
    [Fact]
    public async Task UnknownKid_RefreshesTheKeysAndRetries()
    {
        using var rolled = RSA.Create(2048);
        var rolledKey = new RsaSecurityKey(rolled) { KeyId = "rolled-key" };
        var configuration = new RefreshingConfigurationManager(Configuration(_key), Configuration(rolledKey));

        var result = await CreateValidator(configuration: configuration).ValidateAsync(Token(UserClaims(), key: rolledKey), Audiences, []);

        result.IsAccepted.ShouldBeTrue(result.RefusalReason);
        configuration.RefreshRequests.ShouldBe(1);
    }

    [Fact]
    public async Task AddEntraIdUserAuthentication_RegistersTheSubjectTokenValidator()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Environment.EnvironmentName = Environments.Production;
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:Provider"] = "EntraId",
            ["Auth:TenantId"] = TenantId,
            ["Auth:ClientId"] = ApiClientId,
        });
        builder.Services.AddEntraIdUserAuthentication(builder.Configuration);
        builder.Services.AddEntraIdUserAuthentication(builder.Configuration);
        using var host = builder.Build();

        await host.StartAsync();

        host.Services.GetServices<IUserSubjectTokenValidator>().ShouldHaveSingleItem().ShouldBeOfType<EntraIdUserSubjectTokenValidator>();

        await host.StopAsync();
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    // The issuer of the common and organizations metadata by default; one tenant's metadata names its GUID.
    private static OpenIdConnectConfiguration Configuration(SecurityKey key, string issuer = "https://login.microsoftonline.com/{tenantid}/v2.0")
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer };
        configuration.SigningKeys.Add(key);
        return configuration;
    }

    private static Dictionary<string, object> UserClaims(Dictionary<string, object>? extra = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "pairwise-sub",
            ["tid"] = TenantId,
            ["oid"] = ObjectId,
            ["scp"] = "Mcp.Access",
            ["ver"] = "2.0",
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

    private static SecurityTokenDescriptor Descriptor(Dictionary<string, object> claims, string issuer, string audience, DateTime expires)
    {
        return new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-5),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = expires,
        };
    }

    private EntraIdUserSubjectTokenValidator CreateValidator(string tenantId = TenantId, IConfigurationManager<OpenIdConnectConfiguration>? configuration = null)
    {
        var options = Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = tenantId,
            ClientId = ApiClientId,
        });
        var keys = new EntraIdBearerTokenValidator(options, new TestHostEnvironment(), configuration ?? _configuration);

        return new EntraIdUserSubjectTokenValidator(keys, options);
    }

    private string Token(
        Dictionary<string, object> claims,
        string issuer = V2Issuer,
        string audience = McpClientId,
        DateTime? expires = null,
        SecurityKey? key = null,
        string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(30);
        var descriptor = Descriptor(claims, issuer, audience, expiresAt);

        if (expiresAt < DateTime.UtcNow)
        {
            descriptor.IssuedAt = descriptor.NotBefore = expiresAt.AddMinutes(-30);
        }

        descriptor.SigningCredentials = new SigningCredentials(key ?? _key, algorithm);
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private sealed class StaticConfigurationManager(OpenIdConnectConfiguration? configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int Calls { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return configuration is null
                ? throw new HttpRequestException("The metadata endpoint is unreachable.")
                : Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
        }
    }

    // Returns the published keys until RequestRefresh, then the rolled set.
    private sealed class RefreshingConfigurationManager(OpenIdConnectConfiguration current, OpenIdConnectConfiguration rolled)
        : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private bool _refreshed;

        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            return Task.FromResult(_refreshed ? rolled : current);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
            _refreshed = true;
        }
    }
}
