using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring;

// One configuration root's view of a Storyteller source. The state lives in the engine; the provider holds a copy of
// the snapshot it published last, so a Set on one root never leaks into another. Attached to the engine from its
// construction, at Build(), until it is disposed with its root.
public sealed class StorytellerConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly Lock _lock = new();
    private long _publishedSequence;
    private IReadOnlyDictionary<string, string>? _publishedData;
    private int _isDisposed;

    internal StorytellerConfigurationProvider(StorytellerConfigurationEngine engine)
    {
        Engine = engine;
        engine.Attach(this);
    }

    internal StorytellerConfigurationEngine Engine { get; }

    // The first load of the engine reads the platform, synchronously and within ReadTimeout; every later one publishes
    // the current snapshot without a request.
    public override void Load()
    {
        try
        {
            Engine.Load(this);
        }
        catch
        {
            // Only a failed startup throws, and neither ConfigurationRoot nor ConfigurationManager keeps or disposes a
            // provider whose Load() threw: attached, it would keep the engine polling after the last root is gone.
            Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            Engine.Detach(this);
        }
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerConfigurationProvider)} for '{Engine.ActiveKey}' ({Engine.Settings.Mode})";
    }

    // A snapshot that is not newer than the last one published is skipped, so a late or concurrent notification never
    // puts an older document back. One that changed only its metadata shares the data: nothing to reload. Returns
    // whether Data changed; the engine calls Reload() after, outside this lock.
    internal bool Publish(ConfigurationSnapshot snapshot)
    {
        lock (_lock)
        {
            if (snapshot.Sequence <= _publishedSequence)
            {
                return false;
            }

            _publishedSequence = snapshot.Sequence;

            if (ReferenceEquals(snapshot.Data, _publishedData))
            {
                return false;
            }

            _publishedData = snapshot.Data;
            Data = snapshot.Data.ToDictionary(pair => pair.Key, pair => (string?)pair.Value, StringComparer.OrdinalIgnoreCase);
            return true;
        }
    }

    // Raises the reload token: IOptionsMonitor rebinds on this thread, and what a rebind throws comes out here.
    internal void Reload()
    {
        OnReload();
    }
}
