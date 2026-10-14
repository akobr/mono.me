#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Testcontainers.LowkeyVault;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Both stores together: the catalog in the Cosmos emulator, the key-encryption key in Lowkey Vault (Docker).
public class KeyValueEndToEndTests(Startup startup)
    : BaseTestsClass(startup), IAsyncLifetime
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private LowkeyVaultContainer? _vault;
    private Uri _vaultUri = new("https://localhost:8443/");

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("LOWKEY_VAULT_URL");

        if (!string.IsNullOrWhiteSpace(configured))
        {
            _vaultUri = new Uri(configured);
            return;
        }

        _vault = new LowkeyVaultBuilder("nagyesta/lowkey-vault:7.3.112").WithReuse(true).Build();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _vault.StartAsync(cancellation.Token);
        _vaultUri = new Uri(_vault.GetBaseAddress());
    }

    public async Task DisposeAsync()
    {
        if (_vault is not null)
        {
            await _vault.DisposeAsync();
        }
    }

    [Fact]
    public async Task Secret_WrittenWithTheVaultKey_RoundTripsThroughCosmos()
    {
        var services = Context.Services;
        var options = Options.Create(new KeyValueOptions { Protection = new KeyValueProtectionOptions { Emulator = true } });
        var provider = KeyVaultKeyProvider.CreateStandard(KeyVaultClients.CreateKeyClient(_vaultUri, emulator: true, isProduction: false), options);
        var policies = services.GetRequiredService<IKeyValuePolicyStore>();
        using var protector = new EnvelopeValueProtector([provider], policies, options);
        var keyValues = new CosmosKeyValueService(
            services.GetRequiredService<IContainerRepositoryProvider>(),
            protector,
            policies,
            options,
            services.GetRequiredService<IJsonSerializationSettingsProvider>(),
            services.GetRequiredService<IOptions<JsonSerializerSettings>>());

        var project = $"kv-e2e-{Guid.NewGuid():N}"[..30];
        var admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));

        var written = await keyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "hunter2" }, admin);
        await keyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Value = "hunter3" }, admin);

        written.Version.Should().Be(1);
        (await keyValues.GetAsync(Org, project, View, "db.password", new KeyValueReadOptions { Reveal = true }, admin))!.Value.Should().Be("hunter3");
        (await keyValues.GetVersionAsync(Org, project, View, "db.password", 1, reveal: true, admin))!.Value.Should().Be("hunter2");

        var container = services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Org).Container;
        using var response = await container.ReadItemStreamAsync("kv.db.password", PartitionKeys.GetCosmosKeyValues(project, View));
        using var reader = new StreamReader(response.Content, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        raw.Should().NotContain("hunter");
        raw.Should().Contain($"{_vaultUri.Host}");
        raw.Should().Contain("\"Algorithm\":\"RSA-OAEP-256\"");
    }
}
