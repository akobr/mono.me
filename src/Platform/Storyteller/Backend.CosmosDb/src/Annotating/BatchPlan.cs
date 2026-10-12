using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Annotating;

// Writes grouped into transactional batches. A unit is the operations of one annotation and its items, always in
// one partition; units run in waves (ascending), and within a wave every partition gets its own batches of at most
// 100 operations and about 1.5 MB of payload (Cosmos rejects a batch above 2 MB). A failed batch stops the plan: the state stays valid because the waves run from the deepest
// descendants to the root (deletes) or from the roots to the descendants (creates and restores).
internal sealed class BatchPlan : IDisposable
{
    private const int MaxOperationsPerBatch = 100;

    // below the 2 MB request limit of a transactional batch, with room for the operation headers and estimates
    private const long MaxPayloadBytesPerBatch = 1_500_000;
    private const int MaxParallelBatches = 8;

    private readonly Container _container;
    private readonly List<Unit> _units = [];
    private readonly List<IDisposable> _resources = [];

    public BatchPlan(Container container)
    {
        _container = container;
    }

    public bool IsEmpty => _units.All(unit => unit.Operations.Count == 0);

    // payloadBytes: the estimated size of the documents the operations send
    public void Add(int wave, string partitionKey, long payloadBytes, params Action<TransactionalBatch>[] operations)
    {
        _units.Add(new Unit(wave, partitionKey, operations.ToList(), payloadBytes));
    }

    public void Add(int wave, string partitionKey, long payloadBytes, IEnumerable<Action<TransactionalBatch>> operations)
    {
        _units.Add(new Unit(wave, partitionKey, operations.ToList(), payloadBytes));
    }

    // A stream handed to a batch must stay open until the batch is executed.
    public MemoryStream Keep(MemoryStream stream)
    {
        _resources.Add(stream);
        return stream;
    }

    public async Task ExecuteAsync()
    {
        using var throttler = new SemaphoreSlim(MaxParallelBatches);

        foreach (var wave in _units.Where(unit => unit.Operations.Count > 0).GroupBy(unit => unit.Wave).OrderBy(group => group.Key))
        {
            var batches = new List<(string PartitionKey, List<Action<TransactionalBatch>> Operations)>();

            foreach (var partition in wave.GroupBy(unit => unit.PartitionKey, StringComparer.Ordinal))
            {
                var current = new List<Action<TransactionalBatch>>();
                var currentBytes = 0L;

                // a unit is never split: a unit above a limit gets a batch of its own
                foreach (var unit in partition)
                {
                    if (current.Count > 0
                        && (current.Count + unit.Operations.Count > MaxOperationsPerBatch
                            || currentBytes + unit.PayloadBytes > MaxPayloadBytesPerBatch))
                    {
                        batches.Add((partition.Key, current));
                        current = [];
                        currentBytes = 0;
                    }

                    current.AddRange(unit.Operations);
                    currentBytes += unit.PayloadBytes;
                }

                if (current.Count > 0)
                {
                    batches.Add((partition.Key, current));
                }
            }

            await Task.WhenAll(batches.Select(batch => ExecuteBatchAsync(batch.PartitionKey, batch.Operations, throttler)));
        }
    }

    public void Dispose()
    {
        foreach (var resource in _resources)
        {
            resource.Dispose();
        }

        _resources.Clear();
    }

    private async Task ExecuteBatchAsync(string partitionKey, List<Action<TransactionalBatch>> operations, SemaphoreSlim throttler)
    {
        await throttler.WaitAsync();

        try
        {
            var batch = _container.CreateTransactionalBatch(new PartitionKey(partitionKey));

            foreach (var operation in operations)
            {
                operation(batch);
            }

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (response.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            {
                throw new AnnotationConcurrencyException(
                    $"A concurrent change in partition '{partitionKey}' stopped the write ({(int)response.StatusCode}).");
            }

            throw new CosmosException(
                $"A transactional batch in partition '{partitionKey}' failed: {response.ErrorMessage}",
                response.StatusCode,
                0,
                response.ActivityId,
                response.RequestCharge);
        }
        finally
        {
            throttler.Release();
        }
    }

    private sealed record Unit(int Wave, string PartitionKey, List<Action<TransactionalBatch>> Operations, long PayloadBytes);
}
