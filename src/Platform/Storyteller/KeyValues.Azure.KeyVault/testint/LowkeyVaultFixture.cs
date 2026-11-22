using _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;
using Azure.Security.KeyVault.Keys;
using Testcontainers.LowkeyVault;
using Xunit;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests;

// Lowkey Vault in Docker (the pinned image and readiness check of LowkeyVaultTestContainer), or a running instance named
// by LOWKEY_VAULT_URL.
public sealed class LowkeyVaultFixture : IAsyncLifetime
{
    private LowkeyVaultContainer? _container;

    public Uri VaultUri { get; private set; } = new("https://localhost:8443/");

    public KeyClient CreateClient()
    {
        return KeyVaultClients.CreateKeyClient(VaultUri, emulator: true, isProduction: false);
    }

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LOWKEY_VAULT_URL");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            VaultUri = new Uri(configured);
            return;
        }

        // Its own label, so its own reused vault (Backend.CosmosDb's KeyValueEndToEndTests reuse another).
        _container = LowkeyVaultTestContainer.Build("keyvalues-azure-keyvault-integrationtests");

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _container.StartAsync(cancellation.Token);
        VaultUri = new Uri(_container.GetBaseAddress());
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

[CollectionDefinition("LowkeyVault")]
public sealed class LowkeyVaultCollection : ICollectionFixture<LowkeyVaultFixture>
{
}
