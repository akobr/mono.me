namespace _42.nHolistic;

[AttributeUsage(AttributeTargets.Method)]
sealed class SynchronizedAttribute(string synchronizationKey) : Attribute
{
    public string SynchronizationKey { get; } = synchronizationKey;
}
