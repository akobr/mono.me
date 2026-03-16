using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Container = Microsoft.Azure.Cosmos.Container;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller;

public static class ContainerExtensions
{
    public static Task<bool> ExistsAsync(this Container @this, FullKey key)
    {
        return ExistsAsync(@this, key.GetCosmosItemId(), key.GetCosmosPartitionKey());
    }

    public static async Task<bool> ExistsAsync(this Container @this, string id, PartitionKey partitionKey)
    {
        try
        {
            using var response = await @this.ReadItemStreamAsync(id, partitionKey);
            return response.StatusCode == HttpStatusCode.OK;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public static async Task<TItem?> TryReadItemAsync<TItem>(
        this Container @this,
        string id,
        PartitionKey partitionKey,
        Func<Stream, TItem?> serialization,
        ItemRequestOptions? requestOptions = null,
        CancellationToken cancellationToken = default)
        where TItem : class
    {
        using var response = await @this.ReadItemStreamAsync(id, partitionKey, requestOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        var item = serialization(response.Content);
        return item;
    }

    public static async Task UpsertStoredProcedureAsync(this Container @this, StoredProcedureProperties props)
    {
        try
        {
            await @this.Scripts.CreateStoredProcedureAsync(props);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            await @this.Scripts.ReplaceStoredProcedureAsync(props);
        }
    }

    public static async Task RemoveArrayValueAsync(this Container @this, string id, string propertyName, string valueToRemove, PartitionKey partitionKey)
    {
        var @params = new dynamic[] { id, propertyName, valueToRemove };
        await @this.Scripts.ExecuteStoredProcedureAsync<string>(
            CosmosStoredProcedureNames.RemoveValueFromArray,
            partitionKey,
            @params);
    }

    public static async Task DeleteUsageAndExecutionsAsync(this Container @this, string usageId, string executionIdPrefix, string unitOfExecutionIdPrefix, PartitionKey partitionKey)
    {
        var @params = new dynamic[] { usageId, executionIdPrefix, unitOfExecutionIdPrefix };
        var finished = false;

        while (!finished)
        {
            var response = await @this.Scripts.ExecuteStoredProcedureAsync<DeleteResponse>(
                CosmosStoredProcedureNames.DeleteUsageAndExecutions,
                partitionKey,
                @params);

            finished = response.Resource.Finished;

            // Optional: Add a small delay if needed, though usually not necessary for SP continuations
            // if (!finished) await Task.Delay(10);
        }
    }

    private class DeleteResponse
    {
        public bool Finished { get; set; }

        public int Count { get; set; }
    }
}
