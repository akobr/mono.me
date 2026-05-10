using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosApiKeyHashStore : IApiKeyHashStore
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosApiKeyHashStore(
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
        var entity = new ApiKeyHashEntity
        {
            PartitionKey = $"{project}.access",
            Id = $"akh.{machineAccessId}",
            HashedSecret = hashedSecret,
            Scope = scope,
        };

        await repository.Container.UpsertItemAsync(entity, partitionKey);
    }

    public async Task<ApiKeyHashEntry?> GetAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entity = await repository.Container.TryReadItemAsync(
            $"akh.{machineAccessId}",
            new PartitionKey($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<ApiKeyHashEntity>(_serializerOptions));

        return entity is not null
            ? new ApiKeyHashEntry(entity.HashedSecret, entity.Scope)
            : null;
    }

    public async Task<bool> DeleteAsync(string organization, string project, string machineAccessId)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        try
        {
            await repository.Container.DeleteItemAsync<ApiKeyHashEntity>(
                $"akh.{machineAccessId}",
                new PartitionKey($"{project}.access"));
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
