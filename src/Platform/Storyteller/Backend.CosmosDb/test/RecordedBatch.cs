using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// A transactional batch as the StorageInterceptor saw it. Start and End are numbers of one sequence shared by all
// batches of the interceptor, taken right before and right after the execution.
public sealed class RecordedBatch(PartitionKey partitionKey)
{
    private readonly List<RecordedOperation> _operations = [];

    public PartitionKey PartitionKey { get; } = partitionKey;

    public IReadOnlyList<RecordedOperation> Operations => _operations;

    public int Start { get; internal set; }

    public int End { get; internal set; }

    public HttpStatusCode StatusCode { get; internal set; }

    // the cache invalidation patches items and does nothing else
    public bool IsInvalidation => _operations.Count > 0 && _operations.All(operation => operation.Kind == BatchOperationKind.Patch);

    public bool Contains(BatchOperationKind kind, string id)
    {
        return _operations.Any(operation => operation.Kind == kind && operation.Id == id);
    }

    internal void Add(BatchOperationKind kind, string id, TransactionalBatchItemRequestOptions requestOptions)
    {
        _operations.Add(new RecordedOperation(kind, id, requestOptions?.IfMatchEtag));
    }
}
