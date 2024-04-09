using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Utils.Async;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class ContainerFactory : IContainerFactory
{
    private readonly ICosmosClientProvider _cosmosClientProvider;
    private readonly AsyncLazy<Database> _database;

    public ContainerFactory(ICosmosClientProvider cosmosClientProvider)
    {
        _cosmosClientProvider = cosmosClientProvider;
        _database = new AsyncLazy<Database>(BuildDatabase);
    }

    public async Task<Container> CreateContainerIfNotExistsAsync(string containerName)
    {
        var database = await _database;

        var containerResult = await database.CreateContainerIfNotExistsAsync(
            id: containerName,
            partitionKeyPath: $"/{nameof(Entity.PartitionKey)}");

        return containerResult.Container;
    }

    private async Task<Database> BuildDatabase()
    {
        var databaseResponse = await _cosmosClientProvider.Client.CreateDatabaseIfNotExistsAsync(
            "42.Platform.2S",
            ThroughputProperties.CreateAutoscaleThroughput(1000));
        return databaseResponse.Database;
    }
}
