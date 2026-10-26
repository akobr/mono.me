using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Records the operations of a transactional batch and hands the execution to the StorageInterceptor.
public sealed class InterceptedBatch(TransactionalBatch inner, PartitionKey partitionKey, StorageInterceptor interceptor) : TransactionalBatch
{
    private readonly RecordedBatch _record = new(partitionKey);

    public override TransactionalBatch CreateItem<T>(T item, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Create, IdOf(item), requestOptions);
        inner.CreateItem(item, requestOptions);
        return this;
    }

    public override TransactionalBatch CreateItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Create, null, requestOptions);
        inner.CreateItemStream(streamPayload, requestOptions);
        return this;
    }

    public override TransactionalBatch ReadItem(string id, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Read, id, requestOptions);
        inner.ReadItem(id, requestOptions);
        return this;
    }

    public override TransactionalBatch UpsertItem<T>(T item, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Upsert, IdOf(item), requestOptions);
        inner.UpsertItem(item, requestOptions);
        return this;
    }

    public override TransactionalBatch UpsertItemStream(Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Upsert, null, requestOptions);
        inner.UpsertItemStream(streamPayload, requestOptions);
        return this;
    }

    public override TransactionalBatch ReplaceItem<T>(string id, T item, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Replace, id, requestOptions);
        inner.ReplaceItem(id, item, requestOptions);
        return this;
    }

    public override TransactionalBatch ReplaceItemStream(string id, Stream streamPayload, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Replace, id, requestOptions);
        inner.ReplaceItemStream(id, streamPayload, requestOptions);
        return this;
    }

    public override TransactionalBatch DeleteItem(string id, TransactionalBatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Delete, id, requestOptions);
        inner.DeleteItem(id, requestOptions);
        return this;
    }

    public override TransactionalBatch PatchItem(string id, IReadOnlyList<PatchOperation> patchOperations, TransactionalBatchPatchItemRequestOptions requestOptions = null)
    {
        _record.Add(BatchOperationKind.Patch, id, requestOptions);
        inner.PatchItem(id, patchOperations, requestOptions);
        return this;
    }

    public override Task<TransactionalBatchResponse> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(null, cancellationToken);
    }

    public override Task<TransactionalBatchResponse> ExecuteAsync(TransactionalBatchRequestOptions requestOptions, CancellationToken cancellationToken = default)
    {
        return interceptor.ExecuteAsync(_record, () => inner.ExecuteAsync(requestOptions, cancellationToken));
    }

    private static string IdOf<T>(T item)
    {
        return item switch
        {
            Entity entity => entity.Id,
            JObject json => json.Value<string>("id"),
            _ => null,
        };
    }
}
