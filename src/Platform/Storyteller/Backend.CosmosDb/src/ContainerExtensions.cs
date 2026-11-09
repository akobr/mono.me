using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
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

    public static async Task<(TItem? Item, string? ETag)> TryReadItemWithETagAsync<TItem>(
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
            return (default, null);
        }

        var item = serialization(response.Content);
        return (item, response.Headers.ETag);
    }
}
