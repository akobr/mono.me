using System;
using _42.Platform.Storyteller.KeyValues;
using Azure.Identity;
using FluentAssertions;
using Xunit;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.UnitTests;

public class KeyVaultClientsTests
{
    private const string ClientId = "3f2b8c1e-4d5a-4b6c-9e7f-0a1b2c3d4e5f";

    [Fact]
    public void GetManagedIdentityId_WithClientId_IsTheUserAssignedIdentity()
    {
        var id = KeyVaultClients.GetManagedIdentityId(ClientId);

        id.ToString().Should().Be(ManagedIdentityId.FromUserAssignedClientId(ClientId).ToString());
        id.ToString().Should().NotBe(ManagedIdentityId.SystemAssigned.ToString());
    }

    [Fact]
    public void GetManagedIdentityId_TrimsTheClientId()
    {
        var id = KeyVaultClients.GetManagedIdentityId($" {ClientId}\n");

        id.ToString().Should().Be(ManagedIdentityId.FromUserAssignedClientId(ClientId).ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetManagedIdentityId_WithoutClientId_IsTheSystemAssignedIdentity(string? clientId)
    {
        KeyVaultClients.GetManagedIdentityId(clientId).ToString().Should().Be(ManagedIdentityId.SystemAssigned.ToString());
    }

    [Fact]
    public void CreateCredential_InProduction_ChainsTheManagedIdentity()
    {
        KeyVaultClients.CreateCredential(isProduction: true, ClientId).Should().BeOfType<ChainedTokenCredential>();
    }

    [Fact]
    public void CreateCredential_OutsideProduction_IsTheDefaultCredential()
    {
        KeyVaultClients.CreateCredential(isProduction: false, ClientId).Should().BeOfType<DefaultAzureCredential>();
    }

    [Fact]
    public void CreateKeyClient_TheEmulatorInProduction_IsRefused()
    {
        var create = () => KeyVaultClients.CreateKeyClient(new Uri("https://localhost:8443"), emulator: true, isProduction: true, ClientId);

        create.Should().Throw<InvalidOperationException>();
    }
}
