using System.Diagnostics;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests.Subscriptions;

// Runs a watch of the subscriptions on a background task and keeps what it yields; events reach it after the loop
// published them, so a test waits for their count.
internal sealed class WatchProbe : IAsyncDisposable
{
    private readonly List<StorytellerSubscriptionChange> _items = [];
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _stop = new();

    public WatchProbe(IStorytellerSubscriptions subscriptions)
    {
        Completion = Task.Run(async () =>
        {
            await foreach (var item in subscriptions.WatchAsync(_stop.Token))
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

    public IReadOnlyList<StorytellerSubscriptionChange> Items
    {
        get
        {
            lock (_lock)
            {
                return [.. _items];
            }
        }
    }

    // The first count items, once that many arrived.
    public async Task<IReadOnlyList<StorytellerSubscriptionChange>> WaitForAsync(int count)
    {
        var waiting = Stopwatch.StartNew();

        while (Items.Count < count)
        {
            if (Completion.IsCompleted)
            {
                await Completion;
                throw new InvalidOperationException($"The watch ended after {Items.Count} of {count} changes.");
            }

            if (waiting.Elapsed > HostingFixture.Patience)
            {
                throw new TimeoutException($"The watch yielded {Items.Count} of {count} changes.");
            }

            await Task.Delay(5);
        }

        return Items.Take(count).ToList();
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
}
