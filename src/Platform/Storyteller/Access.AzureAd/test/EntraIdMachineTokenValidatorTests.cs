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

public sealed class EntraIdMachineTokenValidatorTests : IDisposable
{
    private const string ClientId = "api-client";
    private const string Issuer = "https://login.microsoftonline.com/tenant-id/v2.0";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly EntraIdMachineTokenValidator _validator;

    public EntraIdMachineTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "entra-key" };
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(_key);

        _validator = new EntraIdMachineTokenValidator(new EntraIdBearerTokenValidator(
            Options.Create(new UserAuthenticationOptions { TenantId = "tenant-id", ClientId = ClientId }),
            new ProductionEnvironment(),
            new StaticConfigurationManager(configuration)));
    }

    [Fact]
    public async Task MachineToken_IsAccepted()
    {
        var result = await _validator.ValidateAsync(Token([new Claim("sub", "sp-1"), new Claim("azp", "machine-app"), new Claim("roles", "App.Default.Read")]));

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
    }

    [Fact]
    public async Task UserToken_IsNotAMachineResult()
    {
        var result = await _validator.ValidateAsync(Token([new Claim("sub", "user-1"), new Claim("azp", ClientId)]));

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TokenForAnotherApi_IsRejected()
    {
        var result = await _validator.ValidateAsync(Token([new Claim("azp", "machine-app")], audience: "api://someone-else"));

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/tenant-id/v2.0", true)]
    [InlineData("https://sts.windows.net/tenant-id/", true)]
    [InlineData("https://login.microsoftonline.com.evil/tenant-id/v2.0", false)]
    [InlineData("https://example.authkit.app", false)]
    public void CanValidate_MicrosoftIssuers(string issuer, bool expected)
    {
        _validator.CanValidate(issuer).ShouldBe(expected);
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private string Token(IEnumerable<Claim> claims, string audience = $"api://{ClientId}")
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = DateTime.UtcNow.AddMinutes(-5),
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        }));
    }

    private sealed class StaticConfigurationManager(OpenIdConnectConfiguration configuration) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel)
        {
            return Task.FromResult(configuration);
        }

        public void RequestRefresh()
        {
        }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
