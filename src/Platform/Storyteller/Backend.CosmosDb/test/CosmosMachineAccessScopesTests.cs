#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The scopes of a machine in Cosmos: stored by name on the machine document, read back the same by the point read,
// the list and the API key hash store; a document stored before the flat scopes reads as a machine without any.
public class CosmosMachineAccessScopesTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;

    private static readonly MachineScope[] Granted = [MachineScope.AnnotationRead, MachineScope.AnnotationWrite, MachineScope.SecretsRead, MachineScope.SchemaWrite];

    private Container Container => Context.Services
        .GetRequiredService<IContainerRepositoryProvider>()
        .GetOrganizationContainer(Org)
        .Container;

    [Fact]
    public async Task Create_StoresTheReturnedScopesByName_AndEveryReadReturnsThem()
    {
        var project = $"scopes-{Guid.NewGuid():N}"[..20];
        var hashStore = CreateHashStore();
        var access = CreateAccessService(new HashStoringMachineAccessService(hashStore));

        var created = await access.CreateMachineAccessAsync(new MachineAccessCreate { Organization = Org, Project = project, Scopes = Granted });

        created.Scopes.Should().Equal(Granted);
        (await access.GetMachineAccessAsync(Org, project, created.Id))!.Scopes.Should().Equal(Granted);
        (await access.GetMachineAccessesAsync(Org, project)).Single(machine => machine.Id == created.Id).Scopes.Should().Equal(Granted);
        (await hashStore.GetAsync(Org, project, created.Id))!.Scopes.Should().Equal(Granted);

        var stored = (await Container.ReadItemAsync<JObject>(created.Id, new PartitionKey($"{project}.access"))).Resource;
        stored["Scopes"]!.Values<string>().Should().Equal("AnnotationRead", "AnnotationWrite", "SecretsRead", "SchemaWrite");
        stored.ContainsKey("Scope").Should().BeFalse();
        stored.ContainsKey("ManagementScopes").Should().BeFalse();
    }

    [Fact]
    public async Task HashStore_OnAStoredMachine_WritesTheGivenScopes()
    {
        var project = $"scopes-{Guid.NewGuid():N}"[..20];
        var hashStore = CreateHashStore();
        var access = CreateAccessService(new HashStoringMachineAccessService(hashStore));
        var created = await access.CreateMachineAccessAsync(new MachineAccessCreate { Organization = Org, Project = project, Scopes = Granted });

        // as a reset does: a new hash with the scopes the machine already has
        await hashStore.StoreAsync(Org, project, created.Id, "hash-2", [MachineScope.ConfigurationRead]);

        var entry = await hashStore.GetAsync(Org, project, created.Id);
        entry!.HashedSecret.Should().Be("hash-2");
        entry.Scopes.Should().Equal(MachineScope.ConfigurationRead);
        (await access.GetMachineAccessAsync(Org, project, created.Id))!.Scopes.Should().Equal(MachineScope.ConfigurationRead);
    }

    [Fact]
    public async Task AnOlderDocumentWithoutScopes_ReadsAsAMachineWithoutScopes()
    {
        var project = $"scopes-{Guid.NewGuid():N}"[..20];
        var id = Guid.NewGuid().ToString("D");
        var partitionKey = new PartitionKey($"{project}.access");

        // a machine as stored before the flat scopes: a preset and management scopes, no Scopes
        await Container.UpsertItemAsync(
            new JObject
            {
                ["id"] = id,
                ["PartitionKey"] = $"{project}.access",
                ["ObjectId"] = id,
                ["AccessKey"] = "2s.***",
                ["Scope"] = "DefaultReadWrite",
                ["ManagementScopes"] = new JArray("SchemaWrite"),
                ["CredentialKind"] = "ApiKey",
                ["HashedSecret"] = "hash-1",
            },
            partitionKey);

        var access = CreateAccessService();
        (await access.GetMachineAccessAsync(Org, project, id))!.Scopes.Should().BeEmpty();
        (await access.GetMachineAccessesAsync(Org, project)).Single(machine => machine.Id == id).Scopes.Should().BeEmpty();

        var entry = await CreateHashStore().GetAsync(Org, project, id);
        entry!.HashedSecret.Should().Be("hash-1");
        entry.Scopes.Should().BeEmpty();
    }

    private CosmosMergedApiKeyHashStore CreateHashStore() => new(
        Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
        Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>());

    private CosmosAccessService CreateAccessService(IMachineAccessService? machineAccessService = null) => new(
        Context.Services.GetRequiredService<IContainerRepositoryProvider>(),
        Context.Services.GetRequiredService<IContainerFactory>(),
        Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(),
        machineAccessService);

    // Stores the hash first and returns exactly the requested scopes, as the API key service does.
    private sealed class HashStoringMachineAccessService(IApiKeyHashStore hashStore) : IMachineAccessService
    {
        public async Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
        {
            var id = Guid.NewGuid().ToString("D");
            var scopes = model.Scopes ?? [];
            await hashStore.StoreAsync(model.Organization, model.Project, id, "hash-1", scopes);

            return new MachineAccess
            {
                Id = id,
                ObjectId = id,
                AccessKey = "2s.raw-key",
                Scopes = scopes,
            };
        }

        public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project) => throw new NotSupportedException();

        public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project) => throw new NotSupportedException();
    }
}
