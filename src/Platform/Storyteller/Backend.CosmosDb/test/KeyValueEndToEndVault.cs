#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Testcontainers.LowkeyVault;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The vault of KeyValueEndToEndTests, started once for the class: a running instance named by LOWKEY_VAULT_URL, or Lowkey
// Vault in Docker with its own reuse label. Disposing it stops the reused container after the class.
public sealed class KeyValueEndToEndVault : IAsyncLifetime
{
    private LowkeyVaultContainer? _container;

    public Uri VaultUri { get; private set; } = new("https://localhost:8443/");

    public async ValueTask InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LOWKEY_VAULT_URL");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            VaultUri = new Uri(configured);
            return;
        }

        _container = LowkeyVaultTestContainer.Build("backend-cosmosdb-unittests");
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
