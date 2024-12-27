using System.Diagnostics.CodeAnalysis;

namespace _42.nHolistic;

public interface ISynchronizationService
{
    object GetOrCreateLock(string synchronizationKey);

    bool TryGetLock(string synchronizationKey, [MaybeNullWhen(false)]out object lockObject);

    bool IsLocked(string synchronizationKey);

    void CleanUp();
}
