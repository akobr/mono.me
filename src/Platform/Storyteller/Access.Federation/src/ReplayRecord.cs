using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;

namespace _42.Platform.Storyteller;

// What this instance has seen, for replay limits: an own MemoryCache with a size limit (the shared one has none), on the
// injected clock. Entries are never evicted before they expire, so a full record refuses (fails closed) rather than
// forgets what it saw.
internal sealed class ReplayRecord : IDisposable
{
    private readonly MemoryCache _cache;
    private readonly long _sizeLimit;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();

    public ReplayRecord(long sizeLimit, TimeProvider timeProvider)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeLimit);

        _sizeLimit = sizeLimit;
        _timeProvider = timeProvider;
        _cache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = sizeLimit,
            Clock = new TimeProviderClock(timeProvider),
        });
    }

    // For the logs of a refusal: whether it is the limit rather than a key seen before.
    public bool IsFull => _cache.Count >= _sizeLimit;

    // Records a one-time key until it expires. False when it was recorded before, or when the record is full.
    public bool TryAdd(string key, DateTimeOffset expiresAt)
    {
        lock (_lock)
        {
            return !_cache.TryGetValue(key, out _) && TrySet(key, new object(), expiresAt);
        }
    }

    // Counts a use of the key in a fixed window that starts with its first use. False once the window has more than
    // max uses, or when the record is full.
    public bool TryCount(string key, TimeSpan window, int max)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out UseCount? count) && count is not null)
            {
                // Stops counting at the limit, so a flood cannot overflow it.
                return count.Value < max && ++count.Value <= max;
            }

            return TrySet(key, new UseCount { Value = 1 }, _timeProvider.GetUtcNow() + window) && max >= 1;
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
    }

    // MemoryCache does not add an entry beyond its size limit; the read tells. Expired entries still count until a
    // compaction removes them, which a full cache only starts in the background: here it runs at once, then once more.
    private bool TrySet(string key, object value, DateTimeOffset expiresAt)
    {
        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = expiresAt,
            Priority = CacheItemPriority.NeverRemove,
            Size = 1,
        };

        _cache.Set(key, value, options);

        if (_cache.TryGetValue(key, out _))
        {
            return true;
        }

        // Removes the expired entries only: no other is removable.
        _cache.Compact(0);
        _cache.Set(key, value, options);
        return _cache.TryGetValue(key, out _);
    }

    private sealed class UseCount
    {
        public int Value { get; set; }
    }

    private sealed class TimeProviderClock(TimeProvider timeProvider) : ISystemClock
    {
        public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
    }
}
