#nullable enable

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Both stores together: the catalog in the Cosmos emulator, the key-encryption key in Lowkey Vault (Docker). The vault
// is a class fixture, started once for the tests of the class: started for each test, the second test met a vault that
// was still restarting (424-310).
public class KeyValueEndToEndTests(Startup startup, KeyValueEndToEndVault vault)
    : BaseTestsClass(startup), IClassFixture<KeyValueEndToEndVault>
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private readonly Uri _vaultUri = vault.VaultUri;

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
        using var response = await container.ReadItemStreamAsync("kv.db.password", PartitionKeys.GetCosmosKeyValues(Org, project, View));
        using var reader = new StreamReader(response.Content, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();

        raw.Should().NotContain("hunter");
        raw.Should().Contain($"{_vaultUri.Host}");
        // parsed, not matched as text: the vNext emulator returns the stored document formatted
        System.Text.Json.Nodes.JsonNode.Parse(raw)!["Content"]!["Encrypted"]!["Algorithm"]!.GetValue<string>().Should().Be("RSA-OAEP-256");
    }

    // 424-167: the first Standard secret records the vault's key in the project's policy.
    [Fact]
    public async Task FirstStandardSecret_RecordsTheVaultKey()
    {
        var services = Context.Services;
        var options = Options.Create(new KeyValueOptions { Protection = new KeyValueProtectionOptions { Emulator = true } });
        var provider = KeyVaultKeyProvider.CreateStandard(KeyVaultClients.CreateKeyClient(_vaultUri, emulator: true, isProduction: false), options);
        var policies = services.GetRequiredService<IKeyValuePolicyStore>();
        var repositories = services.GetRequiredService<IContainerRepositoryProvider>();
        using var protector = new EnvelopeValueProtector([provider], policies, options);
        var maintenance = new CosmosKeyValueMaintenanceService(policies, [provider], repositories, options);
        var keyValues = new CosmosKeyValueService(
            repositories,
            protector,
            policies,
            options,
            services.GetRequiredService<IJsonSerializationSettingsProvider>(),
            services.GetRequiredService<IOptions<JsonSerializerSettings>>(),
            maintenance: maintenance);

        var project = await Context.CreateProjectWithAccessPointAsync("e2e");
        var admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));
        (await policies.GetAsync(Org, project)).Standard.Should().BeNull();

        await keyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "hunter2" }, admin);

        var policy = await policies.GetAsync(Org, project);
        var keyName = KeyVaultKeyProvider.GetKeyName(options.Value.Protection.Standard.KeyNamePrefix, new KeyValueScope(Org, project));
        policy.Standard.Should().NotBeNull();
        policy.Standard!.KeyId.Should().EndWith($"/keys/{keyName}");
        policy.Standard.CurrentKeyVersion.Should().NotBeNullOrEmpty();
        policy.Standard.NextRotationAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(300));
        policy.Version.Should().Be(1);

        var container = repositories.GetOrganizationContainer(Org).Container;
        using var response = await container.ReadItemStreamAsync("kv.db.password", PartitionKeys.GetCosmosKeyValues(Org, project, View));
        using var reader = new StreamReader(response.Content, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync();
        raw.Should().Contain($"/keys/{keyName}/{policy.Standard.CurrentKeyVersion}\"", "the envelope was wrapped with the recorded version");
    }
}
