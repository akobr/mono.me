using System;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Annotating;

// An operation of a batch, with what runs with its result once the batch has committed, for example to keep the ETag
// the operation wrote.
internal sealed record BatchOperation(Action<TransactionalBatch> Apply, Action<TransactionalBatchOperationResult>? Committed = null)
{
    public static BatchOperation KeepingETag(Action<TransactionalBatch> apply, Action<string?> keep)
    {
        return new BatchOperation(apply, result => keep(result.ETag));
    }
}
