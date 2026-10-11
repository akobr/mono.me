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
// 100 operations. A failed batch stops the plan: the state stays valid because the waves run from the deepest
// descendants to the root (deletes) or from the roots to the descendants (creates and restores).
internal sealed class BatchPlan : IDisposable
{
    private const int MaxOperationsPerBatch = 100;
    private const int MaxParallelBatches = 8;

    private readonly Container _container;
    private readonly List<Unit> _units = [];
    private readonly List<IDisposable> _resources = [];

    public BatchPlan(Container container)
    {
        _container = container;
    }

    public bool IsEmpty => _units.All(unit => unit.Operations.Count == 0);

    public void Add(int wave, string partitionKey, params Action<TransactionalBatch>[] operations)
    {
        _units.Add(new Unit(wave, partitionKey, operations.ToList()));
    }

    public void Add(int wave, string partitionKey, IEnumerable<Action<TransactionalBatch>> operations)
    {
        _units.Add(new Unit(wave, partitionKey, operations.ToList()));
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

                foreach (var unit in partition)
                {
                    if (current.Count > 0 && current.Count + unit.Operations.Count > MaxOperationsPerBatch)
                    {
                        batches.Add((partition.Key, current));
                        current = [];
                    }

                    current.AddRange(unit.Operations);
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

    private sealed record Unit(int Wave, string PartitionKey, List<Action<TransactionalBatch>> Operations);
}
