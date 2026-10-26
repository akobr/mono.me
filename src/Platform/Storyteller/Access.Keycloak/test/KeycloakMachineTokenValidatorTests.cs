using System.Security.Cryptography;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Keycloak.UnitTests;

public sealed class KeycloakMachineTokenValidatorTests : IDisposable
{
    private const string Issuer = "https://keycloak.example/realms/storyteller";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly StaticConfigurationManager _configuration;
    private readonly KeycloakMachineTokenValidator _validator;

    public KeycloakMachineTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "realm-key" };
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(_key);
        _configuration = new StaticConfigurationManager(configuration);

        _validator = new KeycloakMachineTokenValidator(
            Options.Create(new KeycloakOptions { ServerUrl = "https://keycloak.example/", Realm = "storyteller", Audience = "storyteller-api" }),
            _configuration);
    }

    [Fact]
    public async Task MachineToken_IsAMachineWithItsStorytellerScopes()
    {
        var result = await _validator.ValidateAsync(Token(new Dictionary<string, object>
        {
            ["azp"] = "42.sform.org1.proj1.abc",
            ["client_id"] = "42.sform.org1.proj1.abc",
            ["sub"] = "b0c1d2e3-service-account",
            ["storyteller_scope"] = "Annotation.Read Annotation.Write",
            ["scp"] = "Secrets.Read",
            ["roles"] = "Configuration.Write",
        }));

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("42.sform.org1.proj1.abc");
        Values(result, "scp").ShouldBe(["Annotation.Read Annotation.Write"]);
        Values(result, "azp").ShouldBe(["42.sform.org1.proj1.abc"]);
        Values(result, "roles").ShouldBeEmpty();
        Values(result, "storyteller_scope").ShouldBeEmpty();
    }

    // Service accounts of the realm are machines, whichever of the claims Keycloak versions write names them.
    [Theory]
    [InlineData("client_id", "machine")]
    [InlineData("clientId", "machine")]
    [InlineData("preferred_username", "service-account-machine")]
    public async Task ServiceAccountToken_IsTheMachineOfItsClient(string claimType, string value)
    {
        var result = await _validator.ValidateAsync(Token(new Dictionary<string, object> { ["azp"] = "machine", [claimType] = value }));

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine");
    }

    // A person's token of the realm is never a machine, whatever its azp: an access token of a sign-in client the user
    // validator refused, or an ID token whose aud is the machine audience.
    [Theory]
    [InlineData("sform", "Bearer")]
    [InlineData("storyteller-api", "ID")]
    public async Task PersonToken_IsNotAMachine(string clientId, string tokenType)
    {
        var token = Token(new Dictionary<string, object>
        {
            ["sub"] = "5f1c2b3a-9d8e-4f70-8a6b-1c2d3e4f5a6b",
            ["azp"] = clientId,
            ["typ"] = tokenType,
            ["preferred_username"] = "ada",
        });

        (await _validator.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutTheAudience_IsRejected()
    {
        var token = Token(MachineClaims(), audience: "account");

        (await _validator.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenFromAnotherRealm_IsRejected()
    {
        var token = Token(MachineClaims(), issuer: "https://keycloak.example/realms/other");

        (await _validator.ValidateAsync(token)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenWithoutAzp_IsRejected()
    {
        (await _validator.ValidateAsync(Token(new Dictionary<string, object> { ["sub"] = "someone", ["client_id"] = "machine" }))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenSignedWithAnotherKey_IsRejectedAndRefreshesTheKeys()
    {
        using var other = RSA.Create(2048);
        var token = Token(MachineClaims(), key: new RsaSecurityKey(other) { KeyId = "rotated" });

        (await _validator.ValidateAsync(token)).ShouldBeNull();
        _configuration.RefreshRequests.ShouldBe(1);
    }

    [Fact]
    public async Task KeyRetrievalFailure_Throws()
    {
        var validator = new KeycloakMachineTokenValidator(
            Options.Create(new KeycloakOptions { ServerUrl = "https://keycloak.example", Realm = "storyteller" }),
            new StaticConfigurationManager(null));

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => validator.ValidateAsync(Token(MachineClaims())));
    }

    [Theory]
    [InlineData("https://keycloak.example/realms/storyteller", true)]
    [InlineData("https://keycloak.example/realms/storyteller/", true)]
    [InlineData("https://keycloak.example/realms/storyteller2", false)]
    [InlineData("https://login.microsoftonline.com/tenant/v2.0", false)]
    public void CanValidate_OnlyTheRealmIssuer(string issuer, bool expected)
    {
        _validator.CanValidate(issuer).ShouldBe(expected);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    // A service-account token of the client machine, which the validator accepts unless the test breaks something else.
    private static Dictionary<string, object> MachineClaims()
    {
        return new Dictionary<string, object> { ["azp"] = "machine", ["client_id"] = "machine" };
    }

    private static string[] Values(BearerValidationResult result, string type)
    {
        return result.Claims.Where(claim => claim.Type == type).Select(claim => claim.Value).ToArray();
    }

    private string Token(Dictionary<string, object> claims, string issuer = Issuer, string audience = "storyteller-api", SecurityKey? key = null)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1),
            NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }

    private sealed class StaticConfigurationManager(OpenIdConnectConfiguration? configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public int RefreshRequests { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
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
