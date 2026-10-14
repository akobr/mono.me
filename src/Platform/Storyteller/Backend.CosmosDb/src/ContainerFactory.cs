using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Utils.Async;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class ContainerFactory : IContainerFactory
{
    // Organization containers: TTL on without a default expiry (only items with a ttl expire: versions, and the
    // items of soft-deleted annotations), and the audit entries left out of the index.
    public const string AuditEntriesPath = "/Entries/*";

    private readonly ICosmosClientProvider _cosmosClientProvider;
    private readonly CosmosDbOptions _options;
    private readonly AsyncLazy<Database> _database;

    public ContainerFactory(ICosmosClientProvider cosmosClientProvider, IOptions<CosmosDbOptions> options)
    {
        _cosmosClientProvider = cosmosClientProvider;
        _options = options.Value;
        _database = new AsyncLazy<Database>(BuildDatabase);
    }

    public async Task<Container> CreateContainerIfNotExistsAsync(string containerName)
    {
        var database = await _database;
        var properties = new ContainerProperties(containerName, $"/{nameof(Entity.PartitionKey)}");
        var isOrganization = containerName.StartsWith(CosmosConstants.OrganizationContainerNamePrefix, StringComparison.Ordinal);

        if (isOrganization)
        {
            ApplySettings(properties);
        }

        var containerResult = await database.CreateContainerIfNotExistsAsync(properties);

        if (isOrganization)
        {
            await EnsureContainerSettingsAsync(containerResult.Container);
        }

        await containerResult.Container.UpsertStoredProcedureAsync(new StoredProcedureProperties
        {
            Id = CosmosStoredProcedureNames.RemoveValueFromArray,
            Body = """
                   function removeValueFromArray(itemId, propertyName, valueToRemove) {
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

        await containerResult.Container.UpsertStoredProcedureAsync(new StoredProcedureProperties
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

    // Containers created before the settings existed get them on the next start of DbCreator.
    public async Task EnsureContainerSettingsAsync(Container container)
    {
        var response = await container.ReadContainerAsync();
        var properties = response.Resource;

        if (ApplySettings(properties))
        {
            await container.ReplaceContainerAsync(properties);
        }
    }

    public async Task<IReadOnlyList<string>> EnsureOrganizationContainersAsync()
    {
        var database = await _database;
        var updated = new List<string>();
        using var iterator = database.GetContainerQueryIterator<ContainerProperties>();

        while (iterator.HasMoreResults)
        {
            foreach (var properties in await iterator.ReadNextAsync())
            {
                if (!properties.Id.StartsWith(CosmosConstants.OrganizationContainerNamePrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                await EnsureContainerSettingsAsync(database.GetContainer(properties.Id));
                updated.Add(properties.Id);
            }
        }

        return updated;
    }

    public async Task<IReadOnlyList<string>> GetContainerNamesAsync()
    {
        var database = await _database;
        var names = new List<string>();
        using var iterator = database.GetContainerQueryIterator<ContainerProperties>();

        while (iterator.HasMoreResults)
        {
            names.AddRange((await iterator.ReadNextAsync()).Select(properties => properties.Id));
        }

        return names;
    }

    // Returns whether anything changed.
    private static bool ApplySettings(ContainerProperties properties)
    {
        var changed = false;

        // null: TTL off, so the ttl of versions and deleted items is ignored; positive: every item without its own ttl
        // expires, which active records never may. -1 keeps TTL on with no default expiry.
        if (properties.DefaultTimeToLive is null or > 0)
        {
            properties.DefaultTimeToLive = -1;
            changed = true;
        }

        var policy = properties.IndexingPolicy;

        if (policy.IncludedPaths.Count == 0)
        {
            policy.IncludedPaths.Add(new IncludedPath { Path = "/*" });
            changed = true;
        }

        if (!policy.ExcludedPaths.Any(path => string.Equals(path.Path, AuditEntriesPath, StringComparison.Ordinal)))
        {
            policy.ExcludedPaths.Add(new ExcludedPath { Path = AuditEntriesPath });
            changed = true;
        }

        return changed;
    }

    private async Task<Database> BuildDatabase()
    {
        ThroughputProperties? throughput = _options.ManualThroughput.HasValue
            ? ThroughputProperties.CreateManualThroughput(_options.ManualThroughput.Value)
            : _options.AutoscaleMaxThroughput.HasValue
                ? ThroughputProperties.CreateAutoscaleThroughput(_options.AutoscaleMaxThroughput.Value)
                : null;

        var databaseResponse = await _cosmosClientProvider.Client.CreateDatabaseIfNotExistsAsync(
            "42.Platform.2S",
            throughput);
        return databaseResponse.Database;
    }
}
