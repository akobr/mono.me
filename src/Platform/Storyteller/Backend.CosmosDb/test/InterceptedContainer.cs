using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// A container that forwards every call to a real one, and lets a StorageInterceptor record and fail transactional
// batches and run actions around them and after point reads. Every operation still runs against the emulator.
public sealed class InterceptedContainer(Container inner, StorageInterceptor interceptor) : Container
{
    public override string Id => inner.Id;

    public override Database Database => inner.Database;

    public override Conflicts Conflicts => inner.Conflicts;

    public override Scripts Scripts => inner.Scripts;

    public override TransactionalBatch CreateTransactionalBatch(PartitionKey partitionKey)
    {
        return new InterceptedBatch(inner.CreateTransactionalBatch(partitionKey), partitionKey, interceptor);
    }

    public override async Task<ResponseMessage> ReadItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
    {
        var response = await inner.ReadItemStreamAsync(id, partitionKey, requestOptions, cancellationToken);
        await interceptor.OnReadAsync(id);
        return response;
    }

    public override Task<ContainerResponse> ReadContainerAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReadContainerAsync(requestOptions, cancellationToken);

    public override Task<ResponseMessage> ReadContainerStreamAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReadContainerStreamAsync(requestOptions, cancellationToken);

    public override Task<ContainerResponse> ReplaceContainerAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceContainerAsync(containerProperties, requestOptions, cancellationToken);

