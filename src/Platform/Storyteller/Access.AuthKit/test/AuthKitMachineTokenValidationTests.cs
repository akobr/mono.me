using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.AuthKit.UnitTests;

// M2M tokens (AuthKitMachineTokenValidator): issued by the AuthKit domain, audience the environment
// client ID, sub == client_id. The user validator leaves them alone.
public sealed class AuthKitMachineTokenValidationTests : IDisposable
{
    private const string ClientId = "client_123";
    private const string UserIssuer = "https://api.workos.com/";
    private const string Domain = "https://example.authkit.app";
    private const string MachineOrganization = "org_machines";

    private readonly RSA _userRsa = RSA.Create(2048);
    private readonly RSA _machineRsa = RSA.Create(2048);
    private readonly RsaSecurityKey _userKey;
    private readonly RsaSecurityKey _machineKey;
    private readonly CountingKeyManager _userKeys;
    private readonly CountingKeyManager _machineKeys;

    public AuthKitMachineTokenValidationTests()
    {
        _userKey = new RsaSecurityKey(_userRsa) { KeyId = "user-key" };
        _machineKey = new RsaSecurityKey(_machineRsa) { KeyId = "machine-key" };
        _userKeys = new CountingKeyManager(_userKey);
        _machineKeys = new CountingKeyManager(_machineKey);
    }

    [Fact]
    public async Task MachineToken_IsAMachineWithMappedScopes()
    {
        var token = MachineToken(new Dictionary<string, object> { ["scope"] = "storyteller:annotation-read storyteller:unmapped" });

        var result = await CreateMachineValidator().ValidateAsync(token);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("client_m2m01");
        Value(result, "azp").ShouldBe("client_m2m01");
        Value(result, "scp").ShouldBe("Annotation.Read");
        Value(result, "org_id").ShouldBe(MachineOrganization);
    }

    [Fact]
    public async Task MachineToken_IsLeftAloneByTheUserValidator()
    {
        var token = MachineToken(new Dictionary<string, object>());

        (await CreateUserValidator().ValidateAsync(token)).ShouldBeNull();
        _userKeys.Calls.ShouldBe(0);
    }

    [Theory]
    [InlineData("org_someone_else")]
    [InlineData(null)]
    public async Task MachineToken_FromAnotherOrganization_IsRejected(string? organization)
    {
        var token = MachineToken(new Dictionary<string, object>(), organization: organization);

        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task MachineToken_ForTheUserAudience_IsRejected()
    {
        var token = MachineToken(new Dictionary<string, object>(), audience: "https://storyteller.42for.net");

        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task MachineToken_SignedWithTheUserKey_IsRejected()
    {
        var token = MachineToken(new Dictionary<string, object>(), signingKey: _userKey);

        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
        _machineKeys.RefreshRequests.ShouldBe(1);
    }

    [Theory]
    [InlineData("client_m2m01", "client_other")]
    [InlineData("user_01", "client_m2m01")]
    [InlineData("user_01", null)]
    public async Task DomainToken_ThatIsNotAnApplication_IsRejected(string subject, string? clientId)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject };
        if (clientId is not null)
        {
            claims["client_id"] = clientId;
        }

        var token = Token(Domain, ClientId, claims, _machineKey, MachineOrganization);

        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
    }

    [Theory]
    [InlineData("https://example.authkit.app", true)]
    [InlineData("https://example.authkit.app/", true)]
    [InlineData("https://api.workos.com/", false)]
    [InlineData("https://example.authkit.app.evil", false)]
    public void CanValidate_OnlyTheAuthKitDomain(string issuer, bool expected)
    {
        CreateMachineValidator().CanValidate(issuer).ShouldBe(expected);
    }

    [Fact]
    public void MachineValidator_WithoutTheMachineOrganization_CannotBeCreated()
    {
        Should.Throw<InvalidOperationException>(() => CreateMachineValidator(machineOrganization: null));
    }

