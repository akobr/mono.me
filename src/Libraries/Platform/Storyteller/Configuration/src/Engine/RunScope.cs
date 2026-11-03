namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// The cancellation of one run: the caller's token, the stop of the engine, and ReadTimeout. ReadTimeout runs on the
// system clock, as the resilience pipeline does.
internal sealed class RunScope : IDisposable
{
    // The longest delay a CancellationTokenSource takes; anything longer is no limit.
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    private readonly CancellationTokenSource _timeout;
    private readonly CancellationTokenSource _linked;
    private readonly CancellationToken _stopping;
    private readonly CancellationToken _caller;

    public RunScope(TimeSpan readTimeout, CancellationToken stopping, CancellationToken caller)
    {
        _timeout = readTimeout == Timeout.InfiniteTimeSpan || readTimeout > MaxTimeout
            ? new CancellationTokenSource()
            : new CancellationTokenSource(readTimeout);
        _linked = CancellationTokenSource.CreateLinkedTokenSource(stopping, caller, _timeout.Token);
        _stopping = stopping;
        _caller = caller;
    }

    public CancellationToken Token => _linked.Token;

    // Only ReadTimeout ended the run: a transient failure. A stop ends a run silently, and the caller's cancellation
    // belongs to the caller.
    public bool IsTimedOut => _timeout.IsCancellationRequested && !_stopping.IsCancellationRequested && !_caller.IsCancellationRequested;

    public void Dispose()
    {
        _linked.Dispose();
        _timeout.Dispose();
    }
}
