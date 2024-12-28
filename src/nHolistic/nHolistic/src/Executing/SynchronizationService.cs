using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace _42.tHolistic;

public class SynchronizationService : ISynchronizationService
{
    private ConcurrentDictionary<string, WeakReference> _locks = new();

    public object GetOrCreateLock(string synchronizationKey)
    {
        return _locks.GetOrAdd(synchronizationKey, _ => new WeakReference(new object())).Target ?? new object();
    }

    public bool TryGetLock(string synchronizationKey, [MaybeNullWhen(false)] out object lockObject)
    {
        var result = _locks.TryGetValue(synchronizationKey, out var reference);
        lockObject = reference?.Target;
        return result;
    }

    public bool IsLocked(string synchronizationKey)
    {
        _locks.TryGetValue(synchronizationKey, out var reference);
        return reference is { IsAlive: true };
    }

    public void CleanUp()
    {
        foreach (var key in _locks.Keys)
        {
            if (!_locks[key].IsAlive)
            {
                _locks.TryRemove(key, out _);
            }
        }
    }
}
