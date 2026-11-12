using System;
using System.Globalization;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Annotating;

// A failed request of the store as a CosmosException, with the wait of a throttled one (429 left after the retries of
// the client), which an exception made by the public constructor of CosmosException does not carry. The bulk writer
// honours it before it runs again.
internal sealed class CosmosRequestFailedException(
    string message,
    HttpStatusCode statusCode,
    string activityId,
    double requestCharge,
    TimeSpan? retryAfter)
    : CosmosException(message, statusCode, 0, activityId, requestCharge)
{
    // the header of the wait; the typed header of the client is internal
    private const string RetryAfterHeader = "x-ms-retry-after-ms";

    public override TimeSpan? RetryAfter => retryAfter;

    // The failure of a read, as AnnotationItemStore reports it, with the wait of a throttle.
    public static CosmosRequestFailedException From(ResponseMessage response)
    {
        return new CosmosRequestFailedException(
            $"Cosmos DB request failed: {response.ErrorMessage}",
            response.StatusCode,
            response.Headers.ActivityId,
            response.Headers.RequestCharge,
            response.StatusCode == HttpStatusCode.TooManyRequests ? ReadRetryAfter(response.Headers) : null);
    }

    private static TimeSpan? ReadRetryAfter(Headers headers)
    {
        return double.TryParse(headers.Get(RetryAfterHeader), NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds)
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;
    }
}
