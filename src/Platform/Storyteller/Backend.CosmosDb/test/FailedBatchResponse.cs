using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The response of a batch that the test failed before it ran: like the service, the first operation carries the
// status and the others 424 Failed Dependency.
public sealed class FailedBatchResponse : TransactionalBatchResponse
{
    private readonly HttpStatusCode _statusCode;
    private readonly List<TransactionalBatchOperationResult> _results;

    public FailedBatchResponse(HttpStatusCode statusCode, int count)
    {
        _statusCode = statusCode;
        _results = Enumerable.Range(0, count)
            .Select(index => (TransactionalBatchOperationResult)new FailedResult(index == 0 ? statusCode : HttpStatusCode.FailedDependency))
            .ToList();
    }

    public override HttpStatusCode StatusCode => _statusCode;

    public override bool IsSuccessStatusCode => false;

    public override string ErrorMessage => "Failed by the test.";

    public override string ActivityId => "test";

    public override int Count => _results.Count;

    public override TransactionalBatchOperationResult this[int index] => _results[index];

    public override IEnumerator<TransactionalBatchOperationResult> GetEnumerator()
    {
        return _results.GetEnumerator();
    }

    public override TransactionalBatchOperationResult<T> GetOperationResultAtIndex<T>(int index)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        // nothing to release
    }

    private sealed class FailedResult(HttpStatusCode statusCode) : TransactionalBatchOperationResult
    {
        public override HttpStatusCode StatusCode => statusCode;

        public override bool IsSuccessStatusCode => false;
    }
}
