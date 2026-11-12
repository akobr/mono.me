using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// Point reads and upserts in the organization's partition "{project}.meta", with the client's own serializer both ways.
public sealed class CosmosDataSetSeedRecordStore : IDataSetSeedRecordStore
{
    private readonly IContainerRepositoryProvider _repositoryProvider;

    public CosmosDataSetSeedRecordStore(IContainerRepositoryProvider repositoryProvider)
    {
        _repositoryProvider = repositoryProvider;
    }

    public async Task<DataSetSeedRecord?> GetAsync(DataSetTarget target, string set, CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(target.Organization);

        try
        {
            var response = await repository.Container.ReadItemAsync<DataSetSeedRecord>(
                DataSetSeedRecord.GetId(target.View, set),
                repository.Key(DataSetSeedRecord.GetPartitionKey(target.Project)),
                cancellationToken: cancellationToken);
            return response.Resource;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task SaveAsync(DataSetSeedRecord record, CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(record.Organization);
        await repository.Container.UpsertItemAsync(record, repository.Key(record.PartitionKey), cancellationToken: cancellationToken);
    }
}
