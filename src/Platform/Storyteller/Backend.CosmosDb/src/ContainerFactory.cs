using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Utils.Async;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

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

        await containerResult.Container.Scripts.CreateStoredProcedureAsync(new StoredProcedureProperties
        {
            Id = CosmosStoredProcedureNames.RemoveValueFromArray,
            Body = """
                   function removeValueFromArray(itemId, partitionKey, propertyName, valueToRemove) {
                       var context = getContext();
                       var collection = context.getCollection();

                       var query = {
                           query: "SELECT * FROM c WHERE c.id = @id",
                           parameters: [{ name: "@id", value: itemId }]
                       };

                       var isAccepted = collection.queryDocuments(collection.getSelfLink(), query, function (err, items, responseOptions) {
                           if (err) throw new Error("Error: " + err.message);

                           if (items.length != 1) throw new Error("Item not found");

                           var item = items[0];
                           var array = item[propertyName];

                           if (!Array.isArray(array)) throw new Error("Property is not an array");

                           var index = array.indexOf(valueToRemove);

                           if (index > -1) {
                               array.splice(index, 1);

                               var isUpdated = collection.replaceDocument(item._self, item, function (err, updatedItem) {
                                   if (err) throw new Error("Error: " + err.message);
                                   context.getResponse().setBody(updatedItem);
                               });

                               if (!isUpdated) throw new Error("Item update failed");
                           } else {
                               throw new Error("Value not found in array");
                           }
                       });

                       if (!isAccepted) throw new Error("Query not accepted by server");
                   }
                   """
        });
        await containerResult.Container.Scripts.CreateStoredProcedureAsync(new StoredProcedureProperties
        {
            Id = CosmosStoredProcedureNames.DeleteUsageAndExecutions,
            Body = """
                   function deleteUsageAndExecutions(usageId, executionIdPrefix, unitOfExecutionIdPrefix) {
                       var context = getContext();
                       var collection = context.getCollection();
                       var response = context.getResponse();

                       var query = {
                           query: "SELECT c._self FROM c WHERE c.id = @usageId OR STARTSWITH(c.id, @exePrefix) OR STARTSWITH(c.id, @uxePrefix)",
                           parameters: [
                               { name: "@usageId", value: usageId },
                               { name: "@exePrefix", value: executionIdPrefix },
                               { name: "@uxePrefix", value: unitOfExecutionIdPrefix }
                           ]
                       };

                       var isAccepted = collection.queryDocuments(collection.getSelfLink(), query, function (err, items, responseOptions) {
                           if (err) throw new Error("Error querying items: " + err.message);

                           if (items.length === 0) {
                               response.setBody({ finished: true, count: 0 });
                               return;
                           }

                           var count = 0;
                           function deleteNext() {
                               if (count >= items.length) {
                                   response.setBody({ finished: true, count: count });
                                   return;
                               }

                               var item = items[count];
                               var isAcceptedDelete = collection.deleteDocument(item._self, function (err) {
                                   if (err) throw new Error("Failed to delete item: " + err.message);
                                   count++;
                                   deleteNext();
                               });

                               if (!isAcceptedDelete) {
                                   // If deleteDocument returns false, we have reached the limit.
                                   response.setBody({ finished: false, count: count });
                               }
                           }

                           deleteNext();
                       });

                       if (!isAccepted) {
                           // Query wasn't even accepted, might be too many results or RU limit
                           response.setBody({ finished: false, count: 0 });
                       }
                   }
                   """
        });

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
