using Azure.Security.KeyVault.Keys;
using Testcontainers.LowkeyVault;
using Xunit;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests;

// Lowkey Vault in Docker, or a running instance named by LOWKEY_VAULT_URL.
public sealed class LowkeyVaultFixture : IAsyncLifetime
{
    public const string Image = "nagyesta/lowkey-vault:7.3.112";

    private LowkeyVaultContainer? _container;

    public Uri VaultUri { get; private set; } = new("https://localhost:8443/");

    public KeyClient CreateClient()
    {
        return KeyVaultClients.CreateKeyClient(VaultUri, emulator: true, isProduction: false);
    }

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LOWKEY_VAULT_URL");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            VaultUri = new Uri(configured);
            return;
        }

        _container = new LowkeyVaultBuilder(Image)
            .WithReuse(true)
            .Build();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _container.StartAsync(cancellation.Token);
        VaultUri = new Uri(_container.GetBaseAddress());
    }

    public async Task DisposeAsync()
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
