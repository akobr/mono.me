using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.AzureAd.UnitTests;

public class EntraIdClaimNormalizerTests
{
    private const string McpClientId = "6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11";
    private const string McpTenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";

    private readonly EntraIdClaimNormalizer _normalizer = new(Options.Create(new UserAuthenticationOptions
    {
        Provider = IdentityProviderKind.EntraId,
        TenantId = "tenant-id",
        ClientId = "client-id",
        DelegatedClients = [new DelegatedClientOptions { ClientId = McpClientId, TenantId = McpTenantId }],
    }));

    [Fact]
    public void Normalize_AzpMatchingTheApiClient_IsAUserWithTheSameClaims()
    {
        List<Claim> claims = [new("sub", "user-1"), new("azp", "CLIENT-ID")];

        var result = _normalizer.Normalize(claims);

        result.Claims.ShouldBeSameAs(claims);
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    [Fact]
    public void Normalize_OtherAzp_IsAMachine()
    {
        var result = _normalizer.Normalize([new Claim("sub", "machine-app"), new Claim("azp", "machine-app")]);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe("machine-app");
    }

    [Fact]
    public void Normalize_DelegatedTokenOfListedClient_IsAUser()
    {
        var result = _normalizer.Normalize(
            [new Claim("sub", "user-1"), new Claim("azp", McpClientId), new Claim("tid", McpTenantId), new Claim("scp", "User.Impersonation")]);

        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    [Theory]
    [InlineData("{6c0f1a2e-1f43-4b0e-9a4d-2f5e8f0c9b11}", "{8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d}")]
    [InlineData("6c0f1a2e1f434b0e9a4d2f5e8f0c9b11", "8A7B6C5D4E3F4A1B8C9D0E1F2A3B4C5D")]
    public void Normalize_DelegatedTokenOfAClientListedInAnotherGuidForm_IsAUser(string clientId, string tenantId)
    {
        var normalizer = new EntraIdClaimNormalizer(Options.Create(new UserAuthenticationOptions
        {
            Provider = IdentityProviderKind.EntraId,
            TenantId = "tenant-id",
            ClientId = "client-id",
            DelegatedClients = [new DelegatedClientOptions { ClientId = clientId, TenantId = tenantId }],
        }));

        var result = normalizer.Normalize(
            [new Claim("sub", "user-1"), new Claim("azp", McpClientId), new Claim("tid", McpTenantId), new Claim("scp", "User.Impersonation")]);

        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
    }

    [Fact]
    public void Normalize_AppOnlyTokenOfListedClient_IsAMachine()
    {
        var result = _normalizer.Normalize(
            [new Claim("sub", McpClientId), new Claim("azp", McpClientId), new Claim("tid", McpTenantId), new Claim("roles", "Configuration.Read")]);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe(McpClientId);
    }

    [Fact]
    public void Normalize_BlankScopeOfListedClient_IsAMachine()
    {
        var result = _normalizer.Normalize(
            [new Claim("sub", "user-1"), new Claim("azp", McpClientId), new Claim("tid", McpTenantId), new Claim("scp", " ")]);

        result.IsMachine.ShouldBeTrue();
    }

    [Fact]
    public void Normalize_ListedClientFromAnotherTenant_IsAMachine()
    {
        var result = _normalizer.Normalize(
            [new Claim("sub", "user-1"), new Claim("azp", McpClientId), new Claim("scp", "User.Impersonation")]);

        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe(McpClientId);
    }
}
