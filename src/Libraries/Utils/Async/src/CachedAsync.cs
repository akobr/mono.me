using System;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Utils.Async;

public class CachedAsync<T>
    where T : class
{
    private readonly Func<Task<T>> _factory;
    private readonly TimeSpan _ttl;
    private readonly Action<Exception>? _onRefreshError;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private T? _value;
    private DateTime _expiresAtUtc = DateTime.MinValue;

    public CachedAsync(Func<Task<T>> factory, TimeSpan ttl, Action<Exception>? onRefreshError = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _ttl = ttl;
        _onRefreshError = onRefreshError;
    }

    public async Task<T> GetValueAsync()
    {
        if (_value is not null && DateTime.UtcNow < _expiresAtUtc)
        {
            return _value;
        }

        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            // Double-check after acquiring the lock.
            if (_value is not null && DateTime.UtcNow < _expiresAtUtc)
            {
                return _value;
            }

            try
            {
                var newValue = await _factory().ConfigureAwait(false);
                _value = newValue;
                _expiresAtUtc = DateTime.UtcNow.Add(_ttl);
                return newValue;
            }
            catch (Exception ex) when (_value is not null)
            {
                // On refresh failure, keep the previous value and retry next time.
                _onRefreshError?.Invoke(ex);
                _expiresAtUtc = DateTime.UtcNow; // Retry on next access.
                return _value;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Invalidate()
    {
        _expiresAtUtc = DateTime.MinValue;
    }
}
