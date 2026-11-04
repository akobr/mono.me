using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

// The batches of another source until Break(); the next batch after it fails as a bug would, with an exception that is
// not a read's.
internal sealed class BreakingSource : ISubscriptionChangeSource
{
    public const string BugMessage = "A bug with S3cr3t in its message.";

    private readonly ISubscriptionChangeSource _inner;
    private int _isBroken;

    public BreakingSource(ISubscriptionChangeSource inner)
    {
        _inner = inner;
    }

    public void Break()
    {
        Volatile.Write(ref _isBroken, 1);
    }

    public async IAsyncEnumerable<SubscriptionBatch> ReadAsync(SubscriptionScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in _inner.ReadAsync(scope, cancellationToken))
        {
            if (Volatile.Read(ref _isBroken) != 0)
            {
                throw new InvalidOperationException(BugMessage);
            }

            yield return batch;
        }
    }
}
