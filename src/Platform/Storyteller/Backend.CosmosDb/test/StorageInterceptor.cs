using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The hooks and the record of an InterceptedRepositoryProvider. The hooks run on the calling thread of the service,
// invalidation batches run in parallel, so a hook has to be safe to call concurrently.
public sealed class StorageInterceptor
{
    private readonly ConcurrentQueue<RecordedBatch> _batches = new();
    private readonly ConcurrentQueue<string> _pointReads = new();
    private readonly ConcurrentQueue<IReadOnlyList<string>> _readsOfMany = new();
    private int _sequence;

    // Runs before a batch executes. A returned status fails the batch with it, without executing it.
    public Func<RecordedBatch, Task<HttpStatusCode?>> BeforeBatch { get; set; }

    // Runs after a batch executed or was failed.
    public Func<RecordedBatch, Task> AfterBatch { get; set; }

    // Runs after a point read through ReadItemStreamAsync, with the id of the item.
    public Func<string, Task> AfterRead { get; set; }

    // Runs before a read of many items through ReadManyItemsStreamAsync, with the ids of the items. A returned response
    // answers the read instead of the service, without running it.
    public Func<IReadOnlyList<string>, Task<ResponseMessage>> BeforeReadMany { get; set; }

    // The batches in the order they finished.
    public IReadOnlyList<RecordedBatch> Batches => _batches.ToArray();

    // The ids of the point reads through ReadItemStreamAsync, in the order they finished.
    public IReadOnlyList<string> PointReads => _pointReads.ToArray();

    // The ids of each read of many items through ReadManyItemsStreamAsync, one list per request in the order they
    // started, also of the reads BeforeReadMany answered.
    public IReadOnlyList<IReadOnlyList<string>> ReadsOfMany => _readsOfMany.ToArray();

    public void Reset()
    {
        BeforeBatch = null;
        AfterBatch = null;
        AfterRead = null;
        BeforeReadMany = null;
        _batches.Clear();
        _pointReads.Clear();
        _readsOfMany.Clear();
    }

    internal async Task<TransactionalBatchResponse> ExecuteAsync(RecordedBatch batch, Func<Task<TransactionalBatchResponse>> execute)
    {
        var failure = BeforeBatch is null ? null : await BeforeBatch(batch);
        batch.Start = Interlocked.Increment(ref _sequence);

        var response = failure is { } statusCode
            ? new FailedBatchResponse(statusCode, batch.Operations.Count, batch.RetryAfter)
            : await execute();

        batch.StatusCode = response.StatusCode;
        batch.End = Interlocked.Increment(ref _sequence);
        _batches.Enqueue(batch);

        if (AfterBatch is not null)
        {
            await AfterBatch(batch);
        }

        return response;
    }

    internal Task OnReadAsync(string id)
    {
        _pointReads.Enqueue(id);
        return AfterRead?.Invoke(id) ?? Task.CompletedTask;
    }

    internal async Task<ResponseMessage> ReadManyAsync(IReadOnlyList<string> ids, Func<Task<ResponseMessage>> read)
    {
        _readsOfMany.Enqueue(ids);
        var answer = BeforeReadMany is null ? null : await BeforeReadMany(ids);
        return answer ?? await read();
    }
}
