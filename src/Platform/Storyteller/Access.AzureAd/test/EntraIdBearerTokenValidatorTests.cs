using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class EntraIdBearerTokenValidatorTests
{
    [Fact]
    public async Task Validate_UserToken_ReturnsClaimsAndIsNotAMachine()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [
                new Claim("sub", "user-1"),
                new Claim("name", "Ada Lovelace"),
                new Claim("preferred_username", "ada@example.com"),
                new Claim("scp", "User.Impersonation Default.ReadWrite"),
                new Claim("azp", fixture.ClientId),
            ]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        FindClaim(result, "sub", ClaimTypes.NameIdentifier).ShouldBe("user-1");
        FindClaim(result, "name").ShouldBe("Ada Lovelace");
        FindClaim(result, "preferred_username").ShouldBe("ada@example.com");
        result.Claims.ShouldContain(claim => claim.Value.Contains("User.Impersonation", StringComparison.Ordinal));
        result.Claims.ShouldContain(claim => claim.Value.Contains("Default.ReadWrite", StringComparison.Ordinal));
        FindClaim(result, "azp").ShouldBe(fixture.ClientId);
    }

    [Fact]
    public async Task Validate_ClientIdAudience_IsAccepted()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V2Issuer, fixture.ClientId, [new Claim("sub", "user-1")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_V1Issuer_IsAccepted()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V1Issuer, fixture.ApiAudience, [new Claim("sub", "user-1")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Validate_IssuerComparison_IsCaseInsensitive()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V2Issuer.ToUpperInvariant(), fixture.ApiAudience, [new Claim("sub", "user-1")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
    }

    [Fact]
    public async Task Validate_MachineToken_UsesAzpWhenItDiffersFromTheApiClient()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [
                new Claim("sub", "machine-app"),
                new Claim("azp", "machine-app"),
                new Claim("appid", "ignored-app"),
                new Claim("roles", "Default.ReadWrite"),
            ]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
    }

    [Fact]
    public async Task Validate_AppIdWithoutAzp_IsAMachine()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "machine-app"), new Claim("appid", "machine-app")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
    }

    [Fact]
    public async Task Validate_AzpMatchingClientId_IsNotAMachine()
    {
        using var fixture = new TokenFixture(clientId: "Client-Id");
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1"), new Claim("azp", "client-id"), new Claim("appid", "other-app")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("https://login.microsoftonline.com.evil/tenant/v2.0")]
    public async Task Validate_WrongIssuer_ReturnsNull(string issuer)
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(issuer, fixture.ApiAudience, [new Claim("sub", "user-1")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_WrongAudience_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V2Issuer, "api://someone-else", [new Claim("sub", "user-1")]);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_ExpiredToken_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1")],
            expires: DateTime.UtcNow.AddHours(-1));

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_TokenNotYetValid_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1")],
            notBefore: DateTime.UtcNow.AddHours(1),
            expires: DateTime.UtcNow.AddHours(2));

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_HmacToken_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        var symmetric = new SymmetricSecurityKey("0123456789abcdef0123456789abcdef"u8.ToArray())
        {
            KeyId = "sym",
        };
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1")],
            signingKey: symmetric,
            algorithm: SecurityAlgorithms.HmacSha256);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task Validate_UnknownSigningKey_ReturnsNull()
    {
        using var fixture = new TokenFixture();
        using var otherRsa = RSA.Create(2048);
        var otherKey = new RsaSecurityKey(otherRsa) { KeyId = "other-key" };
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1")],
            signingKey: otherKey);

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
        fixture.Manager.RefreshRequests.ShouldBe(1);
        fixture.Manager.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task Validate_RolledSigningKey_RefreshesAndAcceptsTheNewKey()
    {
        using var fixture = new TokenFixture();
        using var rolledRsa = RSA.Create(2048);
        var rolledKey = new RsaSecurityKey(rolledRsa) { KeyId = "rolled-key" };
        var rolledConfiguration = new OpenIdConnectConfiguration
        {
            Issuer = fixture.Configuration.Issuer,
        };
        rolledConfiguration.SigningKeys.Add(rolledKey);
        var manager = new RefreshingConfigurationManager(fixture.Configuration, rolledConfiguration);
        var validator = new EntraIdBearerTokenValidator(
            Options.Create(new UserAuthenticationOptions
            {
                Provider = IdentityProviderKind.EntraId,
                ClientId = fixture.ClientId,
                TenantId = fixture.TenantId,
            }),
            new TestHostEnvironment(),
            manager);
        var rawToken = fixture.CreateToken(
            fixture.V2Issuer,
            fixture.ApiAudience,
            [new Claim("sub", "user-1")],
            signingKey: rolledKey);

        var result = await validator.ValidateAsync(rawToken);

        result.ShouldNotBeNull();
        FindClaim(result, "sub", ClaimTypes.NameIdentifier).ShouldBe("user-1");
        manager.RefreshRequests.ShouldBe(1);
        manager.Calls.ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("not-a-jwt")]
    public async Task Validate_UnreadableToken_ReturnsNullWithoutFetchingKeys(string rawToken)
    {
        using var fixture = new TokenFixture();

        var result = await fixture.Validator.ValidateAsync(rawToken);

        result.ShouldBeNull();
        fixture.Manager.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task Validate_KeyRetrievalFailure_Throws()
    {
        using var fixture = new TokenFixture(failure: new HttpRequestException("jwks down"));
        var rawToken = fixture.CreateToken(fixture.V2Issuer, fixture.ApiAudience, [new Claim("sub", "user-1")]);

        var exception = await Should.ThrowAsync<BearerKeyRetrievalException>(() => fixture.Validator.ValidateAsync(rawToken));

        exception.InnerException.ShouldBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task Validate_RepeatedCalls_ReuseTheConfigurationManager()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V2Issuer, fixture.ApiAudience, [new Claim("sub", "user-1")]);

        (await fixture.Validator.ValidateAsync(rawToken)).ShouldNotBeNull();
        (await fixture.Validator.ValidateAsync(rawToken)).ShouldNotBeNull();

        fixture.Manager.Calls.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Validate_Cancellation_Propagates()
    {
        using var fixture = new TokenFixture();
        var rawToken = fixture.CreateToken(fixture.V2Issuer, fixture.ApiAudience, [new Claim("sub", "user-1")]);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => fixture.Validator.ValidateAsync(rawToken, cancellation.Token));
    }

    [Fact]
    public void Constructor_MissingTenant_Throws()
    {
        var options = Options.Create(new UserAuthenticationOptions { ClientId = "client" });

        Should.Throw<InvalidOperationException>(() => new EntraIdBearerTokenValidator(options, new TestHostEnvironment()));
    }

    [Fact]
    public async Task Validate_MissingClientId_Throws()
    {
        var configuration = new OpenIdConnectConfiguration();
        var validator = new EntraIdBearerTokenValidator(
            Options.Create(new UserAuthenticationOptions { TenantId = "tenant" }),
            new TestHostEnvironment(),
            new StaticConfigurationManager(configuration));

        await Should.ThrowAsync<InvalidOperationException>(() => validator.ValidateAsync("token"));
    }

    private static string? FindClaim(BearerValidationResult result, params string[] types)
    {
        foreach (var type in types)
        {
            var claim = result.Claims.FirstOrDefault(candidate => candidate.Type == type);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        return null;
    }

    private sealed class TokenFixture : IDisposable
    {
        public TokenFixture(string clientId = "client-id", string tenantId = "tenant-id", Exception? failure = null)
        {
            Rsa = RSA.Create(2048);
            Key = new RsaSecurityKey(Rsa) { KeyId = "test-key" };
            ClientId = clientId;
            TenantId = tenantId;
            Configuration = new OpenIdConnectConfiguration
            {
                Issuer = $"https://login.microsoftonline.com/{tenantId}/v2.0",
            };
            Configuration.SigningKeys.Add(Key);
            Manager = new StaticConfigurationManager(Configuration, failure);
            Validator = new EntraIdBearerTokenValidator(
                Options.Create(new UserAuthenticationOptions
                {
                    Provider = IdentityProviderKind.EntraId,
                    ClientId = clientId,
                    TenantId = tenantId,
                }),
                new TestHostEnvironment(),
                Manager);
        }

        public RSA Rsa { get; }

        public RsaSecurityKey Key { get; }

        public string ClientId { get; }

        public string TenantId { get; }

        public OpenIdConnectConfiguration Configuration { get; }

        public StaticConfigurationManager Manager { get; }

        public EntraIdBearerTokenValidator Validator { get; }

        public string V2Issuer => $"https://login.microsoftonline.com/{TenantId}/v2.0";

        public string V1Issuer => $"https://sts.windows.net/{TenantId}/";

        public string ApiAudience => $"api://{ClientId}";

        public string CreateToken(
            string issuer,
            string audience,
            IEnumerable<Claim> claims,
            DateTime? expires = null,
            DateTime? notBefore = null,
            SecurityKey? signingKey = null,
            string? algorithm = null)
        {
            var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(10);
            var notBeforeAt = notBefore ?? (expiresAt < DateTime.UtcNow ? expiresAt.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(-5));
            var handler = new JwtSecurityTokenHandler();
            var descriptor = new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                Subject = new ClaimsIdentity(claims),
                NotBefore = notBeforeAt,
                Expires = expiresAt,
                SigningCredentials = new SigningCredentials(
                    signingKey ?? Key,
                    algorithm ?? SecurityAlgorithms.RsaSha256),
            };

            return handler.WriteToken(handler.CreateToken(descriptor));
        }

        public void Dispose()
        {
            Rsa.Dispose();
        }
    }

    private sealed class StaticConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _configuration;
        private readonly Exception? _failure;

        public StaticConfigurationManager(OpenIdConnectConfiguration configuration, Exception? failure = null)
        {
            _configuration = configuration;
            _failure = failure;
        }

        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            cancel.ThrowIfCancellationRequested();

            if (_failure is not null)
            {
                throw _failure;
            }

            return Task.FromResult(_configuration);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
        }
    }

    // Returns the published keys until RequestRefresh, then the rolled set.
    private sealed class RefreshingConfigurationManager : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private readonly OpenIdConnectConfiguration _current;
        private readonly OpenIdConnectConfiguration _rolled;
        private bool _refreshed;

        public RefreshingConfigurationManager(OpenIdConnectConfiguration current, OpenIdConnectConfiguration rolled)
        {
            _current = current;
            _rolled = rolled;
        }

        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            cancel.ThrowIfCancellationRequested();
            return Task.FromResult(_refreshed ? _rolled : _current);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
            _refreshed = true;
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
