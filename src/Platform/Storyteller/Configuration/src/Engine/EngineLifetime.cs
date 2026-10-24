using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// One start of an engine, from the first attach to the last detach: the epoch. It owns the transport and the timer of
// that start. A run captures its lifetime and swaps, notifies, writes the cache and re-arms the timer only while that
// lifetime is still the engine's current one. Timer, Runs and MarkStopped are guarded by the engine's lock.
internal sealed class EngineLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private volatile bool _isStopped;

    public EngineLifetime(StorytellerTransport transport, ILogger logger)
    {
        Transport = transport;
        Reader = new DocumentReader(transport.Client, logger);
        Stopping = _stopping.Token;
    }

    public StorytellerTransport Transport { get; }

    public DocumentReader Reader { get; }

    // Captured once: the source is disposed with the transport, after the last run of the lifetime ended.
    public CancellationToken Stopping { get; }

    // Set under the engine's lock before the runs are cancelled, so a run that fails because of the stop sees it.
    public bool IsStopped => _isStopped;

    public ITimer? Timer { get; set; }

    // Runs that began on this lifetime and have not ended; the transport is disposed when the last one ends.
    public int Runs { get; set; }

    public void MarkStopped()
    {
        _isStopped = true;
    }

    // Outside the engine's lock: cancellation runs callbacks.
    public void CancelRuns()
    {
        _stopping.Cancel();
    }

    public void Dispose()
    {
        Transport.Dispose();
        _stopping.Dispose();
    }
}
