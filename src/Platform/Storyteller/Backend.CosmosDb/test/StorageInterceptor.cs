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
    private int _sequence;

    // Runs before a batch executes. A returned status fails the batch with it, without executing it.
    public Func<RecordedBatch, Task<HttpStatusCode?>> BeforeBatch { get; set; }

    // Runs after a batch executed or was failed.
    public Func<RecordedBatch, Task> AfterBatch { get; set; }

    // Runs after a point read through ReadItemStreamAsync, with the id of the item.
    public Func<string, Task> AfterRead { get; set; }

    // The batches in the order they finished.
    public IReadOnlyList<RecordedBatch> Batches => _batches.ToArray();

    public void Reset()
    {
        BeforeBatch = null;
        AfterBatch = null;
        AfterRead = null;
        _batches.Clear();
    }

    internal async Task<TransactionalBatchResponse> ExecuteAsync(RecordedBatch batch, Func<Task<TransactionalBatchResponse>> execute)
    {
        var failure = BeforeBatch is null ? null : await BeforeBatch(batch);
        batch.Start = Interlocked.Increment(ref _sequence);

        var response = failure is { } statusCode
            ? new FailedBatchResponse(statusCode, batch.Operations.Count)
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
        return AfterRead?.Invoke(id) ?? Task.CompletedTask;
    }
}