    [Fact]
    public async Task UserToken_IsNotAMachineToken()
    {
        var token = Token(UserIssuer, null, new Dictionary<string, object> { ["sub"] = "user_01", ["email"] = "ada@example.com" }, _userKey, null);

        var user = await CreateUserValidator().ValidateAsync(token);

        user.ShouldNotBeNull();
        user.IsMachine.ShouldBeFalse();
        Value(user, "scp").ShouldBe("User.Impersonation");
        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
        _machineKeys.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task UnknownIssuer_FetchesNoKeys()
    {
        var token = Token("https://evil.example", ClientId, new Dictionary<string, object> { ["sub"] = "client_m2m01", ["client_id"] = "client_m2m01" }, _machineKey, MachineOrganization);

        (await CreateUserValidator().ValidateAsync(token)).ShouldBeNull();
        (await CreateMachineValidator().ValidateAsync(token)).ShouldBeNull();
        _userKeys.Calls.ShouldBe(0);
        _machineKeys.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task SharedCustomDomainIssuer_UsersAndMachinesUseTheirOwnKeys()
    {
        var userValidator = CreateUserValidator(userIssuer: Domain);
        var machineValidator = CreateMachineValidator();
        var userToken = Token(Domain, null, new Dictionary<string, object> { ["sub"] = "user_01" }, _userKey, null);
        var machineToken = MachineToken(new Dictionary<string, object>());

        (await userValidator.ValidateAsync(userToken)).ShouldNotBeNull().IsMachine.ShouldBeFalse();

        // The user validator owns the issuer but not the machine, so the middleware asks the machine validator.
        (await userValidator.ValidateAsync(machineToken)).ShouldBeNull();
        machineValidator.CanValidate(Domain).ShouldBeTrue();
        (await machineValidator.ValidateAsync(machineToken)).ShouldNotBeNull().IsMachine.ShouldBeTrue();
    }

    public void Dispose()
    {
        _userRsa.Dispose();
        _machineRsa.Dispose();
    }

    private static string? Value(BearerValidationResult result, string type)
    {
        return result.Claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }

    private static string Token(string issuer, string? audience, Dictionary<string, object> claims, SecurityKey key, string? organization)
    {
        if (organization is not null)
        {
            claims["org_id"] = organization;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
        });
    }

    private static IOptions<UserAuthenticationOptions> Options(string? machineOrganization = MachineOrganization, string userIssuer = UserIssuer)
    {
        return Microsoft.Extensions.Options.Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.AuthKit,
            AuthKit = new AuthKitOptions
            {
                ClientId = ClientId,
                Issuer = userIssuer,
                AuthKitDomain = Domain,
                MachineOrganizationId = machineOrganization,
                DefaultUserScopes = ["User.Impersonation"],
                PermissionMap = new Dictionary<string, string> { ["storyteller:annotation-read"] = "Annotation.Read" },
            },
        });
    }

    private string MachineToken(
        Dictionary<string, object> claims,
        string? organization = MachineOrganization,
        string audience = ClientId,
        SecurityKey? signingKey = null)
    {
        claims["sub"] = "client_m2m01";
        claims["client_id"] = "client_m2m01";
        return Token(Domain, audience, claims, signingKey ?? _machineKey, organization);
    }

    private AuthKitBearerTokenValidator CreateUserValidator(string userIssuer = UserIssuer)
    {
        var options = Options(userIssuer: userIssuer);
        return new AuthKitBearerTokenValidator(options, new AuthKitClaimNormalizer(options), new TestHostEnvironment(), _userKeys);
    }

    private AuthKitMachineTokenValidator CreateMachineValidator(string? machineOrganization = MachineOrganization)
    {
        var options = Options(machineOrganization);
        return new AuthKitMachineTokenValidator(options, new AuthKitClaimNormalizer(options), _machineKeys);
    }

    private sealed class CountingKeyManager : IConfigurationManager<JsonWebKeySet>
    {
        private readonly JsonWebKeySet _keySet;

        public CountingKeyManager(RsaSecurityKey key)
        {
            _keySet = new JsonWebKeySet();
            _keySet.Keys.Add(JsonWebKeyConverter.ConvertFromRSASecurityKey(key));
        }

        public int Calls { get; private set; }

        public int RefreshRequests { get; private set; }

        public Task<JsonWebKeySet> GetConfigurationAsync(CancellationToken cancel)
        {
            Calls++;
            return Task.FromResult(_keySet);
        }

        public void RequestRefresh()
        {
            RefreshRequests++;
        }
    }
}
