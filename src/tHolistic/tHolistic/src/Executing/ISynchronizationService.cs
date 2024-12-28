using System.Diagnostics.CodeAnalysis;

namespace _42.tHolistic;

public interface ISynchronizationService
{
    object GetOrCreateLock(string synchronizationKey);

    bool TryGetLock(string synchronizationKey, [MaybeNullWhen(false)]out object lockObject);

    bool IsLocked(string synchronizationKey);

    void CleanUp();
}