    public override Task<ResponseMessage> ReplaceContainerStreamAsync(ContainerProperties containerProperties, ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceContainerStreamAsync(containerProperties, requestOptions, cancellationToken);

    public override Task<ContainerResponse> DeleteContainerAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.DeleteContainerAsync(requestOptions, cancellationToken);

    public override Task<ResponseMessage> DeleteContainerStreamAsync(ContainerRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.DeleteContainerStreamAsync(requestOptions, cancellationToken);

    public override Task<int?> ReadThroughputAsync(CancellationToken cancellationToken = default)
        => inner.ReadThroughputAsync(cancellationToken);

    public override Task<ThroughputResponse> ReadThroughputAsync(RequestOptions requestOptions, CancellationToken cancellationToken = default)
        => inner.ReadThroughputAsync(requestOptions, cancellationToken);

    public override Task<ThroughputResponse> ReplaceThroughputAsync(int throughput, RequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceThroughputAsync(throughput, requestOptions, cancellationToken);

    public override Task<ThroughputResponse> ReplaceThroughputAsync(ThroughputProperties throughputProperties, RequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceThroughputAsync(throughputProperties, requestOptions, cancellationToken);

    public override Task<ResponseMessage> CreateItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.CreateItemStreamAsync(streamPayload, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> CreateItemAsync<T>(T item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.CreateItemAsync(item, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> ReadItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReadItemAsync<T>(id, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> UpsertItemStreamAsync(Stream streamPayload, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.UpsertItemStreamAsync(streamPayload, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> UpsertItemAsync<T>(T item, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.UpsertItemAsync(item, partitionKey, requestOptions, cancellationToken);

    public override Task<ResponseMessage> ReplaceItemStreamAsync(Stream streamPayload, string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceItemStreamAsync(streamPayload, id, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> ReplaceItemAsync<T>(T item, string id, PartitionKey? partitionKey = null, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReplaceItemAsync(item, id, partitionKey, requestOptions, cancellationToken);

#pragma warning disable SA1316 // the tuple names are those of the overridden members
    public override Task<ResponseMessage> ReadManyItemsStreamAsync(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReadManyItemsStreamAsync(items, readManyRequestOptions, cancellationToken);

    public override Task<FeedResponse<T>> ReadManyItemsAsync<T>(IReadOnlyList<(string id, PartitionKey partitionKey)> items, ReadManyRequestOptions readManyRequestOptions = null, CancellationToken cancellationToken = default)
        => inner.ReadManyItemsAsync<T>(items, readManyRequestOptions, cancellationToken);
#pragma warning restore SA1316

    public override Task<ItemResponse<T>> PatchItemAsync<T>(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.PatchItemAsync<T>(id, partitionKey, patchOperations, requestOptions, cancellationToken);

    public override Task<ResponseMessage> PatchItemStreamAsync(string id, PartitionKey partitionKey, IReadOnlyList<PatchOperation> patchOperations, PatchItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.PatchItemStreamAsync(id, partitionKey, patchOperations, requestOptions, cancellationToken);

    public override Task<ResponseMessage> DeleteItemStreamAsync(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.DeleteItemStreamAsync(id, partitionKey, requestOptions, cancellationToken);

    public override Task<ItemResponse<T>> DeleteItemAsync<T>(string id, PartitionKey partitionKey, ItemRequestOptions requestOptions = null, CancellationToken cancellationToken = default)
        => inner.DeleteItemAsync<T>(id, partitionKey, requestOptions, cancellationToken);

    public override FeedIterator GetItemQueryStreamIterator(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryStreamIterator(queryDefinition, continuationToken, requestOptions);

    public override FeedIterator<T> GetItemQueryIterator<T>(QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryIterator<T>(queryDefinition, continuationToken, requestOptions);

    public override FeedIterator GetItemQueryStreamIterator(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryStreamIterator(queryText, continuationToken, requestOptions);

    public override FeedIterator<T> GetItemQueryIterator<T>(string queryText = null, string continuationToken = null, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryIterator<T>(queryText, continuationToken, requestOptions);

    public override FeedIterator GetItemQueryStreamIterator(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryStreamIterator(feedRange, queryDefinition, continuationToken, requestOptions);

    public override FeedIterator<T> GetItemQueryIterator<T>(FeedRange feedRange, QueryDefinition queryDefinition, string continuationToken = null, QueryRequestOptions requestOptions = null)
        => inner.GetItemQueryIterator<T>(feedRange, queryDefinition, continuationToken, requestOptions);

    // LINQ queries run on the inner container, so they are neither recorded nor failed
    public override IOrderedQueryable<T> GetItemLinqQueryable<T>(bool allowSynchronousQueryExecution = false, string continuationToken = null, QueryRequestOptions requestOptions = null, CosmosLinqSerializerOptions linqSerializerOptions = null)
        => inner.GetItemLinqQueryable<T>(allowSynchronousQueryExecution, continuationToken, requestOptions, linqSerializerOptions);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, ChangesHandler<T> onChangesDelegate)
        => inner.GetChangeFeedProcessorBuilder(processorName, onChangesDelegate);

    public override ChangeFeedProcessorBuilder GetChangeFeedEstimatorBuilder(string processorName, ChangesEstimationHandler estimationDelegate, TimeSpan? estimationPeriod = null)
        => inner.GetChangeFeedEstimatorBuilder(processorName, estimationDelegate, estimationPeriod);

    public override ChangeFeedEstimator GetChangeFeedEstimator(string processorName, Container leaseContainer)
        => inner.GetChangeFeedEstimator(processorName, leaseContainer);

    public override Task<IReadOnlyList<FeedRange>> GetFeedRangesAsync(CancellationToken cancellationToken = default)
        => inner.GetFeedRangesAsync(cancellationToken);

    public override FeedIterator GetChangeFeedStreamIterator(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null)
        => inner.GetChangeFeedStreamIterator(changeFeedStartFrom, changeFeedMode, changeFeedRequestOptions);

    public override FeedIterator<T> GetChangeFeedIterator<T>(ChangeFeedStartFrom changeFeedStartFrom, ChangeFeedMode changeFeedMode, ChangeFeedRequestOptions changeFeedRequestOptions = null)
        => inner.GetChangeFeedIterator<T>(changeFeedStartFrom, changeFeedMode, changeFeedRequestOptions);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder<T>(string processorName, ChangeFeedHandler<T> onChangesDelegate)
        => inner.GetChangeFeedProcessorBuilder(processorName, onChangesDelegate);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint<T>(string processorName, ChangeFeedHandlerWithManualCheckpoint<T> onChangesDelegate)
        => inner.GetChangeFeedProcessorBuilderWithManualCheckpoint(processorName, onChangesDelegate);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilder(string processorName, ChangeFeedStreamHandler onChangesDelegate)
        => inner.GetChangeFeedProcessorBuilder(processorName, onChangesDelegate);

    public override ChangeFeedProcessorBuilder GetChangeFeedProcessorBuilderWithManualCheckpoint(string processorName, ChangeFeedStreamHandlerWithManualCheckpoint onChangesDelegate)
        => inner.GetChangeFeedProcessorBuilderWithManualCheckpoint(processorName, onChangesDelegate);
}
