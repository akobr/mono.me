using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosMergedApiKeyHashStore : IApiKeyHashStore
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosMergedApiKeyHashStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task StoreAsync(string organization, string project, string machineAccessId,
                                  string hashedSecret, MachineAccessScope scope)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");

        var existing = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (existing is not null)
        {
            var updated = existing with { HashedSecret = hashedSecret, Scope = scope };
            await repository.Container.UpsertItemAsync(updated, partitionKey);
        }
    }

    public async Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");

        var machineAccess = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess?.HashedSecret is not null)
        {
            return new ApiKeyHashEntry(machineAccess.HashedSecret, machineAccess.Scope);
        }

        return null;
    }

    public async Task<bool> DeleteAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");

        var machineAccess = await repository.Container.TryReadItemAsync(
            machineAccessId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<MachineAccessEntity>(_serializerOptions));

        if (machineAccess?.HashedSecret is not null)
        {
            var updated = machineAccess with { HashedSecret = null };
            await repository.Container.UpsertItemAsync(updated, partitionKey);
        }

        return true;
    }
}
