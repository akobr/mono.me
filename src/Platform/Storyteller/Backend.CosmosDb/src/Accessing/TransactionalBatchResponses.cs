using System.Net;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Accessing;

internal static class TransactionalBatchResponses
{
    // A failed batch as a CosmosException with the status of its failed operation (the others answer 424), so the API
    // answers it as a failed single write.
    public static void ThrowIfFailed(this TransactionalBatchResponse response, string what)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new CosmosException(
                $"{what} failed with {(int)response.StatusCode}: {response.ErrorMessage}",
                response.StatusCode,
                0,
                response.ActivityId,
                response.RequestCharge);
        }
    }

    public static bool Failed(this TransactionalBatchResponse response, HttpStatusCode statusCode)
    {
        return !response.IsSuccessStatusCode && response.StatusCode == statusCode;
    }
}
