using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests;

// A fake clock that counts its timers: created, alive now, and the most alive at once.
internal sealed class CountingTimeProvider : FakeTimeProvider
{
    private readonly Lock _lock = new();
    private int _created;
    private int _active;
    private int _maxActive;

    public CountingTimeProvider()
        : this(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero))
    {
    }

    public CountingTimeProvider(DateTimeOffset start)
        : base(start)
    {
    }

    public int Created
    {
        get
        {
            lock (_lock)
            {
                return _created;
            }
        }
    }

    public int Active
    {
        get
        {
            lock (_lock)
            {
                return _active;
            }
        }
    }

    public int MaxActive
    {
        get
        {
            lock (_lock)
            {
                return _maxActive;
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);

        lock (_lock)
        {
            _created++;
            _active++;
            _maxActive = Math.Max(_maxActive, _active);
        }

        return new CountedTimer(this, timer);
    }

    private void Release()
    {
        lock (_lock)
        {
            _active--;
        }
    }

    private sealed class CountedTimer : ITimer
    {
        private readonly CountingTimeProvider _owner;
        private readonly ITimer _inner;
        private int _isDisposed;

        public CountedTimer(CountingTimeProvider owner, ITimer inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            return _inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _owner.Release();
            }

            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
