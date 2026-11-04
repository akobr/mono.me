namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// The seam between the informer and the way it learns of changes. The polling source lists and diffs on an interval; a
// watch of change events (424-49) or a bulk read (C4) can take its place without a change of the public types.
internal interface ISubscriptionChangeSource
{
    // Batches of subscription changes, each with the keys whose documents may have changed. The polling source yields one
    // batch per poll and marks every held key dirty; a watch source would yield a batch per event and mark only the
    // keys an event names. Each enumeration starts from nothing held. Ends with OperationCanceledException when the
    // token is cancelled.
    IAsyncEnumerable<SubscriptionBatch> ReadAsync(SubscriptionScope scope, CancellationToken cancellationToken);
}
