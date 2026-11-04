using System.Diagnostics;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Informer;

// Runs an enumeration of the informer on a background task and keeps what it yields. A poll is over when the loop armed
// the timer of the next one, after everything of the poll was taken, so the clock is advanced only then. The loop must
// be the only one that makes timers of the clock.
internal sealed class PollProbe<T> : IAsyncDisposable
{
    private readonly CountingTimeProvider _time;
    private readonly List<T> _items = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();
    private int _taken;

    public PollProbe(IAsyncEnumerable<T> items, CountingTimeProvider time)
    {
        _time = time;
        Completion = Task.Run(async () =>
        {
            await foreach (var item in items.WithCancellation(_stop.Token))
            {
                lock (_lock)
                {
                    _items.Add(item);
                }
            }
        });
    }

    // Faults or cancels as the enumeration does.
    public Task Completion { get; }

    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }

    // What the first poll yielded.
    public async Task<IReadOnlyList<T>> FirstPollAsync()
    {
        await WaitAsync(() => _time.Created >= 1);
        return Take();
    }

    // Advances the clock and returns what the poll it started yielded.
    public async Task<IReadOnlyList<T>> NextPollAsync(TimeSpan advance)
    {
        var timers = _time.Created;
        _time.Advance(advance);
        await WaitAsync(() => _time.Created > timers);
        return Take();
    }

    public void Stop()
    {
        _stop.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();

        try
        {
            await Completion.WaitAsync(HostingFixture.Patience);
        }
        catch (Exception)
        {
            // The test asserts on Completion when it cares.
        }

        _stop.Dispose();
    }

    // A loop that ended unexpectedly fails the wait with its exception.
    private async Task WaitAsync(Func<bool> isOver)
    {
        var waiting = Stopwatch.StartNew();

        while (!isOver() && !Completion.IsCompleted)
        {
            if (waiting.Elapsed > HostingFixture.Patience)
            {
                throw new TimeoutException("The poll did not end.");
            }

            await Task.Delay(5);
        }

        if (Completion.IsFaulted)
        {
            await Completion;
        }
    }

    private IReadOnlyList<T> Take()
    {
        lock (_lock)
        {
            var items = _items.Skip(_taken).ToList();
            _taken = _items.Count;
            return items;
        }
    }
}
